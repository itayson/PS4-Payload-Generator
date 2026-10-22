#!/usr/bin/env python3
"""Compile against the operator's actual APK reference assemblies; no APK writes."""
import argparse
import os
from pathlib import Path
import subprocess

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--sdk', required=True, type=Path)
    parser.add_argument('--refs', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    sdk, refs, output = args.sdk.resolve(), args.refs.resolve(), args.output.resolve()
    if not (refs / 'PS4RemotePkgMobile.dll').is_file():
        parser.error('Missing APK reference assemblies: PS4RemotePkgMobile.dll')
    output.mkdir(parents=True, exist_ok=True)
    csc, = (sdk / 'sdk').glob('*/Roslyn/bincore/csc.dll')
    runtimes = sorted((sdk / 'shared/Microsoft.NETCore.App').glob('8.*'))
    if not runtimes:
        parser.error('The pinned .NET 8 runtime is required')
    env = {**os.environ, 'DOTNET_GCHeapHardLimit':'10000000', 'DOTNET_GCConserveMemory':'9',
           'DOTNET_gcServer':'0', 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT':'1',
           'DOTNET_CLI_TELEMETRY_OPTOUT':'1'}
    command = [str(sdk / 'dotnet'), str(csc), '/noconfig', '/nostdlib+', '/target:library',
               '/langversion:12', '/optimize+', '/out:' + str(output / 'Tayson.Providers.dll'),
               '/reference:' + str(runtimes[-1] / 'System.Private.CoreLib.dll')]
    command += ['/reference:' + str(p) for p in sorted(refs.glob('*.dll')) if p.name != 'System.Private.CoreLib.dll']
    command += [str(p) for p in sorted((root / 'payload-generator/3.2.0/UPDATE/src').glob('*.cs'))]
    with (output / 'compile.log').open('w') as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
    for line in (output / 'compile.log').read_text().splitlines():
        if 'error ' in line:
            print(line)
    print('C# compilation exit:', result.returncode)
    raise SystemExit(result.returncode)

if __name__ == '__main__':
    main()
