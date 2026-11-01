import bpy
for p in [bpy.data,bpy.context,bpy.data.objects,bpy.data.materials,bpy.context.scene,bpy.data.objects.new]:
 print(type(p), 'bl_rna', getattr(getattr(p,'bl_rna',None),'identifier',None), 'rna_type',getattr(getattr(p,'rna_type',None),'identifier',None),'self',getattr(p,'__self__',None))
prop=bpy.types.BlendData.bl_rna.properties['objects']
print('property',prop, 'srna',getattr(prop,'srna',None),'fixed_type',prop.fixed_type)
print('fn',bpy.types.BlendDataObjects.bl_rna.functions['new'].parameters.keys())
