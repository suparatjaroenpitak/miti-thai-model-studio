"""Inventory the installed Blender's exposed Python/RNA surface without executing operators.

Run with blender --background --factory-startup --python extract_api.py -- --output catalog.json.
This is controlled runtime introspection, not native decompilation or REA Evidence.
"""
import argparse
from collections import Counter
import datetime
import gettext
import hashlib
import importlib
import inspect
import json
import math
import os
from pathlib import Path
import sys
import types

import bpy


def digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def json_value(value):
    if value is None or isinstance(value, (str, bool, int)):
        return value
    if isinstance(value, float):
        return value if math.isfinite(value) else str(value)
    try:
        return [json_value(v) for v in value]
    except TypeError:
        return str(value)


def write_json_atomic(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.tmp-' + str(os.getpid()))
    try:
        temporary.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


class Extractor:
    def __init__(self):
        self.entries = []
        self.errors = []
        self.missing = Counter()
        self.name_missing = Counter()
        self.enum_count = 0
        self.translated_fields = 0
        self.untranslated_fields = 0
        self.catalog_path = Path(bpy.utils.resource_path('LOCAL')) / 'datafiles' / 'locale' / 'th' / 'LC_MESSAGES' / 'blender.mo'
        with self.catalog_path.open('rb') as stream:
            self.thai = gettext.GNUTranslations(stream)

    def translate(self, text, context='*', name=False):
        if not text:
            return None
        translated = self.thai.pgettext(context or '*', text)
        if translated == text:
            translated = self.thai.gettext(text)
        if translated == text and context != '*':
            translated = self.thai.pgettext('*', text)
        if translated != text and any('\u0e00' <= c <= '\u0e7f' for c in translated):
            self.translated_fields += 1
            return translated
        self.untranslated_fields += 1
        self.missing[text] += 1
        if name:
            self.name_missing[text] += 1
        return None

    def labels(self, name, description, context='*'):
        return {'name': name or '', 'description': description or '',
                'thaiName': self.translate(name, context, True),
                'thaiDescription': self.translate(description, context),
                'translationContext': context or '*'}

    def property(self, prop, include_enum=True):
        result = {'id': prop.identifier, **self.labels(prop.name, prop.description, getattr(prop, 'translation_context', '*')),
                  'type': prop.type, 'arrayLength': getattr(prop, 'array_length', 0),
                  'required': bool(getattr(prop, 'is_required', False)),
                  'readOnly': bool(getattr(prop, 'is_readonly', False)),
                  'subtype': getattr(prop, 'subtype', 'NONE'),
                  'isOutput': bool(getattr(prop, 'is_output', False))}
        if prop.type in {'BOOLEAN', 'INT', 'FLOAT', 'STRING', 'ENUM'}:
            try:
                if getattr(prop, 'is_array', False):
                    result['default'] = json_value(prop.default_array)
                elif getattr(prop, 'is_enum_flag', False):
                    result['default'] = sorted(prop.default_flag)
                else:
                    result['default'] = json_value(prop.default)
            except Exception as error:
                result['defaultUnavailable'] = type(error).__name__
        if prop.type in {'INT', 'FLOAT'}:
            result['minimum'] = json_value(prop.hard_min)
            result['maximum'] = json_value(prop.hard_max)
        if prop.type in {'POINTER', 'COLLECTION'}:
            try:
                result['fixedType'] = prop.fixed_type.identifier
            except Exception:
                result['fixedType'] = None
        if prop.type == 'ENUM' and include_enum:
            result['enumItems'] = []
            try:
                for item in prop.enum_items:
                    if not item.identifier:
                        continue
                    result['enumItems'].append({'id': item.identifier,
                                               **self.labels(item.name, item.description, getattr(prop, 'translation_context', '*'))})
                    self.enum_count += 1
            except Exception as error:
                result['enumUnavailable'] = str(error)
        return result

    def operators(self):
        namespaces = [name for name in dir(bpy.ops) if not name.startswith('_')]
        attempted = 0
        for category in sorted(namespaces):
            namespace = getattr(bpy.ops, category)
            for name in sorted(dir(namespace)):
                if name.startswith('_'):
                    continue
                attempted += 1
                identity = 'bpy.ops.' + category + '.' + name
                try:
                    operator = getattr(namespace, name)
                    rna = operator.get_rna_type()
                    try:
                        poll = bool(operator.poll())
                        poll_error = None
                    except Exception as error:
                        poll = None
                        poll_error = repr(error)
                    self.entries.append({'id': identity, 'kind': 'operator', 'category': category,
                                         **self.labels(rna.name, rna.description, rna.translation_context),
                                         'parameters': [self.property(p) for p in rna.properties if p.identifier != 'rna_type'],
                                         'rnaType': rna.identifier, 'pollInFactoryContext': poll,
                                         'pollError': poll_error})
                except Exception as error:
                    self.errors.append({'id': identity, 'phase': 'operator', 'error': repr(error)})
        return {'operatorNamespaces': len(namespaces), 'operatorsAttempted': attempted}

    def rna_types(self):
        attempted = 0
        for name in sorted(dir(bpy.types)):
            if name.startswith('_'):
                continue
            cls = getattr(bpy.types, name)
            rna = getattr(cls, 'bl_rna', None)
            if rna is None:
                continue
            attempted += 1
            identity = 'bpy.types.' + name
            try:
                parent = rna.base
                parent_properties = set(p.identifier for p in parent.properties) if parent else set()
                parent_functions = set(f.identifier for f in parent.functions) if parent else set()
                self.entries.append({'id': identity, 'kind': 'type', 'category': name,
                                     **self.labels(rna.name, rna.description, rna.translation_context),
                                     'parentType': 'bpy.types.' + parent.identifier if parent else None})
                for prop in rna.properties:
                    if prop.identifier == 'rna_type' or prop.identifier in parent_properties:
                        continue
                    metadata = self.property(prop)
                    self.entries.append({**metadata, 'id': identity + '.' + prop.identifier,
                                         'identifier': prop.identifier, 'kind': 'property', 'category': name,
                                         'ownerType': identity})
                for fn in rna.functions:
                    if fn.identifier in parent_functions:
                        continue
                    self.entries.append({'id': identity + '.' + fn.identifier,
                                         'identifier': fn.identifier, 'kind': 'function', 'category': name,
                                         'ownerType': identity,
                                         **self.labels(fn.identifier.replace('_', ' ').title(), fn.description),
                                         'parameters': [self.property(p) for p in fn.parameters],
                                         'useSelf': bool(fn.use_self), 'useSelfType': bool(fn.use_self_type)})
            except Exception as error:
                self.errors.append({'id': identity, 'phase': 'rnaType', 'error': repr(error)})
        return {'rnaTypesAttempted': attempted}

    def extra_type_functions(self):
        """Capture bpy_types.py helpers and non-RNA native bpy_struct methods too."""
        known = {(entry.get('apiPath', entry['id']), entry['kind']) for entry in self.entries}
        extra = 0
        for name in sorted(dir(bpy.types)):
            if name.startswith('_'):
                continue
            cls = getattr(bpy.types, name)
            if not inspect.isclass(cls):
                continue
            owner = 'bpy.types.' + name
            try:
                own_names = set(vars(cls))
            except TypeError:
                own_names = set()
            for member in sorted(own_names):
                if member.startswith('_'):
                    continue
                identity = owner + '.' + member
                if (identity, 'function') in known:
                    continue
                try:
                    function = getattr(cls, member)
                    if not inspect.isroutine(function) and not inspect.ismethoddescriptor(function):
                        continue
                    signature = None
                    parameters = []
                    try:
                        parsed = inspect.signature(function)
                        signature = str(parsed)
                        for parameter in parsed.parameters.values():
                            if parameter.name in {'self', 'cls'}:
                                continue
                            item = {'id': parameter.name,
                                    **self.labels(parameter.name.replace('_', ' ').title(), ''),
                                    'type': 'PYTHON', 'arrayLength': 0,
                                    'required': parameter.default is inspect.Parameter.empty and parameter.kind not in {inspect.Parameter.VAR_POSITIONAL, inspect.Parameter.VAR_KEYWORD},
                                    'readOnly': False, 'subtype': 'NONE', 'parameterKind': parameter.kind.name}
                            if parameter.default is not inspect.Parameter.empty:
                                item['default'] = json_value(parameter.default)
                            parameters.append(item)
                    except (ValueError, TypeError):
                        pass
                    self.entries.append({'id': identity, 'kind': 'function', 'category': name,
                                         'identifier': member, 'ownerType': owner,
                                         **self.labels(member.replace('_', ' ').title(), inspect.getdoc(function) or ''),
                                         'parameters': parameters, 'signature': signature,
                                         'useSelf': True, 'sourceKind': 'public-non-RNA-type-member'})
                    known.add((identity, 'function'))
                    extra += 1
                except Exception as error:
                    self.errors.append({'id': identity, 'phase': 'extraTypeMember', 'error': repr(error)})
        return {'nonRnaTypeFunctions': extra}

    def modules(self):
        module_names = ['bpy', 'bpy.context', 'bpy.data', 'bpy.app', 'bpy.app.handlers', 'bpy.app.timers', 'bpy.app.translations',
                        'bpy.utils', 'bpy.utils.previews', 'bpy.path', 'bpy.props', 'bpy.msgbus',
                        'bmesh', 'bmesh.ops', 'bmesh.utils', 'bmesh.geometry', 'bmesh.types',
                        'mathutils', 'mathutils.geometry', 'mathutils.interpolate',
                        'mathutils.bvhtree', 'mathutils.kdtree', 'mathutils.noise',
                        'gpu', 'gpu.state', 'gpu.matrix', 'gpu.shader', 'gpu.texture', 'gpu.types',
                        'gpu.capabilities', 'gpu.platform', 'gpu.select', 'gpu.compute',
                        'bpy.utils.units', 'blf', 'aud']
        visited = 0
        def append_callable(identity, value, category, owner=None):
            try:
                signature = str(inspect.signature(value))
            except (ValueError, TypeError):
                signature = None
            description = inspect.getdoc(value) or ''
            # RNA functions above cover bpy.types, these are native/module Python bindings.
            self.entries.append({'id': identity, 'kind': 'moduleFunction', 'category': category,
                                 **self.labels(identity.rsplit('.', 1)[-1].replace('_', ' ').title(), description),
                                 'signature': signature, 'ownerType': owner,
                                 'isClass': inspect.isclass(value)})
        for module_name in module_names:
            try:
                module = importlib.import_module(module_name)
            except (ModuleNotFoundError, ImportError):
                try:
                    module = bpy if module_name == 'bpy' else __import__(module_name.split('.')[0])
                    for part in module_name.split('.')[1:]:
                        module = getattr(module, part)
                except Exception as error:
                    self.errors.append({'id': module_name, 'phase': 'module', 'error': repr(error)})
                    continue
            visited += 1
            for name in sorted(dir(module)):
                if name.startswith('_'):
                    continue
                try:
                    value = getattr(module, name)
                    if not callable(value):
                        continue
                    identity = module_name + '.' + name
                    append_callable(identity, value, module_name)
                    if inspect.isclass(value) and module_name in {'mathutils', 'mathutils.bvhtree', 'mathutils.kdtree', 'bmesh', 'bmesh.types', 'gpu', 'gpu.types', 'aud'}:
                        for member in sorted(dir(value)):
                            if member.startswith('_'):
                                continue
                            child = getattr(value, member)
                            if callable(child):
                                append_callable(identity + '.' + member, child, module_name, identity)
                except Exception as error:
                    self.errors.append({'id': module_name + '.' + name, 'phase': 'moduleMember', 'error': repr(error)})
        return {'modulesRequested': len(module_names), 'modulesVisited': visited, 'moduleNames': module_names}

    def save(self, output):
        phases = {}
        for method in (self.operators, self.rna_types, self.extra_type_functions, self.modules):
            phases.update(method())
            print(method.__name__, len(self.entries), flush=True)
        ids = [e['id'] for e in self.entries]
        if len(set(ids)) != len(ids):
            repeated = [identity for identity, count in Counter(ids).items() if count > 1]
            groups = [{'id': identity, 'kinds': [e['kind'] for e in self.entries if e['id'] == identity]} for identity in repeated]
            print('DUPLICATES ' + json.dumps(groups), flush=True)
            by_id = {}
            for entry in self.entries:
                if entry['id'] in by_id:
                    prior = by_id[entry['id']]
                    if prior['kind'] != entry['kind']:
                        # RenderEngine exposes both an RNA property and a function
                        # named render. Keep both, with an unambiguous catalog key.
                        if prior['kind'] == 'property':
                            old_id = prior['id']
                            prior['apiPath'] = old_id
                            prior['id'] = old_id + '#property'
                            by_id[prior['id']] = prior
                            by_id[old_id] = entry
                        else:
                            entry['apiPath'] = entry['id']
                            entry['id'] += '#' + entry['kind']
                            by_id[entry['id']] = entry
                else:
                    by_id[entry['id']] = entry
            self.entries = list(by_id.values())
            phases['duplicateDiscoveryPaths'] = groups
        counts = Counter(entry['kind'] for entry in self.entries)
        counts.update({'entries': len(self.entries), 'enumItems': self.enum_count,
                       'officialTranslatedFields': self.translated_fields, 'missingTranslationFields': self.untranslated_fields})
        data = {'schemaVersion': 1,
                'observationKind': 'ordinary-controlled-runtime-introspection-not-REA-Evidence',
                'generatedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'blender': {'version': list(bpy.app.version), 'versionString': bpy.app.version_string,
                            'buildHash': bpy.app.build_hash.decode(), 'binaryPath': bpy.app.binary_path,
                            'binarySha256': digest(bpy.app.binary_path)},
                'counts': dict(counts), 'entries': sorted(self.entries, key=lambda e: e['id']),
                'coverage': {**phases, 'errors': self.errors,
                             'rnaInheritance': 'Each member is listed at its defining RNA base; parentType links retain inheritance. Inherited duplicate identifiers are omitted.',
                             'registeredState': 'factory-startup background; installed user add-ons are not enabled',
                             'executedOperators': 0,
                             'boundary': 'All operators reported by dir(bpy.ops), every class exposing bl_rna in dir(bpy.types), its defining properties/functions, direct public routine members of bpy.types classes including Python helper methods, and public callables of named modules/objects. Not native internal functions, unregistered extensions, every dynamically returned Python object, or every context-dependent operator behavior.',
                             'nativeDecompilation': False},
                'translations': {'source': str(self.catalog_path), 'sha256': digest(self.catalog_path),
                                 'provenance': 'Official Thai GNU gettext catalog shipped with this installed Blender. Missing translations stay null.',
                                 'gettextMessages': sum(1 for key in self.thai._catalog if isinstance(key, str) and key),
                                 'missingUniqueStrings': len(self.missing)}}
        write_json_atomic(output, data)
        missing = {'schemaVersion': 1, 'provenance': 'English strings missing an official Thai translation in the installed Blender gettext catalog',
                   'names': [{'text': name, 'occurrences': count} for name, count in self.name_missing.most_common()],
                   'allStrings': [{'text': text, 'occurrences': count} for text, count in self.missing.most_common()]}
        write_json_atomic(output.with_name('untranslated-strings.json'), missing)
        print(json.dumps({'output': str(output), 'counts': dict(counts), 'phases': phases, 'errors': len(self.errors)}, ensure_ascii=False, indent=2), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True)
    parser.add_argument('--localized-output')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    Extractor().save(Path(args.output).resolve())
    if args.localized_output:
        sys.path.insert(0, str(Path(__file__).resolve().parent))
        from thai_localization import localize_catalog
        localize_catalog(Path(args.output).resolve(), Path(args.localized_output).resolve())
