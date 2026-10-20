"""Sanitize actual final-delivery evidence; never generate geometry or approvals.

Inputs stay private. The output contains topology counts, declared fixture
dimensions, execution statistics and baseline comparisons, not CAD coordinates,
entity keys, machine paths, signatures, or review artifacts.
"""
import argparse
from collections import Counter
import json
from pathlib import Path
import statistics

def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def compare_native(actual, baseline):
    cases = actual["Cases"]
    old = {c["CaseId"]: c for c in baseline["Cases"]}
    expected = {f"XL-{i:02d}" for i in range(1, 34)}
    if len(cases) != 33 or {c["CaseId"] for c in cases} != expected or set(old) != expected:
        raise ValueError("Exactly the original 33 cases are required; retries cannot be merged")
    if actual["SidecarSha256"] != baseline["SidecarSha256"]:
        raise ValueError("Different fixture specification")
    facts = {
        "FunctionalResult": actual["FunctionalResult"],
        "OverallResult": actual["Result"],
        "ShutdownResult": actual["ShutdownResult"],
        "Assertions": actual["Assertions"],
        "Execution": dict(Counter(c["Execution"] for c in cases)),
        "NativeGeometryExactlyEqualToPr35": all(c["NativeGeometry"] == old[c["CaseId"]]["NativeGeometry"] for c in cases),
        "ColdOutputExactlyEqualToPr35": all(c["Cold"] == old[c["CaseId"]]["Cold"] for c in cases),
        "ProtectedSourcesUnchanged": actual["ProtectedSourcesUnchanged"],
        "NativeBends": sum(c["Cold"]["BendCount"] for c in cases),
        "StaticRows": sum(len(c["Cold"]["Static"]) for c in cases),
        "ProcessRows": sum(len(c["Cold"]["Process"]) for c in cases),
        "Minimum": dict(Counter(m["Status"] for c in cases for m in c["Cold"]["Minimum"])),
        "Manufacturing": dict(Counter(c["ManufacturingStatus"] for c in cases)),
    }
    for phase in ("Cold", "Warm"):
        timings = [c[phase + "Timing"]["RunToVisibleMilliseconds"] for c in cases]
        facts[phase] = {
            "MeanVisibleMs": statistics.mean(timings), "MinimumVisibleMs": min(timings), "MaximumVisibleMs": max(timings),
            "Captures": sum(c[phase + "Metrics"]["Captures"] for c in cases),
            "CaptureHits": sum(c[phase + "Metrics"]["CaptureHits"] for c in cases),
            "NumericQueries": sum(c[phase + "Metrics"]["KernelQueries"] for c in cases),
        }
    # Missing evidence remains a valid software result. It cannot be rewritten
    # to manufacturing PASS by this report or by the functional test status.
    facts["ComparisonResult"] = "PASS" if all(facts[k] for k in (
        "NativeGeometryExactlyEqualToPr35", "ColdOutputExactlyEqualToPr35", "ProtectedSourcesUnchanged")) else "FAIL"
    return facts


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--private-run", required=True, type=Path)
    p.add_argument("--baseline-run", required=True, type=Path)
    p.add_argument("--output", required=True, type=Path)
    args = p.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    holders = read(args.private_run / "holder-inventory-final" / "holder-review-summary.json")["Tools"]
    if len(holders) != 18 or len({h["ToolId"] for h in holders}) != 18:
        raise ValueError("Actual 18-tool inventory required")
    headings = ["Tool", "Source", "Entities", "SPLINE supported / total", "Components", "Closed simple", "Open / branched", "Punch region", "Review", "Clamp verified"]
    rows = [[h["ToolId"], "MATCHED" if h["SourceMatched"] else "UNKNOWN", h["Entities"],
             f'{h["SupportedSplines"]}/{h["Splines"]}', h["ConnectedComponents"], h["ClosedSimpleComponents"],
             h["OpenOrBranchedComponents"], "VALID" if h["PunchRegion"] else "OPEN / INVALID",
             h["Status"], str(h["ClampVerified"]).lower()] for h in holders]
    text = ["# Actual Holder inventory", "", "Topology observations do not assign solid/hole/reference roles or approve mounting.", "",
            "| " + " | ".join(headings) + " |", "| " + " | ".join("---" for _ in headings) + " |"]
    text += ["| " + " | ".join(map(str, row)) + " |" for row in rows]
    text += ["", "All source entities remain in private unsigned proposals. No CAD coordinates, source handles or role approvals are published.", ""]
    (args.output / "holder-status.md").write_text("\n".join(text), encoding="utf-8")
    comparisons = {}
    for mode in ("debug", "release"):
        comparisons[mode] = compare_native(read(args.private_run / f"PRIVATE-final33-{mode}.json"),
                                          read(args.baseline_run / f"PRIVATE-company33-{mode}.json"))
    (args.output / "native-baseline-comparison.json").write_text(json.dumps(comparisons, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({mode: result["ComparisonResult"] for mode, result in comparisons.items()}))
    return 0 if all(r["ComparisonResult"] == "PASS" for r in comparisons.values()) else 1


if __name__ == "__main__":
    raise SystemExit(main())
