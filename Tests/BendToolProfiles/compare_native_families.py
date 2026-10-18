"""Independent original analytic volumes, not generated C# slices. QA only."""
import json
import math
import sys
from collections import Counter
from pathlib import Path


def reference(case):
    b = case['Tool']
    assert 0 < b['X0'] < b['X1'] < 10
    low = math.hypot(max(0, b['Y0']), max(0, b['Z0']))
    high = max(math.hypot(y, z) for y in (b['Y0'], b['Y1']) for z in (b['Z0'], b['Z1']))
    if case['SourceKind'] == 'BoxMinusXCylinder':
        assert all(-4 < b[k+'0'] < b[k+'1'] < 4 for k in 'YZ')
        return high > 2, max(0, 2-high), lambda y, z: -4 <= y <= 4 and -4 <= z <= 4 and math.hypot(y, z) >= 2-1e-8
    assert case['SourceKind'] == 'QuarterAnnulusX'
    assert b['Y0'] > 0 and b['Z0'] > 0
    hit = low < 3 and high > 1
    return hit, max(0, 1-high, low-3), lambda y, z: y >= -1e-8 and z >= -1e-8 and 1-1e-8 <= math.hypot(y, z) <= 3+1e-8


def main():
    root = Path(sys.argv[1])
    data = json.loads((root/'SYNTHETIC-native-family-oracle.json').read_text(encoding='utf-8-sig'))
    assert data['FixtureKind'] == 'EXACT_SYNTHETIC_NATIVE_STYLE_BREP_NOT_COMPANY_DATA'
    counts = Counter()
    for case in data['Cases']:
        hit, distance, contains = reference(case)
        status = case['Status']; counts[status] += 1
        if status == 'CLEAR':
            assert not hit, (case['Name'], 'false CLEAR')
            lower = case['CertifiedClearanceLowerMm']
            assert lower is None or lower <= distance + 1e-8, (case['Name'], 'overstated distance')
        elif status == 'COLLISION':
            assert hit, (case['Name'], 'false COLLISION')
            w = case['Witness']; p = dict(X=w['PointMm']['X'], Y=w['PointMm']['Y'], Z=w['AxialMm'])
            assert all(case['Tool'][k+'0']-1e-8 <= p[k] <= case['Tool'][k+'1']+1e-8 for k in 'XYZ')
            assert contains(p['Y'], p['Z']) and 0 <= p['X'] <= 10, (case['Name'], 'witness outside original volume')
        else:
            assert status == 'UNKNOWN' and case['Witness'] is None and case['CertifiedClearanceLowerMm'] is None
    assert counts['CLEAR'] > 20 and counts['COLLISION'] > 20
    report = dict(Passed=True, Cases=len(data['Cases']), Counts=dict(counts), FalseClear=0, FalseCollision=0,
                  Reference='Independent exact box-minus-cylinder and quarter-annular extrusion source volumes')
    (root/'independent-native-family-qa.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
