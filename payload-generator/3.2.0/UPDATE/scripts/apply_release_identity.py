#!/usr/bin/env python3
"""Apply Tayson package identity and official launcher artwork to the release APK.

This is a post-build step: it preserves the inherited MAUI/JNI classes and
rewrites only Android package identity strings that are safe to rename. The
MonoRuntimeProvider class remains in its original Java namespace because that
class is generated in classes2.dex by the inherited Xamarin/MAUI runtime.
"""
import argparse
import hashlib
import json
import os
import re
import struct
import subprocess
import tempfile
import zipfile
from pathlib import Path

OLD_PACKAGE = "com.phoenixx.ps4remotepkg"
NEW_PACKAGE = "com.tayson.ps4payloadgenerator"
MONO_PROVIDER = OLD_PACKAGE + ".mono.MonoRuntimeProvider.__mono_init__"
ICON_NAMES = ("icone.png", "icone_round.png", "icone_background.png", "icone_foreground.png")
DENSITIES = ("mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi")
EXPECTED_ICONS = {
    f"res/mipmap-{density}-v4/{name}"
    for density in DENSITIES for name in ICON_NAMES
}


def _read_len8(data, pos):
    value = data[pos]
    pos += 1
    if value & 0x80:
        value = ((value & 0x7F) << 8) | data[pos]
        pos += 1
    return value, pos


def _write_len8(value):
    if value < 0x80:
        return bytes([value])
    if value > 0x7FFF:
        raise ValueError("UTF-8 string length too large")
    return bytes([0x80 | (value >> 8), value & 0xFF])


def _read_len16(data, pos):
    value = struct.unpack_from("<H", data, pos)[0]
    pos += 2
    if value & 0x8000:
        value = ((value & 0x7FFF) << 16) | struct.unpack_from("<H", data, pos)[0]
        pos += 2
    return value, pos


def _write_len16(value):
    if value < 0x8000:
        return struct.pack("<H", value)
    if value > 0x7FFFFFFF:
        raise ValueError("UTF-16 string length too large")
    return struct.pack("<HH", 0x8000 | (value >> 16), value & 0xFFFF)


def _decode_string_pool(pool):
    _, header_size, _ = struct.unpack_from("<HHI", pool, 0)
    string_count, style_count, flags, strings_start, _ = struct.unpack_from("<5I", pool, 8)
    if header_size != 28 or style_count != 0:
        raise ValueError("Unsupported Android string-pool layout")
    utf8 = bool(flags & 0x100)
    offsets = [struct.unpack_from("<I", pool, header_size + 4 * i)[0] for i in range(string_count)]
    strings = []
    for offset in offsets:
        pos = strings_start + offset
        if utf8:
            _, pos = _read_len8(pool, pos)
            byte_len, pos = _read_len8(pool, pos)
            strings.append(bytes(pool[pos:pos + byte_len]).decode("utf-8"))
        else:
            char_len, pos = _read_len16(pool, pos)
            strings.append(bytes(pool[pos:pos + char_len * 2]).decode("utf-16-le"))
    return strings, flags


def _encode_string_pool(strings, flags):
    utf8 = bool(flags & 0x100)
    blob = bytearray()
    offsets = []
    for value in strings:
        offsets.append(len(blob))
        if utf8:
            encoded = value.encode("utf-8")
            units = len(value.encode("utf-16-le")) // 2
            blob += _write_len8(units) + _write_len8(len(encoded)) + encoded + b"\0"
        else:
            encoded = value.encode("utf-16-le")
            blob += _write_len16(len(encoded) // 2) + encoded + b"\0\0"
    blob += b"\0" * ((-len(blob)) % 4)
    header_size = 28
    strings_start = header_size + 4 * len(strings)
    out = bytearray(struct.pack(
        "<HHI5I", 0x0001, header_size, strings_start + len(blob),
        len(strings), 0, flags, strings_start, 0
    ))
    out += struct.pack("<" + "I" * len(offsets), *offsets)
    out += blob
    return out


def patch_manifest(data, old_package, new_package):
    chunks = []
    pos = 8
    while pos < len(data):
        typ, header_size, size = struct.unpack_from("<HHI", data, pos)
        if size < header_size or pos + size > len(data):
            raise ValueError("Malformed binary AndroidManifest.xml")
        chunks.append((typ, bytearray(data[pos:pos + size])))
        pos += size

    pool_index = next(i for i, (typ, _) in enumerate(chunks) if typ == 0x0001)
    strings, flags = _decode_string_pool(chunks[pool_index][1])
    replacements = {
        old_package: new_package,
        old_package + ".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION":
            new_package + ".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION",
        old_package + ".androidx-startup": new_package + ".androidx-startup",
    }
    found = {key: 0 for key in replacements}
    for i, value in enumerate(strings):
        if value in replacements:
            found[value] += 1
            strings[i] = replacements[value]

    missing = [key for key, count in found.items() if count == 0]
    if missing:
        raise ValueError("Expected package strings missing: " + ", ".join(missing))
    if MONO_PROVIDER not in strings:
        raise ValueError("Inherited MonoRuntimeProvider class string is missing")

    chunks[pool_index] = (0x0001, _encode_string_pool(strings, flags))
    body = b"".join(bytes(chunk) for _, chunk in chunks)
    return struct.pack("<HHI", 0x0003, 8, len(body) + 8) + body


JAVA_ICON_BUILDER = r'''import java.awt.*;
import java.awt.image.BufferedImage;
import java.io.File;
import javax.imageio.ImageIO;

public final class TaysonIconBuilder {
  static BufferedImage resize(BufferedImage src, int w, int h) {
    BufferedImage out = new BufferedImage(w, h, BufferedImage.TYPE_INT_ARGB);
    Graphics2D g = out.createGraphics();
    g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BICUBIC);
    g.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY);
    g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
    g.drawImage(src, 0, 0, w, h, null);
    g.dispose();
    return out;
  }

  static void save(BufferedImage img, File file) throws Exception {
    file.getParentFile().mkdirs();
    if (!ImageIO.write(img, "png", file)) throw new Exception("PNG writer unavailable");
  }

  public static void main(String[] args) throws Exception {
    BufferedImage src = ImageIO.read(new File(args[0]));
    if (src == null) throw new Exception("Invalid launcher image");
    File root = new File(args[1]);
    String[] d = {"mdpi","hdpi","xhdpi","xxhdpi","xxxhdpi"};
    int[] legacy = {48,72,96,144,192};
    int[] adaptive = {108,162,216,324,432};
    for (int i = 0; i < d.length; i++) {
      File dir = new File(root, "res/mipmap-" + d[i] + "-v4");
      BufferedImage icon = resize(src, legacy[i], legacy[i]);
      save(icon, new File(dir, "icone.png"));
      save(icon, new File(dir, "icone_round.png"));

      int a = adaptive[i];
      BufferedImage bg = new BufferedImage(a, a, BufferedImage.TYPE_INT_ARGB);
      Graphics2D g = bg.createGraphics();
      g.setColor(Color.BLACK);
      g.fillRect(0, 0, a, a);
      int inset = Math.max(1, Math.round(a * 0.055f));
      BufferedImage art = resize(src, a - 2 * inset, a - 2 * inset);
      g.drawImage(art, inset, inset, null);
      g.dispose();
      save(bg, new File(dir, "icone_background.png"));

      BufferedImage fg = new BufferedImage(a, a, BufferedImage.TYPE_INT_ARGB);
      save(fg, new File(dir, "icone_foreground.png"));
    }
  }
}
'''


def build_icon_resources(source, root):
    root.mkdir(parents=True, exist_ok=True)
    java_file = root / "TaysonIconBuilder.java"
    java_file.write_text(JAVA_ICON_BUILDER)
    subprocess.run(["javac", str(java_file)], check=True, cwd=root)
    subprocess.run(["java", "-Djava.awt.headless=true", "-cp", str(root),
                    "TaysonIconBuilder", str(source.resolve()), str(root.resolve())],
                   check=True, cwd=root)
    icons = {}
    for name in EXPECTED_ICONS:
        path = root / name
        if not path.is_file() or path.stat().st_size < 64:
            raise ValueError("Generated launcher resource missing: " + name)
        icons[name] = path.read_bytes()
    return icons


def run(*args):
    subprocess.run([str(x) for x in args], check=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apk", required=True, type=Path,
                        help="Aligned unsigned APK produced by build.py")
    parser.add_argument("--icon-source", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--unsigned-output", required=True, type=Path)
    parser.add_argument("--tools", required=True, type=Path)
    parser.add_argument("--keystore", required=True, type=Path)
    parser.add_argument("--storepass", default="android")
    parser.add_argument("--keypass", default="android")
    parser.add_argument("--alias", default="tayson-test")
    parser.add_argument("--storepass-ref", help="apksigner env:NAME reference, never actual password")
    parser.add_argument("--keypass-ref", help="apksigner env:NAME reference, never actual password")
    parser.add_argument("--build-info", type=Path)
    parser.add_argument("--old-package", default=OLD_PACKAGE)
    parser.add_argument("--new-package", default=NEW_PACKAGE)
    args = parser.parse_args()
    for ref in (args.storepass_ref, args.keypass_ref):
        if ref is not None:
            if not re.fullmatch(r"env:[A-Z][A-Z0-9_]*", ref):
                raise ValueError("Signing password refs must be environment variable names.")
            if not os.environ.get(ref[4:]):
                raise ValueError("Required signing secret environment variable is missing.")

    android = args.tools / "android/android-15"
    zipalign = android / "zipalign"
    signer = android / "lib/apksigner.jar"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.unsigned_output.parent.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="tayson-release-") as tmpdir:
        tmp = Path(tmpdir)
        icon_data = build_icon_resources(args.icon_source, tmp / "icons")
        rewritten = tmp / "rewritten.apk"

        with zipfile.ZipFile(args.apk, "r") as zin, zipfile.ZipFile(rewritten, "w") as zout:
            names = set(zin.namelist())
            if not EXPECTED_ICONS.issubset(names):
                raise ValueError("Base APK is missing launcher resources")
            for item in zin.infolist():
                name = item.filename
                upper = name.upper()
                if name == "META-INF/MANIFEST.MF" or (
                    name.startswith("META-INF/") and upper.endswith((".RSA", ".SF", ".DSA", ".EC"))
                ):
                    continue
                if name == "AndroidManifest.xml":
                    data = patch_manifest(zin.read(name), args.old_package, args.new_package)
                elif name in icon_data:
                    data = icon_data[name]
                else:
                    data = zin.read(name)
                zi = zipfile.ZipInfo(name, item.date_time)
                zi.compress_type = item.compress_type
                zi.comment = item.comment
                zi.extra = item.extra
                zi.create_system = item.create_system
                zi.external_attr = item.external_attr
                zi.internal_attr = item.internal_attr
                zi.flag_bits = item.flag_bits
                zout.writestr(zi, data)

        for path in (args.unsigned_output, args.output, Path(str(args.output) + ".idsig")):
            try:
                path.unlink()
            except FileNotFoundError:
                pass

        run(zipalign, "-f", "-p", "4", rewritten, args.unsigned_output)
        run("java", "-jar", signer, "sign",
            "--ks", args.keystore,
            "--ks-key-alias", args.alias,
            "--ks-pass", args.storepass_ref or ("pass:" + args.storepass),
            "--key-pass", args.keypass_ref or ("pass:" + args.keypass),
            "--out", args.output, args.unsigned_output)
        run("java", "-jar", signer, "verify", "--verbose", args.output)

    with zipfile.ZipFile(args.output, "r") as final_zip, zipfile.ZipFile(args.apk, "r") as base_zip:
        if final_zip.testzip() is not None:
            raise ValueError("Final APK ZIP integrity failed")
        manifest = final_zip.read("AndroidManifest.xml")
        text = manifest.decode("utf-16-le", errors="ignore")
        for required in (
            args.new_package,
            args.new_package + ".DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION",
            args.new_package + ".androidx-startup",
            MONO_PROVIDER,
        ):
            if required not in text:
                raise ValueError("Required manifest identity missing: " + required)
        for name in ("classes.dex", "classes2.dex", "assets/template.elf", "resources.arsc"):
            if final_zip.read(name) != base_zip.read(name):
                raise ValueError(name + " changed while applying release identity")
        for name, expected in icon_data.items():
            if final_zip.read(name) != expected:
                raise ValueError("Launcher resource replacement failed: " + name)

    sha256 = hashlib.sha256(args.output.read_bytes()).hexdigest()
    icon_sha = hashlib.sha256(args.icon_source.read_bytes()).hexdigest()
    if args.build_info and args.build_info.exists():
        details = json.loads(args.build_info.read_text())
        details["apk"] = args.output.name
        details["bytes"] = args.output.stat().st_size
        details["sha256"] = sha256
        details["package"] = args.new_package
        details["previous_package"] = args.old_package
        details["package_identity_changed"] = True
        details["mono_runtime_provider_class_preserved"] = True
        details["launcher_icon_resources_replaced"] = 20
        details["launcher_icon_source_sha256"] = icon_sha
        args.build_info.write_text(json.dumps(details, indent=2) + "\n")

    print(json.dumps({
        "apk": args.output.name,
        "sha256": sha256,
        "package": args.new_package,
        "previousPackage": args.old_package,
        "launcherResources": 20,
        "launcherSourceSha256": icon_sha,
        "monoRuntimeProviderPreserved": True,
    }))


if __name__ == "__main__":
    main()
