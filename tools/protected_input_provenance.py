#!/usr/bin/env python3
"""Bounded read-only provenance investigation; never a native launch gate.

No target-content reads, arbitrary commands, repairs, rig/process operations or
independence-by-repeated-hashing. Host observations cannot recreate lost original
bytes. The unresolved outcome is useful evidence, not permission to launch.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

import protected_input_diagnostic as diag

HISTORY = {
    'report': diag.ROUNDS / '000026-work/integrity-final/report.json',
    'before': diag.ROUNDS / '000026-work/integrity-final/target-python-before.json',
    'after': diag.ROUNDS / '000026-work/integrity-final/target-python-after.json',
    'earlier-report': diag.ROUNDS / '000026-work/integrity-1/report.json',
    **{'older-%02d' % i: diag.ROUNDS / name for i, name in enumerate(diag.HISTORY)},
    'prior-provenance': diag.ROUNDS / '000028-work/provenance-final/report.json',
    'prior-host': diag.ROUNDS / '000028-work/provenance-final/host-observations.json',
}
DIMENSIONS = ('native_injected_state', 'ordinary_input', 'Steam_two_PC', 'different_saves',
              'native_rejoin', 'native_save_reload', 'four_player_soak', 'native_discovery',
              'host_action', 'guest_action', 'authority_once', 'matching_result', 'late_join')


def create_output(run, name):
    if not os.environ.get('RUN') or os.environ['RUN'] != str(run):
        raise ValueError('Explicit RUN must equal the actual assigned --run')
    return diag.create_output(run, name)


def leaf(name):
    if not name or name in ('.', '..') or Path(name).name != name:
        raise ValueError('Receipt name must be a leaf')


def target_metadata(path):
    """Bind identity to a read-only descriptor without reading any log bytes."""
    row = dict(started_utc=diag.utc(), bytes_read=0, identity_stable=False,
               evidence_level='current metadata only; not current content equality')
    try:
        row['before'] = diag.path_observation(path)
        with diag.readonly_descriptor(path) as (fd, flags):
            diag.begin_descriptor(row, fd, flags)
            row['bytes_read'] = os.lseek(fd, 0, os.SEEK_CUR)
            diag.end_descriptor(row, fd, path)
        row['mount'] = diag.mount_observation(path)
        validate_metadata_mount(row)
    except (OSError, ValueError, AttributeError) as error:
        row.update(error=type(error).__name__ + ': ' + str(error), identity_stable=False)
    row['ended_utc'] = diag.utc()
    return row


def validate_metadata_mount(row):
    """Validate recorded metadata only, without opening any described path."""
    try:
        mount = row['mount']
        records = mount['mountinfo']
        if (not isinstance(records, list) or len(records) != 1
                or type(mount['mount_id']) is not int or 'error' in mount
                or type(row['bytes_read']) is not int or row['bytes_read'] != 0
                or mount['descriptor'] != row['descriptor_after']):
            raise ValueError('Incomplete or ambiguous descriptor/mount metadata')
        left, right = records[0].split(' - ', 1)
        fields = left.split()
        if (len(fields) < 6 or len(right.split()) < 3
                or int(fields[0]) != mount['mount_id'] or not fields[4].startswith('/')):
            raise ValueError('Descriptor mount record identity is invalid')
        # Btrfs st_dev can differ from mountinfo's device; do not infer hardware
        # identity or health from either. Bind using fdinfo's mount ID and inode.
        for info in (row['fdinfo_before'], row['fdinfo_after'], mount['fdinfo']):
            values = unique_object(line.split(':', 1) for line in info.splitlines())
            flags = int(values['flags'].strip(), 8)
            if (int(values['pos']) != 0 or int(values['mnt_id']) != mount['mount_id']
                    or int(values['ino']) != row['descriptor_after']['inode']
                    or flags & os.O_ACCMODE != os.O_RDONLY or not flags & os.O_NOATIME):
                raise ValueError('Metadata descriptor position, access or identity mismatch')
    except (KeyError, TypeError, IndexError, AttributeError) as error:
        raise ValueError('Descriptor/mount evidence unavailable or malformed') from error


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON key: ' + key)
        result[key] = value
    return result


def invalid_constant(value):
    raise ValueError('Nonfinite JSON constant: ' + value)


def finite_float(value):
    result = float(value)
    if not math.isfinite(result):
        invalid_constant(value)
    return result


def receipt_read(path, capture=None):
    # Fixed historical inputs must not redirect to protected text or grow into
    # unbounded reads. This also guards the after-read path, not only retention.
    try:
        if path != path.resolve(strict=True):
            raise ValueError('Historical receipt must not redirect')
        if path.suffix != '.json' or path.stat().st_size > 2 * diag.CHUNK_SIZE:
            raise ValueError('Expected bounded historical JSON receipt')
        return diag.hash_read(path, capture=[] if capture is None else capture)
    except (OSError, ValueError) as error:
        return dict(path=str(path), identity_stable=False, error=str(error), bytes_read=0)


def retain_json(path, output, name):
    """Keep exact historical JSON bytes plus a current noatime read receipt."""
    leaf(name)
    captured = []
    with (output / (name + '.raw.json')).open('xb') as raw, \
            (output / (name + '.read.json')).open('x') as receipt:
        row = receipt_read(path, capture=captured)
        payload = b''.join(captured)
        raw.write(payload)
        row['retained_bytes'] = len(payload)
        row['retained_sha256'] = hashlib.sha256(payload).hexdigest()
        data = None
        if row['identity_stable']:
            try:
                data = json.loads(payload, object_pairs_hook=unique_object, parse_constant=invalid_constant,
                                  parse_float=finite_float)
                if not isinstance(data, dict):
                    raise ValueError('Historical JSON must be an object')
            except (ValueError, UnicodeError, RecursionError) as error:
                data = None
                row.update(error=str(error), identity_stable=False)
        json.dump(row, receipt, indent=2)
        receipt.write('\n')
    return row, data


def observe_command(output, name, argv, timeout=20):
    leaf(name)
    with (output / (name + '.command.json')).open('x') as receipt, \
            (output / (name + '.stdout.log')).open('xb') as stdout, \
            (output / (name + '.stderr.log')).open('xb') as stderr:
        row = dict(argv=argv, cwd=str(diag.SOURCE), started_utc=diag.utc(),
                   executable_path=shutil.which(argv[0]), timeout_seconds=timeout,
                   exit_code=127, waited=True, timed_out=False, owned_child_killed_on_timeout=False,
                   stdout=str(Path(stdout.name)), stderr=str(Path(stderr.name)),
                   evidence_level='same-host observation, not independent original bytes')
        row['canonical_executable'] = (str(Path(row['executable_path']).resolve())
                                       if row['executable_path'] else None)
        out, err = b'', b''
        try:
            result = subprocess.run(argv, cwd=diag.SOURCE, stdin=subprocess.DEVNULL,
                                    capture_output=True, timeout=timeout, check=False)
            row['exit_code'] = result.returncode
            out, err = result.stdout, result.stderr
        except subprocess.TimeoutExpired as error:
            # subprocess.run kills and waits ONLY its own direct child on timeout.
            # Fixed probes do not spawn game/native descendants.
            out, err = error.stdout or b'', error.stderr or b''
            row.update(exit_code=124, timed_out=True, owned_child_killed_on_timeout=True, error=str(error))
        except OSError as error:
            row['error'] = type(error).__name__ + ': ' + str(error)
        stdout.write(out)
        stderr.write(err)
        row.update(ended_utc=diag.utc(), stdout_sha256=hashlib.sha256(out).hexdigest(),
                   stderr_sha256=hashlib.sha256(err).hexdigest())
        json.dump(row, receipt, indent=2)
        receipt.write('\n')
    return row


def mount_root(mount):
    try:
        if len(mount['mountinfo']) != 1:
            raise ValueError('Need exactly one descriptor-bound mount record')
        left, right = mount['mountinfo'][0].split(' - ', 1)
        fields = left.split()
        if int(fields[0]) != mount['mount_id'] or right.split()[0] != 'btrfs':
            raise ValueError('Descriptor mount identity or filesystem does not match')
        root = Path(re.sub(r'\\([0-7]{3})', lambda m: chr(int(m[1], 8)), fields[4]))
        if not root.is_absolute():
            raise ValueError('Mount point must be absolute')
        return root
    except (KeyError, IndexError, TypeError) as error:
        raise ValueError('Descriptor mount evidence unavailable') from error


def host_commands(installed, root):
    return {
        'device-stats': ['btrfs', 'device', 'stats', str(installed)],  # Never -z / --reset.
        'subvolume-show': ['btrfs', 'subvolume', 'show', str(root)],
        'readonly-snapshots': ['btrfs', 'subvolume', 'list', '-r', '-s', '-u', str(root)],
    }


def virtual_counter(path):
    """Only fixed kernel/sysfs counters, never process environments or log text."""
    row = dict(path=str(path), started_utc=diag.utc())
    try:
        row['canonical_path'] = str(path.resolve(strict=True))
        row['before'] = diag.metadata(path.stat())
        # Kernel virtual attributes are not on-disk inputs; no sysfs/proc writes.
        with path.open('rb') as stream:
            raw = stream.read(4097)
        if len(raw) > 4096:
            raise ValueError('Virtual counter exceeds bounded size')
        row.update(raw=raw.decode('ascii'), sha256=hashlib.sha256(raw).hexdigest(),
                   after=diag.metadata(path.stat()))
    except (OSError, ValueError, UnicodeError) as error:
        row['error'] = type(error).__name__ + ': ' + str(error)
    row['ended_utc'] = diag.utc()
    return row


def observe_host(output, target):
    root = mount_root(target['mount'])
    if not diag.INSTALLED.resolve(strict=True).is_relative_to(root.resolve(strict=True)):
        raise ValueError('Installed path is not beneath descriptor mount point')
    commands = {name: observe_command(output, name, argv)
                for name, argv in host_commands(diag.INSTALLED, root).items()}
    namespaces = []
    for path in (Path('/proc/self/ns/mnt'), Path('/proc/1/ns/mnt')):
        row = dict(path=str(path), utc=diag.utc())
        try:
            row['target'] = os.readlink(path)
        except OSError as error:
            row['error'] = type(error).__name__ + ': ' + str(error)
        namespaces.append(row)
    edac = dict(root='/sys/devices/system/edac/mc', entries=[], counters=[], complete=False)
    try:
        entries = sorted(Path(edac['root']).iterdir())
        edac['entries'] = [p.name for p in entries[:64]]
        edac['complete'] = len(entries) <= 64
        for entry in entries[:64]:
            if re.fullmatch(r'mc\d+', entry.name):
                edac['counters'].extend(virtual_counter(entry / name) for name in ('ce_count', 'ue_count'))
    except OSError as error:
        edac['error'] = type(error).__name__ + ': ' + str(error)
    result = dict(utc=diag.utc(), uid=os.getuid(), euid=os.geteuid(), platform=list(os.uname()),
        subvolume_query_mount_root=str(root), mount_source=target['mount'],
        commands=commands, mount_namespaces=namespaces, edac=edac,
        kernel_visibility=[virtual_counter(Path('/proc/sys/kernel') / name)
                           for name in ('dmesg_restrict', 'kptr_restrict')],
        limits=[
            'All observations share this host/kernel. Filesystem counters are cumulative, not file/byte attribution.',
            'Snapshot listing is metadata only. Even a listed local snapshot is not independently retained original bytes.',
            'No kernel or installed log text is inspected; kernel evidence is limited to virtual counters and namespace metadata.',
            'Missing/inaccessible EDAC counters do not prove healthy RAM, no ECC, or no past errors.',
            'No external backup/device/account, original-time descriptor/chunk bytes, raw media or memory test was supplied.',
            'No privilege escalation, namespace entry, process-environment inspection, cache eviction or storage mutation attempted.',
        ])
    diag.save_json(output / 'host-observations.json', result)
    return result


def valid_chunks(row):
    try:
        chunks, size, chunk_size = row['chunks'], row['bytes_read'], row['chunk_size']
        if (type(size) is not int or size <= 0 or type(chunk_size) is not int or chunk_size <= 0
                or not isinstance(chunks, list)):
            return False
        return bool(chunks) and chunks[-1]['end_exclusive'] == size and all(
            all(type(c[k]) is int for k in ('index', 'start', 'end_exclusive'))
            and c['index'] == i and c['start'] == i * chunk_size
            and c['end_exclusive'] == min((i + 1) * chunk_size, size)
            and c['end_exclusive'] > c['start'] and re.fullmatch('[0-9a-f]{64}', c['sha256'])
            for i, c in enumerate(chunks))
    except (KeyError, TypeError, IndexError):
        return False


def historical_pair(before, after, target):
    """Validate retained observations, never infer current or original bytes."""
    try:
        for row in (before, after):
            if (not valid_chunks(row) or row['path'] != str(target)
                    or row['identity_stable'] is not True
                    or not re.fullmatch('[0-9a-f]{64}', row['sha256'])):
                raise ValueError('Historical digest/chunks/target identity invalid')
            descriptor = row['descriptor_before']
            for key in ('device', 'inode', 'size', 'atime_ns', 'mtime_ns', 'ctime_ns',
                        'mode', 'uid', 'gid', 'links'):
                if type(descriptor[key]) is not int:
                    raise ValueError('Historical descriptor metadata incomplete')
            if (not isinstance(row['before'], dict) or not isinstance(row['after'], dict)
                    or descriptor != row['descriptor_after'] or descriptor['size'] != row['bytes_read']
                    or not diag.same_path(row['before'], row['after'])
                    or row['before']['stat'] != descriptor or row['before']['lstat'] != descriptor
                    or row['before']['requested_path'] != str(target)
                    or row['descriptor_target'] != row['before']['canonical_path']
                    or not Path(row['descriptor_target']).is_absolute()):
                raise ValueError('Historical descriptor/path/size mismatch')
        if (before['bytes_read'] != after['bytes_read'] or before['chunk_size'] != after['chunk_size']
                or before['descriptor_target'] != after['descriptor_target']):
            raise ValueError('Historical observations do not describe the same target/ranges')
        changes = diag.chunk_changes(before, after)
        equal = before['sha256'] == after['sha256']
        if equal != (not changes):
            raise ValueError('Historical whole and chunk digest claims contradict each other')
        return dict(status='VALID_RETAINED_OBSERVATIONS',
                    evidence_level='retained same-host reads, not fresh content or independent original bytes',
                    full_digests=[before['sha256'], after['sha256']],
                    metadata_equal=diag.same_path(before['after'], after['before']),
                    content_digests_equal=equal, bytes_read=before['bytes_read'],
                    chunk_count=len(before['chunks']), chunk_changes=changes)
    except (KeyError, TypeError, ValueError, IndexError) as error:
        return dict(status='INVALID_OR_MISSING', error=str(error))


def gate_assessment(report):
    """Deterministic assessment of fixed receipts, not a configurable launch gate.

    None of these inputs carries independently authenticated original bytes or
    causal attribution. A boolean/claimed verdict cannot supply that provenance.
    A future new evidence source needs independent review, not an override here.
    """
    conditions = {key: dict(status='OBSERVED' if report.get(field) is True else 'UNAVAILABLE_OR_UNSTABLE',
                           meaning=meaning) for key, field, meaning in (
        ('baseline_pin', 'baseline_pinned_unchanged', 'Original baseline JSON pin and read identity; not original log bytes'),
        ('retained_receipts', 'retained_evidence_unchanged', 'Retained JSON read integrity, not independent historical custody'),
        ('installed_metadata', 'target_metadata_stable', 'Current descriptor/path/mount metadata only; content equality NOT_TESTED'))}
    conditions['historical_pair'] = report.get('historical_pair', dict(status='INVALID_OR_MISSING'))
    conditions['independent_original_provenance'] = dict(status='MISSING',
        meaning='Pinned original records only a whole-file digest; no independently retained original-time '
                'bytes with custody/identity or descriptor/chunk provenance is supplied by these fixed inputs.')
    conditions['independent_drift_attribution'] = dict(status='MISSING',
        meaning='No independently attributable authorized storage/memory or original-time evidence explains '
                'the retained same-metadata differing reads and reconciles the pinned original observation.')
    return dict(schema_version=1, gate_status='BLOCKED', future_isolated_launch_eligible=False,
        native_launch_allowed=False, conditions=conditions,
        expected_baseline_sha256=report.get('expected_baseline_sha256'),
        expected_original_log_sha256=report.get('expected_original_log_sha256'),
        retained_inputs=report.get('retained_inputs', {}),
        prior_provenance_verdict=report.get('prior_provenance_verdict'),
        historical_claims=report.get('historical_claims', {}),
        observed_target=report.get('observed_target'),
        errors=report.get('blockers', []), dimensions={key: 'NOT_TESTED' for key in DIMENSIONS},
        authorization_boundary='Native launch PROHIBITED. No readiness, deployment, scraping or gate override. '
            'Only separately authorized acquisition and independent review of the missing provenance can change eligibility. '
            'Even a future independently cleared gate does not authorize a launch on this card.')


def investigate(run, name, retained_only=False):
    output = create_output(run, name)
    report = dict(started_utc=diag.utc(), argv=list(sys.orig_argv),
        cwd=str(Path.cwd()), output=str(output), run=str(run), exit_code=1, reconciled=False,
        gate_status='BLOCKED', future_isolated_launch_eligible=False,
        role='offline provenance observer; no host or guest gameplay role',
        host_observation_status='NOT_RUN: retained-only reconciliation' if retained_only else 'PENDING',
        native_launch_allowed=False, evidence_level='read-only provenance investigation, NOT native gameplay',
        target_content_bytes_read=0, target_content_equality='NOT_TESTED; deliberately no new content reads',
        baseline_pinned_unchanged=False, retained_evidence_unchanged=False, historical_chunk_changes=[],
        expected_baseline_sha256=diag.ORIGINAL_SHA256, blockers=[],
        independence_limit='Separate tools, snapshots and counters on the same kernel/cache/storage are not independent '
            'original bytes or an attribution of historical read drift.',
        controller_follow_on='native launch PROHIBITED; no readiness or scraping authorized',
        dimensions={key: 'NOT_TESTED' for key in DIMENSIONS}, V11_status='partial, unchanged',
        cleanup=dict(native_processes_started=[], processes_signaled=[], rig_writes=[], protected_writes=[],
                     baseline_writes=[], controller_writes=[], source_binary_writes=[],
                     descriptors_closed=True, observation_subprocesses_waited=True))
    try:
        if diag.ORIGINAL.resolve(strict=True) != diag.ORIGINAL:
            raise ValueError('Original baseline must not be redirected')
        baseline_before, baseline = retain_json(diag.ORIGINAL, output, 'baseline-before')
        if (baseline is None or not baseline_before['identity_stable']
                or baseline_before.get('sha256') != diag.ORIGINAL_SHA256):
            raise ValueError('Original baseline pin/identity unavailable; no target/host observations attempted')
        expected = baseline[str(diag.INSTALLED)][diag.LOG_NAME]
        if not isinstance(expected, str) or not re.fullmatch('[0-9a-f]{64}', expected):
            raise ValueError('Original target digest malformed')
        report['expected_original_log_sha256'] = expected
        report['original_time_evidence'] = {'whole_digest_only': True, 'descriptor': None,
                                           'chunk_hashes': None, 'retained_bytes': None}
        target = diag.INSTALLED / diag.LOG_NAME
        first = target_metadata(target)
        diag.save_json(output / 'target-metadata-before.json', first)
        rows, data = {}, {}
        for key, path in HISTORY.items():
            rows[key], data[key] = retain_json(path, output, 'history-' + key)
            if data[key] is None:
                report['blockers'].append('Historical receipt unavailable/unstable: ' + str(path))
        if valid_chunks(data.get('before') or {}) and valid_chunks(data.get('after') or {}):
            report['historical_chunk_changes'] = diag.chunk_changes(data['before'], data['after'])
            report['historical_full_digests'] = [data[key].get('sha256') for key in ('before', 'after')]
        else:
            report['blockers'].append('Historical chunk ranges incomplete/malformed; no valid comparison possible')
        report['retained_report_verdict'] = {k: (data.get('report') or {}).get(k)
            for k in ('reconciled', 'exit_code', 'current_reads_stable', 'original_digest_matches')}
        report['historical_pair'] = historical_pair(data.get('before'), data.get('after'), target)
        if report['historical_pair']['status'] != 'VALID_RETAINED_OBSERVATIONS':
            report['blockers'].append('Historical descriptor/digest comparison invalid or missing')
        report['retained_inputs'] = {k: dict(path=str(HISTORY[k]), sha256=r.get('sha256')) for k, r in rows.items()}
        report['historical_claims'] = {}
        for key, value in data.items():
            if key.startswith('older-') and isinstance(value, dict):
                body = value.get(str(target), value)
                if not isinstance(body, dict) or not isinstance(body.get('passes'), list):
                    report['blockers'].append('Malformed historical stability receipt: ' + key)
                    continue
                report['historical_claims'][key] = dict(evidence_level='retained claims, not fresh observations',
                    claimed_stable=body.get('stable'),
                    digests=[p.get('sha256') if isinstance(p, dict) else None for p in body['passes']])
        report['prior_provenance_verdict'] = {k: (data.get('prior-provenance') or {}).get(k)
            for k in ('reconciled', 'exit_code', 'native_launch_allowed', 'host_command_exits', 'blockers')}
        host = {'commands': {}} if retained_only else observe_host(output, first)
        if not retained_only:
            report['host_observation_status'] = 'EXECUTED: same-host probes, not independent provenance'
        report['host_command_exits'] = {name: r['exit_code'] for name, r in host['commands'].items()}
        report['host_evidence_failures'] = [name for name, r in host['commands'].items() if r['exit_code'] != 0]
        report['cleanup']['processes_signaled'] = [dict(probe=name, signal='SIGKILL', scope='owned observation child only')
            for name, r in host['commands'].items() if r.get('owned_child_killed_on_timeout')]
        after_rows = {}
        for key, path in HISTORY.items():
            after_rows[key] = receipt_read(path)
            diag.save_json(output / ('history-' + key + '.after-read.json'), after_rows[key])
        report['retained_evidence_unchanged'] = all(
            diag.reads_stable([rows[k], after_rows[k]]) for k in HISTORY)
        if not report['retained_evidence_unchanged']:
            report['blockers'].append('Retained historical receipt identity/digest missing or changed during investigation')
        baseline_after = receipt_read(diag.ORIGINAL)
        diag.save_json(output / 'baseline-after.json', baseline_after)
        report['baseline_pinned_unchanged'] = (diag.reads_stable([baseline_before, baseline_after])
                                              and baseline_after.get('sha256') == diag.ORIGINAL_SHA256)
        last = target_metadata(target)
        diag.save_json(output / 'target-metadata-after.json', last)
        report['target_metadata_stable'] = (first['identity_stable'] and last['identity_stable']
            and diag.same_path(first['after'], last['before'])
            and first['mount']['mountinfo'] == last['mount']['mountinfo'])
        if not report['target_metadata_stable']:
            report['blockers'].append('Target descriptor/path/mount metadata unavailable or unstable; no content inference')
        if not report['baseline_pinned_unchanged']:
            report['blockers'].append('Original baseline pin/descriptor changed or unavailable')
        report['observed_target'] = {k: first.get(k) for k in ('descriptor_target', 'descriptor_before', 'bytes_read')}
        report['observed_target']['historical_descriptor_equal'] = (
            report['historical_pair']['status'] == 'VALID_RETAINED_OBSERVATIONS'
            and first.get('descriptor_before') == data['after']['descriptor_after'])
    except (OSError, ValueError, KeyError, TypeError) as error:
        report['blockers'].append(type(error).__name__ + ': ' + str(error))
    report['blockers'].append('No independently retained original bytes/descriptor/chunk provenance is available '
        'in the supplied baseline or retained receipts. Host observations cannot authenticate or reconstruct it.')
    report['blockers'].append('No authorized independent storage/memory evidence attributes the historical '
        'same-metadata digest drift to this file and reconciles the original observation; same-host reads are not independence.')
    report['source_provenance'] = {}
    for path in (Path(__file__).resolve(), Path(diag.__file__).resolve()):
        row = diag.hash_read(path)
        diag.save_json(output / (path.stem + '.source-read.json'), row)
        report['source_provenance'][str(path)] = dict(sha256=row.get('sha256'), identity_stable=row['identity_stable'])
        if not row['identity_stable']:
            report['blockers'].append('Tool provenance unavailable: ' + str(path))
    report['ended_utc'] = diag.utc()
    diag.save_json(output / 'assessment.json', gate_assessment(report))
    diag.save_json(output / 'report.json', report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--name', required=True, help='new leaf output directory; requires matching RUN environment')
    parser.add_argument('--retained-only', action='store_true',
                        help='reconcile retained evidence and target metadata without repeating host probes')
    args = parser.parse_args()
    try:
        report = investigate(args.run, args.name, retained_only=args.retained_only)
    except (OSError, ValueError) as error:
        print(json.dumps(dict(exit_code=2, error=str(error), gate_status='BLOCKED',
                              future_isolated_launch_eligible=False, native_launch_allowed=False)), file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2))
    return report['exit_code']


if __name__ == '__main__':
    raise SystemExit(main())
