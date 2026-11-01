"""Localize a Blender RNA inventory without changing its canonical API IDs.

The shipped Thai gettext catalogue is authoritative. Curated translations and
word-composed fallback labels are labelled separately, and missing words remain
visible. Generated descriptions are summaries, never claimed as translations.
"""
from collections import Counter
from copy import deepcopy
import argparse
import gettext
import hashlib
import json
import os
from pathlib import Path
import re
import unicodedata


ROOT = Path(__file__).resolve().parent
THAI = re.compile(r"[\u0e00-\u0e7f]")
WORD = re.compile(r"[A-Za-zÀ-ÖØ-öø-ÿ]+(?:'[A-Za-zÀ-ÖØ-öø-ÿ]+)?|\d+(?:\.\d+)?|[^A-Za-zÀ-ÖØ-öø-ÿ\d\s]+")


def identifier(text):
    """Keep Thai combining marks, which Python's re \\w does not retain."""
    result = ''.join(c if unicodedata.category(c)[0] in 'LMN' or c == '_' else '_' for c in text)
    result = re.sub('_+', '_', result).strip('_') or 'คำสั่ง'
    if not result.isidentifier():
        result = 'ค่า_' + result
    return result


def _humanize(text):
    text = str(text).replace('_', ' ')
    text = re.sub(r'(?<=[a-z])(?=[A-Z])', ' ', text)
    text = re.sub(r'(?<=[A-Z])(?=[A-Z][a-z])(?![A-Z]s\b)', ' ', text)
    return re.sub(r'\s+', ' ', text).strip()


def _normal(text):
    return re.sub(r'\s+', ' ', text).strip().casefold()


def _alias_key(text):
    return re.sub(r'[\s_\-./]+', '', unicodedata.normalize('NFKC', text).casefold())


class Localizer:
    def __init__(self, glossary_path=None, mo_path=None, binary_path=None):
        self.glossary = json.loads(Path(glossary_path or ROOT / 'thai_glossary.json').read_text(encoding='utf-8'))
        self.extra_path = ROOT / 'thai_glossary_extra.json'
        self.overlay_paths = (self.extra_path, ROOT / 'thai_glossary_completion.json', ROOT / 'thai_glossary_terms.json', ROOT / 'thai_glossary_labels.json')
        for overlay in self.overlay_paths:
            if overlay.is_file():
                extra = json.loads(overlay.read_text(encoding='utf-8'))
                for group in ('words', 'phrases', 'categories'):
                    self.glossary.setdefault(group, {}).update(extra.get(group, {}))
        self.phrases = {_normal(k): v for k, v in self.glossary['phrases'].items()}
        self.words = {_normal(k): v for k, v in self.glossary['words'].items()}
        self.categories = self.glossary['categories']
        self.technical = {x.casefold() for x in self.glossary.get('preservedTechnicalTerms', [])}
        self.official = gettext.NullTranslations()
        self.mo_path = None
        candidates = []
        if mo_path:
            candidates.append(Path(mo_path))
        if binary_path:
            candidates.extend(Path(binary_path).parent.glob('*/datafiles/locale/th/LC_MESSAGES/blender.mo'))
        try:
            import bpy
            candidates.extend(Path(bpy.app.binary_path).parent.glob('*/datafiles/locale/th/LC_MESSAGES/blender.mo'))
        except ImportError:
            pass
        for base in (os.environ.get('ProgramFiles'), os.environ.get('ProgramFiles(x86)')):
            if base:
                candidates.extend((Path(base) / 'Blender Foundation').glob('Blender */*/datafiles/locale/th/LC_MESSAGES/blender.mo'))
        for path in candidates:
            if path.is_file():
                with path.open('rb') as file:
                    self.official = gettext.GNUTranslations(file)
                self.mo_path = path
                break
        self.sources = Counter()
        self.entry_sources = Counter()
        self.missing = Counter()
        self.untranslated_entries = []

    def official_text(self, text, context='*'):
        if not text:
            return None
        for translated in (self.official.pgettext(context or '*', text), self.official.gettext(text),
                           self.official.pgettext('*', text)):
            if translated != text and THAI.search(translated):
                return translated
        return None

    def label(self, text, context='*', existing=None):
        if existing and THAI.search(existing):
            return existing, 'official', []
        original = text or ''
        official = self.official_text(original, context)
        if official:
            return official, 'official', []
        human = _humanize(original)
        phrase = self.phrases.get(_normal(original)) or self.phrases.get(_normal(human))
        if phrase:
            return phrase, 'manual', []
        if THAI.search(human) and not re.search('[A-Za-z]', human):
            return human, 'manual', []
        atoms = WORD.findall(human)
        parts, missing = [], []
        i = 0
        while i < len(atoms):
            matched = False
            for span in range(min(7, len(atoms) - i), 1, -1):
                candidate = _normal(' '.join(atoms[i:i + span]))
                if candidate in self.phrases:
                    parts.append(self.phrases[candidate]); i += span; matched = True; break
            if matched:
                continue
            word = atoms[i]
            normalized = word.casefold()
            translated = self.words[normalized] if normalized in self.words else self.official_text(word)
            if translated is not None:
                parts.append(translated)
            elif normalized in self.technical or re.fullmatch(r'[A-Za-z]|\d+(?:\.\d+)?', word) or not re.search('[A-Za-zÀ-ÖØ-öø-ÿ]', word):
                parts.append(word)
            else:
                # Preserve unfamiliar technical words. A Thai prefix is not a translation.
                parts.append(word); missing.append(word)
            i += 1
        if not parts:
            parts = ['ไม่มีชื่อ']
        result = re.sub(r'\s+', ' ', ' '.join(parts)).strip()
        translated_any = bool(THAI.search(result))
        if not translated_any:
            return original or 'ไม่มีชื่อ', 'untranslated', missing
        return result, 'glossary', missing

    def named(self, node, kind='parameter', entry=False):
        text = node.get('name') or _humanize(node.get('id', '').rsplit('.', 1)[-1])
        context = node.get('translationContext') or '*'
        label, source, missing = self.label(text, context, node.get('thaiName'))
        if missing:
            original_identifier = (node.get('apiPath') or node.get('id', '')).rsplit('.', 1)[-1]
            alternate = self.label(_humanize(original_identifier), context)
            if len(alternate[2]) < len(missing) and alternate[1] != 'untranslated':
                label, source, missing = alternate
        node['thaiName'] = label
        node['thaiIdentifier'] = identifier(label)
        node['nameTranslationSource'] = source
        node['nameTranslationComplete'] = not missing and source != 'untranslated'
        if missing:
            node['untranslatedWords'] = sorted(set(missing), key=str.casefold)
            self.missing.update(missing)
        self.sources[source] += 1
        if entry:
            self.entry_sources[source] += 1
            if not node['nameTranslationComplete']:
                self.untranslated_entries.append({'id': node['id'], 'name': text, 'thaiName': label, 'missing': missing})
        description = node.get('thaiDescription')
        if description and THAI.search(description):
            node['descriptionTranslationSource'] = 'official'
        else:
            description = self.official_text(node.get('description'), context)
            if description:
                node['descriptionTranslationSource'] = 'official'
            else:
                types = {'BOOLEAN': 'ค่าจริงหรือเท็จ', 'INT': 'จำนวนเต็ม', 'FLOAT': 'จำนวนทศนิยม',
                         'STRING': 'ข้อความ', 'ENUM': 'ตัวเลือก', 'POINTER': 'ข้อมูลอ้างอิง', 'COLLECTION': 'ชุดข้อมูล'}
                detail = types.get(node.get('type'), node.get('type', ''))
                if kind == 'operator':
                    description = 'เรียกคำสั่ง ' + label + ' ผ่าน Blender โดยใช้พารามิเตอร์ด้านล่าง'
                elif kind in ('function', 'moduleFunction'):
                    description = 'เรียกฟังก์ชัน ' + label + ' ของ Blender'
                elif kind == 'property':
                    description = 'คุณสมบัติ ' + label + (' ชนิด' + detail if detail else '')
                elif kind == 'type':
                    description = 'ชนิดข้อมูล ' + label + ' ของ Blender'
                elif kind == 'enumItem':
                    description = 'ตัวเลือก ' + label
                else:
                    description = 'กำหนดค่า ' + label + (' เป็น' + detail if detail else '')
                    if node.get('required'):
                        description += ' (จำเป็นต้องระบุ)'
                node['descriptionTranslationSource'] = 'generated-summary'
        node['thaiDescription'] = description
        for parameter in node.get('parameters', []):
            self.named(parameter, 'parameter')
        self._unique_identifiers(node.get('parameters', []))
        for item in node.get('enumItems', []):
            self.named(item, 'enumItem')
        self._unique_identifiers(node.get('enumItems', []))

    @staticmethod
    def _unique_identifiers(nodes):
        used = set()
        for node in nodes:
            base = node.get('thaiIdentifier') or identifier(node.get('thaiName', 'ค่า'))
            key, number = base, 2
            while _alias_key(key) in used:
                key = base + '_' + str(number); number += 1
            node['thaiIdentifier'] = key
            used.add(_alias_key(key))

    def localize(self, catalog):
        result = deepcopy(catalog)
        paths = set()
        owner_members = {}
        for entry in result.get('entries', []):
            self.named(entry, entry.get('kind', ''), True)
            category = entry.get('category') or ''
            category_thai = self.categories.get(category) or self.label(category)[0]
            entry['thaiCategory'] = category_thai
            if entry.get('kind') == 'operator':
                base = identifier(category_thai) + '.' + entry['thaiIdentifier']
                path, number = base, 2
                while _alias_key(path) in paths:
                    path = base + '_' + str(number); number += 1
                paths.add(_alias_key(path))
                entry['thaiPath'] = path
            elif entry.get('kind') in ('function', 'property'):
                owner_members.setdefault(entry.get('ownerType') or entry.get('category'), []).append(entry)
        for members in owner_members.values():
            self._unique_identifiers(members)
        types = [entry for entry in result.get('entries', []) if entry.get('kind') == 'type']
        self._unique_identifiers(types)
        type_names = {entry['id']: entry['thaiIdentifier'] for entry in types}
        module_names = {
            'aud': 'เสียง', 'blf': 'ตัวอักษร', 'bmesh': 'บีเมช', 'bmesh.ops': 'บีเมช.คำสั่ง',
            'bmesh.types': 'บีเมช.ชนิดข้อมูล', 'bmesh.geometry': 'บีเมช.เรขาคณิต', 'bmesh.utils': 'บีเมช.เครื่องมือ',
            'mathutils': 'คณิตสามมิติ', 'mathutils.bvhtree': 'คณิตสามมิติ.ต้นไม้บีวีเอช',
            'mathutils.kdtree': 'คณิตสามมิติ.ต้นไม้เคดี', 'mathutils.geometry': 'คณิตสามมิติ.เรขาคณิต',
            'mathutils.interpolate': 'คณิตสามมิติ.ประมาณค่า', 'mathutils.noise': 'คณิตสามมิติ.นอยส์',
            'bpy': 'เบลนเดอร์', 'bpy.context': 'เบลนเดอร์.บริบท', 'bpy.data': 'เบลนเดอร์.ข้อมูล',
            'bpy.app': 'เบลนเดอร์.แอป', 'bpy.app.handlers': 'เบลนเดอร์.แอป.เหตุการณ์',
            'bpy.app.timers': 'เบลนเดอร์.แอป.ตัวจับเวลา', 'bpy.app.translations': 'เบลนเดอร์.แอป.การแปล',
            'bpy.msgbus': 'เบลนเดอร์.ระบบข้อความ', 'bpy.path': 'เบลนเดอร์.เส้นทาง', 'bpy.props': 'เบลนเดอร์.คุณสมบัติ',
            'bpy.utils': 'เบลนเดอร์.เครื่องมือ', 'bpy.utils.previews': 'เบลนเดอร์.เครื่องมือ.ภาพตัวอย่าง',
            'bpy.utils.units': 'เบลนเดอร์.เครื่องมือ.หน่วย', 'gpu': 'กราฟิก', 'gpu.types': 'กราฟิก.ชนิดข้อมูล',
            'gpu.capabilities': 'กราฟิก.ความสามารถ', 'gpu.compute': 'กราฟิก.คำนวณ', 'gpu.matrix': 'กราฟิก.เมทริกซ์',
            'gpu.platform': 'กราฟิก.แพลตฟอร์ม', 'gpu.select': 'กราฟิก.เลือก', 'gpu.shader': 'กราฟิก.เชเดอร์',
            'gpu.state': 'กราฟิก.สถานะ', 'gpu.texture': 'กราฟิก.พื้นผิว'
        }
        for entry in result.get('entries', []):
            kind = entry.get('kind')
            if kind == 'operator':
                continue
            if kind == 'type':
                base = 'ชนิดข้อมูล.' + entry['thaiIdentifier']
            elif kind in ('function', 'property'):
                owner = entry.get('ownerType') or ''
                base = 'ชนิดข้อมูล.' + type_names.get(owner, identifier(entry.get('thaiCategory') or owner)) + '.' + entry['thaiIdentifier']
            else:
                prefix = module_names.get(entry.get('category'), identifier(entry.get('thaiCategory') or 'โมดูล'))
                entry['thaiCategory'] = prefix
                base = prefix + '.' + entry['thaiIdentifier']
            path, number = base, 2
            while _alias_key(path) in paths:
                path = base + '_' + str(number); number += 1
            paths.add(_alias_key(path)); entry['thaiPath'] = path
        result['localization'] = {
            'language': 'th', 'schemaVersion': 1,
            'officialCatalog': str(self.mo_path) if self.mo_path else None,
            'officialCatalogSha256': hashlib.sha256(self.mo_path.read_bytes()).hexdigest() if self.mo_path else None,
            'glossarySha256': hashlib.sha256((ROOT / 'thai_glossary.json').read_bytes()).hexdigest(),
            'extraGlossarySha256': hashlib.sha256(self.extra_path.read_bytes()).hexdigest() if self.extra_path.is_file() else None,
            'overlayGlossaries': {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in self.overlay_paths if path.is_file()},
            'sources': dict(self.sources), 'entrySources': dict(self.entry_sources),
            'entryCount': len(result.get('entries', [])),
            'entriesWithCompleteThaiName': len(result.get('entries', [])) - len(self.untranslated_entries),
            'entriesWithUntranslatedWords': len(self.untranslated_entries),
            'missingWords': [{'word': word, 'occurrences': count} for word, count in self.missing.most_common()],
            'untranslatedEntries': self.untranslated_entries,
            'notes': [
                'official = คำแปลจากไฟล์ gettext ภาษาไทยที่ Blender จัดส่ง',
                'manual = คำแปลวลีที่ผู้พัฒนาเรียบเรียง',
                'glossary = ประกอบคำจากอภิธานศัพท์ อาจต้องปรับสำนวน',
                'untranslated = ยังไม่มีคำแปล เก็บชื่ออังกฤษไว้',
                'generated-summary = สรุปภาษาไทยจากชื่อและชนิดข้อมูล ไม่ใช่การแปลคำอธิบายอังกฤษทั้งประโยค',
                'ชื่อ API ดั้งเดิมใน id และค่า enum ไม่ได้เปลี่ยน จึงเรียก Blender จริงได้'
            ]
        }
        return result


def localize_catalog(input_path, output_path, glossary_path=None, mo_path=None):
    catalog = json.loads(Path(input_path).read_text(encoding='utf-8-sig'))
    metadata = catalog.get('blender', {})
    localizer = Localizer(glossary_path, mo_path, metadata.get('binaryPath') or metadata.get('binary'))
    result = localizer.localize(catalog)
    target = Path(output_path)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    return result['localization']


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description='สร้างชื่อและคำอธิบายภาษาไทยจาก Blender API inventory')
    parser.add_argument('input')
    parser.add_argument('output')
    args = parser.parse_args()
    report = localize_catalog(args.input, args.output)
    print(json.dumps({k: v for k, v in report.items() if k not in ('untranslatedEntries', 'missingWords')}, ensure_ascii=False, indent=2))
