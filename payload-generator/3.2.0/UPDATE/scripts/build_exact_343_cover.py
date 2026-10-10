#!/usr/bin/env python3
import argparse, hashlib, json, os, shutil, struct, subprocess, tempfile, zipfile
from pathlib import Path

APP_VERSION = "3.5.3"
APP_VERSION_CODE = 53
BASE_SHA256 = "a251efb5fbdcc48acf402d0d54ed2a91b21fec058f563436a54f6b3ff5ae526e"
PACKAGE = "com.tayson.ps4payloadgenerator"

def run(*cmd, env=None, capture=False):
    if capture:
        return subprocess.check_output([str(x) for x in cmd], env=env, text=True)
    subprocess.run([str(x) for x in cmd], check=True, env=env)

def read_len8(data, pos):
    value=data[pos]; pos+=1
    if value & 0x80:
        value=((value&0x7f)<<8)|data[pos]; pos+=1
    return value,pos

def write_len8(value):
    if value < 0x80: return bytes([value])
    return bytes([0x80 | (value >> 8), value & 0xff])

def read_len16(data,pos):
    value=struct.unpack_from("<H",data,pos)[0]; pos+=2
    if value & 0x8000:
        value=((value&0x7fff)<<16)|struct.unpack_from("<H",data,pos)[0]; pos+=2
    return value,pos

def write_len16(value):
    if value < 0x8000: return struct.pack("<H",value)
    return struct.pack("<HH",0x8000 | (value>>16), value & 0xffff)

def decode_pool(pool):
    _,header,_=struct.unpack_from("<HHI",pool,0)
    count,styles,flags,start,_=struct.unpack_from("<5I",pool,8)
    if header != 28 or styles != 0:
        raise ValueError("Unsupported Android string pool")
    utf8=bool(flags & 0x100)
    offsets=[struct.unpack_from("<I",pool,header+4*i)[0] for i in range(count)]
    strings=[]
    for off in offsets:
        p=start+off
        if utf8:
            _,p=read_len8(pool,p)
            n,p=read_len8(pool,p)
            strings.append(bytes(pool[p:p+n]).decode("utf-8"))
        else:
            n,p=read_len16(pool,p)
            strings.append(bytes(pool[p:p+n*2]).decode("utf-16-le"))
    return strings,flags

def encode_pool(strings,flags):
    utf8=bool(flags & 0x100)
    blob=bytearray(); offsets=[]
    for value in strings:
        offsets.append(len(blob))
        if utf8:
            enc=value.encode("utf-8")
            units=len(value.encode("utf-16-le"))//2
            blob += write_len8(units)+write_len8(len(enc))+enc+b"\0"
        else:
            enc=value.encode("utf-16-le")
            blob += write_len16(len(enc)//2)+enc+b"\0\0"
    blob += b"\0"*((-len(blob))%4)
    header=28
    start=header+4*len(strings)
    return (struct.pack("<HHI5I",1,header,start+len(blob),len(strings),0,flags,start,0) +
            struct.pack("<"+"I"*len(offsets),*offsets) + blob)

def patch_manifest(data):
    chunks=[]; pos=8
    while pos < len(data):
        typ,header,size=struct.unpack_from("<HHI",data,pos)
        if size < header or pos+size > len(data):
            raise ValueError("Malformed AndroidManifest.xml")
        chunks.append([typ,bytearray(data[pos:pos+size])]); pos+=size

    pool_i=next(i for i,(t,_) in enumerate(chunks) if t==1)
    strings,flags=decode_pool(chunks[pool_i][1])
    if PACKAGE not in strings:
        raise ValueError("Expected stable 3.4.3 package identity not found")
    if "3.4.3" not in strings:
        raise ValueError("Expected versionName 3.4.3 not found")
    strings=[APP_VERSION if x=="3.4.3" else x for x in strings]
    chunks[pool_i]=[1,bytearray(encode_pool(strings,flags))]

    res_i=next(i for i,(t,_) in enumerate(chunks) if t==0x180)
    res=chunks[res_i][1]
    ids=list(struct.unpack_from("<"+"I"*((len(res)-8)//4),res,8))

    changed_code=False
    for typ,chunk in chunks:
        if typ != 0x102: continue
        name_idx=struct.unpack_from("<I",chunk,20)[0]
        if name_idx >= len(strings) or strings[name_idx] != "manifest": continue
        attr_start,attr_size,attr_count=struct.unpack_from("<HHH",chunk,24)
        first=16+attr_start
        for i in range(attr_count):
            off=first+i*attr_size
            attr_name=struct.unpack_from("<I",chunk,off+4)[0]
            if attr_name < len(ids) and ids[attr_name] == 0x0101021b:
                struct.pack_into("<I",chunk,off+16,APP_VERSION_CODE)
                changed_code=True
    if not changed_code:
        raise ValueError("versionCode attribute not found")

    body=b"".join(bytes(c) for _,c in chunks)
    return struct.pack("<HHI",3,8,len(body)+8)+body

def target_assembly_index(base_dir):
    manifest=(base_dir/"assemblies/assemblies.manifest").read_text().splitlines()
    store=None; index=None
    for line in manifest[1:]:
        cols=line.split()
        if len(cols) < 5: continue
        if cols[4] == "PS4RemotePkgMobile":
            store=int(cols[2]); index=int(cols[3]); break
    if index is None:
        raise ValueError("PS4RemotePkgMobile assembly index not found")
    return store,index

def replace_assembly_blob(base_dir, patched_dll):
    path=base_dir/"assemblies/assemblies.blob"
    data=bytearray(path.read_bytes())
    magic,version,count,total,store=struct.unpack_from("<5I",data,0)
    if magic != 0x41424158 or version != 1:
        raise ValueError("Unexpected Xamarin assembly store")
    expected_store,index=target_assembly_index(base_dir)
    if store != expected_store or index >= count:
        raise ValueError("PS4RemotePkgMobile store/index mismatch")
    dll=patched_dll.read_bytes()
    data += b"\0"*((-len(data))%16)
    offset=len(data)
    data += dll
    struct.pack_into("<II",data,20+index*24,offset,len(dll))
    return bytes(data), index

def cert_digest(apk, apksigner):
    text=run("java","-jar",apksigner,"verify","--print-certs",apk,capture=True)
    for line in text.splitlines():
        if "Signer #1 certificate SHA-256 digest:" in line:
            return line.split(":",1)[1].strip().lower()
    raise ValueError("APK certificate digest not found")

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--base-apk",required=True,type=Path)
    p.add_argument("--base-dir",required=True,type=Path)
    p.add_argument("--tools",required=True,type=Path)
    p.add_argument("--template",required=True,type=Path)
    p.add_argument("--output",required=True,type=Path)
    p.add_argument("--keystore",required=True,type=Path)
    args=p.parse_args()

    project=Path(__file__).resolve().parents[1]
    exact=project/"exact343"
    refs=args.tools/"assemblies"
    dotnet=args.tools/"dotnet"
    runtime=next((dotnet/"shared/Microsoft.NETCore.App").glob("8.*"))
    env={**os.environ,"DOTNET_ROOT":str(dotnet),"DOTNET_CLI_TELEMETRY_OPTOUT":"1",
         "PATH":str(dotnet)+os.pathsep+os.environ["PATH"]}

    if hashlib.sha256(args.base_apk.read_bytes()).hexdigest() != BASE_SHA256:
        raise ValueError("Base APK is not the exact stable 3.4.3 supplied by the user")

    work=project/"build-exact343"
    if work.exists(): shutil.rmtree(work)
    work.mkdir(parents=True)

    csc=next((dotnet/"sdk").glob("*/Roslyn/bincore/csc.dll"))
    overlay=work/"Tayson.Exact343Cover.dll"
    refs_args=["/reference:"+str(x) for x in refs.glob("*.dll")
               if x.name!="System.Private.CoreLib.dll"]
    run(dotnet/"dotnet",csc,"/noconfig","/nostdlib+","/target:library","/langversion:12","/optimize+",
        "/out:"+str(overlay),"/reference:"+str(runtime/"System.Private.CoreLib.dll"),
        *refs_args, exact/"ExactCoverOverlay.cs", env=env)

    cecil=args.tools/"nuget/mono.cecil/lib/netstandard2.0/Mono.Cecil.dll"
    patchproj=exact/"patcher/ExactCoverPatcher.csproj"
    run(dotnet/"dotnet","build",patchproj,"-c","Release","-p:CecilPath="+str(cecil),
        "-p:NuGetAudit=false","--ignore-failed-sources",env=env)

    original=refs/"PS4RemotePkgMobile.dll"
    merged=work/"merged.dll"
    patched=work/"PS4RemotePkgMobile.dll"
    run(args.tools/"bin/ilrepack","/out:"+str(merged),"/lib:"+str(refs),"/noRepackRes","/ndebug",
        original,overlay,env=env)
    patcher=exact/"patcher/bin/Release/net8.0/ExactCoverPatcher.dll"
    run(dotnet/"dotnet",patcher,original,merged,patched,refs,env=env)

    new_store,assembly_index=replace_assembly_blob(args.base_dir,patched)
    new_manifest=patch_manifest((args.base_dir/"AndroidManifest.xml").read_bytes())
    template=args.template.read_bytes()
    if b"###ICON_PLACEHOLDER###" not in template or b"###ICON_SIZE###" not in template:
        raise ValueError("Cover-capable ELF markers missing")

    allowed={"AndroidManifest.xml","assemblies/assemblies.blob","assets/template.elf"}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    unsigned=work/"unsigned.apk"

    with zipfile.ZipFile(args.base_apk,"r") as zin, zipfile.ZipFile(unsigned,"w") as zout:
        for item in zin.infolist():
            name=item.filename
            upper=name.upper()
            if name=="META-INF/MANIFEST.MF" or (
                name.startswith("META-INF/") and upper.endswith((".RSA",".SF",".DSA",".EC"))
            ):
                continue
            if name=="AndroidManifest.xml": data=new_manifest
            elif name=="assemblies/assemblies.blob": data=new_store
            elif name=="assets/template.elf": data=template
            else: data=zin.read(name)
            zi=zipfile.ZipInfo(name,item.date_time)
            zi.compress_type=item.compress_type
            zi.comment=item.comment; zi.extra=item.extra
            zi.create_system=item.create_system
            zi.external_attr=item.external_attr; zi.internal_attr=item.internal_attr
            zi.flag_bits=item.flag_bits
            zout.writestr(zi,data)

    android=args.tools/"android/android-15"
    zipalign=android/"zipalign"
    apksigner=android/"lib/apksigner.jar"
    aligned=work/"aligned.apk"
    run(zipalign,"-f","-p","4",unsigned,aligned)
    args.keystore.parent.mkdir(parents=True, exist_ok=True)
    if not args.keystore.exists():
        run("keytool","-genkeypair","-keystore",args.keystore,
            "-storepass","android","-keypass","android","-alias","tayson-test",
            "-dname","CN=TaySon PS4 Payload Generator",
            "-keyalg","RSA","-keysize","2048","-validity","10000")
    run("java","-jar",apksigner,"sign","--ks",args.keystore,
        "--ks-key-alias","tayson-test","--ks-pass","pass:android","--key-pass","pass:android",
        "--out",args.output,aligned)
    run("java","-jar",apksigner,"verify","--verbose",args.output)

    base_cert=cert_digest(args.base_apk,apksigner)
    final_cert=cert_digest(args.output,apksigner)
    same_cert = base_cert == final_cert

    with zipfile.ZipFile(args.base_apk,"r") as base, zipfile.ZipFile(args.output,"r") as final:
        if final.testzip() is not None: raise ValueError("Final APK ZIP integrity failed")
        for name in base.namelist():
            if name in allowed or name.startswith("META-INF/"): continue
            if name not in final.namelist():
                raise ValueError("Base entry missing from update: "+name)
            if base.read(name) != final.read(name):
                raise ValueError("Stable 3.4.3 entry unexpectedly changed: "+name)

    badging=run(android/"aapt","dump","badging",args.output,capture=True)
    if "package: name='"+PACKAGE+"'" not in badging:
        raise ValueError("Package identity changed")
    if "versionCode='"+str(APP_VERSION_CODE)+"'" not in badging or "versionName='"+APP_VERSION+"'" not in badging:
        raise ValueError("Version identity mismatch")

    details={
        "base_apk_sha256":BASE_SHA256,
        "output":args.output.name,
        "output_sha256":hashlib.sha256(args.output.read_bytes()).hexdigest(),
        "package":PACKAGE,
        "versionName":APP_VERSION,
        "versionCode":APP_VERSION_CODE,
        "base_signing_certificate_sha256":base_cert,
        "signing_certificate_sha256":final_cert,
        "same_signing_certificate_as_3_4_3":same_cert,
        "requires_uninstall_of_3_4_3_before_install":not same_cert,
        "template_sha256":hashlib.sha256(template).hexdigest(),
        "patched_assembly_index":assembly_index,
        "preserved_all_other_apk_entries":True,
        "exact_stable_343_base":True
    }
    report=args.output.with_suffix(".json")
    report.write_text(json.dumps(details,indent=2)+"\n")
    print(json.dumps(details))

if __name__=="__main__":
    main()
