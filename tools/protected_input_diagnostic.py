#!/usr/bin/env python3
"""Read-only provenance diagnosis, NOT a replacement controller/native launch gate.

Only the pinned original baseline and installed guest log are protected inputs.
Never reads log text, process environments/arguments or unrelated file contents.
O_NOATIME is mandatory: no permission fallback, cache changes or repair options.
"""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import time

SOURCE = Path(__file__).resolve().parents[1]
ROUNDS = SOURCE.parent / 'rounds'
ORIGINAL = ROUNDS / '000014-work/protected-before.json'
ORIGINAL_SHA256 = '25064d49e10f09a2fbe6c6993d30a00ce91a7e9d6dcd56ef367035eb852bcdde'
INSTALLED = Path.home() / '.steam/root/steamapps/common/My Winter Car'
LOG_NAME = 'BepInEx/LogOutput-guest.log'
CHUNK_SIZE = 1024 * 1024
HISTORY = (
    '000014-work/protected-hash-stability.json',
    '000024-work/pane-static/differing-input-stability.json',
    '000024-work/pane-replay/differing-input-stability.json',
    '000024-work/final-log-descriptor-stability.json',
)


def utc():
    return datetime.now(timezone.utc).isoformat(timespec='microseconds')


def save_json(path, data):
    with path.open('x') as stream:
        json.dump(data, stream, indent=2)
        stream.write('\n')


def create_output(run, name):
    if (not run.is_absolute() or run != run.resolve(strict=True)
            or run.parent != ROUNDS.resolve(strict=True)
            or not (run / 'contract.json').is_file()
            or (os.environ.get('RUN') and str(run) != os.environ['RUN'])):
        raise ValueError('--run must be the actual absolute assigned RUN, without symlinks')
    if not name or name in ('.', '..') or Path(name).name != name:
        raise ValueError('Evidence name must be a new leaf directory')
    output = run / name
    output.mkdir()  # Exclusive: previous or symlinked evidence is never replaced.
    return output


def metadata(value):
    return dict(device=value.st_dev, device_major=os.major(value.st_dev),
                device_minor=os.minor(value.st_dev), inode=value.st_ino,
                size=value.st_size, atime_ns=value.st_atime_ns,
                mtime_ns=value.st_mtime_ns, ctime_ns=value.st_ctime_ns,
                mode=value.st_mode, permissions=stat.filemode(value.st_mode),
                uid=value.st_uid, gid=value.st_gid, links=value.st_nlink,
                blocks=value.st_blocks, block_size=value.st_blksize)


def path_observation(path):
    return dict(utc=utc(), requested_path=str(path),
                canonical_path=str(path.resolve(strict=True)),
                lstat=metadata(path.lstat()), stat=metadata(path.stat()))


def same_path(a, b):
    return all(a.get(k) == b.get(k) for k in ('requested_path', 'canonical_path', 'lstat', 'stat'))


@contextmanager
def readonly_descriptor(path):
    # Refuse FIFOs/devices before open and validate the actual opened inode too.
    if not stat.S_ISREG(path.stat().st_mode):
        raise ValueError('Only regular files are permitted')
    flags = os.O_RDONLY | os.O_CLOEXEC | os.O_NONBLOCK | os.O_NOATIME
    fd = os.open(path, flags)
    try:
        if not stat.S_ISREG(os.fstat(fd).st_mode):
            raise ValueError('Opened descriptor is not a regular file')
        yield fd, flags
    finally:
        os.close(fd)


def begin_descriptor(row, fd, flags):
    row.update(open_flags=flags, descriptor_before=metadata(os.fstat(fd)),
               descriptor_target=os.readlink('/proc/self/fd/' + str(fd)),
               fdinfo_before=Path('/proc/self/fdinfo/' + str(fd)).read_text())


def end_descriptor(row, fd, path):
    row.update(descriptor_after=metadata(os.fstat(fd)),
               fdinfo_after=Path('/proc/self/fdinfo/' + str(fd)).read_text(),
               after=path_observation(path))
    row['identity_stable'] = (same_path(row['before'], row['after'])
        and row['before']['stat'] == row['descriptor_before'] == row['descriptor_after']
        == row['after']['stat'] and row['descriptor_target'] == row['before']['canonical_path'])


def hash_read(path, chunk_size=CHUNK_SIZE, capture=None):
    if chunk_size <= 0:
        raise ValueError('chunk_size must be positive')
    row = dict(started_utc=utc(), path=str(path), chunk_size=chunk_size,
               reader='os.read/O_RDONLY|O_NOATIME; hashlib.sha256', identity_stable=False)
    try:
        row['before'] = path_observation(path)
        whole = hashlib.sha256()
        chunks = []
        total = 0
        with readonly_descriptor(path) as (fd, flags):
            begin_descriptor(row, fd, flags)
            # Limit reads to the initial size plus one; a growing file cannot run forever.
            limit = row['descriptor_before']['size'] + 1
            while total < limit:
                block = bytearray()
                wanted = min(chunk_size, limit - total)
                while len(block) < wanted:
                    data = os.read(fd, wanted - len(block))
                    if not data:
                        break
                    block.extend(data)
                if not block:
                    break
                whole.update(block)
                chunks.append(dict(index=len(chunks), start=total, end_exclusive=total + len(block),
                                   sha256=hashlib.sha256(block).hexdigest()))
                total += len(block)
                if capture is not None:
                    if total > 2 * CHUNK_SIZE:
                        raise ValueError('JSON receipt exceeds capture limit')
                    capture.append(bytes(block))
            end_descriptor(row, fd, path)
        row.update(sha256=whole.hexdigest(), chunks=chunks, bytes_read=total)
        row['identity_stable'] &= total == row['descriptor_before']['size']
    except (OSError, ValueError, AttributeError) as error:
        row.update(error=type(error).__name__ + ': ' + str(error), identity_stable=False)
    row['ended_utc'] = utc()
    return row


def json_read(path):
    captured = []
    receipt = hash_read(path, capture=captured)
    if not receipt['identity_stable']:
        return receipt, None
    try:
        return receipt, json.loads(b''.join(captured))
    except (ValueError, UnicodeError) as error:
        receipt.update(error=str(error), identity_stable=False)
        return receipt, None


def external_hash(path, output, name, kind, timeout=120):
    if kind not in ('sha256sum', 'openssl'):
        raise ValueError('Only independent read-only SHA256 commands are permitted')
    if not name or Path(name).name != name or name in ('.', '..'):
        raise ValueError('Receipt name must be a leaf')
    log = output / (name + '.log')
    receipt_path = output / (name + '.command.json')
    # Reserve both artifacts before a read, even on command failure/timeout.
    with receipt_path.open('x') as receipt_stream, log.open('xb') as log_stream:
        argv = ['sha256sum', '-'] if kind == 'sha256sum' else ['openssl', 'dgst', '-sha256']
        row = dict(argv=argv, cwd=str(SOURCE), started_utc=utc(), path=str(path),
                   executable_path=shutil.which(argv[0]),
                   stdin_source='inherited O_RDONLY|O_NOATIME descriptor, not reopened path',
                   output=str(log), exit_code=1, identity_stable=False, timeout_seconds=timeout)
        try:
            row['before'] = path_observation(path)
            with readonly_descriptor(path) as (fd, flags):
                begin_descriptor(row, fd, flags)
                result = subprocess.run(argv, cwd=SOURCE, stdin=fd, stdout=subprocess.PIPE,
                                        stderr=subprocess.STDOUT, timeout=timeout, check=False)
                raw = result.stdout
                row['exit_code'] = result.returncode
                row['bytes_read'] = os.lseek(fd, 0, os.SEEK_CUR)
                end_descriptor(row, fd, path)
            match = re.fullmatch(rb'([0-9a-f]{64})  -\n', raw) if kind == 'sha256sum' else re.fullmatch(
                rb'(?:SHA2?-256|SHA256)\(stdin\)= ([0-9a-f]{64})\n', raw)
            if result.returncode != 0 or not match:
                raise ValueError('Independent digest command failed or returned unexpected output')
            row['sha256'] = match.group(1).decode('ascii')
            row['identity_stable'] &= row['bytes_read'] == row['descriptor_before']['size']
        except (OSError, ValueError, AttributeError, subprocess.TimeoutExpired) as error:
            raw = getattr(error, 'stdout', None) or locals().get('raw', b'')
            row.update(error=type(error).__name__ + ': ' + str(error), identity_stable=False)
            if row['exit_code'] == 0:
                row['exit_code'] = 1
        log_stream.write(raw)
        row.update(ended_utc=utc(), output_sha256=hashlib.sha256(raw).hexdigest())
        json.dump(row, receipt_stream, indent=2)
        receipt_stream.write('\n')
    return row


def reads_stable(reads):
    return (len(reads) >= 2 and all(r.get('identity_stable') and 'sha256' in r for r in reads)
            and len({r['sha256'] for r in reads}) == 1
            and all(same_path(a['after'], b['before']) for a, b in zip(reads, reads[1:])))


def chunk_changes(first, second):
    a, b = first.get('chunks', []), second.get('chunks', [])
    rows = []
    for index in range(max(len(a), len(b))):
        left = a[index] if index < len(a) else None
        right = b[index] if index < len(b) else None
        if left != right:
            present = [r for r in (left, right) if r is not None]
            rows.append(dict(index=index, start=min(r['start'] for r in present),
                             end_exclusive=max(r['end_exclusive'] for r in present),
                             before=left, after=right))
    return rows


def classify(expected, reads, baseline_pinned, historical_digests, errors):
    stable = reads_stable(reads)
    matches = bool(reads) and all(r.get('sha256') == expected for r in reads)
    blockers = list(errors)
    if not baseline_pinned:
        blockers.append('original baseline pin, descriptor or before/after evidence is not intact')
    if not stable:
        blockers.append('current read/descriptor instability or incomplete independent reads')
    if not matches:
        blockers.append('current digests do not equal the original protected digest; stability is not equality')
    historical_drift = any(sha != expected for sha in historical_digests)
    if historical_drift:
        blockers.append('historical differing reads remain unexplained: no original-time descriptor/chunk bytes '
                        'or independent storage/memory provenance reconciles them')
    return dict(current_reads_stable=stable, original_digest_matches=matches,
                historical_drift_unresolved=historical_drift, baseline_pinned_unchanged=baseline_pinned,
                reconciled=not blockers, exit_code=1 if blockers else 0, blockers=blockers,
                native_launch_allowed=False,
                controller_follow_on='readiness-only may be scheduled by controller after independent review'
                    if not blockers else 'native launch PROHIBITED; independent provenance recovery only')


def mount_observation(path):
    row = dict(utc=utc(), path=str(path))
    try:
        with readonly_descriptor(path) as (fd, _):
            info = Path('/proc/self/fdinfo/' + str(fd)).read_text()
            mount_id = re.search(r'^mnt_id:\s*(\d+)$', info, re.M).group(1)
            row.update(fdinfo=info, mount_id=int(mount_id), descriptor=metadata(os.fstat(fd)),
                       mountinfo=[line for line in Path('/proc/self/mountinfo').read_text().splitlines()
                                  if line.split(' ', 1)[0] == mount_id])
            vfs = os.fstatvfs(fd)
            row['statvfs'] = {k: getattr(vfs, k) for k in ('f_bsize', 'f_frsize', 'f_blocks',
                'f_bfree', 'f_bavail', 'f_files', 'f_ffree', 'f_favail', 'f_flag', 'f_namemax', 'f_fsid')}
            device = row['descriptor']
            link = Path('/sys/dev/block') / (str(device['device_major']) + ':' + str(device['device_minor']))
            row['sys_device_path'] = str(link)
            row['sys_device_target'] = str(link.resolve(strict=True)) if link.exists() else None
            row['sys_device_limit'] = 'no direct block-device mapping does not identify underlying storage health'
    except (OSError, ValueError, AttributeError) as error:
        row['error'] = type(error).__name__ + ': ' + str(error)
    return row


def fd_observation(paths, timeout=15):
    """Metadata-only /proc scan. No environ, cmdline, maps, memory or unrelated contents."""
    row = dict(started_utc=utc(), matching_fds=[], processes_scanned=0, fds_scanned=0,
               inaccessible_processes=[], vanished_entries=0, other_errors=[], complete=True,
               limit='point-in-time permitted FD visibility is not proof of no past writer or storage fault')
    identities = {}
    for path in paths:
        try:
            observed = path.stat()
            identities[(observed.st_dev, observed.st_ino)] = str(path)
        except OSError as error:
            row['other_errors'].append(dict(path=str(path), error=str(error)))
    deadline = time.monotonic() + timeout
    for proc in Path('/proc').iterdir():
        if not proc.name.isdigit():
            continue
        if time.monotonic() > deadline:
            row['complete'] = False
            row['other_errors'].append('bounded scan timed out')
            break
        row['processes_scanned'] += 1
        try:
            for entry in (proc / 'fd').iterdir():
                row['fds_scanned'] += 1
                if time.monotonic() > deadline:
                    row['complete'] = False
                    break
                try:
                    st = entry.stat()
                    target = identities.get((st.st_dev, st.st_ino))
                    if target:
                        info = (proc / 'fdinfo' / entry.name).read_text()
                        flag_match = re.search(r'^flags:\s*(\d+)$', info, re.M)
                        flags = int(flag_match.group(1), 8) if flag_match else None
                        row['matching_fds'].append(dict(pid=int(proc.name), fd=int(entry.name),
                            target=target, descriptor_target=os.readlink(entry), fdinfo=info,
                            access_mode=None if flags is None else flags & os.O_ACCMODE,
                            process_name=(proc / 'comm').read_text().strip()))
                except FileNotFoundError:
                    row['vanished_entries'] += 1
                except PermissionError:
                    row['inaccessible_processes'].append(int(proc.name))
        except PermissionError:
            row['inaccessible_processes'].append(int(proc.name))
        except FileNotFoundError:
            row['vanished_entries'] += 1
        except OSError as error:
            row['other_errors'].append(dict(pid=int(proc.name), error=str(error)))
    row['inaccessible_processes'] = sorted(set(row['inaccessible_processes']))
    row['complete'] &= not row['inaccessible_processes'] and not row['other_errors']
    row['ended_utc'] = utc()
    return row


def history_observations(output, target, current):
    histories, digests, errors = [], [], []
    for index, name in enumerate(HISTORY):
        path = ROUNDS / name
        receipt, data = json_read(path)
        # Raw JSON bytes (never log contents), independently read and hashed now.
        save_json(output / ('history-%02d-read.json' % index), receipt)
        if data is None:
            errors.append('cannot independently read historical receipt: ' + name)
            continue
        passes = data.get(str(target), data).get('passes', [])
        if not passes:
            errors.append('historical receipt has no target passes: ' + name)
        comparisons = []
        for old in passes:
            if 'sha256' in old:
                digests.append(old['sha256'])
            size = old.get('bytes_read', old.get('before', {}).get('size'))
            chunk_size = old.get('chunk_size', CHUNK_SIZE)
            # If an old receipt omitted size, do not invent its final range from
            # today's file size. Preserve missing original descriptor evidence.
            normalized = None
            if size is not None:
                normalized = dict(chunks=[dict(index=i, start=i * chunk_size,
                    end_exclusive=min((i + 1) * chunk_size, size), sha256=sha)
                    for i, sha in enumerate(old.get('chunks', []))])
            comparisons.append(dict(utc=old.get('utc'), sha256=old.get('sha256'),
                descriptor_before=old.get('descriptor_before'), descriptor_after=old.get('descriptor_after'),
                canonical_path=old.get('resolved_path'), identity_stable=old.get('identity_stable'),
                size=size, chunk_size=chunk_size,
                changed_indices=[i for i in range(max(len(old.get('chunks', [])), len(current.get('chunks', []))))
                    if (old.get('chunks', [])[i] if i < len(old.get('chunks', [])) else None) !=
                    (current['chunks'][i]['sha256'] if i < len(current.get('chunks', [])) else None)],
                changed_ranges=chunk_changes(normalized, current) if normalized else None,
                missing_range_reason=None if normalized else 'historical byte count/size was not recorded'))
        histories.append(dict(path=str(path), file_sha256=receipt['sha256'], observations=comparisons,
                              source_claim_stable=data.get(str(target), data).get('stable')))
    return histories, digests, errors


def diagnose(run, name, baseline):
    if baseline != ORIGINAL or baseline.resolve(strict=True) != ORIGINAL:
        raise ValueError('Must use the exact original baseline, never a replacement or alias')
    output = create_output(run, name)
    target = INSTALLED / LOG_NAME
    started = utc()
    errors = []
    baseline_before, original_data = json_read(baseline)
    save_json(output / 'baseline-before.json', baseline_before)
    pin_before = baseline_before.get('sha256') == ORIGINAL_SHA256 and baseline_before['identity_stable']
    if not pin_before or original_data is None:
        report = classify(None, [], False, [], ['pinned baseline unavailable; target read not attempted'])
        report.update(started_utc=started, ended_utc=utc(), baseline=str(baseline),
                      expected_baseline_sha256=ORIGINAL_SHA256)
        save_json(output / 'report.json', report)
        return report
    expected = original_data[str(INSTALLED)][LOG_NAME]
    if not re.fullmatch('[0-9a-f]{64}', expected):
        raise ValueError('Invalid original digest')
    mounts_before = [mount_observation(p) for p in (baseline, target)]
    fds_before = fd_observation([baseline, target])
    save_json(output / 'mounts-before.json', mounts_before)
    save_json(output / 'fds-before.json', fds_before)
    first = hash_read(target)
    save_json(output / 'target-python-before.json', first)
    external = [external_hash(target, output, kind + '-target', kind) for kind in ('sha256sum', 'openssl')]
    second = hash_read(target)
    save_json(output / 'target-python-after.json', second)
    histories, historical_digests, history_errors = history_observations(output, target, second)
    errors.extend(history_errors)
    independent_baseline = external_hash(baseline, output, 'sha256sum-baseline', 'sha256sum')
    baseline_after = hash_read(baseline)
    save_json(output / 'baseline-after.json', baseline_after)
    mounts_after = [mount_observation(p) for p in (baseline, target)]
    fds_after = fd_observation([baseline, target])
    save_json(output / 'mounts-after.json', mounts_after)
    save_json(output / 'fds-after.json', fds_after)
    mount_stable = all('error' not in a and 'error' not in b and all(a.get(k) == b.get(k)
        for k in ('descriptor', 'mount_id', 'mountinfo')) for a, b in zip(mounts_before, mounts_after))
    if not mount_stable:
        errors.append('supplemental mount/descriptor observations incomplete or changed')
    baseline_reads = [baseline_before, independent_baseline, baseline_after]
    pinned = reads_stable(baseline_reads) and all(r.get('sha256') == ORIGINAL_SHA256 for r in baseline_reads)
    reads = [first] + external + [second]
    errors.extend(r['error'] for r in reads + baseline_reads if 'error' in r)
    report = classify(expected, reads, pinned, historical_digests, errors)
    report.update(started_utc=started, ended_utc=utc(), argv=[sys.executable] + sys.argv,
        cwd=str(Path.cwd()), evidence_level='read-only protected-input provenance; NOT native gameplay',
        baseline=str(baseline), expected_baseline_sha256=ORIGINAL_SHA256,
        expected_original_log_sha256=expected, installed_log=str(target),
        canonical_log_path=first.get('before', {}).get('canonical_path'),
        mount_descriptor_observations_stable=mount_stable,
        fd_visibility=[dict(complete=r['complete'], matching_fds=r['matching_fds'],
            inaccessible_process_count=len(r['inaccessible_processes']),
            processes_scanned=r['processes_scanned'], fds_scanned=r['fds_scanned']) for r in (fds_before, fds_after)],
        process_observation_limit='Permission-denied or absent visible FDs cannot establish absence of past writers',
        reader_runtime=dict(python_version=sys.version, executable=sys.executable,
            canonical_executable=str(Path(sys.executable).resolve()), platform=list(os.uname())),
        current_sha256=[r.get('sha256') for r in reads], current_chunk_changes=chunk_changes(first, second),
        historical=histories, original_time_descriptor_and_chunks='NOT RECORDED in original baseline',
        independence_limit='Python/OpenSSL/coreutils are separate reads/commands on the same kernel/cache/storage; '
            'not independent historical bytes or a storage/memory health test',
        cleanup=dict(native_processes_started=[], processes_signaled=[], rig_writes=[],
                     protected_writes=[], baseline_writes=[], controller_writes=[], settings_writes=[],
                     descriptors_closed=True, hash_subprocesses_waited=True,
                     note='No native/rig resources created; no native cleanup test claimed'),
        dimensions={key: 'NOT_TESTED' for key in ('native_injected_state', 'ordinary_input', 'Steam_two_PC',
            'different_saves', 'native_rejoin', 'native_save_reload', 'four_player_soak')},
        V11_status='partial, unchanged', H07_status='partial, unchanged')
    save_json(output / 'report.json', report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--baseline', required=True, type=Path)
    parser.add_argument('--name', required=True, help='new evidence directory under assigned RUN')
    args = parser.parse_args()
    report = diagnose(args.run, args.name, args.baseline)
    print(json.dumps({k: report.get(k) for k in ('exit_code', 'reconciled', 'current_reads_stable',
        'original_digest_matches', 'baseline_pinned_unchanged', 'current_sha256', 'blockers',
        'native_launch_allowed', 'controller_follow_on')}, indent=2))
    return report['exit_code']


if __name__ == '__main__':
    raise SystemExit(main())
