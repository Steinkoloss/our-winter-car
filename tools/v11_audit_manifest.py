#!/usr/bin/env python3
"""Read-only provenance/handoff checks for the V11 static fallback, not a launch gate."""
import argparse
import json
from pathlib import Path
import v11_pane_audit as audit
from v11_safety_receipt import differences, stability_receipt, changed_chunks


def compare(root, expected):
    rows = {}
    for name, sha in expected.items():
        path = root / name
        if path.name.startswith('.env'):
            rows[name] = {'skipped': 'credential boundary'}
            continue
        actual = audit.digest(path) if path.is_file() else None
        rows[name] = {'expected': sha, 'actual': actual, 'matches': actual == sha}
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--prior-portable', type=Path, required=True)
    parser.add_argument('--static-name', required=True)
    args = parser.parse_args()
    audit.configure_run(args.run)
    if Path(args.static_name).name != args.static_name:
        parser.error('Static output must be a leaf name')
    prior_path = args.prior_portable / 'verification-manifest.json'
    prior = json.loads(prior_path.read_text())
    checks = []
    def check(label, value):
        checks.append({'assertion': label, 'passed': bool(value)})
    source = compare(audit.SOURCE, prior['source_sha256'])
    binaries = compare(audit.SOURCE, prior['binary_sha256'])
    check('prior portable source hashes still match', all(r.get('matches', False) for r in source.values()))
    check('prior portable binary hashes still match', all(r.get('matches', False) for r in binaries.values()))
    prior_commands = []
    for command in prior['commands']:
        path = Path(command['output'])
        sha = audit.digest(path)
        prior_commands.append(dict(command, current_sha256=sha))
        check('prior raw command log digest: ' + path.name, sha == command['sha256'])
    independent_path = args.prior_portable / 'independent-checks/verification.json'
    independent = json.loads(independent_path.read_text())
    check('independent portable/build review explicitly not gameplay',
          independent['portable_passed'] and independent['core_build_passed']
          and 'NOT gameplay acceptance' in independent['evidence_level'])
    original_reference = Path(prior['comparison_reference'])
    check('prior protected/original reference provenance', audit.digest(original_reference) == prior['reference_sha256'])
    original = compare(Path('/home/jaimep/our-winter-car'), json.loads(original_reference.read_text())['original_source'])
    check('known original-checkout files unchanged (not full enumeration)',
          all(r.get('matches', 'skipped' in r) for r in original.values()))

    before = json.loads((args.run / 'before.json').read_text())
    names = set(before)
    for directory in ('src', 'tools', 'docs', 'catalog', 'protocol'):
        for path in (audit.SOURCE / directory).rglob('*'):
            if path.is_file() and not any(p in ('bin', 'obj', '__pycache__', '.venv', 'build', '.pytest_cache') for p in path.parts):
                if not path.name.startswith('.env'): names.add(str(path.relative_to(audit.SOURCE)))
    current = {n: audit.digest(audit.SOURCE / n) if (audit.SOURCE / n).is_file() else None
               for n in sorted(names) if not Path(n).name.startswith('.env')}
    changes = {n: {'before': before.get(n), 'after': sha} for n, sha in current.items() if before.get(n) != sha}
    check('audit-only changes (no production/catalog/protocol)',
          all(n.startswith('tools/') or n == 'docs/V11-PANE-AUDIT.md' for n in changes))
    static = json.loads((args.run / args.static_name / 'receipt.json').read_text())
    check('static discovery passed without launch or rig writes', static['static_assertions_passed']
          and not static['native_launched'] and not static['rig_writes_performed'] and not static['native_launch_allowed'])
    check('comparison retained original baseline and current protected inputs',
          static['baseline_file_unchanged'] and static['observation_unchanged'])
    check('no owned native cleanup left', not static['owned_processes_remaining']
          and not static['owner_marker_present'] and not static['command_bus_present']
          and not static['game_markers'] and not static['profile_markers'])
    observations = {}
    for path in sorted(args.run.glob('*/receipt.json')):
        observation = json.loads(path.read_text())
        if observation.get('evidence_level') != 'static discovery and read-only protected-input comparison':
            continue
        folder = path.parent
        old = json.loads((folder / 'protected-observation-before.json').read_text())
        new = json.loads((folder / 'protected-observation-after.json').read_text())
        observations[folder.name] = dict(receipt=observation, before_after_differences=differences(old, new))
        # Do not choose an earlier stable receipt to conceal a later failed comparison.
        check('protected observation unchanged: ' + folder.name, old == new)
    target = audit.INSTALLED / 'BepInEx/LogOutput-guest.log'
    stability = stability_receipt(target)
    audit.save_json(args.run / 'final-log-descriptor-stability.json', stability)
    chunk_comparisons = {}
    for name in observations:
        old = json.loads((args.run / name / 'differing-input-stability.json').read_text()).get(str(target))
        if old:
            chunk_comparisons[name] = [dict(before=p.get('sha256'), after=q.get('sha256'),
                changed_chunks=changed_chunks(p['chunks'], q['chunks']) if 'chunks' in p and 'chunks' in q else None)
                for p in old['passes'] for q in stability['passes']]
    commands = [json.loads(p.read_text()) for p in sorted(args.run.glob('*.command.json'))]
    for command in commands:
        check('current raw command log digest: ' + Path(command['output']).name,
              audit.digest(Path(command['output'])) == command['sha256'])
    historical = args.run.parent / '000003-work'
    historical_paths = [historical / p for p in ('hand-description.txt', 'pane-final-live/live-bag/host-1.txt',
                        'pane-final-assertions.json', 'pane-final-live/cleanup.json', 'pane-final-reload/cleanup.json')]
    receipt = dict(utc=audit.utc(), evidence_level='portable/static provenance only, NOT native gameplay',
                   changed_files=changes, assertions=checks, commands=commands,
                   prior_manifest=str(prior_path), prior_manifest_sha256=audit.digest(prior_path),
                   prior_commands=prior_commands, prior_source_comparison=source, prior_binary_comparison=binaries,
                   independent_review=independent, independent_review_sha256=audit.digest(independent_path),
                   known_original_source_comparison=original,
                   historical_native_reference_hashes={str(p): audit.digest(p) for p in historical_paths},
                   all_static_observations=observations, log_chunk_comparisons=chunk_comparisons,
                   final_log_stable=stability['stable'],
                   protected_gate=static['protected_gate'], native_launch_allowed=False,
                   dimensions={k: 'NOT_TESTED in current round' for k in ('native_injected', 'ordinary_input',
                       'Steam_two_PC', 'different_save', 'native_rejoin', 'native_cold_reload', 'four_player_soak')},
                   H07_status='partial: prior portable/build evidence preserved, no native acceptance',
                   V11_status='partial: static discovery/reproduced safety blocker, no current native guest result')
    audit.save_json(args.run / 'audit-verification-manifest.json', receipt)
    print(json.dumps({'changed_files': changes, 'assertion_count': len(checks),
                      'failed': [c for c in checks if not c['passed']], 'protected_gate': receipt['protected_gate'],
                      'observation_differences': {n: o['before_after_differences'] for n, o in observations.items()},
                      'log_chunk_comparisons': chunk_comparisons,
                      'prior_binary_comparison': binaries}, indent=2))
    return 0 if all(c['passed'] for c in checks) else 1


if __name__ == '__main__':
    raise SystemExit(main())
