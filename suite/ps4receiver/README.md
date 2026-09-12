# Tayson PS4 Receiver 1.0

Homebrew PS4 companion for Tayson PS4 Link Sender.

- TCP control: `0.0.0.0:9095` (private/LAN IPv4 clients only)
- Local BGFT proxy: `127.0.0.1:32112`
- The Android app sends only URL, HTTP context and PKG metadata.
- PKG bytes travel directly from the Internet to the PS4.
- HTTP Range is forwarded so BGFT can resume partial downloads.

The app is built with the OpenOrbis toolchain as an installable fake PKG.
