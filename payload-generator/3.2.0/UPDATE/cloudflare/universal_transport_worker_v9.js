const VERSION = "v9-bgft-fixed-length";
const ROUTE_STORE_VERSION = 1;
const MAX_ROUTE_BODY = 32 * 1024;
const MIN_REMAINING_SECONDS = 90;
const MAX_ROUTE_TTL_SECONDS = 7 * 24 * 60 * 60;
const R2_HOST = /^(?:vikingfile|rootz-files)\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i;
const AKIRA_DOWNLOAD_HOST = /^download\.akirabox\.com$/i;
const AKIRA_REFERRER_HOST = /^(?:www\.)?akirabox\.(?:to|com)$/i;
const DEFAULT_AKIRA_UA = "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Mobile Safari/537.36";

function decodeBase64Url(value) {
  if (!value || value.length > 24000) throw new Error("invalid encoded URL");
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

function safeText(value, max) {
  value = String(value || "");
  if (value.length > max || /[\r\n\0]/.test(value)) throw new Error("invalid request context");
  return value;
}

function validateR2(raw) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:") throw new Error("HTTPS required");
  if (!R2_HOST.test(target.hostname)) throw new Error("not an approved VikingFile/Rootz R2 host");
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
  const remaining = Math.floor((started + ttl * 1000 - Date.now()) / 1000);
  if (remaining < MIN_REMAINING_SECONDS) throw new Error("R2 URL expired or near expiry");
  return { kind: "r2", url: raw, remaining };
}

function validateAkira(raw, cookie = "", referer = "https://akirabox.to/", userAgent = DEFAULT_AKIRA_UA) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid AkiraBox URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:" || !AKIRA_DOWNLOAD_HOST.test(target.hostname))
    throw new Error("not an approved AkiraBox download host");
  if (target.username || target.password || target.hash) throw new Error("userinfo/fragment not allowed");
  if (!target.pathname.toLowerCase().endsWith(".pkg")) throw new Error("AkiraBox target is not a PKG");
  const access = target.searchParams.get("access") || "";
  const m = /^(\d{13})\.([A-Za-z0-9_-]{16,})$/.exec(access);
  if (!m) throw new Error("missing or malformed AkiraBox access token");
  const expiresAt = Number(m[1]);
  const remaining = Math.floor((expiresAt - Date.now()) / 1000);
  if (!Number.isFinite(expiresAt) || remaining < MIN_REMAINING_SECONDS)
    throw new Error("AkiraBox URL expired or near expiry");
  cookie = safeText(cookie, 8192);
  userAgent = safeText(userAgent || DEFAULT_AKIRA_UA, 512);
  referer = safeText(referer || "https://akirabox.to/", 2048);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || !AKIRA_REFERRER_HOST.test(ref.hostname))
    throw new Error("invalid AkiraBox referer");
  return {
    kind: "akira",
    url: raw,
    cookie,
    referer,
    userAgent,
    remaining: Math.min(remaining, 24 * 60 * 60)
  };
}

function pkgHeaders() {
  return new Headers({
    "Content-Type": "application/octet-stream",
    "Content-Disposition": 'attachment; filename="download.pkg"',
    "Accept-Ranges": "bytes",
    "Cache-Control": "no-store, no-transform",
    "X-Tayson-Worker-Version": VERSION
  });
}

function totalFromRange(value) {
  const m = /\/(\d+)$/.exec((value || "").trim());
  return m ? Number(m[1]) : null;
}

function upstreamHeaders(route, range, ifRange) {
  const h = new Headers();
  if (range) h.set("Range", range);
  if (ifRange) h.set("If-Range", ifRange);
  h.set("Accept-Encoding", "identity");
  if (route.kind === "akira") {
    h.set("Accept", "*/*");
    h.set("User-Agent", route.userAgent || DEFAULT_AKIRA_UA);
    h.set("Referer", route.referer || "https://akirabox.to/");
    if (route.cookie) h.set("Cookie", route.cookie);
  }
  return h;
}

async function fetchUpstream(route, range, ifRange) {
  return fetch(route.url, {
    method: "GET",
    headers: upstreamHeaders(route, range, ifRange),
    redirect: "follow",
    cache: "no-store"
  });
}

function responseLength(response) {
  const direct = Number(response.headers.get("content-length"));
  if (Number.isSafeInteger(direct) && direct >= 0) return direct;

  const contentRange = (response.headers.get("content-range") || "").trim();
  const m = /^bytes\s+(\d+)-(\d+)\/(?:\d+|\*)$/i.exec(contentRange);
  if (!m) return null;
  const start = Number(m[1]);
  const end = Number(m[2]);
  if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || end < start) return null;
  return end - start + 1;
}

function fixedLengthProxy(response, headers) {
  if (!response.body) return new Response(null, { status: response.status, headers });

  const length = responseLength(response);
  if (!Number.isSafeInteger(length) || length < 0) {
    headers.set("X-Tayson-Length-Mode", "chunked-fallback");
    return new Response(response.body, { status: response.status, headers });
  }

  const { readable, writable } = new FixedLengthStream(length);
  response.body.pipeTo(writable).catch((error) => {
    console.error(JSON.stringify({
      version: VERSION,
      event: "fixed-length-pipe-error",
      message: String(error && error.message || error)
    }));
  });
  headers.set("X-Tayson-Length-Mode", "fixed");
  return new Response(readable, { status: response.status, headers });
}

async function probeSize(route) {
  const r = await fetchUpstream(route, "bytes=0-0", null);
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

async function putRoute(env, route, origin, prefix) {
  const token = crypto.randomUUID().replace(/-/g, "");
  const ttl = Math.max(60, Math.min(route.remaining - 30, MAX_ROUTE_TTL_SECONDS));

  // Strongly-consistent path. Prefer this whenever ROUTE_STORE is bound.
  if (env.ROUTE_STORE && typeof env.ROUTE_STORE.idFromName === "function") {
    const id = env.ROUTE_STORE.idFromName(token);
    const stub = env.ROUTE_STORE.get(id);
    const expiresAt = Date.now() + ttl * 1000;
    const response = await stub.fetch("https://route-store/route", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ version: ROUTE_STORE_VERSION, route, expiresAt })
    });
    if (!response.ok)
      throw Object.assign(new Error("Durable route store failed"), { status: 503 });
    return `${origin}/${prefix}/${token}.pkg`;
  }

  // Backward-compatible fallback. Workers KV is eventually consistent, so
  // this mode is retained for existing deployments but reported by /health.
  if (!env.ROUTES || typeof env.ROUTES.put !== "function") return null;
  await env.ROUTES.put(token, JSON.stringify(route), { expirationTtl: ttl });
  return `${origin}/${prefix}/${token}.pkg`;
}

async function createR2Route(request, env, origin) {
  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_ROUTE_BODY) return new Response("URL too large\n", { status: 413 });
  const raw = (await request.text()).trim();
  let route;
  try { route = validateR2(raw); }
  catch (e) { return new Response("Invalid R2 URL: " + e.message + "\n", { status: 400 }); }
  const short = await putRoute(env, route, origin, "d");
  if (short) return new Response(short + "\n", { status: 200, headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" } });
  return new Response(`${origin}/r2.pkg?u=${encodeBase64Url(route.url)}\n`, {
    status: 200,
    headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }
  });
}

async function createAkiraRoute(request, env, origin) {
  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_ROUTE_BODY) return new Response("Context too large\n", { status: 413 });
  const form = new URLSearchParams(await request.text());
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
  if (short) return new Response(short + "\n", { status: 200, headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" } });
  // No KV: preserve only the signed URL. Cookies are intentionally never placed
  // in the PS4-visible route. This still fixes BGFT HEAD/User-Agent/Referer issues.
  return new Response(`${origin}/akira.pkg?u=${encodeBase64Url(route.url)}\n`, {
    status: 200,
    headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store", "X-Akira-Context": "no-kv-cookie-omitted" }
  });
}

async function loadStoredRoute(env, token) {
  let stored = null;

  if (env.ROUTE_STORE && typeof env.ROUTE_STORE.idFromName === "function") {
    const id = env.ROUTE_STORE.idFromName(token);
    const stub = env.ROUTE_STORE.get(id);
    const response = await stub.fetch("https://route-store/route", { method: "GET" });
    if (response.status === 404 || response.status === 410)
      throw Object.assign(new Error("route expired or missing"), { status: 410 });
    if (!response.ok)
      throw Object.assign(new Error("Durable route store unavailable"), { status: 503 });
    stored = await response.text();
  } else {
    if (!env.ROUTES || typeof env.ROUTES.get !== "function")
      throw Object.assign(new Error("route storage is not configured"), { status: 503 });
    stored = await env.ROUTES.get(token);
    if (!stored) throw Object.assign(new Error("route expired or missing"), { status: 410 });
  }

  try {
    const parsed = JSON.parse(stored);
    if (parsed && parsed.kind === "akira")
      return validateAkira(parsed.url, parsed.cookie, parsed.referer, parsed.userAgent);
    if (parsed && parsed.kind === "r2") return validateR2(parsed.url);
  } catch (e) {
    if (stored.trim().startsWith("{"))
      throw Object.assign(new Error("invalid stored route"), { status: 410 });
  }

  // Backward compatibility with v6/v7 KV values that stored the raw R2 URL.
  return validateR2(stored);
}

async function resolveRoute(url, env) {
  let m = /^\/d\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (m) return loadStoredRoute(env, m[1]);
  m = /^\/a\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (m) return loadStoredRoute(env, m[1]);
  if (url.pathname === "/r2.pkg" || url.pathname === "/viking.pkg")
    return validateR2(decodeBase64Url(url.searchParams.get("u")));
  if (url.pathname === "/akira.pkg")
    return validateAkira(decodeBase64Url(url.searchParams.get("u")), "", "https://akirabox.to/", DEFAULT_AKIRA_UA);
  throw Object.assign(new Error("not found"), { status: 404 });
}

async function servePkg(request, route) {
  if (request.method === "HEAD") {
    const probe = await probeSize(route);
    if (!probe.ok) return new Response(null, { status: probe.status || 502 });
    const h = pkgHeaders();
    // PS4 BGFT uses HEAD/Range discovery. On HTTP/1.1/2 Workers preserves this
    // HEAD length; GET/206 framing is independently enforced below.
    h.set("Content-Length", String(probe.size));
    h.set("X-Tayson-Total-Length", String(probe.size));
    h.set("X-Tayson-Route-Kind", route.kind);
    if (probe.contentType) h.set("Content-Type", probe.contentType);
    if (probe.etag) h.set("ETag", probe.etag);
    if (probe.lastModified) h.set("Last-Modified", probe.lastModified);
    return new Response(null, { status: 200, headers: h });
  }

  if (request.method !== "GET")
    return new Response("Method not allowed\n", {
      status: 405,
      headers: { "Allow": "GET, HEAD", "Cache-Control": "no-store" }
    });

  const requestedRange = request.headers.get("range");
  const r = await fetchUpstream(route, requestedRange, request.headers.get("if-range"));

  if (requestedRange && r.status !== 206) {
    try { await r.body?.cancel(); } catch (_) {}
    return new Response("Upstream ignored byte range\n", {
      status: 502,
      headers: { "Cache-Control": "no-store", "X-Upstream-Status": String(r.status) }
    });
  }
  if (r.status === 206 && !r.headers.get("content-range")) {
    try { await r.body?.cancel(); } catch (_) {}
    return new Response("Upstream returned 206 without Content-Range\n", {
      status: 502,
      headers: { "Cache-Control": "no-store" }
    });
  }

  const h = pkgHeaders();
  for (const name of ["content-range", "content-type", "etag", "last-modified"]) {
    const value = r.headers.get(name);
    if (value) h.set(name, value);
  }
  h.set("X-Upstream-Status", String(r.status));
  h.set("X-Tayson-Route-Kind", route.kind);

  // Do not set Content-Length manually here. Cloudflare ignores a manual
  // Content-Length on an arbitrary ReadableStream. FixedLengthStream makes the
  // runtime emit the correct Content-Length instead of Transfer-Encoding: chunked.
  return fixedLengthProxy(r, h);
}

export class RouteStore {
  constructor(state, env) {
    this.state = state;
    this.env = env;
  }

  async fetch(request) {
    const url = new URL(request.url);
    if (url.pathname !== "/route")
      return new Response("not found\n", { status: 404 });

    if (request.method === "PUT") {
      let value;
      try { value = await request.json(); }
      catch (_) { return new Response("invalid json\n", { status: 400 }); }

      const expiresAt = Number(value && value.expiresAt);
      if (!value || value.version !== ROUTE_STORE_VERSION || !value.route ||
          !Number.isFinite(expiresAt) || expiresAt <= Date.now())
        return new Response("invalid route\n", { status: 400 });

      await this.state.storage.put("route", JSON.stringify(value.route));
      await this.state.storage.put("expiresAt", expiresAt);
      await this.state.storage.setAlarm(expiresAt);
      return new Response("OK\n", { status: 200 });
    }

    if (request.method === "GET") {
      const expiresAt = Number(await this.state.storage.get("expiresAt") || 0);
      if (!expiresAt || expiresAt <= Date.now()) {
        await this.state.storage.deleteAll();
        return new Response("route expired or missing\n", { status: 410 });
      }
      const route = await this.state.storage.get("route");
      if (!route) return new Response("route expired or missing\n", { status: 410 });
      return new Response(String(route), {
        status: 200,
        headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" }
      });
    }

    return new Response("Method not allowed\n", { status: 405 });
  }

  async alarm() {
    await this.state.storage.deleteAll();
  }
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === "/" || url.pathname === "/health") {
      const doReady = !!(env.ROUTE_STORE && typeof env.ROUTE_STORE.idFromName === "function");
      const kvReady = !!(env.ROUTES && typeof env.ROUTES.get === "function" && typeof env.ROUTES.put === "function");
      const storage = doReady ? "DURABLE_OBJECT" : kvReady ? "KV_EVENTUAL" : "MISSING";
      return new Response(
        "Tayson Payload Generator Universal Transport Worker " + VERSION + ": OK\n" +
        "ROUTE_STORE: " + storage + "\n" +
        "FIXED_LENGTH_STREAM: ON\n" +
        "STRICT_RANGE: ON\n" +
        "COLO: " + String(request.cf && request.cf.colo || "unknown") + "\n",
        {
          status: 200,
          headers: { "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store" }
        }
      );
    }
    if (url.pathname === "/api/routes") {
      if (request.method !== "POST") return new Response("Method not allowed\n", { status: 405 });
      return createR2Route(request, env, url.origin);
    }
    if (url.pathname === "/api/akira-routes") {
      if (request.method !== "POST") return new Response("Method not allowed\n", { status: 405 });
      return createAkiraRoute(request, env, url.origin);
    }
    let route;
    try { route = await resolveRoute(url, env); }
    catch (e) { return new Response(e.message + "\n", { status: e.status || 400 }); }
    console.log(JSON.stringify({
      version: VERSION,
      kind: route.kind,
      method: request.method,
      path: url.pathname,
      range: request.headers.get("range")
    }));
    return servePkg(request, route);
  }
};
