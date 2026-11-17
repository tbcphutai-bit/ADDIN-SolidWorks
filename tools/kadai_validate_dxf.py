"""Validate ASCII prototype DXF line bounds and notch widths against measured profiles.
This checks geometry/scale, not machining certification or insertion feasibility.
Usage: python kadai_validate_dxf.py profiles.json package_folder
"""
import json
import math
import pathlib
import sys
from collections import Counter


def entities(path):
    lines = path.read_text(encoding="cp932", errors="replace").splitlines()
    pairs = [(int(lines[i]), lines[i + 1].strip()) for i in range(0, len(lines) - 1, 2)]
    result, current, active = [], None, False
    for i, (code, value) in enumerate(pairs):
        if code == 2 and value == "ENTITIES":
            active = True
            continue
        if not active:
            continue
        if code == 0:
            if current:
                result.append(current)
            if value == "ENDSEC":
                break
            current = {"kind": value, "groups": []}
        elif current:
            current["groups"].append((code, value))
    return result


profiles = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
folder = pathlib.Path(sys.argv[2])
report = []
for profile in profiles:
    path = folder / (profile["RibId"] + ".dxf")
    data = entities(path)
    assert data and all(e["kind"] == "LINE" for e in data), "Expected prototype line contours"
    points, vertical = [], []
    for entity in data:
        group = dict(entity["groups"])
        a = (float(group[10]), float(group[20]))
        b = (float(group[11]), float(group[21]))
        points.extend((a, b))
        if abs(a[0] - b[0]) < 1e-5:
            vertical.append(a[0])
    size = [max(p[k] for p in points) - min(p[k] for p in points) for k in range(2)]
    along, height = profile["AlongDirection"], profile["HeightDirection"]
    world = profile["PointsWorldMeters"]
    project = lambda p, axis: sum(a * b for a, b in zip(p, axis))
    expected = [(max(project(p, axis) for p in world) - min(project(p, axis) for p in world)) * 1000
                for axis in (along, height)]
    assert all(math.isclose(a, b, abs_tol=0.001) for a, b in zip(sorted(size), sorted(expected))), (size, expected)
    xs = sorted(set(round(x, 5) for x in vertical))
    slots = sum(math.isclose(b - a, 1.8, abs_tol=0.001) for a, b in zip(xs, xs[1:]))
    expected_slots = 1 if profile["Direction"] == 0 else 3
    assert slots == expected_slots, (profile["RibId"], slots, expected_slots)
    report.append({"rib": profile["RibId"], "size_mm": size, "expected_mm": expected,
                   "slot_width_mm": 1.8, "slots": slots, "entity_types": dict(Counter(e["kind"] for e in data))})
print(json.dumps(report, indent=2))
print("PASS: four DXFs at mm 1:1; prototype notch widths 1.8 mm.")
