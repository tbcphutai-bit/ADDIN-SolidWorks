"""Combine explicitly partitioned native runs against one immutable source CSV.

Duplicate/retried cases or different source/interpretation revisions are errors.
Missing cases are listed as NOT_RUN with requested facts only, never measured
geometry or fabricated FeatureIds. CAD/coordinate-bearing JSON stays private.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--csv", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("records", nargs="+", type=Path)
    args = parser.parse_args()
    if args.output.exists():
        raise SystemExit("Use a new output file")
    expected = list(csv.DictReader(args.csv.open(encoding="utf-8-sig", newline="")))
    ids = [row["CaseId"] for row in expected]
    if len(ids) != 33 or len(set(ids)) != 33:
        raise SystemExit("Expected exactly 33 unique source cases")
    csv_sha = hashlib.sha256(args.csv.read_bytes()).hexdigest()
    reports = [json.loads(path.read_text(encoding="utf-8-sig")) for path in args.records]
    first = reports[0]
    if any(r.get("Purpose") != "CompanyExcel33" or r.get("SourceCases") != 33 or
           r["SidecarSha256"] != first["SidecarSha256"] or r["SourceHashes"] != first["SourceHashes"] for r in reports):
        raise SystemExit("Incompatible source/sidecar/purpose revisions")
    if not any(s["Role"] == "Csv" and s["Sha256"] == csv_sha for s in first["SourceHashes"]):
        raise SystemExit("CSV source hash does not match native execution")
    actual = {}
    for report in reports:
        for row in report["Cases"]:
            if row["CaseId"] not in ids or row["CaseId"] in actual:
                raise SystemExit("Duplicate/foreign case; do not hide a failed attempt behind a retry")
            actual[row["CaseId"]] = row
    rows = []
    for index, source in enumerate(expected, 2):
        case = actual.get(source["CaseId"])
        if case is None:
            case = {"CaseId": source["CaseId"], "CsvRecord": index, "Shape": source["Shape"],
                    "MaterialGroup": source["MaterialGroup"], "ReferenceAxialLengthMm": float(source["ReferenceAxialLengthMm"]),
                    "LengthReferenceRelation": "NOT_RUN", "Execution": "NOT_RUN", "ManufacturingStatus": "CHUA_XAC_MINH",
                    "Error": "No native execution record. Requested facts only; no measured geometry."}
        rows.append(case)
    functional = "PASS" if all(row["Execution"] == "PASS" for row in rows) else "FAIL"
    shutdown = "PASS" if all(r.get("ShutdownResult") == "PASS" for r in reports) else "FAIL" if any(r.get("ShutdownResult") == "FAIL" for r in reports) else "NOT_CONFIRMED"
    merged = {"Purpose": "CompanyExcel33", "RequestedCases": 33, "SourceCases": 33,
              "SidecarSha256": first["SidecarSha256"], "SourceHashes": first["SourceHashes"],
              "FunctionalResult": functional, "ShutdownResult": shutdown,
              "Result": "PASS" if functional == "PASS" and shutdown == "PASS" else "FAIL",
              "Assertions": sum(r.get("Assertions", 0) for r in reports), "Cases": rows,
              "PassCount": sum(row["Execution"] == "PASS" for row in rows),
              "FailCount": sum(row["Execution"] == "FAIL" for row in rows),
              "NotRunCount": sum(row["Execution"] == "NOT_RUN" for row in rows),
              "ProtectedSourcesUnchanged": all(r.get("ProtectedSourcesUnchanged") is True for r in reports),
              "Batches": [{"Record": str(path), "FunctionalResult": r.get("FunctionalResult"),
                           "ShutdownResult": r.get("ShutdownResult"), "Assertions": r.get("Assertions"),
                           "WrongThreadCaptureCount": r.get("WrongThreadCaptureCount")} for path, r in zip(args.records, reports)]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(merged, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"33 cases: PASS={merged['PassCount']}, FAIL={merged['FailCount']}, NOT_RUN={merged['NotRunCount']}; shutdown={shutdown}")


if __name__ == "__main__":
    main()
