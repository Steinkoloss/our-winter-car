#!/usr/bin/env python3
"""Extract unmodified session lifecycle code into an isolated portable dependency namespace."""
import argparse
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from s09_train_fixture import SOURCE, read
from s09_update_manifest import extract

SIGNATURES = (
    'public void Shutdown(', 'private void DisposeSessionTransport(',
    'private void ResetSessionRuntimeState(', 'private void SetState(',
    'private void AttachTransport(', 'private void SendTo(',
    'private void FlushFailedSessionCleanup(', 'private void FailSession(',
)
AUX_METHODS = (('Session/SessionManager.Launch.cs', 'private static string PendingLaunchStatus('),)
FIELDS = (
    'State', 'IsHost', 'LocalPlayerId', 'StatusText', 'PermanentDeathEnabled',
    '_transport', '_hostPeer', '_devClient', '_playersByPeer', '_sendWriter',
    '_nextPlayerId', '_nextPingAt', '_steamOpStartedAt', '_pingNonce', '_pendingPings',
    '_nextSnapshotRequestAt', '_nextResyncRequestAt', '_nextObjectStateRequestAt',
    '_failedSessionCleanupPending', '_joinAttempt', '_passengerSeats', '_deathSession',
    '_guestSlotsBySteam', '_steamLobbyAttemptActive', '_bypassHostPlayerGate',
    '_joinBrowseActive', '_failedAt', '_pendingMode',
)
CLOTHING_FIELDS = ('LocalClothingAdmission', '_clothingSequence')


def field_declaration(source, name):
    matches = re.findall(r'^        (?:public|private|internal) [\w<>,? ]+ ' + re.escape(name)
                         + r'(?:\s*=(?!=)|\s*;|\s*\{)[^\n]*$', source, re.M)
    assert len(matches) == 1, (name, matches)
    return matches[0]


def generate(out):
    source = read('Session/SessionManager.cs')
    fields = [field_declaration(source, name) for name in FIELDS]
    clothing = read('Session/SessionManager.Clothing.cs')
    fields += [field_declaration(clothing, name) for name in CLOTHING_FIELDS]
    methods = [extract(source, signature) for signature in SIGNATURES]
    methods += [extract(read(path), signature) for path, signature in AUX_METHODS]
    imports = ('#nullable enable\nusing System; using System.Collections.Generic; using UnityEngine; '
               'using WinterMP.Core; using WinterMP.Core.Session; using WinterMP.Net; '
               'using WinterMP.Net.Messages; using WinterMP.Net.Sync; using WinterMP.Net.Transport;\n')
    text = (imports + 'namespace S09.ShutdownFixture {\n' + extract(source, 'public enum SessionState')
            + '\n' + extract(read('LaunchOptions.cs'), 'public enum LaunchMode')
            + '\ninternal sealed partial class SessionManager {\n' + '\n'.join(fields)
            + '\n' + extract(source, 'private sealed class GuestSlot') + '\n'
            + '\n'.join(methods) + '\n} }\n')
    out.mkdir(parents=True, exist_ok=True)
    (out / 'Shutdown.g.cs').write_text(text)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    assert args.out.resolve().is_relative_to(SOURCE / 'tools/TrainSend.Tests/obj')
    generate(args.out.resolve())
