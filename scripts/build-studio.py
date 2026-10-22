#!/usr/bin/env python3
"""Build the Studio fork in a private workspace using upstream's pinned runtime."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import zipfile

BASE_SHA256 = 'a441a8321edba13ebd2597863477c409004e7d4475b9221951bf2991e2feee81'

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-apk', required=True, type=Path)
    parser.add_argument('--tools', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    if platform.system() != 'Linux' or platform.machine() not in ('x86_64', 'amd64'):
        parser.error('APK packaging requires Linux x86_64 with Java 17, javac, keytool and liblz4; use the manual Studio GitHub Actions workflow. ARM64 source checks are separate.')
    for command in ('java', 'javac', 'keytool'):
        if not shutil.which(command):
            parser.error('Missing build dependency: ' + command)
    if hashlib.sha256(args.base_apk.read_bytes()).hexdigest() != BASE_SHA256:
        parser.error('Wrong runtime base. Do not use the patched 3.4.3 APK as an unpatched build base.')
    root = Path(__file__).resolve().parents[1]
    source = root / 'payload-generator/3.2.0/UPDATE'
    output = args.output.resolve()
    tools = args.tools.resolve()
    output.mkdir(parents=True, exist_ok=True)
    env = {**os.environ, 'DOTNET_CLI_TELEMETRY_OPTOUT':'1', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE':'1'}
    def run(name, *command):
        with (output / (name + '.log')).open('w') as log:
            subprocess.run([str(c) for c in command], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
        print(name + ': passed', flush=True)
    with tempfile.TemporaryDirectory(prefix='tayson-studio-') as temporary:
        base = Path(temporary) / 'runtime'
        base.mkdir()
        with zipfile.ZipFile(args.base_apk) as archive:
            for member in archive.infolist():
                path = Path(member.filename)
                if path.is_absolute() or '..' in path.parts or (member.external_attr >> 16) & 0o170000 == 0o120000:
                    raise RuntimeError('Unsafe runtime archive member')
            archive.extractall(base)
        project = base / 'UPDATE'
        shutil.copytree(source, project, ignore=shutil.ignore_patterns('bin','obj','build','signing','__pycache__'))
        backup = project / 'base'
        backup.mkdir(exist_ok=True)
        for src, dst in [('assets/template.elf','original-template.elf'), ('assemblies/assemblies.blob','original-assemblies.blob'), ('AndroidManifest.xml','original-AndroidManifest.xml')]:
            shutil.copy2(base / src, backup / dst)
        run('template', 'python3', project/'scripts/patch_diagnostics.py')
        run('prepare', 'python3', project/'scripts/prepare.py', '--base', base, '--tools', tools)
        dotnet = tools/'dotnet/dotnet'
        run('core-tests', dotnet, 'run', '--project', project/'tests/Tests.csproj', '--', base/'assets/template.elf')
        run('studio-tests', dotnet, 'run', '--project', root/'studio-tests/Studio.Tests.csproj')
        run('build', 'python3', project/'scripts/build.py', '--base', base, '--tools', tools, '--output', output)
        run('identity', 'python3', project/'scripts/apply_release_identity.py',
            '--apk', project/'build/aligned.apk', '--icon-source', project/'assets/tayson-launcher.jpg',
            '--output', output/'Tayson-PS4-Studio-1.0.1.apk',
            '--unsigned-output', output/'Studio-aligned-unsigned.apk', '--tools', tools,
            '--keystore', project/'signing/tayson-test.jks', '--build-info', output/'build-info.json')
    artifact = output/'Tayson-PS4-Studio-1.0.1.apk'
    (output/'SHA256SUMS.txt').write_text(hashlib.sha256(artifact.read_bytes()).hexdigest() + '  ' + artifact.name + '\n')
    metadata = json.loads((output/'build-info.json').read_text())
    if metadata['package'] != 'com.tayson.ps4studio':
        raise RuntimeError('Studio package identity was not applied')
    print('Test APK built and signature verified. Physical Android/PS4 testing remains required.')

if __name__ == '__main__':
    main()
