"""Independent, read-only local QA; input/output contain private CAD, outside Git.

Usage: python compare_holder_splines.py PRIVATE-spline-qa.json [PRIVATE-result.json]
Optional QA dependency: ezdxf. The add-in does not depend on Python or ezdxf.
Sampling cross-checks the evaluator/parameter/transform, not the mathematical
bound: production bounds come from outward-rounded rational Bezier hulls.
"""
import hashlib
import json
import math
import sys
from pathlib import Path

import ezdxf
from ezdxf.math import Matrix44


def distance_to_segment(point, start, end):
    dx, dy = end[0] - start[0], end[1] - start[1]
    length = dx * dx + dy * dy
    t = 0 if length == 0 else max(0, min(1, ((point[0] - start[0]) * dx + (point[1] - start[1]) * dy) / length))
    return math.hypot(point[0] - start[0] - t * dx, point[1] - start[1] - t * dy)


def xy(point):
    return point["X"], point["Y"]


def main():
    data = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
    master = Path(data["MasterPath"])
    before = hashlib.sha256(master.read_bytes()).hexdigest()
    assert before == data["SourceSha256"], "Source revision changed since C# capture"
    doc = ezdxf.readfile(master)
    assert doc.units == 4, "This actual-source QA fixture requires declared mm"
    samples = chords = instances = 0
    maximum_distance = 0.0
    maximum_bound = 0.0
    for row in data["Splines"]:
        entity = doc.entitydb[row["Handle"]]
        assert entity.dxftype() == "SPLINE"
        raw, approximation = row["Spline"], row["Approximation"]
        assert raw["Degree"] == entity.dxf.degree and raw["Flags"] == entity.dxf.flags
        assert list(entity.knots) == raw["Knots"] and list(entity.weights) == raw["Weights"]
        assert len(entity.control_points) == len(raw["SourceControlPoints"])
        for original, parsed in zip(entity.control_points, raw["SourceControlPoints"]):
            assert original[0] == parsed["X"] and original[1] == parsed["Y"] and original[2] == 0
        assert approximation["Supported"], "Unsupported instance must not be counted as converted"
        evaluator = entity.construction_tool()
        matrix = raw["SourceToMm"]
        independent_matrix = Matrix44()
        for handle in row["InsertHandles"]:
            insert = doc.entitydb[handle]
            assert insert.dxftype() == "INSERT" and insert.dxf.row_count == 1 and insert.dxf.column_count == 1
            independent_matrix = insert.matrix44() @ independent_matrix
        for chord in approximation["Chords"]:
            bound = chord["DeviationBoundMm"]
            assert 0 < bound <= approximation["RequestedToleranceMm"] <= 0.001
            maximum_bound = max(maximum_bound, bound)
            start, end = xy(chord["Chord"]["Start"]), xy(chord["Chord"]["End"])
            for index in range(17):
                fraction = index / 16
                parameter = (1 - fraction) * chord["ParameterMin"] + fraction * chord["ParameterMax"]
                original = evaluator.point(parameter)
                parsed_point = (matrix["A"] * original.x + matrix["C"] * original.y + matrix["Tx"],
                                matrix["B"] * original.x + matrix["D"] * original.y + matrix["Ty"])
                expected = independent_matrix.transform(original)
                point = expected.x, expected.y
                assert math.dist(point, parsed_point) <= 1e-8 and abs(expected.z) <= 1e-8, "Source INSERT chain mismatch"
                distance = distance_to_segment(point, start, end)
                # Binary64 independent-library/evaluation slack only. This is
                # not added to, nor substituted for, the production envelope.
                assert distance <= bound + 1e-8, "Independent source evaluation outside bound"
                maximum_distance = max(maximum_distance, distance)
                samples += 1
            chords += 1
        instances += 1
    assert instances == 120, "Expected 120 selected SPLINE instances in this revision"
    assert hashlib.sha256(master.read_bytes()).hexdigest() == before, "Read-only QA changed source"
    result = dict(Passed=True, Instances=instances, Chords=chords, Samples=samples,
                  MaximumSampleDistanceMm=maximum_distance, MaximumCertifiedBoundMm=maximum_bound,
                  IndependentBackend="ezdxf " + ezdxf.__version__, SourceUnchanged=True)
    if len(sys.argv) > 2:
        Path(sys.argv[2]).write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
