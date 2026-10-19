"""Summarize actual native records. Never manufacture missing CSV rows/results.

Default CSV retains the requested local fixture paths. --public replaces paths
with fixture basenames and omits raw CAD, source manifests and coordinates.
Python standard library only; no CAD or spreadsheet writer is required.
"""
import argparse
from collections import Counter
import csv
import json
from pathlib import Path


def counts(values):
    return "; ".join(f"{key}={value}" for key, value in sorted(Counter(values).items())) or "NOT_RUN"


def fields(case, public=False):
    geometry = case.get("NativeGeometry", {})
    result = case.get("Cold", {})
    statics = result.get("Static", [])
    processes = result.get("Process", [])
    planning = result.get("Planning", {})
    reasons = {reason for row in statics for role in ("Punch", "Holder") for reason in row[role]["Reasons"]}
    reasons.update(reason for row in processes for pair in row["Pairs"] for reason in pair["Reasons"])
    reasons.update(reason for tool in planning.get("Tools", []) for reason in tool["Reasons"])
    fixture = case.get("FixturePath", "NOT_RUN")
    if public and fixture != "NOT_RUN":
        # Windows paths are also handled when the report is built on Linux.
        fixture = "private-fixtures/" + fixture.replace("\\", "/").rsplit("/", 1)[-1]
    return {
        "Case ID": case["CaseId"], "Fixture": fixture, "Execution": case["Execution"],
        "Shape": case["Shape"], "Material": case["MaterialGroup"],
        "Source interpretation": case.get("SourceInterpretation", "Controlled generator diagnostic"),
        "Actual length mm": geometry.get("AxialLengthMm", "NOT_RUN"),
        "Actual W mm": geometry.get("W", "NOT_RUN"), "Actual L mm": geometry.get("L", "NOT_RUN"),
        "Actual t mm": ";".join(str(n) for n in geometry.get("ThicknessesMm", [])) or "NOT_RUN",
        "Reference mm": case["ReferenceAxialLengthMm"], "Reference relation": case["LengthReferenceRelation"],
        "Bends": result.get("BendCount", len(geometry.get("FeatureIds", []))),
        "Native FeatureIds": ";".join(str(n) for n in geometry.get("FeatureIds", [])) or "NOT_RUN",
        "Check Min": "; ".join(f'{m["FeatureId"]}:{m["Status"]}' for m in result.get("Minimum", [])) or "NOT_RUN",
        "Candidates per bend": "; ".join(f'{feature}:{count}' for feature, count in sorted(Counter(r["FeatureId"] for r in statics).items())) or "NOT_RUN",
        "Punch": counts(row["Punch"]["Status"] for row in statics), "Holder": counts(row["Holder"]["Status"] for row in statics),
        "Process": counts(row["Status"] for row in processes),
        "Phase 4B": counts(tool["Decision"] for tool in planning.get("Tools", [])),
        "Common tools in checked scope": ";".join(planning.get("CommonToolIds", [])) or "NONE_PROVEN",
        "UNKNOWN reasons": "; ".join(sorted(reasons)) or "NOT_RUN",
        "Cold ms": case.get("ColdTiming", {}).get("RunToVisibleMilliseconds", "NOT_RUN"),
        "Warm ms": case.get("WarmTiming", {}).get("RunToVisibleMilliseconds", "NOT_RUN"),
        "Cold captures": case.get("ColdMetrics", {}).get("Captures", "NOT_RUN"),
        "Warm capture hits": case.get("WarmMetrics", {}).get("CaptureHits", "NOT_RUN"),
        "Cold numeric queries": case.get("ColdMetrics", {}).get("KernelQueries", "NOT_RUN"),
        "Manufacturing": case["ManufacturingStatus"],
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("record", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--public", action="store_true")
    args = parser.parse_args()
    record = json.loads(args.record.read_text(encoding="utf-8-sig"))
    rows = [fields(case, args.public) for case in record["Cases"]]
    if not rows:
        raise SystemExit("No executed native case records; cannot produce a 33-case results table")
    args.output.mkdir(parents=True, exist_ok=False)
    with (args.output / "native-cases.csv").open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)
    table_keys = ["Case ID", "Actual length mm", "Actual W mm", "Actual L mm", "Actual t mm", "Native FeatureIds", "Check Min", "Punch", "Holder", "Process", "Phase 4B", "Cold ms", "Warm ms", "Execution"]
    def cell(value):
        return str(round(value, 3) if isinstance(value, float) else value).replace("|", "\\|").replace("\n", " ")
    text = ["# Native fixture execution", "", f'Purpose: `{record["Purpose"]}`.', ""]
    if record["Purpose"] != "CompanyExcel33":
        text += ["These are generator validation Parts. They are **not** the 33 company Excel/CSV acceptance cases.", ""]
    text += [f'Functional: {record.get("FunctionalResult", "NOT_RUN")}. Shutdown: {record.get("ShutdownResult", "NOT_RUN")}. Overall: {record.get("Result", "NOT_RUN")}.',
             "", "Software execution PASS does not certify manufacturing. Axial 4200/6000 mm values are references, not certified tool working lengths.", "",
             "| " + " | ".join(table_keys) + " |", "| " + " | ".join("---" for _ in table_keys) + " |"]
    text += ["| " + " | ".join(cell(row[key]) for key in table_keys) + " |" for row in rows]
    text += ["", "See native-cases.csv for fixture paths, individual lengths, per-bend candidate counts, all UNKNOWN reasons, capture counts and numeric query counts.",
             "", "No numeric queries means the missing evidence was detected before the numeric kernel. It is not a collision performance result.", ""]
    (args.output / "native-cases.md").write_text("\n".join(text), encoding="utf-8")
    print(f"Wrote {len(rows)} actual native case records; " + counts(row["Execution"] for row in rows))


if __name__ == "__main__":
    main()
