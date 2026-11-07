"""Execute Miti Thai Studio actions inside ZBrush's documented Python bridge.

The WinForms application writes a JSON job and launches this script using
ZBrush.exe -script. This file uses only ZBrush APIs.
"""

import json
import os
import traceback
from pathlib import Path


def execute_action(zbc, action):
    mode = action["mode"]
    path = str(action.get("path") or "").strip()
    if mode in {"press", "set", "toggle", "import", "export"}:
        if not path:
            raise ValueError("ZBrush UI path is empty")
        if not zbc.exists(path):
            raise ValueError("ZBrush menu is unavailable: " + path)
    if mode == "press":
        zbc.press(path)
    elif mode == "set":
        zbc.set(path, float(action["value"]))
    elif mode == "toggle":
        zbc.toggle(path)
    elif mode == "import":
        source = Path(action["file"])
        if not source.is_file() or source.stat().st_size == 0:
            raise FileNotFoundError(source)
        zbc.set_next_filename(str(source))
        zbc.press(path)
    elif mode == "export":
        destination = Path(action["file"])
        destination.parent.mkdir(parents=True, exist_ok=True)
        zbc.set_next_filename(str(destination))
        zbc.press(path)
        if not destination.is_file() or destination.stat().st_size == 0:
            raise RuntimeError("ZBrush did not create the export file: " + str(destination))
    elif mode == "select_subtool":
        index = int(action["index"])
        count = zbc.get_subtool_count()
        if index < 0 or index >= count:
            raise IndexError("SubTool index %d is outside 0..%d" % (index, count - 1))
        zbc.select_subtool(index)
    elif mode == "color":
        color = action.get("color") or [255, 255, 255]
        if len(color) != 3 or any(float(channel) < 0 or float(channel) > 255 for channel in color):
            raise ValueError("Color needs R, G, B values from 0 to 255")
        zbc.set_color(*(int(round(float(channel))) for channel in color))
    elif mode == "transform":
        current = list(zbc.get_transform())
        if len(current) < 9:
            raise RuntimeError("ZBrush returned an incomplete transform")
        move = action.get("move") or [0, 0, 0]
        rotate = action.get("rotate") or [0, 0, 0]
        scale = action.get("scale") or [1, 1, 1]
        if len(move) != 3 or len(rotate) != 3 or len(scale) != 3:
            raise ValueError("Transform needs three values per vector")
        values = [current[i] + float(move[i]) for i in range(3)]
        values.extend(current[i + 3] * float(scale[i]) for i in range(3))
        values.extend(current[i + 6] + float(rotate[i]) for i in range(3))
        zbc.set_transform(*values)
    else:
        raise ValueError("Unsupported ZBrush action: " + mode)


def main():
    job_path = Path(os.environ["MITI_ZBRUSH_JOB"])
    job = json.loads(job_path.read_text(encoding="utf-8"))
    result_path = Path(job["result"])
    try:
        from zbrush import commands as zbc

        completed = 0
        for action in job["actions"]:
            execute_action(zbc, action)
            completed += 1
        result = {"ok": True, "completed": completed}
        try:
            result["activeTool"] = zbc.get_active_tool_path()
            result["subtoolCount"] = zbc.get_subtool_count()
        except Exception:
            pass
    except Exception as exc:
        result = {"ok": False, "error": str(exc), "completed": locals().get("completed", 0),
                  "traceback": traceback.format_exc()}
        result_path.write_text(json.dumps(result, ensure_ascii=False), encoding="utf-8")
        raise
    result_path.write_text(json.dumps(result, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    main()
