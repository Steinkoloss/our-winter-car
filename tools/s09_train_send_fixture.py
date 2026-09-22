#!/usr/bin/env python3
"""Extend the existing exact Train dispatch fixture with real SendTo/codec and a transport double.

Only test dependencies are adapted. Production method bodies are never rewritten.
The object handler is real; its producer is an explicit short sentinel double, not
native object discovery or the full BuildObjectStateMessages iterator.
"""
import argparse
import hashlib
import json
from pathlib import Path
from s09_train_fixture import SOURCE, CORE, build, read
from s09_update_manifest import extract


def generate(out):
    build(out)
    imports = '#nullable enable\nusing System; using System.Collections.Generic; using UnityEngine; using WinterMP.Net; using WinterMP.Net.Messages; using WinterMP.Net.Sync; using WinterMP.Net.Transport;\n'
    methods = [extract(read('Session/SessionManager.cs'), 'private void SendTo('),
               extract(read('Session/SessionManager.Handlers.cs'), 'private void HandleObjectStateRequest(')]
    (out / 'Send.g.cs').write_text(imports + 'namespace WinterMP.Core.Session { internal sealed partial class SessionManager {\n' + '\n'.join(methods) + '\n} }')
    dependencies = (SOURCE / 'tools/TrainDispatch.Tests/Doubles.cs').read_text()
    old_send = extract(dependencies, '        internal void SendTo(')
    assert 'BeforeSend(message); Sent.Add(message);' in old_send
    dependencies = dependencies.replace(old_send, '')
    assert dependencies.count('internal sealed class NetTrafficMeter') == 1
    dependencies = dependencies.replace('internal sealed class NetTrafficMeter', 'internal sealed partial class NetTrafficMeter')
    (out / 'Dependencies.g.cs').write_text('#nullable enable\n' + dependencies)
    # Include the exact object-request switch arm alongside the baseline selected arms.
    session = (out / 'Session.g.cs').read_text()
    messages = read('Session/SessionManager.Messages.cs')
    start = messages.index('case WorldObjectStateRequest objectRequest when IsHost:')
    end = messages.index('case ', start + 5)
    arm = messages[start:end].rstrip()
    anchor = 'switch (message) {\n'
    assert session.count(anchor) == 1
    (out / 'Session.g.cs').write_text(session.replace(anchor, anchor + arm + '\n'))


def baseline(run):
    assert run.parent == SOURCE.parent / 'rounds' and (run / 'contract.json').is_file()
    before = json.loads((run / 'before.json').read_text())
    target = run / 'baseline'
    target.mkdir()
    for leaf in ('SessionManager.cs', 'SessionManager.Handlers.cs'):
        path = CORE / 'Session' / leaf
        assert hashlib.sha256(path.read_bytes()).hexdigest() == before[str(path.relative_to(SOURCE))]
        (target / leaf).write_bytes(path.read_bytes())
    print('PASS: baseline Session source equals assignment hashes; saved in ' + str(target))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--out', type=Path)
    p.add_argument('--baseline', type=Path)
    args = p.parse_args()
    if args.baseline:
        baseline(args.baseline.resolve(strict=True))
    else:
        assert args.out and args.out.resolve().is_relative_to(SOURCE / 'tools/TrainSend.Tests/obj')
        generate(args.out.resolve())
