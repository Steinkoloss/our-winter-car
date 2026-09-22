#!/usr/bin/env python3
"""Compile exact selected production methods against portable dependencies, never native Unity.

Generated code lives in obj. The dispatch switch contains only the exact TrainState,
Ping and snapshot request arms; all other handlers are outside this bounded fixture.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
from s09_update_manifest import extract

SOURCE = Path(__file__).resolve().parents[1]
CORE = SOURCE / 'src/WinterMP.Core'


def read(path):
    return (CORE / path).read_text()


def build(out):
    out.mkdir(parents=True, exist_ok=True)
    messages = read('Session/SessionManager.Messages.cs')
    handlers = read('Session/SessionManager.Handlers.cs')
    snapshots = read('Sync/WorldSyncManager.Snapshots.cs')
    callbacks = read('Sync/WorldSyncManager.Callbacks.cs')
    world_handlers = read('Sync/WorldSyncManager.Handlers.cs')

    imports = '#nullable enable\nusing System; using System.Collections.Generic; using UnityEngine; using WinterMP.Core.Diagnostics; using WinterMP.Core.Session; using WinterMP.Net; using WinterMP.Net.Messages; using WinterMP.Net.Sync; using WinterMP.Net.Transport;\n'
    arms = []
    for first, next_case in (
        ('case TrainState trainState when !IsHost:', 'case CoffeeState'),
        ('case PingMessage ping:', 'case PongMessage'),
        ('case WorldResyncRequest resync when IsHost:', 'case WorldObjectStateRequest'),
        ('case WorldSnapshotRequest snapshotRequest when IsHost:', 'case WorldStateChecksum'),
    ):
        start = messages.index(first)
        arms.append(messages[start:messages.index(next_case, start)].rstrip())
    session = [extract(messages, 'private void OnPacketReceived(')]
    session += ['private void HandleMessage(PeerId peer, IMessage message, Channel channel) { switch (message) {\n' + '\n'.join(arms) + '\n} }']
    for sig in ('private void HandleResyncRequest(', 'private static bool TryBeginHostRequest(', 'private void HandleSnapshotRequest('):
        session.append(extract(handlers, sig))
    send_helper = CORE / 'Session/SessionManager.Snapshots.cs'
    if send_helper.is_file():
        session.append(extract(send_helper.read_text(), 'private bool TrySendSnapshotMessage('))
    (out / 'Session.g.cs').write_text(imports + 'namespace WinterMP.Core.Session { internal sealed partial class SessionManager {\n' + '\n'.join(session) + '\n} }')
    # All statements in these iterators, including every unaffected yield, are compiled.
    methods = [extract(snapshots, 'public IEnumerable<IMessage> BuildResyncMessages('),
               extract(snapshots, 'public IEnumerable<IMessage> BuildWorldSnapshot('),
               extract(world_handlers, 'public void OnTrainState(')]
    if 'private TrainState? BuildTrainSnapshot(' in snapshots:
        methods += [extract(snapshots, 'private TrainState? BuildTrainSnapshot('),
                    extract(callbacks, 'private void ResetTrainMessageErrors(')]
        methods += re.findall(r'private readonly CallbackFailure _train(?:Receive|Snapshot)Failure = new CallbackFailure\(\);', callbacks)
    else:
        methods.append('private void ResetTrainMessageErrors() { }')
    methods += [extract(callbacks, 'private sealed class CallbackFailure'),
                extract(callbacks, 'private void HandleSyncError(')]
    (out / 'World.g.cs').write_text(imports + 'namespace WinterMP.Core.Sync { public sealed partial class WorldSyncManager {\n' + '\n'.join(methods) + '\n} }')
    # Do not duplicate Receive's authority/freshness logic in the double.
    receive = extract(read('Sync/TrainSync.cs'), 'internal void Receive(')
    (out / 'Receive.g.cs').write_text(imports + 'namespace WinterMP.Core.Sync { internal sealed partial class TrainDouble {\n' + receive + '\n} }')
    # Every unselected snapshot dependency yields one sentinel so ordering/starvation
    # assertions are meaningful. Train Snapshot itself is an explicit throwing double.
    text = '\n'.join(methods)
    owners = sorted(set(re.findall(r'(_\w+)\.(?:Build\w+|Snapshot)\(', text)) - {'_train'})
    stub_methods = {}
    for match in re.finditer(r'(_\w+)\.(Build\w+|Snapshot)\(([^)]*)\)', text):
        owner, name, args = match.groups()
        if owner == '_train':
            continue
        params = 'byte owner' if args == 'ownerPlayerId' else ''
        enumerable = bool(re.search(r'foreach\s*\([^\n]*\bin ' + re.escape(owner + '.' + name + '('), text))
        stub_methods[(name, params)] = enumerable
    stubs = []
    for (name, params), enumerable in sorted(stub_methods.items()):
        value = 'new PingMessage { Nonce = WorldSyncManager.Label("' + name + '") }'
        stubs.append('internal ' + ('IEnumerable<IMessage>' if enumerable else 'IMessage?') + ' ' + name + '(' + params + ') { ' + ('yield return ' if enumerable else 'return ') + value + '; }')
    fields = 'private readonly SnapshotDouble ' + ', '.join(x + ' = new SnapshotDouble()' for x in owners) + ';'
    (out / 'Snapshots.g.cs').write_text(imports + 'namespace WinterMP.Core.Sync { internal sealed class SnapshotDouble { ' + '\n'.join(stubs) + ' } public sealed partial class WorldSyncManager { ' + fields + ' } }')
    hashes = {str(p.relative_to(SOURCE)): hashlib.sha256(p.read_bytes()).hexdigest() for p in CORE.rglob('*.cs') if 'obj' not in p.parts and 'bin' not in p.parts}
    (out / 'production-sha256.json').write_text(json.dumps(hashes, indent=2) + '\n')


def baseline(run):
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    hashes = {}
    for root in ('src', 'tools', 'docs', 'catalog', 'protocol'):
        for path in (SOURCE / root).rglob('*'):
            if path.is_file() and not path.is_symlink() and not set(path.parts) & {'bin', 'obj', '__pycache__'}:
                hashes[str(path.relative_to(SOURCE))] = hashlib.sha256(path.read_bytes()).hexdigest()
    hashes['PLAN.md'] = hashlib.sha256((SOURCE / 'PLAN.md').read_bytes()).hexdigest()
    expected = json.loads((run / 'before.json').read_text())
    for path in (CORE / 'Sync').glob('WorldSyncManager*.cs'):
        assert hashes[str(path.relative_to(SOURCE))] == expected[str(path.relative_to(SOURCE))]
    for path in (CORE / 'Sync').glob('WorldSyncManager*.cs'):
        target = run / 'baseline' / path.name
        target.parent.mkdir(exist_ok=True)
        with target.open('xb') as f:
            f.write(path.read_bytes())
    print('Saved baseline hashes and exact world source to ' + str(run))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--out', type=Path)
    p.add_argument('--baseline', type=Path)
    args = p.parse_args()
    if args.baseline:
        baseline(args.baseline.resolve(strict=True))
    else:
        assert args.out and args.out.resolve().is_relative_to(SOURCE / 'tools/TrainDispatch.Tests/obj')
        build(args.out.resolve())
