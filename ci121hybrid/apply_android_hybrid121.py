from pathlib import Path

root = Path('.')
main = root / 'app/src/main/java/com/tayson/ps4linksender/MainActivity.kt'
gradle = root / 'app/build.gradle.kts'
loader = root / 'app/src/main/java/com/tayson/ps4linksender/ps4/PayloadLoaderClient.kt'
relay = root / 'app/src/main/java/com/tayson/ps4linksender/relay/RelayHttpServer.kt'

# Version bump.
g = gradle.read_text()
g = g.replace('versionCode = 17', 'versionCode = 18')
g = g.replace('versionName = "1.2.0"', 'versionName = "1.2.1"')
gradle.write_text(g)

# UI: identify resilient payload build and explain automatic resume.
s = main.read_text()
s = s.replace('1.2.0 HYBRID • BGFT Receiver + Payload Installer',
              '1.2.1 HYBRID • BGFT Receiver + Payload Installer 0.1.1')
s = s.replace('Payload não testado. Ative o BinLoader Server no GoldHEN para carregá-lo pelo celular.',
              'Payload 0.1.1 não testado. Ative o BinLoader Server no GoldHEN para carregá-lo pelo celular.')
s = s.replace(
    'O PKG é salvo temporariamente em /data/tayson/payload-cache/current.pkg e depois entregue ao instalador local do PS4. O caminho BGFT continua disponível acima.',
    'O PKG é salvo temporariamente em /data/tayson/payload-cache/current.pkg e depois entregue ao instalador local do PS4. O Payload 0.1.1 retoma automaticamente transferências interrompidas usando Range. O caminho BGFT continua disponível acima.'
)
main.write_text(s)

# Embed/load Payload Installer 0.1.1.
l = loader.read_text()
l = l.replace('tayson_payload_installer_0_1_0.bin', 'tayson_payload_installer_0_1_1.bin')
loader.write_text(l)

# Be more tolerant of temporary CDN stalls; the PS4 side will reconnect with Range on EOF.
r = relay.read_text()
r = r.replace('s.soTimeout = 30_000', 's.soTimeout = 120_000')
r = r.replace('connectTimeout = 20_000', 'connectTimeout = 30_000')
r = r.replace('readTimeout = 45_000', 'readTimeout = 120_000')
relay.write_text(r)

assert 'versionCode = 18' in gradle.read_text()
assert 'versionName = "1.2.1"' in gradle.read_text()
assert 'Payload Installer 0.1.1' in main.read_text()
assert 'tayson_payload_installer_0_1_1.bin' in loader.read_text()
assert 'readTimeout = 120_000' in relay.read_text()
