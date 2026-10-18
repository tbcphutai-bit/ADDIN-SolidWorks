"""Private native QA using an independent OpenCascade STEP B-rep kernel.

The add-in never loads OCP. Neither sampled points nor this reference grant a
production coverage certificate. No CAD, source coordinates or raw outputs may
be uploaded. Only the sanitized summary is appropriate for public reporting.
"""
import json
import sys
from collections import defaultdict
from pathlib import Path
import OCP
from OCP.STEPControl import STEPControl_Reader
from OCP.IFSelect import IFSelect_RetDone
from OCP.TopAbs import TopAbs_SOLID, TopAbs_IN, TopAbs_OUT, TopAbs_ON
from OCP.TopExp import TopExp_Explorer
from OCP.BRepCheck import BRepCheck_Analyzer
from OCP.BRepClass3d import BRepClass3d_SolidClassifier
from OCP.BRepBndLib import BRepBndLib
from OCP.Bnd import Bnd_Box
from OCP.gp import gp_Pnt


def main():
    root = Path(sys.argv[1])
    source = json.loads((root/'PRIVATE-native-kernel-reference.json').read_text(encoding='utf-8-sig'))
    reader = STEPControl_Reader()
    reader.SetSystemLengthUnit(1.0)  # OpenCascade internal mm; checked against native extrema below.
    assert reader.ReadFile(str(root/'PRIVATE-native-source.step')) == IFSelect_RetDone
    assert reader.TransferRoots() == 1
    shape = reader.OneShape()
    assert BRepCheck_Analyzer(shape).IsValid(), 'Independent STEP B-rep is invalid'
    explorer = TopExp_Explorer(shape, TopAbs_SOLID); solids = []
    while explorer.More():
        solids.append(explorer.Current()); explorer.Next()
    assert len(solids) == 1, 'Unexpected solid scope in STEP reference'
    box = Bnd_Box(); BRepBndLib.AddOptimal_s(shape, box, False, False)
    difference = max(abs(a-b) for a, b in zip(box.Get(), source['NativeBoundsMm']))
    assert difference < .001, ('STEP unit/frame or export bounds mismatch', difference)
    classifier = BRepClass3d_SolidClassifier(solids[0])

    def classify(point):
        classifier.Perform(gp_Pnt(*point), 1e-6)
        state = classifier.State()
        if state == TopAbs_IN: return 'Inside'
        if state == TopAbs_OUT: return 'Outside'
        assert state == TopAbs_ON, 'Independent kernel returned an unclassified state'
        return 'Boundary'

    stats = defaultdict(lambda: dict(Checked=0, Inside=0, Outside=0, Skipped=0))
    failures = []
    for index, item in enumerate(source['Candidates']):
        row = stats[item['Feature']]; exact = classify(item['World'])
        if exact == 'Boundary' or item['SliceLocation'] == 'Unknown':
            row['Skipped'] += 1
            continue
        row['Checked'] += 1; row[exact] += 1
        if item['SliceLocation'] != exact:
            failures.append(dict(Index=index, Feature=item['Feature'], Expected=exact, Actual=item['SliceLocation']))
        if exact == 'Inside' and item['EnvelopeLocation'] == 'Outside':
            failures.append(dict(Index=index, Feature=item['Feature'], Expected='Conservative interval enclosure', Actual='Outside'))
    (root/'PRIVATE-independent-failures.json').write_text(json.dumps(failures, indent=2), encoding='utf-8')
    assert not failures, ('Native section/enclosure mismatch', len(failures), 'see private failures')
    for point in source['Witnesses']:
        assert classify(point) == 'Inside', 'Collision witness is outside independently parsed native material'
    for row in stats.values():
        assert row['Inside'] > 0 and row['Outside'] > 0, 'Each bend needs checked material and void samples'
    report = dict(Passed=True, Backend='OpenCascade '+OCP.__version__, ValidSolidCount=1,
                  Queries=len(source['Candidates']), Checked=sum(v['Checked'] for v in stats.values()),
                  Skipped=sum(v['Skipped'] for v in stats.values()), Witnesses=len(source['Witnesses']),
                  MaximumNativeBoundsDifferenceMm=difference,
                  PerFeature=[dict(Feature=k, **v) for k, v in sorted(stats.items())],
                  ControlledFiniteProbes=source['PerFeature'], FalseMaterial=0, MissedMaterial=0,
                  Reference='Independent STEP B-rep classifier, with native exact extrema unit/frame guard')
    (root/'independent-native-step-qa.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
