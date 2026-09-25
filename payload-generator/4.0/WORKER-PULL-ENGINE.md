# Tayson 4.0 — Native AppInst Handoff

Status: experimental, private branch.

## Goal

Keep the original one-touch experience without BGFT and without installing a
persistent application/service on the PS4.

Android must never store or proxy the PKG. After the native installer accepts
the Worker URL, Android is no longer in the data path.

## Alpha 4 hypothesis

The PS4 exports `sceAppInstUtilInstallByPackage` in `libSceAppInstUtil`.
Public PS4 symbol/header sets expose the symbol, but public projects commonly
fall back to BGFT on Orbis. Therefore URL consumption on the target PS4
firmware is **not assumed to work**: Alpha 4 is a hardware probe.

## Flow

1. Android resolves and validates the provider link.
2. Android creates a short-lived opaque Worker route.
3. Android sends a temporary one-shot ELF to GoldHEN BinLoader 9090.
4. The ELF loads `libSceAppInstUtil.sprx`, calls
   `sceAppInstUtilInitialize()`, then calls
   `sceAppInstUtilInstallByPackage()` with the Worker HTTPS URL.
5. If the native service accepts the request, the ELF returns
   `ACCEPTED APPINST` and exits.
6. Android may close only after that native acceptance.

No Receiver application is installed on the PS4. The ELF exists only in the
current runtime and disappears on reboot.

## Worker responsibilities

- provider authentication/context isolation;
- signed URL normalization;
- short-lived opaque job routes;
- HTTP Range passthrough;
- stable Content-Length/Content-Range;
- no full-PKG buffering;
- no public logging of source URLs, cookies or tokens.

## Android responsibilities

- provider/browser flow;
- metadata validation;
- create Worker job;
- bootstrap the temporary ELF;
- hand off the short Worker URL;
- display the native return code.

Android is not a download proxy.

## Hardware result interpretation

- `ACCEPTED APPINST`: PS4 accepted the native URL handoff.
- `ERR APPINST_INIT 0x........`: initialization failed.
- `ERR APPINST 0x........`: the native URL install call was rejected.
- no response after BinLoader receive: payload/runtime problem before handoff.

The exact return code is intentionally surfaced instead of silently falling
back to BGFT.

## Compatibility rule

The validated 3.4.3 BGFT baseline and 4.0 Alpha 1 handshake remain untouched.
The phone-stream Alpha 2 is not the basis for this line.
