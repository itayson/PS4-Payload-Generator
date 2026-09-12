# Tayson PS4 Payload Installer 0.1.0

Experimental payload backend for the Tayson PS4 Link Sender. It is an additional installation engine and **does not replace the BGFT Receiver**.

## Goal

Use a small `.elf`/`.bin` payload on the PS4 to stage a PKG locally and submit that local file to `sceAppInstUtilAppInstallPkg`. The first prototype deliberately downloads through the Android relay (`http://<phone>:8787/...`) so the payload itself does not need to implement HTTPS, cookies, redirects or provider-specific headers.

Flow:

`provider/CDN -> Android Relay -> PS4 payload cache -> AppInstUtil local install`

The existing BGFT path remains available in parallel:

`provider/CDN -> Tayson Receiver -> BGFT`

## Ports

- `9095`: existing Tayson PS4 Receiver / BGFT backend
- `9096`: Tayson Payload Installer control service
- `8787`: Android relay HTTP server

## Protocol

Ping:

```text
TAYSONP/1 PING\r\n\r\n
```

Start one staging/install job:

```text
TAYSONP/1 START\r\n
Url: http://192.168.1.20:8787/pkg/<token>/game.pkg\r\n
Size: 123456789\r\n
Name: game.pkg\r\n
Title: CUSA00000\r\n
\r\n
```

The URL must be plain HTTP because the intended source is the trusted LAN relay created by the Android app. The payload stores the file at `/data/tayson/payload-cache/current.pkg`, supports resuming with `Range`, optionally validates the Title ID, then calls the local AppInstUtil installer.

Status:

```text
TAYSONP/1 STATUS\r\n\r\n
```

Possible states: `idle`, `downloading`, `installing`, `submitted`, `error`, `cancelled`.

Other commands:

- `TAYSONP/1 STOP` — cancel the active staging download.
- `TAYSONP/1 RESET` — clear an inactive job and delete the cached PKG.
- `TAYSONP/1 EXIT` — terminate the payload service when idle.

## Build

This uses `ps4-payload-dev/sdk` and produces both:

- `Tayson-Payload-Installer-0.1.0.elf`
- `Tayson-Payload-Installer-0.1.0.bin`

The `.bin` is a stripped copy of the ELF payload, matching the format used by recent GoldHEN-compatible payload projects.

## Prototype limits

- One job at a time.
- Payload Direct HTTPS is not implemented yet; 0.1.0 intentionally uses the Android relay.
- Installation progress after AppInstUtil accepts the local PKG is not yet polled.
- The cached PKG is kept after submission so failed patch tests can be inspected/retried without redownloading.
