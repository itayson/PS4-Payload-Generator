#!/usr/bin/env python3
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path.cwd().resolve()
planner = (root / 'app/src/main/java/com/tayson/ps4linksender/compat/BgftRoutePlanner.kt').read_text()
main = (root / 'app/src/main/java/com/tayson/ps4linksender/MainActivity.kt').read_text()
http = (root / 'app/src/main/java/com/tayson/ps4linksender/net/Http.kt').read_text()

checks = {
    'route planner exists': 'object BgftRoutePlanner' in planner,
    'generic pkg fragment rule': 'pathEndsInPkg(uri)' in planner and 'ensurePkgFragment(clean)' in planner,
    'Viking R2 detection': 'r2.cloudflarestorage.com' in planner and 'X-Amz-Signature' in planner,
    'Worker short route': '/api/routes' in planner and 'VIKING_WORKER_SHORT' in planner,
    'inline fallback': '/viking.pkg?u=' in planner and 'VIKING_WORKER_INLINE' in planner,
    'temporary URL expiry guard': 'EXPIRY_SAFETY_SECONDS' in planner,
    'worker strips request context': 'requestContext = if (viaWorker) null' in planner,
    'JSON POST support': 'fun postJson' in http,
    'single send uses routed URL': 'route.transportPkg(fresh)' in main,
    'queue send uses routed URL': 'BgftRoutePlanner.prepare(item.provider' in main,
}

failed = [name for name, ok in checks.items() if not ok]
for name, ok in checks.items():
    print(('OK   ' if ok else 'FAIL ') + name)
if failed:
    raise SystemExit(f'{len(failed)} BGFT checks failed')
print(f'{len(checks)}/{len(checks)} BGFT checks passed')
