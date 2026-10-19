#!/usr/bin/env python3
"""Fail-closed Android APK signing identity gate for in-place v6.1 upgrades.

Input is a signed APK and the local official build-tools apksigner path.
Do not read private keystore contents, passwords, APK user data or secrets.
"""
import re
import subprocess
import sys
from pathlib import Path

EXPECTED_PACKAGE = "com.tayson.ps4payloadgenerator.staging"
EXPECTED_CERT_SHA256 = "c693d66ac7e2c95411c730aefa0c9da62a617586e9b3a5d57d0dd90ab8f62ec5"

def verify(apksigner: str, aapt: str, apk: str) -> None:
    if not Path(apk).is_file():
        raise ValueError("APK not found.")
    signed = subprocess.run([apksigner, "verify", "--verbose", "--print-certs", apk],
                            check=True, capture_output=True, text=True)
    fingerprints = re.findall(
        r"Signer #\d+ certificate SHA-256 digest:\s*([0-9a-f]{64})",
        signed.stdout, re.I)
    if len(fingerprints) != 1 or fingerprints[0].lower() != EXPECTED_CERT_SHA256:
        raise ValueError("Signing certificate mismatch: not an in-place v6.1 update.")
    for scheme in ("v2", "v3"):
        if f"Verified using {scheme} scheme" not in signed.stdout:
            raise ValueError("Required APK signature scheme missing.")

    identity = subprocess.run([aapt, "dump", "badging", apk], check=True,
                              capture_output=True, text=True)
    match = re.search(r"package: name='([^']+)' versionCode='(\d+)'", identity.stdout)
    if not match or match.group(1) != EXPECTED_PACKAGE or int(match.group(2)) < 43:
        raise ValueError("APK package or upgrade versionCode mismatch.")
    print("SIGNED_UPDATE_COMPATIBLE: expected package, versionCode and certificate verified")

if __name__ == "__main__":
    if len(sys.argv) != 4:
        print("Usage: verify_signing_identity.py /path/apksigner /path/aapt signed.apk", file=sys.stderr)
        sys.exit(2)
    try:
        verify(*sys.argv[1:])
    except (ValueError, subprocess.SubprocessError) as e:
        print("BLOCKED: " + str(e), file=sys.stderr)
        sys.exit(1)
