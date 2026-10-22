#!/usr/bin/env python3
"""Fetch the same public, hash-pinned runtime base used by upstream v3.4.3."""
from html.parser import HTMLParser
from pathlib import Path
import hashlib
import sys
import urllib.request
from urllib.parse import urlparse

EXPECTED = 'a441a8321edba13ebd2597863477c409004e7d4475b9221951bf2991e2feee81'
PAGE = 'https://www.mediafire.com/file/hi4h13wzunr2bjm/phoenixx.ps4remotepkg_v3.0%2B%282%29.apk/file'

class DownloadLink(HTMLParser):
    link = None
    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == 'a' and attrs.get('id') == 'downloadButton':
            self.link = attrs.get('href')

def main():
    output = Path(sys.argv[1])
    request = urllib.request.Request(PAGE, headers={'User-Agent':'Mozilla/5.0'})
    with urllib.request.urlopen(request, timeout=30) as response:
        html = response.read(2 * 1024 * 1024 + 1)
    if len(html) > 2 * 1024 * 1024:
        raise RuntimeError('Download page exceeds expected size')
    parser = DownloadLink()
    parser.feed(html.decode('utf-8', errors='replace'))
    url = urlparse(parser.link or '')
    if url.scheme != 'https' or not url.hostname or not url.hostname.endswith('.mediafire.com') or not url.hostname.split('.')[0].startswith('download'):
        raise RuntimeError('No public MediaFire download button available. No CAPTCHA or access restriction is bypassed.')
    output.parent.mkdir(parents=True, exist_ok=True)
    hasher = hashlib.sha256()
    length = 0
    with urllib.request.urlopen(parser.link, timeout=60) as response, output.open('wb') as target:
        while block := response.read(1024 * 1024):
            length += len(block)
            if length > 100 * 1024 * 1024:
                raise RuntimeError('Runtime base exceeds expected maximum')
            hasher.update(block)
            target.write(block)
    if hasher.hexdigest() != EXPECTED:
        raise RuntimeError('Runtime base hash mismatch; build must stop')
    print('Verified inherited runtime SHA-256:', EXPECTED)

if __name__ == '__main__':
    main()
