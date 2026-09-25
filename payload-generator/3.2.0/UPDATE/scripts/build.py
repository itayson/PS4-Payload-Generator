#!/usr/bin/env python3
"""Rebuild the Tayson delta with the user's extracted app and a local .NET toolchain."""
import argparse, hashlib, json, os, shutil, struct, subprocess, zipfile, zlib
from pathlib import Path

APP_VERSION = '4.0.0-alpha2'
APP_VERSION_CODE = 51
APP_LABEL = 'Tayson PS4 Payload Generator'
APK_NAME = 'Tayson-PS4-Receiver-4.0.0-alpha2.apk'

ICON_SIZES = {
    'mipmap-mdpi-v4': 48,
    'mipmap-hdpi-v4': 72,
    'mipmap-xhdpi-v4': 96,
    'mipmap-xxhdpi-v4': 144,
    'mipmap-xxxhdpi-v4': 192,
}
ADAPTIVE_SIZES = {
    'mipmap-mdpi-v4': 108,
    'mipmap-hdpi-v4': 162,
    'mipmap-xhdpi-v4': 216,
    'mipmap-xxhdpi-v4': 324,
    'mipmap-xxxhdpi-v4': 432,
}

def _chunk(kind, payload):
    return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload) & 0xffffffff)

def _png(width, height, pixel):
    raw = bytearray()
    for y in range(height):
        raw.append(0)
        for x in range(width):
            raw.extend(pixel(x, y, width, height))
    return (b'\x89PNG\r\n\x1a\n' +
            _chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)) +
            _chunk(b'IDAT', zlib.compress(bytes(raw), 9)) +
            _chunk(b'IEND', b''))

def _rounded_box(x, y, left, top, right, bottom, radius):
    if x < left or x > right or y < top or y > bottom:
        return False
    if left + radius <= x <= right - radius or top + radius <= y <= bottom - radius:
        return True
    cx = left + radius if x < left + radius else right - radius
    cy = top + radius if y < top + radius else bottom - radius
    dx = x - cx; dy = y - cy
    return dx * dx + dy * dy <= radius * radius

def _circle(x, y, cx, cy, r):
    dx = x - cx; dy = y - cy
    return dx * dx + dy * dy <= r * r

def _t_shape(nx, ny, expand=0.0):
    top_y0 = 0.275 - expand
    top_y1 = 0.385 + expand
    if top_y0 <= ny <= top_y1:
        progress = (ny - top_y0) / max(0.001, top_y1 - top_y0)
        inset = 0.205 + progress * 0.045 - expand
        if inset <= nx <= 1.0 - inset:
            return True
    stem_left = 0.455 - expand
    stem_right = 0.555 + expand
    if stem_left <= nx <= stem_right and 0.355 - expand <= ny <= 0.725 + expand:
        return True
    # Small diagonal cut gives the monogram a custom TaySon identity while
    # keeping it readable at Android's 48 px launcher density.
    if 0.43 - expand <= nx <= 0.555 + expand and 0.66 - expand <= ny <= 0.745 + expand:
        return nx >= 0.43 + (ny - 0.66) * 0.42 - expand
    return False

def _purple(nx, ny, bright=False):
    # Violet gradient: deeper at the bottom, brighter toward the top/right.
    mix = max(0.0, min(1.0, 0.58 * nx + 0.42 * (1.0 - ny)))
    if bright:
        return (176 + int(35 * mix), 72 + int(38 * mix), 255, 255)
    return (104 + int(42 * mix), 28 + int(28 * mix), 210 + int(35 * mix), 255)

def _icon_pixel(x, y, w, h, round_mask=False, foreground_only=False):
    nx = (x + 0.5) / w
    ny = (y + 0.5) / h

    if round_mask and not _circle(nx, ny, 0.5, 0.5, 0.5):
        return (0, 0, 0, 0)

    if foreground_only:
        if _t_shape(nx, ny, 0.012):
            if _t_shape(nx, ny, 0.0):
                return _purple(nx, ny, True)
            return (87, 20, 170, 150)
        return (0, 0, 0, 0)

    # Predominantly black background with only a restrained purple lift.
    edge = abs(nx - 0.5) + abs(ny - 0.5)
    lift = max(0.0, 1.0 - edge * 1.45)
    r = 4 + int(7 * lift)
    g = 2 + int(2 * lift)
    b = 9 + int(17 * lift)

    # Minimal one-pixel-equivalent purple outline at launcher scale.
    outer = _rounded_box(nx, ny, 0.065, 0.065, 0.935, 0.935, 0.17)
    inner = _rounded_box(nx, ny, 0.082, 0.082, 0.918, 0.918, 0.155)
    if outer and not inner:
        return (92, 28, 178, 255)

    if _t_shape(nx, ny, 0.018):
        if _t_shape(nx, ny, 0.0):
            return _purple(nx, ny, True)
        return _purple(nx, ny, False)

    return (r, g, b, 255)

def _background_pixel(x, y, w, h):
    nx = (x + 0.5) / w
    ny = (y + 0.5) / h
    lift = max(0.0, 1.0 - (abs(nx - 0.5) + abs(ny - 0.45)) * 1.3)
    return (4 + int(8 * lift), 2 + int(2 * lift), 10 + int(22 * lift), 255)

def icon_resource(name):
    parts = name.split('/')
    if len(parts) < 3 or not name.startswith('res/mipmap-'):
        return None
    folder = parts[-2]
    file = parts[-1]
    if folder not in ICON_SIZES:
        return None
    if file == 'icone.png':
        size = ICON_SIZES[folder]
        return _png(size, size, lambda x,y,w,h: _icon_pixel(x,y,w,h))
    if file == 'icone_round.png':
        size = ICON_SIZES[folder]
        return _png(size, size, lambda x,y,w,h: _icon_pixel(x,y,w,h,round_mask=True))
    if file == 'icone_background.png':
        size = ADAPTIVE_SIZES[folder]
        return _png(size, size, _background_pixel)
    if file == 'icone_foreground.png':
        size = ADAPTIVE_SIZES[folder]
        return _png(size, size, lambda x,y,w,h: _icon_pixel(x,y,w,h,foreground_only=True))
    return None

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
    strings[strings.index('3.0')] = APP_VERSION
    if 'PS4 Payload Generator' in strings:
        strings[strings.index('PS4 Payload Generator')] = APP_LABEL
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
                    if ids[struct.unpack_from('<I',a,4)[0]]==0x0101021b:struct.pack_into('<I',a,16,APP_VERSION_CODE)
            if strings[name]=='application':
                attrs.append(bytearray(struct.pack('<IIIHBBI',android,newidx,0xffffffff,8,0,0x12,0xffffffff)))
                attrs.sort(key=lambda a:ids[struct.unpack_from('<I',a,4)[0]])
                struct.pack_into('<H',chunk,28,len(attrs))
            chunk=chunk[:first]+b''.join(attrs)+chunk[first+attrcount*attrsize:]
            struct.pack_into('<I',chunk,4,len(chunk))
        output.append(chunk)
    body=b''.join(output)
    return struct.pack('<HHI',3,8,len(body)+8)+body

def patch_managed_ui_strings(data):
    """Patch only exact managed resource strings, never metadata/method names."""
    replacements = [
        (b'\x01\x0dNetwork Error\x01', b'\x01\x0dErro de rede!\x01'),
        (b'\x01\x07Success\x01', b'\x01\x07Sucesso\x01'),
        (b'\x01\x0fSending file...\x01', b'\x01\x0fEnviando ELF...\x01'),
        (b'\x01\x19Successfully sent to PS4!\x01', b'\x01\x19Enviado ao PS4 - sucesso!\x01'),
        (b'\x01\x0dPayload sent!\x01', b'\x01\x0dEnvio ao PS4!\x01'),
        (b'\x01\x1cScan completed successfully!\x01', b'\x01\x1cPKG analisado com sucesso!!!\x01'),
        (b'\x01\x1bPayload saved on the device\x01', b'\x01\x1bPayload salvo no celular!!!\x01'),
        (b'\x01\x0eStatus: Ready.\x01', b'\x01\x0eStatus: Pronto\x01'),
    ]
    patched = 0
    for old, new in replacements:
        assert len(old) == len(new)
        count = data.count(old)
        if count:
            data = data.replace(old, new)
            patched += count
    if patched < 5:
        raise RuntimeError('Managed PT-BR resource patch matched too few strings: %d' % patched)
    return data, patched

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
    dll, ui_patch_count = patch_managed_ui_strings(dll)
    backup=base/'UPDATE/base'
    store=bytearray(((backup/'original-assemblies.blob') if (backup/'original-assemblies.blob').exists() else base/'assemblies/assemblies.blob').read_bytes())
    assert struct.unpack_from('<5I',store)==(0x41424158,1,114,118,0)
    store+=b'\0'*((-len(store))%16);offset=len(store);store+=dll
    struct.pack_into('<II',store,20+78*24,offset,len(dll))
    manifest=manifest_patch(((backup/'original-AndroidManifest.xml') if (backup/'original-AndroidManifest.xml').exists() else base/'AndroidManifest.xml').read_bytes())
    unsigned=work/'unsigned.apk'
    icon_count=0
    with zipfile.ZipFile(unsigned,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for p in sorted(base.rglob('*')):
            if not p.is_file():continue
            if p.is_relative_to(tool) or p.is_relative_to(out) or p.is_relative_to(project):continue
            name=p.relative_to(base).as_posix()
            if name.startswith('UPDATE/') or name.endswith('libaot-PS4RemotePkgMobile.dll.so'):continue
            if name.startswith('META-INF/') and (name.upper().endswith(('.RSA','.SF','.DSA','.EC')) or name=='META-INF/MANIFEST.MF'):continue
            custom_icon=icon_resource(name)
            if custom_icon is not None:
                data=custom_icon
                icon_count+=1
            else:
                data=store if name=='assemblies/assemblies.blob' else manifest if name=='AndroidManifest.xml' else p.read_bytes()
            compression=zipfile.ZIP_STORED if (name.startswith('assemblies/') or name=='resources.arsc') else zipfile.ZIP_DEFLATED
            z.writestr(name,data,compress_type=compression)

        receiver = project/'assets/tayson_receiver.elf'
        if not receiver.exists() or receiver.stat().st_size < 4096:
            raise RuntimeError('Missing or invalid Tayson Receiver ELF: %s' % receiver)
        z.writestr('assets/tayson_receiver.elf', receiver.read_bytes(), compress_type=zipfile.ZIP_DEFLATED)
    if icon_count != 20:
        raise RuntimeError('Expected 20 launcher icon resources, replaced %d' % icon_count)
    android=tool/'android/android-15'
    for name in ['zipalign','apksigner','aapt']:(android/name).chmod(0o755)
    aligned=work/'aligned.apk';run(android/'zipalign','-f','-p','4',unsigned,aligned)
    signing=project/'signing';signing.mkdir(exist_ok=True);key=signing/'tayson-test.jks'
    if not key.exists():
        run('keytool','-genkeypair','-keystore',key,'-storepass','android','-keypass','android','-alias','tayson-test',
            '-dname','CN=TaySon PS4 Payload Generator','-keyalg','RSA','-keysize','2048','-validity','10000')
    apk=out/APK_NAME
    run('java','-jar',android/'lib/apksigner.jar','sign','--ks',key,'--ks-key-alias','tayson-test','--ks-pass','pass:android','--key-pass','pass:android','--out',apk,aligned)
    run('java','-jar',android/'lib/apksigner.jar','verify','--verbose',apk)
    with zipfile.ZipFile(apk) as z:
        assert z.testzip() is None
        assert z.read('assets/template.elf')==(base/'assets/template.elf').read_bytes()
        assert z.read('assets/tayson_receiver.elf')==(project/'assets/tayson_receiver.elf').read_bytes()
        assert z.read('classes.dex')==(base/'classes.dex').read_bytes()
        assert z.read('classes2.dex')==(base/'classes2.dex').read_bytes()
        assert z.read('res/mipmap-xxxhdpi-v4/icone.png') != (base/'res/mipmap-xxxhdpi-v4/icone.png').read_bytes()
    details={'apk':apk.name,'bytes':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),
        'embedded_template_sha256':hashlib.sha256((base/'assets/template.elf').read_bytes()).hexdigest(),
        'receiver_sha256':hashlib.sha256((project/'assets/tayson_receiver.elf').read_bytes()).hexdigest(),
        'receiver_port':9091,
        'transport':'tayson-receiver-phase1-no-bgft',
        'original_template_sha256':hashlib.sha256((backup/'original-template.elf').read_bytes()).hexdigest(),
        'package':'com.phoenixx.ps4remotepkg','applicationLabel':APP_LABEL,
        'versionCode':APP_VERSION_CODE,'versionName':APP_VERSION,
        'managed_ptbr_resource_patches':ui_patch_count,
        'launcher_icon_resources_replaced':icon_count,
        'original_interface_and_dex_preserved':True,'removed_stale_main_aot_libraries':4}
    (out/'build-info.json').write_text(json.dumps(details,indent=2)+'\n')
    print(json.dumps(details))

if __name__=='__main__':main()