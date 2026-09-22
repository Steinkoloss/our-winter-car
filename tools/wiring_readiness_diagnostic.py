#!/usr/bin/env python3
"""Read-only wiring readiness diagnosis from retained failed execution artifacts.

Never launches, deploys, hashes protected log contents, repairs storage, or changes
provenance eligibility. A fresh diagnostic of an old crash is NOT a fresh native run.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

import protected_input_provenance as prov

SOURCE = Path(__file__).resolve().parents[1]
MAX_INPUT = 32 * 1024 * 1024


def read_retained(path):
    path = Path(path)
    if (path != path.resolve(strict=True) or not path.is_relative_to(prov.diag.ROUNDS.resolve())
            or not path.is_file() or path.stat().st_size > MAX_INPUT):
        raise ValueError('Expected bounded nonredirected retained round artifact: ' + str(path))
    with prov.diag.readonly_descriptor(path) as (fd, flags):
        import os
        before = os.fstat(fd)
        parts = []
        total = 0
        while total <= MAX_INPUT:
            part = os.read(fd, min(1024 * 1024, MAX_INPUT + 1 - total))
            if not part: break
            parts.append(part)
            total += len(part)
        after = os.fstat(fd)
        if (total > MAX_INPUT or total != before.st_size or before != after
                or before.st_ino != path.stat().st_ino):
            raise ValueError('Retained input changed or exceeded bound: ' + str(path))
        return b''.join(parts)


def minidump_exception(data):
    """Decode bounded MINIDUMP_EXCEPTION_STREAM and module identity, not a stack."""
    def unpack(fmt, offset):
        try: return struct.unpack_from(fmt, data, offset)
        except struct.error as error: raise ValueError('Truncated minidump') from error
    if data[:4] != b'MDMP': raise ValueError('Not a minidump')
    count, directory = unpack('<II', 8)
    if count > 128: raise ValueError('Unbounded minidump stream count')
    streams = {}
    for index in range(count):
        kind, size, offset = unpack('<III', directory + index * 12)
        if offset + size > len(data): raise ValueError('Truncated minidump stream')
        if kind in streams: raise ValueError('Duplicate minidump stream')
        streams[kind] = (size, offset)
    if 6 not in streams or 4 not in streams: raise ValueError('Missing exception/module streams')
    size, offset = streams[6]
    if size < 168: raise ValueError('Truncated exception stream')
    thread, = unpack('<I', offset)
    code, = unpack('<I', offset + 8)
    address, = unpack('<Q', offset + 24)
    size, offset = streams[4]
    count, = unpack('<I', offset)
    if count > 4096 or size < 4 + count * 108: raise ValueError('Malformed module list')
    result = dict(exception_code=hex(code), address=hex(address), thread_id=thread,
                  module=None, module_offset=None, root_cause='UNKNOWN; module/offset is not causal attribution')
    for index in range(count):
        module = offset + 4 + index * 108
        base, length = unpack('<QI', module)
        if base <= address < base + length:
            name_offset, = unpack('<I', module + 20)
            name_length, = unpack('<I', name_offset)
            if name_length > 8192 or name_offset + 4 + name_length > len(data):
                raise ValueError('Malformed module name')
            result.update(module=data[name_offset + 4:name_offset + 4 + name_length].decode('utf-16-le'),
                          module_base=hex(base), module_size=length, module_offset=hex(address - base))
            break
    return result


def assess(host_log, timeline, crash_log):
    events = lambda name: [row.get('values', {}) for row in timeline if row.get('event') == name]
    pending, responses = set(), set()
    for row in timeline:
        values = row.get('values', {})
        role, sequence = values.get('role'), values.get('sequence')
        if role not in ('host', 'guest') or type(sequence) is not int or sequence <= 0: continue
        if row.get('event') == 'command sent' and values.get('args') == ['wire-view']:
            pending.add((role, sequence))
        elif row.get('event') == 'command response' and values.get('ok') is True and (role, sequence) in pending:
            responses.add(role)
    response = 'host' in responses
    crashed = '0xc0000005' in crash_log.lower()
    return dict(status='CRASH_BEFORE_COMMAND_RESPONSE' if crashed and not response else
                'RETAINED_HOST_RESPONSE_ONLY' if response else 'NO_HOST_COMMAND_RESPONSE',
                host_command_response=response, guest_command_response='guest' in responses,
                guest_launched=any(row.get('role') == 'guest' for row in events('wiring launched')),
                mutex_ready_logged='HostLocal ready' in host_log,
                game_load_requested='LoadLevel: GAME' in host_log,
                game_level_observed="-> 'GAME'" in host_log,
                wheel_awake_exception_observed='Wheel.Awake ()' in host_log,
                access_violation_observed=crashed,
                launcher_exits_before_response=events('launcher exited before response'),
                native_gameplay_pass=False)


def investigate(run, name, native, crash, command, provenance):
    output = prov.create_output(run, name)
    report = dict(argv=[sys.executable] + sys.argv, cwd=str(Path.cwd()), run=str(run),
                  output=str(output), started_utc=prov.diag.utc(), exit_code=1,
                  role='offline observer; no native host or guest launched', native_launched=False,
                  evidence_level='fresh read-only diagnosis of RETAINED native failure and current provenance observation',
                  protected_content_bytes_read=0, rig_writes=[], processes_started=[], processes_signaled=[],
                  V05='partial', V06='partial', inputs={}, errors=[])
    def retain(path, leaf):
        raw = read_retained(path)
        target = output / leaf
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open('xb') as stream: stream.write(raw)
        report['inputs'][leaf] = dict(path=str(path), bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
        return raw
    def decode(raw):
        return json.loads(raw, object_pairs_hook=prov.unique_object, parse_constant=prov.invalid_constant)
    try:
        if provenance.parent.parent != run or provenance.name != 'report.json':
            raise ValueError('Provenance must be the fresh report in this assigned RUN')
        p = decode(retain(provenance, 'provenance/report.json'))
        retain(provenance.with_name('assessment.json'), 'provenance/assessment.json')
        if p.get('run') != str(run) or Path(p.get('output', '')) != provenance.parent:
            raise ValueError('Provenance report RUN/output mismatch')
        report['provenance'] = {key: p.get(key) for key in ('exit_code', 'gate_status', 'reconciled',
            'native_launch_allowed', 'baseline_pinned_unchanged', 'retained_evidence_unchanged',
            'target_metadata_stable', 'target_content_bytes_read', 'expected_original_log_sha256',
            'historical_pair', 'host_command_exits', 'blockers', 'source_provenance')}
        report['provenance']['independence_limit'] = p.get('independence_limit')
        raw_command = retain(command / 'raw.log', 'command/raw.log')
        receipt = decode(retain(command / 'receipt.json', 'command/receipt.json'))
        report['retained_command'] = {key: receipt.get(key) for key in ('argv', 'cwd', 'exit_code', 'seconds')}
        report['retained_command']['raw_digest_matches_receipt'] = (
            hashlib.sha256(raw_command).hexdigest() == receipt.get('raw_sha256'))
        if not report['retained_command']['raw_digest_matches_receipt']:
            report['errors'].append('Retained command log does not match its receipt digest')
        timeline = [decode(line) for line in retain(native / 'timeline.jsonl', 'native/timeline.jsonl').splitlines()]
        host_log = retain(native / 'BepInEx/LogOutput-host.log', 'native/BepInEx/LogOutput-host.log').decode('utf-8-sig')
        for leaf in ('host-launch.log', 'launches.json', 'cleanup.json', 'boot-stages.json',
                     'BepInEx/LogOutput.log', 'WinterMP/boot-trace-host.log', 'WinterMP/hostlocal-ready.flag'):
            retain(native / leaf, 'native/' + leaf)
        error = retain(crash / 'error.log', 'crash/error.log').decode('utf-8-sig')
        dump = retain(crash / 'crash.dmp', 'crash/crash.dmp')
        report['crash_exception'] = minidump_exception(dump)
        report['readiness'] = assess(host_log, timeline, error)
        report['historical_cleanup'] = decode(read_retained(native / 'cleanup.json'))
        historical_sources = decode(retain(native / 'wiring-source-hashes.json', 'native/wiring-source-hashes.json'))
        historical_binaries = decode(retain(native / 'binary-hashes.json', 'native/binary-hashes.json'))
        paths = set(historical_sources) | {'tools/wiring_readiness_diagnostic.py', 'tools/fuel_transfer_evidence.py',
                'tools/protected_input_provenance.py', 'tools/protected_input_diagnostic.py', 'tools/linux-common.sh',
                'tools/v11_pane_audit.py', 'catalog/sync-catalog.json', 'src/WinterMP.Net/Protocol.cs'}
        report['current_source_hashes'] = {}
        for path in sorted(paths):
            source = SOURCE / path
            if source.resolve() != source or not source.is_relative_to(SOURCE):
                raise ValueError('Invalid source linkage path')
            report['current_source_hashes'][path] = prov.diag.hash_read(source).get('sha256')
        report['source_changed_since_retained_native'] = [path for path, sha in historical_sources.items()
            if report['current_source_hashes'].get(path) != sha]
        report['current_build_binaries_not_native_executed'] = {}
        for path, deployed in (
                ('src/WinterMP.Core/bin/Release/net35/WinterMP.Core.dll', 'WinterMP/WinterMP.Core.dll'),
                ('src/WinterMP.Net/bin/Release/net35/WinterMP.Net.dll', 'WinterMP/WinterMP.Net.dll'),
                ('tools/GuestSaveProbe/bin/Release/net35/WinterMP.GuestSaveProbe.dll', 'WinterMP.GuestSaveProbe.dll'),
                ('src/WinterMP.FastBoot/bin/Release/net35/WinterMP.FastBoot.dll', 'WinterMP/WinterMP.FastBoot.dll')):
            current = prov.diag.hash_read(SOURCE / path)
            report['current_build_binaries_not_native_executed'][path] = dict(sha256=current.get('sha256'),
                error=current.get('error'), historical_sha256=historical_binaries.get(deployed),
                matches_retained_deployment=current.get('sha256') is not None and
                    current.get('sha256') == historical_binaries.get(deployed))
    except (OSError, ValueError, KeyError, TypeError) as error:
        report['errors'].append(type(error).__name__ + ': ' + str(error))
    report['ended_utc'] = prov.diag.utc()
    report['next_prerequisite'] = ('Independent original-time protected-input provenance and drift attribution, reviewed without '
        'changing the pinned baseline. Only after clearance: bounded rendered host readiness with current binaries; '
        'capture Mono exception/stack if world load still crashes. Do not infer a wiring bug from Wheel.Awake or module offset.')
    report['limits'] = dict(native_wiring='NOT_TESTED', ordinary_input='NOT_TESTED', native_save_reload='NOT_TESTED',
        different_save='NOT_TESTED', steam_two_pc='NOT_TESTED', four_player_soak='NOT_TESTED',
        native_rejoin='NOT_TESTED', crash_root_cause='UNKNOWN', launch_eligibility='NOT_GRANTED by this diagnostic')
    prov.diag.save_json(output / 'report.json', report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--name', required=True)
    parser.add_argument('--native-evidence', required=True, type=Path)
    parser.add_argument('--crash-evidence', required=True, type=Path)
    parser.add_argument('--command-evidence', required=True, type=Path)
    parser.add_argument('--provenance', required=True, type=Path)
    args = parser.parse_args()
    report = investigate(args.run, args.name, args.native_evidence, args.crash_evidence,
                         args.command_evidence, args.provenance)
    print(json.dumps(report, indent=2))
    return report['exit_code']


if __name__ == '__main__':
    raise SystemExit(main())
