import assert from "node:assert/strict";
import { pathToFileURL } from "node:url";

const workerPath = process.argv[2];
assert.ok(workerPath, "worker module path required");
const mod = await import(pathToFileURL(workerPath).href + "?t=" + Date.now());
const worker = mod.default;
assert.equal(typeof worker?.fetch, "function");

const store = new Map();
const env = {
  ROUTES: {
    async put(key, value) { store.set(key, value); },
    async get(key) { return store.get(key) ?? null; }
  }
};

const account = "04b3d96d52475741e6b10f97f0a84a16";
const now = new Date(Date.now() - 30_000);
const amzDate =
  now.getUTCFullYear().toString().padStart(4, "0") +
  (now.getUTCMonth() + 1).toString().padStart(2, "0") +
  now.getUTCDate().toString().padStart(2, "0") + "T" +
  now.getUTCHours().toString().padStart(2, "0") +
  now.getUTCMinutes().toString().padStart(2, "0") +
  now.getUTCSeconds().toString().padStart(2, "0") + "Z";

function r2(prefix) {
  return "https://" + prefix + "." + account + ".r2.cloudflarestorage.com/game.pkg" +
    "?X-Amz-Content-Sha256=UNSIGNED-PAYLOAD" +
    "&X-Amz-Algorithm=AWS4-HMAC-SHA256" +
    "&X-Amz-Credential=test%2F20990101%2Fauto%2Fs3%2Faws4_request" +
    "&X-Amz-Date=" + amzDate +
    "&X-Amz-SignedHeaders=host" +
    "&X-Amz-Expires=3600" +
    "&X-Amz-Signature=" + "a".repeat(64);
}

async function body(response) {
  return (await response.text()).trim();
}

let response = await worker.fetch(new Request("https://worker.test/health"), env);
assert.equal(response.status, 200);
const health = await body(response);
assert.match(health, /v12-bgft-compat-9-12/);
assert.match(health, /ps4-bgft-fw-9-12/);
assert.match(health, /RANGE_VALIDATION: STRICT_SINGLE_RANGE/);

response = await worker.fetch(new Request("https://worker.test/api/viking-routes", {
  method: "POST",
  body: r2("vikingfile")
}), env);
assert.equal(response.status, 200);
const vikingRoute = await body(response);
assert.match(vikingRoute, /^https:\/\/worker\.test\/v\/[a-f0-9]{32}\.pkg$/);

response = await worker.fetch(new Request("https://worker.test/api/rootz-routes", {
  method: "POST",
  body: r2("rootz-files")
}), env);
assert.equal(response.status, 200);
const rootzRoute = await body(response);
assert.match(rootzRoute, /^https:\/\/worker\.test\/z\/[a-f0-9]{32}\.pkg$/);

let lastUpstream = null;
globalThis.fetch = async (url, init) => {
  lastUpstream = { url: String(url), headers: new Headers(init?.headers || {}) };
  const range = lastUpstream.headers.get("range");
  if (range === "bytes=0-0") {
    return new Response(new Uint8Array([0]), {
      status: 206,
      headers: {
        "Content-Range": "bytes 0-0/4096",
        "Content-Length": "1",
        "Content-Type": "application/octet-stream",
        "Accept-Ranges": "bytes",
        "ETag": "\"fixture\""
      }
    });
  }
  if (range === "bytes=100-199") {
    return new Response(new Uint8Array(100), {
      status: 206,
      headers: {
        "Content-Range": "bytes 100-199/4096",
        "Content-Length": "100",
        "Content-Type": "application/octet-stream"
      }
    });
  }
  return new Response(new Uint8Array(4096), {
    status: 200,
    headers: {
      "Content-Length": "4096",
      "Content-Type": "application/octet-stream"
    }
  });
};

response = await worker.fetch(new Request(vikingRoute, { method: "HEAD" }), env);
assert.equal(response.status, 200);
assert.equal(response.headers.get("content-length"), "4096");
assert.equal(response.headers.get("accept-ranges"), "bytes");
assert.equal(response.headers.get("x-tayson-bgft-profile"), "ps4-bgft-fw-9-12");
assert.equal(lastUpstream.headers.get("accept-encoding"), "identity");
assert.equal(lastUpstream.headers.get("range"), "bytes=0-0");

response = await worker.fetch(new Request(vikingRoute, {
  headers: { Range: "bytes=100-199" }
}), env);
assert.equal(response.status, 206);
assert.equal(response.headers.get("content-range"), "bytes 100-199/4096");
assert.equal(response.headers.get("content-length"), "100");
assert.equal(response.headers.get("x-tayson-source-range"), "verified");

globalThis.fetch = async () => new Response(new Uint8Array(100), {
  status: 206,
  headers: {
    "Content-Range": "bytes 101-200/4096",
    "Content-Length": "100"
  }
});
response = await worker.fetch(new Request(vikingRoute, {
  headers: { Range: "bytes=100-199" }
}), env);
assert.equal(response.status, 502, "mismatched Content-Range must be rejected");

globalThis.fetch = async () => new Response(null, {
  status: 302,
  headers: { Location: "https://evil.example/game.pkg" }
});
response = await worker.fetch(new Request(vikingRoute), env);
assert.equal(response.status, 502, "cross-provider redirects must be rejected");

const akiraUrl = "https://eeur.akirabox.com/game.pkg?access=" +
  String(Date.now() + 3600_000) + "." + "A".repeat(24);
response = await worker.fetch(new Request("https://worker.test/api/akira-routes", {
  method: "POST",
  headers: { "Content-Type": "application/x-www-form-urlencoded" },
  body: new URLSearchParams({
    url: akiraUrl,
    cookie: "session=fixture",
    referer: "https://akirabox.to/",
    userAgent: "fixture-agent"
  })
}), {});
assert.equal(response.status, 503, "authenticated Akira route must require KV");

response = await worker.fetch(new Request("https://worker.test/api/akira-routes", {
  method: "POST",
  headers: { "Content-Type": "application/x-www-form-urlencoded" },
  body: new URLSearchParams({
    url: akiraUrl,
    cookie: "session=fixture",
    referer: "https://akirabox.to/",
    userAgent: "fixture-agent"
  })
}), env);
assert.equal(response.status, 200);
assert.match(await body(response), /^https:\/\/worker\.test\/a\/[a-f0-9]{32}\.pkg$/);

console.log("RESULT: Worker v12 self-test passed");
