#!/usr/bin/env python3
"""Structural validation; behavior is tested in native and Gradle protocol tests.
Replaces obsolete 1.0.0 string assertions that prohibited the requested payload mode.
"""
from pathlib import Path
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
files=list((root/'app/src/main').rglob('*.xml'))
for f in files: ET.parse(f)
assert (root/'ps4receiver/receiver/main.c').is_file()
assert (root/'ps4payload/main.c').is_file()
print(f'{len(files)} Android XML files valid; Receiver and Payload sources present')
