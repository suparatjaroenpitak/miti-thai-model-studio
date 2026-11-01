"""Read-only API discovery plus isolated factory-startup modeling/export probe.

Run with installed Blender --background --factory-startup --python this_file.
Never attaches to or modifies an existing interactive Blender session.
"""
import bpy
import hashlib
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
ARTIFACTS = ROOT / "runtime-artifacts"
ARTIFACTS.mkdir(exist_ok=True)
# Suppress thumbnail generation outside this isolated evidence directory.
preview_property = bpy.context.preferences.filepaths.bl_rna.properties.get("file_preview_type")
if preview_property and "NONE" in {i.identifier for i in preview_property.enum_items}:
    bpy.context.preferences.filepaths.file_preview_type = "NONE"


def op_available(operator):
    try:
        namespace, name = operator.split(".")
        getattr(getattr(bpy.ops, namespace), name).get_rna_type()
        return True
    except Exception:
        return False


def mesh_counts(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    mesh.calc_loop_triangles()
    result = {"vertices": len(mesh.vertices), "triangles": len(mesh.loop_triangles)}
    evaluated.to_mesh_clear()
    return result


report = {
    "kind": "ordinary-shell-controlled-runtime-observation-not-REA-Evidence",
    "executable": bpy.app.binary_path,
    "version": bpy.app.version_string,
    "version_tuple": list(bpy.app.version),
    "build_branch": bpy.app.build_branch.decode(errors="replace"),
    "build_hash": bpy.app.build_hash.decode(errors="replace"),
    "background": bpy.app.background,
    "file_preview_type": bpy.context.preferences.filepaths.file_preview_type,
    "python": sys.version,
    "operations": {},
    "modifier_types": [],
    "modeling_tests": {},
    "exports": {},
}

for operator in [
    "mesh.primitive_cube_add", "mesh.primitive_uv_sphere_add",
    "mesh.primitive_cylinder_add", "mesh.primitive_cone_add",
    "mesh.primitive_torus_add", "mesh.primitive_plane_add",
    "mesh.primitive_monkey_add", "object.modifier_apply",
    "object.shade_smooth", "object.duplicate", "object.join",
    "export_scene.gltf", "wm.obj_export", "wm.stl_export",
    "wm.save_as_mainfile", "wm.open_mainfile",
]:
    report["operations"][operator] = op_available(operator)

report["modifier_types"] = [
    i.identifier for i in bpy.types.Modifier.bl_rna.properties["type"].enum_items
]

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.mesh.primitive_cube_add(size=2, location=(0, 0, 0))
cube = bpy.context.object
cube.name = "REA_isolated_cube"
report["modeling_tests"]["cube"] = mesh_counts(cube)
bevel = cube.modifiers.new(name="REA_probe_bevel", type="BEVEL")
bevel.width = 0.15
bevel.segments = 3
report["modeling_tests"]["bevel_cube"] = mesh_counts(cube)

material = bpy.data.materials.new("REA_probe_material")
material.use_nodes = True
principled = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
principled.inputs["Base Color"].default_value = (0.1, 0.5, 0.8, 1)
cube.data.materials.append(material)
report["modeling_tests"]["material"] = {"node_type": principled.type, "base_color": list(principled.inputs["Base Color"].default_value)}

for kind, filename in [("blend", "probe.blend"), ("glb", "probe.glb"), ("obj", "probe.obj"), ("stl", "probe.stl")]:
    destination = ARTIFACTS / filename
    try:
        if kind == "blend":
            bpy.ops.wm.save_as_mainfile(filepath=str(destination), check_existing=False)
        elif kind == "glb":
            bpy.ops.export_scene.gltf(filepath=str(destination), export_format="GLB", export_apply=True)
        elif kind == "obj":
            bpy.ops.wm.obj_export(filepath=str(destination), apply_modifiers=True)
        elif kind == "stl":
            bpy.ops.wm.stl_export(filepath=str(destination), apply_modifiers=True)
        payload = destination.read_bytes()
        report["exports"][kind] = {"ok": True, "bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest(), "path": str(destination)}
    except Exception as exc:
        report["exports"][kind] = {"ok": False, "error": repr(exc)}

(ROOT / "runtime-probe.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
print("REA_RUNTIME_PROBE_COMPLETED " + str(ROOT / "runtime-probe.json"))
