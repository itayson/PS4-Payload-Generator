# Universal BGFT provider routing

This change keeps provider resolution and BGFT transport as separate stages.

## Rules

1. Validate the real provider URL first with `PkgScanner`.
2. Only after validation, call `BgftRoutePlanner.prepare(provider, finalUrl)`.
3. Direct `.pkg` URLs remain unchanged.
4. Query-signed URLs whose HTTP path already ends in `.pkg` receive `#tayson.pkg`.
   This preserves the HTTP request while satisfying the local BGFT extension check.
5. VikingFile R2 presigned URLs are routed through the Cloudflare Worker.
6. The app first requests a short Worker route (`POST /api/routes`). If Worker v5/KV
   is not available, it falls back to the v4-compatible inline Base64URL route.
7. Browser cookies/referers are removed from the transport copy when the Worker is
   used, because the Worker only needs the R2 presigned URL.
8. Temporary provider URLs are rejected when they are expired or within 90 seconds
   of expiry.

## Cloudflare deployment

Deploy `cloudflare/vikingfile_worker_v5.js` at:

`https://drive.itaysondaniel.workers.dev`

Optional but recommended: bind a Workers KV namespace as `ROUTES`. With KV, the
PS4 receives a short URL such as:

`https://drive.itaysondaniel.workers.dev/d/<token>.pkg#tayson.pkg`

Without KV, v5 still works in inline mode:

`https://drive.itaysondaniel.workers.dev/viking.pkg?u=<base64url>#tayson.pkg`

The Worker validates that every target is HTTPS, belongs to a
`vikingfile.*.r2.cloudflarestorage.com` host and contains AWS SigV4 presign fields.
It is not a generic open proxy.

## Hardware evidence behind the rules

- Mocha: `0x80990033` changed to `Já instalado` after preserving the authenticated
  query and appending `#tayson.pkg`.
- VikingFile: direct R2 returned `0x80991403`; routing through the Worker created the
  PS4 download task successfully.
