"""Check Thai labels, deterministic aliases and source-preserving API metadata."""
from pathlib import Path
import ast
import json
import re
import unicodedata

root = Path(__file__).resolve().parents[1]
raw = json.loads((root / 'docs/rea/blender-api-catalog.json').read_text(encoding='utf-8'))
localized = json.loads((root / 'engine/blender_api.json').read_text(encoding='utf-8'))
assert {e['id'] for e in raw['entries']} == {e['id'] for e in localized['entries']}
assert raw['blender'] == localized['blender']
assert len(localized['entries']) == localized['localization']['entriesWithCompleteThaiName']
def key(value):
    return re.sub(r'[\s_\-./]+', '', unicodedata.normalize('NFKC', value).casefold())
def unique(items):
    aliases = [key(p['thaiIdentifier']) for p in items]
    assert len(set(aliases)) == len(aliases), aliases
paths = set()
all_paths = {key(e['thaiPath']) for e in localized['entries']}
assert len(all_paths) == len(localized['entries']), 'Full Thai API paths collide'
for entry in localized['entries']:
    assert any('\u0e00' <= c <= '\u0e7f' for c in entry['thaiName']), entry['id']
    assert entry['nameTranslationSource'] in {'official', 'manual', 'glossary'}
    unique(entry.get('parameters', [])); unique(entry.get('enumItems', []))
    for parameter in entry.get('parameters', []): unique(parameter.get('enumItems', []))
    if entry['kind'] == 'operator':
        alias = key(entry['thaiPath']); assert alias not in paths, entry['id']; paths.add(alias)
        parameters = [p for p in entry.get('parameters', []) if p.get('required') and not p.get('readOnly')]
        call = 'คำสั่ง(' + repr(entry['thaiPath']) + ''.join(', ' + p['thaiIdentifier'] + '=None' for p in parameters) + ')'
        ast.parse(call)
cube = next(e for e in localized['entries'] if e['id'] == 'bpy.ops.mesh.primitive_cube_add')
assert next(p for p in cube['parameters'] if p['id'] == 'calc_uvs')['thaiName'] == 'สร้างพิกัดยูวี'
print('PASS: %d Thai entry labels, %d unique operator aliases, parameter/enum disambiguation, source IDs preserved, generated calls parse.' % (len(localized['entries']),len(paths)))
