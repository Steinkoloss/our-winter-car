#!/usr/bin/env python3
"""Fresh portable V11 bridge receipt. Never launches/deploys/authorizes a native game."""
import argparse
import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

from v11_readiness_requirements import MISSING

SOURCE = Path(__file__).resolve().parents[1]
ROUNDS = SOURCE.parent / 'rounds'
TEST_NAME = 'PaneScrapeBridge.Tests.BridgeTests.FreshWrongPaneFromGuestHookConsumesSequenceWithoutMutationAndFreshStrokeRecovers'
PREFIX = 'V11_FIXTURE '
LEVEL = 'portable-engine-session-physics-fixture'


def number(value):
    return type(value) in (int, float) and math.isfinite(value)


def preconditions(data):
    """Scenario requirements, NOT additional production authorization rules.

    Missing/null/type-invalid observations fail closed. No pane-park/pose-only
    shortcut. IDs are explicitly the bridge fixture's namespaces, not native IDs.
    """
    rows = {}
    missing = []

    def row(name, keys, predicate):
        keys = keys.split()
        absent = [key for key in keys if key not in data or data[key] is None]
        missing.extend(absent)
        try:
            valid = not absent and predicate()
        except (KeyError, TypeError, ValueError):
            valid = False
        rows[name] = dict(status='SATISFIED_FIXTURE' if valid else 'NOT_READY',
                          missing_fields=absent, observed={k: data.get(k) for k in keys})

    def exact(key, expected):
        return type(data[key]) is type(expected) and data[key] == expected

    def positive(key):
        return type(data[key]) is int and data[key] > 0

    row('authenticated_guest', 'host_is_host guest_is_host host_id guest_id observed_actor_id',
        lambda: exact('host_is_host', True) and exact('guest_is_host', False) and exact('host_id', 0)
        and exact('guest_id', 2) and exact('observed_actor_id', 2))
    row('fresh_alive_pose', 'pose_now pose_timestamp guest_alive guest_pose_matches',
        lambda: number(data['pose_now']) and number(data['pose_timestamp']) and data['pose_timestamp'] > 0
        and 0 <= data['pose_now'] - data['pose_timestamp'] <= .6
        and exact('guest_alive', True) and exact('guest_pose_matches', True))
    row('host_away_no_tool', 'host_guest_distance_squared host_car_distance_squared host_has_tool host_lease_holder',
        lambda: all(number(data[k]) and data[k] > 100 for k in ('host_guest_distance_squared', 'host_car_distance_squared'))
        and exact('host_has_tool', False) and type(data['host_lease_holder']) is int
        and data['host_lease_holder'] in (2, 255))
    row('parked_shared_corris', 'host_vehicle_id guest_vehicle_id host_bound_vehicle_id guest_bound_vehicle_id '
        'host_vehicle_path guest_vehicle_path host_vehicle_body guest_vehicle_body host_is_vehicle guest_is_vehicle',
        lambda: all(positive(k) and data[k] == data['host_vehicle_id'] for k in
                    ('host_vehicle_id', 'guest_vehicle_id', 'host_bound_vehicle_id', 'guest_bound_vehicle_id'))
        and all(exact(k, 'CORRIS') for k in ('host_vehicle_path', 'guest_vehicle_path', 'host_vehicle_body', 'guest_vehicle_body'))
        and exact('host_is_vehicle', True) and exact('guest_is_vehicle', True))
    row('both_non_owner', 'host_local_owner guest_local_owner host_remote_owner guest_remote_owner',
        lambda: exact('host_local_owner', False) and exact('guest_local_owner', False)
        and exact('host_remote_owner', 255) and exact('guest_remote_owner', 255))
    row('outside_seat_trigger', 'guest_move_state guest_passenger guest_inside_trigger guest_local_inside',
        lambda: exact('guest_move_state', 0) and all(exact(k, False) for k in
                ('guest_passenger', 'guest_inside_trigger', 'guest_local_inside')))
    row('zero_linear_angular_motion', 'host_linear_squared guest_linear_squared host_angular_squared guest_angular_squared',
        lambda: all(number(data[k]) and data[k] == 0 for k in
                    ('host_linear_squared', 'guest_linear_squared', 'host_angular_squared', 'guest_angular_squared')))
    row('shared_tool', 'host_tool_id guest_tool_id host_tool_name guest_tool_name',
        lambda: positive('host_tool_id') and positive('guest_tool_id') and data['host_tool_id'] == data['guest_tool_id']
        and exact('host_tool_name', 'ice scraper(itemx)') and exact('guest_tool_name', 'ice scraper(itemx)'))
    ready = all(r['status'] == 'SATISFIED_FIXTURE' for r in rows.values())
    return dict(status='FIXTURE_READY' if ready else 'NOT_READY', ready=ready,
                evidence_level=LEVEL, missing_fields=sorted(set(missing)), matrix=rows)


def native_limits():
    return dict(status='NOT_READY', native_executed=False, v11='partial',
        protected_input=dict(status='BLOCKED', content_read=False,
            missing_artifact='Independently retained original-time protected installed guest-log bytes with custody/identity or independently attributable drift reconciliation; portable receipts cannot clear it.'),
        missing_artifacts=[dict(id=identifier, required_capture=details, status='NOT_SUPPLIED', group=group)
                           for group, items in MISSING.items() for identifier, details in items],
        acceptance={key: 'NOT_TESTED' for key in ('native', 'ordinary_input', 'different_save', 'native_rejoin',
                    'native_save_reload', 'Steam_two_PC', 'four_player_soak')},
        limits=['Pose, collider first-hit, outside bounds and passenger queries are explicit engine/session/physics doubles.',
                'Injected teleport, pickup and collider fixtures are not ordinary input or native evidence.',
                'No vanilla persistence inference, sidecar or native readiness promotion.'],
        next_requirement='Independent protected-input provenance clearance followed by a separate readiness-only run with genuine guest resume/spawn and fresh host-observed alive pose before contact/action acceptance.')


def require(ok, message):
    if not ok:
        raise ValueError(message)


def parse_fixture(path, token):
    root = ET.parse(path).getroot()
    results = [r for r in root.iter() if r.tag.split('}')[-1] == 'UnitTestResult']
    require(len(results) == 1 and results[0].get('testName') == TEST_NAME and results[0].get('outcome') == 'Passed',
            'Exactly the passed fresh WrongPane fixture is required')
    output = '\n'.join(n.text or '' for n in results[0].iter() if n.tag.split('}')[-1] == 'StdOut')
    records = [json.loads(line.strip()[len(PREFIX):]) for line in output.splitlines() if line.strip().startswith(PREFIX)]
    matrices = [r for r in records if r.get('kind') == 'preconditions']
    require([r.get('stage') for r in matrices] == ['before-pickup', 'before-wrong', 'before-recovery'],
            'Missing, duplicate or reordered explicit preconditions')
    require(all(r.get('run_token') == token and r.get('evidence_level') == LEVEL for r in records),
            'Stale token or non-fixture evidence level')
    checked = {r['stage']: preconditions(r['data']) for r in matrices}
    require(all(r['ready'] for r in checked.values()), 'Explicit bridge preconditions NOT_READY')
    identities = ('host_vehicle_id', 'guest_vehicle_id', 'host_bound_vehicle_id', 'guest_bound_vehicle_id',
                  'host_tool_id', 'guest_tool_id', 'host_id', 'guest_id', 'observed_actor_id')
    require(all(r['data'][k] == matrices[0]['data'][k] for r in matrices for k in identities),
            'Shared identity changed between preconditions')
    require([(r.get('kind'), r.get('stage')) for r in records] == [
        ('preconditions', 'before-pickup'), ('decision', 'positive'), ('preconditions', 'before-wrong'),
        ('decision', 'wrong-pane'), ('decision', 'corrected-replay'), ('preconditions', 'before-recovery'),
        ('decision', 'fresh-recovery'), ('decision', 'recovery-duplicate'), ('cleanup', 'finally')],
        'Reordered/missing fixture observations')
    decisions = [r['data'] for r in records if r.get('kind') == 'decision']
    stages = ['positive', 'wrong-pane', 'corrected-replay', 'fresh-recovery', 'recovery-duplicate']
    statuses = ['Accepted', 'WrongPane', 'ReplayedSequence', 'Accepted', 'ReplayedSequence']
    require([d.get('stage') for d in decisions] == stages, 'Missing/duplicate/reordered recovery decisions')
    for index, (d, status) in enumerate(zip(decisions, statuses)):
        require(d['status'] == status and d['actor'] == 2 and d['is_decision'] is True, 'Wrong authority status/actor')
        require(type(d['sequence']) is int and d['sequence'] > 0 and
                d['sequence'] == d['high_water'] == d['lease_seen'] == d['guest_sequence'], 'Sequence consumption differs')
        require(d['host_decisions'] == index + 1, 'Not exactly one authority decision per request')
        require(d['accepted_for_sequence'] == (1 if index in (0, 3, 4) else 0), 'Accepted request was duplicated or denied request accepted')
        require(d['host_glass'] == d['host_material_writes'] == d['guest_effects'] == (1 if index < 3 else 2), 'Incorrect additive/effect call count')
        require(d['guest_glass'] == d['host_effects'] == 0 and d['host_heat'] == 0, 'Guest speculation or host guest-effects')
        require(number(d['guest_heat']) and abs(d['guest_heat'] - d['guest_effects'] * .47) < .000001, 'Actor heat differs')
        require(all(number(d[k]) and d[k] == d['cutoff'] for k in ('host_cutoff', 'guest_cutoff', 'host_material', 'guest_material')),
                'Absolute host/guest result differs')
        require(d['epoch'] > 0 and d['vehicle_id'] == matrices[0]['data']['host_vehicle_id']
                and d['tool_id'] == matrices[0]['data']['host_tool_id'] and d['holder'] == 2 and d['equipped'] is True,
                'Result identity/lease differs')
    positive, wrong, replay, recovered, duplicate = decisions
    require(wrong['sequence'] > positive['sequence'] and replay['sequence'] == wrong['sequence'] and
            recovered['sequence'] == wrong['sequence'] + 1 and duplicate['sequence'] == recovered['sequence'], 'Recovery is not fresh')
    require(recovered['revision'] == positive['revision'] + 1 and
            abs(recovered['cutoff'] - positive['cutoff'] - .005) < .000001, 'Valid action did not change host once')
    stable = ('revision', 'cutoff', 'epoch', 'vehicle_id', 'tool_id', 'holder', 'equipped', 'host_glass',
              'guest_glass', 'host_effects', 'guest_effects', 'host_heat', 'guest_heat', 'host_material_writes')
    for before, after in ((positive, wrong), (wrong, replay), (recovered, duplicate)):
        require(all(before[k] == after[k] for k in stable), 'Invalid/duplicate request mutated state')
    cleanup = [r['data'] for r in records if r.get('kind') == 'cleanup']
    require(len(records) == 9 and cleanup == [dict(mutation_disarmed=True, observer_detached=True, bridges_reset=True)],
            'Missing fixture cleanup or unknown observations')
    return dict(status='PASS_FIXTURE_ONLY', evidence_level=LEVEL, preconditions=checked,
                decisions=decisions, raw_records=records, fixture_cleanup=cleanup[0])


def new_output(run, name):
    if (not run.is_absolute() or run != run.resolve(strict=True) or run.parent != ROUNDS.resolve()
            or not (run / 'contract.json').is_file() or (run / 'contract.json').is_symlink()):
        raise ValueError('RUN must be the actual assigned absolute contract directory without aliases')
    if not name or Path(name).name != name or name in ('.', '..'):
        raise ValueError('name must be a new leaf')
    output = run / name
    output.mkdir()  # exclusive, even a failed attempt remains evidence
    return output


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def hashes():
    paths = [SOURCE / 'catalog/sync-catalog.json', SOURCE / 'protocol/PROTOCOL.md', SOURCE / 'PLAN.md']
    for base in ('src/WinterMP.Net', 'tools/PaneScrapeBridge.Tests'):
        paths += [p for p in (SOURCE / base).rglob('*') if p.suffix in ('.cs', '.csproj') and
                  not any(part in ('bin', 'obj') for part in p.parts)]
    paths += list((SOURCE / 'src/WinterMP.Core/Sync').glob('PaneScrapeSync*.cs'))
    paths += [SOURCE / p for p in ('src/WinterMP.Core/Sync/FsmHook.cs', 'src/WinterMP.Core/Sync/PlayerMoveState.cs',
        'src/WinterMP.Core/Catalog/SyncCatalogJson.PaneScrape.cs', 'tools/v11_bridge_readiness.py',
        'tools/v11_readiness_requirements.py', 'tools/tests/test_v11_bridge_readiness.py')]
    return {str(p.relative_to(SOURCE)): digest(p) for p in sorted(set(paths))}


def save(path, value):
    with path.open('x') as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write('\n')


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def execute(output, token, timeout):
    command = ['dotnet', 'test', 'tools/PaneScrapeBridge.Tests/PaneScrapeBridge.Tests.csproj', '-c', 'Release',
               '-p:DeployToGame=false', '--filter', 'FullyQualifiedName=' + TEST_NAME,
               '--logger', 'trx;LogFileName=fixture.trx', '--results-directory', str(output)]
    receipt = dict(command=command, cwd=str(SOURCE), started_utc=utc(), timeout_seconds=timeout,
                   run_token=token, before_sha256=hashes(), evidence_level=LEVEL, native_executed=False)
    child = None
    code = 125
    try:
        with (output / 'output.log').open('xb') as log:
            child = subprocess.Popen(command, cwd=SOURCE, env=dict(os.environ, WINTERMP_V11_PORTABLE_TOKEN=token),
                                     stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
            receipt['owned_pid'] = child.pid
            try:
                code = child.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                code = 124
                receipt['timed_out'] = True
    finally:
        if child is not None:
            # Own process group only. Also clean descendants on interruption/timeout.
            for sig in (signal.SIGTERM, signal.SIGKILL):
                try:
                    os.killpg(child.pid, sig)
                except ProcessLookupError:
                    break
                if sig == signal.SIGTERM:
                    try:
                        child.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        pass
            child.wait()
        receipt.update(exit_code=code, child_exit_code=child.returncode if child else None, ended_utc=utc(),
                       cleanup='Owned portable process group cleaned and command waited; no native lock/process/deployment/save access.',
                       after_sha256=hashes())
        receipt['artifacts_sha256'] = {p.name: digest(p) for p in output.iterdir() if p.is_file()}
        save(output / 'command.json', receipt)
    require(code == 0, 'Portable test command failed: ' + str(code))
    require(receipt['before_sha256'] == receipt['after_sha256'], 'Source changed during fixture')
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--name', required=True)
    parser.add_argument('--timeout', type=int, default=180)
    args = parser.parse_args()
    if not 1 <= args.timeout <= 600:
        parser.error('timeout must be bounded to 1..600 seconds')
    output = new_output(args.run, args.name)
    report = dict(schema_version=1, argv=sys.argv, cwd=str(Path.cwd()), run=str(args.run),
                  started_utc=utc(), native=native_limits())
    code = 1
    try:
        token = uuid.uuid4().hex
        execute(output, token, args.timeout)
        report['fixture'] = parse_fixture(output / 'fixture.trx', token)
        code = 0  # ONLY the portable contract, never native readiness
    except (ValueError, KeyError, OSError, ET.ParseError) as error:
        report['fixture'] = dict(status='NOT_READY', error=str(error))
    finally:
        report.update(exit_code=code, ended_utc=utc())
        report['artifacts_sha256'] = {p.name: digest(p) for p in output.iterdir() if p.is_file()}
        report['binary_sha256'] = {str(p.relative_to(SOURCE)): digest(p) for p in (
            SOURCE / 'tools/PaneScrapeBridge.Tests/bin/Release/net8.0/PaneScrapeBridge.Tests.dll',
            SOURCE / 'src/WinterMP.Net/bin/Release/netstandard2.0/WinterMP.Net.dll') if p.is_file()}
        save(output / 'report.json', report)
    print(json.dumps(dict(report=str(output / 'report.json'), exit_code=code,
                          fixture=report['fixture']['status'], native='NOT_READY', protected_input='BLOCKED')))
    return code


if __name__ == '__main__':
    def interrupt(signum, frame):
        raise KeyboardInterrupt('Portable audit interrupted: ' + str(signum))
    signal.signal(signal.SIGTERM, interrupt)
    sys.exit(main())
