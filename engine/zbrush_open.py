"""Import one Blender OBJ into an installed ZBrush through Maxon's Python API.

Invoked by ZBrush.exe -script. The job path arrives through MITI_ZBRUSH_JOB so
ZBrush does not mistake an additional command-line argument for a ZPR project.
"""

import json
import os
import traceback
from pathlib import Path


def main():
    job_path = Path(os.environ["MITI_ZBRUSH_JOB"])
    job = json.loads(job_path.read_text(encoding="utf-8"))
    source = Path(job["source"])
    result = Path(job["result"])
    try:
        if not source.is_file() or source.stat().st_size == 0:
            raise FileNotFoundError(source)
        from zbrush import commands as zbc

        if not zbc.exists("Tool:Import"):
            raise RuntimeError("ZBrush Tool:Import is unavailable in this context")
        zbc.set_next_filename(str(source))
        zbc.press("Tool:Import")
        result.write_text(json.dumps({"ok": True, "source": str(source)}, ensure_ascii=False), encoding="utf-8")
    except Exception as exc:
        result.write_text(json.dumps({"ok": False, "error": str(exc), "traceback": traceback.format_exc()},
                                     ensure_ascii=False), encoding="utf-8")
        raise


if __name__ == "__main__":
    main()
