"""Exercise every ZBrush-style engine operation with the installed Blender.

python tests/zbrush_engine_smoke.py [absolute/path/to/blender.exe]
"""
from pathlib import Path
import os
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
BLENDER = Path(sys.argv[1] if len(sys.argv) > 1 else r'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe')
OUTPUT = ROOT / 'artifacts' / ('zbrush-engine-smoke-' + time.strftime('%Y%m%d-%H%M%S'))
OUTPUT.mkdir(parents=True, exist_ok=True)
PROBE = ROOT / 'artifacts' / 'zbrush_engine_probe.py'

PROBE.write_text(r'''
import bpy
import sys
from pathlib import Path

sys.path.insert(0, str(Path(r"""__ENGINE__""")))
import thai_model
import zbrush_engine as zb
import mesh_mask

thai_model.clear()


def cube(name, size=2.0, location=(0, 0, 0)):
    obj = thai_model.cube(name, size=(size, size, size), location=location)
    return obj


def counts(name):
    return len(bpy.data.objects[name].data.vertices)


# 1. Insert Mesh: a primitive stamped into the SubTool
target = cube('insert_target')
before = len(target.data.vertices)
zb.insert_mesh('insert_target', 'ICOSPHERE', location=(1.6, 0, 0), size=0.8)
assert len(bpy.data.objects['insert_target'].data.vertices) > before, 'insert_mesh did not join geometry'
print('insert_mesh ok')

# 2. Clip Curve: cut the top off and cap it
target = cube('clip_target', size=2.0, location=(0, 0, 2))
zb.clip_plane('clip_target', axis='Z', position=2.4, keep='BELOW')
heights = [(target.matrix_world @ v.co).z for v in target.data.vertices]
assert max(heights) <= 2.45, 'clip_plane kept geometry above the plane: %r' % max(heights)
assert len(target.data.polygons) > 0, 'clip_plane removed every face'
print('clip_plane ok')

# 3. Transpose: proportional move around a pivot
target = cube('transpose_target')
zb.transpose('transpose_target', pivot=(1, 1, 1), move=(0, 0, 1.5), radius=0.6)
moved = max(v.co.z for v in target.data.vertices)
assert moved > 2.0, 'transpose did not move the pivot vertex: %r' % moved
neighbours = [v for v in target.data.vertices if v.co.z < 1.2]
assert neighbours, 'transpose ignored the falloff radius'
print('transpose ok')

# 4. Project All: snap a grid onto a sphere
grid = thai_model.plane('project_target', size=2.0, location=(0, 0, 0))
thai_model.sphere('project_source', radius=1.0, location=(0, 0, 0))
zb.project_all('project_target', 'project_source', offset=0.0)
snapped = [v.co.length for v in grid.data.vertices]
assert max(snapped) > 0.9 and max(snapped) < 1.2, 'project_all did not snap to the sphere: %r' % max(snapped)
print('project_all ok')

# 5. Surface Noise
target = thai_model.sphere('noise_target', radius=1.0, location=(3, 0, 0), segments=24, rings=12)
before = [v.co.copy() for v in target.data.vertices]
zb.surface_noise('noise_target', strength=0.12, size=0.4, detail=2)
changed = sum(1 for old, v in zip(before, target.data.vertices) if (old - v.co).length > 1e-4)
assert changed > len(before) // 2, 'surface_noise barely changed the surface: %d' % changed
print('surface_noise ok')

# 6. Sculpt Layer with mask support
target = cube('layer_target')
mesh_mask.mask_set('layer_target', value=1.0, axis='Z', minimum=-100.0)
zb.sculpt_layer('layer_target', name='Muscle', axis='Z', amount=0.3)
keys = bpy.data.objects['layer_target'].data.shape_keys
assert keys is not None and 'Muscle' in keys.key_blocks, 'sculpt_layer did not create a shape key'
layer = keys.key_blocks['Muscle']
unmoved = sum(1 for i, v in enumerate(bpy.data.objects['layer_target'].data.vertices)
              if (layer.data[i].co - v.co).length < 1e-5)
assert unmoved == len(bpy.data.objects['layer_target'].data.vertices), 'masked vertices moved in the layer'
print('sculpt_layer ok')

# 7. ZSpheres base mesh
zsphere = zb.zsphere('ZSphereBase', points=((0, 0, 0), (0, 0, 1.1), (0.4, 0, 2.0)), radii=(0.5, 0.4, 0.25))
assert zsphere.type == 'MESH' and len(zsphere.data.vertices) > 100, 'zsphere conversion produced no mesh'
print('zsphere ok')

# 8. FiberMesh strands
fur_source = thai_model.sphere('fur_source', radius=1.0, location=(0, 5, 0), segments=16, rings=8)
fur = zb.fibermesh('fur_source', count=40, length=0.4, radius=0.01, seed=3)
assert fur.type == 'CURVE' and len(fur.data.splines) == 40, 'fibermesh did not create strands'
print('fibermesh ok')

# 9. NanoMesh scatter
nano_target = cube('nano_target', size=3.0, location=(4, 5, 0))
before = len(nano_target.data.vertices)
zb.nanomesh('nano_target', count=25, scale=0.1, seed=4)
assert len(bpy.data.objects['nano_target'].data.vertices) > before * 2, 'nanomesh added no geometry'
print('nanomesh ok')

# 10. ArrayMesh
array_target = cube('array_target', size=0.5, location=(0, -3, 0))
before = len(array_target.data.vertices)
zb.array_mesh('array_target', count=4, offset=(1.0, 0, 0))
assert len(bpy.data.objects['array_target'].data.vertices) == before * 4, 'array_mesh did not repeat the mesh'
print('array_mesh ok')

# 11. PolyPaint vertex colours
paint_target = cube('paint_target', location=(3, -3, 0))
zb.polypaint('paint_target', '#FF7A29')
attribute = bpy.data.objects['paint_target'].data.color_attributes.get(zb.PAINT_ATTRIBUTE)
assert attribute is not None and attribute.data[0].color[0] > 0.5, 'polypaint wrote no colour'
print('polypaint ok')

# 12. SubTool Boolean
base = cube('bool_base', size=2.0, location=(6, -3, 0))
tool = thai_model.sphere('bool_tool', radius=0.8, location=(6.8, -3, 0))
before = len(base.data.vertices)
zb.subtool_boolean('bool_base', 'bool_tool', 'DIFFERENCE')
assert bpy.data.objects.get('bool_tool') is None, 'subtool_boolean left the tool behind'
assert len(bpy.data.objects['bool_base'].data.vertices) != before, 'subtool_boolean changed nothing'
print('subtool_boolean ok')

# 13. GoZ-style import of an OBJ written by Blender
exchange = cube('exchange_source', size=1.0, location=(0, 0, -5))
destination = Path(r"""__OBJ__""")
bpy.ops.object.select_all(action='DESELECT')
exchange.select_set(True)
bpy.context.view_layer.objects.active = exchange
bpy.ops.wm.obj_export(filepath=str(destination), export_selected_objects=True)
imported = zb.import_mesh(destination, name='GoZ_Import')
assert imported.type == 'MESH' and len(imported.data.vertices) == 8, 'import_mesh did not load the OBJ'
print('import_mesh ok')

print('ZBRUSH_ENGINE_SUCCESS')
'''.replace('__ENGINE__', str(ROOT / 'engine')).replace('__OBJ__', str(OUTPUT / 'exchange.obj')), encoding='utf-8')

environment = dict(os.environ, PYTHONIOENCODING='utf-8', PYTHONUTF8='1')
process = subprocess.run([str(BLENDER), '--background', '--factory-startup', '--python-exit-code', '1',
                          '--python', str(PROBE)], capture_output=True, encoding='utf-8', errors='replace',
                         env=environment, timeout=180)
(OUTPUT / 'probe.log').write_text(process.stdout + process.stderr, encoding='utf-8')
print(process.stdout[-4000:])
print(process.stderr[-2000:])
if process.returncode != 0 or 'ZBRUSH_ENGINE_SUCCESS' not in process.stdout:
    raise SystemExit('zbrush engine smoke failed; see ' + str(OUTPUT / 'probe.log'))
print('ZBRUSH_ENGINE_SMOKE_PASS')
