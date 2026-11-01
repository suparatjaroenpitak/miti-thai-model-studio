# Executed as source by engine/runner.py --window-context in a separate Blender.
assert not bpy.app.background
assert bpy.context.window is not None
assert bpy.context.area.type == 'VIEW_3D'
assert bpy.context.preferences.view.use_translate_interface
คำสั่ง('bpy.ops.view3d.view_axis', type='FRONT')
กล่อง('ทดสอบบริบทหน้าต่าง', location=(0, 0, 1))
print('WINDOW_CONTEXT_SMOKE_PASS')
