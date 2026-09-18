# Cloudflare Worker v9 — BGFT transport hardening

This Worker is an opt-in successor to `universal_transport_worker_v8.js`.
The v8 source remains untouched so the hardware-validated baseline can always
be restored.

## Why v9

Two Cloudflare platform behaviors are relevant to PS4 BGFT delivery:

1. A manually-set `Content-Length` is not authoritative when a Worker returns
   an arbitrary `ReadableStream`. Cloudflare can frame that response with
   chunked transfer encoding. v9 wraps known-length upstream bodies in
   `FixedLengthStream`, so the runtime itself emits the correct length.
2. Workers KV is eventually consistent. A route created by Android can be read
   immediately by the PS4 from a different Cloudflare location before the KV
   write has propagated. v9 supports a SQLite-backed Durable Object binding
   named `ROUTE_STORE` and prefers it over KV.

## Transport changes

- `FixedLengthStream` for GET/206 when the upstream response length is known.
- No manual `Content-Length` on streamed GET responses.
- `cache: "no-store"` on upstream fetches.
- `Accept-Encoding: identity` for all PKG upstream requests.
- Strict Range behavior:
  - a client Range request must receive upstream HTTP 206;
  - HTTP 206 without `Content-Range` is rejected.
- `Cache-Control: no-store, no-transform` for PKG responses.
- Existing `Content-Range`, `ETag`, `Last-Modified` and content type are
  preserved.
- HEAD continues to probe with `bytes=0-0` so providers that do not implement
  HEAD correctly still expose the real package size to BGFT.

## Route storage

Storage priority:

1. `ROUTE_STORE` Durable Object — strongly consistent, recommended.
2. `ROUTES` Workers KV — backward-compatible fallback, eventually consistent.
3. Inline fallback for routes that can safely operate without stored session
   context.

The Durable Object stores one short-lived route per object and schedules an
alarm for automatic cleanup at route expiry.

## Health endpoint

`GET /health` now reports:

```text
Tayson Payload Generator Universal Transport Worker v9-bgft-fixed-length: OK
ROUTE_STORE: DURABLE_OBJECT
FIXED_LENGTH_STREAM: ON
STRICT_RANGE: ON
COLO: <cloudflare colo>
```

If Durable Objects are not configured but KV is present, the second line reads
`ROUTE_STORE: KV_EVENTUAL`.

## Deployment

Keep the existing public Worker hostname if possible so the Android application
does not need a Worker URL change.

For a Wrangler deployment, copy `wrangler-v9.toml.example` next to the Worker,
fill in the existing KV namespace ID only if you want the fallback, and deploy.
The Durable Object migration uses `new_sqlite_classes`, which is the required
backend for newly-created Durable Objects on current Cloudflare accounts.

Do not replace the production v8 deployment until the v9 health endpoint and a
small Range request are validated.
