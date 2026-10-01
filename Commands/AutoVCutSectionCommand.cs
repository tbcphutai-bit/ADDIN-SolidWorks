using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal enum VCutMarkType { None, Circle, DoubleCircle, FilledCircle }

    internal sealed class VCutDefinition
    {
        public string PropertyName;
        public double ValueMm;
        public VCutMarkType MarkType;
        public string BlockFileName;
        public string Mark;
    }

    internal sealed class VCutPropertySet
    {
        public readonly List<VCutDefinition> Definitions = new List<VCutDefinition>();

        public VCutDefinition MatchFeature(Feature feature, VCutFeatureTagStore tags,
            double valueMm, out string reason)
        {
            string kind = tags.Read(feature);
            if (string.IsNullOrEmpty(kind))
            {
                reason = "FEATURE_KIND_NOT_SET";
                return null;
            }
            VCutDefinition definition = Definitions.FirstOrDefault(d =>
                string.Equals(d.PropertyName, kind, StringComparison.Ordinal));
            if (definition == null)
            {
                reason = "MISSING_PROPERTY_" + kind;
                return null;
            }
            if (Math.Abs(definition.ValueMm - valueMm) > AutoVCutSectionCommand.ToleranceMm)
            {
                reason = "BEND_VALUE_MISMATCH_" + kind + "=" +
                    definition.ValueMm.ToString("0.###", CultureInfo.InvariantCulture);
                return null;
            }
            reason = "MATCH_" + kind;
            return definition;
        }

    }

    internal sealed class DrawingBendInfo
    {
        public double StartX, StartY, EndX, EndY, MidX, MidY, Length, AngleDeg;
    }

    internal sealed class VCutBendInfo
    {
        public Feature Feature;
        public string FeatureName, FeatureType, BendTableName, TableSource;
        public double BendRadiusMm, BendAngleDeg, VCutValueMm;
        public double MaterialThicknessMm;
        public double? ProjectedAxisDeg;
        public readonly List<DrawingBendInfo> ProjectedBendSpans = new List<DrawingBendInfo>();
        public DrawingBendInfo ModelLine;
        public readonly List<DrawingBendInfo> ModelLines = new List<DrawingBendInfo>();
        public DrawingBendInfo DrawingBend;
        public VCutDefinition MatchedDefinition;
        public string MatchReason;
        public double? ProjectedCenterX;
        public double? ProjectedCenterY;
        public double? ProjectedCenterZ;
    }

    internal sealed class ProjectedVertexInfo
    {
        public Entity Entity;
        public double X, Y, Z;
    }

    internal sealed class ProjectedSegmentInfo
    {
        public double X1, Y1, X2, Y2;
        public double Length
        {
            get
            {
                double dx = X2 - X1, dy = Y2 - Y1;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }
    }

    internal static class GrooveBendValueReader
    {
        public static bool TryReadMillimetres(string fileName, out double mm)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            int start = fileName.IndexOf('溝');
            int end = fileName.IndexOf('残', start < 0 ? 0 : start + 1);
            if (start < 0 || end <= start + 1) return false;
            string number = fileName.Substring(start + 1, end - start - 1).Trim().Replace(',', '.');
            return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out mm)
                && mm > 0 && !double.IsNaN(mm) && !double.IsInfinity(mm);
        }
    }

    internal static class VCutBlockResolver
    {
        // SOLIDWORKS requires a path to a .sldblk file. Keep the templates in
        // ADDIN.dll and materialize them in a versioned per-user cache on demand.
        private static readonly object ExtractionLock = new object();

        public static string GetBlockPath(VCutDefinition definition)
        {
            if (definition == null) return null;
            string resourceName;
            switch (definition.MarkType)
            {
                case VCutMarkType.Circle: resourceName = "ADDIN.Blocks.Circle.sldblk"; break;
                case VCutMarkType.DoubleCircle: resourceName = "ADDIN.Blocks.DoubleCircle.sldblk"; break;
                case VCutMarkType.FilledCircle: resourceName = "ADDIN.Blocks.FilledCircle.sldblk"; break;
                default: return null;
            }
            try
            {
                byte[] bytes;
                using (Stream resource = typeof(AutoVCutSectionCommand).Assembly
                    .GetManifestResourceStream(resourceName))
                {
                    if (resource == null) return null;
                    using (var buffer = new MemoryStream())
                    {
                        resource.CopyTo(buffer);
                        bytes = buffer.ToArray();
                    }
                }
                string hash;
                using (SHA256 sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                string cache = Path.Combine(System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.LocalApplicationData), "TAI Tool",
                    "BlockTemplates", hash);
                string path = Path.Combine(cache, definition.BlockFileName);
                lock (ExtractionLock)
                {
                    if (File.Exists(path))
                    {
                        using (SHA256 sha = SHA256.Create())
                            if (sha.ComputeHash(File.ReadAllBytes(path)).SequenceEqual(
                                sha.ComputeHash(bytes))) return path;
                    }
                    Directory.CreateDirectory(cache);
                    File.WriteAllBytes(path, bytes);
                    return path;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[VCUT SECTION] BLOCK TEMPLATE ERROR " +
                    resourceName + " " + ex);
                return null;
            }
        }
    }

    // A selected drawing line creates a section; selecting only a view runs diagnostics.
    internal sealed class AutoVCutSectionCommand
    {
        internal const double ToleranceMm = 0.0005;
        private readonly ISldWorks app;
        public AutoVCutSectionCommand(ISldWorks swApp) { app = swApp; }

        public void Run()
        {
            ModelDoc2 drawing = app == null ? null : app.ActiveDoc as ModelDoc2;
            if (drawing == null || drawing.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            { Notify("Chỉ chạy trên Drawing đang mở."); return; }

            SelectionMgr selections = drawing.SelectionManager as SelectionMgr;
            SolidWorks.Interop.sldworks.View selectedView = null;
            SketchSegment selectedCutLine = null;
            SolidWorks.Interop.sldworks.View cutLineView = null;
            int viewCount = 0, lineCount = 0;
            int selectionCount = selections == null ? 0 : selections.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= selectionCount; i++)
            {
                object picked = selections.GetSelectedObject6(i, -1);
                var view = picked as SolidWorks.Interop.sldworks.View;
                if (view != null) { selectedView = view; viewCount++; }
                SketchSegment line = picked as SketchSegment;
                if (line is SketchLine)
                {
                    selectedCutLine = line;
                    lineCount++;
                    cutLineView = selections.GetSelectedObjectsDrawingView2(i, -1);
                }
            }
            if (lineCount == 1) selectedView = cutLineView;
            if (lineCount > 1 || (lineCount == 0 && viewCount != 1) ||
                (lineCount == 1 && selectedView == null))
            { Notify("Hãy chọn một đường Line đã vẽ trong Drawing View của chi tiết, hoặc chọn View để chỉ phân tích."); return; }

            ModelDoc2 part = selectedView.ReferencedDocument as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART)
            { Notify("View đã chọn phải tham chiếu một file Part, không phải sheet hoặc Assembly."); return; }

            string config = selectedView.ReferencedConfiguration ?? "";
            Log("ANALYZE view=" + selectedView.Name + " part=" + part.GetPathName() + " config=" + config);
            bool isFlatPatternView = selectedView.IsFlatPatternView();
            Log("Selected view IsFlatPatternView=" + isFlatPatternView);
            if (selectedCutLine != null && isFlatPatternView)
            { Notify("Hãy vẽ/chọn đường cắt trong Drawing View của chi tiết 3D (không phải Flat Pattern)."); return; }
            VCutPropertySet props = ReadProperties(part, config);
            VCutFeatureTagStore featureTags = new VCutFeatureTagStore(app, part);
            if (props.Definitions.Count == 0)
                Log("No V-CUT Custom Property; crossed groove bends must be configured before creating a section.");

            // A normal folded-part view has no flat-pattern Bend Lines.  In that view,
            // GetBendLines() == null is expected and cannot be used as a failure signal.
            List<DrawingBendInfo> lines = isFlatPatternView
                ? ReadDrawingBendLines(selectedView) : new List<DrawingBendInfo>();
            List<VCutBendInfo> features = ReadModelBends(part, selectedView, isFlatPatternView);
            Log("Drawing Bend Line count=" + lines.Count + " Model groove candidate count=" + features.Count);
            if (!isFlatPatternView)
            {
                double[] parentUraNormal;
                TryGetUraViewNormal(part, selectedView, config, out parentUraNormal);
                int candidates = 0, unassignedOrMismatched = 0, missingCandidateBlocks = 0;
                foreach (VCutBendInfo bend in features)
                {
                    string matchReason;
                    bend.MatchedDefinition = props.MatchFeature(
                        bend.Feature, featureTags, bend.VCutValueMm, out matchReason);
                    bend.MatchReason = matchReason;
                    if (bend.MatchedDefinition == null)
                    {
                        unassignedOrMismatched++;
                        Log("3D CANDIDATE Feature=" + bend.FeatureName + " Type=" + bend.FeatureType +
                            " Table=" + bend.BendTableName + " Remain=" + F(bend.VCutValueMm) +
                            "mm " + matchReason);
                        // Geometry is needed even for an unassigned bend so the
                        // selected section line can report the missing setup rather
                        // than quietly creating a view without its circle mark.
                        LogBendGeometry(bend, selectedView);
                        continue;
                    }
                    candidates++;
                    VCutDefinition definition = bend.MatchedDefinition;
                    string block = VCutBlockResolver.GetBlockPath(definition);
                    if (block == null) missingCandidateBlocks++;
                    Log("3D CANDIDATE Feature=" + bend.FeatureName + " Type=" + bend.FeatureType +
                        " Radius=" + F(bend.BendRadiusMm) + "mm Angle=" + F(bend.BendAngleDeg) +
                        "deg Table=" + bend.BendTableName + " source=" + bend.TableSource +
                        " Remain=" + F(bend.VCutValueMm) + "mm Property=" + definition.PropertyName +
                        "=" + F(definition.ValueMm) + " Mark=" + definition.Mark +
                        " Block=" + (block ?? "MISSING"));
                    LogBendGeometry(bend, selectedView);
                }
                if (selectedCutLine != null)
                {
                    if (features.Count > 0 && props.Definitions.Count == 0)
                    {
                        Notify("Part có Bend Table 溝 nhưng chưa có Custom Property " +
                            "V溝1 / V溝2 / C溝. Chưa tạo mặt cắt để tránh thiếu dấu.\n\n" +
                            "Ví dụ: nhập V溝1=0.8 mm, chọn cạnh bào tương ứng trong " +
                            "Set Bend line, rồi lưu Part. Loại ○ / ◎ / ● do bạn quyết định.");
                        return;
                    }
                    CreateSectionFromSelectedLine(drawing, selectedView, selectedCutLine, features);
                    return;
                }
                Log("3D VIEW: no flat-pattern Bend Lines expected; section position on folded geometry is not mapped yet.");
                Notify("Phân tích V-CUT SECTION trên View 3D (debug)\n\n" +
                    "Groove feature candidates: " + features.Count +
                    "\nProperty-matched candidates: " + candidates +
                    "\nUnassigned/mismatched bends: " + unassignedOrMismatched +
                    "\nMissing blocks: " + missingCandidateBlocks +
                    "\nSections created: 0\n\n" +
                    "Đây là số feature, chưa phải số bend duy nhất. Xem Output > Debug để kiểm tra bảng uốn và dấu.");
                return;
            }
            int mapped = 0, matched = 0, ambiguous = 0, missingBlocks = 0;
            var usedFeatures = new HashSet<VCutBendInfo>();
            for (int i = 0; i < lines.Count; i++)
            {
                DrawingBendInfo line = lines[i];
                Log("Bend #" + (i + 1) + " mid=(" + Mm(line.MidX) + "," + Mm(line.MidY) + ")mm angle=" + F(line.AngleDeg) + "deg");
                VCutBendInfo bend = MatchLine(line, features, usedFeatures, out bool mappingAmbiguous);
                if (bend == null)
                {
                    if (mappingAmbiguous) ambiguous++;
                    Log(mappingAmbiguous ? "AMBIGUOUS model bend" : "NO RELIABLE MODEL MATCH");
                    continue;
                }
                mapped++;
                usedFeatures.Add(bend);
                bend.DrawingBend = line;
                Log("Feature=" + bend.FeatureName + " Type=" + bend.FeatureType +
                    " Radius=" + F(bend.BendRadiusMm) + "mm Angle=" + F(bend.BendAngleDeg) +
                    "deg Table=" + bend.BendTableName + " source=" + bend.TableSource +
                    " Remain=" + F(bend.VCutValueMm) + "mm");
                string matchReason;
                bend.MatchedDefinition = props.MatchFeature(
                    bend.Feature, featureTags, bend.VCutValueMm, out matchReason);
                bend.MatchReason = matchReason;
                if (bend.MatchedDefinition == null)
                {
                    Log((matchReason == "FEATURE_KIND_NOT_SET" ? "UNASSIGNED " : "NG ") +
                        matchReason + " Bend=" + F(bend.VCutValueMm) + "mm");
                    continue;
                }
                matched++;
                VCutDefinition definition = bend.MatchedDefinition;
                string block = VCutBlockResolver.GetBlockPath(definition);
                if (block == null) missingBlocks++;
                Log("Property=" + definition.PropertyName + "=" + F(definition.ValueMm) +
                    " Mark=" + definition.Mark + " Block=" + (block ?? "MISSING") +
                    " Section=DEBUG_ONLY BlockInsertion=DEBUG_ONLY");
            }

            Notify("Phân tích V-CUT SECTION (debug)\n\nBend lines: " + lines.Count +
                "\nGroove features: " + features.Count + "\nMapped: " + mapped +
                "\nMatched: " + matched + "\nAmbiguous: " + ambiguous +
                "\nMissing blocks: " + missingBlocks +
                "\nSections created: 0\n\nXem Output > Debug để kiểm tra mapping trước khi bật tạo Section.");
        }

        private VCutPropertySet ReadProperties(ModelDoc2 part, string config)
        {
            var result = new VCutPropertySet();
            AddProperty(part, config, result, "V溝1", VCutMarkType.Circle, "○", "プレーナー○印.sldblk");
            AddProperty(part, config, result, "V溝2", VCutMarkType.DoubleCircle, "◎", "プレーナー◎印.sldblk");
            AddProperty(part, config, result, "C溝", VCutMarkType.FilledCircle, "●", "プレーナー●印.sldblk");
            return result;
        }

        private static void AddProperty(ModelDoc2 part, string config, VCutPropertySet set,
            string name, VCutMarkType mark, string symbol, string block)
        {
            string value = ReadProperty(part, config, name);
            Log("Custom Property " + name + "=" + value);
            double mm;
            if (double.TryParse((value ?? "").Trim().Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out mm) && mm > 0 && !double.IsInfinity(mm))
                set.Definitions.Add(new VCutDefinition { PropertyName = name, ValueMm = mm,
                    MarkType = mark, Mark = symbol, BlockFileName = block });
        }

        private static string ReadProperty(ModelDoc2 part, string config, string name)
        {
            foreach (string scope in new[] { config ?? "", "" }.Distinct())
            {
                var manager = part.Extension.get_CustomPropertyManager(scope);
                if (manager == null) continue;
                string raw, resolved; bool wasResolved, linkToParent;
                manager.Get6(name, false, out raw, out resolved, out wasResolved, out linkToParent);
                string value = !string.IsNullOrWhiteSpace(resolved) ? resolved : raw;
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return "";
        }

        private List<DrawingBendInfo> ReadDrawingBendLines(SolidWorks.Interop.sldworks.View view)
        {
            var result = new List<DrawingBendInfo>();
            Array segments = view.GetBendLines() as Array;
            if (segments == null) return result;
            foreach (object obj in segments)
            {
                SketchSegment segment = obj as SketchSegment;
                DrawingBendInfo line = TransformLine(segment, view.ModelToViewTransform);
                if (line != null) result.Add(line);
            }
            return result;
        }

        private List<VCutBendInfo> ReadModelBends(ModelDoc2 part, SolidWorks.Interop.sldworks.View view,
            bool readFlatPatternLines)
        {
            var result = new List<VCutBendInfo>();
            var defaults = new List<BendAllowanceInfo>();
            var bendFeatures = new List<Feature>();
            var thicknesses = new HashSet<double>();
            // Walk subfeatures as CheckKegaki does; do not infer a mapping from enumeration order.
            for (Feature root = part.FirstFeature() as Feature; root != null; root = root.GetNextFeature() as Feature)
                CollectFeature(root, defaults, bendFeatures, thicknesses);
            double modelThicknessMm = thicknesses.Count == 1 ? thicknesses.First() : 0;
            string uniqueDefault = null;
            foreach (BendAllowanceInfo allowance in defaults)
            {
                if (allowance == null || !allowance.HasBendTableFile()) continue;
                string name = allowance.GetBendTableFilePath();
                if (uniqueDefault == null) uniqueDefault = name;
                else if (!string.Equals(uniqueDefault, name, StringComparison.OrdinalIgnoreCase))
                    uniqueDefault = "";
            }
            foreach (Feature feature in bendFeatures)
            {
                try
                {
                    string type = feature.GetTypeName2() ?? "";
                    object data = feature.GetDefinition();
                    IOneBendFeatureData one = data as IOneBendFeatureData;
                    IEdgeFlangeFeatureData flange = data as IEdgeFlangeFeatureData;
                    ISketchedBendFeatureData sketch = data as ISketchedBendFeatureData;
                    if (one == null && flange == null && sketch == null)
                    { Log("SKIP unsupported bend data " + feature.Name + " type=" + type); continue; }
                    bool useDefault = one != null ? one.UseDefaultBendAllowance :
                        flange != null ? flange.UseDefaultBendAllowance : sketch.UseDefaultBendAllowance;
                    object custom = one != null ? one.GetCustomBendAllowance() :
                        flange != null ? flange.GetCustomBendAllowance() : sketch.GetCustomBendAllowance();
                    BendAllowanceInfo info = BendAllowanceInfo.Capture(custom);
                    string table = info != null && info.HasBendTableFile()
                        ? info.GetBendTableFilePath()
                        : useDefault ? uniqueDefault : null;
                    if (string.IsNullOrWhiteSpace(table)) { Log("SKIP unknown bend table " + feature.Name); continue; }
                    string resolved, tableError;
                    double tableThickness, remain;
                    bool isGroove;
                    if (!BendTableCellReader.TryRead(table, modelThicknessMm, out resolved,
                        out tableThickness, out remain, out isGroove, out tableError))
                    { Log("SKIP unreadable bend table " + feature.Name + " table=" + table + " reason=" + tableError); continue; }
                    if (!isGroove)
                    { Log("SKIP normal bend table " + feature.Name + " table=" + resolved); continue; }
                    var bend = new VCutBendInfo { Feature = feature, FeatureName = feature.Name, FeatureType = type,
                        BendTableName = resolved, TableSource = info != null && info.HasBendTableFile() ? "OVERRIDE" : "DEFAULT",
                        BendRadiusMm = Math.Abs((one != null ? one.BendRadius :
                            flange != null ? flange.BendRadius : sketch.BendRadius) * 1000),
                        BendAngleDeg = Math.Abs((one != null ? one.BendAngle :
                            flange != null ? flange.BendAngle : sketch.BendAngle) * 180 / Math.PI),
                        VCutValueMm = remain,
                        MaterialThicknessMm = modelThicknessMm > 0 ? modelThicknessMm : tableThickness };
                    Log("BEND TABLE " + feature.Name + " 厚み=" + F(tableThickness) +
                        "mm 溝=" + F(remain) + "mm file=" + resolved);
                    if (one != null && readFlatPatternLines)
                    {
                        Array modelSegments = one.FlatPatternSketchSegments2 as Array;
                        if (modelSegments != null)
                            foreach (object segment in modelSegments)
                            {
                                DrawingBendInfo modelLine = TransformLine(segment as SketchSegment, view.ModelToViewTransform);
                                if (modelLine != null) bend.ModelLines.Add(modelLine);
                            }
                    }
                    bend.ModelLine = bend.ModelLines.Count > 0 ? bend.ModelLines[0] : null;
                    if (readFlatPatternLines && bend.ModelLines.Count == 0)
                        Log("NO MODEL BEND LINE geometry " + bend.FeatureName);
                    result.Add(bend);
                }
                catch (Exception ex) { Log("BEND READ ERROR " + feature.Name + " " + ex.Message); }
            }
            return result;
        }

        // The small-radius cylindrical face identifies the V-CUT bend. Its projected
        // center is used to choose the closest corner in the created section view.
        private void LogBendGeometry(VCutBendInfo bend, SolidWorks.Interop.sldworks.View view)
        {
            bend.ProjectedBendSpans.Clear();
            try
            {
                Array faces = bend.Feature == null ? null : bend.Feature.GetFaces() as Array;
                if (faces == null) { Log("3D GEOMETRY " + bend.FeatureName + " faces=0"); return; }
                MathTransform transform = view.ModelToViewTransform;
                MathUtility math = app.IGetMathUtility();
                int cylinderCount = 0;
                foreach (object item in faces)
                {
                    Face2 face = item as Face2;
                    Surface surface = face == null ? null : face.GetSurface() as Surface;
                    if (surface == null || !surface.IsCylinder()) continue;
                    double[] cylinder = surface.CylinderParams as double[];
                    if (cylinder == null || cylinder.Length < 7) continue;
                    cylinderCount++;
                    double[] center = null;
                    double[] box = face.GetBox() as double[];
                    if (box != null && box.Length >= 6)
                        center = TransformModelPoint(math, transform,
                            (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2);
                    double[] origin = TransformModelPoint(math, transform, cylinder[0], cylinder[1], cylinder[2]);
                    double[] along = TransformModelPoint(math, transform,
                        cylinder[0] + cylinder[3], cylinder[1] + cylinder[4], cylinder[2] + cylinder[5]);
                    if (origin == null || along == null) continue;
                    double dx = along[0] - origin[0], dy = along[1] - origin[1], dz = along[2] - origin[2];
                    double projected = Math.Sqrt(dx * dx + dy * dy);
                    double full = Math.Sqrt(projected * projected + dz * dz);
                    string axis = projected < 1e-9 ? "END_ON" : F((Math.Atan2(dy, dx) * 180 / Math.PI + 180) % 180) + "deg";
                    string cut = projected < 1e-9 ? "UNAVAILABLE" : F((Math.Atan2(dy, dx) * 180 / Math.PI + 270) % 180) + "deg";
                    if (projected >= 1e-9 && full > 0 && projected / full >= 0.95 &&
                        (!bend.ProjectedAxisDeg.HasValue ||
                         Math.Abs(cylinder[6] * 1000 - bend.BendRadiusMm) < 0.05))
                        bend.ProjectedAxisDeg = (Math.Atan2(dy, dx) * 180 / Math.PI + 180) % 180;
                    if (center != null && Math.Abs(cylinder[6] * 1000 - bend.BendRadiusMm) < 0.05)
                    {
                        bend.ProjectedCenterX = center[0];
                        bend.ProjectedCenterY = center[1];
                        bend.ProjectedCenterZ = center.Length > 2 ? center[2] : 0;
                        DrawingBendInfo span = ProjectBendSpan(face, cylinder, math, transform);
                        if (span != null)
                        {
                            bend.ProjectedBendSpans.Add(span);
                            Log("3D BEND SPAN " + bend.FeatureName + " from=(" +
                                Mm(span.StartX) + "," + Mm(span.StartY) + ")mm to=(" +
                                Mm(span.EndX) + "," + Mm(span.EndY) + ")mm");
                        }
                    }
                    Log("3D GEOMETRY " + bend.FeatureName + " cylinder#" + cylinderCount +
                        " radius=" + F(cylinder[6] * 1000) + "mm" +
                        " approxViewCenter=" + (center == null ? "UNKNOWN" :
                            "(" + Mm(center[0]) + "," + Mm(center[1]) + ")mm") +
                        " projectedAxis=" + axis + " perpendicularCut=" + cut +
                        " axisInViewRatio=" + (full < 1e-9 ? "UNKNOWN" : F(projected / full)));
                }
                Log("3D GEOMETRY " + bend.FeatureName + " cylindricalFaces=" + cylinderCount);
            }
            catch (Exception ex) { Log("3D GEOMETRY ERROR " + bend.FeatureName + " " + ex.Message); }
        }

        // A section may cross anywhere along a bend, not only at its midpoint.
        // Use each trimmed cylindrical face's axial extent independently.
        private static DrawingBendInfo ProjectBendSpan(Face2 face, double[] cylinder,
            MathUtility math, MathTransform transform)
        {
            double norm = Math.Sqrt(cylinder[3] * cylinder[3] +
                cylinder[4] * cylinder[4] + cylinder[5] * cylinder[5]);
            if (norm < 1e-12) return null;
            double ux = cylinder[3] / norm, uy = cylinder[4] / norm, uz = cylinder[5] / norm;
            double min = double.MaxValue, max = double.MinValue;
            Array edges = face.GetEdges() as Array;
            if (edges == null) return null;
            foreach (object item in edges)
            {
                Edge edge = item as Edge;
                if (edge == null) continue;
                foreach (Vertex vertex in new[] { edge.GetStartVertex() as Vertex,
                    edge.GetEndVertex() as Vertex })
                {
                    double[] point = vertex == null ? null : vertex.GetPoint() as double[];
                    if (point == null || point.Length < 3) continue;
                    double t = (point[0] - cylinder[0]) * ux +
                        (point[1] - cylinder[1]) * uy + (point[2] - cylinder[2]) * uz;
                    min = Math.Min(min, t); max = Math.Max(max, t);
                }
            }
            if (min == double.MaxValue || max - min < 1e-9) return null;
            double[] a = TransformModelPoint(math, transform, cylinder[0] + min * ux,
                cylinder[1] + min * uy, cylinder[2] + min * uz);
            double[] b = TransformModelPoint(math, transform, cylinder[0] + max * ux,
                cylinder[1] + max * uy, cylinder[2] + max * uz);
            if (a == null || b == null) return null;
            return new DrawingBendInfo { StartX = a[0], StartY = a[1], EndX = b[0], EndY = b[1] };
        }

        // Preview only: a proposed section line must be reviewed against the
        // actual profile before CreateSectionViewAt5 changes the Drawing.
        private static void LogProposedSections(SolidWorks.Interop.sldworks.View view,
            List<VCutBendInfo> matchedBends)
        {
            double[] outline = view.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
            { Log("SECTION PLAN unavailable: no view outline"); return; }
            double cx = (outline[0] + outline[2]) / 2;
            double cy = (outline[1] + outline[3]) / 2;
            double halfLength = Math.Sqrt(Math.Pow(outline[2] - outline[0], 2) +
                Math.Pow(outline[3] - outline[1], 2)) / 2 + 0.010;
            var axes = new List<double>();
            foreach (VCutBendInfo bend in matchedBends)
            {
                if (!bend.ProjectedAxisDeg.HasValue)
                { Log("SECTION PLAN SKIP " + bend.FeatureName + " no reliable projected axis"); continue; }
                double axis = bend.ProjectedAxisDeg.Value;
                if (axes.Any(existing => Math.Min(Math.Abs(existing - axis), 180 - Math.Abs(existing - axis)) <= 2))
                    continue;
                axes.Add(axis);
                double cutRadians = (axis + 90) * Math.PI / 180;
                double dx = Math.Cos(cutRadians) * halfLength;
                double dy = Math.Sin(cutRadians) * halfLength;
                Log("SECTION PLAN PREVIEW #" + axes.Count + " bend=" + bend.FeatureName +
                    " axis=" + F(axis) + "deg cut=" + F((axis + 90) % 180) +
                    "deg from=(" + Mm(cx - dx) + "," + Mm(cy - dy) +
                    ")mm to=(" + Mm(cx + dx) + "," + Mm(cy + dy) +
                    ")mm viewOutline=(" + Mm(outline[0]) + "," + Mm(outline[1]) +
                    ")- (" + Mm(outline[2]) + "," + Mm(outline[3]) + ")mm");
            }
        }

        private void CreateSectionFromSelectedLine(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View parent, SketchSegment selectedLine,
            List<VCutBendInfo> bends)
        {
            DrawingDoc drawingDoc = drawing as DrawingDoc;
            if (drawingDoc == null) return;
            DrawingBendInfo cut = TransformDrawingViewSketchLineToSheet(selectedLine);
            if (cut == null)
            { Notify("Không đọc được hình học đường Line đã chọn trong Drawing View."); return; }
            Log("MANUAL SECTION LINE from=(" + Mm(cut.StartX) + "," + Mm(cut.StartY) +
                ")mm to=(" + Mm(cut.EndX) + "," + Mm(cut.EndY) +
                ")mm angle=" + F(cut.AngleDeg) + "deg");
            // A part can contain several parallel bends. Orientation alone is not
            // enough: only bends physically crossed by the selected finite section
            // line receive a mark. The along-line value also gives deterministic
            // ordering when one section crosses several V-CUT types.
            const double cutHitTolerance = 0.003;
            var hits = new List<Tuple<VCutBendInfo, double>>();
            var ngBends = new List<string>();
            double nearestBendDistance = double.MaxValue;
            foreach (VCutBendInfo bend in bends)
            {
                if (!bend.ProjectedAxisDeg.HasValue)
                    continue;
                double angleDifference = UndirectedAngleDifference(cut.AngleDeg,
                    bend.ProjectedAxisDeg.Value + 90);
                if (angleDifference > 2)
                {
                    Log("MANUAL SECTION V-CUT REJECT feature=" + bend.FeatureName +
                        " reason=ANGLE delta=" + F(angleDifference) + "deg");
                    continue;
                }
                if (bend.ProjectedBendSpans.Count == 0)
                {
                    Log("MANUAL SECTION V-CUT REJECT feature=" + bend.FeatureName +
                        " reason=NO_PROJECTED_SPAN");
                    ngBends.Add(bend.FeatureName + ": không đọc được chiều dài cạnh bẻ để kiểm tra đường cắt");
                    continue;
                }
                double along = 0;
                double distance = double.MaxValue;
                foreach (DrawingBendInfo span in bend.ProjectedBendSpans)
                {
                    double candidateAlong;
                    double candidateDistance = DistanceSegmentToSegment(cut, span, out candidateAlong);
                    if (candidateDistance < distance)
                    { distance = candidateDistance; along = candidateAlong; }
                }
                nearestBendDistance = Math.Min(nearestBendDistance, distance);
                if (distance > cutHitTolerance)
                {
                    Log("MANUAL SECTION V-CUT REJECT feature=" + bend.FeatureName +
                        " property=" + (bend.MatchedDefinition == null ?
                            bend.MatchReason : bend.MatchedDefinition.PropertyName) +
                        " mark=" + (bend.MatchedDefinition == null ? "NG" :
                            bend.MatchedDefinition.Mark) + " reason=MISS distance=" +
                        Mm(distance) + "mm tolerance=" + Mm(cutHitTolerance) + "mm");
                    continue;
                }
                if (bend.MatchedDefinition == null)
                {
                    string issue = bend.MatchReason == "FEATURE_KIND_NOT_SET"
                        ? "chưa gán V溝1 / V溝2 / C溝 trong Set Bend line"
                        : bend.MatchReason != null &&
                            bend.MatchReason.StartsWith("MISSING_PROPERTY_")
                            ? "thiếu Custom Property " +
                                bend.MatchReason.Substring("MISSING_PROPERTY_".Length)
                            : bend.MatchReason != null &&
                                bend.MatchReason.StartsWith("BEND_VALUE_MISMATCH_")
                                ? "NG — " + bend.MatchReason.Substring(
                                    "BEND_VALUE_MISMATCH_".Length)
                                : bend.MatchReason ?? "thiếu thông tin gán bào";
                    ngBends.Add(bend.FeatureName + ": 溝=" +
                        F(bend.VCutValueMm) + " mm; " + issue);
                    continue;
                }
                hits.Add(Tuple.Create(bend, along));
                Log("MANUAL SECTION V-CUT HIT feature=" + bend.FeatureName +
                    " property=" + bend.MatchedDefinition.PropertyName +
                    " mark=" + bend.MatchedDefinition.Mark + " distance=" +
                    Mm(distance) + "mm along=" + F(along));
            }
            List<VCutBendInfo> relevant = hits.OrderBy(hit => hit.Item2)
                .Select(hit => hit.Item1).ToList();
            if (ngBends.Count > 0)
            {
                Log("MANUAL SECTION SETUP REQUIRED " + string.Join("; ", ngBends));
                Notify("Chưa tạo mặt cắt vì cạnh bào chưa sẵn sàng:\n" +
                    string.Join("\n", ngBends) +
                    "\n\nKiểm tra thông tin cạnh nêu trên trong Set Bend line rồi thử lại.");
                return;
            }
            if (bends.Count > 0 && relevant.Count == 0)
            {
                string distanceText = nearestBendDistance == double.MaxValue ?
                    "không đo được" : Mm(nearestBendDistance) + " mm";
                Log("MANUAL SECTION NO GROOVE HIT nearest=" + distanceText +
                    " tolerance=" + Mm(cutHitTolerance) + "mm");
                Notify("Đã nhận đường cắt, nhưng không tìm thấy giao với đoạn cạnh bào phù hợp. " +
                    "Khoảng cách gần nhất: " + distanceText +
                    "; giới hạn nhận dạng: " + Mm(cutHitTolerance) +
                    " mm. Hãy kiểm tra đường cắt có giao và vuông góc với cạnh bào cần tạo mặt cắt.");
                return;
            }
            foreach (VCutBendInfo bend in relevant)
            {
                if (VCutBlockResolver.GetBlockPath(bend.MatchedDefinition) != null) continue;
                Log("MANUAL SECTION TEMPLATE MISSING feature=" + bend.FeatureName +
                    " mark=" + bend.MatchedDefinition.Mark);
                Notify("Chưa tạo mặt cắt: không đọc được mẫu dấu " +
                    bend.MatchedDefinition.Mark + " từ ADDIN.dll. " +
                    "Xem Output > Debug để biết lỗi tải mẫu.");
                return;
            }
            Log("MANUAL SECTION V-CUT candidate count=" + relevant.Count +
                " features=" + string.Join(",", relevant.Select(b => b.FeatureName +
                    ":" + b.MatchedDefinition.PropertyName + b.MatchedDefinition.Mark)));
            double[] outline = parent.GetOutline() as double[];
            if (outline == null || outline.Length < 4)
            { Notify("Không đọc được giới hạn Drawing View để đặt mặt cắt."); return; }
            string label = NextAvailableSectionLabel(drawingDoc);
            if (label == null)
            { Notify("Không tìm được nhãn mặt cắt còn trống."); return; }
            if (!drawingDoc.ActivateView(parent.GetName2()))
            { Notify("Không kích hoạt được Drawing View chứa đường cắt."); return; }
            drawing.ClearSelection2(true);
            if (!selectedLine.Select4(false, null))
            { Notify("Không chọn lại được đường Line để tạo mặt cắt."); return; }
            double centerX = (outline[0] + outline[2]) / 2;
            double centerY = (outline[1] + outline[3]) / 2;
            bool verticalCut = UndirectedAngleDifference(cut.AngleDeg, 90) <
                UndirectedAngleDifference(cut.AngleDeg, 0);
            double placeX = verticalCut
                ? outline[0] - (outline[2] - outline[0]) / 2 - 0.030 : centerX;
            double placeY = verticalCut
                ? centerY : outline[1] - (outline[3] - outline[1]) / 2 - 0.030;
            try
            {
                int sectionOptions = (int)swCreateSectionViewAtOptions_e.swCreateSectionView_Partial |
                    (int)swCreateSectionViewAtOptions_e.swCreateSectionView_DisplaySurfaceCut |
                    (int)swCreateSectionViewAtOptions_e.swCreateSectionView_CutSurfaceBodies;
                SolidWorks.Interop.sldworks.View section = drawingDoc.CreateSectionViewAt5(
                    placeX, placeY, 0, label, sectionOptions, null, 0);
                if (section == null)
                { Log("SECTION ERROR CreateSectionViewAt5 returned null"); Notify("SOLIDWORKS không tạo được Section View từ Line đã chọn."); return; }
                Log("SECTION CREATED view=" + section.GetName2() + " label=" + label +
                    " parent=" + parent.GetName2() + " VCutCandidates=" + relevant.Count);
                drawing.EditRebuild3();
                Log("SECTION GEOMETRY view=" + section.GetName2());
                ModelDoc2 referencedPart = parent.ReferencedDocument as ModelDoc2;
                double[] parentUraNormal = null;
                bool hasUra = referencedPart != null && TryGetUraViewNormal(referencedPart,
                    parent, parent.ReferencedConfiguration ?? "", out parentUraNormal);
                bool omoteVisible = hasUra && parentUraNormal[2] < -1e-7;
                DrSection drSection = section.GetSection() as DrSection;
                ConfigureSectionOptions(section, drSection);
                bool canonicalLineDirection = Math.Abs(cut.EndX - cut.StartX) >=
                    Math.Abs(cut.EndY - cut.StartY)
                    ? cut.EndX >= cut.StartX
                    : cut.EndY <= cut.StartY;
                bool reverseCut = hasUra && (omoteVisible != canonicalLineDirection);
                if (drSection != null && hasUra)
                {
                    // The direction of an ordinary sketch segment depends on which
                    // endpoint SOLIDWORKS stored first.  Normalize that direction and
                    // use the visible colored face to choose the section direction.
                    // SetReversedCutDirection already reverses the resulting section;
                    // rotating IView.Angle as well would reverse it a second time.
                    drSection.SetReversedCutDirection(reverseCut);
                    drawing.EditRebuild3();
                    Log("SECTION DIRECTION parentFace=" + (omoteVisible ? "OMOTE" : "URA") +
                        " lineCanonical=" + canonicalLineDirection +
                        " requestedReversed=" + reverseCut +
                        " actualReversed=" + drSection.GetReversedCutDirection() +
                        " viewRotationDeg=" + F(section.Angle * 180 / Math.PI));
                }
                double materialThickness = relevant.Where(b => b.MaterialThicknessMm > 0 &&
                    !double.IsNaN(b.MaterialThicknessMm) && !double.IsInfinity(b.MaterialThicknessMm))
                    .Select(b => b.MaterialThicknessMm).DefaultIfEmpty(0).Min();
                double targetWidth, targetHeight;
                GetSectionLayoutTargets(drawingDoc, parent, verticalCut, out targetWidth, out targetHeight);
                AutoScaleSectionView(drawing, drawingDoc, section, parent, verticalCut,
                    targetWidth, targetHeight);
                drawing.EditRebuild3();
                bool omoteVerified = hasUra && EnsureOmoteDisplaySide(drawing,
                    section, drSection, omoteVisible);
                int blocks = 0;
                var failedMarks = new List<string>();
                foreach (VCutBendInfo bend in relevant)
                {
                    bend.ProjectedCenterX = null;
                    bend.ProjectedCenterY = null;
                    bend.ProjectedCenterZ = null;
                    LogBendGeometry(bend, section);
                    if (InsertVCutBlock(drawing, drawingDoc, section, bend)) blocks++;
                    else failedMarks.Add(bend.FeatureName + " " + bend.MatchedDefinition.Mark);
                }
                drawing.EditRebuild3();
                // Break the view only after placing the mark blocks.  With the
                // SOLIDWORKS "Break sketch blocks" option enabled, each mark
                // follows its own end of the shortened section.
                string breakNotice;
                string parentName = parent.GetName2();
                section = AutoBreakSectionView(drawing, drawingDoc, section,
                    verticalCut, relevant, verticalCut ? targetHeight : targetWidth, out breakNotice);
                parent = FindSectionView(drawingDoc, parentName) ?? parent;
                PlaceSectionView(drawing, drawingDoc, section, parent, verticalCut);
                drawing.EditRebuild3();
                double[] finalOutline = section.GetOutline() as double[];
                Sheet finalSheet = drawingDoc.GetCurrentSheet() as Sheet;
                if (finalOutline != null && finalSheet != null)
                {
                    double paperWidth = 0, paperHeight = 0;
                    finalSheet.GetSize(ref paperWidth, ref paperHeight);
                    if (finalOutline[0] < 0 || finalOutline[1] < 0 ||
                        finalOutline[2] > paperWidth || finalOutline[3] > paperHeight)
                        breakNotice += "\nMặt cắt còn vượt tờ: cần kiểm tra bố trí hoặc chọn khổ giấy lớn hơn.";
                }
                Notify("Đã tạo Section View " + label + ".\nBend V-CUT cùng hướng: " + relevant.Count +
                    "\nĐã chèn dấu: " + blocks +
                    "\nHướng OMOTE ra ngoài: " + (omoteVerified ? "OK" : "không xác định") +
                    "\nTỉ lệ mặt cắt: " + FormatSectionScale(section.ScaleDecimal) +
                    (materialThickness > 0 ? " — bề dày trên giấy: " +
                        F(materialThickness * section.ScaleDecimal) + " mm (in 100%)" :
                        " — chưa đọc được bề dày vật liệu để kiểm tra khi in") +
                    (string.IsNullOrEmpty(breakNotice) ? "" : "\n" + breakNotice) +
                    (failedMarks.Count == 0 ? "" : "\nNG — chưa chèn được dấu cho: " +
                        string.Join(", ", failedMarks) + "\nXem Output > Debug để biết nguyên nhân."));
            }
            catch (Exception ex)
            { Log("SECTION ERROR " + ex); Notify("Không tạo được Section View: " + ex.Message); }
            finally { drawing.ClearSelection2(true); }
        }

        private static double UndirectedAngleDifference(double a, double b)
        {
            double delta = Math.Abs((a % 180 + 180) % 180 - (b % 180 + 180) % 180);
            return Math.Min(delta, 180 - delta);
        }

        private static double DistancePointToSegment(double px, double py,
            double x1, double y1, double x2, double y2, out double along)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-18)
            {
                along = 0;
                double sx = px - x1, sy = py - y1;
                return Math.Sqrt(sx * sx + sy * sy);
            }
            along = ((px - x1) * dx + (py - y1) * dy) / lengthSquared;
            double clamped = Math.Max(0, Math.Min(1, along));
            double nearestX = x1 + clamped * dx;
            double nearestY = y1 + clamped * dy;
            double offsetX = px - nearestX, offsetY = py - nearestY;
            return Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        }

        private static double DistanceSegmentToSegment(DrawingBendInfo cut,
            DrawingBendInfo bend, out double along)
        {
            double rx = cut.EndX - cut.StartX, ry = cut.EndY - cut.StartY;
            double sx = bend.EndX - bend.StartX, sy = bend.EndY - bend.StartY;
            double qx = bend.StartX - cut.StartX, qy = bend.StartY - cut.StartY;
            double cross = rx * sy - ry * sx;
            if (Math.Abs(cross) > 1e-18)
            {
                double t = (qx * sy - qy * sx) / cross;
                double u = (qx * ry - qy * rx) / cross;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                { along = t; return 0; }
            }
            double candidateAlong;
            double best = DistancePointToSegment(bend.StartX, bend.StartY,
                cut.StartX, cut.StartY, cut.EndX, cut.EndY, out along);
            along = Math.Max(0, Math.Min(1, along));
            double distance = DistancePointToSegment(bend.EndX, bend.EndY,
                cut.StartX, cut.StartY, cut.EndX, cut.EndY, out candidateAlong);
            if (distance < best)
            { best = distance; along = Math.Max(0, Math.Min(1, candidateAlong)); }
            distance = DistancePointToSegment(cut.StartX, cut.StartY,
                bend.StartX, bend.StartY, bend.EndX, bend.EndY, out candidateAlong);
            if (distance < best) { best = distance; along = 0; }
            distance = DistancePointToSegment(cut.EndX, cut.EndY,
                bend.StartX, bend.StartY, bend.EndX, bend.EndY, out candidateAlong);
            if (distance < best) { best = distance; along = 1; }
            return best;
        }

        private static double NormalizeRadians(double angle)
        {
            double full = Math.PI * 2;
            angle %= full;
            return angle < 0 ? angle + full : angle;
        }

        private static bool EnsureOmoteDisplaySide(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View section, DrSection drSection,
            bool omoteVisible)
        {
            if (drawing == null || section == null || drSection == null) return false;
            bool actualAtMinimum;
            string profileAxis;
            if (!TryGetDominantProfileSide(section, out actualAtMinimum, out profileAxis))
            {
                Log("SECTION OMOTE VERIFY unavailable: dominant profile edge not found");
                return false;
            }

            // Examples supplied by the user establish the drawing convention:
            // OMOTE -> long edge at bottom for a wide profile, or at left for a tall
            // profile. URA uses the opposite side. Both bottom and left are the
            // minimum coordinate in SOLIDWORKS sheet space.
            bool desiredAtMinimum = omoteVisible;
            bool before = drSection.GetReversedCutDirection();
            bool toggled = actualAtMinimum != desiredAtMinimum;
            if (toggled)
            {
                drSection.SetReversedCutDirection(!before);
                drawing.EditRebuild3();
            }

            bool verifiedAtMinimum;
            string verifiedAxis;
            bool measured = TryGetDominantProfileSide(section,
                out verifiedAtMinimum, out verifiedAxis);
            bool verified = measured && verifiedAtMinimum == desiredAtMinimum;
            Log("SECTION OMOTE VERIFY parentFace=" + (omoteVisible ? "OMOTE" : "URA") +
                " profileAxis=" + profileAxis + " actualSide=" +
                (actualAtMinimum ? "MIN" : "MAX") + " desiredSide=" +
                (desiredAtMinimum ? "MIN" : "MAX") + " toggled=" + toggled +
                " reversedBefore=" + before + " reversedAfter=" +
                drSection.GetReversedCutDirection() + " verified=" + verified +
                (measured ? " verifiedSide=" + (verifiedAtMinimum ? "MIN" : "MAX") : ""));
            return verified;
        }

        private static void ConfigureSectionOptions(SolidWorks.Interop.sldworks.View section,
            DrSection drSection)
        {
            if (section != null)
            {
                section.EmphasizeOutline = false;
                section.ScaleHatchPattern = false;

                // A section created from a shaded parent view inherits the parent's
                // green/brown face display unless it is explicitly given a local
                // display mode.  Use precise HLR so the result is a clean technical
                // profile (white background and visible edges) like a normal drawing
                // section.  Facetted=false requests high-quality geometry.
                bool displayChanged = section.SetDisplayMode3(false,
                    (int)swDisplayMode_e.swHIDDEN, false, false);
                section.UpdateViewDisplayGeometry();
                Log("SECTION DISPLAY requested=HLR highQuality=True changed=" +
                    displayChanged + " actualMode=" + section.GetDisplayMode2() +
                    " useParent=" + section.GetUseParentDisplayMode() +
                    " facetted=" + section.GetFacettedHlrDisplay());
            }
            if (drSection == null) return;
            drSection.SetPartialSection(true);
            drSection.SetDisplayOnlySurfaceCut(true);
            drSection.SetAutoHatch(true);
            drSection.RandomizeScale = false;
            drSection.ScaleHatchPattern = false;
            drSection.SetScaleWithModelChanges(true);
            drSection.CutSurfaceBodies = true;
            drSection.DisplaySurfaceBodies = true;
            drSection.ExcludeSliceSectionBodies = false;
            drSection.SectionDepth = 0;
            Log("SECTION OPTIONS partial=" + drSection.GetPartialSection() +
                " slice=" + drSection.GetDisplayOnlySurfaceCut() +
                " autoHatch=" + drSection.GetAutoHatch() +
                " randomize=" + drSection.RandomizeScale +
                " scaleHatch=" + drSection.ScaleHatchPattern +
                " cutSurfaceBodies=" + drSection.CutSurfaceBodies +
                " displaySurfaceBodies=" + drSection.DisplaySurfaceBodies +
                " excludeSliceBodies=" + drSection.ExcludeSliceSectionBodies);
        }

        // Size the profile in a reserved part of the sheet. Material thickness
        // is deliberately absent: changing the stock gauge must not resize it.
        private static double RoundSectionScale(double scale, bool fitInside)
        {
            if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) return 1;
            // Very small scales use 1:N so rounding cannot produce a zero scale.
            if (scale < 0.1)
            {
                double denominator = 1 / scale;
                denominator = fitInside ? Math.Ceiling(denominator * 10) / 10
                    : Math.Round(denominator, 1, MidpointRounding.AwayFromZero);
                return 1 / denominator;
            }
            return fitInside ? Math.Floor(scale * 10 + 1e-9) / 10
                : Math.Round(scale, 1, MidpointRounding.AwayFromZero);
        }

        private static string FormatSectionScale(double scale)
        {
            return scale < 0.1 && scale > 0
                ? "1:" + (1 / scale).ToString("0.#", CultureInfo.InvariantCulture)
                : scale.ToString("0.#", CultureInfo.InvariantCulture) + ":1";
        }

        private static double CalculateSectionScale(double width, double height,
            double currentScale, bool verticalCut, double targetWidth, double targetHeight)
        {
            if (width <= 0 || height <= 0 || currentScale <= 0 ||
                targetWidth <= 0 || targetHeight <= 0) return currentScale;
            double fit = Math.Min(targetWidth / width, targetHeight / height);
            double length = verticalCut ? height : width;
            double cross = verticalCut ? width : height;
            // For an elongated bent profile, size the flange first. Its straight
            // run will be shortened independently, so it must not limit scale.
            if (length / cross > 4)
                fit = (verticalCut ? targetWidth : targetHeight) * 0.5 / cross;
            return currentScale * fit;
        }

        private static void GetSectionLayoutTargets(DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View parent, bool verticalCut,
            out double targetWidth, out double targetHeight)
        {
            double width = 0.297, height = 0.210;
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            if (sheet != null) sheet.GetSize(ref width, ref height);
            double bottom = Math.Max(0.015, Math.Min(0.025, height * 0.08));
            double top = height - Math.Max(0.020, Math.Min(0.030, height * 0.12));
            targetWidth = Math.Max(0.010, (width - 0.024) * 0.30);
            targetHeight = Math.Max(0.010, (top - bottom) * 0.25);
            double[] parentBox = parent.GetOutline() as double[];
            if (parentBox == null || parentBox.Length < 4) return;
            // A construction parent may be parked outside the sheet. Only
            // constrain the available side when it is inside the drawing area.
            if (parentBox[0] >= 0.012 && parentBox[2] <= width - 0.012 &&
                parentBox[1] >= bottom && parentBox[3] <= top)
            {
                double sideSpace = verticalCut
                    ? Math.Max(parentBox[0] - 0.032, width - 0.032 - parentBox[2])
                    : Math.Max(parentBox[1] - bottom - 0.020, top - parentBox[3] - 0.020);
                if (sideSpace > 0.010)
                {
                    if (verticalCut) targetWidth = Math.Min(targetWidth, sideSpace);
                    else targetHeight = Math.Min(targetHeight, sideSpace);
                }
            }
        }

        private static void AutoScaleSectionView(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, SolidWorks.Interop.sldworks.View parent,
            bool verticalCut, double targetWidth, double targetHeight)
        {
            double[] outline = section.GetOutline() as double[];
            if (outline == null || outline.Length < 4) return;
            double currentScale = section.ScaleDecimal;
            if (currentScale <= 0) currentScale = parent.ScaleDecimal;
            if (currentScale <= 0) currentScale = 1;
            // GetOutline includes a fixed paper-space margin and annotations.
            // At small parent scales those can exceed the actual flange height.
            List<ProjectedSegmentInfo> profile = GetDisplayedSegments(section);
            bool measuredProfile = profile.Count > 0;
            if (measuredProfile)
                outline = new[] {
                    profile.Min(s => Math.Min(s.X1, s.X2)),
                    profile.Min(s => Math.Min(s.Y1, s.Y2)),
                    profile.Max(s => Math.Max(s.X1, s.X2)),
                    profile.Max(s => Math.Max(s.Y1, s.Y2)) };
            double width = Math.Max(1e-9, outline[2] - outline[0]);
            double height = Math.Max(1e-9, outline[3] - outline[1]);
            double parentScale = parent.ScaleDecimal > 0 ? parent.ScaleDecimal : currentScale;
            double calculated = CalculateSectionScale(width, height, currentScale,
                verticalCut, targetWidth, targetHeight);
            double chosen = RoundSectionScale(calculated, false);
            section.UseParentScale = false;
            section.UseSheetScale = 0;
            section.ScaleDecimal = chosen;
            drawing.EditRebuild3();
            double[] scaled = section.GetOutline() as double[];
            Log("SECTION SCALE parent=" + F(parentScale) + " initial=" + F(currentScale) +
                " calculated=" + F(calculated) + " chosen=" + FormatSectionScale(chosen) +
                " initialSize=(" + Mm(width) + "," + Mm(height) +
                ")mm finalSize=" + (scaled == null ? "UNKNOWN" : "(" +
                Mm(scaled[2] - scaled[0]) + "," + Mm(scaled[3] - scaled[1]) + ")mm") +
                " longAxis=" + (verticalCut ? "Y" : "X") +
                " bounds=" + (measuredProfile ? "DISPLAY_GEOMETRY" : "VIEW_OUTLINE_FALLBACK") +
                " sizing=FLANGE_AND_SHEET target=(" +
                Mm(targetWidth) + "," + Mm(targetHeight) + ")mm");
        }

        private static SolidWorks.Interop.sldworks.View AutoBreakSectionView(
            ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, bool verticalCut,
            List<VCutBendInfo> markedBends, double target, out string notice)
        {
            notice = "";
            double[] outline = section.GetOutline() as double[];
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            if (outline == null || outline.Length < 4 || sheet == null) return section;
            int axis = verticalCut ? 1 : 0;
            double before = outline[axis + 2] - outline[axis];
            if (before <= target + 0.002) return section;

            var protectedPoints = new List<double>();
            foreach (VCutBendInfo bend in markedBends)
            {
                double? position = verticalCut ? bend.ProjectedCenterY : bend.ProjectedCenterX;
                if (position.HasValue) protectedPoints.Add(position.Value);
            }
            double first, second;
            if (!TryPlanSectionBreak(outline[axis], outline[axis + 2], target,
                protectedPoints, out first, out second))
            {
                // If a safe break is impossible, fit the entire geometry to its
                // reserved area instead of using a material-thickness floor.
                double oldScale = section.ScaleDecimal;
                double fitScale = RoundSectionScale(oldScale * target / before * 0.97, true);
                section.UseParentScale = false;
                section.UseSheetScale = 0;
                section.ScaleDecimal = fitScale;
                drawing.EditRebuild3();
                notice = "Không có đoạn an toàn để ngắt; đã giảm tỉ lệ mặt cắt cho vừa tờ.";
                Log("SECTION BREAK SKIP no safe interval axis=" + (verticalCut ? "Y" : "X") +
                    " length=" + Mm(before) + "mm target=" + Mm(target) +
                    "mm fallbackScale=" + F(section.ScaleDecimal));
                return section;
            }

            string viewName = section.GetName2();
            string sheetName = sheet.GetName();
            double[] origin = section.Position as double[];
            if (origin == null || origin.Length < 2)
            {
                notice = "Chưa thu ngắn mặt cắt: không đọc được vị trí Section View.";
                return section;
            }
            double relativeFirst = first - origin[axis];
            double relativeSecond = second - origin[axis];
            Log("SECTION BREAK PLAN view=" + viewName +
                " axis=" + (verticalCut ? "Y" : "X") +
                " before=" + Mm(before) + "mm target=" + Mm(target) +
                "mm linesSheet=(" + Mm(first) + "," + Mm(second) +
                ")mm linesView=(" + Mm(relativeFirst) + "," +
                Mm(relativeSecond) + ")mm gap=1mm style=Straight breakSketchBlocks=True");
            try
            {
                if (!drawingDoc.ActivateView(viewName))
                    throw new InvalidOperationException("Không kích hoạt được Section View.");
                section.BreakLineGap = 0.001;
                object line = section.InsertBreak3(
                    (int)(verticalCut ? swBreakLineOrientation_e.swBreakLineHorizontal :
                        swBreakLineOrientation_e.swBreakLineVertical),
                    relativeFirst, relativeSecond,
                    (int)swBreakLineStyle_e.swBreakLine_Straight, 1, true);
                if (line == null)
                    throw new InvalidOperationException("InsertBreak3 trả về null.");
                drawing.EditRebuild3();

                // BreakView is a sheet-level command.  The view sketch must be
                // deactivated and the drawing view selected before invoking it.
                if (!drawingDoc.ActivateSheet(sheetName))
                    throw new InvalidOperationException("Không kích hoạt được Sheet.");
                drawing.ClearSelection2(true);
                if (!drawing.Extension.SelectByID2(viewName, "DRAWINGVIEW", 0, 0, 0,
                    false, 0, null, 0))
                    throw new InvalidOperationException("Không chọn được Section View.");
                drawingDoc.BreakView();
                drawing.EditRebuild3();
                drawing.ClearSelection2(true);

                // Native BreakView can invalidate the old COM view reference.
                SolidWorks.Interop.sldworks.View fresh = FindSectionView(drawingDoc, viewName);
                if (fresh == null || !fresh.IsBroken())
                    throw new InvalidOperationException("SOLIDWORKS chưa áp dụng Broken View.");
                fresh.BreakLineGap = 0.001;
                double[] afterOutline = fresh.GetOutline() as double[];
                double after = afterOutline == null ? 0 :
                    afterOutline[axis + 2] - afterOutline[axis];
                Log("SECTION BREAK APPLIED view=" + viewName + " axis=" +
                    (verticalCut ? "Y" : "X") + " before=" + Mm(before) +
                    "mm after=" + Mm(after) + "mm target=" + Mm(target) + "mm");
                if (after > target + 0.005)
                    notice = "Mặt cắt đã thu ngắn nhưng vẫn cần kiểm tra khoảng trống trên tờ.";
                return fresh;
            }
            catch (Exception ex)
            {
                Log("SECTION BREAK ERROR view=" + viewName + " " + ex);
                notice = "Chưa thu ngắn được mặt cắt; hãy kiểm tra Broken View trong bản vẽ.";
                return FindSectionView(drawingDoc, viewName) ?? section;
            }
            finally { drawing.ClearSelection2(true); }
        }

        // All coordinates are on the drawing sheet, in meters.  SOLIDWORKS
        // receives these positions relative to the view origin later.
        private static bool TryPlanSectionBreak(double minimum, double maximum,
            double target, IEnumerable<double> protectedPoints,
            out double first, out double second)
        {
            first = second = 0;
            const double gap = 0.001;
            const double bendClearance = 0.012;
            const double endClearance = 0.012;
            double removal = maximum - minimum - target + gap;
            if (removal < 0.003) return false;

            var blocked = protectedPoints.Where(p => p >= minimum && p <= maximum)
                .Select(p => Tuple.Create(p - bendClearance, p + bendClearance))
                .OrderBy(p => p.Item1).ToList();
            double cursor = minimum + endClearance;
            double last = maximum - endClearance;
            double bestCenter = 0, bestDistance = double.MaxValue;
            bool found = false;
            foreach (Tuple<double, double> interval in blocked.Concat(new[] {
                Tuple.Create(last, last) }))
            {
                double freeEnd = Math.Min(interval.Item1, last);
                if (freeEnd - cursor >= removal + 0.002)
                {
                    double center = (cursor + freeEnd) / 2;
                    double distance = Math.Abs(center - (minimum + maximum) / 2);
                    if (!found || distance < bestDistance)
                    { bestCenter = center; bestDistance = distance; found = true; }
                }
                cursor = Math.Max(cursor, interval.Item2);
            }
            if (!found) return false;
            first = bestCenter - removal / 2;
            second = bestCenter + removal / 2;
            return true;
        }

        private static SolidWorks.Interop.sldworks.View FindSectionView(
            DrawingDoc drawingDoc, string name)
        {
            for (SolidWorks.Interop.sldworks.View view = drawingDoc.GetFirstView() as
                    SolidWorks.Interop.sldworks.View;
                view != null; view = view.GetNextView() as SolidWorks.Interop.sldworks.View)
                if (string.Equals(view.GetName2(), name, StringComparison.OrdinalIgnoreCase))
                    return view;
            return null;
        }

        private static void PlaceSectionView(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, SolidWorks.Interop.sldworks.View parent,
            bool verticalCut)
        {
            double[] sectionOutline = section.GetOutline() as double[];
            double[] parentOutline = parent.GetOutline() as double[];
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            if (sectionOutline == null || parentOutline == null || sheet == null) return;
            double sheetWidth = 0, sheetHeight = 0;
            sheet.GetSize(ref sheetWidth, ref sheetHeight);
            double width = sectionOutline[2] - sectionOutline[0];
            double height = sectionOutline[3] - sectionOutline[1];
            const double margin = 0.012;
            const double gap = 0.020;
            // Keep the view clear of the usual header and bottom title area;
            // calculate these reserves from the current sheet, not an A3 size.
            double safeBottom = Math.Max(0.015, Math.Min(0.025, sheetHeight * 0.08));
            double safeTop = sheetHeight - Math.Max(0.020,
                Math.Min(0.030, sheetHeight * 0.12));
            double x, y;
            if (verticalCut)
            {
                x = parentOutline[0] - gap - width / 2;
                if (x - width / 2 < margin)
                    x = parentOutline[2] + gap + width / 2;
                y = (parentOutline[1] + parentOutline[3]) / 2;
            }
            else
            {
                x = (parentOutline[0] + parentOutline[2]) / 2;
                y = parentOutline[1] - gap - height / 2;
                if (y - height / 2 < margin)
                    y = parentOutline[3] + gap + height / 2;
            }
            x = Math.Max(margin + width / 2, Math.Min(sheetWidth - margin - width / 2, x));
            y = Math.Max(safeBottom + height / 2,
                Math.Min(safeTop - height / 2, y));
            section.RemoveAlignment();
            section.Position = new[] { x, y };
            drawing.EditRebuild3();
            Log("SECTION POSITION center=(" + Mm(x) + "," + Mm(y) + ")mm size=(" +
                Mm(width) + "," + Mm(height) + ")mm sheet=(" + Mm(sheetWidth) + "," +
                Mm(sheetHeight) + ")mm");
        }

        private bool InsertVCutBlock(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, VCutBendInfo bend)
        {
            if (bend == null || bend.MatchedDefinition == null) return false;
            string blockPath = VCutBlockResolver.GetBlockPath(bend.MatchedDefinition);
            if (blockPath == null)
            { Log("BLOCK SKIP missing file for " + bend.FeatureName); return false; }
            if (!bend.ProjectedCenterX.HasValue || !bend.ProjectedCenterY.HasValue ||
                !bend.ProjectedCenterZ.HasValue)
            { Log("BLOCK SKIP no projected bend center for " + bend.FeatureName); return false; }

            List<ProjectedSegmentInfo> displayedSegments = GetDisplayedSegments(section);
            double anchorX, anchorY, outsideX, outsideY;
            if (!TryFindLocalBendCorner(displayedSegments, bend.ProjectedCenterX.Value,
                bend.ProjectedCenterY.Value, out anchorX, out anchorY,
                out outsideX, out outsideY))
            {
                Log("BLOCK SKIP no reliable corner near bend " + bend.FeatureName);
                return false;
            }
            // The exterior is opposite the two legs that run away from this
            // particular bend. A global view centroid is unreliable with many bends.
            const double outsideClearance = 0.008;
            double insertX = anchorX + outsideClearance * outsideX;
            double insertY = anchorY + outsideClearance * outsideY;
            double insertZ = 0;
            Log("BLOCK PLAN feature=" + bend.FeatureName + " mark=" + bend.MatchedDefinition.Mark +
                " bendCenter=(" + Mm(bend.ProjectedCenterX.Value) + "," +
                Mm(bend.ProjectedCenterY.Value) + ")mm anchor=(" + Mm(anchorX) + "," +
                Mm(anchorY) + ")mm" +
                " insert=(" + Mm(insertX) + "," + Mm(insertY) + ")mm");

            try
            {
                if (!drawingDoc.ActivateView(section.GetName2()))
                { Log("BLOCK ERROR cannot activate section view"); return false; }
                MathUtility math = app.IGetMathUtility();
                SketchManager sketchManager = drawing.SketchManager;
                MathPoint position = ViewPointToSketchPoint(math, section,
                    insertX, insertY, insertZ);
                if (position == null)
                { Log("BLOCK ERROR cannot transform view point to section sketch"); return false; }
                double[] local = position.ArrayData as double[];
                if (local == null || local.Length < 3)
                { Log("BLOCK ERROR invalid section-sketch position"); return false; }
                Log("BLOCK SKETCH POSITION sheet/view=(" + Mm(insertX) + "," + Mm(insertY) +
                    "," + Mm(insertZ) + ")mm local=(" + Mm(local[0]) + "," +
                    Mm(local[1]) + "," + Mm(local[2]) + ")mm");
                Sketch hostSketch = section.GetSketch() as Sketch;
                HashSet<string> instanceNamesBefore = new HashSet<string>(
                    GetBlockInstances(hostSketch).Select(i => i.Name ?? ""),
                    StringComparer.OrdinalIgnoreCase);

                // MakeSketchBlockFromFile inserts an instance in drawings, but some
                // SOLIDWORKS releases still return null for the definition.  Detect
                // the newly inserted instance from the section sketch instead of
                // treating a null return value as failure.
                Log("BLOCK TEMPLATE LOAD begin " + blockPath);
                SketchBlockDefinition definition = sketchManager.MakeSketchBlockFromFile(
                    position, blockPath, false, 1.0, 0.0);
                drawing.EditRebuild3();
                hostSketch = section.GetSketch() as Sketch;
                List<SketchBlockInstance> instancesAfter = GetBlockInstances(hostSketch);
                SketchBlockInstance instance = instancesAfter.FirstOrDefault(i =>
                    !instanceNamesBefore.Contains(i.Name ?? ""));
                Log("BLOCK TEMPLATE LOAD complete definition=" + (definition != null) +
                    " autoInserted=" + (instance != null));
                if (instance == null && definition != null)
                {
                    Log("BLOCK INSTANCE INSERT begin");
                    instance = sketchManager.InsertSketchBlockInstance(
                        definition, position, 1.0, 0.0);
                    Log("BLOCK INSTANCE INSERT complete=" + (instance != null));
                }
                if (instance == null)
                { Log("BLOCK ERROR template produced no instance " + blockPath); return false; }
                instance.LockAngle = true;
                Log("BLOCK INSERTED feature=" + bend.FeatureName + " name=" + instance.Name +
                    " attachedToSectionSketch=True distancePrompt=False");
                return true;
            }
            catch (Exception ex)
            { Log("BLOCK ERROR " + bend.FeatureName + " " + ex); return false; }
            finally { drawing.ClearSelection2(true); }
        }

        private static bool TryFindLocalBendCorner(List<ProjectedSegmentInfo> segments,
            double bendX, double bendY, out double cornerX, out double cornerY,
            out double outsideX, out double outsideY)
        {
            cornerX = cornerY = outsideX = outsideY = 0;
            if (segments == null) return false;
            const double minimumLeg = 0.002;
            const double maximumJoinGap = 0.006;
            const double maximumBendDistance = 0.020;
            double bestScore = double.MaxValue;
            var legs = segments.Where(s => s.Length >= minimumLeg &&
                Math.Min(Math.Sqrt(Math.Pow(s.X1 - bendX, 2) +
                    Math.Pow(s.Y1 - bendY, 2)),
                    Math.Sqrt(Math.Pow(s.X2 - bendX, 2) +
                    Math.Pow(s.Y2 - bendY, 2))) <= maximumBendDistance +
                    maximumJoinGap).ToList();
            for (int i = 0; i < legs.Count; i++)
            {
                ProjectedSegmentInfo a = legs[i];
                for (int j = i + 1; j < legs.Count; j++)
                {
                    ProjectedSegmentInfo b = legs[j];
                    for (int ai = 0; ai < 2; ai++)
                    for (int bi = 0; bi < 2; bi++)
                    {
                        double ax = ai == 0 ? a.X1 : a.X2;
                        double ay = ai == 0 ? a.Y1 : a.Y2;
                        double bx = bi == 0 ? b.X1 : b.X2;
                        double by = bi == 0 ? b.Y1 : b.Y2;
                        double gap = Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(ay - by, 2));
                        if (gap > maximumJoinGap) continue;
                        double x = (ax + bx) / 2, y = (ay + by) / 2;
                        double bendDistance = Math.Sqrt(Math.Pow(x - bendX, 2) +
                            Math.Pow(y - bendY, 2));
                        if (bendDistance > maximumBendDistance) continue;
                        double adx = (ai == 0 ? a.X2 - a.X1 : a.X1 - a.X2) / a.Length;
                        double ady = (ai == 0 ? a.Y2 - a.Y1 : a.Y1 - a.Y2) / a.Length;
                        double bdx = (bi == 0 ? b.X2 - b.X1 : b.X1 - b.X2) / b.Length;
                        double bdy = (bi == 0 ? b.Y2 - b.Y1 : b.Y1 - b.Y2) / b.Length;
                        if (Math.Abs(adx * bdx + ady * bdy) > 0.85) continue;
                        double ox = -adx - bdx, oy = -ady - bdy;
                        double length = Math.Sqrt(ox * ox + oy * oy);
                        if (length < 1e-9) continue;
                        double score = bendDistance + gap;
                        if (score >= bestScore) continue;
                        bestScore = score;
                        cornerX = x; cornerY = y;
                        outsideX = ox / length; outsideY = oy / length;
                    }
                }
            }
            return bestScore < double.MaxValue;
        }

        private static MathPoint ViewPointToSketchPoint(MathUtility math,
            SolidWorks.Interop.sldworks.View view, double x, double y, double z)
        {
            if (math == null || view == null) return null;
            Sketch sketch = view.GetSketch() as Sketch;
            MathTransform sheetToSketch = sketch == null ? null : sketch.ModelToSketchTransform;
            MathPoint sheetPoint = math.CreatePoint(new[] { x, y, 0.0 }) as MathPoint;
            MathPoint sketchPoint = sheetPoint == null || sheetToSketch == null ? null :
                sheetPoint.MultiplyTransform(sheetToSketch) as MathPoint;
            double[] local = sketchPoint == null ? null : sketchPoint.ArrayData as double[];
            if (local != null && local.Length >= 3)
                Log("BLOCK SHEET TO SKETCH sheet=(" + Mm(x) + "," + Mm(y) +
                    ")mm local=(" + Mm(local[0]) + "," + Mm(local[1]) + "," +
                    Mm(local[2]) + ")mm");
            return sketchPoint;
        }

        private static SketchBlockDefinition FindBlockDefinition(SketchManager manager, string path)
        {
            Array definitions = manager == null ? null : manager.GetSketchBlockDefinitions() as Array;
            if (definitions == null) return null;
            foreach (object item in definitions)
            {
                SketchBlockDefinition definition = item as SketchBlockDefinition;
                if (definition != null && string.Equals(definition.FileName, path,
                    StringComparison.OrdinalIgnoreCase)) return definition;
            }
            return null;
        }

        private static List<SketchBlockInstance> GetBlockInstances(Sketch sketch)
        {
            var result = new List<SketchBlockInstance>();
            Array instances = sketch == null ? null : sketch.GetSketchBlockInstances() as Array;
            if (instances == null) return result;
            foreach (object item in instances)
            {
                SketchBlockInstance instance = item as SketchBlockInstance;
                if (instance != null) result.Add(instance);
            }
            return result;
        }

        private static bool TryGetDominantProfileSide(SolidWorks.Interop.sldworks.View view,
            out bool atMinimum, out string profileAxis)
        {
            atMinimum = false;
            profileAxis = "UNKNOWN";
            List<ProjectedSegmentInfo> segments = GetDisplayedSegments(view);
            if (segments.Count == 0) return false;

            double minX = segments.Min(s => Math.Min(s.X1, s.X2));
            double maxX = segments.Max(s => Math.Max(s.X1, s.X2));
            double minY = segments.Min(s => Math.Min(s.Y1, s.Y2));
            double maxY = segments.Max(s => Math.Max(s.Y1, s.Y2));
            bool wide = maxX - minX >= maxY - minY;
            List<ProjectedSegmentInfo> aligned = segments.Where(s =>
            {
                double dx = Math.Abs(s.X2 - s.X1);
                double dy = Math.Abs(s.Y2 - s.Y1);
                return wide ? dx >= dy * 4 : dy >= dx * 4;
            }).ToList();
            if (aligned.Count == 0) return false;

            ProjectedSegmentInfo dominant = aligned.OrderByDescending(s => s.Length).First();
            if (dominant.Length < 1e-6) return false;
            if (wide)
            {
                double coordinate = (dominant.Y1 + dominant.Y2) / 2;
                atMinimum = coordinate <= (minY + maxY) / 2;
                profileAxis = "WIDE";
                Log("SECTION PROFILE dominant=H length=" + Mm(dominant.Length) +
                    "mm coordinate=" + Mm(coordinate) + "mm boundsY=(" +
                    Mm(minY) + "," + Mm(maxY) + ")mm side=" +
                    (atMinimum ? "MIN" : "MAX"));
            }
            else
            {
                double coordinate = (dominant.X1 + dominant.X2) / 2;
                atMinimum = coordinate <= (minX + maxX) / 2;
                profileAxis = "TALL";
                Log("SECTION PROFILE dominant=V length=" + Mm(dominant.Length) +
                    "mm coordinate=" + Mm(coordinate) + "mm boundsX=(" +
                    Mm(minX) + "," + Mm(maxX) + ")mm side=" +
                    (atMinimum ? "MIN" : "MAX"));
            }
            return true;
        }

        private static List<ProjectedSegmentInfo> GetDisplayedSegments(
            SolidWorks.Interop.sldworks.View view)
        {
            var result = new List<ProjectedSegmentInfo>();
            try
            {
                view.UpdateViewDisplayGeometry();
                object polylineBuffer;
                view.GetPolylines7(1, out polylineBuffer);
                Array data = polylineBuffer as Array;
                double[] viewTransform = view.GetXform() as double[];
                if (data == null || viewTransform == null || viewTransform.Length < 3)
                    return result;

                double originX = viewTransform[0], originY = viewTransform[1];
                double scale = viewTransform[2];
                int cursor = 0;
                while (cursor + 9 <= data.Length)
                {
                    int geometrySize = Convert.ToInt32(data.GetValue(cursor + 1),
                        CultureInfo.InvariantCulture);
                    int attributes = cursor + 2 + geometrySize;
                    int pointCountIndex = attributes + 6;
                    if (geometrySize < 0 || pointCountIndex >= data.Length) break;
                    int pointCount = Convert.ToInt32(data.GetValue(pointCountIndex),
                        CultureInfo.InvariantCulture);
                    int firstPoint = pointCountIndex + 1;
                    int next = firstPoint + pointCount * 3;
                    if (pointCount < 1 || next > data.Length) break;

                    for (int point = 0; point + 1 < pointCount; point++)
                    {
                        int first = firstPoint + point * 3;
                        int second = first + 3;
                        double x1 = originX + Convert.ToDouble(data.GetValue(first),
                            CultureInfo.InvariantCulture) * scale;
                        double y1 = originY + Convert.ToDouble(data.GetValue(first + 1),
                            CultureInfo.InvariantCulture) * scale;
                        double x2 = originX + Convert.ToDouble(data.GetValue(second),
                            CultureInfo.InvariantCulture) * scale;
                        double y2 = originY + Convert.ToDouble(data.GetValue(second + 1),
                            CultureInfo.InvariantCulture) * scale;
                        var segment = new ProjectedSegmentInfo
                        { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
                        if (segment.Length >= 1e-7) result.Add(segment);
                    }
                    cursor = next;
                }
            }
            catch (Exception ex)
            {
                Log("SECTION PROFILE ERROR " + ex.Message);
            }
            return result;
        }

        private List<ProjectedVertexInfo> GetVisibleVertices(SolidWorks.Interop.sldworks.View view)
        {
            var result = new List<ProjectedVertexInfo>();
            var components = new List<Component2>();
            Array visible = view.GetVisibleComponents() as Array;
            if (visible != null)
                foreach (object item in visible)
                {
                    Component2 component = item as Component2;
                    if (component != null) components.Add(component);
                }
            DrawingComponent root = view.RootDrawingComponent;
            Component2 rootComponent = root == null ? null : root.Component;
            if (rootComponent != null && !components.Contains(rootComponent)) components.Add(rootComponent);
            MathUtility math = app.IGetMathUtility();
            foreach (Component2 component in components)
            {
                Array entities = view.GetVisibleEntities2(component,
                    (int)swViewEntityType_e.swViewEntityType_Vertex) as Array;
                if (entities != null)
                    foreach (object item in entities)
                        AddProjectedVertex(result, item as Vertex, item as Entity,
                            math, view.ModelToViewTransform);
                Array edges = view.GetVisibleEntities2(component,
                    (int)swViewEntityType_e.swViewEntityType_Edge) as Array;
                if (edges == null) continue;
                foreach (object item in edges)
                {
                    Edge edge = item as Edge;
                    if (edge == null) continue;
                    Vertex start = edge.GetStartVertex() as Vertex;
                    Vertex end = edge.GetEndVertex() as Vertex;
                    AddProjectedVertex(result, start, start as Entity,
                        math, view.ModelToViewTransform);
                    AddProjectedVertex(result, end, end as Entity,
                        math, view.ModelToViewTransform);
                }
            }
            // Slice/partial section views can return no components and no visible
            // topology through GetVisibleEntities2 even though their HLR geometry is
            // displayed.  GetPolylines7 is the supported source for that displayed
            // model geometry.  Its edge array also gives us a selectable entity for
            // the hidden dimensions that anchor the V-CUT block.
            if (result.Count == 0)
                AddDisplayedPolylineVertices(view, result);
            Log("SECTION VISIBLE VERTICES view=" + view.GetName2() + " count=" + result.Count);
            return result;
        }

        private static void AddDisplayedPolylineVertices(SolidWorks.Interop.sldworks.View view,
            List<ProjectedVertexInfo> result)
        {
            try
            {
                view.UpdateViewDisplayGeometry();
                object polylineBuffer;
                object edgeBuffer = view.GetPolylines7(1, out polylineBuffer);
                Array data = polylineBuffer as Array;
                Array edges = edgeBuffer as Array;
                double[] viewTransform = view.GetXform() as double[];
                if (data == null || data.Length == 0)
                {
                    Log("SECTION POLYLINES unavailable");
                    return;
                }
                if (viewTransform == null || viewTransform.Length < 3)
                {
                    Log("SECTION POLYLINES missing view transform");
                    return;
                }

                int cursor = 0;
                int polylineIndex = 0;
                while (cursor + 9 <= data.Length)
                {
                    int geometrySize = Convert.ToInt32(data.GetValue(cursor + 1),
                        CultureInfo.InvariantCulture);
                    int attributes = cursor + 2 + geometrySize;
                    int pointCountIndex = attributes + 6;
                    if (geometrySize < 0 || pointCountIndex >= data.Length) break;
                    int pointCount = Convert.ToInt32(data.GetValue(pointCountIndex),
                        CultureInfo.InvariantCulture);
                    int firstPoint = pointCountIndex + 1;
                    int next = firstPoint + pointCount * 3;
                    if (pointCount < 1 || next > data.Length) break;

                    Entity edgeEntity = null;
                    if (edges != null && polylineIndex < edges.Length)
                        edgeEntity = edges.GetValue(polylineIndex) as Entity;

                    AddDisplayedPoint(result, data, firstPoint, edgeEntity, viewTransform);
                    if (pointCount > 1)
                        AddDisplayedPoint(result, data, firstPoint + (pointCount - 1) * 3,
                            edgeEntity, viewTransform);

                    cursor = next;
                    polylineIndex++;
                }

                if (result.Count > 0)
                {
                    Log("SECTION POLYLINES records=" + polylineIndex + " endpoints=" +
                        result.Count + " bounds=(" + Mm(result.Min(v => v.X)) + "," +
                        Mm(result.Min(v => v.Y)) + ")-(" + Mm(result.Max(v => v.X)) + "," +
                        Mm(result.Max(v => v.Y)) + ")mm selectable=" +
                        result.Count(v => v.Entity != null));
                }
            }
            catch (Exception ex)
            {
                Log("SECTION POLYLINES ERROR " + ex.Message);
            }
        }

        private static void AddDisplayedPoint(List<ProjectedVertexInfo> result, Array data,
            int index, Entity entity, double[] viewTransform)
        {
            // GetPolylines7 returns view-space points.  GetXform supplies the view
            // origin on the sheet and its scale, which converts them to sheet space.
            double scale = viewTransform[2];
            double x = viewTransform[0] +
                Convert.ToDouble(data.GetValue(index), CultureInfo.InvariantCulture) * scale;
            double y = viewTransform[1] +
                Convert.ToDouble(data.GetValue(index + 1), CultureInfo.InvariantCulture) * scale;
            double z = Convert.ToDouble(data.GetValue(index + 2),
                CultureInfo.InvariantCulture) * scale;
            ProjectedVertexInfo existing = result.FirstOrDefault(v =>
                Math.Abs(v.X - x) < 1e-7 && Math.Abs(v.Y - y) < 1e-7);
            if (existing != null)
            {
                if (existing.Entity == null && entity != null) existing.Entity = entity;
                return;
            }
            result.Add(new ProjectedVertexInfo { Entity = entity, X = x, Y = y, Z = z });
        }

        private static void AddProjectedVertex(List<ProjectedVertexInfo> result,
            Vertex vertex, Entity entity, MathUtility math, MathTransform transform)
        {
            double[] model = vertex == null ? null : vertex.GetPoint() as double[];
            if (entity == null || model == null || model.Length < 3) return;
            double[] point = TransformModelPoint(math, transform, model[0], model[1], model[2]);
            if (point == null) return;
            if (result.Any(v => Math.Abs(v.X - point[0]) < 1e-7 &&
                                Math.Abs(v.Y - point[1]) < 1e-7)) return;
            result.Add(new ProjectedVertexInfo { Entity = entity, X = point[0], Y = point[1],
                Z = point.Length > 2 ? point[2] : 0 });
        }

        private static string NextAvailableSectionLabel(DrawingDoc drawingDoc)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            SolidWorks.Interop.sldworks.View sheet = drawingDoc.GetFirstView() as SolidWorks.Interop.sldworks.View;
            for (SolidWorks.Interop.sldworks.View view = sheet == null ? null :
                    sheet.GetNextView() as SolidWorks.Interop.sldworks.View;
                 view != null; view = view.GetNextView() as SolidWorks.Interop.sldworks.View)
            {
                DrSection existing = view.GetSection() as DrSection;
                if (existing != null) used.Add(existing.GetLabel());
            }
            foreach (char letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
                if (!used.Contains(letter.ToString())) return letter.ToString();
            for (char first = 'A'; first <= 'Z'; first++)
                for (char second = 'A'; second <= 'Z'; second++)
                {
                    string label = new string(new[] { first, second });
                    if (!used.Contains(label)) return label;
                }
            return null;
        }

        private static double[] TransformModelPoint(MathUtility math, MathTransform transform,
            double x, double y, double z)
        {
            MathPoint point = math == null ? null : math.CreatePoint(new[] { x, y, z }) as MathPoint;
            MathPoint inView = point == null || transform == null ? null : point.MultiplyTransform(transform) as MathPoint;
            return inView == null ? null : inView.ArrayData as double[];
        }

        // The pink/brown face is URA. A negative view-normal Z means URA faces
        // away from the viewer, so the visible large face is OMOTE.
        private bool TryGetUraViewNormal(ModelDoc2 part, SolidWorks.Interop.sldworks.View view,
            string referencedConfig, out double[] viewNormal)
        {
            viewNormal = null;
            try
            {
                ConfigurationManager manager = part.ConfigurationManager;
                Configuration active = manager == null ? null : manager.ActiveConfiguration;
                if (active == null || !string.Equals(active.Name, referencedConfig, StringComparison.OrdinalIgnoreCase))
                {
                    Log("URA SIDE UNKNOWN: referenced configuration is not active; active=" +
                        (active == null ? "NONE" : active.Name));
                    return false;
                }
                Face2 best = null;
                double bestArea = 0;
                int pinkFaces = 0;
                bool authoritative = false;
                Array states = active.GetDisplayStates() as Array;
                if (states != null && states.Length > 0)
                {
                    object rawFaces;
                    Array rawMaterials = active.GetDisplayStateFaceProperties(
                        Convert.ToString(states.GetValue(0)), out rawFaces) as Array;
                    Array faces = rawFaces as Array;
                    authoritative = manager.LinkDisplayStatesToConfigurations && faces != null;
                    if (faces != null && rawMaterials != null)
                        for (int i = 0; i < faces.Length; i++)
                        {
                            Face2 face = faces.GetValue(i) as Face2;
                            double[] material = ReadDisplayStateMaterial(rawMaterials, i);
                            if (face != null && IsPinkMaterial(material))
                                ConsiderUraFace(face, ref best, ref bestArea, ref pinkFaces);
                        }
                }
                if (!authoritative && pinkFaces == 0)
                {
                    PartDoc partDoc = part as PartDoc;
                    Array bodies = partDoc == null ? null : partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as Array;
                    if (bodies != null)
                        foreach (object bodyObject in bodies)
                        {
                            Body2 body = bodyObject as Body2;
                            Array faces = body == null ? null : body.GetFaces() as Array;
                            if (faces == null) continue;
                            foreach (object faceObject in faces)
                            {
                                Face2 face = faceObject as Face2;
                                if (face == null) continue;
                                double[] material = face.GetMaterialPropertyValues2(
                                    (int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                                if (material == null || material.Length < 3)
                                    material = face.MaterialPropertyValues as double[];
                                if (IsPinkMaterial(material))
                                    ConsiderUraFace(face, ref best, ref bestArea, ref pinkFaces);
                            }
                        }
                }
                double[] normal = best == null ? null : best.Normal as double[];
                if (normal == null || normal.Length < 3)
                { Log("URA SIDE UNKNOWN: pinkFaces=" + pinkFaces + " authoritativeDisplayState=" + authoritative); return false; }
                MathUtility math = app.IGetMathUtility();
                MathTransform transform = view.ModelToViewTransform;
                double[] origin = TransformModelPoint(math, transform, 0, 0, 0);
                double[] tip = TransformModelPoint(math, transform, normal[0], normal[1], normal[2]);
                if (origin == null || tip == null) return false;
                viewNormal = new[] { tip[0] - origin[0], tip[1] - origin[1], tip[2] - origin[2] };
                Log("URA SIDE pinkFaces=" + pinkFaces + " dominantAreaMm2=" + F(bestArea * 1000000) +
                    " modelNormal=(" + F(normal[0]) + "," + F(normal[1]) + "," + F(normal[2]) + ")" +
                    " viewNormal=(" + F(viewNormal[0]) + "," + F(viewNormal[1]) +
                    "," + F(viewNormal[2]) + ") authoritativeDisplayState=" + authoritative);
                return true;
            }
            catch (Exception ex) { Log("URA SIDE ERROR " + ex.Message); return false; }
        }

        private static void ConsiderUraFace(Face2 face, ref Face2 best, ref double bestArea, ref int count)
        {
            count++;
            double area = Math.Abs(face.GetArea());
            if (area > bestArea) { best = face; bestArea = area; }
        }

        private static double[] ReadDisplayStateMaterial(Array properties, int faceIndex)
        {
            if (properties == null) return null;
            try
            {
                Array nested = faceIndex < properties.Length ? properties.GetValue(faceIndex) as Array : null;
                if (nested != null && nested.Length >= 9)
                {
                    var values = new double[9];
                    for (int j = 0; j < 9; j++) values[j] = Convert.ToDouble(nested.GetValue(j));
                    return values;
                }
                if (properties.Length < (faceIndex + 1) * 9) return null;
                var flat = new double[9];
                for (int j = 0; j < 9; j++) flat[j] = Convert.ToDouble(properties.GetValue(faceIndex * 9 + j));
                return flat;
            }
            catch { return null; }
        }

        private static bool IsPinkMaterial(double[] material)
        {
            if (material == null || material.Length < 3) return false;
            double red = material[0], green = material[1], blue = material[2];
            return red > 0.45 && red >= green + 0.08 && blue >= green - 0.05 &&
                Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) > 0.08;
        }

        private static void CollectFeature(Feature feature, List<BendAllowanceInfo> defaults,
            List<Feature> bends, HashSet<double> thicknesses)
        {
            if (IsSuppressedInCurrentConfiguration(feature)) return;
            string type = feature.GetTypeName2() ?? "";
            if (string.Equals(type, "SheetMetal", StringComparison.OrdinalIgnoreCase))
            {
                var data = feature.GetDefinition() as ISheetMetalFeatureData;
                if (data != null)
                {
                    if (data.Thickness > 0)
                        thicknesses.Add(Math.Round(data.Thickness * 1000, 3));
                    defaults.Add(BendAllowanceInfo.Capture(data.GetCustomBendAllowance()));
                }
            }
            else if (IsBendType(type))
            {
                // An EdgeFlange (and similar parent feature) can expose its own
                // allowance as well as a more specific OneBend subfeature.
                // The child describes the physical bend; counting both would
                // create duplicate sections and may choose the wrong table.
                if (!string.Equals(type, "OneBend", StringComparison.OrdinalIgnoreCase) &&
                    HasOneBendDescendant(feature, 0))
                    Log("SKIP parent bend with OneBend child " + feature.Name + " type=" + type);
                else
                    bends.Add(feature);
            }
            for (Feature sub = feature.GetFirstSubFeature() as Feature; sub != null; sub = sub.GetNextSubFeature() as Feature)
                CollectFeature(sub, defaults, bends, thicknesses);
        }

        private static bool IsSuppressedInCurrentConfiguration(Feature feature)
        {
            try
            {
                object states = feature.IsSuppressed2(
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                if (states is bool) return (bool)states;
                Array values = states as Array;
                if (values != null && values.Length > 0)
                    return Convert.ToBoolean(values.GetValue(0));
            }
            catch { }
            try { return feature.IsSuppressed(); }
            catch { return false; }
        }

        private static bool HasOneBendDescendant(Feature parent, int depth)
        {
            if (parent == null || depth >= 12) return false;
            for (Feature child = parent.GetFirstSubFeature() as Feature; child != null;
                child = child.GetNextSubFeature() as Feature)
            {
                if (string.Equals(child.GetTypeName2(), "OneBend", StringComparison.OrdinalIgnoreCase) ||
                    HasOneBendDescendant(child, depth + 1))
                    return true;
            }
            return false;
        }

        private static bool IsBendType(string type)
        {
            return type == "OneBend" || type == "SketchBend" || type == "EdgeFlange" ||
                type == "ToroidalBend" || type == "ProfileBend" || type == "SM3dBend" ||
                type == "SMMiteredBend" || type == "SweptFlange" || type == "MiterFlange" ||
                type == "Hem" || type == "Jog";
        }

        private DrawingBendInfo TransformDrawingViewSketchLineToSheet(SketchSegment segment)
        {
            SketchLine line = segment as SketchLine;
            Sketch sketch = segment == null ? null : segment.GetSketch();
            MathTransform modelToSketch = sketch == null ? null : sketch.ModelToSketchTransform;
            MathTransform sketchToSheet = modelToSketch == null ? null :
                modelToSketch.Inverse() as MathTransform;
            SketchPoint a = line == null ? null : line.GetStartPoint2() as SketchPoint;
            SketchPoint b = line == null ? null : line.GetEndPoint2() as SketchPoint;
            if (sketchToSheet == null || a == null || b == null) return null;

            MathUtility math = app.IGetMathUtility();
            MathPoint pa = math.CreatePoint(new[] { a.X, a.Y, a.Z }) as MathPoint;
            MathPoint pb = math.CreatePoint(new[] { b.X, b.Y, b.Z }) as MathPoint;
            pa = pa == null ? null : pa.MultiplyTransform(sketchToSheet) as MathPoint;
            pb = pb == null ? null : pb.MultiplyTransform(sketchToSheet) as MathPoint;
            double[] p = pa == null ? null : pa.ArrayData as double[];
            double[] q = pb == null ? null : pb.ArrayData as double[];
            if (p == null || q == null || p.Length < 2 || q.Length < 2) return null;

            double dx = q[0] - p[0], dy = q[1] - p[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6) return null;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angle < 0) angle += 180;
            Log("MANUAL SECTION RAW SKETCH from=(" + Mm(a.X) + "," + Mm(a.Y) +
                ")mm to=(" + Mm(b.X) + "," + Mm(b.Y) + ")mm");
            return new DrawingBendInfo
            {
                StartX = p[0], StartY = p[1], EndX = q[0], EndY = q[1],
                MidX = (p[0] + q[0]) / 2, MidY = (p[1] + q[1]) / 2,
                Length = length, AngleDeg = angle
            };
        }

        private DrawingBendInfo TransformLine(SketchSegment segment, MathTransform modelToView)
        {
            SketchLine line = segment as SketchLine;
            if (line == null || modelToView == null) return null;
            Sketch sketch = segment.GetSketch();
            MathTransform modelToSketch = sketch == null ? null : sketch.ModelToSketchTransform;
            MathTransform sketchToModel = modelToSketch == null ? null : modelToSketch.Inverse() as MathTransform;
            SketchPoint a = line.GetStartPoint2() as SketchPoint;
            SketchPoint b = line.GetEndPoint2() as SketchPoint;
            if (sketchToModel == null || a == null || b == null) return null;
            MathUtility math = app.IGetMathUtility();
            double[] p = Transform(math, sketchToModel, modelToView, a.X, a.Y, a.Z);
            double[] q = Transform(math, sketchToModel, modelToView, b.X, b.Y, b.Z);
            if (p == null || q == null) return null;
            double dx = q[0] - p[0], dy = q[1] - p[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6) return null;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (angle < 0) angle += 180;
            return new DrawingBendInfo { StartX = p[0], StartY = p[1], EndX = q[0], EndY = q[1],
                MidX = (p[0] + q[0]) / 2, MidY = (p[1] + q[1]) / 2,
                Length = length, AngleDeg = angle };
        }

        private static double[] Transform(MathUtility math, MathTransform sketchToModel,
            MathTransform modelToView, double x, double y, double z)
        {
            MathPoint point = math.CreatePoint(new[] { x, y, z }) as MathPoint;
            if (point == null) return null;
            MathPoint modelPoint = point.MultiplyTransform(sketchToModel) as MathPoint;
            MathPoint viewPoint = modelPoint == null ? null : modelPoint.MultiplyTransform(modelToView) as MathPoint;
            return viewPoint == null ? null : viewPoint.ArrayData as double[];
        }

        private static VCutBendInfo MatchLine(DrawingBendInfo drawingLine,
            List<VCutBendInfo> bends, HashSet<VCutBendInfo> usedFeatures, out bool ambiguous)
        {
            ambiguous = false;
            var ranked = bends.Where(b => b.ModelLines.Count > 0)
                .Select(b => new { Bend = b, Score = b.ModelLines.Min(line => Score(drawingLine, line)) })
                .OrderBy(b => b.Score).ToList();
            if (ranked.Count == 0 || ranked[0].Score > 2.0) return null;
            if (ranked.Count > 1 && ranked[1].Score - ranked[0].Score < 0.5)
            { ambiguous = true; return null; }
            if (usedFeatures.Contains(ranked[0].Bend))
            { ambiguous = true; return null; }
            return ranked[0].Bend;
        }

        private static double Score(DrawingBendInfo a, DrawingBendInfo b)
        {
            double distanceMm = Math.Sqrt(Math.Pow(a.MidX - b.MidX, 2) + Math.Pow(a.MidY - b.MidY, 2)) * 1000;
            double angle = Math.Abs(a.AngleDeg - b.AngleDeg);
            angle = Math.Min(angle, 180 - angle);
            double lengthDeltaMm = Math.Abs(a.Length - b.Length) * 1000;
            if (angle > 2 || distanceMm > 2 || lengthDeltaMm > 5) return double.MaxValue;
            return distanceMm + angle * 0.1 + lengthDeltaMm * 0.1;
        }

        private static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private static string Mm(double metres) { return F(metres * 1000); }
        private static void Log(string value) { Debug.WriteLine("[VCUT SECTION] " + value); }
        private static void Notify(string message) { MessageBox.Show(message, "V-CUT SECTION", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
}
