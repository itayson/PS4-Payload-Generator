const VERSION = "v11.3-akira-regional-hosts";
const MAX_ROUTE_BODY = 32 * 1024;
const MIN_REMAINING_SECONDS = 90;
const MAX_ROUTE_TTL_SECONDS = 7 * 24 * 60 * 60;
const R2_HOST = /^(?:vikingfile|rootz-files)\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i;
const AKIRA_DOWNLOAD_HOST = /^(?:[a-z0-9-]+\.)*akirabox\.(?:com|to)$/i;
const AKIRA_REFERRER_HOST = /^(?:www\.)?akirabox\.(?:to|com)$/i;
const MOCHA_HOST = /(?:^|\.)mocha\.my$/i;
const FILEK_HOST = /(?:^|\.)(?:filekeeper\.net|dlproxy\.uk)$/i;
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

function validateMocha(raw, referer = "https://mocha.my/", userAgent = DEFAULT_AKIRA_UA) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid Mocha URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:" || !MOCHA_HOST.test(target.hostname))
    throw new Error("not an approved Mocha download host");
  if (target.username || target.password || target.hash)
    throw new Error("userinfo/fragment not allowed");
  const expires = target.searchParams.get("expires") || "";
  let remaining = 24 * 60 * 60;
  if (expires) {
    const unix = Number(expires);
    if (!Number.isInteger(unix) || unix <= 0)
      throw new Error("invalid Mocha expiry");
    remaining = Math.floor(unix - Date.now() / 1000);
    if (remaining < MIN_REMAINING_SECONDS)
      throw new Error("Mocha URL expired or near expiry");
    remaining = Math.min(remaining, 24 * 60 * 60);
  }

  referer = safeText(referer || "https://mocha.my/", 2048);
  userAgent = safeText(userAgent || DEFAULT_AKIRA_UA, 512);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || !MOCHA_HOST.test(ref.hostname))
    throw new Error("invalid Mocha referer");

  return { kind: "mocha", url: raw, referer, userAgent, remaining };
}

function validateFileK(raw, referer = "https://filekeeper.net/", userAgent = DEFAULT_AKIRA_UA) {
  if (!raw || raw.length > MAX_ROUTE_BODY || /[\r\n\0]/.test(raw))
    throw new Error("invalid FileKeeper URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:" || !FILEK_HOST.test(target.hostname))
    throw new Error("not an approved FileKeeper download host");
  if (target.username || target.password || target.hash)
    throw new Error("userinfo/fragment not allowed");
  referer = safeText(referer || "https://filekeeper.net/", 2048);
  userAgent = safeText(userAgent || DEFAULT_AKIRA_UA, 512);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || !FILEK_HOST.test(ref.hostname))
    throw new Error("invalid FileKeeper referer");

  return {
    kind: "filek",
    url: raw,
    referer,
    userAgent,
    remaining: 24 * 60 * 60
  };
}

function validateViking(raw) {
  const route = validateR2(raw);
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
    "Accept-Ranges": "bytes",
    "Cache-Control": "no-store",
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

async function fetchUpstream(route, range, ifRange) {
  return fetch(route.url, {
    method: "GET",
    headers: upstreamHeaders(route, range, ifRange),
    redirect: "follow"
  });
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
  if (!env.ROUTES || typeof env.ROUTES.put !== "function") return null;
  const token = crypto.randomUUID().replace(/-/g, "");
  const ttl = Math.max(60, Math.min(route.remaining - 30, MAX_ROUTE_TTL_SECONDS));
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


async function createProviderRoute(request, env, origin, provider) {
  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_ROUTE_BODY)
    return new Response("URL too large\n", { status: 413 });

  const raw = (await request.text()).trim();
  let route;
  let prefix;

  try {
    if (provider === "mocha") {
      route = validateMocha(raw);
      prefix = "m";
    } else if (provider === "filek") {
      route = validateFileK(raw);
      prefix = "f";
    } else if (provider === "viking") {
      route = validateViking(raw);
      prefix = "v";
    } else {
      throw new Error("unsupported provider route");
    }
  } catch (e) {
    return new Response(
      "Invalid " + provider + " URL: " + e.message + "\n",
      { status: 400 }
    );
  }

  const short = await putRoute(env, route, origin, prefix);
  if (short) {
    return new Response(short + "\n", {
      status: 200,
      headers: {
        "Content-Type": "text/plain; charset=utf-8",
        "Cache-Control": "no-store",
        "X-Tayson-Provider": provider
      }
    });
  }

  if (provider === "viking") {
    return new Response(
      origin + "/viking.pkg?u=" + encodeBase64Url(route.url) + "\n",
      {
        status: 200,
        headers: {
          "Content-Type": "text/plain; charset=utf-8",
          "Cache-Control": "no-store",
          "X-Tayson-Provider": provider
        }
      }
    );
  }

  return new Response("ROUTES KV binding is required for " + provider + "\n", {
    status: 503,
    headers: { "Cache-Control": "no-store" }
  });
}

async function loadStoredRoute(env, token) {
  if (!env.ROUTES || typeof env.ROUTES.get !== "function")
    throw Object.assign(new Error("KV route storage is not configured"), { status: 503 });
  const stored = await env.ROUTES.get(token);
  if (!stored) throw Object.assign(new Error("route expired or missing"), { status: 410 });
  try {
    const parsed = JSON.parse(stored);
    if (parsed && parsed.kind === "akira")
      return validateAkira(parsed.url, parsed.cookie, parsed.referer, parsed.userAgent);
    if (parsed && parsed.kind === "mocha")
      return validateMocha(parsed.url, parsed.referer, parsed.userAgent);
    if (parsed && parsed.kind === "filek")
      return validateFileK(parsed.url, parsed.referer, parsed.userAgent);
    if (parsed && parsed.kind === "viking")
      return validateViking(parsed.url);
    if (parsed && parsed.kind === "r2") return validateR2(parsed.url);
  } catch (e) {
    if (stored.trim().startsWith("{")) throw Object.assign(new Error("invalid stored route"), { status: 410 });
  }
  // Backward compatibility with v6/v7 KV values that stored the raw R2 URL.
  return validateR2(stored);
}

async function resolveRoute(url, env) {
  let m = /^\/d\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (m) return loadStoredRoute(env, m[1]);
  m = /^\/a\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (m) return loadStoredRoute(env, m[1]);
  m = /^\/m\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (m) return loadStoredRoute(env, m[1]);
  m = /^\/f\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (m) return loadStoredRoute(env, m[1]);
  m = /^\/v\/([a-f0-9]{32})\.pkg$/i.exec(url.pathname);
  if (m) return loadStoredRoute(env, m[1]);

  if (url.pathname === "/r2.pkg")
    return validateR2(decodeBase64Url(url.searchParams.get("u")));
  if (url.pathname === "/viking.pkg")
    return validateViking(decodeBase64Url(url.searchParams.get("u")));
  if (url.pathname === "/mocha.pkg")
    return validateMocha(decodeBase64Url(url.searchParams.get("u")));
  if (url.pathname === "/filek.pkg")
    return validateFileK(decodeBase64Url(url.searchParams.get("u")));
  if (url.pathname === "/akira.pkg")
    return validateAkira(decodeBase64Url(url.searchParams.get("u")), "", "https://akirabox.to/", DEFAULT_AKIRA_UA);
  throw Object.assign(new Error("not found"), { status: 404 });
}

async function servePkg(request, route) {
  if (request.method === "HEAD") {
    const probe = await probeSize(route);
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
  const r = await fetchUpstream(route, request.headers.get("range"), request.headers.get("if-range"));
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
      const routesReady = !!(env.ROUTES &&
        typeof env.ROUTES.get === "function" &&
        typeof env.ROUTES.put === "function");
      return new Response(
        "Tayson Payload Generator Universal Transport Worker " + VERSION + ": OK\n" +
        "ROUTES: " + (routesReady ? "OK" : "MISSING") + "\n" +
        "PROVIDERS: AkiraBox, Rootz, Mocha, FileKeeper, VikingFile\n" +
        "AKIRA_REGIONAL_HOSTS: ON\n" +
        "FILEK_EXTENSION_NORMALIZATION: ON\n" +
        "FILEK_DLPROXY: ON\n" +
        "MOCHA_ROUTE: /m/<token>.pkg\n" +
        "FILEK_ROUTE: /f/<token>.pkg\n" +
        "VIKING_ROUTE: /v/<token>.pkg\n",
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
    if (url.pathname === "/api/mocha-routes") {
      if (request.method !== "POST") return new Response("Method not allowed\n", { status: 405 });
      return createProviderRoute(request, env, url.origin, "mocha");
    }
    if (url.pathname === "/api/filek-routes") {
      if (request.method !== "POST") return new Response("Method not allowed\n", { status: 405 });
      return createProviderRoute(request, env, url.origin, "filek");
    }
    if (url.pathname === "/api/viking-routes") {
      if (request.method !== "POST") return new Response("Method not allowed\n", { status: 405 });
      return createProviderRoute(request, env, url.origin, "viking");
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
