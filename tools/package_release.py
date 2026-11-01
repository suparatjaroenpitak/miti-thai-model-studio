"""Package the verified portable app without user builds or developer tooling."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[1]
folder = root / 'dist' / 'MitiStudio-0.2'
for name in ['MitiStudio.exe', 'MitiStudio.dll', 'MitiStudio.runtimeconfig.json', 'engine/blender_api.json', 'engine/thai_api.py']:
    assert (folder / name).is_file(), name
shutil.copy2(root / 'README.md', folder / 'README.md')
for name in ['API-ภาษาไทย.md', 'Blender-REA.md', 'คำสั่งภาษาไทย.md', 'thai-api-runtime.md']:
    shutil.copy2(root / 'docs' / name, folder / 'docs' / name)
catalog = json.loads((folder / 'engine/blender_api.json').read_text(encoding='utf-8'))
assert catalog['localization']['entriesWithCompleteThaiName'] == len(catalog['entries'])
assert hashlib.sha256((folder / 'engine/blender_api.json').read_bytes()).digest() == hashlib.sha256((root / 'engine/blender_api.json').read_bytes()).digest()
for log, marker in [('generated-api-build.log', 'GENERATED_THAI_API_PASS'), ('thai-api-final.log', 'THAI_API_SMOKE_PASS'), ('window-context-smoke.log', 'WINDOW_CONTEXT_SMOKE_PASS'), ('api-inventory-validation.log', '"status": "passed"')]:
    assert marker in (root / 'artifacts' / log).read_text(encoding='utf-8-sig'), (log, marker)
report = {
    'appVersion': '0.2.0', 'blender': catalog['blender'], 'counts': catalog['counts'],
    'thaiEntryLabels': catalog['localization']['entriesWithCompleteThaiName'],
    'checks': ['Registered API inventory compared with installed Blender', 'Thai names and aliases; Python syntax for generated operator calls',
               'WinForms catalog, parameter selection and five kinds of generated source', 'Generated source executed in Blender; saved BLEND and GLB',
               'Thai enum/property/RNA/bmesh/mathutils bindings', 'Real window VIEW_3D context'],
    'scope': catalog['coverage']['boundary'], 'nativeDecompilation': False,
    'translationNote': 'Official translations, curated phrases and glossary-composed labels; descriptions may be generated summaries. Not every API behavior was executed.'
}
(folder / 'VERIFICATION.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
archive = root / 'dist' / 'MitiStudio-0.2-Windows-x64.zip'
with ZipFile(archive, 'w', ZIP_DEFLATED) as package:
    for file in sorted(folder.rglob('*')):
        relative = file.relative_to(folder)
        if file.is_file() and not {'UserData', '__pycache__'}.intersection(relative.parts):
            package.write(file, 'MitiStudio-0.2/' + relative.as_posix())
with ZipFile(archive) as package:
    assert package.testzip() is None
    names = package.namelist()
    assert not any('/UserData/' in name or '/__pycache__/' in name for name in names)
print(json.dumps({'zip': str(archive), 'bytes': archive.stat().st_size, 'files': len(names), 'thaiEntryLabels': report['thaiEntryLabels'], 'integrity': 'passed'}))
