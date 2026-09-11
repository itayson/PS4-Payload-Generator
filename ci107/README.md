# Tayson PS4 Receiver 1.0.7 TLS callback test

This patch adds a template-scoped `sceHttpsSetSslCallback` acceptance callback for Receiver upstream HTTPS requests and keeps the existing `sceHttpsDisableOption` compatibility path.

Purpose: work around PS4 `SCE_SSL_ERROR_UNKNOWN_CA (0x8095F00C)` for modern CDN certificate chains while keeping the behavior limited to Receiver-created upstream templates.
