#!/usr/bin/env python3
"""Read-only H08 static/source discovery; does not certify transfer or run a game.

Inputs must be explicit existing extracts. Missing action operands stay unknown;
Boolean Steam.Water is never treated as a numeric supply. Output is fresh-only.
"""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = 'YARD/Building/SAUNA/Sauna'
STEAM = ROOT + '/Kiuas/StoveTrigger'


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def relevant(fsm):
    text = ' '.join([fsm.get('path', ''), fsm.get('fsmName', '')]
                    + [s.get('name', '') for s in fsm.get('states', [])]).lower()
    return any(word in text for word in ('dipper', 'water bucket', 'stovetrigger'))


def compact(fsm):
    return {k: fsm[k] for k in ('path', 'fsmName', 'variables', 'globalTransitions', 'states') if k in fsm}


def analyze(catalog, assets):
    records = []
    seen = set()
    for fsm in catalog.get('fsms', []):
        if relevant(fsm):
            entry = compact(fsm)
            key = json.dumps(entry, sort_keys=True)
            if key not in seen:
                seen.add(key)
                records.append(entry)
    definitions = [f for f in assets.get('fsms', []) if f.get('path') == STEAM and f.get('fsmName') == 'Steam']
    if len(definitions) != 1:
        raise ValueError('exactly one audited nested Steam definition required')
    steam = definitions[0]
    variables = steam.get('variables', {})
    missing = []
    for fsm in records:
        absent = [s['name'] for s in fsm.get('states', []) if 'actions' not in s]
        if absent:
            missing.append({'path': fsm['path'], 'fsmName': fsm['fsmName'], 'states_without_operands': absent})
    return {
        'evidence_level': 'historical static extract + current source discovery; NOT native runtime',
        'transfer_readiness': 'NOT_ESTABLISHED',
        'nested_path': STEAM,
        'steam_water_type': ('bool' if 'Water' in variables.get('BoolVariables', []) else 'absent'),
        'steam_has_numeric_water': any('Water' in variables.get(k, []) for k in ('FloatVariables', 'IntVariables')),
        'steam_definition': compact(steam),
        'related_catalog_records': records,
        'missing_catalog_action_operands': missing,
        'limits': [
            'No authenticated actor -> held dipper -> finite native debit -> nested contact chain established.',
            'Action names, default values or Steam.Water bool cannot establish finite amount, capacity, or resource ownership.',
            'No transfer intent/result, ordinary input, native persistence, Steam or soak acceptance claimed.'
        ]
    }


def audit(source, catalog_file, asset_file):
    report = analyze(json.loads(catalog_file.read_text()), json.loads(asset_file.read_text()))
    paths = [catalog_file, asset_file]
    names = (
        'src/WinterMP.Core/Sync/HeatSourceSync.cs',
        'src/WinterMP.Core/Sync/HeatSourceSync.Sauna.cs',
        'src/WinterMP.Core/Sync/WorldSyncManager.Handlers.cs',
        'src/WinterMP.Core/Session/SessionManager.Messages.cs',
        'src/WinterMP.Net/Messages/HeatingMessages.cs',
        'src/WinterMP.Net/Messages/SaunaTimerMessages.cs',
        'src/WinterMP.Net/Messages/MessageRegistry.cs',
        'src/WinterMP.Net/SessionMessagePolicy.cs',
        'src/WinterMP.Net/Sync/SaunaTimerAuthority.cs',
        'src/WinterMP.Core/Sync/FluidContainerSync.Transfer.cs',
        'src/WinterMP.Net/Messages/ContainerFuelMessages.cs',
        'protocol/PROTOCOL.md',
        'catalog/sync-catalog.json',
    )
    paths.extend(source / name for name in names)
    report['input_hashes'] = {str(p.resolve()): sha256(p) for p in paths}
    report['source_links'] = {}
    needles = ('StoveTrigger', 'SaunaTimer', 'HeatSourceIntent', 'HeatSourceState', 'ActionSaunaThrow',
               'ContainerFuel', 'GasolinePath', 'SorbetPath', 'IsFiniteSource')
    for name in names:
        lines = (source / name).read_text().splitlines()
        report['source_links'][name] = [{'line': i, 'text': line.strip()} for i, line in enumerate(lines, 1)
                                      if any(n in line for n in needles)]
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--catalog', type=Path, required=True)
    parser.add_argument('--assets', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    report = audit(args.source, args.catalog, args.assets)
    with args.out.open('x') as output:
        json.dump(report, output, indent=2)
        output.write('\n')
    print(json.dumps({k: report[k] for k in ('evidence_level', 'transfer_readiness', 'nested_path',
                                           'steam_water_type', 'steam_has_numeric_water')}, indent=2))
    print('Related records:', len(report['related_catalog_records']))
    print('Records missing action operands:', len(report['missing_catalog_action_operands']))
    for fsm in report['related_catalog_records']:
        print(fsm['path'] + '::' + fsm['fsmName'])


if __name__ == '__main__':
    main()
