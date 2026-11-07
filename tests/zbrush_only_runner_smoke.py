"""Check ZBrush-only runner with a stand-in for Maxon's Python API."""
import importlib.util
import json
import os
import sys
import tempfile
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("miti_zbrush_runner", ROOT / "zbrush" / "runner.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

class FakeCommands:
    def __init__(self):
        self.calls = []
        self.selected_file = None
    def exists(self, path): return path in {"Tool:Geometry:Divide", "Draw:Draw Size", "Tool:Import", "Tool:Export", "Transform:Edit"}
    def press(self, path):
        self.calls.append(("press", path))
        if path == "Tool:Export": Path(self.selected_file).write_text("o Test\nv 0 0 0\n", encoding="utf-8")
    def set(self, path, value): self.calls.append(("set", path, value))
    def toggle(self, path): self.calls.append(("toggle", path))
    def set_next_filename(self, path): self.selected_file = path; self.calls.append(("file", path))
    def get_subtool_count(self): return 3
    def select_subtool(self, index): self.calls.append(("subtool", index))
    def get_transform(self): return [10, 20, 30, 100, 100, 100, 0, 0, 0]
    def set_transform(self, *values): self.calls.append(("transform", values))
    def set_color(self, *values): self.calls.append(("color", values))
    def get_active_tool_path(self): return "Tool:Demo"

fake = FakeCommands()
with tempfile.TemporaryDirectory() as folder:
    directory = Path(folder)
    obj = directory / "input.obj"; obj.write_text("o Input\nv 0 0 0\n", encoding="utf-8")
    exported = directory / "output.obj"
    actions = [
        {"mode": "press", "path": "Tool:Geometry:Divide"},
        {"mode": "set", "path": "Draw:Draw Size", "value": 42},
        {"mode": "toggle", "path": "Transform:Edit"},
        {"mode": "import", "path": "Tool:Import", "file": str(obj)},
        {"mode": "export", "path": "Tool:Export", "file": str(exported)},
        {"mode": "select_subtool", "index": 2},
        {"mode": "color", "color": [10, 20, 30]},
        {"mode": "transform", "move": [1, 2, 3], "rotate": [0, 90, 0], "scale": [2, 1, 1]},
    ]
    job_path = directory / "job.json"; result_path = directory / "result.json"
    job_path.write_text(json.dumps({"actions": actions, "result": str(result_path)}), encoding="utf-8")
    os.environ["MITI_ZBRUSH_JOB"] = str(job_path)
    package = types.ModuleType("zbrush"); package.commands = fake
    sys.modules["zbrush"] = package
    runner.main()
    result = json.loads(result_path.read_text(encoding="utf-8"))
    assert result["ok"] is True and result["completed"] == 8, result
    assert exported.exists() and exported.stat().st_size > 0
    assert fake.calls[-1] == ("transform", (11.0, 22.0, 33.0, 200.0, 100.0, 100.0, 0.0, 90.0, 0.0)), fake.calls[-1]
    try:
        runner.execute_action(fake, {"mode": "press", "path": "Missing:Button"})
    except ValueError as error:
        assert "unavailable" in str(error)
    else:
        raise AssertionError("Missing menu did not fail")
print("ZBRUSH_ONLY_RUNNER_PASS")
