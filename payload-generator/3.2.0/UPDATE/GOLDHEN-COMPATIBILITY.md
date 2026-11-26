# GoldHEN compatibility — phase 1 (experimental)
Baseline: c88246469e8069ffbb173dde7315f892bf464e5b. Target: 3.5.0, not a release or compatibility certification.

## What changes
- One-touch uses a bounded TCP sender; 9090 and four pre-connect attempts remain defaults.
- After connecting, failures are ambiguous and must not trigger automatic replay.
- TCP completion does not confirm execution, BGFT registration, or installation.
- Settings accepts manually declared firmware and GoldHEN version per IP; advisory only, does not select a native binary.
- No changes to Worker, native ELF, artwork, history or provider resolution.
- PR CI produces a 3.5.0-alpha.1 candidate (versionCode 41) with test signing. It is not a stable update; verify signing before installation.
- Dependency audit remains mandatory: replace missing ContinueWith with a compatible asynchronous observer.

## Evidence / targets
GoldHEN official changelog: https://github.com/GoldHEN/GoldHEN/blob/master/CHANGELOG.md
Official releases: https://github.com/GoldHEN/GoldHEN/releases
Community archive (secondary): https://github.com/EEJTek/GoldHEN_Archived

1.x: no integrated BinLoader; external loader would require its own verification.
2.x legacy: transport/format verification required; no conversion from ELF to BIN is implied.
2.4b18.5–2.4b18.14: candidates only, based on archived notes; native ABI/BGFT compatibility is unproven.
Unknown releases: no automatic assumptions.

Record each exact firmware and GoldHEN build separately, starting at firmware 12.02.
For authorized homebrew content: verify loader enabled, local TCP send, observed execution on PS4,
BGFT task registration, transfer and completion. Record sanitized logs and template SHA256.
A TCP loopback fixture exercises bytes and EOF only; it is not a console test.
Retain the stable APK and do not install an unsigned or differently signed replacement over it.

## Build gate
The existing tests retain their original 103-check summary; additional GoldHenRegression checks
are mandatory before that summary and fail the run on exception. The existing PR workflow
builds from the pinned original APK; no stable release is published on a pull request.
