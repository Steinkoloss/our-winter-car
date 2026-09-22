"""Print the relevant static native stain/PISS action signatures (not live evidence)."""
import json
import sys
from pathlib import Path

data = json.loads(Path(sys.argv[1]).read_text())
for fsm in data['fsms']:
    print('\nFSM', fsm['path'], fsm['fsmName'], 'globals', fsm['globalTransitions'])
    print('DEFAULTS', json.dumps(fsm['variableDefaults']))
    for state in fsm['states']:
        print('STATE', state['name'], state['transitions'])
        for action in state['actions']:
            print(' ', action['type'], json.dumps(action['parameters']))
for pose in data.get('transforms', []):
    print('TRANSFORM', json.dumps(pose))
