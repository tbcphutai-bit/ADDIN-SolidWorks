"""Independent GEOS oracle for CONTROLLED SYNTHETIC fixtures only.

python compare_collision_with_geos.py <oracle-output-directory>
Optional QA dependencies: shapely/numpy, never loaded by the add-in.
ARC tessellation is QA-only with an explicit sagitta allowance. Production
retains analytic arcs and interval enclosures. Component holes are subtracted
before union, independently of the C# point predicates and distance solver.
"""
import json
import math
import sys
from collections import Counter
from pathlib import Path

import shapely
from shapely.geometry import LineString, Point, Polygon
from shapely.ops import unary_union

ARC_ERROR = 1e-7
QUADS = 32
BUFFER_FACTOR = 1 / math.cos(math.pi / (4 * QUADS))


def xy(p):
    return p["X"], p["Y"]


def ring(contour):
    points = []
    allowance = 0.0
    for curve in contour["Boundary"]:
        if curve["Kind"] == "LINE":
            segment = [xy(curve["Start"]), xy(curve["End"])]
        else:
            radius = curve["RadiusMm"]
            start = math.atan2(curve["Start"]["Y"] - curve["Center"]["Y"], curve["Start"]["X"] - curve["Center"]["X"])
            sweep = curve["SweepRadians"]
            maximum_angle = 2 * math.acos(1 - ARC_ERROR / radius)
            count = max(1, math.ceil(abs(sweep) / maximum_angle))
            assert count <= 262144, "QA arc budget exceeded"
            allowance = max(allowance, radius * (1 - math.cos(abs(sweep) / (2 * count))))
            segment = [(curve["Center"]["X"] + radius * math.cos(start + sweep * i / count),
                        curve["Center"]["Y"] + radius * math.sin(start + sweep * i / count)) for i in range(count + 1)]
            # Use the supplied analytic endpoints after checking their numeric
            # agreement. In particular an explicit full circle has ONE seam,
            # not an extra microscopic GEOS edge caused by sin(2*pi) rounding.
            endpoint_error = max(math.dist(segment[0], xy(curve["Start"])),
                                 math.dist(segment[-1], xy(curve["End"])))
            assert endpoint_error <= 1e-10, "Inconsistent analytic source endpoint"
            allowance += endpoint_error
            segment[0], segment[-1] = xy(curve["Start"]), xy(curve["End"])
        if points:
            assert math.dist(points[-1], segment[0]) < 1e-8, "Fixture must supply ordered source edges; no QA repair"
        points.extend(segment if not points else segment[1:])
    assert math.dist(points[0], points[-1]) < 1e-8, "No invented closing source edge"
    return points, allowance


def material(source, numerical):
    components = []
    certain_components = []
    lower_envelope = []
    for component in source["Components"]:
        outline, arc_error = ring(component["Outer"])
        outer = Polygon(outline)
        assert outer.is_valid and not outer.is_empty
        holes = []
        inner_outer = outer.buffer(-(arc_error + component["Outer"]["UncertaintyMm"] + numerical) * BUFFER_FACTOR, quad_segs=QUADS)
        excluded = []
        contours = [component["Outer"]] + component["Holes"]
        for hole in component["Holes"]:
            coords, error = ring(hole)
            shape = Polygon(coords)
            assert shape.is_valid and outer.covers(shape)
            holes.append(shape)
            excluded.append(shape.buffer((hole["UncertaintyMm"] + error + numerical) * BUFFER_FACTOR, quad_segs=QUADS))
        nominal = outer.difference(unary_union(holes))
        certain = inner_outer.difference(unary_union(excluded))
        components.append(nominal)
        certain_components.append(certain)
        # This is a SUBSET of the conservative envelope. Its distance is an
        # upper bound, suitable to falsify an overstated C# clearance lower.
        lower_envelope.append(nominal.buffer(-(arc_error + ARC_ERROR + numerical) * BUFFER_FACTOR, quad_segs=QUADS))
        for contour in contours:
            coords, error = ring(contour)
            radius = contour["UncertaintyMm"] - error - numerical
            if radius > 0:
                lower_envelope.append(LineString(coords).buffer(radius / BUFFER_FACTOR, quad_segs=QUADS))
    return unary_union(components), unary_union(certain_components), unary_union(lower_envelope)


def main():
    root = Path(sys.argv[1])
    data = json.loads((root / "SYNTHETIC-collision-oracle.json").read_text(encoding="utf-8-sig"))
    assert data["FixtureKind"] == "CONTROLLED_SYNTHETIC_NO_COMPANY_DATA"
    counts = Counter()
    worst_excess = 0.0
    for case in data["Cases"]:
        a, inside_a, envelope_a = material(case["Workpiece"], case["NumericalGuardMm"])
        b, inside_b, envelope_b = material(case["Obstacle"], case["NumericalGuardMm"])
        status = case["Status"]
        counts[status] += 1
        if status == "CLEAR":
            lower = case["CertifiedClearanceLowerMm"]
            assert lower > case["ToleranceMm"], (case["Name"], "no positive certificate")
            assert a.intersection(b).area <= 1e-12, (case["Name"], "false CLEAR: nominal material overlaps")
            if not envelope_a.is_empty and not envelope_b.is_empty:
                reference_upper = envelope_a.distance(envelope_b)
                worst_excess = max(worst_excess, lower - reference_upper)
                assert lower <= reference_upper + 1e-8, (case["Name"], "overstated certified clearance", lower, reference_upper)
        elif status == "COLLISION":
            point = Point(xy(case["Witness"]["PointMm"]))
            assert inside_a.contains(point) and inside_b.contains(point), (case["Name"], "witness not robustly in both materials")
            margin = case["Witness"]["InteriorMarginLowerMm"]
            assert margin > case["ToleranceMm"]
            # The oracle erodes uncertain boundaries plus its own arc/roundoff
            # allowance, so allow that QA-only erosion in this comparison.
            upper = min(a.boundary.distance(point), b.boundary.distance(point))
            assert margin <= upper + 2 * ARC_ERROR + 1e-8, (case["Name"], "overstated interior disk")
        else:
            assert status == "UNKNOWN" and case["CertifiedClearanceLowerMm"] is None
    assert counts["CLEAR"] > 50 and counts["COLLISION"] > 50 and counts["UNKNOWN"] > 0, "Oracle must exercise nontrivial outcomes"
    result = dict(Passed=True, Cases=len(data["Cases"]), Counts=dict(counts), Shapely=shapely.__version__,
                  GEOS=shapely.geos_version_string, ArcSagittaAllowanceMm=ARC_ERROR, FalseClear=0, FalseCollision=0,
                  MaximumLowerCertificateExcessMm=worst_excess)
    (root / "independent-collision-qa.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
