"""Apply Thai UI translations to this Blender process only; never save preferences."""
from pathlib import Path
import json
import os
import bpy


def configure_thai_interface():
    view = bpy.context.preferences.view
    prop = view.bl_rna.properties.get('language')
    available = [item.identifier for item in prop.enum_items] if prop else []
    language = next((value for value in available if value.startswith('th')), None)
    if language:
        view.language = language
    view.use_translate_interface = True
    view.use_translate_tooltips = True
    catalog = Path(os.environ.get('MITI_API_CATALOG') or Path(__file__).with_name('blender_api.json'))
    if not catalog.is_file():
        return
    data = json.loads(catalog.read_text(encoding='utf-8'))
    labels = {}
    def add(item):
        context = item.get('translationContext') or '*'
        if item.get('name') and item.get('thaiName'):
            labels[(context, item['name'])] = item['thaiName']
        # A generated summary is not an equivalent translation of a tooltip.
        if item.get('description') and item.get('thaiDescription') and item.get('descriptionTranslationSource') == 'official':
            labels[(context, item['description'])] = item['thaiDescription']
        for parameter in item.get('parameters', []):
            add(parameter)
        for choice in item.get('enumItems', []):
            add(choice)
    for entry in data.get('entries', []):
        add(entry)
    try:
        bpy.app.translations.unregister('miti_studio_thai')
    except (ValueError, RuntimeError):
        pass
    bpy.app.translations.register('miti_studio_thai', {language or 'th_TH': labels, 'th_TH': labels})


if __name__ == '__main__':
    configure_thai_interface()
