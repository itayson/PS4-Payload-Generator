const VERSION = "v6-payload-generator";
const MAX_ROUTE_BODY = 16 * 1024;
const MIN_REMAINING_SECONDS = 90;
const MAX_ROUTE_TTL_SECONDS = 7 * 24 * 60 * 60;
const R2_HOST = /^vikingfile\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i;

function decodeBase64Url(value) {
  if (!value || value.length > 16384) throw new Error("invalid encoded URL");
  let b64 = value.replace(/-/g, "+").replace(/_/g, "/");
  while (b64.length % 4) b64 += "=";
  const binary = atob(b64);
  const bytes = Uint8Array.from(binary, c => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
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
  return Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]);
}

function validateUpstream(raw) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid URL length or characters");

  const target = new URL(raw);
  if (target.protocol !== "https:") throw new Error("HTTPS required");
  if (!R2_HOST.test(target.hostname)) throw new Error("not a VikingFile R2 host");
  if (target.username || target.password || target.hash) throw new Error("userinfo/fragment not allowed");
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
  const expiresAt = started + ttl * 1000;
  const remaining = Math.floor((expiresAt - Date.now()) / 1000);
  if (remaining < MIN_REMAINING_SECONDS) throw new Error("R2 URL expired or near expiry");

  // Keep RAW unchanged for SigV4. URL() above is validation only.
  return { raw, remaining };
}

function pkgHeaders() {
  return new Headers({
    "Content-Type": "application/octet-stream",
    "Content-Disposition": 'attachment; filename="download.pkg"',
    "Accept-Ranges": "bytes",
    "Cache-Control": "no-store",
    "X-Tayson-Worker-Version": VERSION
  });
}

function totalFromRange(value) {
  const m = /\/(\d+)$/.exec((value || "").trim());
  return m ? Number(m[1]) : null;
}

async function probeSize(upstream) {
  const r = await fetch(upstream, {
    method: "GET",
    headers: { "Range": "bytes=0-0" },
    redirect: "follow"
  });

  if (r.status !== 200 && r.status !== 206) {
    const status = r.status;
    try { await r.body?.cancel(); } catch (_) {}
    return { ok: false, status };
  }

  let size = totalFromRange(r.headers.get("content-range"));
  if (!size && r.status === 200) {
    const n = Number(r.headers.get("content-length"));
    if (Number.isFinite(n) && n > 0) size = n;
  }

  const meta = {
    ok: Number.isFinite(size) && size > 0,
    status: r.status,
    size,
    contentType: r.headers.get("content-type"),
    etag: r.headers.get("etag"),
    lastModified: r.headers.get("last-modified")
  };
  try { await r.body?.cancel(); } catch (_) {}
  return meta;
}

async function createRoute(request, env, origin) {
  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_ROUTE_BODY) return new Response("URL too large\n", { status: 413 });
  const raw = await request.text();
  let validated;
  try { validated = validateUpstream(raw.trim()); }
  catch (e) { return new Response("Invalid VikingFile URL: " + e.message + "\n", { status: 400 }); }

  if (env.ROUTES && typeof env.ROUTES.put === "function") {
    const token = crypto.randomUUID().replace(/-/g, "");
    const ttl = Math.max(60, Math.min(validated.remaining - 30, MAX_ROUTE_TTL_SECONDS));
    await env.ROUTES.put(token, validated.raw, { expirationTtl: ttl });
    return new Response(`${origin}/d/${token}.pkg\n`, {
      status: 200,
      headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }
    });
  }

  // No KV binding: remain functional with an inline, URL-safe route. The app
  // will enforce the ELF's 2,047-byte URL limit before generating a payload.
  const encoded = encodeBase64Url(validated.raw);
  return new Response(`${origin}/viking.pkg?u=${encoded}\n`, {
    status: 200,
    headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }
  });
}

async function resolveRoute(url, env) {
  const short = /^\/d\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (short) {
    if (!env.ROUTES || typeof env.ROUTES.get !== "function")
      throw Object.assign(new Error("KV route storage is not configured"), { status: 503 });
    const raw = await env.ROUTES.get(short[1]);
    if (!raw) throw Object.assign(new Error("route expired or missing"), { status: 410 });
    return validateUpstream(raw).raw;
  }

  if (url.pathname === "/viking.pkg") {
    const raw = decodeBase64Url(url.searchParams.get("u"));
    return validateUpstream(raw).raw;
  }

  throw Object.assign(new Error("not found"), { status: 404 });
}

async function servePkg(request, upstream) {
  if (request.method === "HEAD") {
    const probe = await probeSize(upstream);
    if (!probe.ok) return new Response(null, { status: probe.status || 502 });
    const h = pkgHeaders();
    h.set("Content-Length", String(probe.size));
    if (probe.contentType) h.set("Content-Type", probe.contentType);
    if (probe.etag) h.set("ETag", probe.etag);
    if (probe.lastModified) h.set("Last-Modified", probe.lastModified);
    return new Response(null, { status: 200, headers: h });
  }

  if (request.method !== "GET")
    return new Response("Method not allowed\n", { status: 405, headers: { "Allow": "GET, HEAD" } });

  const forward = new Headers();
  for (const name of ["range", "if-range"]) {
    const value = request.headers.get(name);
    if (value) forward.set(name, value);
  }

  const r = await fetch(upstream, { method: "GET", headers: forward, redirect: "follow" });
  const h = pkgHeaders();
  for (const name of ["content-length", "content-range", "content-type", "etag", "last-modified"]) {
    const value = r.headers.get(name);
    if (value) h.set(name, value);
  }
  h.set("X-Upstream-Status", String(r.status));
  return new Response(r.body, { status: r.status, headers: h });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/" || url.pathname === "/health") {
      return new Response("Tayson Payload Generator VikingFile Worker v6: OK\n", {
        status: 200,
        headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }
      });
    }

    if (url.pathname === "/api/routes") {
      if (request.method !== "POST") return new Response("Method not allowed\n", { status: 405 });
      return createRoute(request, env, url.origin);
    }

    let upstream;
    try { upstream = await resolveRoute(url, env); }
    catch (e) { return new Response(e.message + "\n", { status: e.status || 400 }); }

    console.log(JSON.stringify({ version: VERSION, method: request.method, path: url.pathname, range: request.headers.get("range") }));
    return servePkg(request, upstream);
  }
};
