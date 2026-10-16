import sys,math,json,collections
from pathlib import Path
# Optional QA dependency: ezdxf (not used by the add-in).
import ezdxf
from ezdxf.math import Matrix44,Vec3,bulge_to_arc
ROOT=Path(sys.argv[1]).resolve()
checks=0;max_error=0

def expected_curves(entity,matrix):
    typ=entity.dxftype();ocs=entity.ocs()
    cv=[]
    def add(kind,a,b,center=None,r=None,mid=None,i=0):
        cv.append(dict(Kind=kind,Start=matrix.transform(a),End=matrix.transform(b),Center=matrix.transform(center) if center is not None else None,Mid=matrix.transform(mid) if mid is not None else None,SourceSegment=i))
    if typ=='LINE':add('LINE',entity.dxf.start,entity.dxf.end)
    elif typ in ('ARC','CIRCLE'):
        c=entity.dxf.center;r=entity.dxf.radius;s=math.radians(entity.dxf.start_angle) if typ=='ARC' else 0
        sweep=math.radians((entity.dxf.end_angle-entity.dxf.start_angle)%360) if typ=='ARC' else 2*math.pi
        p=lambda a:ocs.to_wcs((c.x+r*math.cos(a),c.y+r*math.sin(a),c.z))
        add('ARC',p(s),p(s+sweep),ocs.to_wcs(c),r,p(s+sweep/2))
    elif typ in ('LWPOLYLINE','POLYLINE'):
        if typ=='LWPOLYLINE': vertices=list(entity.get_points('xyb'));closed=entity.closed;z=entity.dxf.elevation
        else:vertices=[(v.dxf.location.x,v.dxf.location.y,v.dxf.bulge) for v in entity.vertices];closed=entity.is_closed;z=entity.dxf.elevation.z
        for i in range(len(vertices) if closed else len(vertices)-1):
            a=vertices[i];b=vertices[(i+1)%len(vertices)];v=lambda p:ocs.to_wcs((p[0],p[1],z))
            if a[2]==0:add('LINE',v(a),v(b),i=i)
            else:
                c,_,_,r=bulge_to_arc(a[:2],b[:2],a[2]);s=math.atan2(a[1]-c.y,a[0]-c.x);angle=s+2*math.atan(a[2]);mid=(c.x+r*math.cos(angle),c.y+r*math.sin(angle))
                add('ARC',v(a),v(b),v(c),r,v(mid),i)
    return cv

def compare(actual,expected):
    global checks,max_error
    if actual['Kind']!=expected['Kind']:return float('inf')
    values=[]
    for key in ('Start','End','Center'):
        if expected[key] is not None:
            values.extend(abs(actual[key][k]-getattr(expected[key],k.lower())) for k in ('X','Y'))
    if expected['Kind']=='ARC':
        r=math.hypot(expected['Start'].x-expected['Center'].x,expected['Start'].y-expected['Center'].y)
        values.append(abs(actual['RadiusMm']-r))
        a=math.atan2(actual['Start']['Y']-actual['Center']['Y'],actual['Start']['X']-actual['Center']['X'])+actual['SweepRadians']/2
        mid=(actual['Center']['X']+actual['RadiusMm']*math.cos(a),actual['Center']['Y']+actual['RadiusMm']*math.sin(a))
        values.extend((abs(mid[0]-expected['Mid'].x),abs(mid[1]-expected['Mid'].y)))
    return max(values,default=0)

def compare_document(path,entities):
    global checks,max_error
    doc=ezdxf.readfile(path); pool=collections.defaultdict(list)
    def walk(items,matrix,names=(),layer=''):
        for entity in items:
            if entity.dxf.paperspace:continue
            typ=entity.dxftype();effective=layer if entity.dxf.layer=='0' and layer else entity.dxf.layer
            if typ=='INSERT':walk(entity.block(),entity.matrix44()@matrix,names+(entity.dxf.name,),effective)
            else:
                for c in expected_curves(entity,matrix):pool[(entity.dxf.handle,tuple(names),effective,c['SourceSegment'])].append(c)
    walk(doc.modelspace(),Matrix44())
    actual_count=0
    for entity in entities:
        for curve in entity['Curves']:
            key=(entity['Handle'],tuple(entity['Blocks']),entity['Layer'],curve['SourceSegment']);candidates=pool[key]
            assert candidates,('unmatched source curve',path,key)
            distances=[compare(curve,c) for c in candidates];best=min(range(len(distances)),key=distances.__getitem__);error=distances[best]
            assert error<1e-7,('geometry mismatch',path,key,error)
            max_error=max(max_error,error);checks+=1;actual_count+=1;candidates.pop(best)
    remaining=sum(len(v) for v in pool.values())
    assert remaining==0,('missing supported curves',path,remaining)
    return actual_count

snapshot=json.loads((ROOT/'PRIVATE-company-snapshot.json').read_text(encoding='utf8'));counts={}
for tool in snapshot['Tools']:
    part=next(p for p in tool['Parts'] if p['Role']==0)
    # Include reference lines too when comparing the entire dimensioned source.
    entities=[e for p in tool['Parts'] for e in p['Entities']]
    counts[tool['Id']]=compare_document(part['Source']['Path'],entities)
for i in (0,1):
    data=json.loads((ROOT/f'PRIVATE-document-{i}.json').read_text(encoding='utf8'))
    counts[f'aggregate-{i}']=compare_document(data['Source']['Path'],data['Entities'])
result={'qa_library':'ezdxf '+ezdxf.__version__,'curve_checks':checks,'max_coordinate_radius_midpoint_error_mm':max_error,'source_counts':counts,'passed':True}
(ROOT/'independent-geometry-qa.json').write_text(json.dumps(result,indent=2),encoding='utf8')
print(json.dumps({k:v for k,v in result.items() if k!='source_counts'}))
