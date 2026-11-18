"""Validate ASCII prototype DXF line bounds and notch widths against measured profiles.
This checks geometry/scale, not machining certification or insertion feasibility.
Usage: python kadai_validate_dxf.py profiles.json package_folder
"""
import json
import csv
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
joint_plan = folder / "JointPlan.csv"
joint_rows = []
if joint_plan.exists():
    with joint_plan.open(encoding="utf-8-sig", newline="") as stream:
        joint_rows = list(csv.DictReader(stream))
report = []
for profile in profiles:
    path = folder / (profile["RibId"] + ".dxf")
    data = entities(path)
    assert data and all(e["kind"] == "LINE" for e in data), "Expected prototype line contours"
    points, vertical, segments = [], [], []
    for entity in data:
        group = dict(entity["groups"])
        a = (float(group[10]), float(group[20]))
        b = (float(group[11]), float(group[21]))
        points.extend((a, b))
        segments.append((a, b))
        if abs(a[0] - b[0]) < 1e-5:
            vertical.append(a[0])
    size = [max(p[k] for p in points) - min(p[k] for p in points) for k in range(2)]
    along, height = profile["AlongDirection"], profile["HeightDirection"]
    world = profile["PointsWorldMeters"]
    project = lambda p, axis: sum(a * b for a, b in zip(p, axis))
    expected = [(max(project(p, axis) for p in world) - min(project(p, axis) for p in world)) * 1000
                for axis in (along, height)]
    assert all(math.isclose(a, b, abs_tol=0.001) for a, b in zip(sorted(size), sorted(expected))), (size, expected)
    if joint_plan.exists():
        rows = [row for row in joint_rows if row["RibId"] == profile["RibId"]]
        expected_slots = sum(p["Direction"] != profile["Direction"] for p in profiles)
        assert len(rows) == expected_slots, (profile["RibId"], len(rows), expected_slots)
        assert all(math.isclose(a, b, abs_tol=.001) for a, b in zip(size, expected)), (size, expected)
        normalized = {tuple(round(v, 5) for v in point) for point in points}
        assert len(normalized) == len(segments), path
        degree = Counter(tuple(round(v, 5) for v in point) for point in points)
        assert set(degree.values()) == {2}, path

        def near(a, b):
            return math.isclose(a, b, abs_tol=.001)

        # Front-plane export places the local height on negative DXF Y.
        widths = []
        for row in rows:
            left, right = float(row["LeftMm"]), float(row["RightMm"])
            join = -float(row["TopMm"] if profile["Direction"] == 0 else row["BottomMm"])
            left += min(p[0] for p in points)
            right += min(p[0] for p in points)
            join += max(p[1] for p in points)
            assert any(near(a[1], join) and near(b[1], join)
                       and near(min(a[0], b[0]), left) and near(max(a[0], b[0]), right)
                       for a, b in segments), (path, row["SlotId"], "missing full-width slot seat", left, right, join)
            for side in (left, right):
                assert any(near(a[0], side) and near(b[0], side)
                           and (near(a[1], join) or near(b[1], join))
                           and (max(a[1], b[1]) > join + .001 if profile["Direction"] == 0
                                else min(a[1], b[1]) < join - .001)
                           for a, b in segments), (path, row["SlotId"], "missing side wall", side)
            widths.append(right - left)
    else:
        xs = sorted(set(round(x, 5) for x in vertical))
        slots = sum(math.isclose(b - a, 1.8, abs_tol=0.001) for a, b in zip(xs, xs[1:]))
        expected_slots = 1 if profile["Direction"] == 0 else 3
        assert slots == expected_slots, (profile["RibId"], slots, expected_slots)
        widths = [1.8] * slots
    report.append({"rib": profile["RibId"], "size_mm": size, "expected_mm": expected,
                   "slot_widths_mm": widths, "slots": len(widths), "entity_types": dict(Counter(e["kind"] for e in data))})
print(json.dumps(report, indent=2))
print(f"PASS: {len(report)} DXFs at mm 1:1; {sum(row['slots'] for row in report)} prototype slots.")
