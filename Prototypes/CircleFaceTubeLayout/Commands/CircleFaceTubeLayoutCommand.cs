// Experimental V0.1 command. NOT wired into ADDIN.csproj or existing commands.
// C# 7.3 / .NET Framework 4.8, SolidWorks 2024. Compile and validate via Codex.
// Assumptions: one selected Part face, one loop, exactly four edges.
// WARNING: Initial straight-axis solution, not proof of tangent tubes or no collision.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    public sealed class CircleFaceTubeLayoutCommand
    {
        const double Mm = 0.001;
        private readonly ISldWorks app;
        private readonly StringBuilder debug = new StringBuilder();
        private string logPath;
        public CircleFaceTubeLayoutCommand(ISldWorks application) { app = application; }

        public void Run()
        {
            try
            {
                InitLog();
                Log("START;version=0.1;status=UNVALIDATED_PROTOTYPE");
                var model = app.ActiveDoc as ModelDoc2;
                if (model == null || model.GetType() != 1)
                    throw new InvalidOperationException("Please open a Part.");
                if (model.SketchManager.ActiveSketch != null)
                    throw new InvalidOperationException("Exit Edit Sketch first.");
                var sm = (SelectionMgr)model.SelectionManager;
                if (sm.GetSelectedObjectCount2(-1) != 1 || sm.GetSelectedObjectType3(1, -1) != 2)
                    throw new InvalidOperationException("Select exactly ONE face.");
                var face = sm.GetSelectedObject6(1, -1) as Face2;
                if (face == null) throw new InvalidOperationException("No Face2 was selected.");
                Log("DOCUMENT=" + model.GetPathName());
                Log("LOOPS=" + face.GetLoopCount());
                using (var form = new CircleFaceTubeLayoutForm())
                {
                    if (form.ShowDialog() != DialogResult.OK) { Log("CANCEL_SETTINGS"); return; }
                    var a = Analyze(face, form.DiameterMm * Mm, form.TotalCount,
                        form.LargeEndMarginMm * Mm, form.FlipSide);
                    string report = a.Describe() + Environment.NewLine +
                        "WARNING: preview is numeric only; gaps/clashes are approximate." +
                        Environment.NewLine + "Create axes as 3D Sketch? NO solid tubes are created." +
                        Environment.NewLine + "Debug: " + logPath;
                    Log(report.Replace(Environment.NewLine, " | "));
                    if (MessageBox.Show(report, "Circle Along Face - Prototype Preview",
                        MessageBoxButtons.YesNo, a.Warnings > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    { Log("CANCEL_PREVIEW"); return; }
                    MakeSketch(model, a);
                    Log("SUCCESS;axes=" + a.Axes.Count);
                    MessageBox.Show("Created " + a.Axes.Count + " axes.\nDebug: " + logPath);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR;" + ex);
                MessageBox.Show("Circle Along Face failed safely:\n" + ex.Message +
                    "\nDebug: " + logPath, "Circle Along Face", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Flush(); }
        }

        private Layout Analyze(Face2 face, double diameter, int n, double bigMargin, bool flip)
        {
            if (n < 2 || n > 500 || diameter <= 0 || bigMargin < 0)
                throw new ArgumentException("Invalid tube diameter/count/margin.");
            if (face.GetLoopCount() != 1)
                throw new NotSupportedException("Prototype supports ONE exterior loop only.");
            object[] raw = face.GetEdges() as object[];
            if (raw == null || raw.Length != 4)
                throw new NotSupportedException("Prototype expects exactly four face edges.");
            Rail[] rails = raw.Cast<Edge>().Select(e => new Rail(e)).ToArray();
            for (int i = 0; i < rails.Length; i++)
                Log(string.Format(CultureInfo.InvariantCulture,
                    "EDGE {0}; L={1:F5}mm; bow={2:F8}; A={3}; B={4}",
                    i, rails[i].Length / Mm, rails[i].Bow, rails[i].First, rails[i].Last));

            // Opposite edges do not share any endpoint.
            var candidates = new List<Tuple<int, int, double>>();
            for (int i = 0; i < 3; i++)
                for (int j = i + 1; j < 4; j++)
                    if (!ShareEndpoint(rails[i], rails[j]))
                        candidates.Add(Tuple.Create(i, j, rails[i].Bow + rails[j].Bow));
            candidates = candidates.OrderByDescending(p => p.Item3).ToList();
            if (candidates.Count == 0 || candidates[0].Item3 < 0.001)
                throw new InvalidOperationException("Cannot reliably detect two curved opposite ends.");
            if (candidates.Count > 1 && Math.Abs(candidates[0].Item3-candidates[1].Item3) < 0.0001)
                throw new InvalidOperationException("Ambiguous rail pair; refuse to guess.");
            Rail a = rails[candidates[0].Item1], b = rails[candidates[0].Item2];
            Rail small = a.Length <= b.Length ? a : b;
            Rail large = a.Length <= b.Length ? b : a;
            bool reverseLarge = Vec.Distance(small.First, large.First) + Vec.Distance(small.Last, large.Last) >
                Vec.Distance(small.First, large.Last) + Vec.Distance(small.Last, large.First);
            double smallFirst = (small.Length - (n-1)*diameter)/2;
            double largeFirst = diameter/2 + bigMargin;
            if (smallFirst < 0 || large.Length < 2*largeFirst)
                throw new InvalidOperationException("Insufficient arc length for requested diameter/count/margin.");
            double largePitch = (large.Length - 2*largeFirst)/(n-1);
            var result = new Layout { Small = small.Length, Large = large.Length,
                Diameter = diameter, SmallFirst = smallFirst, LargeMargin = bigMargin,
                LargePitch = largePitch, Flip = flip };
            Log(string.Format(CultureInfo.InvariantCulture,
                "RAILS;small={0:F4};large={1:F4};smallFirst={2:F4};largePitch={3:F4};reverse={4}",
                small.Length/Mm, large.Length/Mm, smallFirst/Mm, largePitch/Mm, reverseLarge));
            var surface = (Surface)face.GetSurface();
            double sign = (face.FaceInSurfaceSense ? -1 : 1)*(flip ? -1 : 1);
            for (int i = 0; i < n; i++)
            {
                Vec p = small.PointAt(smallFirst+i*diameter, false);
                Vec q = large.PointAt(largeFirst+i*largePitch, reverseLarge);
                Vec normalA = Normal(face, surface, Vec.Lerp(p,q,0.002), sign);
                Vec normalB = Normal(face, surface, Vec.Lerp(q,p,0.002), sign);
                var axis = new Axis { Number=i+1,
                    A=p+normalA*(diameter/2), B=q+normalB*(diameter/2) };
                if (Vec.Distance(axis.A,axis.B) < 0.0001)
                    throw new InvalidOperationException("Degenerate tube axis " + axis.Number);
                result.Axes.Add(axis);
                Log("AXIS;" + axis.Number + ";A=" + axis.A + ";B=" + axis.B);
            }
            for (int i = 0; i < result.Axes.Count; i++)
            {
                Axis axis = result.Axes[i];
                for (int k = 0; k <= 20; k++)
                {
                    Vec sample = Vec.Lerp(axis.A, axis.B, k/20.0);
                    Vec nearest = Closest(face, sample);
                    double sep = Vec.Dot(sample-nearest, Normal(face,surface,nearest,sign)) - diameter/2;
                    if (sep < -0.0001)
                    {
                        result.Warnings++;
                        Log(string.Format(CultureInfo.InvariantCulture,
                            "WARN;axis={0};sample={1};approxFacePenetrationMm={2:F4}",
                            axis.Number,k,-sep/Mm));
                        break;
                    }
                }
            }
            for (int i = 0; i < result.Axes.Count; i++)
                for (int j = i+1; j < result.Axes.Count; j++)
                {
                    double dist = SegmentGap(result.Axes[i].A,result.Axes[i].B,
                        result.Axes[j].A,result.Axes[j].B);
                    if (dist < diameter - 0.00005)
                    {
                        result.Warnings++;
                        Log(string.Format(CultureInfo.InvariantCulture,
                            "WARN;pair={0}-{1};axisGapMm={2:F5}",
                            i+1,j+1,dist/Mm));
                    }
                }
            return result;
        }

        private static bool ShareEndpoint(Rail a,Rail b)
        {
            return Vec.Distance(a.First,b.First)<0.0001 ||
                Vec.Distance(a.First,b.Last)<0.0001 ||
                Vec.Distance(a.Last,b.First)<0.0001 ||
                Vec.Distance(a.Last,b.Last)<0.0001;
        }
        private static Vec Closest(Face2 face,Vec p)
        {
            var r=(double[])face.GetClosestPointOn(p.X,p.Y,p.Z);
            if (r==null || r.Length<3) throw new InvalidOperationException("Cannot find face closest point.");
            return new Vec(r[0],r[1],r[2]);
        }
        private static Vec Normal(Face2 face,Surface surface,Vec p,double sign)
        {
            Vec q=Closest(face,p);
            var r=(double[])surface.EvaluateAtPoint(q.X,q.Y,q.Z);
            if (r==null || r.Length<3) throw new InvalidOperationException("Cannot evaluate face normal.");
            return new Vec(r[0],r[1],r[2]).Unit()*sign;
        }

        private static void MakeSketch(ModelDoc2 model,Layout layout)
        {
            var mgr=model.SketchManager;
            bool open=false, oldAdd=mgr.AddToDB, oldDisplay=mgr.DisplayWhenAdded;
            int count=0;
            try
            {
                model.ClearSelection2(true);
                mgr.Insert3DSketch(true); open=true;
                var sketch=mgr.ActiveSketch;
                if (sketch==null) throw new InvalidOperationException("3D Sketch did not start.");
                mgr.AddToDB=true; mgr.DisplayWhenAdded=false;
                foreach (Axis ax in layout.Axes)
                {
                    SketchSegment line=mgr.CreateCenterLine(ax.A.X,ax.A.Y,ax.A.Z,ax.B.X,ax.B.Y,ax.B.Z);
                    if(line==null) throw new InvalidOperationException("Could not create axis "+ax.Number);
                    count++;
                }
                var feature=sketch.GetFeature();
                if(feature!=null) feature.Name="TUBE_AXES_3D_"+count;
                mgr.AddToDB=oldAdd; mgr.DisplayWhenAdded=oldDisplay;
                mgr.Insert3DSketch(true);open=false;
                model.GraphicsRedraw2();
            }
            catch(Exception ex)
            {
                if(open) { try { mgr.Insert3DSketch(true); } catch {} }
                throw new InvalidOperationException("Sketch failed after "+count+
                    " axes; Undo/remove any partial sketch. "+ex.Message,ex);
            }
            finally
            {
                try { mgr.AddToDB=oldAdd; mgr.DisplayWhenAdded=oldDisplay; } catch {}
            }
        }

        // Minimum distance between two finite segments (conservative cylinder-axis precheck).
        private static double SegmentGap(Vec p,Vec p2,Vec q,Vec q2)
        {
            Vec u=p2-p,v=q2-q,w=p-q;
            double a=Vec.Dot(u,u),b=Vec.Dot(u,v),c=Vec.Dot(v,v),d=Vec.Dot(u,w),e=Vec.Dot(v,w);
            double D=a*c-b*b,sN,sD=D,tN,tD=D,eps=1e-16;
            if(D<eps){sN=0;sD=1;tN=e;tD=c;}
            else {
                sN=b*e-c*d;tN=a*e-b*d;
                if(sN<0){sN=0;tN=e;tD=c;}
                else if(sN>sD){sN=sD;tN=e+b;tD=c;}
            }
            if(tN<0){tN=0;if(-d<0)sN=0;else if(-d>a)sN=sD;else{sN=-d;sD=a;}}
            else if(tN>tD){tN=tD;if(-d+b<0)sN=0;else if(-d+b>a)sN=sD;else{sN=-d+b;sD=a;}}
            double sc=Math.Abs(sN)<eps?0:sN/sD,tc=Math.Abs(tN)<eps?0:tN/tD;
            return (w+u*sc-v*tc).Length();
        }

        private void InitLog()
        {
            string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TAI_TOOL","CircleFaceTubeLayout");
            Directory.CreateDirectory(folder);
            logPath=Path.Combine(folder,"TUBE_FACE_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff",
                CultureInfo.InvariantCulture)+".log");
        }
        private void Log(string text)
        {
            string s=DateTime.Now.ToString("O",CultureInfo.InvariantCulture)+" "+text+Environment.NewLine;
            debug.Append(s);
            try { if(!string.IsNullOrEmpty(logPath)) File.AppendAllText(logPath,s,Encoding.UTF8); }
            catch {}
        }
        private void Flush()
        {
            try { if(!string.IsNullOrEmpty(logPath)) File.WriteAllText(logPath,debug.ToString(),Encoding.UTF8); }
            catch {}
        }

        private sealed class Rail
        {
            readonly Curve curve;
            readonly double min,max;
            public readonly double Length,Bow;
            public readonly Vec First,Last;
            public Rail(Edge edge)
            {
                curve=(Curve)edge.GetCurve();
                var range=(CurveParamData)edge.GetCurveParams3();
                min=range.UMinValue;max=range.UMaxValue;
                if(max<=min) throw new InvalidOperationException("Invalid curve parameters.");
                Length=Math.Abs(curve.GetLength3(min,max));
                if(Length<1e-8)throw new InvalidOperationException("Zero length edge.");
                First=Evaluate(min);Last=Evaluate(max);
                Vec mid=PointAt(Length/2,false),chord=Last-First;
                double x=Math.Max(0,Math.Min(1,Vec.Dot(mid-First,chord)/
                    Math.Max(1e-20,Vec.Dot(chord,chord))));
                Bow=Vec.Distance(mid,First+chord*x)/Length;
            }
            private Vec Evaluate(double t)
            {
                var p=(double[])curve.Evaluate2(t,0);
                if(p==null || p.Length<3)throw new InvalidOperationException("Curve evaluation failed.");
                return new Vec(p[0],p[1],p[2]);
            }
            public Vec PointAt(double length,bool reverse)
            {
                if(length < -1e-9 || length > Length+1e-9)
                    throw new InvalidOperationException("Arc length out of bound.");
                double target=Math.Max(0,Math.Min(Length,length));
                if(reverse)target=Length-target;
                double a=min,b=max;
                for(int i=0;i<45;i++)
                {
                    double mid=(a+b)/2;
                    if(Math.Abs(curve.GetLength3(min,mid))<target)a=mid;else b=mid;
                }
                return Evaluate((a+b)/2);
            }
        }
        private sealed class Axis { public int Number;public Vec A,B; }
        private sealed class Layout
        {
            public double Small,Large,Diameter,SmallFirst,LargePitch,LargeMargin;
            public bool Flip;
            public int Warnings;
            public readonly List<Axis> Axes=new List<Axis>();
            public string Describe()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Tube Face Layout - numerical PREVIEW\nSmall rail: {0:F3} mm\nLarge rail: {1:F3} mm\n" +
                    "Count: {2}; diameter: {3:F3} mm; flip: {4}\nSmall center margin: {5:F3} mm\n" +
                    "Large center pitch: {6:F3} mm; large EDGE margin: {7:F3} mm\nWarnings: {8}",
                    Small/Mm,Large/Mm,Axes.Count,Diameter/Mm,Flip,SmallFirst/Mm,
                    LargePitch/Mm,LargeMargin/Mm,Warnings);
            }
        }
        private struct Vec
        {
            public readonly double X,Y,Z;
            public Vec(double x,double y,double z){X=x;Y=y;Z=z;}
            public static Vec operator +(Vec a,Vec b){return new Vec(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
            public static Vec operator -(Vec a,Vec b){return new Vec(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
            public static Vec operator *(Vec a,double k){return new Vec(a.X*k,a.Y*k,a.Z*k);}
            public static double Dot(Vec a,Vec b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
            public double Length(){return Math.Sqrt(Dot(this,this));}
            public Vec Unit(){double l=Length();if(l<1e-12)throw new InvalidOperationException("Zero normal.");return this*(1/l);}
            public static double Distance(Vec a,Vec b){return (a-b).Length();}
            public static Vec Lerp(Vec a,Vec b,double t){return a+(b-a)*t;}
            public override string ToString(){return string.Format(CultureInfo.InvariantCulture,
                "({0:F7},{1:F7},{2:F7})m",X,Y,Z);}
        }
    }
}
