using System;
using System.Collections.Generic;
using System.Linq;
using P = ADDIN.Commands.SectionProfilePlanner;

internal static class SectionProfilePlannerTests
{
    private static int passed;
    private static double Cross(P.Point a, P.Point b) { return a.X * b.Y - a.Y * b.X; }
    private static P.Point Normal(P.Point a) { return new P.Point(-a.Y, a.X); }
    private static P.Point Intersect(P.Point a, P.Point u, P.Point b, P.Point v)
    { return a + u * (Cross(b - a, v) / Cross(u, v)); }
    private static P.Point[] Points(params double[] xy)
    {
        var points = new List<P.Point>();
        for (int i = 0; i < xy.Length; i += 2) points.Add(new P.Point(xy[i], xy[i + 1]));
        return points.ToArray();
    }
    // Generate actual two-skin geometry, not just a centerline graph. Round
    // corresponding corners concentrically, retaining cap edges and short lips.
    private static List<P.Curve> Section(P.Point[] center, double thickness, double innerRadius, double[] bendRadii = null)
    {
        var left = Skin(center, thickness / 2);
        var right = Skin(center, -thickness / 2);
        var curves = new List<P.Curve>();
        AddSkin(curves, left, center, thickness, innerRadius, bendRadii);
        AddSkin(curves, right, center, thickness, innerRadius, bendRadii);
        curves.Add(Line(left[0], right[0]));
        curves.Add(Line(left[left.Length - 1], right[right.Length - 1]));
        return curves;
    }
    private static P.Point[] Skin(P.Point[] center, double offset)
    {
        var result = new P.Point[center.Length];
        result[0] = center[0] + Normal((center[1] - center[0]).Unit) * offset;
        result[result.Length - 1] = center[center.Length - 1] + Normal((center[center.Length - 1] - center[center.Length - 2]).Unit) * offset;
        for (int i = 1; i < center.Length - 1; i++)
        {
            var a = (center[i] - center[i - 1]).Unit;
            var b = (center[i + 1] - center[i]).Unit;
            result[i] = Intersect(center[i] + Normal(a) * offset, a, center[i] + Normal(b) * offset, b);
        }
        return result;
    }
    private static P.Curve Line(P.Point a, P.Point b) { return new P.Curve { A = a, B = b, Source = new object() }; }
    private static void AddSkin(List<P.Curve> curves, P.Point[] points, P.Point[] center, double thickness, double innerRadius, double[] bendRadii)
    {
        var before = (P.Point[])points.Clone();
        var after = (P.Point[])points.Clone();
        if (innerRadius > 0 || bendRadii != null)
            for (int i = 1; i < points.Length - 1; i++)
            {
                P.Point incoming = (points[i] - points[i - 1]).Unit, outgoing = (points[i + 1] - points[i]).Unit;
                P.Point inwardBisector = (outgoing - incoming).Unit;
                double turn = Math.Acos(P.Dot(incoming, outgoing));
                bool inner = P.Dot(points[i] - center[i], inwardBisector) > 0;
                double localRadius = bendRadii == null ? innerRadius : bendRadii[i - 1];
                if (localRadius == 0) continue;
                double radius = inner ? localRadius : localRadius + thickness;
                double trim = radius * Math.Tan(turn / 2);
                before[i] = points[i] - incoming * trim;
                after[i] = points[i] + outgoing * trim;
                P.Point arcCenter = before[i] + Normal(incoming) * (Math.Sign(Cross(incoming, outgoing)) * radius);
                curves.Add(new P.Curve { A = before[i], B = after[i], Center = arcCenter, IsArc = true, SweepAngleRadians = turn, Source = new object() });
            }
        for (int i = 0; i < points.Length - 1; i++) curves.Add(Line(after[i], before[i + 1]));
    }
    private static List<P.Curve> Transform(List<P.Curve> curves, double scale, double angle, bool mirror, bool reverse)
    {
        Func<P.Point, P.Point> transform = p => {
            double x = mirror ? -p.X : p.X;
            return new P.Point((x * Math.Cos(angle) - p.Y * Math.Sin(angle)) * scale + .23,
                (x * Math.Sin(angle) + p.Y * Math.Cos(angle)) * scale - .17);
        };
        return curves.Select(c => new P.Curve { A = transform(reverse ? c.B : c.A), B = transform(reverse ? c.A : c.B),
            Center = transform(c.Center), IsArc = c.IsArc, SweepAngleRadians = c.SweepAngleRadians, Source = c.Source }).Reverse().ToList();
    }

    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static P.Point Map(P.Point p, double scale, double angle, bool mirror)
    {
        double x = mirror ? -p.X : p.X;
        return new P.Point((x*Math.Cos(angle)-p.Y*Math.Sin(angle))*scale+.23,
            (x*Math.Sin(angle)+p.Y*Math.Cos(angle))*scale-.17);
    }
    private static P.Plan Build(List<P.Curve> curves, object seed, double scale)
    {
        P.Plan plan; string reason;
        Assert(P.TryBuild(curves, seed, .01*scale, out plan, out reason), reason);
        return plan;
    }
    private static void Values(P.Plan plan, double scale, double[] expected, string label)
    {
        var actual = plan.Lengths.Select(l=>l.Value/scale).OrderBy(x=>x).ToArray();
        var sorted = expected.OrderBy(x=>x).ToArray();
        Assert(actual.Length == sorted.Length, label+" count");
        for(int i=0;i<actual.Length;i++) Assert(Math.Abs(actual[i]-sorted[i])<1e-5,
            label+" expected "+string.Join(",",sorted)+" got "+string.Join(",",actual));
    }
    private static List<P.Curve> PickedSection(P.Point[] picked, double t, double r)
    { return Section(Skin(picked, -t/2),t,r); }

    private static void TestPicked(string name, P.Point[] picked, double[] expected, string shape, double thickness, double radius)
    {
        var raw = PickedSection(picked, thickness, radius);
        int skinCount = (raw.Count-2)/2;
        var skinSources = new HashSet<object>(raw.Take(skinCount).Select(c=>c.Source));
        foreach (double scale in new[] {.0002, .001, .003})
        foreach (double angle in new[] {0.0, .43, Math.PI/2})
        foreach (bool mirror in new[] {false,true})
        {
            var curves = Transform(raw,scale,angle,mirror,true);
            var expectedPoints = picked.Select(p=>Map(p,scale,angle,mirror)).ToArray();
            // Every line on a skin, including the terminal lip, must give the
            // SAME dimension plan; reversed edge sense must not switch skins.
            foreach(var seed in curves.Where(c=>!c.IsArc && skinSources.Contains(c.Source)))
            {
                var plan=Build(curves,seed.Source,scale);
                Values(plan,scale,expected,name);
                Assert(plan.Shape.StartsWith(shape),name+" shape "+plan.Shape);
                Assert(plan.DimensionArcs.Count==0,name+" small fillet misclassified");
                foreach(var length in plan.Lengths)
                {
                    Assert(skinSources.Contains(length.Side.First.Source),name+" crossed skin");
                    bool envelope = length.Rule.Contains("terminal-return-envelope");
                    Assert(envelope || expectedPoints.Any(p=>(p-length.Start.Position).Length < 1e-6*scale),name+" wrong start corner");
                    Assert(envelope || expectedPoints.Any(p=>(p-length.End.Position).Length < 1e-6*scale),name+" wrong end corner");
                    Assert(Math.Abs(Cross(length.End.Position-length.Start.Position,length.Side.Direction))<1e-6*scale,name+" off measurement axis");
                }
            }
            passed++;
        }
        Console.WriteLine("PASS "+name+" t="+thickness+" Rinner="+radius+" all line seeds/rotations/scales");
    }
    private static void TestCurvedL()
    {
        double t=2.3, inner=3.5, outer=inner+t;
        var picked=Points(0,0,39.6,0,39.6,-21.1);
        var raw=PickedSection(picked,t,inner);
        int skinCount=(raw.Count-2)/2;
        foreach(bool inside in new[]{false,true})
        foreach(double rotation in new[]{0.0,.61})
        foreach(double scale in new[]{.0002,.001,.004})
        {
            var sources=new HashSet<object>(raw.Skip(inside?skinCount:0).Take(skinCount).Select(c=>c.Source));
            var curves=Transform(raw,scale,rotation,true,true);
            foreach(var seed in curves.Where(c=>sources.Contains(c.Source)))
            {
                var plan=Build(curves,seed.Source,scale);
                Values(plan,scale,new[]{33.8,15.3},"line-arc-line");
                Assert(plan.DimensionArcs.Count==1,"R count");
                var arc=plan.DimensionArcs[0];
                Assert(sources.Contains(arc.Source),"R swapped skin");
                double radius=(arc.A-arc.Center).Length/scale;
                Assert(Math.Abs(radius-(inside?inner:outer))<1e-6,"radius not selected skin");
                Assert(Math.Abs(radius*Math.Abs(arc.SweepAngleRadians)-(inside?inner:outer)*Math.PI/2)<1e-5,"arc length");
                Assert(plan.Lengths.Sum(l=>(l.Start.IsTangent?1:0)+(l.End.IsTangent?1:0))==2,"missing tangent ends");
            }
            passed++;
        }
        Console.WriteLine("PASS R5.8: tangent lines 33.8 / 15.3; inner/outer R and arc length separately");
    }
    private static void TestTerminalReturnReferences()
    {
        foreach(double t in new[]{.8,2.3,4.2})
        foreach(double width in new[]{29.9,63.7})
        foreach(bool reversed in new[]{false,true})
        foreach(bool mirror in new[]{false,true})
        foreach(double angle in new[]{0.0,.71})
        {
            var picked=Points(44,0,0,0,0,48,12,48,12,60,12+width,60,12+width,75.5);
            var raw=PickedSection(picked,t,t*.35);
            int n=(raw.Count-2)/2;
            var own=new HashSet<object>(raw.Take(n).Select(c=>c.Source));
            var farEdge=raw.Skip(n).Take(n).Single(c=>!c.IsArc &&
                Math.Abs(c.A.X-(12+width+t))<1e-6 && Math.Abs(c.B.X-c.A.X)<1e-6);
            var curves=Transform(raw,.001,angle,mirror,reversed);
            foreach(var seed in curves.Where(c=>!c.IsArc && own.Contains(c.Source)))
            {
                var plan=Build(curves,seed.Source,.001);
                Values(plan,.001,new[]{44.0,48,12,12,width+t,15.5},"terminal envelope varied width");
                var envelope=plan.Lengths.Single(l=>l.Rule.Contains("terminal-return-envelope"));
                var near=Map(new P.Point(12,60),.001,angle,mirror);
                var far=Map(new P.Point(12+width+t,60),.001,angle,mirror);
                Assert(((envelope.Start.Position-near).Length<1e-9 && (envelope.End.Position-far).Length<1e-9) ||
                    ((envelope.End.Position-near).Length<1e-9 && (envelope.Start.Position-far).Length<1e-9),"envelope exact miter references");
                Assert(new[]{envelope.Start,envelope.End}.Any(r=>ReferenceEquals(r.Second.Source,farEdge.Source)),"missing physical far return edge");
                var lip=plan.Lengths.Single(l=>Math.Abs(l.Value/.001-15.5)<1e-6);
                Assert(new[]{lip.Start,lip.End}.All(r=>own.Contains(r.First.Source)),"lip reference changed skin");
            }
            passed++;
        }
        Console.WriteLine("PASS terminal-return references: far physical edge, unchanged lip, varied width/thickness/sense/rotation/mirror");
    }
    private static void Reject(string name,List<P.Curve> curves,object seed)
    {
        P.Plan plan; string reason;
        Assert(!P.TryBuild(curves,seed,.01,out plan,out reason),name+" wrongly accepted");
        passed++; Console.WriteLine("PASS reject "+name+": "+reason);
    }
    private static P.Curve Arc(P.Point a,P.Point b,P.Point center)
    {
        P.Point u=a-center,v=b-center;
        return new P.Curve {A=a,B=b,Center=center,IsArc=true,Source=new object(),
            SweepAngleRadians=Math.Atan2(Cross(u,v),P.Dot(u,v))};
    }
    private static List<P.Curve> CurvedFlanges(double radius,double length,double t)
    {
        double half=length/(2*radius),x=radius*Math.Sin(half);
        P.Point center=new P.Point(0,radius*Math.Cos(half));
        var result=new List<P.Curve>();
        for(int side=0;side<2;side++)
        {
            double r=radius-side*t,bend=1.4*t-side*t,xx=x-side*t,top=174.1-side*t;
            double fy=center.Y-Math.Sqrt((r-bend)*(r-bend)-(xx-bend)*(xx-bend));
            P.Point fl=new P.Point(-xx+bend,fy),fr=new P.Point(xx-bend,fy);
            P.Point left=center+(fl-center)*(r/(r-bend)),right=center+(fr-center)*(r/(r-bend));
            P.Point ls=new P.Point(-xx,fy),re=new P.Point(xx,fy);
            result.Add(Line(new P.Point(-xx,30),ls));
            result.Add(Arc(ls,left,fl));
            result.Add(Arc(left,right,center));
            result.Add(Arc(right,re,fr));
            P.Point up=new P.Point(xx,top-bend),last=new P.Point(xx-bend,top);
            result.Add(Line(re,up));
            result.Add(Arc(up,last,new P.Point(xx-bend,top-bend)));
            result.Add(Line(last,new P.Point(x-161.1,top)));
        }
        result.Add(Line(new P.Point(-x,30),new P.Point(-x+t,30)));
        result.Add(Line(new P.Point(x-161.1,174.1),new P.Point(x-161.1,174.1-t)));
        return result;
    }
    private static void TestCurvedFlanges()
    {
        foreach(double radius in new[]{44200.0,18000.0})
        foreach(double t in new[]{.8,2.3,4.2})
        foreach(double scale in new[]{.0002,.001})
        foreach(double rotation in new[]{0.0,.73})
        foreach(bool mirror in new[]{false,true})
        {
            var raw=CurvedFlanges(radius,500,t);
            var outer=new HashSet<object>(raw.Take(7).Select(c=>c.Source));
            var curves=Transform(raw,scale,rotation,mirror,true);
            foreach(var seed in curves.Where(c=>outer.Contains(c.Source)))
            {
                var plan=Build(curves,seed.Source,scale);
                Assert(plan.Curved!=null,"curved flange routing");
                Values(plan,scale,new[]{30.0,174.1,161.1},"curved flange envelope");
                Assert(Math.Abs(plan.Curved.ArcLength/scale-500)<1e-5,"extended arc not 500");
                Assert(Math.Abs((plan.Curved.Arc.A-plan.Curved.Arc.Center).Length/scale-radius)<1e-5,"main R changed");
                Assert(plan.Curved.Supports.All(c=>outer.Contains(c.Source)),"curved skin switched");
                Assert(plan.Curved.Angles.Last()==0,"right-angle line/line must not get DIM angle");
            }
            var opposite=Build(curves,raw[7].Source,scale);
            Assert(opposite.Curved!=null && Math.Abs((opposite.Curved.Arc.A-opposite.Curved.Arc.Center).Length/scale-(radius-t))<1e-5,"inner main arc");
            passed++;
        }
        foreach(double bendAngle in new[]{90.0,110.9,75.0})
        {
            double radius=2000,t=2.3,theta=.12,delta=(bendAngle-90)*Math.PI/180;
            P.Point a=new P.Point(-Math.Sin(theta)*radius,-Math.Cos(theta)*radius);
            P.Point b=new P.Point(Math.Sin(theta)*radius,-Math.Cos(theta)*radius);
            P.Point radial=a.Unit;
            P.Point heading=new P.Point(-radial.X*Math.Cos(delta)+radial.Y*Math.Sin(delta),
                -radial.X*Math.Sin(delta)-radial.Y*Math.Cos(delta));
            P.Point outgoing=b.Unit*-1, tipA=a+heading*40,tipB=b+outgoing*60;
            P.Point offsetA=Normal(heading*-1)*t,offsetB=Normal(outgoing)*t;
            Func<P.Point,P.Point,P.Point> circleHit=(origin,direction)=>{
                P.Point foot=origin-direction*P.Dot(origin,direction);
                double d=Math.Sqrt((radius-t)*(radius-t)-P.Dot(foot,foot));
                P.Point p=foot+direction*d,q=foot-direction*d;
                return (p-origin).Length<(q-origin).Length?p:q;
            };
            P.Point ai=circleHit(a+offsetA,heading),bi=circleHit(b+offsetB,outgoing);
            var geometry=new List<P.Curve>{Line(tipA,a),Arc(a,b,new P.Point(0,0)),Line(b,tipB),
                Line(tipA+offsetA,ai),Arc(ai,bi,new P.Point(0,0)),Line(bi,tipB+offsetB),
                Line(tipA,tipA+offsetA),Line(tipB,tipB+offsetB)};
            foreach(double rotation in new[]{0.0,.61})
            {
                var plan=Build(Transform(geometry,.001,rotation,true,true),geometry[0].Source,.001);
                Assert(plan.Curved!=null,"sharp arc/flange classification");
                Values(plan,.001,new[]{40.0,60},"oblique flange aligned lengths");
                double[] angles=plan.Curved.Angles.Where(v=>v!=0).ToArray();
                Assert(bendAngle==90?angles.Length==0:angles.Length==1 && Math.Abs(angles[0]-bendAngle)<1e-6,"angle must use local tangent; skip 90");
                passed++;
            }
        }
        var sharp=CurvedFlanges(44200,500,2.3);
        sharp.RemoveAt(1); // deliberately broken skin must still be rejected.
        Reject("missing curved-web fillet",sharp,sharp[0].Source);
        Console.WriteLine("PASS curved web R + flange sketch plan, 500 arc length, selected skins, transforms");
    }
    private static void Run()
    {
        var examples=new[]{
            Points(44,0,0,0,0,48,12,48,12,60,41.9,60,41.9,75.5),
            Points(0,45,0,30,20,30,20,0,110,0),
            Points(0,30,0,0,500,0,500,30),
            Points(0,30,0,0,500,0,500,174.1,338.9,174.1),
            Points(0,30,0,0,50,0,50,-30),
            Points(0,30,0,0,90,0)
        };
        double[][] dimensions={
            new[]{44.0,48,12,12,29.9,15.5}, new[]{15.0,20,30,90},
            new[]{30.0,500,30}, new[]{30.0,500,174.1,161.1},
            new[]{30.0,50,30}, new[]{30.0,90}
        };
        string[] shapes={"Multi","Multi","U","Multi","Z","L"};
        for(int i=0;i<examples.Length;i++)
        foreach(double t in new[]{.8,2.3,4.2})
        foreach(double ratio in new[]{0.0,.35})
        {
            var expected=(double[])dimensions[i].Clone();
            if(i==0) expected[4]+=t; // far face of the terminal return; lip stays 15.5
            if(i==1) { expected[1]+=t; expected[2]+=t; }
            if(i==4) expected[1]+=t;
            TestPicked("drawing fixture "+i,examples[i],expected,shapes[i],t,t*ratio);
        }

        // Opposite Z skin has a different terminal convention, even though the
        // intermediate web has the same length. No global min/max is permissible.
        var z=Points(0,30,0,0,50,0,50,-30);
        var raw=PickedSection(z,2.3,.5);
        int n=(raw.Count-2)/2;
        var opposite=Build(raw,raw.Skip(n).First(c=>!c.IsArc).Source,1);
        Values(opposite,1,new[]{32.3,52.3,27.7},"opposite Z");
        Assert(opposite.Lengths.All(l=>Math.Abs(Cross(l.End.Position-l.Start.Position,l.Side.Direction))<1e-6),"Z diagonal");
        passed++;

        TestCurvedFlanges();
        TestTerminalReturnReferences();
        TestCurvedL();
        foreach(double degrees in new[]{80.0,110.9,135.0})
        {
            double theta=degrees*Math.PI/180;
            var points=Points(Math.Cos(theta)*50,Math.Sin(theta)*50,0,0,100,0);
            var geometry=PickedSection(points,2.3,8);
            var plan=Build(geometry,geometry.First(c=>!c.IsArc).Source,1);
            double trim=8*Math.Tan((Math.PI-theta)/2);
            Values(plan,1,new[]{50-trim,100-trim},"non90 large R");
            Assert(plan.DimensionArcs.Count==1 && plan.Bends.Count==1,"non90 R/angle count");
            Assert(Math.Abs(plan.Bends[0].AngleDegrees-degrees)<1e-7,"supplementary angle");
            passed++;
        }
        double angle=110.9*Math.PI/180;
        TestPicked("inclined L",Points(Math.Cos(angle)*32.1,Math.Sin(angle)*32.1,0,0,90,0),
            new[]{32.1,90},"L",2.3,.5);
        var incline=PickedSection(Points(Math.Cos(angle)*32.1,Math.Sin(angle)*32.1,0,0,90,0),2.3,.5);
        var inclPlan=Build(incline,incline.First(c=>!c.IsArc).Source,1);
        Assert(inclPlan.Bends.Count==1 && Math.Abs(inclPlan.Bends[0].AngleDegrees-110.9)<1e-7,"included angle 110.9");
        passed++;

        // Explicit arc selection requests radius/tangent measurement even when
        // this same small arc is treated as a bend fillet for a line seed.
        var small=PickedSection(Points(0,0,40,0,40,-25),2.3,.5);
        var linePlan=Build(small,small.First(c=>!c.IsArc).Source,1);
        Assert(linePlan.DimensionArcs.Count==0,"small R auto");
        var arcPlan=Build(small,small[0].Source,1);
        Assert(arcPlan.DimensionArcs.Count==1,"explicit arc override");
        Values(arcPlan,1,new[]{37.2,22.2},"explicit small R");
        passed++;

        // Mixed large R and small reversed bend: tangent on one end, virtual
        // on the other; the terminal lip still follows the same chosen skin.
        var mixed=Section(Skin(Points(0,0,80,0,80,-60,130,-60),-2.3/2),2.3,4,new[]{4.0,.5});
        var mixPlan=Build(mixed,mixed.First(c=>!c.IsArc).Source,1);
        Assert(mixPlan.DimensionArcs.Count==1 && mixPlan.Lengths.Count==3,"mixed large R + bend");
        Values(mixPlan,1,new[]{73.7,53.7,50},"mixed tangent to small reverse bend");
        Assert(mixPlan.Lengths.Any(l=>(l.Start.IsTangent && !l.End.IsTangent && !l.End.IsEnd)
            || (l.End.IsTangent && !l.Start.IsTangent && !l.Start.IsEnd)),"missing tangent-virtual transition");
        passed++;

        var split=PickedSection(z,2.3,.5);
        var line=split.First(c=>!c.IsArc);
        var mid=(line.A+line.B)*.5;
        split.Remove(line);
        var half1=Line(line.A,mid); var half2=Line(mid,line.B);
        split.Add(half1);split.Add(half2);
        Values(Build(split,half2.Source,1),1,new[]{30.0,52.3,30},"split line seed");
        passed++;
        var invalid=PickedSection(z,2.3,.5);
        Reject("no seed",invalid,null);
        Reject("cap seed",invalid,invalid.Last().Source);
        var broken=new List<P.Curve>(invalid); broken.RemoveAt(0);
        Reject("missing R",broken,broken.First(c=>!c.IsArc).Source);
        var branch=new List<P.Curve>(invalid); branch.Add(Line(invalid[0].A,invalid[0].A+new P.Point(5,9)));
        Reject("branch",branch,invalid.First(c=>!c.IsArc).Source);
        var bad=PickedSection(z,2.3,.5); bad[0].Center+=new P.Point(1,0);
        Reject("non-tangent arc",bad,bad.First(c=>!c.IsArc).Source);
        var crowded=new List<P.Curve>(invalid);
        crowded.AddRange(PickedSection(z.Select(p=>p+new P.Point(300,300)).ToArray(),2.3,.5));
        Values(Build(crowded,invalid.First(c=>!c.IsArc).Source,1),1,new[]{30.0,52.3,30},"selected component only");
        passed++;
        Console.WriteLine("TOTAL PASS "+passed);
    }
    public static int Main()
    {
        try{Run();return 0;}catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);return 1;}
    }
}
