"""Validate catalog coverage against the actual registered Blender RNA surface.

blender --background --factory-startup --python tests/api_inventory_smoke.py -- --catalog docs/rea/blender-api-catalog.json
"""
import argparse
from collections import Counter
import json
import inspect
from pathlib import Path
import sys
import bpy

parser = argparse.ArgumentParser()
parser.add_argument('--catalog', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
catalog = json.loads(Path(args.catalog).read_text(encoding='utf-8'))
entries = catalog['entries']
assert len({entry['id'] for entry in entries}) == len(entries), 'Duplicate catalog keys'
assert catalog['blender']['version'] == list(bpy.app.version), 'Catalog Blender version differs'
assert catalog['blender']['buildHash'] == bpy.app.build_hash.decode(), 'Catalog build differs'
assert catalog['coverage']['errors'] == [], 'Extraction had unresolved errors'
assert catalog['coverage']['executedOperators'] == 0

actual_ops = {'bpy.ops.' + ns + '.' + name
              for ns in dir(bpy.ops) if not ns.startswith('_')
              for name in dir(getattr(bpy.ops, ns)) if not name.startswith('_')}
catalog_ops = {entry['id'] for entry in entries if entry['kind'] == 'operator'}
assert actual_ops == catalog_ops, {'missingOperators': sorted(actual_ops - catalog_ops), 'extraOperators': sorted(catalog_ops - actual_ops)}

actual_types = set()
actual_properties = set()
actual_functions = set()
for name in dir(bpy.types):
    if name.startswith('_'):
        continue
    rna = getattr(getattr(bpy.types, name), 'bl_rna', None)
    if rna is None:
        continue
    owner = 'bpy.types.' + name
    actual_types.add(owner)
    base = rna.base
    inherited_props = {prop.identifier for prop in base.properties} if base else set()
    inherited_functions = {fn.identifier for fn in base.functions} if base else set()
    actual_properties.update(owner + '.' + prop.identifier for prop in rna.properties
                             if prop.identifier != 'rna_type' and prop.identifier not in inherited_props)
    actual_functions.update(owner + '.' + fn.identifier for fn in rna.functions if fn.identifier not in inherited_functions)
assert actual_types == {e['id'] for e in entries if e['kind'] == 'type'}, 'RNA type coverage differs'
assert actual_properties == {e.get('apiPath', e['id']) for e in entries if e['kind'] == 'property'}, 'RNA property coverage differs'
assert actual_functions == {e.get('apiPath', e['id']) for e in entries if e['kind'] == 'function' and e.get('sourceKind') != 'public-non-RNA-type-member'}, 'RNA function coverage differs'

actual_extra_functions = set()
for name in dir(bpy.types):
    if name.startswith('_'):
        continue
    cls = getattr(bpy.types, name)
    if not isinstance(cls, type):
        continue
    for member in vars(cls):
        value = getattr(cls, member)
        if member.startswith('_') or not (inspect.isroutine(value) or inspect.ismethoddescriptor(value)):
            continue
        identity = 'bpy.types.' + name + '.' + member
        if identity not in actual_functions:
            actual_extra_functions.add(identity)
assert actual_extra_functions == {e.get('apiPath', e['id']) for e in entries if e.get('sourceKind') == 'public-non-RNA-type-member'}, 'Additional public type function coverage differs'

for entry in entries:
    for field in ('thaiName', 'thaiDescription'):
        translated = entry.get(field)
        if translated:
            assert any('\u0e00' <= char <= '\u0e7f' for char in translated), entry['id']
    if entry['kind'] == 'operator':
        assert 'pollInFactoryContext' in entry
        assert isinstance(entry['parameters'], list)
        assert len({p['id'] for p in entry['parameters']}) == len(entry['parameters'])
    for parameter in entry.get('parameters', []):
        assert {'id', 'type', 'required', 'readOnly', 'arrayLength'} <= parameter.keys()

expected = {'bmesh.ops.create_cube', 'bmesh.types.BMesh.copy', 'mathutils.Vector.cross',
            'mathutils.Matrix.inverted', 'gpu.types.GPUShader.bind', 'bpy.types.BlendDataObjects.new',
            'bpy.types.Mesh.from_pydata', 'bpy.context.temp_override'}
assert expected <= {entry['id'] for entry in entries}, sorted(expected - {entry['id'] for entry in entries})
print(json.dumps({'status': 'passed', 'counts': dict(Counter(e['kind'] for e in entries)),
                  'checks': ['Unique keys and installed build identity', 'All registered bpy.ops identities',
                             'Every bpy.types RNA class and defining property/function',
                             'Operator parameter schema', 'Official Thai translation provenance',
                             'Representative bmesh/mathutils/GPU/data bindings']}, ensure_ascii=False, indent=2))
