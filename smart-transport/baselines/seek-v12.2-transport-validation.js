const VERSION = "v12.2-transport-validation";
const UPSTREAM_HEADERS_TIMEOUT_MS = 15000;
const PREFIX_TIMEOUT_MS = 15000;
const MAX_ROUTE_BODY = 32 * 1024;
const MAX_UPSTREAM_REDIRECTS = 5;
const MAX_RANGE_HEADER = 96;
const MIN_REMAINING_SECONDS = 90;
const MAX_ROUTE_TTL_SECONDS = 7 * 24 * 60 * 60;
const R2_HOST = /^(?:vikingfile|rootz-files)\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i;
const AKIRA_DOWNLOAD_HOST = /^(?:[a-z0-9-]+\.)*akirabox\.(?:com|to)$/i;
const AKIRA_REFERRER_HOST = /^(?:www\.)?akirabox\.(?:to|com)$/i;
const MOCHA_HOST = /(?:^|\.)mocha\.my$/i;
const FILEK_HOST = /(?:^|\.)(?:filekeeper\.net|dlproxy\.uk)$/i;
const DEFAULT_AKIRA_UA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Mobile Safari/537.36";

function decodeBase64Url(value) {
  if (typeof value !== "string" || value.length > 24000 ||
      !/^[A-Za-z0-9_-]+$/.test(value)) throw safeError("invalid encoded route", 400);
  try {
    let b64 = value.replace(/-/g, "+").replace(/_/g, "/");
    while (b64.length % 4) b64 += "=";
    const binary = atob(b64);
    const bytes = Uint8Array.from(binary, c => c.charCodeAt(0));
    return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  } catch (_) {
    throw safeError("invalid encoded route", 400);
  }
}

function encodeBase64Url(value) {
  const bytes = new TextEncoder().encode(value);
  let binary = "";
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function parseAmzDate(value) {
  const m = /^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z$/.exec(value || "");
  if (!m) return NaN;
  const stamp = Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]);
  if (!Number.isFinite(stamp) || new Date(stamp).toISOString().replace(/[-:]/g, "").slice(0, 15) + "Z" !== value) return NaN;
  return stamp;
}

function safeText(value, max) {
  value = String(value || "");
  if (value.length > max || /[\r\n\0]/.test(value)) throw new Error("invalid request context");
  return value;
}

function validateR2(raw, minimumRemaining = MIN_REMAINING_SECONDS) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:") throw new Error("HTTPS required");
  if (!R2_HOST.test(target.hostname)) throw new Error("not an approved VikingFile/Rootz R2 host");
  if (target.username || target.password || target.hash || target.port) throw new Error("userinfo/fragment not allowed");
  if (target.searchParams.get("X-Amz-Algorithm") !== "AWS4-HMAC-SHA256")
    throw new Error("invalid signing algorithm");
  if (!target.searchParams.get("X-Amz-Signature") ||
      !target.searchParams.get("X-Amz-Date") ||
      !target.searchParams.get("X-Amz-Expires"))
    throw new Error("missing R2 presign fields");
  const started = parseAmzDate(target.searchParams.get("X-Amz-Date"));
  const ttl = Number(target.searchParams.get("X-Amz-Expires"));
  if (!Number.isFinite(started) || !Number.isInteger(ttl) || ttl <= 0 || ttl > MAX_ROUTE_TTL_SECONDS)
    throw new Error("invalid R2 expiry");
  const remaining = Math.floor((started + ttl * 1000 - Date.now()) / 1000);
  if (remaining < minimumRemaining) throw new Error("R2 URL expired or near expiry");
  return { kind: "r2", url: raw, remaining, providerExpiresAt: started + ttl * 1000 };
}

function validateAkira(raw, cookie = "", referer = "https://akirabox.to/", userAgent = DEFAULT_AKIRA_UA, minimumRemaining = MIN_REMAINING_SECONDS) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid AkiraBox URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:" || !AKIRA_DOWNLOAD_HOST.test(target.hostname))
    throw new Error("not an approved AkiraBox download host");
  if (target.username || target.password || target.hash || target.port) throw new Error("userinfo/fragment not allowed");
  if (!target.pathname.toLowerCase().endsWith(".pkg")) throw new Error("AkiraBox target is not a PKG");
  const access = target.searchParams.get("access") || "";
  const m = /^(\d{13})\.([A-Za-z0-9_-]{16,})$/.exec(access);
  if (!m) throw new Error("missing or malformed AkiraBox access token");
  const expiresAt = Number(m[1]);
  const remaining = Math.floor((expiresAt - Date.now()) / 1000);
  if (!Number.isFinite(expiresAt) || remaining < minimumRemaining)
    throw new Error("AkiraBox URL expired or near expiry");
  cookie = safeText(cookie, 8192);
  userAgent = safeText(userAgent || DEFAULT_AKIRA_UA, 512);
  referer = safeText(referer || "https://akirabox.to/", 2048);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || ref.port || !AKIRA_REFERRER_HOST.test(ref.hostname))
    throw new Error("invalid AkiraBox referer");
  return {
    kind: "akira",
    url: raw,
    cookie,
    referer,
    userAgent,
    remaining: Math.min(remaining, 24 * 60 * 60),
    providerExpiresAt: expiresAt
  };
}

function validateMocha(raw, referer = "https://mocha.my/", userAgent = DEFAULT_AKIRA_UA, minimumRemaining = MIN_REMAINING_SECONDS) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid Mocha URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:" || !MOCHA_HOST.test(target.hostname))
    throw new Error("not an approved Mocha download host");
  if (target.username || target.password || target.hash || target.port)
    throw new Error("userinfo/fragment not allowed");
  const expires = target.searchParams.get("expires") || "";
  let remaining = 24 * 60 * 60;
  if (expires) {
    const unix = Number(expires);
    if (!Number.isInteger(unix) || unix <= 0)
      throw new Error("invalid Mocha expiry");
    remaining = Math.floor(unix - Date.now() / 1000);
    if (remaining < minimumRemaining)
      throw new Error("Mocha URL expired or near expiry");
    remaining = Math.min(remaining, 24 * 60 * 60);
  }

  referer = safeText(referer || "https://mocha.my/", 2048);
  userAgent = safeText(userAgent || DEFAULT_AKIRA_UA, 512);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || ref.port || !MOCHA_HOST.test(ref.hostname))
    throw new Error("invalid Mocha referer");

  return { kind: "mocha", url: raw, referer, userAgent, remaining,
    providerExpiresAt: expires ? Number(expires) * 1000 : null };
}

function validateFileK(raw, referer = "https://filekeeper.net/", userAgent = DEFAULT_AKIRA_UA) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid FileKeeper URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:" || !FILEK_HOST.test(target.hostname))
    throw new Error("not an approved FileKeeper download host");
  if (target.username || target.password || target.hash || target.port)
    throw new Error("userinfo/fragment not allowed");
  referer = safeText(referer || "https://filekeeper.net/", 2048);
  userAgent = safeText(userAgent || DEFAULT_AKIRA_UA, 512);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || ref.port || !FILEK_HOST.test(ref.hostname))
    throw new Error("invalid FileKeeper referer");

  return {
    kind: "filek",
    url: raw,
    referer,
    userAgent,
    remaining: 24 * 60 * 60
  };
}

function validateViking(raw, minimumRemaining = MIN_REMAINING_SECONDS) {
  const route = validateR2(raw, minimumRemaining);
  const host = new URL(raw).hostname;
  if (!/^vikingfile\./i.test(host))
    throw new Error("not a VikingFile R2 URL");
  route.kind = "viking";
  return route;
}

function pkgHeaders() {
  return new Headers({
    "Content-Type": "application/octet-stream",
    "Content-Disposition": 'attachment; filename="download.pkg"',
    "Cache-Control": "no-store",
    "X-Tayson-Worker-Version": VERSION
  });
}

function safeError(message, status = 502) {
  return Object.assign(new Error(message), { status });
}

// Deadlines cover headers or the small inspected prefix, never the whole PKG.
async function beforeDeadline(operation, deadline, onTimeout) {
  let timer;
  try {
    return await Promise.race([
      operation(),
      new Promise((_, reject) => {
        timer = setTimeout(() => {
          reject(safeError("upstream timeout", 504));
          onTimeout?.();
        }, Math.max(0, deadline - Date.now()));
      })
    ]);
  } finally { clearTimeout(timer); }
}

function responseLength(response) {
  const raw = response.headers.get("content-length");
  if (raw === null) return null;
  const length = Number(raw);
  if (!/^\d+$/.test(raw) || !Number.isSafeInteger(length))
    throw safeError("invalid upstream Content-Length");
  return length;
}

function validateRepresentation(response, requestedRange, ifRange, allowFull = false) {
  if (isWrongContentType(response.headers.get("content-type")) || !identityEncoding(response))
    throw safeError("upstream supplied invalid content type or encoding");
  responseLength(response);
  if (!(allowFull && response.status === 200))
    validateUpstreamRange(response, requestedRange, ifRange);
}

async function readBoundedBody(request) {
  const declared = Number(request.headers.get("content-length") || 0);
  if (declared > MAX_ROUTE_BODY) throw safeError("request body too large", 413);
  const reader = request.body?.getReader();
  if (!reader) return "";
  const chunks = [];
  let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      size += value.byteLength;
      if (size > MAX_ROUTE_BODY) throw safeError("request body too large", 413);
      chunks.push(value);
    }
  } finally {
    try { await reader.cancel(); } catch (_) {}
    reader.releaseLock();
  }
  const body = new Uint8Array(size);
  let pos = 0;
  for (const chunk of chunks) { body.set(chunk, pos); pos += chunk.length; }
  return new TextDecoder("utf-8", { fatal: true }).decode(body);
}

function requireRouteAuth(request, env) {
  // Opt-in: enabling this on the existing 3.4.3 app may require a matching
  // supported client configuration. Keep it disabled until migration tested.
  if (!env.ROUTE_API_KEY) return null;
  if (request.headers.get("x-api-key") === env.ROUTE_API_KEY) return null;
  return new Response("Unauthorized\n", {
    status: 401, headers: { "Cache-Control": "no-store" }
  });
}

function isWrongContentType(value) {
  const type = (value || "").split(";")[0].trim().toLowerCase();
  return type === "text/html" || type === "application/json" ||
    type === "application/problem+json" || type === "application/xml" ||
    type === "text/xml" || type === "application/xhtml+xml";
}

function identityEncoding(response) {
  const encoding = (response.headers.get("content-encoding") || "identity").trim().toLowerCase();
  return encoding === "identity";
}

function hasPkgMagic(bytes) {
  return bytes.length >= 4 && bytes[0] === 0x7f && bytes[1] === 0x43 &&
    bytes[2] === 0x4e && bytes[3] === 0x54;
}

// Validate only the first few bytes; stream the remainder with backpressure.
// Never buffer a PKG, never consume the upstream twice, and propagate cancellation.
async function guardPkgPrefix(response, requestedRange) {
  const matchesStart = !requestedRange || /^bytes=0-(?:[3-9]|[1-9][0-9]+|)$/i.test(requestedRange.trim());
  if (!matchesStart || !response.body) return response.body;
  const lengthText = response.headers.get("content-length");
  // An intentional 0-0 probe must not be rejected as an invalid PKG.
  if (lengthText !== null && Number(lengthText) < 4) {
    if (requestedRange && /^bytes=0-[012]$/i.test(requestedRange.trim())) return response.body;
    try { await response.body.cancel(); } catch (_) {}
    throw safeError("PKG shorter than minimum signature");
  }
  const reader = response.body.getReader();
  const deadline = Date.now() + PREFIX_TIMEOUT_MS;
  const prefixChunks = [];
  let length = 0;
  try {
    while (length < 4) {
      const next = await beforeDeadline(() => reader.read(), deadline,
        () => { void reader.cancel().catch(() => {}); });
      if (next.done) break;
      if (next.value.byteLength === 0) continue;
      prefixChunks.push(next.value);
      length += next.value.byteLength;
    }
    const first = new Uint8Array(Math.min(length, 4));
    let offset = 0;
    for (const chunk of prefixChunks) {
      const n = Math.min(first.length - offset, chunk.length);
      first.set(chunk.subarray(0, n), offset);
      offset += n;
      if (offset === first.length) break;
    }
    if (!hasPkgMagic(first)) throw safeError("PKG header signature invalid");
    let index = 0;
    return new ReadableStream({
      async pull(controller) {
        if (index < prefixChunks.length) {
          controller.enqueue(prefixChunks[index++]);
          return;
        }
        try {
          const part = await reader.read();
          if (part.done) { reader.releaseLock(); controller.close(); }
          else controller.enqueue(part.value);
        } catch (e) {
          try { await reader.cancel(); } catch (_) {}
          try { reader.releaseLock(); } catch (_) {}
          controller.error(e);
        }
      },
      async cancel(reason) { try { await reader.cancel(reason); } finally { try { reader.releaseLock(); } catch (_) {} } }
    });
  } catch (e) {
    try { await reader.cancel(); } catch (_) {}
    try { reader.releaseLock(); } catch (_) {}
    throw e;
  }
}

// Supported transport profile: single byte intervals; console capture still needed.
// The parser accepts open-ended ranges, but intentionally rejects suffix ranges.
function parseClientRange(value) {
  if (value === null) return null;
  if (value.length > MAX_RANGE_HEADER) throw safeError("unsupported byte range", 416);
  const match = /^bytes=(\d+)-(\d*)$/i.exec(value.trim());
  if (!match) throw safeError("unsupported byte range", 416);
  const first = Number(match[1]);
  const last = match[2] === "" ? null : Number(match[2]);
  if (!Number.isSafeInteger(first) || first < 0 ||
      (last !== null && (!Number.isSafeInteger(last) || last < first)))
    throw safeError("invalid byte range", 416);
  return { first, last, header: `bytes=${first}-${last === null ? "" : last}` };
}

function parseResponseRange(header) {
  const match = /^bytes (\d+)-(\d+)\/(\d+|\*)$/i.exec((header || "").trim());
  if (!match) return null;
  const first = Number(match[1]);
  const last = Number(match[2]);
  const total = match[3] === "*" ? null : Number(match[3]);
  if (![first, last].every(Number.isSafeInteger) || first > last ||
      (total !== null && (!Number.isSafeInteger(total) || last >= total))) return null;
  return { first, last, total };
}

function validateUpstreamRange(response, requestedRange, ifRange) {
  if (response.status === 206) {
    const part = parseResponseRange(response.headers.get("content-range"));
    if (!part || !requestedRange)
      throw safeError("upstream 206 has missing or invalid Content-Range");
    if (part.first !== requestedRange.first ||
        (requestedRange.last !== null && part.last > requestedRange.last))
      throw safeError("upstream returned a different byte range");
    const lengthHeader = response.headers.get("content-length");
    if (lengthHeader !== null &&
        (!/^\d+$/.test(lengthHeader) || Number(lengthHeader) !== part.last - part.first + 1))
      throw safeError("upstream Content-Length does not match Content-Range");
  }
  // If-Range mismatch allows a normal 200 containing the entire representation.
  if (response.status === 200 && requestedRange && !ifRange)
    throw safeError("upstream ignored requested Range");
}

function upstreamHeaders(route, range, ifRange) {
  const h = new Headers();
  h.set("Accept-Encoding", "identity");
  if (range) h.set("Range", range);
  if (ifRange) h.set("If-Range", ifRange);
  if (route.kind === "akira") {
    h.set("Accept", "*/*");
    h.set("Accept-Encoding", "identity");
    h.set("User-Agent", route.userAgent || DEFAULT_AKIRA_UA);
    h.set("Referer", route.referer || "https://akirabox.to/");
    if (route.cookie) h.set("Cookie", route.cookie);
  } else if (route.kind === "mocha") {
    h.set("Accept", "*/*");
    h.set("Accept-Encoding", "identity");
    h.set("User-Agent", route.userAgent || DEFAULT_AKIRA_UA);
    h.set("Referer", route.referer || "https://mocha.my/");
  } else if (route.kind === "filek") {
    // Match the app's successful validation path as closely as possible:
    // FileKeeper direct downloads are probed without Referer/User-Agent.
    h.set("Accept-Encoding", "identity");
  }
  return h;
}

function allowedUpstreamHost(route, hostname) {
  if (route.kind === "r2" || route.kind === "viking") return R2_HOST.test(hostname);
  if (route.kind === "akira") return AKIRA_DOWNLOAD_HOST.test(hostname);
  if (route.kind === "mocha") return MOCHA_HOST.test(hostname);
  if (route.kind === "filek") return FILEK_HOST.test(hostname);
  return false;
}

async function fetchUpstream(route, range, ifRange) {
  let destination = new URL(route.url);
  const initialHost = destination.hostname;
  const deadline = Date.now() + UPSTREAM_HEADERS_TIMEOUT_MS;
  for (let hop = 0; hop <= MAX_UPSTREAM_REDIRECTS; hop++) {
    if (destination.protocol !== "https:" || destination.port || destination.username || destination.password ||
        !allowedUpstreamHost(route, destination.hostname)) {
      throw safeError("upstream redirect target not approved");
    }
    // Never forward a browser cookie to another host, even when the hostname
    // passes the approved family whitelist. Other context is non-secret.
    const context = (destination.hostname === initialHost) ? route : { ...route, cookie: "" };
    const abort = new AbortController();
    const result = await beforeDeadline(() => fetch(destination.href, {
      method: "GET", headers: upstreamHeaders(context, range, ifRange),
      redirect: "manual", signal: abort.signal
    }), deadline, () => abort.abort());
    if (route.transportTrace) Object.assign(route.transportTrace, {
      upstream_status: result.status, redirect_count: hop,
      returned_range: parseResponseRange(result.headers.get("content-range")),
      announced_length: /^\d+$/.test(result.headers.get("content-length") || "") ?
        Number(result.headers.get("content-length")) : null
    });
    if (![301, 302, 303, 307, 308].includes(result.status)) return result;
    const location = result.headers.get("location");
    try { await result.body?.cancel(); } catch (_) {}
    if (!location || hop === MAX_UPSTREAM_REDIRECTS) throw safeError("upstream redirect limit exceeded");
    destination = new URL(location, destination);
  }
  throw safeError("upstream redirect limit exceeded");
}

async function probeSize(route) {
  const range = parseClientRange("bytes=0-3");
  const r = await fetchUpstream(route, range.header, null);
  if (r.status !== 200 && r.status !== 206) {
    const status = r.status;
    try { await r.body?.cancel(); } catch (_) {}
    return { ok: false, status };
  }
  try {
    validateRepresentation(r, range, null, true);
    const part = r.status === 206 ? parseResponseRange(r.headers.get("content-range")) : null;
    if (r.status === 206 && (!part || part.first !== 0 || part.last !== 3 || part.total === null))
      return { ok: false, status: 502 };
    let size = part?.total || null;
    if (r.status === 200) {
      const n = responseLength(r);
      if (Number.isSafeInteger(n) && n > 0) size = n;
    }
    if (!Number.isSafeInteger(size) || size < 4) return { ok: false, status: 502 };
    const checkedBody = await guardPkgPrefix(r, r.status === 206 ? range.header : null);
    if (!checkedBody) return { ok: false, status: 502 };
    await checkedBody.cancel();
    return {
      ok: Number.isSafeInteger(size) && size > 0,
      status: 502,
      size,
      supportsRanges: r.status === 206,
      contentType: r.headers.get("content-type"),
      etag: r.headers.get("etag"),
      lastModified: r.headers.get("last-modified")
    };
  } catch (e) {
    if (route.transportTrace) route.transportTrace.rejection = e.message;
    return { ok: false, status: e.status || 502 };
  } finally {
    try { await r.body?.cancel(); } catch (_) {}
  }
}

function fallbackPermitted(env, url) {
  // Legacy app compatibility defaults to ON. Opt in to fail-closed short routes.
  if (env.DISABLE_PUBLIC_FALLBACK === "true") return false;
  return encodeBase64Url(url).length <= 24000;
}

async function putRoute(env, route, origin, prefix) {
  const useDurable = env.ROUTE_STORE_MODE === "durable";
  if (!useDurable && (!env.ROUTES || typeof env.ROUTES.put !== "function")) return null;
  const token = crypto.randomUUID().replace(/-/g, "");
  const ttl = Math.max(60, Math.min(route.remaining - 30, MAX_ROUTE_TTL_SECONDS));
  route.routeExpiresAt = Date.now() + ttl * 1000;
  if (env.ROUTE_STORE_MODE === "durable") {
    await putDurableRoute(env, token, route, ttl);
  } else {
    await env.ROUTES.put(token, JSON.stringify(route), { expirationTtl: ttl });
  }
  return `${origin}/${prefix}/${token}.pkg`;
}

async function createR2Route(request, env, origin) {
  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_ROUTE_BODY) return new Response("URL too large\n", { status: 413 });
  const raw = (await readBoundedBody(request)).trim();
  let route;
  try { route = validateR2(raw); }
  catch (e) { return new Response("Invalid R2 URL: " + e.message + "\n", { status: 400 }); }
  const short = await putRoute(env, route, origin, "d");
  if (short) return new Response(short + "\n", { status: 200, headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" } });
  if (!fallbackPermitted(env, route.url)) return new Response("Route storage required\n", { status: 503 });
  return new Response(`${origin}/r2.pkg?u=${encodeBase64Url(route.url)}\n`, {
    status: 200,
    headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }
  });
}

async function createAkiraRoute(request, env, origin) {
  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_ROUTE_BODY) return new Response("Context too large\n", { status: 413 });
  const form = new URLSearchParams(await readBoundedBody(request));
  let route;
  try {
    route = validateAkira(
      form.get("url") || "",
      form.get("cookie") || "",
      form.get("referer") || "https://akirabox.to/",
      form.get("userAgent") || DEFAULT_AKIRA_UA
    );
  } catch (e) {
    return new Response("Invalid AkiraBox route: " + e.message + "\n", { status: 400 });
  }
  const short = await putRoute(env, route, origin, "a");
  if (short) return new Response(short + "\n", { status: 200, headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" } }) --- TRUNCATED --- 46,724 chars