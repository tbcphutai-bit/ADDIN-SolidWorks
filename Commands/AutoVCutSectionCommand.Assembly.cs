using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed partial class AutoVCutSectionCommand
    {
        private readonly Dictionary<string, Component2> assemblyModelComponents =
            new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> assemblyDrawingRootNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private sealed class AssemblyOuterPart
        {
            public string ComponentKey;
            public string ComponentName;
            public double ProjectedArea;
            public double UraViewNormalZ;
            public double[] UraFaceCenter;
        }

        private sealed class AssemblySectionEdge
        {
            public string ComponentKey;
            public Edge Edge;
            public Vertex Start;
            public Vertex End;
            public double X1, Y1, X2, Y2;
            public double Length
            {
                get { return Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1)); }
            }
            public double Coordinate(bool wide, bool atEnd)
            {
                return wide ? (atEnd ? X2 : X1) : (atEnd ? Y2 : Y1);
            }
        }

        private sealed class AssemblyOuterRun
        {
            public bool Wide;
            public bool AtMinimum;
            public double MinAxis, MaxAxis, Normal;
            public AssemblySectionEdge FirstEdge, LastEdge;
            public Vertex FirstVertex, LastVertex;
            public double FirstX, FirstY, LastX, LastY;
        }

        private sealed class AssemblyFrameCorner
        {
            public AssemblySectionEdge Base;
            public AssemblySectionEdge Wall;
            public double X, Y, Offset, NormalGap;
        }

        private void RunAssemblySection(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View parent, SketchSegment selectedLine)
        {
            // Preflight also calls COM APIs. Keep its failures in the same log as
            // section creation rather than losing them in the task-pane handler.
            try { RunAssemblySectionCore(drawing, parent, selectedLine); }
            catch (Exception ex)
            {
                Log("ASSEMBLY PREFLIGHT ERROR " + ex);
                Notify("Chưa tạo mặt cắt Assembly: " + ex.Message +
                    "\nChi tiết lỗi đã được ghi vào Output debug.");
            }
        }

        private void RunAssemblySectionCore(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View parent, SketchSegment selectedLine)
        {
            Log("ASSEMBLY START view=" + parent.GetName2() +
                " config=" + parent.ReferencedConfiguration);
            if (selectedLine == null)
            {
                Notify("Hãy chọn một đường cắt đã vẽ trong Drawing View của Assembly.");
                return;
            }
            if (parent.IsFlatPatternView())
            {
                Notify("Đường cắt Assembly phải nằm trên View 3D, không phải Flat Pattern.");
                return;
            }
            DrawingDoc drawingDoc = drawing as DrawingDoc;
            Log("ASSEMBLY PREFLIGHT reading cut and outline");
            DrawingBendInfo cut = TransformDrawingViewSketchLineToSheet(selectedLine);
            double[] outline = parent.GetOutline() as double[];
            if (drawingDoc == null || cut == null || outline == null || outline.Length < 4)
            {
                Notify("Không đọc được đường cắt hoặc giới hạn View Assembly.");
                return;
            }

            Log("ASSEMBLY CUT SHEET from=(" + Mm(cut.StartX) + "," + Mm(cut.StartY) +
                ")mm to=(" + Mm(cut.EndX) + "," + Mm(cut.EndY) +
                ")mm outline=(" + Mm(outline[0]) + "," + Mm(outline[1]) +
                "," + Mm(outline[2]) + "," + Mm(outline[3]) + ")mm");

            AssemblyOuterPart outer;
            string why;
            if (!TryFindAssemblyOuterPart(parent, cut, out outer, out why))
            {
                Notify("Chưa tạo mặt cắt Assembly: " + why);
                return;
            }
            string label = NextAvailableSectionLabel(drawingDoc);
            if (string.IsNullOrEmpty(label))
            {
                Notify("Không tìm được nhãn mặt cắt còn trống.");
                return;
            }
            if (!drawingDoc.ActivateView(parent.GetName2()))
            {
                Notify("Không kích hoạt được View Assembly chứa đường cắt.");
                return;
            }
            drawing.ClearSelection2(true);
            if (!selectedLine.Select4(false, null))
            {
                Notify("Không chọn lại được đường cắt Assembly.");
                return;
            }

            bool verticalCut = UndirectedAngleDifference(cut.AngleDeg, 90) <
                UndirectedAngleDifference(cut.AngleDeg, 0);
            double centerX = (outline[0] + outline[2]) / 2;
            double centerY = (outline[1] + outline[3]) / 2;
            double placeX = verticalCut ? outline[0] - (outline[2] - outline[0]) / 2 - 0.030 : centerX;
            double placeY = verticalCut ? centerY : outline[1] - (outline[3] - outline[1]) / 2 - 0.030;
            try
            {
                int options = (int)swCreateSectionViewAtOptions_e.swCreateSectionView_Partial |
                    (int)swCreateSectionViewAtOptions_e.swCreateSectionView_DisplaySurfaceCut |
                    (int)swCreateSectionViewAtOptions_e.swCreateSectionView_CutSurfaceBodies;
                Log("ASSEMBLY CREATE label=" + label + " options=" + options);
                SolidWorks.Interop.sldworks.View section = drawingDoc.CreateSectionViewAt5(
                    placeX, placeY, 0, label, options, null, 0);
                if (section == null)
                {
                    Notify("SOLIDWORKS không tạo được mặt cắt Assembly từ đường đã chọn.");
                    return;
                }
                Log("ASSEMBLY CREATED view=" + section.GetName2());
                drawing.EditRebuild3();
                DrSection drSection = section.GetSection() as DrSection;
                ConfigureSectionOptions(section, drSection);
                bool omoteVisible = outer.UraViewNormalZ < -1e-7;
                bool outsideMinimum = GetAssemblyOutsideMinimum(omoteVisible);
                bool canonicalLineDirection = Math.Abs(cut.EndX - cut.StartX) >=
                    Math.Abs(cut.EndY - cut.StartY)
                    ? cut.EndX >= cut.StartX : cut.EndY <= cut.StartY;
                if (drSection != null)
                {
                    drSection.SetReversedCutDirection(outsideMinimum != canonicalLineDirection);
                    drawing.EditRebuild3();
                }

                string scaleNotice = SynchronizeAssemblySectionScales(drawing, drawingDoc, section, parent);
                drawing.EditRebuild3();

                bool sideVerified = EnsureAssemblyOuterSide(drawing, section, drSection,
                    outer, omoteVisible);
                int dimensions = AddAssemblySectionDimensions(drawing, drawingDoc, section,
                    outer.ComponentKey, outsideMinimum, out string dimensionNotice);
                if (!string.IsNullOrWhiteSpace(scaleNotice)) dimensionNotice += "\n" + scaleNotice;
                string breakNotice;
                string parentName = parent.GetName2();
                section = AutoBreakAssemblySectionView(drawing, drawingDoc, section,
                    verticalCut, out breakNotice, parent);
                parent = FindSectionView(drawingDoc, parentName) ?? parent;
                bool placed = PlaceAssemblySectionView(drawing, drawingDoc, section, parent, verticalCut);
                if (!placed)
                {
                    // Keep the readable profile visible for the user to arrange;
                    // do not compress it again just to fit an occupied corner.
                    PlaceSectionView(drawing, drawingDoc, section, parent, verticalCut);
                    dimensionNotice += "\nTờ đang thiếu chỗ trống cho mặt cắt đủ rõ. " +
                        "Hãy sắp xếp lại các View trước khi in; cần kiểm tra chỗ chồng nhau.";
                }
                drawing.EditRebuild3();
                Log("ASSEMBLY SECTION outer=" + outer.ComponentName +
                    " label=" + label + " omote=" + omoteVisible +
                    " verified=" + sideVerified + " dims=" + dimensions +
                    " scale=" + FormatSectionScale(section.ScaleDecimal));
                Notify("Đã tạo mặt cắt Assembly " + label + ".\n" +
                    "Tấm ngoài làm chuẩn: " + outer.ComponentName + "\n" +
                    "Hướng ウラ／オモテ: " + (sideVerified ? "đã kiểm tra" :
                        "đã đặt theo tấm ngoài; cần kiểm tra lại hình") + "\n" +
                    "Tỉ lệ: " + FormatSectionScale(section.ScaleDecimal) + "\n" +
                    "DIM đã tạo: " + dimensions +
                    (string.IsNullOrWhiteSpace(dimensionNotice) ? "" :
                        "\n" + dimensionNotice) +
                    (string.IsNullOrWhiteSpace(breakNotice) ? "" :
                        "\n" + breakNotice));
            }
            catch (Exception ex)
            {
                Log("ASSEMBLY SECTION ERROR " + ex);
                Notify("Mặt cắt Assembly chưa hoàn tất: " + ex.Message +
                    "\nKiểm tra Drawing trước khi chạy lại.");
            }
            finally { drawing.ClearSelection2(true); }
        }

        private bool TryFindAssemblyOuterPart(SolidWorks.Interop.sldworks.View view,
            DrawingBendInfo cut,
            out AssemblyOuterPart outer, out string reason)
        {
            outer = null;
            reason = "Không tìm được tấm ngoài có mặt ウラ được đánh dấu màu.";
            // ResolveAllLightWeightComponents requires an assembly window; it
            // does not resolve a model loaded only as a drawing reference.
            // Read the drawing components without changing the active document.
            Log("ASSEMBLY OUTER SCAN reading visible components");
            Array visible = view.GetVisibleComponents() as Array;
            if (visible == null || visible.Length == 0)
            {
                reason = "View không trả component nào. Hãy kiểm tra model tham chiếu " +
                    "và trạng thái Lightweight của Drawing.";
                Log("ASSEMBLY OUTER REJECT NO_VISIBLE_COMPONENTS");
                return false;
            }
            Log("ASSEMBLY OUTER SCAN visible=" + visible.Length);
            if (!TryReadAssemblyModelComponents(view, out reason)) return false;
            reason = "Không tìm được tấm ngoài có mặt ウラ được đánh dấu màu.";
            var candidates = new List<AssemblyOuterPart>();
            int unloaded = 0, noUra = 0, outsideCut = 0, invalidGeometry = 0, mappingFailed = 0, errors = 0;
            foreach (Component2 component in visible.OfType<Component2>())
            {
                string componentName = "<unknown>";
                try
                {
                    componentName = component.Name2;
                    Component2 modelComponent;
                    string configName, componentReason;
                    if (!TryResolveAssemblyDrawingComponent(component, out modelComponent,
                        out configName, out componentReason))
                    {
                        mappingFailed++;
                        Log("ASSEMBLY COMPONENT REJECT " + componentReason + " name=" + componentName);
                        continue;
                    }
                    Log("ASSEMBLY COMPONENT name=" + componentName +
                        " drawingConfig=" + component.ReferencedConfiguration +
                        " modelConfig=" + configName);
                    ModelDoc2 part = modelComponent.GetModelDoc2() as ModelDoc2;
                    if (part == null)
                    {
                        // A lightweight drawing proxy can omit its model pointer
                        // even though that same document is already loaded.
                        string path = modelComponent.GetPathName();
                        if (!string.IsNullOrEmpty(path))
                            part = app.GetOpenDocumentByName(path) as ModelDoc2;
                        if (part != null) Log("ASSEMBLY MODEL FOUND in open documents");
                    }
                    if (part == null)
                    {
                        unloaded++;
                        Log("ASSEMBLY COMPONENT REJECT MODEL_NOT_LOADED name=" + componentName);
                        continue;
                    }
                    if (part.GetType() != (int)swDocumentTypes_e.swDocPART)
                    {
                        Log("ASSEMBLY COMPONENT SKIP NON_PART name=" + componentName);
                        continue;
                    }
                    Face2 face;
                    double area;
                    string faceReason;
                    if (!TryGetLargestUraFace(part, configName, out face, out area, out faceReason))
                    {
                        noUra++;
                        Log("ASSEMBLY COMPONENT REJECT " + faceReason + " name=" + componentName);
                        continue;
                    }
                    if (!UraFaceBoundsMeetCut(component, view, face, cut))
                    {
                        outsideCut++;
                        Log("ASSEMBLY COMPONENT REJECT OUTSIDE_CUT name=" + componentName);
                        continue;
                    }
                    double[] localNormal = face.Normal as double[];
                    double[] viewNormal = TransformComponentVectorToView(component, view, localNormal);
                    double[] faceBox = face.GetBox() as double[];
                    if (viewNormal == null || faceBox == null || faceBox.Length < 6)
                    {
                        invalidGeometry++;
                        Log("ASSEMBLY COMPONENT REJECT INVALID_TRANSFORM_OR_FACE name=" + componentName);
                        continue;
                    }
                    double length = Math.Sqrt(viewNormal[0] * viewNormal[0] +
                        viewNormal[1] * viewNormal[1] + viewNormal[2] * viewNormal[2]);
                    double facing = length < 1e-10 ? 0 : Math.Abs(viewNormal[2] / length);
                    if (facing < .5)
                    {
                        invalidGeometry++;
                        Log("ASSEMBLY COMPONENT REJECT FACE_NOT_FACING_VIEW name=" + componentName +
                            " facing=" + F(facing));
                        continue;
                    }
                    candidates.Add(new AssemblyOuterPart {
                        ComponentKey = AssemblyComponentKey(component),
                        ComponentName = componentName,
                        ProjectedArea = area * facing,
                        UraViewNormalZ = viewNormal[2] / length,
                        UraFaceCenter = new[] { (faceBox[0] + faceBox[3]) / 2,
                            (faceBox[1] + faceBox[4]) / 2,
                            (faceBox[2] + faceBox[5]) / 2 }
                    });
                    Log("ASSEMBLY OUTER CANDIDATE name=" + componentName +
                        " areaMm2=" + F(area * facing * 1000000));
                }
                catch (Exception ex)
                {
                    errors++;
                    Log("ASSEMBLY COMPONENT ERROR name=" + componentName + " " + ex);
                }
            }
            Log("ASSEMBLY OUTER SUMMARY candidates=" + candidates.Count +
                " unloaded=" + unloaded + " noUra=" + noUra + " outsideCut=" + outsideCut +
                " invalidGeometry=" + invalidGeometry + " mappingFailed=" + mappingFailed + " errors=" + errors);
            candidates = candidates.OrderByDescending(c => c.ProjectedArea).ToList();
            if (candidates.Count == 0)
            {
                var details = new List<string>();
                if (mappingFailed > 0)
                {
                    reason = "Chưa đối chiếu được component của Drawing với Assembly.";
                    details.Add(mappingFailed + " component chưa khớp đường dẫn instance " +
                        "hoặc configuration (xem Output debug)");
                }
                if (unloaded > 0) details.Add(unloaded + " component chưa tải model; " +
                    "hãy mở Assembly và Resolve component rồi cập nhật Drawing");
                if (noUra > 0) details.Add(noUra + " chi tiết chưa đọc được mặt ウラ " +
                    "trong configuration tham chiếu");
                if (outsideCut > 0) details.Add(outsideCut + " mặt ウラ không giao vùng đường cắt");
                if (invalidGeometry > 0) details.Add(invalidGeometry +
                    " mặt chưa xác định được hướng hoặc tọa độ");
                if (errors > 0) details.Add(errors + " component có lỗi API (xem Output debug)");
                if (details.Count > 0) reason += "\n\n" + string.Join("\n", details);
                return false;
            }
            // A missing model or failed API read may conceal the outer plate.
            // Do not choose a smaller available part as the orientation reference.
            if (unloaded > 0 || mappingFailed > 0 || errors > 0)
            {
                reason = "Chưa đọc đủ các component để chọn tấm ngoài chắc chắn. " +
                    "Có " + unloaded + " component chưa tải, " + mappingFailed +
                    " component chưa đối chiếu được và " + errors +
                    " component có lỗi API. Hãy Resolve model và cập nhật Drawing; " +
                    "chi tiết được ghi trong Output debug.";
                return false;
            }
            if (candidates.Count > 1 &&
                candidates[1].ProjectedArea >= candidates[0].ProjectedArea * .85)
            {
                reason = "Có nhiều tấm ngoài có diện tích gần bằng nhau (" +
                    candidates[0].ComponentName + ", " + candidates[1].ComponentName +
                    "). Chưa thể chọn chuẩn ウラ／オモテ chắc chắn.";
                return false;
            }
            outer = candidates[0];
            Log("ASSEMBLY OUTER component=" + outer.ComponentName +
                " area=" + F(outer.ProjectedArea) +
                " uraNormalZ=" + F(outer.UraViewNormalZ));
            return true;
        }

        // Assembly drawings use the opposite convention to component drawings:
        // URA -> outer skin at left/bottom; OMOTE -> outer skin at right/top.
        // DIM and the colored-face orientation must use this same convention.
        private static bool GetAssemblyOutsideMinimum(bool omoteVisible)
        {
            return !omoteVisible;
        }

        private static double CalculateAssemblyUnitSectionScale(double unitWidth,
            double unitHeight, double unitDepth, double sheetWidth, double sheetHeight)
        {
            if (!IsPositiveAssemblySize(unitWidth) || !IsPositiveAssemblySize(unitHeight) ||
                !IsPositiveAssemblySize(sheetWidth) || !IsPositiveAssemblySize(sheetHeight)) return 0;
            double paperWidth = Math.Max(.010, sheetWidth - .030);
            double paperHeight = Math.Max(.010, sheetHeight - .042);
            // Match long and short sides, so drawing rotation and which cut was
            // made first cannot change a unit's scale. Square-root enlargement
            // keeps frame details readable while large units receive less scale.
            double unitFit = Math.Min(Math.Max(paperWidth, paperHeight) /
                Math.Max(unitWidth, unitHeight), Math.Min(paperWidth, paperHeight) /
                Math.Min(unitWidth, unitHeight));
            double scale = Math.Sqrt(unitFit);
            // Limit the complete assembly depth on paper, not sheet thickness
            // or the flange depth of one particular horizontal/vertical cut.
            if (IsPositiveAssemblySize(unitDepth))
                scale = Math.Min(scale, Math.Min(paperWidth, paperHeight) * .12 / unitDepth);
            return RoundSectionScale(scale, false);
        }

        private static bool IsPositiveAssemblySize(double value)
        {
            return value > 1e-9 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private bool TryGetAssemblyUnitSize(SolidWorks.Interop.sldworks.View parent,
            out double width, out double height, out double depth)
        {
            width = height = depth = 0;
            ModelDoc2 model = parent.ReferencedDocument as ModelDoc2;
            AssemblyDoc assembly = model as AssemblyDoc;
            double parentScale = parent.ScaleDecimal;
            if (assembly == null || !IsPositiveAssemblySize(parentScale)) return false;
            double[] box = assembly.GetBox(0) as double[];
            MathUtility math = app.IGetMathUtility();
            MathTransform transform = parent.ModelToViewTransform;
            if (box != null && box.Length >= 6 && math != null && transform != null)
            {
                var corners = new List<double[]>();
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        for (int z = 0; z < 2; z++)
                        {
                            MathPoint point = math.CreatePoint(new[] {
                                box[x * 3], box[1 + y * 3], box[2 + z * 3] }) as MathPoint;
                            point = point == null ? null : point.MultiplyTransform(transform) as MathPoint;
                            double[] coordinates = point == null ? null : point.ArrayData as double[];
                            if (coordinates != null && coordinates.Length >= 3) corners.Add(coordinates);
                        }
                if (corners.Count == 8)
                {
                    width = (corners.Max(p => p[0]) - corners.Min(p => p[0])) / parentScale;
                    height = (corners.Max(p => p[1]) - corners.Min(p => p[1])) / parentScale;
                    depth = (corners.Max(p => p[2]) - corners.Min(p => p[2])) / parentScale;
                }
            }
            // Native displayed geometry avoids the fixed GetOutline margin,
            // dimensions and balloons, especially in very small parent views.
            List<ProjectedSegmentInfo> profile = GetDisplayedSegments(parent);
            if (profile.Count > 0)
            {
                width = (profile.Max(s => Math.Max(s.X1, s.X2)) -
                    profile.Min(s => Math.Min(s.X1, s.X2))) / parentScale;
                height = (profile.Max(s => Math.Max(s.Y1, s.Y2)) -
                    profile.Min(s => Math.Min(s.Y1, s.Y2))) / parentScale;
            }
            return IsPositiveAssemblySize(width) && IsPositiveAssemblySize(height);
        }

        private static bool SameAssemblySectionUnit(string path, string config,
            string otherPath, string otherConfig)
        {
            return !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(config) &&
                string.Equals(path, otherPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(config, otherConfig, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class AssemblyAnnotationLayout
        {
            public Annotation Annotation;
            public double X, Y;
        }

        private sealed class AssemblySectionLayout
        {
            public double[] Bounds;
            public readonly List<AssemblyAnnotationLayout> Annotations = new List<AssemblyAnnotationLayout>();
        }

        private static AssemblySectionLayout CaptureAssemblySectionLayout(SolidWorks.Interop.sldworks.View view)
        {
            view.UpdateViewDisplayGeometry();
            var layout = new AssemblySectionLayout { Bounds = view.GetOutline() as double[] };
            Array dimensions = view.GetDisplayDimensions() as Array;
            var annotations = (dimensions == null ? Enumerable.Empty<DisplayDimension>() :
                dimensions.OfType<DisplayDimension>()).Select(d => d.GetAnnotation() as Annotation).ToList();
            Array other = view.GetAnnotations() as Array;
            foreach (Annotation annotation in other == null ? Enumerable.Empty<Annotation>() : other.OfType<Annotation>())
            {
                object specific = annotation.GetSpecificAnnotation();
                if (specific is Note || specific is WeldSymbol || specific is SFSymbol) annotations.Add(annotation);
            }
            foreach (Annotation annotation in annotations.Where(a => a != null).Distinct())
            {
                double[] point = annotation == null ? null : annotation.GetPosition() as double[];
                if (point != null && point.Length >= 2)
                    layout.Annotations.Add(new AssemblyAnnotationLayout {
                        Annotation = annotation, X = point[0], Y = point[1] });
            }
            return layout;
        }

        private static double RemapAssemblyAnnotation(double value, double oldMin,
            double oldMax, double newMin, double newMax)
        {
            // Keep paper-space offsets outside the profile, so extension-line
            // spacing and dimension text remain readable after rebreaking.
            if (value < oldMin) return newMin + value - oldMin;
            if (value > oldMax) return newMax + value - oldMax;
            return oldMax - oldMin > 1e-9 ? newMin + (value - oldMin) *
                (newMax - newMin) / (oldMax - oldMin) : (newMin + newMax) / 2;
        }

        private static void RestoreAssemblySectionLayout(SolidWorks.Interop.sldworks.View view,
            AssemblySectionLayout layout)
        {
            view.UpdateViewDisplayGeometry();
            double[] bounds = view.GetOutline() as double[];
            if (bounds == null || layout.Bounds == null) return;
            foreach (var annotation in layout.Annotations)
                annotation.Annotation.SetPosition2(
                    RemapAssemblyAnnotation(annotation.X, layout.Bounds[0], layout.Bounds[2], bounds[0], bounds[2]),
                    RemapAssemblyAnnotation(annotation.Y, layout.Bounds[1], layout.Bounds[3], bounds[1], bounds[3]), 0);
        }

        private string SynchronizeAssemblySectionScales(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, SolidWorks.Interop.sldworks.View parent)
        {
            double width, height, depth;
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            if (sheet == null || !TryGetAssemblyUnitSize(parent, out width, out height, out depth))
                throw new InvalidOperationException("Không đọc được kích thước unit để tính tỉ lệ chung.");
            double sheetWidth = 0, sheetHeight = 0;
            sheet.GetSize(ref sheetWidth, ref sheetHeight);
            double chosen = CalculateAssemblyUnitSectionScale(width, height, depth,
                sheetWidth, sheetHeight);
            if (!IsPositiveAssemblySize(chosen))
                throw new InvalidOperationException("Không tính được tỉ lệ chung cho mặt cắt Assembly.");
            ModelDoc2 unit = parent.ReferencedDocument as ModelDoc2;
            string unitPath = unit.GetPathName(), config = parent.ReferencedConfiguration;
            var changed = new List<SolidWorks.Interop.sldworks.View>();
            var resized = new HashSet<string>(StringComparer.Ordinal);
            var layouts = new Dictionary<string, AssemblySectionLayout>(StringComparer.Ordinal);
            var notices = new List<string>();
            for (var view = drawingDoc.GetFirstView() as SolidWorks.Interop.sldworks.View;
                view != null; view = view.GetNextView() as SolidWorks.Interop.sldworks.View)
            {
                if (view.GetSection() == null) continue;
                ModelDoc2 referenced = view.ReferencedDocument as ModelDoc2;
                if (referenced == null || !SameAssemblySectionUnit(unitPath, config,
                    referenced.GetPathName(), view.ReferencedConfiguration)) continue;
                if (Math.Abs(view.ScaleDecimal - chosen) > 1e-9)
                {
                    resized.Add(view.GetName2());
                    if (view.GetName2() != section.GetName2())
                        layouts.Add(view.GetName2(), CaptureAssemblySectionLayout(view));
                }
                view.UseParentScale = false;
                view.UseSheetScale = 0;
                if (resized.Contains(view.GetName2())) view.ScaleDecimal = chosen;
                changed.Add(view);
            }
            drawing.EditRebuild3();
            foreach (var existing in changed.Where(v => v.GetName2() != section.GetName2() &&
                resized.Contains(v.GetName2())))
            {
                // Existing dimensions and cut direction remain associative.
                // Old break locations must be replanned at the common scale.
                var oldSection = UnbreakAssemblySectionView(drawing, drawingDoc, existing);
                oldSection.UpdateViewDisplayGeometry();
                double[] oldBox = oldSection.GetOutline() as double[];
                if (oldBox == null || oldBox.Length < 4) continue;
                bool vertical = oldBox[3] - oldBox[1] > oldBox[2] - oldBox[0];
                string ignored;
                oldSection = AutoBreakAssemblySectionView(drawing, drawingDoc, oldSection,
                    vertical, out ignored, parent);
                if (!string.IsNullOrWhiteSpace(ignored))
                {
                    Log("ASSEMBLY SCALE existing view: " + ignored);
                    notices.Add(oldSection.GetName2() + ": " + ignored);
                }
                // Keep the user's existing placement and reposition only the
                // dimension text that SOLIDWORKS leaves in unbroken coordinates.
                RestoreAssemblySectionLayout(oldSection, layouts[oldSection.GetName2()]);
            }
            Log("ASSEMBLY SCALE shared=" + FormatSectionScale(chosen) +
                " unitMm=(" + Mm(width) + "," + Mm(height) + "," + Mm(depth) + ")" +
                " sectionCount=" + changed.Count + " sheet=" + sheet.GetName());
            return string.Join("\n", notices);
        }

        private static SolidWorks.Interop.sldworks.View UnbreakAssemblySectionView(
            ModelDoc2 drawing, DrawingDoc drawingDoc, SolidWorks.Interop.sldworks.View section)
        {
            string name = section.GetName2();
            if (section.IsBroken())
            {
                Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
                drawingDoc.ActivateSheet(sheet.GetName());
                drawing.ClearSelection2(true);
                if (!drawing.Extension.SelectByID2(name, "DRAWINGVIEW", 0, 0, 0,
                    false, 0, null, 0))
                    throw new InvalidOperationException("Không chọn được mặt cắt để cập nhật khoảng ngắt.");
                drawingDoc.UnBreakView();
                drawing.ClearSelection2(true);
                drawing.EditRebuild3();
                section = FindSectionView(drawingDoc, name) ?? section;
            }
            // Reuse the existing native break feature. Deleting it can detach
            // dimensioned sketch blocks and stud symbols from older drawings.
            section.UpdateViewDisplayGeometry();
            return section;
        }

        private static SolidWorks.Interop.sldworks.View ApplyAssemblySectionBreaks(
            ModelDoc2 drawing, DrawingDoc drawingDoc, SolidWorks.Interop.sldworks.View section)
        {
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            string name = section.GetName2();
            if (!drawingDoc.ActivateSheet(sheet.GetName()))
                throw new InvalidOperationException("Không kích hoạt được Sheet.");
            drawing.ClearSelection2(true);
            if (!drawing.Extension.SelectByID2(name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                throw new InvalidOperationException("Không chọn được mặt cắt.");
            drawingDoc.BreakView(); drawing.EditRebuild3(); drawing.ClearSelection2(true);
            var fresh = FindSectionView(drawingDoc, name);
            if (fresh == null || !fresh.IsBroken())
                throw new InvalidOperationException("Chưa áp dụng được Broken View.");
            fresh.UpdateViewDisplayGeometry();
            return fresh;
        }

        private static bool PlaceAssemblySectionView(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, SolidWorks.Interop.sldworks.View parent,
            bool verticalCut)
        {
            section.UpdateViewDisplayGeometry();
            double[] outline = section.GetOutline() as double[];
            double[] parentBox = parent.GetOutline() as double[];
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            if (outline == null || parentBox == null || sheet == null) return false;
            double sheetWidth = 0, sheetHeight = 0;
            sheet.GetSize(ref sheetWidth, ref sheetHeight);
            double width = outline[2] - outline[0], height = outline[3] - outline[1];
            const double margin = .015, gap = .006;
            double bottom = Math.Max(margin, Math.Min(.025, sheetHeight * .08));
            double top = sheetHeight - Math.Max(.020, Math.Min(.030, sheetHeight * .12));
            double minX = margin + width / 2, maxX = sheetWidth - margin - width / 2;
            double minY = bottom + height / 2, maxY = top - height / 2;
            if (minX > maxX || minY > maxY) return false;
            var occupied = new List<double[]>();
            for (SolidWorks.Interop.sldworks.View view = drawingDoc.GetFirstView() as
                SolidWorks.Interop.sldworks.View; view != null;
                view = view.GetNextView() as SolidWorks.Interop.sldworks.View)
            {
                if (view.GetName2() == section.GetName2() || view.ReferencedDocument == null) continue;
                double[] box = view.GetOutline() as double[];
                if (box != null && box.Length >= 4) occupied.Add(box);
            }
            double preferredX = verticalCut ? parentBox[0] - .020 - width / 2 :
                (parentBox[0] + parentBox[2]) / 2;
            double preferredY = verticalCut ? (parentBox[1] + parentBox[3]) / 2 :
                parentBox[1] - .020 - height / 2;
            var xs = new List<double> {minX, maxX, Math.Max(minX, Math.Min(maxX, preferredX))};
            var ys = new List<double> {minY, maxY, Math.Max(minY, Math.Min(maxY, preferredY))};
            foreach (double[] box in occupied)
            {
                xs.Add(box[0] - gap - width / 2); xs.Add(box[2] + gap + width / 2);
                ys.Add(box[1] - gap - height / 2); ys.Add(box[3] + gap + height / 2);
            }
            var positions = (from x in xs.Distinct() from y in ys.Distinct()
                where x >= minX && x <= maxX && y >= minY && y <= maxY
                where !occupied.Any(box => x + width / 2 > box[0] - gap &&
                    x - width / 2 < box[2] + gap && y + height / 2 > box[1] - gap &&
                    y - height / 2 < box[3] + gap)
                orderby Distance2(x, y, preferredX, preferredY)
                select new[] {x, y}).ToList();
            if (positions.Count == 0) return false;
            section.RemoveAlignment(); section.Position = positions[0]; drawing.EditRebuild3();
            Log("ASSEMBLY POSITION clearOfViews=True center=(" + Mm(positions[0][0]) +
                "," + Mm(positions[0][1]) + ")mm");
            return true;
        }

        private sealed class AssemblyBreakSpan
        {
            public double First, Second;
        }

        private static List<Tuple<double, double>> GetAssemblyBreakProtectedIntervals(
            List<ProjectedSegmentInfo> profile, bool vertical)
        {
            var intervals = new List<Tuple<double, double>>();
            // Only straight runs parallel to the profile's long axis may be
            // removed. Cross edges and turns preserve frame legs, fillets and
            // intermediate components. Distances here are on the printed sheet.
            const double clearance = .004;
            foreach (ProjectedSegmentInfo segment in profile)
            {
                double a = vertical ? segment.Y1 : segment.X1;
                double b = vertical ? segment.Y2 : segment.X2;
                double across = vertical ? Math.Abs(segment.X2 - segment.X1) :
                    Math.Abs(segment.Y2 - segment.Y1);
                // Preserve short parallel flanges as well as cross edges.
                // Breaking a 33/38.5 mm frame flange can hide its sketch DIMs
                // even when the transverse wall itself is retained.
                if ((across > .00002 && across > Math.Abs(b - a) * .06) ||
                    segment.Length <= .030)
                    intervals.Add(Tuple.Create(Math.Min(a, b) - clearance,
                        Math.Max(a, b) + clearance));
            }
            return intervals;
        }

        private static bool TryPlanAssemblyBreaks(double minimum, double maximum,
            double requestedLength, IEnumerable<Tuple<double, double>> protectedIntervals,
            out List<AssemblyBreakSpan> breaks, out double retainedLength)
        {
            breaks = new List<AssemblyBreakSpan>();
            retainedLength = maximum - minimum;
            if (double.IsNaN(retainedLength) || double.IsInfinity(retainedLength) ||
                retainedLength <= 0 || requestedLength <= 0) return false;
            const double gap = .001, endRun = .010, straightRun = .006;
            var protectedRanges = protectedIntervals.Concat(new[] {
                Tuple.Create(minimum, minimum + endRun),
                Tuple.Create(maximum - endRun, maximum) })
                .Select(range => Tuple.Create(Math.Max(minimum, range.Item1),
                    Math.Min(maximum, range.Item2)))
                .Where(range => range.Item2 > range.Item1)
                .OrderBy(range => range.Item1).ToList();
            var merged = new List<Tuple<double, double>>();
            foreach (var range in protectedRanges)
            {
                if (merged.Count == 0 || range.Item1 > merged.Last().Item2)
                    merged.Add(range);
                else
                {
                    var previous = merged.Last();
                    merged[merged.Count - 1] = Tuple.Create(previous.Item1,
                        Math.Max(previous.Item2, range.Item2));
                }
            }
            var free = new List<AssemblyBreakSpan>();
            for (int i = 1; i < merged.Count; i++)
                // Break the long central runs, keeping short end runs intact.
                // Short end breaks can swallow a stud/sketch block and its
                // associative 38.5/53.5 mm DIM even if model edges survive.
                if (merged[i].Item1 - merged[i - 1].Item2 > .050)
                    free.Add(new AssemblyBreakSpan {
                        First = merged[i - 1].Item2, Second = merged[i].Item1 });
            // A protected middle frame can require two or more breaks. Never
            // discard it or reduce scale when a single large break cannot fit.
            double capacity = free.Sum(span => span.Second - span.First - straightRun - gap);
            retainedLength = Math.Max(requestedLength, maximum - minimum - capacity);
            retainedLength = Math.Min(maximum - minimum, retainedLength);
            double shorten = maximum - minimum - retainedLength;
            if (shorten < .003 || free.Count == 0) return false;
            var selected = new List<AssemblyBreakSpan>();
            double selectedCapacity = 0;
            foreach (var span in free.OrderByDescending(s => s.Second - s.First))
            {
                selected.Add(span);
                selectedCapacity += span.Second - span.First - straightRun - gap;
                if (selectedCapacity >= shorten - 1e-9) break;
            }
            double removal = shorten + selected.Count * gap;
            double removalCapacity = selected.Sum(s => s.Second - s.First - straightRun);
            foreach (var span in selected)
            {
                double length = removal * (span.Second - span.First - straightRun) / removalCapacity;
                double middle = (span.First + span.Second) / 2;
                breaks.Add(new AssemblyBreakSpan {
                    First = middle - length / 2, Second = middle + length / 2 });
            }
            breaks = breaks.OrderBy(span => span.First).ToList();
            return true;
        }

        private SolidWorks.Interop.sldworks.View AutoBreakAssemblySectionView(
            ModelDoc2 drawing, DrawingDoc drawingDoc, SolidWorks.Interop.sldworks.View section,
            bool vertical, out string notice, SolidWorks.Interop.sldworks.View parent = null)
        {
            notice = "";
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            drawingDoc.ActivateView(section.GetName2());
            List<ProjectedSegmentInfo> profile = GetDisplayedSegments(section);
            double[] outline = section.GetOutline() as double[];
            if (sheet == null || outline == null || profile.Count == 0)
            {
                notice = "Chưa đọc đủ hình học để thu ngắn an toàn; giữ nguyên mặt cắt.";
                return section;
            }
            double width = 0, height = 0;
            sheet.GetSize(ref width, ref height);
            int axis = vertical ? 1 : 0;
            double[] origin = section.Position as double[];
            if (origin == null || origin.Length < 2) return section;
            // Position is the geometric view center. Unbreaking older derived
            // sections can leave their polyline/model transforms translated by
            // the former breaks. Express the profile about its geometric center
            // before converting to InsertBreak3's view-relative coordinates.
            double center = profile.Min(s => vertical ? Math.Min(s.Y1,s.Y2) : Math.Min(s.X1,s.X2)) / 2 +
                profile.Max(s => vertical ? Math.Max(s.Y1,s.Y2) : Math.Max(s.X1,s.X2)) / 2;
            double shift = origin[axis] - center;
            profile = profile.Select(s => new ProjectedSegmentInfo {
                X1 = s.X1 + (vertical ? 0 : shift), X2 = s.X2 + (vertical ? 0 : shift),
                Y1 = s.Y1 + (vertical ? shift : 0), Y2 = s.Y2 + (vertical ? shift : 0) }).ToList();
            double minimum = profile.Min(s => vertical ? Math.Min(s.Y1,s.Y2) : Math.Min(s.X1,s.X2)) - .0042;
            double maximum = profile.Max(s => vertical ? Math.Max(s.Y1,s.Y2) : Math.Max(s.X1,s.X2)) + .0042;
            double usable = vertical ? height - .042 : width - .030;
            // Leave enough length to read the frame ends and the middle parts.
            // The target follows the current sheet and preserved geometry;
            // material thickness and part names do not participate.
            double requested = usable * (vertical ? .55 : .45);
            if (parent != null)
            {
                List<ProjectedSegmentInfo> parentProfile = GetDisplayedSegments(parent);
                if (parentProfile.Count > 0)
                {
                    double parentLength = vertical ?
                        parentProfile.Max(s => Math.Max(s.Y1, s.Y2)) - parentProfile.Min(s => Math.Min(s.Y1, s.Y2)) :
                        parentProfile.Max(s => Math.Max(s.X1, s.X2)) - parentProfile.Min(s => Math.Min(s.X1, s.X2));
                    if (IsPositiveAssemblySize(parentLength))
                        requested = Math.Min(requested, Math.Max(.060, parentLength * (vertical ? 1.0 : 1.4)));
                }
            }
            List<AssemblyBreakSpan> breaks;
            double retained;
            if (!TryPlanAssemblyBreaks(minimum, maximum, requested,
                GetAssemblyBreakProtectedIntervals(profile, vertical), out breaks, out retained))
            {
                Log("ASSEMBLY BREAK skipped no removable straight run; preserve geometry");
                Array existingLines = section.GetBreakLines() as Array;
                if (existingLines != null && existingLines.Length > 0 && !section.IsBroken())
                    section = ApplyAssemblySectionBreaks(drawing, drawingDoc, section);
                return section;
            }
            string viewName = section.GetName2();
            Log("ASSEMBLY BREAK PLAN before=" + Mm(maximum - minimum) +
                "mm retained=" + Mm(retained) + "mm breaks=" + breaks.Count +
                " preserveFrame=True gap=1mm view=" + viewName +
                " originMm=(" + Mm(origin[0]) + "," + Mm(origin[1]) + ")" +
                " profileMm=(" + Mm(profile.Min(s => vertical ? Math.Min(s.Y1,s.Y2) : Math.Min(s.X1,s.X2))) +
                "," + Mm(profile.Max(s => vertical ? Math.Max(s.Y1,s.Y2) : Math.Max(s.X1,s.X2))) + ")");
            try
            {
                if (!drawingDoc.ActivateView(viewName))
                    throw new InvalidOperationException("Không kích hoạt được mặt cắt.");
                section.BreakLineGap = .001;
                int orientation = (int)(vertical ? swBreakLineOrientation_e.swBreakLineHorizontal :
                    swBreakLineOrientation_e.swBreakLineVertical);
                Array oldLines = section.GetBreakLines() as Array;
                var reusable = (oldLines == null ? Enumerable.Empty<BreakLine>() : oldLines.OfType<BreakLine>())
                    .Where(line => line.Orientation == orientation).OrderBy(line => line.GetPosition(0)).ToList();
                SelectionMgr manager = drawing.SelectionManager as SelectionMgr;
                SelectData selectData = manager.CreateSelectData() as SelectData;
                selectData.View = section;
                foreach (BreakLine extra in reusable.Skip(breaks.Count))
                {
                    drawing.ClearSelection2(true);
                    if (!extra.Select(false, selectData) || !drawing.Extension.DeleteSelection2(0))
                        throw new InvalidOperationException("Không cập nhật được khoảng ngắt cũ.");
                }
                drawing.ClearSelection2(true);
                for (int index = 0; index < breaks.Count; index++)
                {
                    AssemblyBreakSpan span = breaks[index];
                    object created;
                    if (index < reusable.Count)
                        created = reusable[index].SetPosition(span.First - origin[axis], span.Second - origin[axis]) ?
                            reusable[index] : null;
                    else created = section.InsertBreak3(orientation,
                            span.First - origin[axis], span.Second - origin[axis],
                            (int)swBreakLineStyle_e.swBreakLine_Straight, 1, true);
                    if (created == null)
                        throw new InvalidOperationException("SOLIDWORKS không tạo được khoảng ngắt.");
                    Log("ASSEMBLY BREAK INTERVAL (" + Mm(span.First) + "," +
                        Mm(span.Second) + ")mm");
                }
                drawing.EditRebuild3();
                var fresh = ApplyAssemblySectionBreaks(drawing, drawingDoc, section);
                fresh.BreakLineGap = .001;
                fresh.UpdateViewDisplayGeometry();
                double[] final = fresh.GetOutline() as double[];
                Log("ASSEMBLY BREAK APPLIED retained=" + (final == null ? "UNKNOWN" :
                    Mm(final[axis + 2] - final[axis])) + "mm breaks=" + breaks.Count);
                return fresh;
            }
            catch (Exception ex)
            {
                Log("ASSEMBLY BREAK ERROR " + ex);
                notice = "Chưa thu ngắn được mặt cắt an toàn. Kiểm tra các khoảng ngắt trước khi in.";
                return FindSectionView(drawingDoc, viewName) ?? section;
            }
            finally { drawing.ClearSelection2(true); }
        }

        private bool TryReadAssemblyModelComponents(SolidWorks.Interop.sldworks.View view,
            out string reason)
        {
            assemblyModelComponents.Clear();
            assemblyDrawingRootNames.Clear();
            reason = "";
            ModelDoc2 model = view.ReferencedDocument as ModelDoc2;
            AssemblyDoc assembly = model as AssemblyDoc;
            Configuration active = model == null || model.ConfigurationManager == null ? null :
                model.ConfigurationManager.ActiveConfiguration;
            string referencedConfig = view.ReferencedConfiguration;
            // GetComponents returns the active assembly configuration. Refuse a
            // different one rather than treating the part's active config as the
            // configuration of a particular assembly instance.
            if (assembly == null || active == null ||
                !string.Equals(active.Name, referencedConfig, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Configuration của Assembly đang tải khác configuration của View. " +
                    "View: " + referencedConfig + "; Assembly: " +
                    (active == null ? "chưa tải" : active.Name) +
                    ". Hãy mở Assembly đúng configuration rồi cập nhật Drawing.";
                Log("ASSEMBLY MODEL CONFIG REJECT referenced=" + referencedConfig +
                    " active=" + (active == null ? "NONE" : active.Name));
                return false;
            }
            // Drawing proxies include the drawing's assembly root instance in
            // Name2; model component names are relative to the assembly document.
            // Only strip roots obtained from this view, never arbitrary ancestors.
            RememberAssemblyDrawingRootNames(view);
            Log("ASSEMBLY DRAWING ROOT names=" + string.Join(";", assemblyDrawingRootNames));
            Array components = assembly.GetComponents(false) as Array;
            if (components != null)
                foreach (Component2 component in components.OfType<Component2>())
                {
                    string name = NormalizeAssemblyInstanceName(component.Name2);
                    if (string.IsNullOrEmpty(name)) continue;
                    Log("ASSEMBLY MODEL COMPONENT name=" + name +
                        " config=" + component.ReferencedConfiguration);
                    // Preserve the complete subassembly path. Repeated part names
                    // and instances must never resolve by filename or leaf name.
                    if (assemblyModelComponents.ContainsKey(name))
                        assemblyModelComponents[name] = null;
                    else assemblyModelComponents.Add(name, component);
                }
            Log("ASSEMBLY MODEL COMPONENTS config=" + referencedConfig +
                " count=" + assemblyModelComponents.Count);
            if (assemblyModelComponents.Count > 0) return true;
            reason = "Assembly không trả cây component của configuration " + referencedConfig +
                ". Hãy kiểm tra model tham chiếu rồi cập nhật Drawing.";
            return false;
        }

        private void RememberAssemblyDrawingRootNames(SolidWorks.Interop.sldworks.View view)
        {
            DrawingComponent root = view == null ? null : view.RootDrawingComponent;
            if (root == null) return;
            string rootName = NormalizeAssemblyInstanceName(root.Name);
            if (!string.IsNullOrEmpty(rootName) && assemblyDrawingRootNames.Add(rootName))
                Log("ASSEMBLY DRAWING ROOT view=" + view.GetName2() + " name=" + rootName);
            Component2 rootComponent = root.Component;
            rootName = NormalizeAssemblyInstanceName(rootComponent == null ? null : rootComponent.Name2);
            if (!string.IsNullOrEmpty(rootName) && assemblyDrawingRootNames.Add(rootName))
                Log("ASSEMBLY DRAWING ROOT view=" + view.GetName2() + " componentName=" + rootName);
        }

        private static string NormalizeAssemblyInstanceName(string name)
        {
            return (name ?? "").Replace('\\', '/').Trim('/');
        }

        private bool TryResolveAssemblyDrawingComponent(Component2 drawingComponent,
            out Component2 modelComponent, out string configName, out string reason)
        {
            modelComponent = null;
            configName = null;
            reason = "MODEL_COMPONENT_NOT_FOUND";
            if (drawingComponent == null || !TryFindAssemblyModelComponent(
                drawingComponent.Name2, out modelComponent)) return false;
            string drawingPath = drawingComponent.GetPathName();
            string modelPath = modelComponent.GetPathName();
            if (!string.IsNullOrEmpty(drawingPath) && !string.IsNullOrEmpty(modelPath) &&
                !string.Equals(drawingPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                reason = "MODEL_COMPONENT_PATH_MISMATCH";
                return false;
            }
            configName = modelComponent.ReferencedConfiguration;
            if (string.IsNullOrWhiteSpace(configName))
            {
                reason = "MODEL_COMPONENT_CONFIG_EMPTY";
                return false;
            }
            string drawingConfig = drawingComponent.ReferencedConfiguration;
            if (!string.IsNullOrWhiteSpace(drawingConfig) &&
                !string.Equals(drawingConfig, configName, StringComparison.OrdinalIgnoreCase))
            {
                reason = "MODEL_COMPONENT_CONFIG_MISMATCH drawing=" + drawingConfig +
                    " model=" + configName;
                return false;
            }
            return true;
        }

        private bool TryFindAssemblyModelComponent(string drawingName, out Component2 component)
        {
            component = null;
            string name = NormalizeAssemblyInstanceName(drawingName);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
            foreach (string root in assemblyDrawingRootNames)
                if (name.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                    keys.Add(name.Substring(root.Length + 1));
            string matchedName = null;
            foreach (string key in keys)
            {
                Component2 candidate;
                if (!assemblyModelComponents.TryGetValue(key, out candidate)) continue;
                // Keep ambiguity visible even if an alternate spelling finds one
                // component. Never match by filename or the final path segment.
                if (candidate == null || (matchedName != null &&
                    !string.Equals(matchedName, key, StringComparison.OrdinalIgnoreCase)))
                {
                    component = null;
                    return false;
                }
                matchedName = key;
                component = candidate;
            }
            return component != null;
        }

        private Component2 GetAssemblyModelComponent(Component2 drawingComponent)
        {
            Component2 source;
            if (drawingComponent != null && TryFindAssemblyModelComponent(drawingComponent.Name2, out source))
                return source;
            return drawingComponent;
        }

        private bool UraFaceBoundsMeetCut(Component2 component,
            SolidWorks.Interop.sldworks.View view, Face2 face, DrawingBendInfo cut)
        {
            double[] box = face.GetBox() as double[];
            if (box == null || box.Length < 6 || cut == null) return false;
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int ix = 0; ix < 2; ix++)
                for (int iy = 0; iy < 2; iy++)
                    for (int iz = 0; iz < 2; iz++)
                    {
                        double[] p = ProjectComponentPoint(component, view, new[] {
                            box[ix == 0 ? 0 : 3], box[iy == 0 ? 1 : 4],
                            box[iz == 0 ? 2 : 5] });
                        if (p == null) return false;
                        minX = Math.Min(minX, p[0]); maxX = Math.Max(maxX, p[0]);
                        minY = Math.Min(minY, p[1]); maxY = Math.Max(maxY, p[1]);
                    }
            // Bounding boxes are only a preflight. The actual section is still
            // generated by SOLIDWORKS from the user's selected finite line.
            const double tolerance = .002;
            bool meets = Math.Max(cut.StartX, cut.EndX) >= minX - tolerance &&
                Math.Min(cut.StartX, cut.EndX) <= maxX + tolerance &&
                Math.Max(cut.StartY, cut.EndY) >= minY - tolerance &&
                Math.Min(cut.StartY, cut.EndY) <= maxY + tolerance;
            Log("ASSEMBLY URA BOUNDS component=" + component.Name2 + " sheet=(" +
                Mm(minX) + "," + Mm(minY) + "," + Mm(maxX) + "," + Mm(maxY) +
                ")mm meetsCut=" + meets);
            return meets;
        }

        private static bool TryGetLargestUraFace(ModelDoc2 part, string configName,
            out Face2 best, out double bestArea, out string reason)
        {
            best = null;
            bestArea = 0;
            reason = "URA_FACE_NOT_FOUND";
            Log("ASSEMBLY URA reading display state config=" + configName);
            Configuration config = part.GetConfigurationByName(configName) as Configuration;
            if (config != null)
            {
                Array states = config.GetDisplayStates() as Array;
                if (states != null && states.Length > 0)
                {
                    object rawFaces;
                    Array materials = config.GetDisplayStateFaceProperties(
                        Convert.ToString(states.GetValue(0)), out rawFaces) as Array;
                    Array faces = rawFaces as Array;
                    Log("ASSEMBLY URA displayState=" + Convert.ToString(states.GetValue(0)) +
                        " faceCount=" + (faces == null ? 0 : faces.Length));
                    if (faces != null && materials != null)
                        for (int i = 0; i < faces.Length; i++)
                        {
                            Face2 face = faces.GetValue(i) as Face2;
                            if (face == null || !IsPinkMaterial(ReadDisplayStateMaterial(materials, i)))
                                continue;
                            double area = Math.Abs(face.GetArea());
                            if (area > bestArea) { best = face; bestArea = area; }
                        }
                }
            }
            if (best != null) return true;
            // The live face material is trustworthy only when the part is already
            // showing the configuration referenced by this assembly component.
            Configuration active = part.ConfigurationManager.ActiveConfiguration;
            if (active == null || !string.Equals(active.Name, configName,
                StringComparison.OrdinalIgnoreCase))
            {
                reason = "URA_CONFIG_NOT_ACTIVE active=" + (active == null ? "NONE" : active.Name) +
                    " referenced=" + configName;
                return false;
            }
            PartDoc partDoc = part as PartDoc;
            Array bodies = partDoc == null ? null :
                partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as Array;
            if (bodies == null) return false;
            foreach (Body2 body in bodies.OfType<Body2>())
            {
                Array faces = body.GetFaces() as Array;
                if (faces == null) continue;
                foreach (Face2 face in faces.OfType<Face2>())
                {
                    double[] material = face.GetMaterialPropertyValues2(
                        (int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                    if (material == null || material.Length < 3)
                        material = face.MaterialPropertyValues as double[];
                    if (!IsPinkMaterial(material)) continue;
                    double area = Math.Abs(face.GetArea());
                    if (area > bestArea) { best = face; bestArea = area; }
                }
            }
            return best != null;
        }

        private string AssemblyComponentKey(Component2 component)
        {
            component = GetAssemblyModelComponent(component);
            if (component == null) return "";
            return (component.Name2 ?? "") + "|" +
                (component.GetPathName() ?? "") + "|" +
                (component.ReferencedConfiguration ?? "");
        }

        private double[] TransformComponentVectorToView(Component2 component,
            SolidWorks.Interop.sldworks.View view, double[] vector)
        {
            if (vector == null || vector.Length < 3) return null;
            double[] origin = ProjectComponentPoint(component, view, new double[] { 0, 0, 0 });
            double[] tip = ProjectComponentPoint(component, view, vector);
            return origin == null || tip == null ? null : new double[] {
                tip[0] - origin[0], tip[1] - origin[1], tip[2] - origin[2] };
        }

        private double[] ProjectComponentPoint(Component2 component,
            SolidWorks.Interop.sldworks.View view, double[] localPoint)
        {
            if (localPoint == null || localPoint.Length < 3) return null;
            MathUtility math = app.IGetMathUtility();
            component = GetAssemblyModelComponent(component);
            MathTransform componentTransform = component == null ? null : component.Transform2;
            MathTransform viewTransform = view == null ? null : view.ModelToViewTransform;
            if (math == null || componentTransform == null || viewTransform == null) return null;
            MathPoint point = math.CreatePoint(new[] { localPoint[0], localPoint[1], localPoint[2] }) as MathPoint;
            point = point == null ? null : point.MultiplyTransform(componentTransform) as MathPoint;
            point = point == null ? null : point.MultiplyTransform(viewTransform) as MathPoint;
            return point == null ? null : point.ArrayData as double[];
        }

        private bool EnsureAssemblyOuterSide(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View section, DrSection drSection,
            AssemblyOuterPart outer, bool omoteVisible)
        {
            if (drSection == null) return false;
            bool pinkAtMinimum;
            if (!TryGetAssemblyPinkSide(section, outer, out pinkAtMinimum))
            {
                Log("ASSEMBLY SIDE cannot verify outer component=" + outer.ComponentKey);
                return false;
            }
            // The colored face is URA, opposite the exterior skin used by DIM.
            // In Assembly drawings this is reversed from the component rule.
            bool desiredPinkMinimum = !GetAssemblyOutsideMinimum(omoteVisible);
            bool reversedBefore = drSection.GetReversedCutDirection();
            bool toggled = pinkAtMinimum != desiredPinkMinimum;
            if (toggled)
            {
                drSection.SetReversedCutDirection(!reversedBefore);
                drawing.EditRebuild3();
            }
            bool verifiedMinimum;
            bool verified = TryGetAssemblyPinkSide(section, outer, out verifiedMinimum) &&
                verifiedMinimum == desiredPinkMinimum;
            Log("ASSEMBLY SIDE parentFace=" + (omoteVisible ? "OMOTE" : "URA") +
                " desiredPinkMinimum=" + desiredPinkMinimum +
                " actualPinkMinimum=" + pinkAtMinimum + " toggled=" + toggled +
                " reversedBefore=" + reversedBefore + " reversedAfter=" +
                drSection.GetReversedCutDirection() + " verified=" + verified +
                " verifiedPinkMinimum=" + verifiedMinimum);
            return verified;
        }

        private bool TryGetAssemblyPinkSide(SolidWorks.Interop.sldworks.View section,
            AssemblyOuterPart outer, out bool atMinimum)
        {
            atMinimum = false;
            var own = GetAssemblySectionEdges(section)
                .Where(e => e.ComponentKey == outer.ComponentKey).ToList();
            if (own.Count == 0) return false;
            // Slice sections use a temporary SectionAssembly; its component
            // proxies need not share the parent drawing's instance prefix.
            Component2 component = assemblyModelComponents.Values
                .FirstOrDefault(c => c != null && AssemblyComponentKey(c) == outer.ComponentKey);
            double[] pinkPoint = component == null ? null :
                ProjectComponentPoint(component, section, outer.UraFaceCenter);
            if (pinkPoint == null) return false;
            bool wide = own.Max(e => Math.Max(e.X1, e.X2)) -
                own.Min(e => Math.Min(e.X1, e.X2)) >=
                own.Max(e => Math.Max(e.Y1, e.Y2)) -
                own.Min(e => Math.Min(e.Y1, e.Y2));
            double minimum = wide ? own.Min(e => Math.Min(e.Y1, e.Y2)) :
                own.Min(e => Math.Min(e.X1, e.X2));
            double maximum = wide ? own.Max(e => Math.Max(e.Y1, e.Y2)) :
                own.Max(e => Math.Max(e.X1, e.X2));
            if (maximum - minimum < 1e-7) return false;
            double normal = wide ? pinkPoint[1] : pinkPoint[0];
            // A colored-face center in the middle of the profile is not a
            // trustworthy orientation reference for this section.
            if (Math.Abs(normal - (minimum + maximum) / 2) <
                (maximum - minimum) * .08) return false;
            atMinimum = normal <= (minimum + maximum) / 2;
            return true;
        }

        private List<AssemblySectionEdge> GetAssemblySectionEdges(
            SolidWorks.Interop.sldworks.View view)
        {
            var result = new List<AssemblySectionEdge>();
            // A derived section can have a different drawing root instance name
            // from its parent. Keep both mapped to the same model identities.
            RememberAssemblyDrawingRootNames(view);
            // Newly created slice sections can expose no visible topology until
            // the display geometry has been updated.
            view.UpdateViewDisplayGeometry();
            Array components = view.GetVisibleComponents() as Array;
            foreach (Component2 component in (components == null ?
                Enumerable.Empty<Component2>() : components.OfType<Component2>()))
            {
                Array visible = null;
                try { visible = view.GetVisibleEntities2(component,
                    (int)swViewEntityType_e.swViewEntityType_Edge) as Array; }
                catch (Exception ex)
                {
                    Log("ASSEMBLY EDGE READ " + component.Name2 + " " + ex.Message);
                }
                if (visible == null) continue;
                foreach (Edge edge in visible.OfType<Edge>())
                {
                    AddAssemblySectionEdge(view, edge, result);
                }
            }
            // GetPolylines7 returns selectable native cut edges even when
            // GetVisibleEntities2 omits them in a partial/slice section.
            object polylines;
            Array displayedEdges = view.GetPolylines7(1, out polylines) as Array;
            if (displayedEdges != null)
                foreach (Edge edge in displayedEdges.OfType<Edge>())
                    AddAssemblySectionEdge(view, edge, result);
            Log("ASSEMBLY SECTION EDGES view=" + view.Name + " count=" + result.Count);
            return result;
        }

        private void AddAssemblySectionEdge(SolidWorks.Interop.sldworks.View view,
            Edge edge, List<AssemblySectionEdge> result)
        {
            Entity entity = edge as Entity;
            Component2 owner = entity == null ? null : entity.IGetComponent2();
            Component2 component;
            string config, reason;
            if (!TryResolveAssemblyDrawingComponent(owner, out component, out config, out reason))
                return;
            Curve curve = edge.GetCurve() as Curve;
            if (curve == null || !curve.IsLine()) return;
            Vertex start = edge.GetStartVertex() as Vertex;
            Vertex end = edge.GetEndVertex() as Vertex;
            double[] p1 = start == null ? null :
                ProjectComponentPoint(component, view, start.GetPoint() as double[]);
            double[] p2 = end == null ? null :
                ProjectComponentPoint(component, view, end.GetPoint() as double[]);
            if (p1 == null || p2 == null || p1.Length < 2 || p2.Length < 2) return;
            var info = new AssemblySectionEdge {
                ComponentKey = AssemblyComponentKey(component),
                Edge = edge, Start = start, End = end,
                X1 = p1[0], Y1 = p1[1], X2 = p2[0], Y2 = p2[1]
            };
            // HLR can repeat the same native edge in several display records.
            if (info.Length > 1e-7 && !result.Any(old =>
                old.ComponentKey == info.ComponentKey &&
                ((Distance2(old.X1, old.Y1, info.X1, info.Y1) < 1e-8 &&
                  Distance2(old.X2, old.Y2, info.X2, info.Y2) < 1e-8) ||
                 (Distance2(old.X1, old.Y1, info.X2, info.Y2) < 1e-8 &&
                  Distance2(old.X2, old.Y2, info.X1, info.Y1) < 1e-8))))
                result.Add(info);
        }

        private int AddAssemblySectionDimensions(ModelDoc2 drawing, DrawingDoc drawingDoc,
            SolidWorks.Interop.sldworks.View section, string outerKey,
            bool outsideMinimum, out string notice)
        {
            notice = "";
            List<AssemblySectionEdge> edges = GetAssemblySectionEdges(section);
            if (edges.Count == 0)
            {
                notice = "Chưa DIM: SOLIDWORKS không trả cạnh của các component trong mặt cắt.";
                return 0;
            }
            AssemblyOuterRun run;
            if (!TryBuildAssemblyOuterRun(edges, outerKey, outsideMinimum,
                section.ScaleDecimal, out run))
            {
                notice = "Chưa DIM: không xác định được cạnh ngoài của tấm ngoài cùng.";
                return 0;
            }
            if (!drawingDoc.ActivateView(section.GetName2()))
            {
                notice = "Chưa DIM: không kích hoạt được mặt cắt Assembly.";
                return 0;
            }
            SelectionMgr manager = drawing.SelectionManager as SelectionMgr;
            SelectData data = manager == null ? null : manager.CreateSelectData() as SelectData;
            if (data == null)
            {
                notice = "Chưa DIM: không tạo được đối tượng chọn cạnh.";
                return 0;
            }
            data.View = section;
            int added = 0;
            var messages = new List<string>();
            if (TryAddAssemblyOverallDimension(drawing, section, data, run,
                edges.Where(e => e.ComponentKey == outerKey).ToList())) added++;
            else messages.Add("Chưa tạo DIM kích thước tổng thể.");
            foreach (bool atFirst in new[] { true, false })
            {
                double endX = atFirst ? run.FirstX : run.LastX;
                double endY = atFirst ? run.FirstY : run.LastY;
                if (OuterPanelHasFlange(edges, outerKey, run,
                    endX, endY, section.ScaleDecimal))
                {
                    Log("ASSEMBLY GAP SKIP outer panel flange at " +
                        (atFirst ? "first" : "last") + " end");
                    continue;
                }
                AssemblyFrameCorner corner;
                if (!TryFindAssemblyFrameCorner(edges, outerKey, run,
                    endX, endY, section.ScaleDecimal, out corner))
                {
                    messages.Add("Không xác định chắc chắn giao điểm frame ở đầu " +
                        (atFirst ? "thứ nhất" : "thứ hai") + ".");
                    continue;
                }
                Vertex outerVertex = atFirst ? run.FirstVertex : run.LastVertex;
                if (TryAddAssemblyGapDimension(drawing, section, data,
                    outerVertex, endX, endY, corner, run)) added++;
                else messages.Add("Chưa tạo được DIM khoảng hở ở đầu " +
                    (atFirst ? "thứ nhất" : "thứ hai") + ".");
            }
            notice = string.Join("\n", messages);
            drawing.ClearSelection2(true);
            return added;
        }

        private static bool TryBuildAssemblyOuterRun(List<AssemblySectionEdge> all,
            string outerKey, bool outsideMinimum, double scale,
            out AssemblyOuterRun result)
        {
            result = null;
            var own = all.Where(e => e.ComponentKey == outerKey).ToList();
            if (own.Count == 0 || scale <= 0) return false;
            double width = own.Max(e => Math.Max(e.X1, e.X2)) -
                own.Min(e => Math.Min(e.X1, e.X2));
            double height = own.Max(e => Math.Max(e.Y1, e.Y2)) -
                own.Min(e => Math.Min(e.Y1, e.Y2));
            bool wide = width >= height;
            var aligned = own.Where(e =>
            {
                double along = wide ? Math.Abs(e.X2 - e.X1) : Math.Abs(e.Y2 - e.Y1);
                double across = wide ? Math.Abs(e.Y2 - e.Y1) : Math.Abs(e.X2 - e.X1);
                return along >= across * 4 && along * 1000 / scale >= 5;
            }).ToList();
            if (aligned.Count == 0) return false;
            double longest = aligned.Max(e => e.Length);
            aligned = aligned.Where(e => e.Length >= longest * .25).ToList();
            double extreme = outsideMinimum ?
                aligned.Min(e => wide ? (e.Y1 + e.Y2) / 2 : (e.X1 + e.X2) / 2) :
                aligned.Max(e => wide ? (e.Y1 + e.Y2) / 2 : (e.X1 + e.X2) / 2);
            double band = Math.Max(.0002, scale * .002);
            var skin = aligned.Where(e => Math.Abs(
                (wide ? (e.Y1 + e.Y2) / 2 : (e.X1 + e.X2) / 2) - extreme) <= band)
                .ToList();
            if (skin.Count == 0) return false;
            var endpoints = skin.SelectMany(e => new[] {
                new { Edge = e, Vertex = e.Start, X = e.X1, Y = e.Y1 },
                new { Edge = e, Vertex = e.End, X = e.X2, Y = e.Y2 }
            }).OrderBy(p => wide ? p.X : p.Y).ToList();
            var first = endpoints.First();
            var last = endpoints.Last();
            double length = wide ? last.X - first.X : last.Y - first.Y;
            if (length * 1000 / scale < 20) return false;
            result = new AssemblyOuterRun {
                Wide = wide, AtMinimum = outsideMinimum,
                MinAxis = wide ? first.X : first.Y,
                MaxAxis = wide ? last.X : last.Y,
                Normal = extreme,
                FirstEdge = first.Edge, LastEdge = last.Edge,
                FirstVertex = first.Vertex, LastVertex = last.Vertex,
                FirstX = first.X, FirstY = first.Y,
                LastX = last.X, LastY = last.Y
            };
            return true;
        }

        private static bool OuterPanelHasFlange(List<AssemblySectionEdge> edges,
            string outerKey, AssemblyOuterRun run, double endX, double endY,
            double scale)
        {
            double endpointTolerance = scale * .004;
            double minimumLeg = scale * .006;
            foreach (AssemblySectionEdge edge in edges)
            {
                if (edge.ComponentKey != outerKey || edge.Length < minimumLeg) continue;
                double along = run.Wide ? Math.Abs(edge.X2 - edge.X1) :
                    Math.Abs(edge.Y2 - edge.Y1);
                double across = run.Wide ? Math.Abs(edge.Y2 - edge.Y1) :
                    Math.Abs(edge.X2 - edge.X1);
                if (across < along * .6) continue;
                if (Math.Min(Distance2(endX, endY, edge.X1, edge.Y1),
                    Distance2(endX, endY, edge.X2, edge.Y2)) <= endpointTolerance)
                    return true;
            }
            return false;
        }

        private static bool TryFindAssemblyFrameCorner(List<AssemblySectionEdge> edges,
            string outerKey, AssemblyOuterRun run, double endX, double endY,
            double scale, out AssemblyFrameCorner corner)
        {
            corner = null;
            var byComponent = edges.Where(e => e.ComponentKey != outerKey)
                .GroupBy(e => e.ComponentKey);
            var candidates = new List<AssemblyFrameCorner>();
            double maxOffset = scale * .030;
            double joinTolerance = scale * .005;
            foreach (var group in byComponent)
            {
                var bases = group.Where(e =>
                {
                    double along = run.Wide ? Math.Abs(e.X2 - e.X1) :
                        Math.Abs(e.Y2 - e.Y1);
                    double across = run.Wide ? Math.Abs(e.Y2 - e.Y1) :
                        Math.Abs(e.X2 - e.X1);
                    return along >= across * 4 && along >= scale * .005;
                });
                var walls = group.Where(e =>
                {
                    double along = run.Wide ? Math.Abs(e.X2 - e.X1) :
                        Math.Abs(e.Y2 - e.Y1);
                    double across = run.Wide ? Math.Abs(e.Y2 - e.Y1) :
                        Math.Abs(e.X2 - e.X1);
                    return across >= along * .6 && across >= scale * .005;
                }).ToList();
                foreach (AssemblySectionEdge baseEdge in bases)
                    foreach (AssemblySectionEdge wall in walls)
                    {
                        double x, y;
                        if (!IntersectAssemblyLines(baseEdge, wall, out x, out y)) continue;
                        double distanceToBase = Math.Min(Distance2(x, y, baseEdge.X1, baseEdge.Y1),
                            Distance2(x, y, baseEdge.X2, baseEdge.Y2));
                        double distanceToWall = Math.Min(Distance2(x, y, wall.X1, wall.Y1),
                            Distance2(x, y, wall.X2, wall.Y2));
                        if (distanceToBase > joinTolerance || distanceToWall > joinTolerance)
                            continue;
                        double offset = Math.Abs((run.Wide ? x - endX : y - endY));
                        double normalGap = Math.Abs(run.Wide ? y - endY : x - endX);
                        if (offset < scale * .00005 || offset > maxOffset ||
                            normalGap > scale * .012) continue;
                        candidates.Add(new AssemblyFrameCorner {
                            Base = baseEdge, Wall = wall, X = x, Y = y,
                            Offset = offset, NormalGap = normalGap
                        });
                    }
            }
            // First select the frame base on the panel-facing surface, then
            // intersect it with the wall nearest the panel end. Ranking the
            // offset first selects the opposite thickness edge at an angled
            // bend (e.g. 19.333 mm instead of the panel-facing sharp's 20 mm).
            // Compare surfaces in model units, independently of the view scale.
            candidates = candidates.GroupBy(c => c.Base.ComponentKey)
                .Select(group => group.OrderBy(c => Math.Round(c.NormalGap / scale, 6))
                    .ThenBy(c => c.Offset).First())
                .OrderBy(c => c.Offset).ToList();
            Log("ASSEMBLY FRAME candidates=" + candidates.Count + " end=(" +
                Mm(endX) + "," + Mm(endY) + ") " + string.Join(";", candidates.Take(8)
                    .Select(c => c.Base.ComponentKey.Split('|')[0] + ":offset=" +
                        F(c.Offset * 1000 / scale) + ":normal=" +
                        F(Math.Abs(run.Wide ? c.Y - endY : c.X - endX) * 1000 / scale))));
            if (candidates.Count == 0) return false;
            // Two different frame instances near this end remain ambiguous.
            if (candidates.Count > 1 &&
                candidates[1].Offset < candidates[0].Offset + scale * .002)
                return false;
            corner = candidates[0];
            return true;
        }

        private static bool IntersectAssemblyLines(AssemblySectionEdge a,
            AssemblySectionEdge b, out double x, out double y)
        {
            x = y = 0;
            double ax = a.X2 - a.X1, ay = a.Y2 - a.Y1;
            double bx = b.X2 - b.X1, by = b.Y2 - b.Y1;
            double denominator = ax * by - ay * bx;
            if (Math.Abs(denominator) < a.Length * b.Length * .01) return false;
            double t = ((b.X1 - a.X1) * by - (b.Y1 - a.Y1) * bx) / denominator;
            x = a.X1 + t * ax;
            y = a.Y1 + t * ay;
            return !double.IsNaN(x) && !double.IsInfinity(x) &&
                !double.IsNaN(y) && !double.IsInfinity(y);
        }

        private static double Distance2(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool TryAddAssemblyOverallDimension(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View section, SelectData data,
            AssemblyOuterRun run, List<AssemblySectionEdge> outerEdges)
        {
            if (outerEdges == null || outerEdges.Count == 0) return false;
            var endpoints = outerEdges.SelectMany(e => new[] {
                new { Vertex = e.Start, X = e.X1, Y = e.Y1 },
                new { Vertex = e.End, X = e.X2, Y = e.Y2 }
            }).Where(p => p.Vertex != null)
              .OrderBy(p => run.Wide ? p.X : p.Y).ToList();
            if (endpoints.Count < 2) return false;
            var first = endpoints.First();
            var last = endpoints.Last();
            // Keep the overall DIM above the smaller endpoint offsets.
            // Spacing is on paper, so it remains legible at different scales.
            double offset = Math.Max(.012, section.ScaleDecimal * .014);
            double x = run.Wide ? (first.X + last.X) / 2 :
                (run.AtMinimum ? run.Normal - offset : run.Normal + offset);
            double y = run.Wide ?
                (run.AtMinimum ? run.Normal - offset : run.Normal + offset) :
                (first.Y + last.Y) / 2;
            double expected = (run.Wide ? last.X - first.X : last.Y - first.Y) /
                section.ScaleDecimal;
            return TryAddAssemblyDimension(drawing, section, data,
                first.Vertex, last.Vertex, run.Wide, x, y, expected);
        }

        private bool TryAddAssemblyGapDimension(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View section, SelectData data,
            Vertex outerVertex, double outerX, double outerY,
            AssemblyFrameCorner corner, AssemblyOuterRun run)
        {
            if (outerVertex == null || corner == null) return false;
            drawing.SetPickMode();
            if (!((DrawingDoc)drawing).ActivateView(section.GetName2())) return false;
            data.View = section;
            Sketch sketch = section.GetSketch() as Sketch;
            if (sketch == null) return false;
            List<SketchPoint> before = GetAssemblySketchPoints(sketch);
            drawing.SetPickMode();
            drawing.ClearSelection2(true);
            if (!SelectAssemblyReference(corner.Base.Edge, false, data) ||
                !SelectAssemblyReference(corner.Wall.Edge, true, data))
            {
                drawing.ClearSelection2(true);
                return false;
            }
            bool sharpCommand = app.RunCommand(72, string.Empty); // native associative virtual sharp
            drawing.SetPickMode();
            sketch = section.GetSketch() as Sketch ?? sketch;
            List<SketchPoint> after = GetAssemblySketchPoints(sketch);
            SketchPoint sharp = after.FirstOrDefault(point =>
                !before.Any(old => ReferenceEquals(old, point) || old.Equals(point)));
            if (sharp == null)
            {
                drawing.ClearSelection2(true);
                Log("ASSEMBLY DIM virtual sharp creation failed at (" +
                    Mm(corner.X) + "," + Mm(corner.Y) + ") command=" + sharpCommand +
                    " beforePoints=" + before.Count + " afterPoints=" + after.Count);
                return false;
            }
            double offset = Math.Max(.006, section.ScaleDecimal * .006);
            // A 1 mm gap is narrower on paper than its text. Put that text
            // beyond the endpoint instead of squeezing it between the arrows.
            bool smallGap = corner.Offset < .010;
            double alongText = run.Wide ? (outerX + corner.X) / 2 :
                (outerY + corner.Y) / 2;
            if (smallGap)
            {
                bool firstEnd = run.Wide ? outerX < (run.FirstX + run.LastX) / 2 :
                    outerY < (run.FirstY + run.LastY) / 2;
                alongText = firstEnd ?
                    Math.Min(run.Wide ? outerX : outerY, run.Wide ? corner.X : corner.Y) - .008 :
                    Math.Max(run.Wide ? outerX : outerY, run.Wide ? corner.X : corner.Y) + .008;
            }
            double x = run.Wide ? alongText :
                (run.AtMinimum ? Math.Min(outerX, corner.X) - offset :
                    Math.Max(outerX, corner.X) + offset);
            double y = run.Wide ?
                (run.AtMinimum ? Math.Min(outerY, corner.Y) - offset :
                    Math.Max(outerY, corner.Y) + offset) :
                alongText;
            double expected = corner.Offset / section.ScaleDecimal;
            bool made = TryAddAssemblyDimension(drawing, section, data,
                outerVertex, sharp, run.Wide, x, y, expected, !smallGap);
            if (!made)
            {
                drawing.ClearSelection2(true);
                if (SelectAssemblyReference(sharp, false, data)) drawing.EditDelete();
            }
            Log("ASSEMBLY GAP DIM offsetMm=" + F(expected * 1000) +
                " outer=" + (run.Wide ? Mm(outerX) : Mm(outerY)) +
                " frameVirtual=" + (run.Wide ? Mm(corner.X) : Mm(corner.Y)) +
                " created=" + made);
            return made;
        }

        private static bool TryAddAssemblyDimension(ModelDoc2 drawing,
            SolidWorks.Interop.sldworks.View section, SelectData data,
            object first, object second, bool horizontal,
            double textX, double textY, double expectedModelMetres,
            bool centerText = true)
        {
            drawing.SetPickMode();
            drawing.ClearSelection2(true);
            // Creating a virtual sharp can change the active drawing view.
            // Restore the section context for both the model vertex and point.
            if (!((DrawingDoc)drawing).ActivateView(section.GetName2()))
            {
                Log("ASSEMBLY DIM failed: section activation");
                return false;
            }
            data.View = section;
            bool firstSelected = SelectAssemblyReference(first, false, data);
            bool secondSelected = firstSelected && SelectAssemblyReference(second, true, data);
            if (!firstSelected || !secondSelected)
            {
                Log("ASSEMBLY DIM selection failed first=" + firstSelected +
                    " second=" + secondSelected + " secondIsSketchPoint=" + (second is SketchPoint));
                drawing.ClearSelection2(true);
                return false;
            }
            bool displayWhenAdded = drawing.GetDisplayWhenAdded();
            DisplayDimension display;
            try
            {
                drawing.SetDisplayWhenAdded(true);
                display = horizontal ?
                    drawing.AddHorizontalDimension2(textX, textY, 0) as DisplayDimension :
                    drawing.AddVerticalDimension2(textX, textY, 0) as DisplayDimension;
            }
            finally { drawing.SetDisplayWhenAdded(displayWhenAdded); }
            drawing.ClearSelection2(true);
            if (display == null)
            {
                Log("ASSEMBLY DIM failed: native dimension returned null expectedMm=" +
                    F(expectedModelMetres * 1000));
                return false;
            }
            Dimension dimension = display.GetDimension2(0) as Dimension;
            double actual = dimension == null ? double.NaN :
                dimension.GetSystemValue2("");
            double tolerance = Math.Max(.00005, expectedModelMetres * .002);
            bool correct = !double.IsNaN(actual) &&
                Math.Abs(Math.Abs(actual) - expectedModelMetres) <= tolerance;
            if (!correct)
            {
                Annotation annotation = display.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Select3(false, null))
                    drawing.EditDelete();
                Log("ASSEMBLY DIM rejected expectedMm=" + F(expectedModelMetres * 1000) +
                    " actualMm=" + F(actual * 1000));
            }
            else
            {
                display.ShowDimensionValue = true;
                display.CenterText = centerText;
                Annotation annotation = display.GetAnnotation() as Annotation;
                if (annotation != null) annotation.SetPosition2(textX, textY, 0);
            }
            return correct;
        }

        private static bool SelectAssemblyReference(object reference,
            bool append, SelectData data)
        {
            if (reference == null) return false;
            try
            {
                // Call the declared COM interface: in-process SketchPoint RCWs
                // do not reliably expose Select4 to the C# dynamic binder.
                SketchPoint point = reference as SketchPoint;
                if (point != null) return point.Select4(append, data);
                Entity entity = reference as Entity;
                return entity != null && entity.Select4(append, data);
            }
            catch (Exception ex)
            {
                Log("ASSEMBLY DIM reference selection exception: " + ex.Message);
                return false;
            }
        }

        private static List<SketchPoint> GetAssemblySketchPoints(Sketch sketch)
        {
            Array points = sketch.GetSketchPoints2() as Array;
            return points == null ? new List<SketchPoint>() :
                points.OfType<SketchPoint>().ToList();
        }
    }
}
