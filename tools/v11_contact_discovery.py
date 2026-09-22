#!/usr/bin/env python3
"""Read-only V11 catalog/source discovery receipt; no native launch capability.

Consumes a fresh retained-only provenance observation in this RUN. This tool is
not a replacement/override for the protected-input or native-launch gate.
"""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import sys

import protected_input_provenance as provenance
from v11_pane_audit import AUDITED_PANE_BINDING, discover_pane
from v11_readiness_requirements import EXTRA_TEXT_INPUTS, readiness_report

SOURCE = Path(__file__).resolve().parents[1]
PROFILE = AUDITED_PANE_BINDING
PROFILE_SOURCE = 'src/WinterMP.Core/Catalog/SyncCatalogJson.PaneScrape.cs'
BINDINGS_SOURCE = 'src/WinterMP.Core/Sync/PaneScrapeSync.Bindings.cs'
SYNC_SOURCE = 'src/WinterMP.Core/Sync/PaneScrapeSync.cs'
CLIMATE_SOURCE = 'src/WinterMP.Core/Sync/VehicleWorldSync.Climate.cs'
AUTHORITY_SOURCE = 'src/WinterMP.Net/Sync/PaneScrapeAuthority.cs'
TEXT_INPUTS = (PROFILE_SOURCE, BINDINGS_SOURCE, SYNC_SOURCE, CLIMATE_SOURCE, AUTHORITY_SOURCE,
               'src/WinterMP.Core/Session/SessionManager.Messages.cs',
               'src/WinterMP.Tools/FsmDumperPlugin.cs', 'docs/V11-PANE-AUDIT.md',
               'docs/V11-PORTABLE-BRIDGE.md', 'docs/V11-CONTACT-DISCOVERY.md', *EXTRA_TEXT_INPUTS)
SOURCE_INPUTS = tuple(dict.fromkeys((*TEXT_INPUTS, 'catalog/sync-catalog.json', 'catalog/dump-23268598.json',
                 'src/WinterMP.Net/Sync/PaneScrapeReplica.cs', 'src/WinterMP.Net/Sync/ScraperLease.cs',
                 'src/WinterMP.Net/Sync/PaneScrapePolicy.cs', 'src/WinterMP.Net/Protocol.cs',
                 'src/WinterMP.Net/Messages/PaneScrapeMessages.cs', 'protocol/PROTOCOL.md',
                 'tools/v11_contact_discovery.py', 'tools/v11_pane_audit.py',
                 'tools/tests/test_v11_contact_discovery.py', 'tools/tests/test_v11_pane_static.py',
                 'tools/protected_input_provenance.py', 'tools/protected_input_diagnostic.py',
                 'tools/v11_readiness_requirements.py', 'tools/tests/test_v11_readiness.py',
                 'tools/PaneScrapeBridge.Tests/GuestStrokeWire.cs',
                 'tools/PaneScrapeBridge.Tests/PaneScrapeBridge.Tests.csproj')))
BINARY_INPUTS = tuple('src/' + p + '/bin/Release/net35/' + p + '.dll'
                      for p in ('WinterMP.Core', 'WinterMP.Net'))

# These are source/historical expectations, NEVER current native observations.
NATIVE_EXPECTATIONS = {
    'glass_actions': {
        'source': BINDINGS_SOURCE,
        'expected': ['State 7: exactly FloatAdd then SetMaterialFloat',
                     'FloatAdd.floatVariable == bound CutoffWindshield; add.Name == ScrapeEfficiency',
                     'FloatAdd.everyFrame/perSecond == false; no Sound transition replay on host'],
        'unknown_fields': ['native action types/field identities', 'ScrapeEfficiency live value (.005 historical only)']},
    'material_6': {
        'source': BINDINGS_SOURCE,
        'expected': ['Freezing MaterialVariables[6] nonnull', 'SetMaterialFloat.material.Value equals material 6',
                     'floatValue == bound cutoff; namedFloat.Value == _Cutoff; everyFrame == false'],
        'unknown_fields': ['live material identity/name (corris_frozen_windshield historical only)', '_Cutoff live value']},
    'stroke_actions': {
        'source': BINDINGS_SOURCE,
        'expected': ['Scrape 2: unique SendEventByName with sendEvent.Value == WINDSHIELD',
                     'native Windows/Freezing target and camera-X alternating stroke chain are historical observations'],
        'unknown_fields': ['live SendEventByName fields/target', 'X/Xold/Windows/This/InsideTrigger values']},
    'personal_effects': {
        'source': BINDINGS_SOURCE,
        'expected': ['Sound: RandomFloat, ArrayListGetRandom, MasterAudioPlaySound, FloatAdd',
                     'FloatAdd.floatVariable.Name == PlayerTemp; add.Name == BodyTempAdd; one-shot',
                     'GlassPos assigned pane; only accepted actor runs effects'],
        'unknown_fields': ['native action field values', 'BodyTempAdd live value (.47 historical only)', 'sound/heat observation']},
    'hand_equipment': {
        'source': BINDINGS_SOURCE,
        'expected': ['PickUp.PickedObject is actual shared ice scraper(itemx), not separate camera model',
                     'Set pivot 2 waits for host lease before native parenting/joint',
                     'Ice Scraper/Off/Drop part/Drop part 2/cancel state initialized',
                     'SCRAPER_ON/OFF broadcast behavior is historical native evidence'],
        'unknown_fields': ['live shared tool count/identity', 'Hand action types/fields', 'holder/equipped lease', 'native picked object']},
    'eye_contact': {
        'source': SYNC_SOURCE,
        'expected': [PROFILE['eyePath'], 'Scrape.Distance == .8',
                     'native MousePickEvent/camera-X motion historical only',
                     'host first-hit Physics.Raycast == pane collider within .8m; pickup ray within 1m',
                     'finite unit direction and eye bounded against fresh host-known feet/yaw'],
        'unknown_fields': ['live eye/collider objects', 'MousePickEvent fields', 'near/far/obstruction/contact observations']},
    'inside_player': {
        'source': SYNC_SOURCE,
        'expected': [PROFILE['insidePath'] + '::PlayerTrigger.PlayerInside',
                     'inside collider must exist; remote seat flags/passenger mapping and feet bounds checked'],
        'unknown_fields': ['live PlayerInside', 'trigger collider geometry', 'authenticated outside/alive player pose']},
    'ownership': {
        'source': SYNC_SOURCE,
        'expected': ['unique shared Corris and scraper; parked linear/angular squared speeds <= .01',
                     'valid host OR guest intent executes on host without vehicle ownership transfer',
                     'bound windshield exact-float host result owns guest presentation; other panes/interior frost remain climate'],
        'unknown_fields': ['vehicle/tool live identities and owner', 'velocity', 'lease ownership', 'peer frost/material convergence']},
    'interior_frost': {
        'source': CLIMATE_SOURCE,
        'expected': ['GlassFrosting.Frost and FrostGlass are separate from exterior CutoffWindshield/material 6'],
        'unknown_fields': ['live interior frost value/material; no cross-pane mutation observation']},
    'save_load_reset': {
        'source': AUTHORITY_SOURCE,
        'expected': ['Capture reads native cutoff; ResetSession clears in-memory epoch/lease/replica',
                     'vanilla FREEZE/Delay/Check roof initialization preserved; no new frost sidecar',
                     'historical injected cold reload .26 -> 1 is NOT a current persistence observation'],
        'unknown_fields': ['native save/load action fields', 'FREEZE/roof reset values',
                           'current copied-save cold reload', 'same/fresh/different-save peer join']},
}

ANCHORS = {
    BINDINGS_SOURCE: ['found.Fsm.Initialized && found.Fsm.Started', 'Ambiguous shared scraper identity.',
                     '!delta.IsInitialized || !stroke.IsInitialized || !pickup.IsInitialized || !sound.IsInitialized',
                     'delta.Actions.Length != 2', '"floatVariable") != cutoff',
                     '"add")?.Name != "ScrapeEfficiency"', '"namedFloat")?.Value != "_Cutoff"',
                     'Ambiguous stroke event.', 'FindFsmFloat("Distance")?.Value != .8f',
                     'replacement[eventIndex] = new StrokeAction', 'foreach (var action in _soundActions!) action.OnEnter();'],
    SYNC_SOURCE: ['_authority.Decide(actor, action.Intent())', 'Session.SendWorldMessage(request, Channel.ReliableOrdered);',
                  '_glassActions![0].OnEnter(); _glassActions[1].OnEnter();', 'hit.collider == _pane',
                  'age > .6f', '=> Physics.Raycast(', '_cutoff.Value = _replica.Current.Cutoff;',
                  'ResumePersonalEffectsAfter(update.HighWater)', '_epoch = 0; _sequence = 0;'],
    CLIMATE_SOURCE: ['if (PaneScrapeSync.Instance?.OwnsPane(item.Id) != true)', 'ApplyRemoteFrostLevel(item, frost);'],
    AUTHORITY_SOURCE: ['intent.Actor != authenticatedActor', '_host.ApplyWindshieldDelta();',
                       'float cutoff = _host.ReadWindshieldCutoff();'],
    'src/WinterMP.Core/Session/SessionManager.Messages.cs': [
        'case ScraperAction scraper when IsHost:', 'Sync.PaneScrapeSync.Instance?.OnAction(scraper, scraperActor);',
        'case PaneScrapeUpdate paneUpdate when !IsHost:'],
}


def strict_json(raw):
    return json.loads(raw, object_pairs_hook=provenance.unique_object,
                      parse_constant=provenance.invalid_constant, parse_float=provenance.finite_float)


def source_profile(text):
    arrays = []
    for name in ('Keys', 'Audited'):
        match = re.findall(r'\b' + name + r'\s*=\s*\{([^}]+)\}', text)
        if len(match) != 1:
            raise ValueError('Ambiguous or changed production PaneScrapeData.' + name)
        values = re.findall(r'"([^"\\]*)"', match[0])
        if re.sub(r'"[^"\\]*"|[\s,]', '', match[0]):
            raise ValueError('Unsupported production profile expression')
        arrays.append(values)
    keys, values = arrays
    if len(keys) != len(values) or len(set(keys)) != len(keys):
        raise ValueError('Ambiguous production pane profile')
    return dict(zip(keys, values))


def classify(catalog, dump, sources):
    rows, selected = [], {}
    def add(key, status, level, expected, observed=None, source=None):
        rows.append(dict(id=key, classification=status, level=level, expected=expected,
                         observed=observed, source=source))
    try:
        production = source_profile(sources[PROFILE_SOURCE])
    except ValueError as error:
        production = {}
        add('production_profile', 'ambiguous', 'source_text', str(error), source=PROFILE_SOURCE)
    binding = catalog.get('paneScrape', {})
    if not isinstance(binding, dict):
        binding = {}
    for key, value in PROFILE.items():
        actual = dict(catalog=binding.get(key), production=production.get(key))
        add('profile.' + key, 'exact' if actual == dict(catalog=value, production=value) else 'changed',
            'current_catalog_and_source', value, actual, PROFILE_SOURCE)
    if set(production) != set(PROFILE):
        add('production_profile_keys', 'changed', 'source_text', sorted(PROFILE), sorted(production), PROFILE_SOURCE)
    # Always select the pinned scope, never retarget onto a changed catalog path.
    targets = dict(pane=(PROFILE['panePath'], 'Scrape'), freezing=(PROFILE['freezingPath'], 'Freezing'),
                   interior_frost=(PROFILE['freezingPath'], 'GlassFrosting'), hand=(PROFILE['handPath'], 'PickUp'),
                   inside=(PROFILE['insidePath'], 'PlayerTrigger'), tool_factory=('Spawner/CreateItems', 'IceScraper'))
    for key, (path, name) in targets.items():
        matches = [f for f in dump['fsms'] if f.get('path') == path and f.get('fsmName') == name]
        status = 'exact' if len(matches) == 1 else 'ambiguous' if matches else 'changed'
        add('fsm.' + key, status, 'retained_catalog_identity', path + '::' + name,
            dict(count=len(matches), net_ids=[f.get('netId') for f in matches]), 'catalog/dump-23268598.json')
        if len(matches) != 1:
            continue
        f = matches[0]
        selected[key] = dict(raw=f, runtime_initialized=None, runtime_started=None,
                             active_and_state_are_historical_dump_only=True)
        add('native.initialization.' + key, 'unreachable', 'live_native',
            'unique live FSM; Initialized and Started; action states IsInitialized before binding',
            'BLOCKED: no native process; active/activeState in old catalog are not these flags', BINDINGS_SOURCE)

    variables = {
        'pane': {'Float': ['Distance', 'X', 'Xold'], 'GameObject': ['Windows', 'This', 'InsideTrigger']},
        'freezing': {'Float': ['CutoffWindshield', 'ScrapeEfficiency', 'BodyTempAdd'],
                     'GameObject': ['GlassPos', 'RoofCheck'], 'Bool': ['UnderRoof'], 'Material': ['6']},
        'interior_frost': {'Float': ['Frost'], 'Material': ['FrostGlass']},
        'hand': {'GameObject': ['PickedObject']}, 'inside': {'Bool': ['PlayerInside']}}
    states = {'pane': ['Mouse off 2', 'Get scroll', 'Get this glass', 'Scrape 1', 'Scrape 2', 'Player outside?', 'Init'],
              'freezing': ['State 7', 'Sound', 'Update', 'Delay', 'Check roof'],
              'hand': ['Set pivot 2', 'Ice Scraper', 'Off', 'Drop part', 'Drop part 2', 'Look for object']}
    for key, item in selected.items():
        f = item['raw']
        for kind, names in variables.get(key, {}).items():
            available = f.get('variables', {}).get(kind + 'Variables')
            for name in names:
                count = available.count(name) if isinstance(available, list) else None
                status = 'unreachable' if count is None else 'exact' if count == 1 else 'ambiguous' if count > 1 else 'changed'
                add('variable.' + key + '.' + kind + '.' + name, status, 'retained_catalog_declaration',
                    'one ' + kind + ' ' + name, dict(count=count, value='NOT_RECORDED'), 'catalog/dump-23268598.json')
        for name in states.get(key, []):
            matches = [s for s in f.get('states', []) if s.get('name') == name]
            add('state.' + key + '.' + name, 'exact' if len(matches) == 1 else 'ambiguous' if matches else 'changed',
                'retained_catalog_declaration', name, matches, 'catalog/dump-23268598.json')
        if key in ('pane', 'freezing'):
            for event in (('SCRAPER_ON', 'SCRAPER_OFF') if key == 'pane' else ('WINDSHIELD', 'FREEZE')):
                count = f.get('events', []).count(event)
                add('event.' + key + '.' + event, 'exact' if count == 1 else 'ambiguous' if count > 1 else 'changed',
                    'retained_catalog_declaration', event, dict(count=count, globalTransitions=f.get('globalTransitions')))

    for key, expectation in NATIVE_EXPECTATIONS.items():
        add('native.' + key, 'unreachable', 'live_native', expectation,
            'BLOCKED: current live actions/fields/contact/save observations unavailable; source expectations are not native evidence',
            expectation['source'])
    for path, anchors in ANCHORS.items():
        lines = sources[path].splitlines()
        for index, anchor in enumerate(anchors):
            matches = [dict(line=i + 1, text=line) for i, line in enumerate(lines) if anchor in line]
            add('source.' + path + '.' + str(index), 'exact' if matches else 'changed', 'source_text_only',
                anchor, matches, path)
    try:
        discovery = discover_pane(catalog, dump)
        helper = dict(passed=True, action_fields_available=discovery['action_fields_available'],
                      global_transitions_available=discovery['global_transitions_available'])
    except (ValueError, KeyError, TypeError) as error:
        helper = dict(passed=False, error=str(error))
    readiness = readiness_report(PROFILE, dump, sources)
    return dict(schema_version=2, evidence_level='fresh executable inspection of current source/catalog; no fresh native discovery',
                V11_status='partial', native_executed=False, dump_meta=dump.get('meta'),
                bindings=rows, selected_fsms=selected, helper_check=helper,
                readiness=readiness,
                static_checks_passed=helper['passed'] and readiness['catalog_routes_match'] and readiness['source_seams_match']
                    and not any(r['classification'] in ('changed', 'ambiguous') for r in rows),
                classification_counts=dict(Counter(r['classification'] for r in rows)),
                dimensions={key: 'NOT_TESTED' for key in provenance.DIMENSIONS},
                limits=['Exact means exact only at the row evidence level, not a native gameplay pass.',
                        'Old catalog timestamps/activeState and historical native descriptions are not live initialization.',
                        'Names-only catalog cannot prove action fields, material value, tool existence, eye contact or absence of saves.',
                        'No new persistence, other-pane/vehicle behavior or authorizations are inferred.'])


def read_input(path, root):
    if path != path.resolve(strict=True) or not path.is_relative_to(root.resolve(strict=True)):
        raise ValueError('Input must be a nonredirected file under its declared root: ' + str(path))
    if path.stat().st_size > 64 * 1024 * 1024:
        raise ValueError('Input exceeds bounded source/catalog size')
    before = provenance.diag.path_observation(path)
    with provenance.diag.readonly_descriptor(path) as (fd, flags):
        row = dict(before=before)
        provenance.diag.begin_descriptor(row, fd, flags)
        chunks, remaining = [], min(row['descriptor_before']['size'] + 1, 64 * 1024 * 1024 + 1)
        while remaining:
            data = os.read(fd, min(remaining, 1024 * 1024))
            if not data:
                break
            chunks.append(data)
            remaining -= len(data)
        raw = b''.join(chunks)
        provenance.diag.end_descriptor(row, fd, path)
    if not row['identity_stable'] or len(raw) != row['descriptor_before']['size']:
        raise ValueError('Input changed during read: ' + str(path))
    row.update(sha256=hashlib.sha256(raw).hexdigest(), bytes_read=len(raw))
    return raw, row


def load_gate(run, name):
    provenance.leaf(name)
    directory = run / name
    report = strict_json(read_input(directory / 'report.json', run)[0])
    assessment = strict_json(read_input(directory / 'assessment.json', run)[0])
    if (report.get('run') != str(run) or report.get('output') != str(directory)
            or report.get('gate_status') != 'BLOCKED' or report.get('native_launch_allowed') is not False
            or report.get('target_content_bytes_read') != 0
            or report.get('exit_code') != 1
            or report.get('host_observation_status') != 'NOT_RUN: retained-only reconciliation'
            or assessment != provenance.gate_assessment(report)):
        raise ValueError('Require an unchanged same-RUN retained-only BLOCKED observation, not a launch override')
    return report, assessment


def execute(run, name, gate_name):
    output = provenance.create_output(run, name)
    receipt = dict(started_utc=provenance.diag.utc(), argv=[sys.executable] + sys.argv,
                   cwd=str(Path.cwd()), run=str(run), output=str(output), exit_code=2,
                   role='read-only catalog/source observer; neither host nor guest',
                   native_launch_allowed=False, native_launched=False, gate_status='BLOCKED',
                   input_reads={}, existing_binaries_not_deployed={}, cleanup={})
    raw_inputs = {}
    try:
        gate, assessment = load_gate(run, gate_name)
        receipt['provenance'] = dict(directory=str(run / gate_name), started_utc=gate.get('started_utc'),
                                     ended_utc=gate.get('ended_utc'), assessment=assessment)
        # Retain exact bytes, including the old dump, to allow offline replay. No
        # prior evidence is rewritten. Neither source nor installed data is opened writable.
        for path in SOURCE_INPUTS:
            raw, row = read_input(SOURCE / path, SOURCE)
            target = output / 'inputs' / path
            target.parent.mkdir(parents=True, exist_ok=True)
            with target.open('xb') as stream:
                stream.write(raw)
            raw_inputs[path] = raw
            receipt['input_reads'][path] = row
        for path in BINARY_INPUTS:
            if (SOURCE / path).is_file():
                receipt['existing_binaries_not_deployed'][path] = read_input(SOURCE / path, SOURCE)[1]
            else:
                receipt['existing_binaries_not_deployed'][path] = dict(status='NOT_BUILT')
        gate_hashes = {}
        for path in sorted((run / gate_name).iterdir()):
            if path.suffix == '.json':
                gate_hashes[path.name] = read_input(path, run)[1]['sha256']
        receipt['provenance_raw_sha256'] = gate_hashes
        result = classify(strict_json(raw_inputs['catalog/sync-catalog.json']),
                          strict_json(raw_inputs['catalog/dump-23268598.json']),
                          {p: raw_inputs[p].decode('utf-8') for p in TEXT_INPUTS})
        provenance.diag.save_json(output / 'discovery.json', result)
        after = {p: read_input(SOURCE / p, SOURCE)[1] for p in SOURCE_INPUTS}
        unchanged = all(after[p]['sha256'] == receipt['input_reads'][p]['sha256']
                        and after[p]['descriptor_before'] == receipt['input_reads'][p]['descriptor_after'] for p in SOURCE_INPUTS)
        provenance.diag.save_json(output / 'inputs-after.json', after)
        receipt.update(inputs_unchanged=unchanged, static_checks_passed=result['static_checks_passed'],
                       classification_counts=result['classification_counts'], dimensions=result['dimensions'], V11_status='partial',
                       exit_code=1 if unchanged and result['static_checks_passed'] else 2)
        if load_gate(run, gate_name) != (gate, assessment):
            raise ValueError('Provenance receipt changed during audit')
    except (OSError, ValueError, KeyError, TypeError) as error:
        receipt.update(exit_code=2, error=type(error).__name__ + ': ' + str(error))
    finally:
        receipt['cleanup'] = dict(descriptors_closed=True, child_processes_started=[], processes_signaled=[],
                                  rig_accesses=[], deployments=[], protected_content_reads=[],
                                  controller_writes=[], original_checkout_accesses=[],
                                  scope='Only source inputs and same-RUN retained JSON read; only new RUN leaf written. '
                                        'The separate provenance observer opened protected-log metadata with zero content bytes. '
                                        'No game/rig resource was acquired and no unrelated process was inspected or signaled.')
        receipt['ended_utc'] = provenance.diag.utc()
        receipt['output_sha256'] = {str(p.relative_to(output)): hashlib.sha256(p.read_bytes()).hexdigest()
                                    for p in sorted(output.rglob('*')) if p.is_file()}
        provenance.diag.save_json(output / 'receipt.json', receipt)
    return receipt


def verify(output):
    """Replay retained catalog/source without opening a native or protected input."""
    receipt = strict_json(read_input(output / 'receipt.json', output)[0])
    expected = {'discovery.json', 'inputs-after.json'} | {'inputs/' + p for p in SOURCE_INPUTS}
    hashes = receipt['output_sha256']
    if set(hashes) != expected:
        raise ValueError('Incomplete or redirected output hash manifest')
    raw = {}
    for name, digest in hashes.items():
        raw[name], row = read_input(output / name, output)
        if row['sha256'] != digest:
            raise ValueError('Output hash mismatch: ' + name)
    sources = {p: raw['inputs/' + p].decode('utf-8') for p in TEXT_INPUTS}
    replay = classify(strict_json(raw['inputs/catalog/sync-catalog.json']),
                      strict_json(raw['inputs/catalog/dump-23268598.json']), sources)
    if (replay != strict_json(raw['discovery.json']) or not replay['static_checks_passed']
            or receipt.get('static_checks_passed') is not True or receipt.get('inputs_unchanged') is not True
            or receipt.get('native_launch_allowed') is not False or receipt.get('native_launched') is not False
            or receipt.get('gate_status') != 'BLOCKED' or receipt.get('exit_code') != 1
            or receipt.get('classification_counts') != replay['classification_counts']):
        raise ValueError('Offline replay/result mismatch; not a verified BLOCKED receipt')
    gate_dir = Path(receipt['provenance']['directory'])
    if gate_dir.parent != output.parent:
        raise ValueError('Provenance must remain under the same RUN')
    _, assessment = load_gate(output.parent, gate_dir.name)
    if receipt['provenance']['assessment'] != assessment:
        raise ValueError('Provenance assessment mismatch')
    for name, digest in receipt['provenance_raw_sha256'].items():
        provenance.leaf(name)
        if read_input(gate_dir / name, output.parent)[1]['sha256'] != digest:
            raise ValueError('Provenance hash mismatch: ' + name)
    return dict(offline_replay_equal=True, output_hashes_checked=len(hashes),
                provenance_hashes_checked=len(receipt['provenance_raw_sha256']),
                classification_counts=replay['classification_counts'], gate_status='BLOCKED',
                native_launch_allowed=False, evidence_level='offline artifact replay, not native execution')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--name', required=True, help='new output leaf; never overwrites prior evidence')
    parser.add_argument('--provenance', help='same-RUN retained-only provenance leaf (required for audit)')
    parser.add_argument('--verify', action='store_true', help='verify/replay the existing named output; no writes')
    args = parser.parse_args()
    try:
        if args.verify:
            provenance.leaf(args.name)
            if (str(args.run) != os.environ.get('RUN') or args.run != args.run.resolve(strict=True)
                    or args.run.parent != provenance.diag.ROUNDS or not (args.run / 'contract.json').is_file()):
                raise ValueError('Require the actual assigned RUN')
            print(json.dumps(verify(args.run / args.name)))
            return 0
        if not args.provenance:
            parser.error('--provenance is required for a new audit')
        receipt = execute(args.run, args.name, args.provenance)
    except (OSError, ValueError) as error:
        print(json.dumps(dict(exit_code=2, error=str(error), native_launch_allowed=False)))
        return 2
    print(json.dumps({k: receipt.get(k) for k in ('output', 'exit_code', 'gate_status', 'static_checks_passed',
                                                'classification_counts', 'inputs_unchanged', 'error')}))
    return receipt['exit_code']


if __name__ == '__main__':
    raise SystemExit(main())
