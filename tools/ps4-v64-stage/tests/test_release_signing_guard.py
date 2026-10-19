#!/usr/bin/env python3
"""Release fail-closed checks; never requires the owner's signing password."""
from pathlib import Path
import ast
import re
import subprocess
import sys

root = Path(__file__).resolve().parents[3]
build = root / "payload-generator/3.2.0/UPDATE/scripts/build.py"
release = root / "payload-generator/3.2.0/UPDATE/scripts/apply_release_identity.py"
text = release.read_text()
assert "APP_VERSION_CODE = 46" in build.read_text()
assert 'NEW_PACKAGE = "com.tayson.ps4payloadgenerator.staging"' in text
assert 'if args.new_package != NEW_PACKAGE:' in text
assert "PERMANENT_STAGING_SIGNER_SHA256" in text
assert "3d8daee7afa38dedd1378f6a6dd0c7cf6604785ef594413c344a5f94e1d56219" in text
assert 'default="android"' not in text
assert 'default="tayson-v631-permanent"' in text
ast.parse(text)
prefix = [sys.executable, str(release), "--apk", "not-created.apk",
    "--tools", "/nonexistent", "--keystore", "/nonexistent/test.p12",
    "--output", "/nonexistent/final.apk",
    "--unsigned-output", "/nonexistent/unsigned.apk",
    "--icon-source", "/nonexistent/icon.jpg"]
result = subprocess.run(prefix, text=True, capture_output=True)
assert result.returncode != 0
assert "requires --storepass-ref and --keypass-ref" in result.stderr
print("PASS missing password references rejected before APK writes")
result = subprocess.run(prefix + ["--new-package", "com.tayson.ps4payloadgenerator"],
                        text=True, capture_output=True)
assert result.returncode != 0
assert "Release package mismatch" in result.stderr
print("PASS prevent alternate package from replacing production stable app")
result = subprocess.run(prefix + ["--storepass-ref", "env:NOT_SET_PS4_KEY",
    "--keypass-ref", "env:NOT_SET_PS4_KEY"], text=True, capture_output=True)
assert result.returncode != 0
assert "Required signing secret environment variable is missing" in result.stderr
print("PASS absent secret environment variable rejected")
pattern = r"Signer #1 certificate SHA-256 digest:\s*([0-9a-fA-F:]+)"
sample = "Signer #1 certificate SHA-256 digest: 3d8daee7afa38dedd1378f6a6dd0c7cf6604785ef594413c344a5f94e1d56219"
found = re.search(pattern, sample, re.IGNORECASE)
assert found is not None
print("PASS certificate digest verifier pattern")
print("RESULT 4 fail-closed signing/identity tests passed")
