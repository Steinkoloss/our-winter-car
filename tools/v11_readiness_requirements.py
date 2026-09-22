"""Nested retained-catalog trace and native operand checklist, never a launch gate.

A declared transition is not an observed event or a runtime action operand. Keep
that distinction even when a later dump happens to contain action-shaped JSON.
"""
BIND = 'src/WinterMP.Core/Sync/PaneScrapeSync.Bindings.cs'
SYNC = 'src/WinterMP.Core/Sync/PaneScrapeSync.cs'
AUTH = 'src/WinterMP.Net/Sync/PaneScrapeAuthority.cs'
CLIMATE = 'src/WinterMP.Core/Sync/VehicleWorldSync.Climate.cs'
PROBE = 'tools/GuestSaveProbe/LiveBagProbe.Pane.cs'
TRACE = 'tools/GuestSaveProbe/LiveBagProbe.PaneTrace.cs'
DRIVER = 'tools/v11_pane_audit.py'
WRONG = 'tools/PaneScrapeBridge.Tests/BridgeTests.WrongPane.cs'
BRIDGE = 'tools/PaneScrapeBridge.Tests/BridgeTests.cs'
EXTRA_TEXT_INPUTS = (PROBE, TRACE, WRONG, BRIDGE, DRIVER)

# Selected nested edges, not all similarly named Scrape FSMs in the world.
ROUTES = {
    'pane': {'Mouse off 2': {'FINISHED': 'Get scroll'},
             'Get scroll': {'DOWN': 'Get this glass'},
             'Get this glass': {'FINISHED': 'Scrape 1'},
             'Scrape 1': {'SCRAPE': 'Scrape 2', 'OFF': 'Mouse off 2'},
             'Scrape 2': {'SCRAPE': 'Scrape 1', 'OFF': 'Mouse off 2'},
             'Init': {'FINISHED': 'Player outside?'}, 'Player outside?': {'DOWN': 'Mouse off 2'}},
    'hand': {'Set pivot 2': {'FINISHED': 'Item picked'},
             'Item picked': {'EQUIP': 'Check item', 'PROCEED Drop': 'Drop part', 'PROCEED Throw': 'Drop part 2'},
             'Check item': {'ICESCRAPER': 'Ice Scraper'}, 'Ice Scraper': {'FINISHED': 'Off'},
             'Off': {'FINISHED': 'Hand'}, 'Hand': {'FINISHED': 'Set pivot 2'}},
    'freezing': {'State 7': {'FINISHED': 'Sound'}, 'Sound': {'FINISHED': 'Update'},
                 'Delay': {'FINISHED': 'Check roof'}, 'Check roof': {'FINISHED': 'Update'}},
    'tool_factory': {'Load ID': {'FINISHED': 'Idle', 'LOOP': 'Add ID'},
                     'Create product': {'FINISHED': 'Idle'}, 'Save': {}, 'Save new': {'FINISHED': 'Load ID'}},
    'tool_save': {'State 1': {'FINISHED': 'Load', 'SAVE': 'State 3'},
                  'Load': {'FINISHED': 'State 3'}, 'Save': {'DESTROY': 'State 6'}},
}

SEAMS = {
    'native_objects': [(BIND, 'found.Fsm.Initialized && found.Fsm.Started'),
                       (BIND, 'Ambiguous shared scraper identity.'), (BIND, 'delta.Actions.Length != 2')],
    'contact': [(SYNC, '=> Physics.Raycast('), (SYNC, 'hit.collider == _pane'),
                (SYNC, 'Physics.DefaultRaycastLayers'), (PROBE, 'coll.Raycast'),
                (PROBE, 'var origin = target.bounds.center')],
    'tool_lease': [(BIND, '_owner.Send(ScraperOperation.Pickup)'),
                   (BIND, 'HookHand("equip", ScraperOperation.Equip)'),
                   (SYNC, '_lease.Apply(actor, action,'), (PROBE, 'ObjectNumberInt'),
                   (PROBE, 'FindFsmGameObject("PickedObject").Value = ScraperBody().gameObject')],
    'host_away_guest': [(SYNC, 'age > .6f'), (SYNC, 'VehicleParked = Parked'),
                        (DRIVER, 'receipt[\'host_pose\'] = snapshot_until(\'host\', fresh_guest_pose)'),
                        (PROBE, 'player.position = body.transform.TransformPoint(new Vector3(2, 1, 1))')],
    'accepted_action': [(BIND, 'replacement[eventIndex] = new StrokeAction'),
                        (SYNC, '_authority.Decide(actor, action.Intent())'),
                        (SYNC, '_glassActions![0].OnEnter(); _glassActions[1].OnEnter();'),
                        (TRACE, 'request.Operation == ScraperOperation.Stroke'),
                        (WRONG, 'FreshWrongPaneFromGuestHookConsumesSequenceWithoutMutationAndFreshStrokeRecovers')],
    'absolute_result': [(SYNC, 'Revision = snapshot.Revision, Cutoff = snapshot.Cutoff'),
                        (SYNC, '_cutoff.Value = _replica.Current.Cutoff;'),
                        (CLIMATE, 'if (PaneScrapeSync.Instance?.OwnsPane(item.Id) != true)'),
                        (CLIMATE, 'ApplyRemoteFrostLevel(item, frost);'),
                        (DRIVER, 'check_actor_effects(h, g)')],
    'vanilla_save_reload': [(AUTH, 'float cutoff = _host.ReadWindshieldCutoff();'),
                            (SYNC, '_authority = null; _replica = null; _lease = null; _received = null;'),
                            (SYNC, 'RestoreBindings();')],
}

MISSING = {
    'native_objects': [
        ('live-fsm-identity', 'Fresh host AND guest pane-describe/pane-snapshot: exact paths, Initialized/Started, state IsInitialized, colliders and bridge binding.'),
        ('live-eye-and-tool-map', 'Live GameObject hierarchy for eye and Hand, shared item registry IDs and picked rigidbody. The retained dump is not a complete object tree.')],
    'contact': [
        ('native-mousepick-operands', 'Pane Scrape MousePickEvent fields: camera, layer, ray distance; X/Xold, This, Windows and InsideTrigger object references.'),
        ('host-first-hit-contact', 'Fresh authenticated guest pose plus production ReadContext first-hit pane identity/distance <= .8m; tool pickup first hit <= 1m; near/far/obstruction observations.')],
    'tool_lease': [
        ('shared-tool-identity', 'Both live item registries must identify exactly one ice scraper(itemx). Resolve IceScraper.Prefab/New and icescraper0.Use.Owner to the shared body, not a camera prop.'),
        ('pickup-equip-admission', 'Actual guest Set pivot 2 -> native attachment resume -> Ice Scraper requests and host exclusive Holder/Equipped lease with fresh epoch/sequence/tool ID.')],
    'host_away_guest': [
        ('guest-spawn-readiness', 'Fresh same-run readiness.json: real guest command response, resume selection/relocation and host-observed alive pose aged 0..0.6s, without pose/ready injection.'),
        ('host-away-nonowner-preconditions', 'Both ownership snapshots, linear/angular speed, guest outside trigger/seat, host feet away/no tool, matching shared vehicle/tool identity. bridge() currently does not assert all of these.')],
    'accepted_action': [
        ('native-action-fields', 'Fresh Scrape 2.SendEventByName target/event and Freezing.State 7 FloatAdd/SetMaterialFloat references/one-shot flags; Sound action operands and actor-local effect target.'),
        ('native-action-trace', 'Actual host and authorized non-owner guest hook/packet/decision traces, accepted once, duplicate denial, invalid contact and fresh WrongPane/replay/recovery. No forged final values.')],
    'absolute_result': [
        ('native-absolute-result', 'Before/after host and guest CutoffWindshield AND material 6._Cutoff, exact revision, host glass call count and guest-only effects; accepted action generated the result.'),
        ('native-frost-noninterference', 'Before/after GlassFrosting.Frost/FrostGlass and other-pane cutoff/material observations. Interior frost is separate, not the windshield cutoff.')],
    'vanilla_save_reload': [
        ('native-save-field-schema', 'Actual ES2 save/load fields/tags and global transitions for IceScraper factory and icescraper0.Use; Freezing FREEZE/Delay/Check roof initialization and any save hooks.'),
        ('copied-save-cold-reload', 'Protected, copied-save native save/stop/restart observations of pane/material and tool mapping, plus reconnect/new guest observations. No sidecar or replayed scrape value.')],
}

DRIVER_LIMITS = {
    'pane-contact': 'collider.Raycast on the selected collider bypasses scene occlusion and production Physics.Raycast; not input/contact acceptance.',
    'pane-fixture-tool': 'Host SPAWNITEM uses a temporary spawn point only if ObjectNumberInt is zero; not an ordinary purchase or proof an existing tool is uniquely bound.',
    'pane-pickup-tool': 'Assigns native PickedObject and enters Set pivot 2; production lease gate remains, but normal object selection is bypassed.',
    'pane-aim-glass': 'Teleports/holds camera at collider bounds plus car.forward offset; actual camera alignment and first-hit geometry remain unproved.',
    'pane-stroke-bridge': 'Enters Get this glass and Scrape 2; production replacement emits intent, not ordinary input or camera-X reversal.',
    'pane-seed': 'Sets initial host cutoff .25 only; not a result or persistence observation.',
    'bridge_host_away': 'Host-away and both non-owner/linear+angular preconditions are not asserted in bridge(); pane-park places both players near the car. Historical audit() rollback is not guest acceptance.',
    'wrong_pane': 'Fresh one-shot WrongPane/replay/recovery is portable only. Native pane-replay-stroke repeats a prior sequence unchanged; it cannot establish fresh WrongPane.',
}


def status(count):
    return 'DECLARED' if count == 1 else 'MISSING' if count == 0 else 'AMBIGUOUS'


def selection(records, predicate, pointer):
    if not isinstance(records, list) or any(not isinstance(r, dict) for r in records):
        return dict(status='MALFORMED', count=None, pointer=pointer), None
    matches = [(i, r) for i, r in enumerate(records) if predicate(r)]
    row = dict(status=status(len(matches)), count=len(matches), pointer=pointer,
               matching_pointers=[pointer + '/' + str(i) for i, _ in matches])
    if len(matches) != 1:
        return row, None
    i, record = matches[0]
    row['pointer'] += '/' + str(i)
    return row, record


def trace_node(dump, kind, path, fsm, routes):
    row, record = selection(dump.get(kind), lambda r: r.get('path') == path and
                            (fsm is None or r.get('fsmName') == fsm), '/' + kind)
    row.update(path=path, fsm=fsm, level='retained_catalog_declaration', states={})
    if record is None:
        return row
    row.update(net_id=record.get('netId'), raw=record,
               path_components=path.split('/'), parent_objects_verified=False)
    for name, expected in routes.items():
        state, raw = selection(record.get('states'), lambda s: s.get('name') == name, row['pointer'] + '/states')
        state['routes'] = {}
        row['states'][name] = state
        if raw is None:
            continue
        actions = raw.get('actions')
        state['actions'] = dict(status='NOT_RECORDED' if 'actions' not in raw else 'RETAINED_ONLY',
                                pointer=state['pointer'] + '/actions', raw=actions,
                                action_types=raw.get('actionTypes'), live_fields_verified=False)
        for event, destination in expected.items():
            edge, value = selection(raw.get('transitions'), lambda t: t.get('event') == event,
                                    state['pointer'] + '/transitions')
            edge.update(expected_destination=destination, observed=value)
            if value is not None and value.get('to') != destination:
                edge['status'] = 'CHANGED'
            if value is not None and sum(s.get('name') == destination for s in record['states']) != 1:
                edge['status'] = 'UNRESOLVED_DESTINATION'
            state['routes'][event] = edge
    return row


def readiness_report(profile, dump, sources):
    targets = {
        'pane': ('fsms', profile['panePath'], 'Scrape'),
        'hand': ('fsms', profile['handPath'], 'PickUp'),
        'freezing': ('fsms', profile['freezingPath'], 'Freezing'),
        'interior_frost': ('fsms', profile['freezingPath'], 'GlassFrosting'),
        'inside': ('fsms', profile['insidePath'], 'PlayerTrigger'),
        'tool_factory': ('fsms', 'Spawner/CreateItems', 'IceScraper'),
        'tool_save': ('fsms', 'icescraper0', 'Use'),
        'pane_body': ('rigidbodies', profile['panePath'], None),
        'tool_saved_body': ('rigidbodies', 'icescraper0', None),
    }
    nodes = {key: trace_node(dump, *target, ROUTES.get(key, {})) for key, target in targets.items()}
    def declared(node):
        return node['status'] == 'DECLARED' and all(s['status'] == 'DECLARED' and
            all(e['status'] == 'DECLARED' for e in s['routes'].values()) for s in node['states'].values())
    missing_anchors, prerequisites = [], {}
    for key, anchors in SEAMS.items():
        evidence = []
        for path, anchor in anchors:
            matches = [dict(line=i + 1, text=line) for i, line in enumerate(sources[path].splitlines()) if anchor in line]
            evidence.append(dict(path=path, anchor=anchor, matches=matches, level='source_text_only'))
            if not matches:
                missing_anchors.append(dict(path=path, anchor=anchor))
        prerequisites[key] = dict(status='NOT_READY', source_evidence=evidence,
            missing_artifacts=[dict(id=identifier, required_capture=details,
                                   status='NOT_SUPPLIED', blocked_by='protected_input') for identifier, details in MISSING[key]])
    return dict(schema_version=1, native_status='NOT_READY', native_executed=False,
        catalog_routes_match=all(declared(n) for n in nodes.values()),
        source_seams_match=not missing_anchors, missing_source_anchors=missing_anchors,
        catalog_nodes=nodes, prerequisites=prerequisites, driver_limits=DRIVER_LIMITS,
        protected_input=dict(status='BLOCKED', native_launch_allowed=False,
            independent_original_provenance='MISSING', independent_drift_attribution='MISSING',
            evidence='See receipt.provenance.directory assessment.json and pinned raw receipt hashes; no protected content opened by this audit.',
            missing_artifact='Independently retained original-time log bytes with custody/identity or independently attributable drift reconciliation. Repeated same-host hashes cannot supply it.'),
        portable_fixture_execution=dict(status='NOT_TESTED',
            evidence_level='This read-only audit does not run tests. Companion portable command receipts/TRX must be checked separately.',
            sources=[BRIDGE, WRONG, 'tools/PaneScrapeBridge.Tests/GuestStrokeWire.cs'],
            limits='Production-linked hooks/codec/authority/result use explicit engine/session/physics doubles; even green fixtures never promote native readiness.'),
        persistence=dict(windshield_saved='UNKNOWN', tool_saved_fields='UNKNOWN',
            tool_save_declarations=nodes['tool_save']['status'],
            source_policy='Capture observes vanilla cutoff; ResetSession clears in-memory authority. No new persistence or saved-stroke replay.',
            distinction='icescraper0.Use has Save/Load declarations; this does not prove tool field semantics or windshield persistence. Missing Freezing save metadata is unknown, not no-save proof.'),
        limits=['FSM netId, rigidbody netId and live shared item ID are different namespaces; no equivalence inferred.',
                'Path components are nested names, not verified parent objects; eye/camera prop identity needs a live hierarchy.',
                'Nested catalog transitions prove declarations only. Action operands/global event targets/initialization remain missing.',
                'No ordinary input, Steam/two-PC, different-save, late-join, native save/reload or four-player soak acceptance.'])
