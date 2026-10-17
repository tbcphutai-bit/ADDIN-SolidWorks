"""Independent exact-box / circle solid reference for controlled fixtures.

No production solver or native B-rep compiler is imported. The boxes describe
the ORIGINAL source cell union, not C# generated slices. Reference checks are
QA only and never grant a production coverage certificate.
"""
import json
import math
import sys
from collections import Counter
from pathlib import Path


def gap(a, b, axis):
    return max(0.0, a[axis + "0"] - b[axis + "1"], b[axis + "0"] - a[axis + "1"])


def overlap(a, b):
    return all(min(a[k + "1"], b[k + "1"]) > max(a[k + "0"], b[k + "0"]) for k in "XYZ")


def contains(box, p, slack=1e-8):
    return all(box[k + "0"] - slack <= p[k] <= box[k + "1"] + slack for k in "XYZ")


def reference(case):
    tool, source = case["Tool"], case["Reference"]
    if source["Kind"] == "ExactUnionOfBoxes":
        collision = any(overlap(tool, box) for box in source["Boxes"])
        distance = min(math.sqrt(sum(gap(tool, box, k)**2 for k in "XYZ")) for box in source["Boxes"])
        inside = lambda p: any(contains(b, p) for b in source["Boxes"])
        return collision, distance, inside
    assert source["Kind"] == "ExactBoxMinusXCylinder"
    box, radius = source["Box"], source["Radius"]
    cy, cz = source["CenterY"], source["CenterZ"]
    assert all(box[k + "0"] < tool[k + "0"] < tool[k + "1"] < box[k + "1"] for k in "XYZ")
    # On a rectangle the maximum squared radius is attained at a corner.
    # The box is material outside the specified analytic through-cylinder.
    farthest = max(math.hypot(y - cy, z - cz) for y in (tool["Y0"], tool["Y1"]) for z in (tool["Z0"], tool["Z1"]))
    collision = farthest > radius
    distance = max(0.0, radius - farthest)
    inside = lambda p: contains(box, p) and math.hypot(p["Y"] - cy, p["Z"] - cz) >= radius - 1e-8
    return collision, distance, inside


def main():
    root = Path(sys.argv[1])
    data = json.loads((root / "SYNTHETIC-axial-oracle.json").read_text(encoding="utf-8-sig"))
    assert data["FixtureKind"] == "CONTROLLED_SYNTHETIC_NO_COMPANY_DATA"
    counts = Counter()
    for case in data["Cases"]:
        hit, distance, inside = reference(case)
        status = case["Status"]
        counts[status] += 1
        if status == "CLEAR":
            assert not hit, (case["Name"], "false CLEAR in original source volume")
            lower = case["CertifiedClearanceLowerMm"]
            if lower is not None:
                assert lower <= distance + 1e-8, (case["Name"], "overstated 3D clearance", lower, distance)
        elif status == "COLLISION":
            assert hit, (case["Name"], "false COLLISION")
            witness = case["Witness"]
            p = dict(X=witness["PointMm"]["X"], Y=witness["PointMm"]["Y"], Z=witness["AxialMm"])
            assert contains(case["Tool"], p) and inside(p), (case["Name"], "witness outside true source material or axial occupancy")
            assert witness["CrossSectionInteriorMarginLowerMm"] > 0
            assert witness["AxialOccupancyMarginLowerMm"] > 0
        else:
            assert status == "UNKNOWN" and case["CertifiedClearanceLowerMm"] is None and case["Witness"] is None
    assert counts["CLEAR"] > 50 and counts["COLLISION"] > 50
    result = dict(Passed=True, Cases=len(data["Cases"]), Counts=dict(counts), FalseClear=0, FalseCollision=0,
                  Reference="Independent analytic source cell union and box-minus-cylinder, no sampled proof")
    (root / "independent-axial-qa.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
