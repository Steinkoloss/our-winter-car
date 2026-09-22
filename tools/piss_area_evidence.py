"""H15 command receipts, including the production-linked bridge binary. No native launch."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import time
from fuel_transfer_evidence import ROOT, manifest


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(output, command):
    output.mkdir(parents=True, exist_ok=False)
    source = manifest()
    started = time.time()
    code = 125
    with (output / 'raw.log').open('w') as log:
        try:
            code = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=600).returncode
        except (OSError, subprocess.TimeoutExpired) as error:
            log.write('\nRUNNER ERROR: ' + str(error) + '\n')
    binaries = {}
    for base in ('src', 'tools'):
        for path in (ROOT / base).glob('*/bin/Release/**/*.dll'):
            if path.name.startswith('WinterMP.') or path.name.endswith('.Tests.dll'):
                binaries[str(path.relative_to(ROOT))] = digest(path)
    (output / 'receipt.json').write_text(json.dumps({
        'cwd': str(ROOT), 'argv': command, 'exit_code': code,
        'started_unix': started, 'seconds': time.time()-started,
        'source_sha256': source, 'binaries': binaries,
        'raw_sha256': digest(output / 'raw.log'),
        'evidence_level': 'portable/static/build only; no native game process',
    }, indent=2) + '\n')
    print(output, 'exit', code)
    print((output / 'raw.log').read_text()[-3500:])
    return code


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    raise SystemExit(run(args.output, command))
