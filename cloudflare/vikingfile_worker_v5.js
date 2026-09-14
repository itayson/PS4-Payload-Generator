const VERSION = "v5-universal";
const VIKING_PREFIX = "vikingfile.";
const R2_SUFFIX = ".r2.cloudflarestorage.com";
const MAX_URL_BYTES = 16384;
const MIN_ROUTE_TTL = 90;
const MAX_ROUTE_TTL = 86400;

function json(data, status = 200, extra = {}) {
  return new Response(JSON.stringify(data), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Tayson-Worker-Version": VERSION,
      ...extra,
    },
  });
}

function decodeBase64Url(value) {
  if (!value || value.length > MAX_URL_BYTES * 2) throw new Error("invalid encoded URL");
  let b64 = value.replace(/-/g, "+").replace(/_/g, "/");
  while (b64.length % 4) b64 += "=";
  const binary = atob(b64);
  const bytes = Uint8Array.from(binary, c => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

function encodeBase64Url(value) {
  const bytes = new TextEncoder().encode(value);
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function parseAmzDate(value) {
  if (!/^\d{8}T\d{6}Z$/.test(value || "")) return NaN;
  const iso = `${value.slice(0, 4)}-${value.slice(4, 6)}-${value.slice(6, 8)}T${value.slice(9, 11)}:${value.slice(11, 13)}:${value.slice(13, 15)}Z`;
  return Date.parse(iso);
}

function validateVikingUrl(raw) {
  if (!raw || new TextEncoder().encode(raw).length > MAX_URL_BYTES) {
    throw new Error("URL too large");
  }
  const target = new URL(raw);
  const host = target.hostname.toLowerCase();
  if (target.protocol !== "https:") throw new Error("only HTTPS is allowed");
  if (!host.startsWith(VIKING_PREFIX) || !host.endsWith(R2_SUFFIX)) {
    throw new Error("host is not a VikingFile R2 endpoint");
  }
  if (target.searchParams.get("X-Amz-Algorithm") !== "AWS4-HMAC-SHA256" ||
      !target.searchParams.has("X-Amz-Signature") ||
      !target.searchParams.has("X-Amz-Date") ||
      !target.searchParams.has("X-Amz-Expires")) {
    throw new Error("missing R2 presign fields");
  }

  const startMs = parseAmzDate(target.searchParams.get("X-Amz-Date"));
  const lifetime = Number(target.searchParams.get("X-Amz-Expires"));
  if (!Number.isFinite(startMs) || !Number.isFinite(lifetime) || lifetime <= 0) {
    throw new Error("invalid R2 expiry");
  }
  const expiresAtMs = startMs + lifetime * 1000;
  const ttl = Math.floor((expiresAtMs - Date.now()) / 1000);
  if (ttl <= MIN_ROUTE_TTL) throw new Error("R2 URL expired or too close to expiry");

  // Validate with URL, but preserve the exact signed string. Re-serializing a
  // presigned URL can change escaping/canonicalization and invalidate SigV4.
  return {
    url: raw,
    expiresAtMs,
    ttl: Math.min(ttl, MAX_ROUTE_TTL),
  };
}

function commonHeaders() {
  return new Headers({
    "Content-Type": "application/octet-stream",
    "Content-Disposition": 'attachment; filename="download.pkg"',
    "Accept-Ranges": "bytes",
    "Cache-Control": "no-store",
    "X-Tayson-Worker-Version": VERSION,
  });
}

function totalFromContentRange(value) {
  if (!value) return null;
  const m = /\/(\d+)$/.exec(value.trim());
  return m ? Number(m[1]) : null;
}

async function probeSize(upstream) {
  const r = await fetch(upstream, {
    method: "GET",
    headers: { Range: "bytes=0-0" },
    redirect: "follow",
  });

  if (r.status !== 200 && r.status !== 206) {
    const status = r.status;
    try { await r.body?.cancel(); } catch (_) {}
    return { ok: false, status };
  }

  let size = totalFromContentRange(r.headers.get("content-range"));
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
    lastModified: r.headers.get("last-modified"),
  };
  try { await r.body?.cancel(); } catch (_) {}
  return meta;
}

async function resolveUpstream(url, env) {
  const tokenMatch = /^\/d\/([A-Za-z0-9_-]{16,64})\.pkg$/i.exec(url.pathname);
  if (tokenMatch) {
    if (!env.ROUTES) throw new Error("KV binding ROUTES is not configured");
    const stored = await env.ROUTES.get(tokenMatch[1]);
    if (!stored) throw new Error("route not found or expired");
    return validateVikingUrl(stored).url;
  }

  if (url.pathname.toLowerCase() === "/viking.pkg") {
    return validateVikingUrl(decodeBase64Url(url.searchParams.get("u"))).url;
  }

  throw new Error("route not found");
}

async function createRoute(request, env) {
  const contentLength = Number(request.headers.get("content-length") || "0");
  if (contentLength > MAX_URL_BYTES + 1024) return json({ error: "request too large" }, 413);

  let payload;
  try {
    const raw = await request.text();
    if (new TextEncoder().encode(raw).length > MAX_URL_BYTES + 1024) return json({ error: "request too large" }, 413);
    payload = JSON.parse(raw);
  } catch (_) {
    return json({ error: "invalid JSON" }, 400);
  }

  let target;
  try {
    target = validateVikingUrl(payload?.url);
  } catch (e) {
    return json({ error: e.message }, 400);
  }

  const origin = new URL(request.url).origin;
  if (!env.ROUTES) {
    return json({
      mode: "inline",
      url: `${origin}/viking.pkg?u=${encodeBase64Url(target.url)}#tayson.pkg`,
      expiresAt: Math.floor(target.expiresAtMs / 1000),
    });
  }

  const token = crypto.randomUUID().replace(/-/g, "");
  await env.ROUTES.put(token, target.url, { expirationTtl: target.ttl });
  return json({
    mode: "kv",
    url: `${origin}/d/${token}.pkg#tayson.pkg`,
    expiresAt: Math.floor(target.expiresAtMs / 1000),
  });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (url.pathname === "/" || url.pathname === "/health") {
      return json({
        ok: true,
        version: VERSION,
        kv: Boolean(env.ROUTES),
      });
    }

    if (url.pathname === "/api/routes") {
      if (request.method !== "POST") {
        return new Response("Method not allowed\n", {
          status: 405,
          headers: { Allow: "POST" },
        });
      }
      return createRoute(request, env);
    }

    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("Method not allowed\n", {
        status: 405,
        headers: { Allow: "GET, HEAD" },
      });
    }

    let upstream;
    try {
      upstream = await resolveUpstream(url, env);
    } catch (e) {
      return json({ error: e.message }, e.message.includes("not found") ? 404 : 400);
    }

    console.log(JSON.stringify({
      version: VERSION,
      method: request.method,
      path: url.pathname,
      range: request.headers.get("range"),
    }));

    if (request.method === "HEAD") {
      const probe = await probeSize(upstream);
      if (!probe.ok) {
        return new Response(null, {
          status: probe.status || 502,
          headers: { "X-Tayson-Worker-Version": VERSION },
        });
      }
      const h = commonHeaders();
      h.set("Content-Length", String(probe.size));
      if (probe.contentType) h.set("Content-Type", probe.contentType);
      if (probe.etag) h.set("ETag", probe.etag);
      if (probe.lastModified) h.set("Last-Modified", probe.lastModified);
      return new Response(null, { status: 200, headers: h });
    }

    const forward = new Headers();
    for (const name of ["range", "if-range"]) {
      const value = request.headers.get(name);
      if (value) forward.set(name, value);
    }

    const r = await fetch(upstream, {
      method: "GET",
      headers: forward,
      redirect: "follow",
    });

    const h = commonHeaders();
    for (const name of ["content-length", "content-range", "content-type", "etag", "last-modified"]) {
      const value = r.headers.get(name);
      if (value) h.set(name, value);
    }
    h.set("X-Upstream-Status", String(r.status));
    return new Response(r.body, { status: r.status, headers: h });
  },
};
