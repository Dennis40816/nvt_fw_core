"""List resource keys that are referenced but not defined in a folder of .axaml files."""
import os
import re
import sys

root = sys.argv[1]
defs, refs = set(), {}
for base, dirs, names in os.walk(root):
    dirs[:] = [d for d in dirs if d not in ('bin', 'obj')]
    for name in names:
        if not name.endswith('.axaml'):
            continue
        text = open(os.path.join(base, name), encoding='utf-8').read()
        defs |= set(re.findall(r'x:Key="([^"]+)"', text))
        for key in re.findall(r'\{(?:StaticResource|DynamicResource)\s+([^}\s]+)\s*\}', text):
            refs.setdefault(key, set()).add(name)
        for key in re.findall(r'ResourceKey="([^"]+)"', text):
            refs.setdefault(key, set()).add(name)
undefined = sorted(key for key in refs if key not in defs)
print('defined keys:', len(defs), 'referenced keys:', len(refs), 'undefined:', len(undefined))
for key in undefined[:60]:
    print('  undefined', key, sorted(refs[key])[:2])
