"""End-to-end tests using the installed Blender, without opening a UI.

python tests/backend_smoke.py [absolute/path/to/blender.exe]
"""
from pathlib import Path
import json
import os
import struct
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
BLENDER = Path(sys.argv[1] if len(sys.argv) > 1 else r'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe')
OUTPUT = ROOT / 'artifacts' / ('backend-smoke-' + time.strftime('%Y%m%d-%H%M%S'))
OUTPUT.mkdir(parents=True)
ENV = dict(os.environ, PYTHONIOENCODING='utf-8', PYTHONUTF8='1')
results = []


def run(label, args, success=True):
    p = subprocess.run([str(BLENDER), *args], capture_output=True, encoding='utf-8', errors='replace', env=ENV, timeout=90)
    (OUTPUT / (label + '.log')).write_text(p.stdout + p.stderr, encoding='utf-8')
    assert (p.returncode == 0) == success, label + ': wrong exit code\n' + p.stdout + p.stderr
    return p


def build(label, source, success=True):
    directory = OUTPUT / label
    run(label, ['--background', '--factory-startup', '--python-exit-code', '1', '--python', str(ROOT / 'engine' / 'runner.py'), '--', '--source', str(source), '--output', str(directory)], success)
    assert (directory / 'build-success.json').exists() == success
    if success:
        for filename in ('scene.blend', 'scene.glb', 'preview.json'):
            assert (directory / filename).stat().st_size > 0
        glb = (directory / 'scene.glb').read_bytes()
        assert glb[:4] == b'glTF' and struct.unpack_from('<I', glb, 4)[0] == 2
        preview = json.loads((directory / 'preview.json').read_text(encoding='utf-8'))
        assert preview['objects']
        for obj in preview['objects']:
            assert len(obj['vertices']) > 0
            assert len(obj['triangleColors']) == len(obj['triangles'])
            for tri in obj['triangles']:
                assert len(tri) == 3 and min(tri) >= 0 and max(tri) < len(obj['vertices'])
        return directory, preview
    assert not (directory / 'scene.blend').exists()
    return directory, None


for filename in ('01_house.py', '02_robot.py', '03_city.py', '04_bpy.py'):
    directory, preview = build(Path(filename).stem, ROOT / 'examples' / filename)
    results.append({'case': filename, 'objects': len(preview['objects']), 'triangles': preview['sourceTriangleCount'], 'passed': True})

house = OUTPUT / '01_house' / 'scene.blend'
for kind in ('obj', 'stl', 'glb', 'blend'):
    destination = OUTPUT / ('ส่งออกทดสอบ.' + kind)
    run('export-' + kind, ['--background', str(house), '--python-exit-code', '1', '--python', str(ROOT / 'engine' / 'runner.py'), '--', '--export', str(destination), '--format', kind])
    assert destination.stat().st_size > 0
    if kind == 'obj':
        text = destination.read_text(encoding='utf-8')
        assert '\nv ' in text and '\nf ' in text
    if kind == 'stl':
        data = destination.read_bytes()
        assert len(data) == 84 + 50 * struct.unpack_from('<I', data, 80)[0]
    results.append({'case': 'export-' + kind, 'bytes': destination.stat().st_size, 'passed': True})

bad = OUTPUT / 'invalid.py'
bad.write_text('กล่อง("ก่อนข้อผิดพลาด")\nraise RuntimeError("ทดสอบข้อผิดพลาดภาษาไทย")\n', encoding='utf-8')
build('error-case', bad, success=False)
results.append({'case': 'error-exit-and-no-success-artifacts', 'passed': True})

dense = OUTPUT / 'dense.py'
dense.write_text('ทรงกลม("ละเอียด", segments=256, rings=128)\n', encoding='utf-8')
_, preview = build('dense', dense)
assert preview['sourceTriangleCount'] > 60000
count = sum(len(obj['triangles']) for obj in preview['objects'])
assert preview['simplified'] and count < preview['sourceTriangleCount'] and count <= 61000
results.append({'case': 'dense-simplification', 'sourceTriangles': preview['sourceTriangleCount'], 'previewTriangles': count, 'passed': True})

helpers = OUTPUT / 'helpers.py'
helpers.write_text('''
สี = วัสดุ("สีทดสอบ", color=(0.2, 0.5, 0.8))
a = กล่อง("กล่อง", material=สี)
b = ทำซ้ำ(a, name="สำเนา", location=(3, 0, 0))
ย้าย(a, (1, 2, 3))
หมุน(a, (0, 0, 90))
ปรับขนาด(a, 2)
ขอบมน(a)
ผิวเรียบ(a)
กรวย("กรวย", location=(0, 3, 0))
ทรงกระบอก("กระบอก", location=(3, 3, 0))
วงแหวน("วงแหวน", location=(5, 0, 0))
พื้น("พื้น", location=(0, 0, -2))
assert tuple(a.location) == (1, 2, 3)
assert tuple(a.scale) == (2, 2, 2)
''', encoding='utf-8')
_, preview = build('helpers', helpers)
assert len(preview['objects']) == 6
results.append({'case': 'thai-helpers', 'passed': True})

(OUTPUT / 'results.json').write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding='utf-8')
print(json.dumps({'passed': len(results), 'output': str(OUTPUT), 'results': results}, ensure_ascii=False, indent=2))
