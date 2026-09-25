# Tayson 3.4.3 — compatibility contract

This document defines the product behavior that future transport/BGFT work must preserve.

## Frozen product surface

The stable 3.4.3 user experience is the compatibility baseline:

- Home flow remains one-touch: paste URL -> validate PKG -> resolve provider -> metadata/artwork -> generate ELF in memory -> send to GoldHEN BinLoader on TCP 9090 -> BGFT.
- Do not require Android file picking or saving the generated ELF.
- Do not require a separate Remote Package Installer application on the PS4.
- Manual generate/inject controls remain available as fallback.
- Saved PS4 IP, local history, metadata and artwork/gallery behavior remain intact.
- Supported provider behavior remains intact: Mocha, VikingFile, FileKeeper/dlproxy, Rootz, AkiraBox and direct HTTP(S) PKG links.
- The published Worker remains a transport adapter for providers that need stable/short BGFT routes. It must not become a permanent phone-side proxy.
- The PS4 remains responsible for downloading the PKG after registration.

## Change boundary

Transport experiments must live behind the existing 3.4.3 pipeline. A change is acceptable only when it does not require a user-visible workflow change.

The following are intentionally out of scope unless separately approved:

- replacing BinLoader :9090 with an RPI :12800 workflow;
- removing provider resolvers or Worker routing;
- redesigning Home/Settings;
- changing metadata, artwork or history semantics;
- saving ELF files as a required step;
- making the Android device relay the full PKG download.

## BGFT transport seam

The existing application-side contract is:

1. `Integration.ResolveAsync` resolves the provider URL.
2. `DownloadClient` validates the PKG and reads bounded metadata.
3. `BgftRoutePlanner` converts only the final transport URL when required.
4. `PayloadPatcher` embeds URL/content metadata into the packaged ELF template.
5. `UiAutoPipeline` sends that ELF once to GoldHEN BinLoader TCP 9090.
6. The payload performs the PS4-side registration/start operation.

Therefore BGFT research should change step 6 first. Steps 1–5 are compatibility boundaries, not redesign targets.

## Regression gate

Before a transport change is promoted:

- existing provider and URL-preservation tests must pass;
- ELF patching must remain fixed-size and non-destructive;
- automatic sending must not replay an ELF after a successful TCP connection;
- direct PKG links must continue to bypass the Worker when no adapter is needed;
- provider context must survive resolver host changes;
- the final candidate must be validated on a real PS4 before being called stable.

The stable 3.4.3 implementation remains the fallback until the replacement transport passes these checks.
