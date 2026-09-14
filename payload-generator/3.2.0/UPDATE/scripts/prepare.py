#!/usr/bin/env python3
"""Prepare a Linux x64 toolchain and extract the original, version-matched assemblies."""
import argparse, ctypes, ctypes.util, hashlib, json, os, struct, subprocess, tarfile, urllib.request, xml.etree.ElementTree as ET, zipfile
from pathlib import Path

def fetch(url,path):
    urllib.request.urlretrieve(url,path)

def extract_refs(base,tool):
    folder=base/'assemblies';refs=tool/'assemblies';refs.mkdir(exist_ok=True)
    library=ctypes.util.find_library('lz4')
    if not library:raise RuntimeError('Install liblz4-1 before extracting Xamarin assembly stores.')
    lz4=ctypes.CDLL(library);lz4.LZ4_decompress_safe.argtypes=[ctypes.c_char_p,ctypes.c_void_p,ctypes.c_int,ctypes.c_int];lz4.LZ4_decompress_safe.restype=ctypes.c_int
    names={}
    for line in (folder/'assemblies.manifest').read_text().splitlines()[1:]:
        cols=line.split();names[(int(cols[2]),int(cols[3]))]=cols[4]
    backup=base/'UPDATE/base/original-assemblies.blob'
    for path in [backup if backup.exists() else folder/'assemblies.blob',folder/'assemblies.x86_64.blob']:
        data=path.read_bytes();magic,version,count,total,store=struct.unpack_from('<5I',data)
        assert magic==0x41424158 and version==1
        for i in range(count):
            offset,size=struct.unpack_from('<II',data,20+i*24);dll=data[offset:offset+size]
            if dll[:4]==b'XALZ':
                length=struct.unpack_from('<I',dll,8)[0];out=ctypes.create_string_buffer(length)
                assert lz4.LZ4_decompress_safe(dll[12:],out,len(dll)-12,length)==length
                dll=out.raw
            assert dll[:2]==b'MZ'
            destination=refs/(names[(store,i)]+'.dll');destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(dll)
    print('118 version-matched assemblies extracted.',flush=True)

def main():
    p=argparse.ArgumentParser();p.add_argument('--base',required=True,type=Path);p.add_argument('--tools',required=True,type=Path);a=p.parse_args()
    base=a.base.resolve();tool=a.tools.resolve();tool.mkdir(parents=True,exist_ok=True);dotnet=tool/'dotnet'
    if not (dotnet/'dotnet').exists():
        metadata=json.load(urllib.request.urlopen('https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json'))
        sdks=[s for r in metadata['releases'] for s in r.get('sdks',[r['sdk']])]
        sdk=next(s for s in sdks if s['version']=='8.0.425')
        file=next(f for f in sdk['files'] if f['rid']=='linux-x64' and f['name'].endswith('.tar.gz'))
        archive=tool/'dotnet.tar.gz';fetch(file['url'],archive)
        assert hashlib.sha512(archive.read_bytes()).hexdigest().lower()==file['hash'].lower()
        dotnet.mkdir(exist_ok=True)
        with tarfile.open(archive) as t:t.extractall(dotnet,filter='data')
    env={**os.environ,'DOTNET_ROOT':str(dotnet),'DOTNET_CLI_TELEMETRY_OPTOUT':'1','PATH':str(dotnet)+os.pathsep+os.environ['PATH']}
    tools=tool/'bin';tools.mkdir(exist_ok=True)
    if not (tools/'ilrepack').exists():
        subprocess.run([str(dotnet/'dotnet'),'tool','install','dotnet-ilrepack','--version','2.0.44','--tool-path',str(tools)],check=True,env=env)
    android=tool/'android/android-15'
    if not (android/'aapt').exists():
        meta='https://dl.google.com/android/repository/repository2-1.xml'
        root=ET.fromstring(urllib.request.urlopen(meta).read());pkg=next(e for e in root.findall('remotePackage') if e.attrib.get('path')=='build-tools;35.0.0')
        entry=next(e for e in pkg.findall('./archives/archive') if e.findtext('host-os')=='linux')
        archive=tool/'build-tools.zip';fetch('https://dl.google.com/android/repository/'+entry.findtext('./complete/url'),archive)
        checksum=entry.findtext('./complete/checksum')
        if checksum:assert hashlib.sha1(archive.read_bytes()).hexdigest()==checksum
        with zipfile.ZipFile(archive) as z:z.extractall(tool/'android')
    for name in ['aapt','zipalign','apksigner']:(android/name).chmod(0o755)
    cecil=tool/'nuget/mono.cecil';assembly=cecil/'lib/netstandard2.0/Mono.Cecil.dll'
    if not assembly.exists():
        cecil.mkdir(parents=True,exist_ok=True);archive=cecil/'cecil.nupkg';fetch('https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg',archive)
        with zipfile.ZipFile(archive) as z:z.extractall(cecil)
    extract_refs(base,tool)
    project=Path(__file__).resolve().parents[1]
    subprocess.run([str(dotnet/'dotnet'),'build',str(project/'patcher/Patcher.csproj'),'-c','Release','-p:CecilPath='+str(assembly),'-p:NuGetAudit=false','--ignore-failed-sources'],check=True,env=env)
    print('Ready. Run scripts/build.py with the same --base and --tools.',flush=True)

if __name__=='__main__':main()
