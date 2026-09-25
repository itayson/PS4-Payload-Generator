# Tayson 4.0 — Worker Pull Engine

Status: experimental, private branch.

## Goal

Remove BGFT from the transport path and remove Android from the data path.

Android must never store the PKG and must not remain open while the PS4 downloads.

## Flow

1. Android resolves and validates the provider link.
2. Android asks the Worker for one short-lived normalized job URL.
3. Android sends only the small job descriptor to the resident PS4 service.
4. PS4 acknowledges the job. Android may close immediately.
5. PS4 downloads directly from the Worker using native HTTP(S).
6. Worker streams provider bytes and forwards Range requests; it does not buffer the whole PKG.
7. PS4 stores only a temporary/resumable file on its own disk.
8. PS4 validates size, CNT magic and package metadata.
9. Local install is started through AppInstUtil after validation.

## Worker responsibilities

- provider authentication/context isolation;
- signed URL normalization;
- short-lived opaque job routes;
- HTTP Range passthrough;
- stable Content-Length/Content-Range;
- no full-PKG buffering;
- no public logging of source URLs, cookies or tokens.

## PS4 responsibilities

- resident job queue;
- native HTTP(S) pull;
- retry and Range resume;
- local preallocation before writing PKG data;
- package validation;
- installation;
- persistent status independent of Android.

## Android responsibilities

- provider/browser flow;
- metadata validation;
- create Worker job;
- submit tiny descriptor to PS4;
- optional status viewer.

Android is not a proxy and is not part of the transfer after job acceptance.

## Compatibility rule

The 3.4.3 BGFT baseline and the validated 4.0 Alpha 1 Receiver remain untouched.
The rejected phone-stream Alpha 2 is not the basis for this line.
