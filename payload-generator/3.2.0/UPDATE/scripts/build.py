#!/usr/bin/env python3
"""Rebuild the V3 delta with the user's extracted app and a local .NET toolchain."""
import argparse, hashlib, json, os, shutil, struct, subprocess, zipfile
from pathlib import Path

def manifest_patch(data):
    chunks = []
    pos = 8
    while pos < len(data):
        typ, header, size = struct.unpack_from('<HHI', data, pos)
        chunks.append([typ, bytearray(data[pos:pos+size])]); pos += size
    pool = next(c for t,c in chunks if t == 1)
    count, styles, flags, start, style_start = struct.unpack_from('<5I', pool, 8)
    assert styles == 0, 'Unexpected styled manifest pool'
    strings = []
    def length(p, utf8):
        if utf8:
            n=pool[p];p+=1
            if n & 128:n=((n&127)<<8)|pool[p];p+=1
        else:
            n=struct.unpack_from('<H',pool,p)[0];p+=2
            if n & 0x8000:n=((n&0x7fff)<<16)|struct.unpack_from('<H',pool,p)[0];p+=2
        return n,p
    for i in range(count):
        p=start+struct.unpack_from('<I',pool,28+4*i)[0]
        n,p=length(p,bool(flags&256))
        if flags&256:
            n,p=length(p,True);s=pool[p:p+n].decode('utf8')
        else:s=pool[p:p+n*2].decode('utf-16-le')
        strings.append(s)
    assert '3.0' in strings
    strings[strings.index('3.0')]='3.2.0-tayson-test'
    strings.append('usesCleartextTraffic');newidx=len(strings)-1
    offsets=[];blob=bytearray()
    for s in strings:
        offsets.append(len(blob));encoded=s.encode('utf-16-le');units=len(encoded)//2
        blob+=struct.pack('<H',units)+encoded+b'\0\0'
    blob+=b'\0'*((-len(blob))%4)
    pool_start=28+4*len(strings)
    newpool=struct.pack('<HHI5I',1,28,pool_start+len(blob),len(strings),0,0,pool_start,0)+struct.pack('<'+'I'*len(offsets),*offsets)+blob
    resource=next(c for t,c in chunks if t==0x180)
    ids=list(struct.unpack_from('<'+'I'*((len(resource)-8)//4),resource,8))
    ids += [0]*(len(strings)-len(ids));ids[newidx]=0x010104ec
    newmap=struct.pack('<HHI',0x180,8,8+4*len(ids))+struct.pack('<'+'I'*len(ids),*ids)
    android=strings.index('http://schemas.android.com/apk/res/android')
    output=[]
    for typ,chunk in chunks:
        if typ==1:output.append(newpool);continue
        if typ==0x180:output.append(newmap);continue
        if typ==0x102:
            name=struct.unpack_from('<I',chunk,20)[0]
            attrstart,attrsize,attrcount=struct.unpack_from('<HHH',chunk,24)
            first=16+attrstart
            attrs=[bytearray(chunk[first+i*attrsize:first+(i+1)*attrsize]) for i in range(attrcount)]
            if strings[name]=='manifest':
                for a in attrs:
                    if ids[struct.unpack_from('<I',a,4)[0]]==0x0101021b:struct.pack_into('<I',a,16,34)
            if strings[name]=='application':
                attrs.append(bytearray(struct.pack('<IIIHBBI',android,newidx,0xffffffff,8,0,0x12,0xffffffff)))
                attrs.sort(key=lambda a:ids[struct.unpack_from('<I',a,4)[0]])
                struct.pack_into('<H',chunk,28,len(attrs))
            chunk=chunk[:first]+b''.join(attrs)+chunk[first+attrcount*attrsize:]
            struct.pack_into('<I',chunk,4,len(chunk))
        output.append(chunk)
    body=b''.join(output)
    return struct.pack('<HHI',3,8,len(body)+8)+body

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--base',required=True,type=Path)
    parser.add_argument('--tools',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args()
    project=Path(__file__).resolve().parents[1];base=args.base.resolve();tool=args.tools.resolve();out=args.output.resolve()
    work=project/'build';work.mkdir(exist_ok=True);out.mkdir(exist_ok=True,parents=True)
    refs=tool/'assemblies';sdk=tool/'dotnet';runtime=next((sdk/'shared/Microsoft.NETCore.App').glob('8.*'))
    env={**os.environ,'DOTNET_ROOT':str(sdk),'DOTNET_CLI_TELEMETRY_OPTOUT':'1','PATH':str(sdk)+os.pathsep+os.environ['PATH']}
    def run(*cmd):subprocess.run([str(x) for x in cmd],check=True,env=env)
    csc=next((sdk/'sdk').glob('*/Roslyn/bincore/csc.dll'))
    run(sdk/'dotnet',csc,'/noconfig','/nostdlib+','/target:library','/langversion:12','/optimize+',
        '/out:'+str(work/'Tayson.Providers.dll'),'/reference:'+str(runtime/'System.Private.CoreLib.dll'),
        *['/reference:'+str(p) for p in refs.glob('*.dll') if p.name!='System.Private.CoreLib.dll'],*sorted((project/'src').glob('*.cs')))
    patcher=project/'patcher/bin/Release/net8.0/Patcher.dll'
    run(sdk/'dotnet',patcher,'audit',work/'Tayson.Providers.dll',refs)
    run(tool/'bin/ilrepack','/out:'+str(work/'merged.dll'),'/lib:'+str(refs),'/noRepackRes','/ndebug',refs/'PS4RemotePkgMobile.dll',work/'Tayson.Providers.dll')
    run(sdk/'dotnet',patcher,'patch',refs/'PS4RemotePkgMobile.dll',work/'merged.dll',work/'PS4RemotePkgMobile.dll',refs)
    dll=(work/'PS4RemotePkgMobile.dll').read_bytes()
    backup=base/'UPDATE/base'
    store=bytearray(((backup/'original-assemblies.blob') if (backup/'original-assemblies.blob').exists() else base/'assemblies/assemblies.blob').read_bytes())
    assert struct.unpack_from('<5I',store)==(0x41424158,1,114,118,0)
    store+=b'\0'*((-len(store))%16);offset=len(store);store+=dll
    struct.pack_into('<II',store,20+78*24,offset,len(dll))
    manifest=manifest_patch(((backup/'original-AndroidManifest.xml') if (backup/'original-AndroidManifest.xml').exists() else base/'AndroidManifest.xml').read_bytes())
    unsigned=work/'unsigned.apk'
    with zipfile.ZipFile(unsigned,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for p in sorted(base.rglob('*')):
            if not p.is_file():continue
            if p.is_relative_to(tool) or p.is_relative_to(out) or p.is_relative_to(project):continue
            name=p.relative_to(base).as_posix()
            if name.startswith('UPDATE/') or name.endswith('libaot-PS4RemotePkgMobile.dll.so'):continue
            if name.startswith('META-INF/') and (name.upper().endswith(('.RSA','.SF','.DSA','.EC')) or name=='META-INF/MANIFEST.MF'):continue
            data=store if name=='assemblies/assemblies.blob' else manifest if name=='AndroidManifest.xml' else p.read_bytes()
            # Mono mmaps assembly stores; keep them uncompressed and aligned.
            compression=zipfile.ZIP_STORED if (name.startswith('assemblies/') or name=='resources.arsc') else zipfile.ZIP_DEFLATED
            z.writestr(name,data,compress_type=compression)
    android=tool/'android/android-15'
    for name in ['zipalign','apksigner','aapt']:(android/name).chmod(0o755)
    aligned=work/'aligned.apk';run(android/'zipalign','-f','-p','4',unsigned,aligned)
    signing=project/'signing';signing.mkdir(exist_ok=True);key=signing/'tayson-test.jks'
    if not key.exists():
        run('keytool','-genkeypair','-keystore',key,'-storepass','android','-keypass','android','-alias','tayson-test',
            '-dname','CN=Tayson PS4 Test Build','-keyalg','RSA','-keysize','2048','-validity','10000')
    apk=out/'PS4-Payload-Generator-3.2.0-Tayson.apk'
    run('java','-jar',android/'lib/apksigner.jar','sign','--ks',key,'--ks-key-alias','tayson-test','--ks-pass','pass:android','--key-pass','pass:android','--out',apk,aligned)
    run('java','-jar',android/'lib/apksigner.jar','verify','--verbose',apk)
    with zipfile.ZipFile(apk) as z:
        assert z.testzip() is None
        assert z.read('assets/template.elf')==(base/'assets/template.elf').read_bytes()
        assert z.read('classes.dex')==(base/'classes.dex').read_bytes()
        assert z.read('classes2.dex')==(base/'classes2.dex').read_bytes()
    details={'apk':apk.name,'bytes':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),
        'embedded_template_sha256':hashlib.sha256((base/'assets/template.elf').read_bytes()).hexdigest(),
        'original_template_sha256':hashlib.sha256((backup/'original-template.elf').read_bytes()).hexdigest(),
        'package':'com.phoenixx.ps4remotepkg','versionCode':34,'versionName':'3.2.0-tayson-test',
        'original_interface_and_dex_preserved':True,'removed_stale_main_aot_libraries':4}
    (out/'build-info.json').write_text(json.dumps(details,indent=2)+'\n')
    print(json.dumps(details))

if __name__=='__main__':main()
