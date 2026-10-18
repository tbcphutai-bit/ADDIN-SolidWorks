"""Independent analytic reference for the rotating-bar fixture (no solver imports).

Tool disk center has radius R=5; bar is [1,8] x [-.25,.25]. Closest angular
position is clamp(center angle, [0, pi/2]). For the tested exterior angles
within .3 radians of that interval, local x remains strictly inside [1,8],
so exact point-to-swept-bar distance is max(0,R*sin(delta)-.25). The fixed
panel and bounded hinge cannot reach these tool locations. This closed-form
whole-path oracle is independent of production adaptive interval/kernel math.
"""
import json
import math
import pathlib
import sys


def main():
    source = pathlib.Path(sys.argv[1])
    data = json.loads(source.read_text(encoding="utf-8-sig"))
    assert data["Bar"] == [1, 8, -.25, .25]
    assert data["StartAngle"] == 0 and abs(data["EndAngle"] - math.pi/2) < 1e-15
    counts = dict(CLEAR=0, COLLISION=0, UNKNOWN=0)
    missed_by_three = 0
    errors = []
    for row in data["Cases"]:
        a, r = row["Angle"], row["Radius"]
        closest = max(0, min(math.pi/2, a))
        delta = abs(a - closest)
        assert delta <= .3000001 and 1 < 5*math.cos(delta) < 8
        distance = max(0, 5*math.sin(delta)-.25)
        gap = distance-r
        status = row["Status"]
        counts[status] += 1
        if status == "CLEAR" and gap <= 1e-5:
            errors.append((row["Index"], "false CLEAR", gap))
        if status == "COLLISION":
            if gap >= -1e-5:
                errors.append((row["Index"], "false COLLISION", gap))
            w = row["Witness"]
            theta = w["Parameter"]*math.pi/2
            x, y = w["X"], w["Y"]
            local_x = x*math.cos(theta)+y*math.sin(theta)
            local_y = -x*math.sin(theta)+y*math.cos(theta)
            if not (1 < local_x < 8 and -.25 < local_y < .25 and -4 < w["AxialMm"] < 4 and
                    math.hypot(x-5*math.cos(a), y-5*math.sin(a)) < r):
                errors.append((row["Index"], "witness outside actual bar/disk"))
        def sample_gap(theta):
            # Independent point-to-axis-aligned rectangle in panel coordinates.
            x, y = 5*math.cos(a-theta), 5*math.sin(a-theta)
            return math.hypot(max(1-x, 0, x-8), max(abs(y)-.25, 0))-r
        if gap < -1e-4 and all(sample_gap(t) > 1e-4 for t in (0, math.pi/4, math.pi/2)):
            missed_by_three += 1
    assert counts["CLEAR"] and counts["COLLISION"] and missed_by_three
    report = dict(cases=len(data["Cases"]), statuses=counts,
                  sparse_three_angles_miss=missed_by_three, errors=errors,
                  method="ClosedFormRotatingRectangleVsDiskContinuousAngleV1")
    source.with_name("process-independent-result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
