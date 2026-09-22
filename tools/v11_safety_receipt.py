#!/usr/bin/env python3
"""Read-only V11 receipt comparison; release only this round's idle owner marker."""
import argparse
import fcntl
import hashlib
import json
import os
from itertools import zip_longest
from pathlib import Path
import v11_pane_audit as audit
import v11_rendered as rendered


def differences(before, after):
    return {root: {name: {'before': before.get(root, {}).get(name), 'after': after.get(root, {}).get(name)}
                   for name in sorted(set(before.get(root, {})) | set(after.get(root, {})))
                   if before.get(root, {}).get(name) != after.get(root, {}).get(name)}
            for root in sorted(set(before) | set(after))
            if before.get(root) != after.get(root)}


def stat_metadata(value):
    return dict(device=value.st_dev, inode=value.st_ino, size=value.st_size,
                mtime_ns=value.st_mtime_ns, ctime_ns=value.st_ctime_ns,
                mode=value.st_mode, links=value.st_nlink)


def hash_pass(path, chunk_size=1024 * 1024):
    """Bind each digest to its open descriptor, not just a possibly replaced path."""
    if chunk_size <= 0: raise ValueError('chunk_size must be positive')
    result = dict(utc=audit.utc(), path=str(path), chunk_size=chunk_size)
    try:
        result['before'] = stat_metadata(path.stat())
        result['resolved_path'] = str(path.resolve(strict=True))
        whole = hashlib.sha256()
        chunks = []
        byte_count = 0
        with path.open('rb') as stream:
            result['descriptor_before'] = stat_metadata(os.fstat(stream.fileno()))
            # Linux provenance is supplemental: unavailable /proc is recorded,
            # never mistaken for a different file or silently omitted.
            try:
                result['descriptor_target'] = os.readlink('/proc/self/fd/' + str(stream.fileno()))
                result['fdinfo'] = Path('/proc/self/fdinfo/' + str(stream.fileno())).read_text()
            except OSError as error:
                result['descriptor_provenance_error'] = str(error)
            for chunk in iter(lambda: stream.read(chunk_size), b''):
                whole.update(chunk)
                chunks.append(hashlib.sha256(chunk).hexdigest())
                byte_count += len(chunk)
            result['descriptor_after'] = stat_metadata(os.fstat(stream.fileno()))
        result.update(sha256=whole.hexdigest(), chunks=chunks, bytes_read=byte_count)
        result['after'] = stat_metadata(path.stat())
        result['identity_stable'] = (result['before'] == result['descriptor_before']
                                     == result['descriptor_after'] == result['after']
                                     and byte_count == result['after']['size'])
    except OSError as error:
        result.update(error=type(error).__name__ + ': ' + str(error), identity_stable=False)
    return result


def changed_chunks(first, second):
    # zip alone hides appended/truncated tails, even when the whole hash differs.
    return [i for i, (a, b) in enumerate(zip_longest(first, second)) if a != b]


def stability_receipt(path):
    passes = [hash_pass(path) for _ in range(2)]
    complete = all('error' not in item for item in passes)
    return dict(passes=passes,
                changed_chunk_indices=changed_chunks(passes[0]['chunks'], passes[1]['chunks']) if complete else None,
                stable=complete and all(item['identity_stable'] for item in passes)
                and passes[0]['sha256'] == passes[1]['sha256']
                and passes[0]['after'] == passes[1]['before'])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--release', action='store_true')
    parser.add_argument('--stability', action='store_true', help='Two read-only chunk/hash passes over differing protected inputs')
    args = parser.parse_args()
    audit.configure_run(args.run)
    before = json.loads((args.run / 'protected-before.json').read_text())
    with (audit.RIG / 'v11.lock').open('a') as lock, (audit.GAME / 'WinterMP/local2p.lock').open('a') as game_lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fcntl.flock(game_lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        receipts = {}
        remaining = []
        for file in args.run.glob('*/cleanup.json'):
            cleanup = json.loads(file.read_text())
            native = object.__new__(audit.NativeRun)
            native.token = cleanup['run_token']
            remaining.extend(native.owned())
            render_file = file.parent / 'render.json'
            if render_file.exists():
                render = json.loads(render_file.read_text())
                remaining.extend(rendered.owned(render['token']))
            after = json.loads((file.parent / 'protected-after.json').read_text())
            receipts[file.parent.name] = differences(before, after)
        current = audit.protected()
        if args.stability:
            stability = {}
            for root, files in differences(before, current).items():
                for name in files:
                    path = Path(root) / name
                    stability[str(path)] = stability_receipt(path)
            audit.save_json(args.run / 'protected-hash-stability.json', stability)
        marker = audit.RIG / 'v11-owner.txt'
        owner_matches = marker.exists() and marker.read_text() == audit.MARKER
        if args.release:
            assert owner_matches and not remaining, 'Never release another owner or a live run'
            assert not (audit.GAME / 'live-bag').exists(), 'Command bus still present'
            marker.unlink()
        receipt = dict(utc=audit.utc(), historical_differences=receipts, current_differences=differences(before, current),
                       protected_unchanged=before == current, owned_processes_remaining=remaining,
                       owner_marker_removed=not marker.exists(), command_bus_removed=not (audit.GAME / 'live-bag').exists(),
                       game_markers_removed=not any(audit.GAME.glob('wintermp-*-sandbox.txt')),
                       profile_markers_removed=not any(audit.RIG.glob('*/pfx/drive_c/users/*/AppData/LocalLow/Amistech/My Winter Car/wintermp-*-sandbox.txt')))
        audit.save_json(args.run / ('final-safety.json' if args.release else 'safety-stability-summary.json' if args.stability else 'safety-differences.json'), receipt)
        print(json.dumps(receipt, indent=2))
        raise SystemExit(0 if receipt['protected_unchanged'] and not remaining else 1)
