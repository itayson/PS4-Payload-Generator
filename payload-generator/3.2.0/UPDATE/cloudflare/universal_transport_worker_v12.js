const VERSION = "v12-bgft-compat-9-12";
const PROFILE = "ps4-bgft-fw-9-12";
const MAX_ROUTE_BODY = 32 * 1024;
const MAX_ENCODED_URL = 24000;
const MIN_REMAINING_SECONDS = 120;
const MAX_ROUTE_TTL_SECONDS = 7 * 24 * 60 * 60;
const MAX_REDIRECTS = 5;

const R2_HOST = /^(?:vikingfile|rootz-files)\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i;
const VIKING_R2_HOST = /^vikingfile\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i;
const ROOTZ_R2_HOST = /^rootz-files\.[a-f0-9]{32}\.r2\.cloudflarestorage\.com$/i;
const AKIRA_DOWNLOAD_HOST = /^(?:[a-z0-9-]+\.)*akirabox\.(?:com|to)$/i;
const AKIRA_REFERRER_HOST = /^(?:www\.)?akirabox\.(?:to|com)$/i;
const MOCHA_HOST = /(?:^|\.)mocha\.my$/i;
const FILEK_HOST = /(?:^|\.)(?:filekeeper\.net|dlproxy\.uk)$/i;

const DEFAULT_UA =
  "Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Mobile Safari/537.36";

function safeText(value, max) {
  value = String(value || "");
  if (value.length > max || /[\r\n\0]/.test(value))
    throw new Error("invalid request context");
  return value;
}

function encodeBase64Url(value) {
  const bytes = new TextEncoder().encode(value);
  let binary = "";
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function decodeBase64Url(value) {
  if (!value || value.length > MAX_ENCODED_URL) throw new Error("invalid encoded URL");
  let b64 = value.replace(/-/g, "+").replace(/_/g, "/");
  while (b64.length % 4) b64 += "=";
  const binary = atob(b64);
  const bytes = Uint8Array.from(binary, c => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

function parseAmzDate(value) {
  const m = /^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z$/.exec(value || "");
  if (!m) return NaN;
  return Date.UTC(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]);
}

function validateBasicUrl(raw, max = MAX_ROUTE_BODY) {
  if (!raw || raw.length > max || /[\r\n\0]/.test(raw))
    throw new Error("invalid URL length or characters");
  const target = new URL(raw);
  if (target.protocol !== "https:") throw new Error("HTTPS required");
  if (target.username || target.password || target.hash)
    throw new Error("userinfo/fragment not allowed");
  return target;
}

function validateR2(raw, expectedKind = "r2") {
  const target = validateBasicUrl(raw);
  if (!R2_HOST.test(target.hostname))
    throw new Error("not an approved VikingFile/Rootz R2 host");
  if (expectedKind === "viking" && !VIKING_R2_HOST.test(target.hostname))
    throw new Error("not a VikingFile R2 URL");
  if (expectedKind === "rootz" && !ROOTZ_R2_HOST.test(target.hostname))
    throw new Error("not a Rootz R2 URL");
  if (target.searchParams.get("X-Amz-Algorithm") !== "AWS4-HMAC-SHA256")
    throw new Error("invalid signing algorithm");
  if (!target.searchParams.get("X-Amz-Signature") ||
      !target.searchParams.get("X-Amz-Date") ||
      !target.searchParams.get("X-Amz-Expires"))
    throw new Error("missing R2 presign fields");

  const started = parseAmzDate(target.searchParams.get("X-Amz-Date"));
  const ttl = Number(target.searchParams.get("X-Amz-Expires"));
  if (!Number.isFinite(started) || !Number.isInteger(ttl) ||
      ttl <= 0 || ttl > MAX_ROUTE_TTL_SECONDS)
    throw new Error("invalid R2 expiry");

  const remaining = Math.floor((started + ttl * 1000 - Date.now()) / 1000);
  if (remaining < MIN_REMAINING_SECONDS)
    throw new Error("R2 URL expired or near expiry");

  return { kind: expectedKind, url: raw, remaining };
}

function validateAkira(raw, cookie = "", referer = "https://akirabox.to/", userAgent = DEFAULT_UA) {
  const target = validateBasicUrl(raw);
  if (!AKIRA_DOWNLOAD_HOST.test(target.hostname))
    throw new Error("not an approved AkiraBox download host");
  if (!target.pathname.toLowerCase().endsWith(".pkg"))
    throw new Error("AkiraBox target is not a PKG");

  const access = target.searchParams.get("access") || "";
  let remaining = 24 * 60 * 60;
  if (access) {
    const m = /^(\d{13})\.([A-Za-z0-9_-]{16,})$/.exec(access);
    if (!m) throw new Error("malformed AkiraBox access token");
    const expiresAt = Number(m[1]);
    remaining = Math.floor((expiresAt - Date.now()) / 1000);
    if (!Number.isFinite(expiresAt) || remaining < MIN_REMAINING_SECONDS)
      throw new Error("AkiraBox URL expired or near expiry");
    remaining = Math.min(remaining, 24 * 60 * 60);
  }

  cookie = safeText(cookie, 8192);
  userAgent = safeText(userAgent || DEFAULT_UA, 512);
  referer = safeText(referer || "https://akirabox.to/", 2048);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || !AKIRA_REFERRER_HOST.test(ref.hostname))
    throw new Error("invalid AkiraBox referer");

  return { kind: "akira", url: raw, cookie, referer, userAgent, remaining };
}

function validateMocha(raw, referer = "https://mocha.my/", userAgent = DEFAULT_UA) {
  const target = validateBasicUrl(raw);
  if (!MOCHA_HOST.test(target.hostname))
    throw new Error("not an approved Mocha download host");

  let remaining = 24 * 60 * 60;
  const expires = target.searchParams.get("expires") || "";
  if (expires) {
    const unix = Number(expires);
    if (!Number.isInteger(unix) || unix <= 0) throw new Error("invalid Mocha expiry");
    remaining = Math.floor(unix - Date.now() / 1000);
    if (remaining < MIN_REMAINING_SECONDS)
      throw new Error("Mocha URL expired or near expiry");
    remaining = Math.min(remaining, 24 * 60 * 60);
  }

  referer = safeText(referer || "https://mocha.my/", 2048);
  userAgent = safeText(userAgent || DEFAULT_UA, 512);
  const ref = new URL(referer);
  if (ref.protocol !== "https:" || !MOCHA_HOST.test(ref.hostname))
    throw new Error("invalid Mocha referer");

  return { kind: "mocha", url: raw, referer, userAgent, remaining };
}

function validateFileK(raw, referer = "https://filekeeper.net/", userAgent = DEFAULT_UA) {
  const target = validateBasicUrl(raw);
  if (!FILEK_HOST.test(target.hostname))
    throw new Error("not an approved FileKeeper host");

  referer = safeText(referer || "https://filekeeper.net/", 2048);
  userAgent = safeText(userAgent || DEFAULT_UA, 512);
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

function validateStoredRoute(route) {
  if (!route || typeof route !== "object") throw new Error("invalid stored route");
  if (route.kind === "akira")
    return validateAkira(route.url, route.cookie, route.referer, route.userAgent);
  if (route.kind === "mocha")
    return validateMocha(route.url, route.referer, route.userAgent);
  if (route.kind === "filek")
    return validateFileK(route.url, route.referer, route.userAgent);
  if (route.kind === "viking") return validateR2(route.url, "viking");
  if (route.kind === "rootz") return validateR2(route.url, "rootz");
  if (route.kind === "r2") return validateR2(route.url, "r2");
  throw new Error("unsupported stored route");
}

function routeAllowsUrl(route, candidate) {
  const u = validateBasicUrl(candidate);
  if (route.kind === "akira") return AKIRA_DOWNLOAD_HOST.test(u.hostname);
  if (route.kind === "mocha") return MOCHA_HOST.test(u.hostname);
  if (route.kind === "filek") return FILEK_HOST.test(u.hostname);
  if (route.kind === "viking") return VIKING_R2_HOST.test(u.hostname);
  if (route.kind === "rootz") return ROOTZ_R2_HOST.test(u.hostname);
  if (route.kind === "r2") return R2_HOST.test(u.hostname);
  return false;
}

function upstreamHeaders(route, range, ifRange) {
  const h = new Headers({
    "Accept": "*/*",
    "Accept-Encoding": "identity"
  });
  if (range) h.set("Range", range);
  if (ifRange) h.set("If-Range", ifRange);

  if (route.kind === "akira") {
    h.set("User-Agent", route.userAgent || DEFAULT_UA);
    h.set("Referer", route.referer || "https://akirabox.to/");
    if (route.cookie) h.set("Cookie", route.cookie);
  } else if (route.kind === "mocha") {
    h.set("User-Agent", route.userAgent || DEFAULT_UA);
    h.set("Referer", route.referer || "https://mocha.my/");
  } else if (route.kind === "filek") {
    // v11.2 hardware validation found FileKeeper most reliable without
    // Referer/User-Agent. Keep identity encoding only.
  }

  return h;
}

async function fetchUpstream(route, range, ifRange) {
  let current = route.url;
  const seen = new Set();

  for (let redirects = 0; redirects <= MAX_REDIRECTS; redirects++) {
    if (seen.has(current)) throw new Error("upstream redirect loop");
    seen.add(current);

    const r = await fetch(current, {
      method: "GET",
      headers: upstreamHeaders(route, range, ifRange),
      redirect: "manual"
    });

    if (![301, 302, 303, 307, 308].includes(r.status)) return r;

    const location = r.headers.get("location");
    try { await r.body?.cancel(); } catch (_) {}
    if (!location) throw new Error("upstream redirect without Location");
    const next = new URL(location, current).toString();
    if (!routeAllowsUrl(route, next))
      throw new Error("upstream redirect left the approved provider");
    current = next;
  }

  throw new Error("too many upstream redirects");
}

function parseRange(value) {
  if (!value) return null;
  if (value.includes(",")) throw new Error("multiple ranges are not supported");
  const m = /^bytes=(\d+)-(\d*)$/i.exec(value.trim());
  if (!m) throw new Error("invalid range");
  const start = Number(m[1]);
  const end = m[2] ? Number(m[2]) : null;
  if (!Number.isSafeInteger(start) || start < 0 ||
      (end !== null && (!Number.isSafeInteger(end) || end < start)))
    throw new Error("invalid range");
  return { start, end };
}

function parseContentRange(value) {
  const m = /^bytes\s+(\d+)-(\d+)\/(\d+)$/i.exec((value || "").trim());
  if (!m) return null;
  const start = Number(m[1]), end = Number(m[2]), total = Number(m[3]);
  if (![start, end, total].every(Number.isSafeInteger) ||
      start < 0 || end < start || total <= end)
    return null;
  return { start, end, total };
}

function pkgHeaders() {
  return new Headers({
    "Content-Type": "application/octet-stream",
    "Content-Disposition": 'attachment; filename="download.pkg"',
    "Accept-Ranges": "bytes",
    "Cache-Control": "no-store, no-transform",
    "X-Tayson-Worker-Version": VERSION,
    "X-Tayson-BGFT-Profile": PROFILE
  });
}

async function probeSize(route) {
  const r = await fetchUpstream(route, "bytes=0-0", null);
  const encoding = (r.headers.get("content-encoding") || "").trim().toLowerCase();

  if (encoding && encoding !== "identity") {
    try { await r.body?.cancel(); } catch (_) {}
    return { ok: false, status: 502, reason: "encoded upstream response" };
  }

  if (r.status !== 200 && r.status !== 206) {
    const status = r.status;
    try { await r.body?.cancel(); } catch (_) {}
    return { ok: false, status };
  }

  let size = null;
  const cr = parseContentRange(r.headers.get("content-range"));
  if (r.status === 206 && cr && cr.start === 0) size = cr.total;
  if (!size && r.status === 200) {
    const n = Number(r.headers.get("content-length"));
    if (Number.isSafeInteger(n) && n > 0) size = n;
  }

  const meta = {
    ok: Number.isSafeInteger(size) && size > 0,
    status: r.status,
    size,
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
  await env.ROUTES.put(token, JSON.stringify({ schema: 12, ...route }), { expirationTtl: ttl });
  return origin + "/" + prefix + "/" + token + ".pkg";
}

function routeReply(route) {
  return new Response(route + "\n", {
    status: 200,
    headers: {
      "Content-Type": "text/plain; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Tayson-Worker-Version": VERSION
    }
  });
}

async function createRoute(request, env, origin, provider) {
  const length = Number(request.headers.get("content-length") || 0);
  if (length > MAX_ROUTE_BODY) return new Response("Request too large\n", { status: 413 });

  let route;
  let prefix;
  try {
    if (provider === "akira") {
      const form = new URLSearchParams(await request.text());
      route = validateAkira(
        form.get("url") || "",
        form.get("cookie") || "",
        form.get("referer") || "https://akirabox.to/",
        form.get("userAgent") || DEFAULT_UA
      );
      prefix = "a";
    } else {
      const raw = (await request.text()).trim();
      if (provider === "mocha") {
        route = validateMocha(raw); prefix = "m";
      } else if (provider === "filek") {
        route = validateFileK(raw); prefix = "f";
      } else if (provider === "viking") {
        route = validateR2(raw, "viking"); prefix = "v";
      } else if (provider === "rootz") {
        route = validateR2(raw, "rootz"); prefix = "z";
      } else if (provider === "r2") {
        route = validateR2(raw, "r2"); prefix = "d";
      } else {
        throw new Error("unsupported provider route");
      }
    }
  } catch (e) {
    return new Response("Invalid " + provider + " route: " + e.message + "\n", {
      status: 400,
      headers: { "Cache-Control": "no-store" }
    });
  }

  const short = await putRoute(env, route, origin, prefix);
  if (short) return routeReply(short);

  if (provider === "akira" && route.cookie) {
    return new Response("ROUTES KV binding is required for authenticated AkiraBox routes\n", {
      status: 503,
      headers: { "Cache-Control": "no-store" }
    });
  }

  const endpoint =
    provider === "viking" ? "viking.pkg" :
    provider === "rootz" ? "rootz.pkg" :
    provider === "mocha" ? "mocha.pkg" :
    provider === "filek" ? "filek.pkg" :
    provider === "akira" ? "akira.pkg" : "r2.pkg";

  return routeReply(origin + "/" + endpoint + "?u=" + encodeBase64Url(route.url));
}

async function loadStoredRoute(env, token) {
  if (!env.ROUTES || typeof env.ROUTES.get !== "function")
    throw Object.assign(new Error("KV route storage is not configured"), { status: 503 });

  const stored = await env.ROUTES.get(token);
  if (!stored) throw Object.assign(new Error("route expired or missing"), { status: 410 });

  try {
    const parsed = JSON.parse(stored);
    return validateStoredRoute(parsed);
  } catch (e) {
    if (!stored.trim().startsWith("{")) return validateR2(stored, "r2");
    throw Object.assign(new Error("invalid stored route"), { status: 410 });
  }
}

async function resolveRoute(url, env) {
  const mappings = [
    [/^\/d\/([a-f0-9]{32})\.pkg$/i, "r2"],
    [/^\/v\/([a-f0-9]{32})\.pkg$/i, "viking"],
    [/^\/z\/([a-f0-9]{32})\.pkg$/i, "rootz"],
    [/^\/a\/([a-f0-9]{32})\.pkg$/i, "akira"],
    [/^\/m\/([a-f0-9]{32})\.pkg$/i, "mocha"],
    [/^\/f\/([a-f0-9]{32})\.pkg$/i, "filek"]
  ];

  for (const item of mappings) {
    const m = item[0].exec(url.pathname);
    if (m) return loadStoredRoute(env, m[1]);
  }

  const encoded = url.searchParams.get("u");
  if (url.pathname === "/r2.pkg") return validateR2(decodeBase64Url(encoded), "r2");
  if (url.pathname === "/viking.pkg") return validateR2(decodeBase64Url(encoded), "viking");
  if (url.pathname === "/rootz.pkg") return validateR2(decodeBase64Url(encoded), "rootz");
  if (url.pathname === "/mocha.pkg") return validateMocha(decodeBase64Url(encoded));
  if (url.pathname === "/filek.pkg") return validateFileK(decodeBase64Url(encoded));
  if (url.pathname === "/akira.pkg")
    return validateAkira(decodeBase64Url(encoded), "", "https://akirabox.to/", DEFAULT_UA);

  throw Object.assign(new Error("not found"), { status: 404 });
}

async function servePkg(request, route) {
  if (request.method === "HEAD") {
    const probe = await probeSize(route);
    if (!probe.ok)
      return new Response(null, {
        status: probe.status || 502,
        headers: { "Cache-Control": "no-store" }
      });

    const h = pkgHeaders();
    h.set("Content-Length", String(probe.size));
    if (probe.etag) h.set("ETag", probe.etag);
    if (probe.lastModified) h.set("Last-Modified", probe.lastModified);
    return new Response(null, { status: 200, headers: h });
  }

  if (request.method !== "GET")
    return new Response("Method not allowed\n", {
      status: 405,
      headers: { "Allow": "GET, HEAD", "Cache-Control": "no-store" }
    });

  let requestedRange = null;
  try { requestedRange = parseRange(request.headers.get("range")); }
  catch (e) {
    return new Response(null, {
      status: 416,
      headers: { "Accept-Ranges": "bytes", "Cache-Control": "no-store" }
    });
  }

  let upstream;
  try {
    upstream = await fetchUpstream(
      route,
      request.headers.get("range"),
      request.headers.get("if-range")
    );
  } catch (e) {
    return new Response("Upstream transport error\n", {
      status: 502,
      headers: { "Cache-Control": "no-store" }
    });
  }

  const encoding = (upstream.headers.get("content-encoding") || "").trim().toLowerCase();
  if (encoding && encoding !== "identity") {
    try { await upstream.body?.cancel(); } catch (_) {}
    return new Response("Encoded upstream responses are not BGFT-safe\n", {
      status: 502,
      headers: { "Cache-Control": "no-store" }
    });
  }

  const h = pkgHeaders();
  for (const name of ["etag", "last-modified"]) {
    const value = upstream.headers.get(name);
    if (value) h.set(name, value);
  }

  if (requestedRange) {
    if (upstream.status !== 206) {
      try { await upstream.body?.cancel(); } catch (_) {}
      return new Response(null, {
        status: 502,
        headers: { "Accept-Ranges": "bytes", "Cache-Control": "no-store" }
      });
    }

    const cr = parseContentRange(upstream.headers.get("content-range"));
    if (!cr ||
        cr.start !== requestedRange.start ||
        (requestedRange.end !== null && cr.end > requestedRange.end)) {
      try { await upstream.body?.cancel(); } catch (_) {}
      return new Response(null, {
        status: 502,
        headers: { "Accept-Ranges": "bytes", "Cache-Control": "no-store" }
      });
    }

    h.set("Content-Range", "bytes " + cr.start + "-" + cr.end + "/" + cr.total);
    h.set("Content-Length", String(cr.end - cr.start + 1));
    h.set("X-Tayson-Source-Range", "verified");
    return new Response(upstream.body, { status: 206, headers: h });
  }

  if (upstream.status !== 200 && upstream.status !== 206) {
    const status = upstream.status;
    try { await upstream.body?.cancel(); } catch (_) {}
    return new Response(null, {
      status,
      headers: { "Cache-Control": "no-store" }
    });
  }

  const length = Number(upstream.headers.get("content-length"));
  if (Number.isSafeInteger(length) && length >= 0)
    h.set("Content-Length", String(length));

  const cr = parseContentRange(upstream.headers.get("content-range"));
  if (upstream.status === 206 && cr) {
    h.set("Content-Range", "bytes " + cr.start + "-" + cr.end + "/" + cr.total);
    h.set("Content-Length", String(cr.end - cr.start + 1));
  }

  return new Response(upstream.body, { status: upstream.status, headers: h });
}

function health(env) {
  const routesReady = !!(env.ROUTES &&
    typeof env.ROUTES.get === "function" &&
    typeof env.ROUTES.put === "function");

  return [
    "Tayson Universal Transport Worker " + VERSION + ": OK",
    "PROFILE: " + PROFILE,
    "TARGET: PS4 firmware 9.x-12.x with GoldHEN BinLoader 9090",
    "ROUTES: " + (routesReady ? "OK" : "MISSING"),
    "PROVIDERS: AkiraBox, Rootz, Mocha, FileKeeper, VikingFile",
    "HEAD_EMULATION: RANGE_0_0",
    "RANGE_VALIDATION: STRICT_SINGLE_RANGE",
    "CONTENT_ENCODING: IDENTITY_ONLY",
    "REDIRECT_POLICY: SAME_APPROVED_PROVIDER",
    "AKIRA_REGIONAL_HOSTS: ON",
    "SIGNED_URL_PRESERVATION: ON",
    "TOKEN_LOGGING: OFF"
  ].join("\n") + "\n";
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/" || url.pathname === "/health") {
      return new Response(health(env), {
        status: 200,
        headers: {
          "Content-Type": "text/plain; charset=utf-8",
          "Cache-Control": "no-store",
          "X-Tayson-Worker-Version": VERSION
        }
      });
    }

    const routeApis = {
      "/api/routes": "r2",
      "/api/viking-routes": "viking",
      "/api/rootz-routes": "rootz",
      "/api/akira-routes": "akira",
      "/api/mocha-routes": "mocha",
      "/api/filek-routes": "filek"
    };

    if (routeApis[url.pathname]) {
      if (request.method !== "POST")
        return new Response("Method not allowed\n", {
          status: 405,
          headers: { "Allow": "POST", "Cache-Control": "no-store" }
        });
      return createRoute(request, env, url.origin, routeApis[url.pathname]);
    }

    let route;
    try {
      route = await resolveRoute(url, env);
    } catch (e) {
      return new Response((e.message || "invalid route") + "\n", {
        status: e.status || 400,
        headers: { "Cache-Control": "no-store" }
      });
    }

    console.log(JSON.stringify({
      version: VERSION,
      profile: PROFILE,
      kind: route.kind,
      method: request.method,
      path: url.pathname,
      hasRange: !!request.headers.get("range")
    }));

    return servePkg(request, route);
  }
};
