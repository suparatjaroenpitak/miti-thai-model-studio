"""Blender background worker for Thai Model Studio. No connection to a live scene."""
import argparse
import json
import math
import re
import os
from pathlib import Path
import sys
import traceback

import bpy

sys.path.insert(0, str(Path(__file__).resolve().parent))
import thai_model
import thai_api
import thai_workspace

PREVIEW_TRIANGLES = 60000


def log(message):
    print(message, flush=True)


def get_color(obj, index=0):
    fallback = [0.38, 0.65, 0.86, 1.0]
    if index >= len(obj.material_slots):
        return fallback
    mat = obj.material_slots[index].material
    if mat is None:
        return fallback
    if mat.use_nodes and mat.node_tree:
        node = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if node:
            return list(node.inputs['Base Color'].default_value)
    return list(mat.diffuse_color)


def write_preview(path):
    bpy.context.view_layer.update()
    deps = bpy.context.evaluated_depsgraph_get()
    entries = []
    triangle_total = 0
    for obj in list(bpy.context.scene.objects):
        if obj.type not in {'MESH', 'CURVE', 'SURFACE', 'FONT', 'META'} or obj.hide_viewport or obj.hide_render:
            continue
        evaluated = obj.evaluated_get(deps)
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            triangle_total += len(mesh.loop_triangles)
            entries.append((obj, mesh.copy(), len(mesh.loop_triangles)))
        finally:
            evaluated.to_mesh_clear()

    objects = []
    simplified = False
    try:
        for obj, mesh, count in entries:
            temporary = None
            evaluated_temp = None
            active_mesh = mesh
            # Collapse a private temporary mesh, keeping full geometry in the .blend.
            if triangle_total > PREVIEW_TRIANGLES and count > 16:
                budget = max(12, int(PREVIEW_TRIANGLES * count / triangle_total))
                temporary = bpy.data.objects.new('__preview_only__', mesh)
                bpy.context.scene.collection.objects.link(temporary)
                modifier = temporary.modifiers.new('preview_decimate', 'DECIMATE')
                modifier.ratio = min(1.0, budget / max(1, count))
                deps.update()
                evaluated_temp = temporary.evaluated_get(deps)
                active_mesh = evaluated_temp.to_mesh()
                simplified = True
            try:
                active_mesh.calc_loop_triangles()
                matrix = obj.matrix_world
                vertices = [[round(float(v), 6) for v in matrix @ vert.co] for vert in active_mesh.vertices]
                triangles = [list(t.vertices) for t in active_mesh.loop_triangles]
                colors = [get_color(obj, t.material_index) for t in active_mesh.loop_triangles]
                objects.append({'name': obj.name, 'vertices': vertices, 'triangles': triangles,
                                'color': get_color(obj), 'triangleColors': colors})
            finally:
                if evaluated_temp is not None:
                    evaluated_temp.to_mesh_clear()
                if temporary is not None:
                    bpy.data.objects.remove(temporary, do_unlink=True)
    finally:
        for _, mesh, _ in entries:
            if mesh.users == 0:
                bpy.data.meshes.remove(mesh)
    data = {'objects': objects, 'simplified': simplified, 'sourceTriangleCount': triangle_total}
    path.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':'), allow_nan=False), encoding='utf-8')
    log('Preview: %d objects / %d source triangles%s' % (len(objects), triangle_total, ' (simplified preview)' if simplified else ''))


def export_file(destination, kind):
    destination = Path(destination).resolve()
    destination.parent.mkdir(parents=True, exist_ok=True)
    if kind == 'blend':
        # A background build has no screenshot; avoid writing Blender thumbnails
        # outside the project as a side effect of saving a scene.
        preview_property = bpy.context.preferences.filepaths.bl_rna.properties.get('file_preview_type')
        if preview_property and 'NONE' in {item.identifier for item in preview_property.enum_items}:
            bpy.context.preferences.filepaths.file_preview_type = 'NONE'
        bpy.ops.wm.save_as_mainfile(filepath=str(destination), check_existing=False)
    elif kind == 'glb':
        bpy.ops.export_scene.gltf(filepath=str(destination), export_format='GLB', use_visible=True, export_apply=True)
    elif kind == 'obj':
        for obj in bpy.context.scene.objects:
            obj.select_set(obj.type == 'MESH' and not obj.hide_viewport and not obj.hide_render)
        bpy.ops.wm.obj_export(filepath=str(destination), export_selected_objects=True, apply_modifiers=True)
    elif kind == 'stl':
        for obj in bpy.context.scene.objects:
            obj.select_set(obj.type == 'MESH' and not obj.hide_viewport and not obj.hide_render)
        bpy.ops.wm.stl_export(filepath=str(destination), export_selected_objects=True, apply_modifiers=True)
    else:
        raise ValueError('Unsupported export format: ' + kind)
    if not destination.is_file() or destination.stat().st_size == 0:
        raise RuntimeError('Export produced no file: ' + str(destination))
    log('Exported: ' + str(destination))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source')
    parser.add_argument('--output')
    parser.add_argument('--export')
    parser.add_argument('--format', choices=['blend', 'glb', 'obj', 'stl'])
    parser.add_argument('--window-context', action='store_true')
    parser.add_argument('--preview-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    if args.export:
        export_file(args.export, args.format)
        return
    if not args.source or not args.output:
        parser.error('--source and --output are required for building')
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    source_path = Path(args.source).resolve()
    source = source_path.read_text(encoding='utf-8-sig')
    # Blender 4.x used BLENDER_EEVEE_NEXT; Blender 5.2 exposes BLENDER_EEVEE.
    # Normalize this compatibility spelling before executing user code.
    if 'BLENDER_EEVEE_NEXT' in source:
        source = source.replace('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE')
    # Blender 5.x collection membership accepts names, not ID objects.
    # Keep older scripts such as ``obj not in collection.objects`` working.
    source = re.sub(r"(\b\w+)\s+not\s+in\s+([A-Za-z_][\w\[\]'\"]*\.objects)", r"\1.name not in _miti_collection_names(\2)", source)
    source = re.sub(r"(\b\w+)\s+in\s+([A-Za-z_][\w\[\]'\"]*\.objects)", r"\1.name in _miti_collection_names(\2)", source)
    thai_model.clear()
    namespace = {'__name__': '__main__', '__file__': str(source_path), 'bpy': bpy, 'math': math,
                 '_miti_collection_names': lambda collection: {item.name for item in collection}}
    namespace.update({name: getattr(thai_model, name) for name in thai_model.__all__})
    namespace.update({name: getattr(thai_api, name) for name in thai_api.__all__})
    namespace.update({name: getattr(thai_workspace, name) for name in thai_workspace.__dict__ if not name.startswith('_')})
    log('Building model...')
    exec(compile(source, str(source_path), 'exec'), namespace, namespace)
    bpy.context.view_layer.update()
    write_preview(output / 'preview.json')
    if args.preview_only:
        (output / 'preview-success.json').write_text(json.dumps({'success': True}), encoding='utf-8')
        log('PREVIEW_SUCCESS')
        return
    export_file(output / 'scene.blend', 'blend')
    export_file(output / 'scene.glb', 'glb')
    # Written last: consumers must never mistake partial or failed output for success.
    (output / 'build-success.json').write_text(json.dumps({'success': True}), encoding='utf-8')
    log('BUILD_SUCCESS')


if __name__ == '__main__':
    def run_checked():
        try:
            main()
        except BaseException:
            traceback.print_exc()
            sys.stdout.flush()
            sys.stderr.flush()
            # Blender's default process exit code is 0 for Python exceptions.
            os._exit(1)

    if '--window-context' in sys.argv and not bpy.app.background:
        def run_in_window():
            try:
                from thai_interface import configure_thai_interface
                configure_thai_interface()
                window = bpy.context.window or next(iter(bpy.context.window_manager.windows), None)
                if window is None:
                    raise RuntimeError('Blender ยังไม่มีหน้าต่างสำหรับคำสั่งนี้')
                area = next((area for area in window.screen.areas if area.type == 'VIEW_3D'), None)
                if area is None:
                    raise RuntimeError('ไม่พบพื้นที่มุมมองสามมิติในหน้าต่าง Blender')
                region = next(region for region in area.regions if region.type == 'WINDOW')
                with bpy.context.temp_override(window=window, area=area, region=region):
                    run_checked()
                bpy.ops.wm.quit_blender()
            except BaseException:
                traceback.print_exc()
                sys.stdout.flush()
                sys.stderr.flush()
                os._exit(1)
            return None
        bpy.app.timers.register(run_in_window, first_interval=0.8)
    else:
        run_checked()
