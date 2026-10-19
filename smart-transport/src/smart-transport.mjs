/**
 * TaySon Smart Transport v0.1 — module for the existing Cloudflare Worker.
 * Direct mode is fail-closed and OFF by default. It does not download PKGs.
 * Preserve legacy /api/*-routes responses and the BGFT installer unchanged.
 */
export async function smartTransportDecision(route, env = {}, options = {}) {
  if (env.SMART_DIRECT_ENABLED !== 'true') return { mode: 'proxy', reason: 'disabled' };
  if (env.SMART_BGFT_REDIRECT_VERIFIED !== 'true') return { mode: 'proxy', reason: 'bgft_not_verified' };
  if (!route || !['r2', 'viking'].includes(route.kind))
    return { mode: 'proxy', reason: 'provider_not_approved' };
  // Require an explicit allowlist: verification for one provider must not enable another.
  const enabledProviders = String(env.SMART_DIRECT_PROVIDERS || '')
    .split(',').map(value => value.trim().toLowerCase()).filter(Boolean);
  if (!enabledProviders.includes(route.kind))
    return { mode: 'proxy', reason: 'provider_not_enabled' };
  if (route.cookie || route.referer || route.userAgent)
    return { mode: 'proxy', reason: 'provider_requires_context' };

  let upstream;
  try { upstream = new URL(route.url); }
  catch (_) { return { mode: 'proxy', reason: 'invalid_url' }; }
  if (upstream.protocol !== 'https:' || upstream.port || upstream.username || upstream.password || upstream.hash ||
      !/^(?:vikingfile|rootz-files)\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i.test(upstream.hostname))
    return { mode: 'proxy', reason: 'unapproved_host' };
  const signed = upstream.searchParams;
  if (signed.get('X-Amz-Algorithm') !== 'AWS4-HMAC-SHA256' ||
      !signed.get('X-Amz-Signature') || !signed.get('X-Amz-Date') || !signed.get('X-Amz-Expires'))
    return { mode: 'proxy', reason: 'missing_signature' };
  const date = /^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z$/.exec(signed.get('X-Amz-Date'));
  const ttl = Number(signed.get('X-Amz-Expires'));
  if (!date || !Number.isInteger(ttl) || ttl <= 0 || ttl > 604800)
    return { mode: 'proxy', reason: 'invalid_expiry' };
  const signedAt = Date.UTC(+date[1], +date[2] - 1, +date[3], +date[4], +date[5], +date[6]);
  if (!Number.isFinite(signedAt) || new Date(signedAt).toISOString().slice(0, 19).replace(/[-:]/g, '') + 'Z' !== signed.get('X-Amz-Date'))
    return { mode: 'proxy', reason: 'invalid_signed_date' };
  const now = options.now ? options.now() : Date.now();
  const remainingSec = Math.floor((signedAt + ttl * 1000 - now) / 1000);
  if (remainingSec < 3600) return { mode: 'proxy', reason: 'insufficient_expiry' };

  const requestFetch = options.fetch || fetch;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), options.timeoutMs || 8000);
  let response;
  try {
    response = await requestFetch(upstream.href, {
      method: 'GET', redirect: 'manual',
      headers: { 'Range': 'bytes=0-3', 'Accept-Encoding': 'identity' },
      signal: controller.signal
    });
    if (response.status !== 206) return { mode: 'proxy', reason: 'range_not_supported' };
    if (/text\/html|application\/(?:json|xml)|text\/xml/i.test(response.headers.get('content-type') || '') ||
        !['', 'identity'].includes((response.headers.get('content-encoding') || '').toLowerCase()))
      return { mode: 'proxy', reason: 'invalid_representation' };
    const match = /^bytes 0-3\/(\d+)$/i.exec(response.headers.get('content-range') || '');
    if (!match || response.headers.get('content-length') !== '4')
      return { mode: 'proxy', reason: 'incorrect_content_range' };
    const total = BigInt(match[1]);
    if (total < 4n) return { mode: 'proxy', reason: 'invalid_size' };
    // Budget assumes a deliberately conservative minimum of 512 KiB/s + 30 minutes.
    // This is a policy threshold, not a prediction of the actual BGFT speed.
    const predicted = (total + 524287n) / 524288n + 1800n;
    if (BigInt(remainingSec) < predicted) return { mode: 'proxy', reason: 'insufficient_expiry_for_size' };
    if (route.routeExpiresAt && (BigInt(Math.floor((route.routeExpiresAt - now) / 1000)) < predicted))
      return { mode: 'proxy', reason: 'route_expires_too_soon' };
    const reader = response.body?.getReader();
    if (!reader) return { mode: 'proxy', reason: 'empty_body' };
    const signature = new Uint8Array(4);
    let count = 0;
    try {
      while (count < 4) {
        const { value, done } = await reader.read();
        if (done) break;
        if (value.byteLength > 4 - count) return { mode: 'proxy', reason: 'oversized_probe_body' };
        signature.set(value, count);
        count += value.byteLength;
      }
    } finally { try { await reader.cancel(); } catch (_) {} try { reader.releaseLock(); } catch (_) {} }
    if (count !== 4 || !signature.every((byte, idx) => byte === [0x7f, 0x43, 0x4e, 0x54][idx]))
      return { mode: 'proxy', reason: 'invalid_pkg_magic' };
    return { mode: 'direct', reason: 'eligible', totalBytes: total.toString(), remainingSec };
  } catch (_) {
    return { mode: 'proxy', reason: 'probe_failed' };
  } finally {
    clearTimeout(timeout);
    if (response?.body && !response.body.locked) {
      try { await response.body.cancel(); } catch (_) {}
    }
  }
}

/** Returns a redirect only when explicitly enabled and compatible; otherwise null. */
export async function smartTransportMaybeRedirect(request, route, env, options = {}) {
  // Keep HEAD on Worker metadata path; GET Range can work even if origin HEAD fails.
  if (request.method !== 'GET') return null;
  const decision = await smartTransportDecision(route, env, options);
  if (decision.mode !== 'direct') return null;
  return new Response(null, {
    status: 307,
    headers: {
      Location: route.url,
      'Cache-Control': 'no-store',
      'X-TaySon-Transport': 'DIRECT',
      'Referrer-Policy': 'no-referrer'
    }
  });
}
