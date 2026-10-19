"""Prepare private CSV + source-bound section sidecar from reviewed matrix facts.

The reviewed JSON supplies SourcePath/SourceSha256, 33 Rows and Materials.
No company dimensions or CAD geometry are embedded in this program. The XLSX
is authored/reviewed separately; its numeric cells are checked before binding.
Explicit profiles are fixture interpretations, never manufacturing evidence.
"""
import argparse
import csv
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("reviewed", type=Path)
    parser.add_argument("matrix", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    data = json.loads(args.reviewed.read_text(encoding="utf-8-sig"))
    rows = data["Rows"]
    if len(rows) != 33 or len({r["CaseId"].casefold() for r in rows}) != 33:
        raise SystemExit("33 unique reviewed source cases required")
    if sha(data["SourcePath"]) != data["SourceSha256"]:
        raise SystemExit("Stale original Excel evidence")
    ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
    with zipfile.ZipFile(args.matrix) as book:
        cells = {c.get("r"): c for c in ET.fromstring(book.read("xl/worksheets/sheet1.xml")).findall(".//s:c", ns)}
        for index, row in enumerate(rows, 6):
            for column, key in [("D", "ThicknessMm"), ("E", "W"), ("F", "L"), ("G", "AxialLengthMm"),
                                ("H", "ReferenceAxialLengthMm"), ("I", "InsideRadiusMm")]:
                cell = cells[f"{column}{index}"]
                if cell.get("t") in ("s", "inlineStr") or float(cell.find("s:v", ns).text) != row[key]:
                    raise SystemExit(f"Typed matrix value differs from reviewed case {row['CaseId']}, {key}")
    args.output.mkdir(parents=True, exist_ok=False)
    output = args.output.resolve()
    csv_path = output / "Check_Chan_Test_Cases_4200_6000.csv"
    headers = ["CaseId", "Shape", "AxialLengthMm", "ReferenceAxialLengthMm", "W", "L", "ThicknessMm",
               "MaterialGroup", "InsideRadiusMm", "Origin", "SourceReference"]
    with csv_path.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=headers, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)
    sections = []
    for row in rows:
        t, w, height = row["ThicknessMm"], row["W"], row["L"]
        if row["Shape"] == "U":
            points = [[0, height-t], [0, 0], [w-2*t, 0], [w-2*t, height-t]]
        elif row["Shape"] == "Z":
            points = [[0, 0], [w/2, 0], [w/2, height-t], [w, height-t]]
        elif row["Shape"] == "THREE":
            points = [[0, 0], [w/2, 0], [w/2, height/2], [w-t, height/2], [w-t, height-t]]
        else:
            raise SystemExit("Unreviewed section convention")
        sections.append({"CaseId": row["CaseId"], "InsideRadiusMm": row["InsideRadiusMm"], "SketchVerticesMm": points,
                         "Evidence": "; ".join(row[key] for key in ["Origin", "SourceReference", "SectionInterpretation", "RadiusEvidence"])})
    sources = [("ExcelExamples", Path(data["SourcePath"]).resolve()), ("ExcelMatrix", args.matrix.resolve()), ("Csv", csv_path)]
    spec = {"Version": 1, "Purpose": "CompanyExcel33", "InterpretationEvidence": data["Authorization"],
            "Sources": [{"Role": role, "Path": str(path), "Sha256": sha(path)} for role, path in sources],
            "Columns": {h: h for h in headers[:8]}, "Materials": data["Materials"], "Sections": sections}
    (output / "PRIVATE-company33-sidecar.json").write_text(json.dumps(spec, ensure_ascii=False, indent=2), encoding="utf-8")
    print("Prepared 33 reviewed fixture cases; no process state, mounting datum or working tool length inferred")


if __name__ == "__main__":
    main()
