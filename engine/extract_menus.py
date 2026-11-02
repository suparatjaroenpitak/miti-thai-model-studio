"""Inspect shipped Blender UI Python without executing draw callbacks/operators.

Ordinary AST/package inspection plus optional registered Menu metadata, not native
decompilation or a retained REA Evidence record. Run in Blender for registry data.
"""
import argparse
import ast
from collections import Counter
import datetime
import hashlib
import json
from pathlib import Path
import re
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from thai_localization import Localizer, _humanize


class Symbol:
    def __init__(self, expression): self.expression = expression


class OperatorReference:
    def __init__(self, item): self.item = item


UNKNOWN = object()
UI_METHODS = {'operator', 'operator_enum', 'operator_menu_enum', 'menu', 'menu_contents',
              'prop', 'prop_enum', 'prop_menu_enum', 'prop_search', 'label', 'separator',
              'separator_spacer', 'popover', 'popover_group', 'template_operator_search',
              'template_node_operator_asset_menu_items', 'template_node_operator_asset_root_items',
              'template_modifier_asset_menu_items', 'template_modifier_asset_root_items',
              'template_edit_mode_selection'}


def sha256(path): return hashlib.sha256(path.read_bytes()).hexdigest()


class MenuExtractor:
    def __init__(self, source_root, api_path):
        self.source_root = source_root
        api = json.loads(api_path.read_text(encoding='utf-8'))
        self.blender = api['blender']
        self.api = {e['id'].removeprefix('bpy.ops.'): e for e in api['entries'] if e['kind'] == 'operator'}
        self.localizer = Localizer(binary_path=self.blender.get('binaryPath'))
        self.files, self.classes, self.functions, self.errors = {}, {}, {}, []
        self.file_hashes = []
        self.registered_menus = {}
        try:
            import bpy
            for name in dir(bpy.types):
                cls = getattr(bpy.types, name)
                if isinstance(cls, type) and issubclass(cls, bpy.types.Menu) and cls is not bpy.types.Menu:
                    self.registered_menus[name] = {'name': getattr(cls, 'bl_label', name), 'rnaName': cls.bl_rna.name}
        except ImportError:
            pass
        for path in sorted(source_root.glob('*.py')):
            try:
                tree = ast.parse(path.read_text(encoding='utf-8'), filename=str(path))
                constants, imports = {}, {}
                for node in tree.body:
                    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                        self.functions[(path.stem, node.name)] = (node, path.stem, None)
                    elif isinstance(node, ast.Assign):
                        for target in node.targets:
                            if isinstance(target, ast.Name):
                                try: constants[target.id] = ast.literal_eval(node.value)
                                except (ValueError, TypeError): pass
                    elif isinstance(node, ast.ImportFrom) and node.module:
                        for alias in node.names: imports[alias.asname or alias.name] = (node.module.split('.')[-1], alias.name)
                self.files[path.stem] = {'path': path, 'tree': tree, 'constants': constants, 'imports': imports}
                self.file_hashes.append({'path': str(path), 'sha256': sha256(path)})
                for node in tree.body:
                    if isinstance(node, ast.ClassDef):
                        attrs = {}
                        for member in node.body:
                            if isinstance(member, ast.Assign):
                                for target in member.targets:
                                    if isinstance(target, ast.Name):
                                        try: attrs[target.id] = ast.literal_eval(member.value)
                                        except (ValueError, TypeError): pass
                        info = {'node': node, 'module': path.stem, 'path': path, 'attrs': attrs,
                                'bases': [ast.unparse(base).split('.')[-1] for base in node.bases],
                                'methods': {n.name: n for n in node.body if isinstance(n, (ast.FunctionDef, ast.AsyncFunctionDef))}}
                        if node.name in self.classes:
                            self.errors.append({'phase': 'class-name', 'id': node.name, 'source': str(path), 'message': 'Duplicate source class name; first definition retained'})
                        else: self.classes[node.name] = info
            except Exception as error:
                self.errors.append({'phase': 'parse', 'path': str(path), 'message': repr(error)})

    def lineage(self, name, trail=()):
        if name in trail: return set()
        result = {name}
        for base in self.classes.get(name, {}).get('bases', []): result.update(self.lineage(base, trail + (name,)))
        return result

    def method(self, owner, name, trail=()):
        if owner in trail: return None
        info = self.classes.get(owner)
        if not info: return None
        if name in info['methods']: return info['methods'][name], info['module'], owner
        for base in info['bases']:
            found = self.method(base, name, trail + (owner,))
            if found: return found
        return None

    def expression(self, node, env):
        if isinstance(node, ast.Name) and isinstance(env.get(node.id), Symbol): return env[node.id].expression
        if isinstance(node, ast.Attribute): return self.expression(node.value, env) + '.' + node.attr
        return ast.unparse(node)

    def value(self, node, env, owner):
        if node is None: return UNKNOWN
        if isinstance(node, ast.Name): return env.get(node.id, Symbol(node.id))
        if isinstance(node, ast.Constant): return node.value
        if isinstance(node, (ast.List, ast.Tuple, ast.Set)):
            values = [self.value(x, env, owner) for x in node.elts]
            return values if all(self.literal(v) for v in values) else UNKNOWN
        if isinstance(node, ast.Dict):
            keys, values = [self.value(x, env, owner) for x in node.keys], [self.value(x, env, owner) for x in node.values]
            if all(self.literal(x) for x in keys + values):
                try: return dict(zip(keys, values))
                except TypeError: return UNKNOWN
        if isinstance(node, ast.Attribute):
            if isinstance(node.value, ast.Name) and node.value.id in {'self', 'cls'}:
                attrs = self.classes.get(owner, {}).get('attrs', {})
                if node.attr in attrs: return attrs[node.attr]
            base = self.value(node.value, env, owner)
            if isinstance(base, dict) and node.attr in base: return base[node.attr]
            return Symbol(self.expression(node, env))
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            left, right = self.value(node.left, env, owner), self.value(node.right, env, owner)
            if isinstance(left, str) and isinstance(right, str): return left + right
        if isinstance(node, ast.Subscript):
            base, index = self.value(node.value, env, owner), self.value(node.slice, env, owner)
            if self.literal(base) and self.literal(index):
                try: return base[index]
                except (TypeError, KeyError, IndexError): pass
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id in {'iface_', 'rpt_', 'tip_', '_', 'pgettext_iface', 'pgettext_tip'} and node.args:
            return self.value(node.args[0], env, owner)
        try: return ast.literal_eval(node)
        except (ValueError, TypeError): return UNKNOWN

    @staticmethod
    def literal(value):
        if value is None or isinstance(value, (str, int, float, bool)): return True
        if isinstance(value, (list, tuple)): return all(MenuExtractor.literal(v) for v in value)
        if isinstance(value, dict): return all(isinstance(k, (str, int, float, bool)) and MenuExtractor.literal(v) for k,v in value.items())
        return False

    def context(self, name, info):
        module = info.get('module', '')
        editor = {'space_view3d': 'VIEW_3D', 'space_image': 'IMAGE_EDITOR', 'space_node': 'NODE_EDITOR',
                  'space_text': 'TEXT_EDITOR', 'space_console': 'CONSOLE', 'space_sequencer': 'SEQUENCE_EDITOR',
                  'space_graph': 'GRAPH_EDITOR', 'space_dopesheet': 'DOPESHEET_EDITOR', 'space_nla': 'NLA_EDITOR',
                  'space_outliner': 'OUTLINER', 'space_clip': 'CLIP_EDITOR', 'space_filebrowser': 'FILE_BROWSER',
                  'space_spreadsheet': 'SPREADSHEET', 'space_info': 'INFO', 'space_topbar': 'TOPBAR',
                  'space_statusbar': 'STATUSBAR'}.get(module)
        editor = info.get('attrs', {}).get('bl_space_type', editor)
        context = {'editor': editor} if editor else {}
        lower = name.lower()
        if 'edit_mesh' in lower or 'edit_curve' in lower or 'edit_armature' in lower: context['mode'] = 'EDIT'
        elif 'paint_texture' in lower: context['mode'] = 'TEXTURE_PAINT'
        elif 'paint_vertex' in lower: context['mode'] = 'VERTEX_PAINT'
        elif 'paint_weight' in lower: context['mode'] = 'WEIGHT_PAINT'
        elif '_sculpt' in lower: context['mode'] = 'SCULPT'
        elif '_pose' in lower: context['mode'] = 'POSE'
        if 'uv' in lower and editor == 'IMAGE_EDITOR': context.update({'uiType': 'UV', 'mode': 'EDIT'})
        if context.get('mode') == 'EDIT' and 'mesh' in lower:
            for word, selection in [('vert', 'VERT'), ('edge', 'EDGE'), ('face', 'FACE')]:
                if word in lower: context['selection'] = selection; break
        if editor == 'NODE_EDITOR':
            for text, tree in [('shader', 'ShaderNodeTree'), ('geometry', 'GeometryNodeTree'), ('compositor', 'CompositorNodeTree')]:
                if text in lower: context['treeType'] = tree
        return context

    def translated(self, text, context='*'):
        if not text: return None, 'missing'
        value, source, _ = self.localizer.label(text, context)
        return value, source

    def extract_surface(self, name, info, kind):
        attrs = info['attrs']; identity = attrs.get('bl_idname') or name
        label = attrs.get('bl_label') or self.registered_menus.get(name, {}).get('name') or _humanize(re.sub(r'^.*?_(?:MT|HT|PT)_', '', name))
        thai, provenance = self.translated(label)
        surface = {'id': identity, 'className': name, 'kind': kind, 'name': label, 'thaiName': thai,
                   'nameTranslationSource': provenance, 'registered': name in self.registered_menus if kind == 'menu' else None,
                   'source': {'path': str(info['path']), 'line': info['node'].lineno, 'sha256': sha256(info['path'])},
                   'context': self.context(name, info), 'items': []}
        self.current = surface; self.calls = 0; self.helper_stack = []
        method = self.method(name, 'draw')
        if method:
            env = dict(self.files[method[1]]['constants']); env.update({'context': Symbol('bpy.context'), 'self': Symbol('self'), 'cls': Symbol(name)})
            self.walk_function(method, env, [], [], method[2] or name)
        else:
            surface['items'].append({'kind': 'dynamic', 'id': '', 'label': 'draw callback unavailable in source', 'thaiLabel': 'ไม่พบฟังก์ชันวาดในไฟล์ต้นทาง',
                                     'resolved': False, 'properties': {}, 'sourceLine': info['node'].lineno, 'expression': name + '.draw', 'condition': ''})
        return surface

    def walk_function(self, function, env, arguments, conditions, current_owner):
        node, module, owner = function
        key = (module, owner, node.name)
        if key in self.helper_stack or len(self.helper_stack) > 14:
            self.add_dynamic(node, env, conditions, 'recursive draw helper')
            return
        local = dict(self.files[module]['constants']); local.update(env)
        parameters = [p.arg for p in node.args.posonlyargs + node.args.args]
        for index, value in enumerate(arguments):
            if index < len(parameters): local[parameters[index]] = value
        self.helper_stack.append(key)
        self.walk_block(node.body, local, conditions, owner or current_owner, module)
        self.helper_stack.pop()

    def item(self, kind, identity, label, node, env, conditions, **extra):
        api = self.api.get(identity) if kind == 'operator' else None
        if not label and api: label = api.get('name')
        thai = api.get('thaiName') if api and not extra.get('explicitLabel') else None
        provenance = api.get('nameTranslationSource') if thai else None
        if not thai: thai, provenance = self.translated(label)
        result = {'kind': kind, 'id': identity or '', 'label': label or identity or '', 'thaiLabel': thai or label or identity or '',
                  'nameTranslationSource': provenance, 'properties': {}, 'sourceLine': node.lineno,
                  'condition': ' and '.join('(' + c + ')' for c in conditions),
                  'expression': self.expression(node, env), 'resolved': bool(identity), **extra}
        if kind == 'operator': result['apiAvailable'] = api is not None
        self.current['items'].append(result)
        return result

    def add_dynamic(self, node, env, conditions, reason):
        self.item('dynamic', '', reason, node, env, conditions, resolved=False)

    def call(self, node, env, conditions, owner, module):
        self.calls += 1
        if self.calls > 12000:
            if self.calls == 12001: self.add_dynamic(node, env, conditions, 'draw expansion budget reached')
            return UNKNOWN
        name = node.func.attr if isinstance(node.func, ast.Attribute) else None
        if name in UI_METHODS:
            values = [self.value(arg, env, owner) for arg in node.args]
            kwargs = {k.arg: self.value(k.value, env, owner) for k in node.keywords if k.arg}
            identity = values[0] if values and isinstance(values[0], str) else ''
            label = kwargs.get('text') if isinstance(kwargs.get('text'), str) and kwargs.get('text') else None
            if name in {'separator', 'separator_spacer'}: return UNKNOWN
            if name in {'operator', 'operator_enum', 'operator_menu_enum'}:
                item = self.item('operator', identity, label, node, env, conditions, explicitLabel=bool(label), uiMethod=name)
                if len(values) > 1 and isinstance(values[1], str): item['enumProperty'] = values[1]
                if env.get('__operator_context'): item['operatorContext'] = env['__operator_context']
                if not identity: item['dynamicReason'] = 'operator id is computed at runtime'
                return OperatorReference(item)
            if name in {'menu', 'menu_contents'}:
                return self.item('menu', identity, label, node, env, conditions, uiMethod=name)
            if name.startswith('prop'):
                prop = values[1] if len(values) > 1 and isinstance(values[1], str) else ''
                target = self.expression(node.args[0], env) if node.args else ''
                item = self.item('property', prop, label or _humanize(prop), node, env, conditions, uiMethod=name,
                                 target=target, property=prop, propertyId=prop, resolved=bool(prop and target),
                                 propertyExpressions={})
                if name == 'prop_enum' and len(values) > 2 and self.literal(values[2]): item['value'] = values[2]
                item['uiArguments'] = {key: value for key, value in kwargs.items() if self.literal(value)}
                return UNKNOWN
            if name == 'label':
                self.item('label', '', label, node, env, conditions, resolved=True)
                return UNKNOWN
            self.item('dynamic', identity, label or name, node, env, conditions, resolved=False, uiMethod=name,
                      dynamicReason='template/popover expands from runtime data')
            return UNKNOWN
        function = None; implicit = []
        if isinstance(node.func, ast.Name):
            function = self.functions.get((module, node.func.id))
            if not function:
                imported = self.files[module]['imports'].get(node.func.id)
                if imported: function = self.functions.get(imported)
        elif isinstance(node.func, ast.Attribute) and isinstance(node.func.value, ast.Name):
            parent = node.func.value.id
            if parent in {'self', 'cls'}:
                function = self.method(owner, node.func.attr); implicit = [Symbol(parent)]
            elif parent in self.classes:
                function = self.method(parent, node.func.attr)
        if function:
            args = implicit + [self.value(arg, env, owner) for arg in node.args]
            self.walk_function(function, env, args, conditions, owner)
            return UNKNOWN
        # Capture a reference to a dynamic draw helper so omitted branches are visible.
        expression = ast.unparse(node.func)
        if ('draw' in expression or 'template_' in expression) and not expression.startswith(('bpy.ops.', 'isinstance')):
            self.add_dynamic(node, env, conditions, 'dynamic draw helper: ' + expression)
        for child in ast.iter_child_nodes(node):
            if isinstance(child, ast.Call): self.call(child, env, conditions, owner, module)
        return self.value(node, env, owner)

    def bind(self, target, value, env, conditions, owner, module):
        if isinstance(target, ast.Name): env[target.id] = value
        elif isinstance(target, (ast.Tuple, ast.List)):
            if isinstance(value, (tuple, list)) and len(value) == len(target.elts):
                for child, v in zip(target.elts, value): self.bind(child, v, env, conditions, owner, module)
        elif isinstance(target, ast.Attribute):
            reference = self.call(target.value, env, conditions, owner, module) if isinstance(target.value, ast.Call) else self.value(target.value, env, owner)
            if isinstance(reference, OperatorReference):
                if self.literal(value): reference.item['properties'][target.attr] = value
                elif isinstance(value, Symbol): reference.item.setdefault('propertyExpressions', {})[target.attr] = value.expression
                else: reference.item.setdefault('propertyExpressions', {})[target.attr] = 'computed at runtime'
            elif target.attr == 'operator_context' and isinstance(value, str): env['__operator_context'] = value

    def walk_block(self, nodes, env, conditions, owner, module):
        for node in nodes:
            if self.calls > 12000: break
            if isinstance(node, (ast.Assign, ast.AnnAssign)):
                raw = node.value
                value = self.call(raw, env, conditions, owner, module) if isinstance(raw, ast.Call) else self.value(raw, env, owner)
                if value is UNKNOWN: value = Symbol(self.expression(raw, env)) if raw is not None else UNKNOWN
                targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                for target in targets: self.bind(target, value, env, conditions, owner, module)
            elif isinstance(node, ast.Expr) and isinstance(node.value, ast.Call): self.call(node.value, env, conditions, owner, module)
            elif isinstance(node, ast.If):
                condition = self.expression(node.test, env)
                self.walk_block(node.body, dict(env), conditions + [condition], owner, module)
                self.walk_block(node.orelse, dict(env), conditions + ['not (' + condition + ')'], owner, module)
            elif isinstance(node, (ast.For, ast.AsyncFor)):
                values = self.value(node.iter, env, owner)
                if isinstance(values, (list, tuple)) and len(values) <= 200:
                    for v in values:
                        branch = dict(env); self.bind(node.target, v, branch, conditions, owner, module)
                        self.walk_block(node.body, branch, conditions, owner, module)
                else:
                    branch = dict(env); self.bind(node.target, Symbol(ast.unparse(node.target)), branch, conditions, owner, module)
                    self.walk_block(node.body, branch, conditions + ['for ' + ast.unparse(node.target) + ' in ' + self.expression(node.iter, env)], owner, module)
                self.walk_block(node.orelse, dict(env), conditions, owner, module)
            elif isinstance(node, (ast.With, ast.AsyncWith)):
                self.walk_block(node.body, dict(env), conditions, owner, module)
            elif isinstance(node, ast.Try):
                self.walk_block(node.body, dict(env), conditions, owner, module)
                for handler in node.handlers: self.walk_block(handler.body, dict(env), conditions + ['exception branch'], owner, module)
                self.walk_block(node.orelse + node.finalbody, dict(env), conditions, owner, module)
            elif isinstance(node, ast.While): self.walk_block(node.body, dict(env), conditions + [self.expression(node.test, env)], owner, module)
            elif isinstance(node, ast.Return) and isinstance(node.value, ast.Call): self.call(node.value, env, conditions, owner, module)

    def run(self):
        menus, surfaces = [], []
        for name, info in sorted(self.classes.items()):
            lineage = self.lineage(name)
            kind = 'menu' if 'Menu' in lineage else 'header' if 'Header' in lineage else 'panel' if 'Panel' in lineage else None
            if not kind: continue
            item = self.extract_surface(name, info, kind)
            (menus if kind == 'menu' else surfaces).append(item)
        source_classes = {m['className'] for m in menus}
        for name, metadata in sorted(self.registered_menus.items()):
            if name in source_classes: continue
            label = metadata['name'] or metadata['rnaName'] or _humanize(name)
            thai, provenance = self.translated(label)
            menus.append({'id': name, 'className': name, 'kind': 'menu', 'name': label, 'thaiName': thai,
                          'nameTranslationSource': provenance, 'registered': True, 'source': None, 'context': {},
                          'items': [{'kind': 'dynamic', 'id': '', 'label': label, 'thaiLabel': thai, 'resolved': False,
                                     'properties': {}, 'sourceLine': 0, 'expression': name + '.draw', 'condition': '',
                                     'dynamicReason': 'registered menu has no source class in selected bl_ui files'}]})
        all_items = [i for m in menus + surfaces for i in m['items']]
        source_menu_ids = sorted(source_classes)
        counts = {'menus': len(menus), 'sourceMenus': len(source_classes), 'registeredMenus': len(self.registered_menus),
                  'headers': sum(s['kind'] == 'header' for s in surfaces), 'panels': sum(s['kind'] == 'panel' for s in surfaces),
                  'menuActions': sum(len(m['items']) for m in menus), 'surfaceActions': sum(len(m['items']) for m in surfaces),
                  'actions': len(all_items), 'actionKinds': dict(Counter(i['kind'] for i in all_items)),
                  'resolvedOperatorActions': sum(i['kind'] == 'operator' and i.get('resolved', False) for i in all_items),
                  'dynamicOperatorIds': sum(i['kind'] == 'operator' and not i.get('resolved', False) for i in all_items),
                  'unavailableOperatorActions': sum(i['kind'] == 'operator' and i.get('resolved') and not i.get('apiAvailable') for i in all_items)}
        coverage = {'kind': 'shipped-Python-AST-inspection-and-optional-registered-Menu-metadata-not-native-REA-Evidence',
                    'sourceRoot': str(self.source_root), 'sourceFiles': self.file_hashes,
                    'sourceMenuClasses': source_menu_ids, 'registeredMenuClasses': sorted(self.registered_menus),
                    'sourceMenuCoverage': 'All statically declared Menu subclasses (including inheritance) in every direct *.py of selected bl_ui directory.',
                    'expanded': 'Literal UI draw actions and resolvable local/imported Python draw helpers; both conditional branches retained. No operators or draw callbacks executed.',
                    'notExpanded': 'Runtime-generated assets, context data, native templates/popovers, computed operator IDs, extension menus outside selected source root, callbacks appended dynamically, every possible runtime branch value.',
                    'nativeDecompilation': False, 'executedOperators': 0, 'errors': self.errors}
        return {'schemaVersion': 1, 'generatedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                'blender': self.blender, 'counts': counts, 'menus': menus, 'surfaces': surfaces, 'coverage': coverage}


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description='Inspect installed Blender UI source and map menus to code metadata')
    parser.add_argument('--source-root')
    parser.add_argument('--api-catalog', default=str(root / 'engine' / 'blender_api.json'))
    parser.add_argument('--output', default=str(root / 'engine' / 'blender_menus.json'))
    parser.add_argument('--coverage-output', default=str(root / 'docs' / 'rea' / 'menu-coverage.json'))
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else None)
    if args.source_root: source_root = Path(args.source_root)
    else:
        api = json.loads(Path(args.api_catalog).read_text(encoding='utf-8'))
        binary_dir = Path(api['blender']['binaryPath']).parent
        paths = sorted(binary_dir.glob('*/scripts/startup/bl_ui'))
        if not paths: raise FileNotFoundError('No shipped Blender bl_ui directory')
        source_root = paths[-1]
    data = MenuExtractor(source_root, Path(args.api_catalog)).run()
    target = Path(args.output); target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    report = Path(args.coverage_output); report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps({'schemaVersion': 1, 'blender': data['blender'], 'counts': data['counts'], 'coverage': data['coverage']}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'output': str(target), 'counts': data['counts'], 'errors': len(data['coverage']['errors'])}, ensure_ascii=True, indent=2), flush=True)


if __name__ == '__main__': main()
