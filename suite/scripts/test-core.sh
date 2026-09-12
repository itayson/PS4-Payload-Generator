#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
python3 tools/validate_project.py
python3 tools/test_payload_protocol.py
