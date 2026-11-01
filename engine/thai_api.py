"""Thai access layer for Blender's *real* Python API.

This module does not reimplement Blender. It resolves localized metadata to
the installed bpy/bmesh/mathutils API and returns native Blender values.
"""
from pathlib import Path
import importlib
import json
import os
import re
import unicodedata

import bpy
import bmesh
import mathutils


def _key(value):
    return re.sub(r"[\s_\-./]+", "", unicodedata.normalize('NFKC', str(value)).casefold())


def _names(item):
    return [str(item[key]) for key in ('id', 'name', 'thaiName', 'thaiIdentifier', 'thaiPath')
            if item.get(key)]


_COMMON = {
    'ข้อมูล': 'data', 'บริบท': 'context', 'คำสั่ง': 'ops', 'ชนิด': 'type',
    'เครื่องมือ': 'utils', 'แอป': 'app', 'เส้นทาง': 'path',
    'ชื่อ': 'name', 'ชนิดข้อมูล': 'type', 'ประเภท': 'type', 'ชนิดวัตถุ': 'type',
    'ตำแหน่ง': 'location', 'การหมุน': 'rotation_euler', 'สเกล': 'scale',
    'ขนาด': 'size', 'มิติ': 'dimensions', 'รัศมี': 'radius', 'ความลึก': 'depth',
    'ความกว้าง': 'width', 'ความสูง': 'height', 'ความยาว': 'length',
    'จุดยอด': 'vertices', 'ขอบ': 'edges', 'หน้า': 'faces',
    'รูปหลายเหลี่ยม': 'polygons', 'โหนด': 'nodes', 'การเชื่อมโยง': 'links',
    'อินพุต': 'inputs', 'เอาต์พุต': 'outputs', 'ค่าเริ่มต้น': 'default_value',
    'โครงสร้างโหนด': 'node_tree', 'ใช้โหนด': 'use_nodes',
    'สร้าง': 'new', 'เพิ่ม': 'add', 'ลบ': 'remove', 'ล้าง': 'clear',
    'อัปเดต': 'update', 'ค้นหา': 'find', 'รับ': 'get',
    'ผนวก': 'append', 'เชื่อม': 'link', 'ยกเลิกการเชื่อม': 'unlink',
    'จำนวน': 'count', 'ส่วน': 'segments', 'วงแหวน': 'rings',
    'เลือก': 'select', 'เลือกทั้งหมด': 'select_all', 'ปัจจุบัน': 'active',
    'ตัวปรับแต่ง': 'modifiers', 'ข้อมูลวัตถุ': 'object_data',
    'โหมด': 'mode', 'การกระทำ': 'action', 'การดำเนินการ': 'operation',
    'รวมวัตถุ': 'join', 'เมทริกซ์': 'matrix', 'สี': 'color',
}
_CATEGORIES = {
    'ตาข่าย': 'mesh', 'เมช': 'mesh', 'วัตถุ': 'object', 'แปลงรูป': 'transform',
    'แปลง': 'transform', 'โหนด': 'node', 'เรนเดอร์': 'render', 'ฉาก': 'scene',
    'ระบบหน้าต่าง': 'wm', 'หน้าต่าง': 'wm', 'หน้าจอ': 'screen', 'มุมมองสามมิติ': 'view3d',
    'มุมมอง3มิติ': 'view3d', 'ภาพ': 'image', 'วัสดุ': 'material', 'พื้นผิว': 'texture',
    'เส้นโค้ง': 'curve', 'กระดูก': 'armature', 'โพส': 'pose', 'ปั้น': 'sculpt',
    'ยูวี': 'uv', 'การเคลื่อนไหว': 'anim', 'แอนิเมชัน': 'anim',
    'จาระบีเพนซิล': 'grease_pencil', 'ดินสอ': 'grease_pencil',
    'อนุภาค': 'particle', 'ลำดับภาพ': 'sequencer', 'ข้อความ': 'text',
    'อักษร': 'font', 'ไฟล์': 'file', 'นำเข้าฉาก': 'import_scene',
    'ส่งออกฉาก': 'export_scene', 'การตั้งค่า': 'preferences',
}
_CONTEXT_NAMES = {
    'bpy': {'ข้อมูล': 'data', 'บริบท': 'context', 'คำสั่ง': 'ops', 'ชนิด': 'types'},
    'bpy.data': {'วัตถุ': 'objects', 'วัสดุ': 'materials', 'ฉาก': 'scenes',
                 'ตาข่าย': 'meshes', 'เมช': 'meshes', 'ภาพ': 'images',
                 'เส้นโค้ง': 'curves', 'กล้อง': 'cameras', 'แสง': 'lights',
                 'คอลเลกชัน': 'collections', 'กลุ่มโหนด': 'node_groups',
                 'โลก': 'worlds', 'ข้อความ': 'texts', 'กระดูก': 'armatures'},
    'bpy.context': {'วัตถุ': 'object', 'วัตถุปัจจุบัน': 'active_object',
                    'วัตถุที่เลือก': 'selected_objects', 'ฉาก': 'scene',
                    'คอลเลกชัน': 'collection', 'ชั้นมุมมอง': 'view_layer',
                    'พื้นที่': 'area', 'หน้าต่าง': 'window',
                    'การตั้งค่า': 'preferences', 'โหมด': 'mode'},
    'bpy.ops': _CATEGORIES,
}
_BUILTIN_OPERATOR_ALIASES = {
    'mesh.primitive_cube_add': ['เพิ่มลูกบาศก์', 'เพิ่มกล่อง'],
    'mesh.primitive_uv_sphere_add': ['เพิ่มทรงกลมยูวี', 'เพิ่มทรงกลม'],
    'mesh.primitive_cylinder_add': ['เพิ่มทรงกระบอก'],
    'mesh.primitive_cone_add': ['เพิ่มกรวย'],
    'mesh.primitive_plane_add': ['เพิ่มระนาบ', 'เพิ่มพื้น'],
    'mesh.primitive_torus_add': ['เพิ่มวงแหวน'],
    'mesh.primitive_monkey_add': ['เพิ่มหัวลิง'],
    'object.modifier_add': ['เพิ่มตัวปรับแต่ง'],
    'object.mode_set': ['ตั้งโหมด'], 'object.select_all': ['เลือกทั้งหมด'],
    'object.delete': ['ลบวัตถุ'], 'object.join': ['รวมวัตถุ'],
    'transform.translate': ['เลื่อน'], 'transform.rotate': ['หมุน'],
    'transform.resize': ['ปรับขนาด'],
}


class _Catalog:
    def __init__(self):
        self.loaded = False
        self.path = None
        self.data = {}
        self.entries = {}
        self.native_entries = {}
        self.aliases = {}
        self.members = {}
        self.explicit_aliases = {}
        self.explicit_members = {}
        self.type_paths = {}

    @staticmethod
    def add(mapping, alias, identifier):
        if alias:
            mapping.setdefault(_key(alias), set()).add(identifier)

    def load(self):
        if self.loaded:
            return self
        configured = os.environ.get('MITI_API_CATALOG')
        candidates = ([Path(configured)] if configured else []) + [Path(__file__).with_name('blender_api.json')]
        for path in candidates:
            if path.is_file():
                self.path = str(path)
                with path.open(encoding='utf-8-sig') as stream:
                    self.data = json.load(stream)
                break
        for entry in self.data.get('entries', []):
            identifier = entry.get('id')
            if not identifier:
                continue
            self.entries[identifier] = entry
            native = entry.get('apiPath') or identifier
            self.native_entries.setdefault(native, []).append(entry)
            if entry.get('kind') == 'type':
                self.type_paths.setdefault(native.rsplit('.', 1)[-1], set()).add(native)
            for explicit in (entry.get('thaiPath'), entry.get('thaiIdentifier')):
                if explicit:
                    self.add(self.explicit_aliases, explicit, identifier)
            # Only fully qualified names resolve arbitrary API members; bare
            # display labels intentionally remain ambiguous when duplicated.
            for alias in _names(entry):
                self.add(self.aliases, alias, identifier)
            if native.startswith('bpy.ops.'):
                short = native[len('bpy.ops.'):]
                category, member = short.split('.', 1)
                self.add(self.aliases, short, identifier)
                for alias in _names(entry):
                    self.add(self.aliases, category + '.' + alias, identifier)
                    for thai, english in _CATEGORIES.items():
                        if english == category:
                            self.add(self.aliases, thai + '.' + alias, identifier)
                aliases = self.members.setdefault('bpy.ops.' + category, {})
                if entry.get('thaiIdentifier'):
                    self.add(self.explicit_members.setdefault('bpy.ops.' + category, {}), entry['thaiIdentifier'], member)
                for alias in _names(entry):
                    self.add(aliases, alias, member)
            elif '.' in native:
                owner, member = native.rsplit('.', 1)
                aliases = self.members.setdefault(owner, {})
                if entry.get('thaiIdentifier'):
                    self.add(self.explicit_members.setdefault(owner, {}), entry['thaiIdentifier'], member)
                for alias in _names(entry):
                    self.add(aliases, alias, member)
        for identifier, names in _BUILTIN_OPERATOR_ALIASES.items():
            full = 'bpy.ops.' + identifier
            category, member = identifier.split('.', 1)
            aliases = self.members.setdefault('bpy.ops.' + category, {})
            self.add(self.aliases, identifier, full)
            for name in names:
                self.add(self.aliases, name, full)
                self.add(aliases, name, member)
                for thai, english in _CATEGORIES.items():
                    if english == category:
                        self.add(self.aliases, thai + '.' + name, full)
        self.loaded = True
        return self


_catalog = _Catalog()


def _unique(candidates, value, label):
    if len(candidates) > 1:
        raise ValueError('%s "%s" ซ้ำกัน กรุณาใช้ชื่อ API เต็ม: %s' %
                         (label, value, ', '.join(sorted(candidates))))
    return next(iter(candidates)) if candidates else None


def _unwrap(value):
    if isinstance(value, (_Proxy, _Callable)):
        return object.__getattribute__(value, '_target')
    if isinstance(value, tuple):
        return tuple(_unwrap(item) for item in value)
    if isinstance(value, list):
        return [_unwrap(item) for item in value]
    if isinstance(value, dict):
        return {key: _unwrap(item) for key, item in value.items()}
    return value


def ของจริง(value):
    """Return the original bpy/bmesh/mathutils object from a Thai navigation proxy."""
    return _unwrap(value)


def _rna(target):
    try:
        result = target.bl_rna
        # bpy.ops fabricates members on getattr, including a fake "bl_rna".
        return result if isinstance(getattr(result, 'identifier', None), str) else None
    except (AttributeError, ReferenceError, RuntimeError):
        return None


def _owner_paths(target, path=''):
    if path:
        yield path
    rna = _rna(target)
    visited = set()
    while rna and rna.identifier not in visited:
        visited.add(rna.identifier)
        yield 'bpy.types.' + rna.identifier
        rna = rna.base
    if not visited:
        actual_type = target if isinstance(target, type) else type(target)
        module = getattr(actual_type, '__module__', '')
        name = getattr(actual_type, '__name__', '')
        if module and name:
            yield module + '.' + name
        known = _catalog.type_paths.get(name, set())
        if len(known) == 1:
            yield next(iter(known))


def _member(target, name, path=''):
    # An existing identifier always wins, even if a translated label collides.
    if name.isascii() and hasattr(target, name):
        return name
    candidates = set()
    for alias, identifier in _CONTEXT_NAMES.get(path, {}).items():
        if _key(alias) == _key(name):
            candidates.add(identifier)
    _catalog.load()
    for owner in _owner_paths(target, path):
        explicit = _catalog.explicit_members.get(owner, {}).get(_key(name), set())
        resolved = _unique(explicit, name, 'รหัสสมาชิกไทย')
        if resolved and hasattr(target, resolved):
            return resolved
    for owner in _owner_paths(target, path):
        candidates.update(_catalog.members.get(owner, {}).get(_key(name), set()))
    if not candidates:
        for alias, identifier in _COMMON.items():
            if _key(alias) == _key(name):
                candidates.add(identifier)
    available = {identifier for identifier in candidates if hasattr(target, identifier)}
    resolved = _unique(available, name, 'ชื่อสมาชิก')
    if resolved is not None:
        return resolved
    if hasattr(target, name):
        return name
    raise AttributeError('ไม่พบสมาชิก "%s" ใน %s ใช้รหัส API อังกฤษได้โดยตรง' % (name, path or type(target).__name__))


def _schema_property(prop):
    items = []
    if prop.type == 'ENUM':
        try:
            items = [{'id': item.identifier, 'name': item.name} for item in prop.enum_items if item.identifier]
        except (AttributeError, TypeError):
            pass
    return {'id': prop.identifier, 'name': prop.name, 'type': prop.type, 'enumItems': items}


def _function_metadata(target, name, path=''):
    _catalog.load()
    for owner in _owner_paths(target, path):
        entries = _catalog.native_entries.get(owner + '.' + name, [])
        entry = next((entry for entry in entries if entry.get('kind') in {'function', 'moduleFunction'}), None)
        if entry is not None:
            return entry
    rna = _rna(target)
    if rna and name in rna.functions:
        return {'parameters': [_schema_property(p) for p in rna.functions[name].parameters if not p.is_output]}
    return {}


def _property_metadata(target, name, path=''):
    _catalog.load()
    for owner in _owner_paths(target, path):
        entries = _catalog.native_entries.get(owner + '.' + name, [])
        entry = next((entry for entry in entries if entry.get('kind') == 'property'), None)
        if entry is not None:
            return entry
    rna = _rna(target)
    if rna and name in rna.properties:
        return _schema_property(rna.properties[name])
    return {}


def _enum_value(value, definition):
    value = _unwrap(value)
    items = definition.get('enumItems') or definition.get('enum_items') or []
    if not items:
        return value
    ids = {item.get('id', item.get('identifier')) for item in items}
    aliases = {}
    explicit = {}
    for item in items:
        identifier = item.get('id', item.get('identifier'))
        if item.get('thaiIdentifier'):
            _Catalog.add(explicit, item['thaiIdentifier'], identifier)
        for alias in _names(item):
            _Catalog.add(aliases, alias, identifier)
    def translate(item):
        if not isinstance(item, str) or item in ids:
            return item
        return (_unique(explicit.get(_key(item), set()), item, 'รหัส enum ไทย')
                or _unique(aliases.get(_key(item), set()), item, 'ค่า enum') or item)
    if isinstance(value, (set, frozenset)):
        return {translate(item) for item in value}
    return translate(value)


def _kwargs(metadata, values):
    definitions = metadata.get('parameters') or []
    by_id = {p['id']: p for p in definitions if p.get('id')}
    aliases = {}
    explicit = {}
    for item in definitions:
        if item.get('thaiIdentifier'):
            _Catalog.add(explicit, item['thaiIdentifier'], item['id'])
        for alias in _names(item):
            _Catalog.add(aliases, alias, item['id'])
        for thai, english in _COMMON.items():
            if english == item['id']:
                _Catalog.add(aliases, thai, item['id'])
    result = {}
    for name, value in values.items():
        identifier = name if name in by_id else _unique(explicit.get(_key(name), set()), name, 'รหัสพารามิเตอร์ไทย')
        if identifier is None:
            identifier = _unique(aliases.get(_key(name), set()), name, 'พารามิเตอร์')
        if identifier is None:
            identifier = name if name.isascii() else _COMMON.get(name, name)
        if identifier in result:
            raise TypeError('พารามิเตอร์ "%s" ถูกระบุมากกว่าหนึ่งครั้ง' % identifier)
        result[identifier] = _enum_value(value, by_id.get(identifier, {}))
    return result


def _operator_metadata(path, operator):
    _catalog.load()
    if path in _catalog.entries:
        return _catalog.entries[path]
    try:
        return {'parameters': [_schema_property(p) for p in operator.get_rna_type().properties if p.identifier != 'rna_type']}
    except (AttributeError, RuntimeError):
        return {}


def _canonical(path):
    if not isinstance(path, str):
        raise TypeError('ชื่อคำสั่งต้องเป็นข้อความ')
    _catalog.load()
    if path in _catalog.entries:
        return _catalog.entries[path].get('apiPath') or path
    explicit = _unique(_catalog.explicit_aliases.get(_key(path), set()), path, 'รหัส API ไทย')
    if explicit:
        return _catalog.entries.get(explicit, {}).get('apiPath') or explicit
    if path.startswith(('bpy.', 'bmesh.', 'mathutils.')) and path.isascii():
        return path
    if path.isascii() and len(path.split('.')) == 2:
        first, second = path.split('.')
        if first in dir(bpy.ops) and second in dir(getattr(bpy.ops, first)):
            return 'bpy.ops.' + path
    resolved = _unique(_catalog.aliases.get(_key(path), set()), path, 'ชื่อ API')
    if resolved:
        return _catalog.entries.get(resolved, {}).get('apiPath') or resolved
    prefixes = ('เบลนเดอร์.คำสั่ง.', 'คำสั่ง.')
    for prefix in prefixes:
        if path.startswith(prefix):
            return _canonical(path[len(prefix):])
    return path


def _resolve(path):
    parts = _canonical(path).split('.')
    roots = {'bpy': bpy, 'เบลนเดอร์': bpy, 'bmesh': bmesh, 'ตาข่าย': bmesh,
             'mathutils': mathutils, 'คณิตสามมิติ': mathutils}
    module_roots = {'gpu', 'gpu_extras', 'bpy_extras', 'blf', 'aud', 'idprop'}
    if parts and parts[0] in module_roots:
        roots[parts[0]] = importlib.import_module(parts[0])
    if not parts or parts[0] not in roots:
        raise LookupError('ไม่พบ API "%s" ใช้รหัสเต็ม เช่น bpy.ops.mesh.primitive_cube_add หรือชื่อไทยจากคลังคำสั่ง' % path)
    target = roots[parts[0]]
    current = {'เบลนเดอร์': 'bpy', 'ตาข่าย': 'bmesh', 'คณิตสามมิติ': 'mathutils'}.get(parts[0], parts[0])
    metadata = {}
    for name in parts[1:]:
        try:
            member = _member(target, name, current)
        except AttributeError:
            # Several bundled packages deliberately do not import submodules
            # in their __init__. Resolve their public path without eval().
            if current.split('.')[0] not in {'bpy', 'เบลนเดอร์'} and name.isascii():
                try:
                    imported = importlib.import_module(current + '.' + name)
                except ModuleNotFoundError:
                    raise AttributeError('ไม่พบ API "%s.%s"' % (current, name)) from None
                target = imported
                current += '.' + name
                metadata = _catalog.entries.get(current, {})
                continue
            raise
        metadata = _function_metadata(target, member, current)
        target = getattr(target, member)
        current += '.' + member
    if current.startswith('bpy.ops.') and len(current.split('.')) == 4:
        metadata = _operator_metadata(current, target)
    else:
        choices = _catalog.native_entries.get(current, [])
        metadata = next((entry for entry in choices if entry.get('kind') in {'function', 'moduleFunction', 'type'}), metadata)
    return target, current, metadata


def _invoke(target, path, metadata, args, kwargs):
    try:
        return target(*(_unwrap(value) for value in args), **_kwargs(metadata, kwargs))
    except Exception as error:
        if hasattr(error, 'add_note'):
            context = ('คำสั่งบางชนิดต้องมีวัตถุ โหมด editor หรือ UI context ที่เหมาะสม; '
                       'process เบื้องหลังไม่มีหน้าต่าง Blender') if path.startswith('bpy.ops.') else 'ตรวจชนิดข้อมูลและเป้าหมายตาม Blender API'
            error.add_note('เรียก Blender API: %s\n%s' % (path, context))
        raise


def คำสั่ง(path, *args, **kwargs):
    """Call a real bpy.ops operator by canonical ID or a unique localized name."""
    target, canonical, metadata = _resolve(path)
    if not canonical.startswith('bpy.ops.') or len(canonical.split('.')) != 4:
        raise TypeError('คำสั่ง() ใช้กับ bpy.ops เท่านั้น สำหรับ API อื่นใช้ เรียกAPI()')
    return _invoke(target, canonical, metadata, args, kwargs)


เรียก = คำสั่ง


def เรียกAPI(path, *args, **kwargs):
    """Resolve and call any public bpy/bmesh/mathutils callable, retaining Blender behavior."""
    target, canonical, metadata = _resolve(path)
    if not callable(target):
        raise TypeError('API "%s" เป็นข้อมูล ไม่ใช่ฟังก์ชัน ใช้ อ่านค่า()' % canonical)
    return _invoke(target, canonical, metadata, args, kwargs)


def เรียกฟังก์ชัน(target, name, *args, **kwargs):
    raw = _unwrap(target)
    path = object.__getattribute__(target, '_path') if isinstance(target, _Proxy) else ''
    identifier = _member(raw, name, path)
    function = getattr(raw, identifier)
    if not callable(function):
        raise TypeError('สมาชิก "%s" ไม่ใช่ฟังก์ชัน' % name)
    return _invoke(function, path + '.' + identifier, _function_metadata(raw, identifier, path), args, kwargs)


def อ่านค่า(target, name):
    raw = _unwrap(target)
    path = object.__getattribute__(target, '_path') if isinstance(target, _Proxy) else ''
    return getattr(raw, _member(raw, name, path))


def ตั้งค่า(target, **values):
    raw = _unwrap(target)
    path = object.__getattribute__(target, '_path') if isinstance(target, _Proxy) else ''
    for name, value in values.items():
        identifier = _member(raw, name, path)
        setattr(raw, identifier, _enum_value(value, _property_metadata(raw, identifier, path)))
    return raw


def ค้นหาคำสั่ง(query='', limit=50, kind=None):
    """Return catalog metadata, never execute results. None limit returns all matches."""
    _catalog.load()
    words = [_key(word) for word in str(query).split()]
    results = []
    for entry in _catalog.entries.values():
        if kind and entry.get('kind') != kind:
            continue
        text = _key(' '.join(str(entry.get(key, '')) for key in
                             ('id', 'name', 'thaiName', 'thaiPath', 'description', 'thaiDescription')))
        if all(word in text for word in words):
            results.append(entry)
            if limit is not None and len(results) >= limit:
                break
    return results


def ข้อมูลคลังคำสั่ง():
    _catalog.load()
    return {'path': _catalog.path, 'entries': len(_catalog.entries),
            'blender': _catalog.data.get('blender', {}), 'counts': _catalog.data.get('counts', {}),
            'coverage': _catalog.data.get('coverage', {})}


class _Callable:
    def __init__(self, target, path, metadata):
        self._target = target
        self._path = path
        self._metadata = metadata

    def __call__(self, *args, **kwargs):
        return _invoke(self._target, self._path, self._metadata, args, kwargs)

    def __getattr__(self, name):
        return getattr(self._target, name)

    def __repr__(self):
        return '<ฟังก์ชันไทย %s>' % self._path


def _wrap(value, path='', metadata=None):
    if callable(value):
        return _Callable(value, path, metadata or {})
    if value is None or isinstance(value, (str, bytes, bool, int, float, complex, tuple, list, dict, set, frozenset)):
        return value
    if type(value).__module__.startswith('mathutils') or type(value).__name__ == 'bpy_prop_array':
        return value
    return _Proxy(value, path)


class _Proxy:
    def __init__(self, target, path=''):
        object.__setattr__(self, '_target', target)
        object.__setattr__(self, '_path', path)

    @property
    def ของจริง(self):
        return object.__getattribute__(self, '_target')

    @property
    def ต้นฉบับ(self):
        return object.__getattribute__(self, '_target')

    def __getattr__(self, name):
        target = object.__getattribute__(self, '_target')
        path = object.__getattribute__(self, '_path')
        identifier = _member(target, name, path)
        value = getattr(target, identifier)
        new_path = path + '.' + identifier if path else identifier
        metadata = _function_metadata(target, identifier, path)
        if new_path.startswith('bpy.ops.') and len(new_path.split('.')) == 4:
            metadata = _operator_metadata(new_path, value)
        return _wrap(value, new_path, metadata)

    def __setattr__(self, name, value):
        if name.startswith('_'):
            object.__setattr__(self, name, value)
            return
        target = object.__getattribute__(self, '_target')
        path = object.__getattribute__(self, '_path')
        identifier = _member(target, name, path)
        setattr(target, identifier, _enum_value(value, _property_metadata(target, identifier, path)))

    def __getitem__(self, key):
        return self._target[_unwrap(key)]

    def __setitem__(self, key, value):
        self._target[_unwrap(key)] = _unwrap(value)

    def __delitem__(self, key):
        del self._target[_unwrap(key)]

    def __iter__(self):
        return iter(self._target)

    def __len__(self):
        return len(self._target)

    def __bool__(self):
        return bool(self._target)

    def __eq__(self, other):
        return self._target == _unwrap(other)

    def __hash__(self):
        return hash(self._target)

    def __contains__(self, value):
        return _unwrap(value) in self._target

    def __repr__(self):
        return '<ไทย %s: %r>' % (self._path, self._target)


def ไทย(target):
    return target if isinstance(target, (_Proxy, _Callable)) else _wrap(target)


เบลนเดอร์ = _Proxy(bpy, 'bpy')
ตาข่าย = _Proxy(bmesh, 'bmesh')
คณิตสามมิติ = _Proxy(mathutils, 'mathutils')
call_operator = คำสั่ง
call_api = เรียกAPI
call_method = เรียกฟังก์ชัน
get_property = อ่านค่า
set_properties = ตั้งค่า
search_api = ค้นหาคำสั่ง
unwrap = ของจริง

__all__ = ['คำสั่ง', 'เรียก', 'เรียกAPI', 'เรียกฟังก์ชัน', 'อ่านค่า', 'ตั้งค่า',
           'ค้นหาคำสั่ง', 'ข้อมูลคลังคำสั่ง', 'ไทย', 'ของจริง', 'เบลนเดอร์', 'ตาข่าย',
           'คณิตสามมิติ', 'call_operator', 'call_api', 'call_method', 'get_property',
           'set_properties', 'search_api', 'unwrap']
# Python normalizes source identifiers with NFKC (notably Thai SARA AM).
# import-star uses literal string lookup, so exported names need the same form.
__all__ = [unicodedata.normalize('NFKC', name) for name in __all__]
