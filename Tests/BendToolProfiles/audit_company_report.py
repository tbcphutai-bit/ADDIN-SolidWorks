"""Local-only regression with a separately supplied private company baseline.

Usage: python audit_company_report.py <private-output> <private-expected.json>
No company IDs, readiness observations or baseline values are embedded here.
"""
import csv
import hashlib
import json
import re
import sys
from pathlib import Path


def main(root, expected_path):
    root = Path(root)
    contract = Path(__file__).resolve().parents[2] / "Commands/BendToolProfiles/ToolProfileModels.cs"
    ids = re.findall(r'"((?:AMD|KIK)-[A-Z0-9-]+)"', contract.read_text(encoding="utf-8-sig"))
    assert len(ids) == len(set(ids)) == 18
    expected = json.loads(Path(expected_path).read_text(encoding="utf-8-sig"))
    assert expected["Version"] == 1 and [r["ToolId"] for r in expected["Tools"]] == ids
    report = json.loads((root / "PRIVATE-audit.json").read_text(encoding="utf-8-sig"))
    rows = report["Tools"]
    assert [r["ToolId"] for r in rows] == ids
    watched = {}
    for row, baseline in zip(rows, expected["Tools"]):
        assert all(row[key] == value for key, value in baseline.items()), "Private baseline mismatch; inspect locally"
        assert all(issue["Field"] and issue["RequiredEvidence"] for issue in row["Issues"])
        for source in row["SourceRevisions"]:
            fact = source["Fact"]
            if fact is not None and fact["SourceSha256"] is not None:
                watched[fact["Path"]] = fact["SourceSha256"]
    for path, sha in watched.items():
        assert hashlib.sha256(Path(path).read_bytes()).hexdigest() == sha, "Source changed since audit; rerun"
    summary = json.loads((root / "PRIVATE-status-summary.json").read_text(encoding="utf-8-sig"))
    assert [r["ToolId"] for r in summary["Tools"]] == ids
    assert all("PrivateSelection" not in r and "SourceRevisions" not in r and "Issues" not in r for r in summary["Tools"])
    with (root / "18-tool-status.csv").open(encoding="utf-8-sig", newline="") as file:
        assert [r["ToolId"] for r in csv.DictReader(file)] == ids
    mappings = json.loads((root / "PRIVATE-four-mappings.json").read_text(encoding="utf-8-sig"))
    assert {r["ToolId"] for r in mappings} == set(expected["SeparateMappingFollowups"])
    for filename in ("PRIVATE-intake-UNFILLED.json", "PRIVATE-holder-review-UNFILLED.json", "PRIVATE-seating-review-UNFILLED.json", "PRIVATE-axial-measurements-UNFILLED.json"):
        text = (root / filename).read_text(encoding="utf-8-sig")
        assert '\"SignatureBase64\":null' in text and '\"ReviewerId\":null' in text
    checklist = (root / "PRIVATE-operator-checklist.md").read_text(encoding="utf-8-sig")
    assert all(f"## {tool}" in checklist for tool in ids)
    print(f"PRIVATE COMPANY REGRESSION PASS: {len(rows)} contract IDs; private baseline, {len(watched)} source hashes, CSV, checklists and blank templates checked.")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
