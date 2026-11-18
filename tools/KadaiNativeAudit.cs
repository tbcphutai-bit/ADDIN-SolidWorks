// Read-only native audit. Build against the installed SW2024 interop; run in an STA.
// CAD evidence is local only and must never be committed to the public repository.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;

public static class KadaiNativeAudit
{
    static object[] Items(object value) { return value as object[] ?? new object[0]; }
    static object Try(Func<object> read) { try { return read(); } catch (Exception e) { return "UNAVAILABLE: " + e.Message; } }
    static string[] Names(object value) { return Items(value).OfType<Feature>().Select(f => f.Name).ToArray(); }
    static object Properties(CustomPropertyManager p)
    {
        var result = new Dictionary<string, object>();
        foreach (string name in Items(p.GetNames()))
        {
            string raw, resolved; bool wasResolved, linked;
            int status = p.Get6(name, false, out raw, out resolved, out wasResolved, out linked);
            result[name] = new { raw, resolved, wasResolved, linked, status };
        }
        return result;
    }
    static object FeatureInfo(Feature f, ModelDoc2 doc, int depth, HashSet<string> stack)
    {
        var r = new Dictionary<string, object>();
        string type = f.GetTypeName2();
        r["name"] = f.Name; r["type"] = type; r["legacyType"] = f.GetTypeName();
        if (type == "ICE") type = f.GetTypeName();
        r["suppressed"] = Try(() => f.IsSuppressed2(1, null));
        r["parents"] = Try(() => Names(f.GetParents()));
        r["children"] = Try(() => Names(f.GetChildren()));
        r["error"] = Try(() => { bool w; int e = f.GetErrorCode2(out w); return new { error = e, warning = w }; });
        var dims = new List<object>();
        var dd = f.GetFirstDisplayDimension() as DisplayDimension;
        for (int n = 0; dd != null && n < 1000; n++, dd = f.GetNextDisplayDimension(dd) as DisplayDimension)
        {
            var d = dd.GetDimension2(0) as Dimension;
            if (d != null) dims.Add(new { name = d.FullName, systemValue = d.SystemValue });
        }
        r["dimensions"] = dims;
        if (type == "LPattern") r["pattern"] = Try(() => {
            var p = (LinearPatternFeatureData)f.GetDefinition();
            return new { p.D1Spacing, p.D1TotalInstances, p.D1ReverseDirection,
                p.D2Spacing, p.D2TotalInstances, skipped = p.SkippedItemArray };
        });
        if (type == "SheetMetal") r["sheetMetal"] = Try(() => {
            var s = (SheetMetalFeatureData)f.GetDefinition();
            return new { s.Thickness, s.BendRadius, s.KFactor, s.BendAllowanceType, s.BendAllowance, s.BendTableFile };
        });
        if (type == "SMBaseFlange") r["baseFlange"] = Try(() => {
            var s = (BaseFlangeFeatureData)f.GetDefinition();
            return new { s.Thickness, s.BendRadius, s.KFactor, s.ReverseThickness, s.SymmetricThickness };
        });
        if (type == "Cut" || type == "Extrusion" || type == "Boss") r["extrude"] = Try(() => {
            var s = (ExtrudeFeatureData2)f.GetDefinition();
            // Do not roll back the feature tree during a read-only batch audit.
            // Scope entities may remain unresolved and are checked in PropertyManager.
            return new { access = false, depth1 = s.GetDepth(true), depth2 = s.GetDepth(false),
                end1 = s.GetEndCondition(true), end2 = s.GetEndCondition(false), s.ReverseDirection,
                s.BothDirections, s.NormalCut, s.FeatureScope, s.AutoSelect,
                s.LinkToThickness,
                scopeBodies = Items(s.FeatureScopeBodies).OfType<Body2>().Select(b => b.Name).ToArray() };
        });
        if (type == "ProfileFeature" || type == "3DProfileFeature") r["sketch"] = Try(() => {
            var s = (Sketch)f.GetSpecificFeature2();
            var segs = new List<object>();
            foreach (SketchSegment seg in Items(s.GetSketchSegments())) {
                var line = seg.GetType() == 0 ? (SketchLine)seg : null;
                if (line != null) { var a = (SketchPoint)line.GetStartPoint2(); var b = (SketchPoint)line.GetEndPoint2();
                    segs.Add(new { type = seg.GetType(), construction = seg.ConstructionGeometry,
                        start = new[] { a.X, a.Y, a.Z }, end = new[] { b.X, b.Y, b.Z } }); }
                else segs.Add(new { type = seg.GetType(), length = seg.GetLength(), construction = seg.ConstructionGeometry });
            }
            return new { segments = segs, transform = ((MathTransform)s.ModelToSketchTransform).ArrayData,
                relations = Try(() => { var rel = s.RelationManager; return Items(rel.GetRelations(0)).OfType<SketchRelation>()
                    .Select(x => new { type = x.GetRelationType(), entities = x.GetEntitiesCount() }).ToArray(); }) };
        });
        string key = f.Name + ":" + type;
        var sub = new List<object>();
        if (depth < 12 && stack.Add(key)) {
            Feature c = f.GetFirstSubFeature() as Feature;
            for (int n = 0; c != null && n < 1000; n++, c = c.GetNextSubFeature() as Feature)
                sub.Add(FeatureInfo(c, doc, depth + 1, stack));
            stack.Remove(key);
        }
        r["subfeatures"] = sub; return r;
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try {
            var sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
            string expected = System.Environment.GetEnvironmentVariable("KADAI_EXPECTED_SW_PID");
            int expectedPid;
            int actualPid = sw.GetProcessID();
            if (!string.IsNullOrEmpty(expected) && (!int.TryParse(expected, out expectedPid) || actualPid != expectedPid))
                throw new InvalidOperationException("Audit refused: SolidWorks PID differs from KADAI_EXPECTED_SW_PID.");
            Console.WriteLine("SW_PID=" + actualPid + "; UserControl=" + sw.UserControl);
            var initialPaths = new HashSet<string>(Items(sw.GetDocuments()).OfType<ModelDoc2>()
                .Select(d => d.GetPathName()), StringComparer.OrdinalIgnoreCase);
            var all = new Dictionary<string, object>(); all["revision"] = sw.RevisionNumber();
            all["documentsInitiallyOpen"] = Items(sw.GetDocuments()).OfType<ModelDoc2>()
                .Select(d => new { title = d.GetTitle(), path = d.GetPathName(), dirty = d.GetSaveFlag() }).ToArray();
            Console.WriteLine("SW=" + sw.RevisionNumber());
            if (args.Length < 2) return 0;
            var docs = new List<object>();
            foreach (string path in args.Skip(1)) {
                Console.WriteLine("OPEN " + Path.GetFileName(path));
                int err = 0, warn = 0;
                var d = sw.GetOpenDocumentByName(Path.GetFullPath(path)) as ModelDoc2;
                if (d == null) d = sw.OpenDoc6(Path.GetFullPath(path), path.EndsWith("SLDASM", StringComparison.OrdinalIgnoreCase) ? 2 : 1, 3, "", ref err, ref warn) as ModelDoc2;
                if (d == null) { docs.Add(new { requested = path, error = err, warning = warn }); continue; }
                if (!string.Equals(Path.GetFullPath(path), d.GetPathName(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("OpenDoc returned a different path: " + d.GetPathName());
                var r = new Dictionary<string, object>(); r["path"] = d.GetPathName(); r["type"] = d.GetType();
                r["openError"] = err; r["openWarning"] = warn; r["dirtyBeforeAudit"] = d.GetSaveFlag();
                r["properties"] = Properties(d.Extension.CustomPropertyManager[""]);
                r["configurations"] = Items(d.GetConfigurationNames()).Select(c => new { name = c,
                    properties = Properties(d.Extension.CustomPropertyManager[(string)c]) }).ToArray();
                var part = d as PartDoc;
                if (part != null) {
                    string db; string material = part.GetMaterialPropertyName2("", out db);
                    r["material"] = new { material, database = db };
                    r["bodies"] = Items(part.GetBodies2(-1, false)).OfType<Body2>().Select(b => new {
                        name = b.Name, type = b.GetType(), sheetMetal = b.IsSheetMetal(), box = b.GetBodyBox(),
                        faces = Items(b.GetFaces()).OfType<Face2>().Select(face => new { area = face.GetArea(),
                            surfaceType = ((Surface)face.GetSurface()).Identity(), box = face.GetBox(),
                            persistentId = Try(() => Convert.ToBase64String((byte[])d.Extension.GetPersistReference3(face))) }).ToArray() }).ToArray();
                }
                var assembly = d as AssemblyDoc;
                if (assembly != null) r["components"] = Items(assembly.GetComponents(false)).OfType<Component2>().Select(c => new {
                    name = c.Name2, path = c.GetPathName(), config = c.ReferencedConfiguration,
                    transform = c.Transform2 == null ? null : c.Transform2.ArrayData }).ToArray();
                var features = new List<object>();
                for (Feature f = d.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                    features.Add(FeatureInfo(f, d, 0, new HashSet<string>()));
                r["features"] = features;
                r["dependencies"] = Try(() => d.GetDependencies2(true, true, false));
                r["dirtyAfterAudit"] = d.GetSaveFlag(); docs.Add(r);
                Console.WriteLine(Path.GetFileName(path) + " features=" + features.Count);
                all["documents"] = docs;
                File.WriteAllText(args[0], new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 200 }.Serialize(all));
                // Bound memory during batch audits. Never close a pre-existing document.
                if (Path.GetFileName(path).StartsWith("KADAI_TEST__", StringComparison.Ordinal)
                    && !initialPaths.Contains(d.GetPathName()))
                    sw.CloseDoc(d.GetPathName());
            }
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

