"""Check the documented ZBrush API handoff protocol with a stand-in API.

This does not claim that installed ZBrush can start on the test machine.
"""
import json
import os
import runpy
import sys
import tempfile
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory() as folder:
    directory = Path(folder)
    source = directory / "selected.obj"
    source.write_text("o Selected\nv 0 0 0\n", encoding="utf-8")
    result = directory / "result.json"
    job = directory / "job.json"
    job.write_text(json.dumps({"source": str(source), "result": str(result)}), encoding="utf-8")
    os.environ["MITI_ZBRUSH_JOB"] = str(job)
    calls = []
    api = types.ModuleType("zbrush.commands")
    api.exists = lambda path: path == "Tool:Import"
    api.set_next_filename = lambda path: calls.append(("filename", path))
    api.press = lambda path: calls.append(("press", path))
    package = types.ModuleType("zbrush")
    package.commands = api
    sys.modules["zbrush"] = package
    sys.modules["zbrush.commands"] = api
    runpy.run_path(str(ROOT / "engine" / "zbrush_open.py"), run_name="__main__")
    assert calls == [("filename", str(source)), ("press", "Tool:Import")], calls
    assert json.loads(result.read_text(encoding="utf-8"))["ok"] is True
print("ZBRUSH_NATIVE_BRIDGE_PROTOCOL_PASS")
