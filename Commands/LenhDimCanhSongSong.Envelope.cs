using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Profile = ADDIN.Commands.SectionProfilePlanner;

namespace ADDIN.Commands
{
    public partial class LenhDimCanhSongSong
    {
        private List<DisplayDimension> protectedSectionDimensions = new List<DisplayDimension>();
        private sealed class PlannedDisplay
        {
            public DisplayDimension Display;
            public double Expected;
            public int Type;
            public string Label;
            public readonly List<SketchPoint> Points = new List<SketchPoint>();
            public BrokenArcRelocation BrokenArc;
        }

        private sealed class BrokenArcRelocation
        {
            public string Kind;
            public double CenterX;
            public double CenterY;
            public double StartX;
            public double StartY;
            public double EndX;
            public double EndY;
            public double RadiusMm;
            public double ArcLengthMm;
            public double PreBreakX;
            public double PreBreakY;
        }

        private sealed class BrokenDisplayEdgeRecord
        {
            public Edge Edge;
            public int PrimitiveType;
            public readonly List<Profile.Point> Points = new List<Profile.Point>();
        }

        public void Run()
        {
            const string build = "20260919-broken-view-native-transaction-v18.5";
            Debug.WriteLine("[DIM MAT CAT] build=" + build);
            ModelDoc2 model = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            { Msg("Mo Drawing va chon mot canh hoac cung tren mat can DIM.", swMessageBoxIcon_e.swMbWarning); return; }

            SelectionMgr selection = model.SelectionManager as SelectionMgr;
            Edge clicked = null;
            SolidWorks.Interop.sldworks.View view = null;
            int selectedEdges = 0;
            for (int i = 1; selection != null && i <= selection.GetSelectedObjectCount2(-1); i++)
            {
                Edge candidate = selection.GetSelectedObject6(i, -1) as Edge;
                if (candidate == null) continue;
                clicked = candidate;
                view = selection.GetSelectedObjectsDrawingView2(i, -1) as SolidWorks.Interop.sldworks.View;
                selectedEdges++;
            }
            if (selectedEdges != 1 || view == null)
            { Msg("Click dung mot canh hoac cung cua mat trong/ngoai can DIM. Khong chon mep day ton.", swMessageBoxIcon_e.swMbInformation); return; }

            var errors = new List<string>();
            var createdPoints = new List<SketchPoint>();
            var created = new List<PlannedDisplay>();
            var references = new Dictionary<Profile.Reference, object>();
            bool preferenceChanged = false, previousInput = false;
            int deleted = 0;
            DrawingDoc drawing = (DrawingDoc)model;
            BrokenViewState brokenState = null;
            DrawingViewIdentity viewIdentity = null;
            bool brokenViewWasSuspended = false;
            bool brokenViewWasRestored = false;
            string previousViewName = null, previousSheetName = null;
            bool restoreView = false;
            var oldDimensions = new List<DisplayDimension>();
            var oldConstruction = new List<SketchSegment>();
            var newConstruction = new List<SketchSegment>();
            bool curvedReplacement = false, replacingOld = false, curvedReplacementCommitted = false;
            try
            {
                if (!drawing.GetEditSheet())
                    throw new InvalidOperationException("Thoat Edit Sheet Format truoc khi DIM mat cat.");
                var previousView = drawing.ActiveDrawingView as SolidWorks.Interop.sldworks.View;
                var previousSheet = drawing.GetCurrentSheet() as Sheet;
                previousViewName = previousView == null ? null : previousView.Name;
                previousSheetName = previousSheet == null ? null : previousSheet.GetName();
                ValidateDrawingSketchContext(model, drawing);

                // Capture only value data from the selected edge BEFORE changing Broken View.
                // UnBreakView can invalidate every COM object that belongs to the drawing view,
                // including View/Edge/SketchSegment/SelectData.  The numeric seed geometry remains
                // safe and is used to find the same edge again after the view is rebuilt.
                viewIdentity = CaptureDrawingViewIdentity(drawing, view);
                viewScale = view.ScaleDecimal;
                if (viewScale <= 0) throw new InvalidOperationException("Ti le view khong hop le.");
                MathUtility math = swApp.GetMathUtility() as MathUtility;
                MathTransform transform = view.ModelToViewTransform as MathTransform;
                if (math == null || transform == null) throw new InvalidOperationException("Khong doc duoc he toa do view.");
                EdgeInfo seedLine = MakeEdgeInfo(clicked, math, transform);
                ArcInfo seedArc = seedLine == null ? MakeArcInfo(clicked, math, transform) : null;
                if (seedLine == null && seedArc == null)
                    throw new InvalidOperationException("Khong luu duoc hinh hoc canh/cung click truoc khi xu ly Broken View.");

                // Native SOLIDWORKS behavior is stable when dimensions are created first and the
                // view is broken afterwards.  For an already-broken view we reproduce that flow,
                // but NEVER reuse COM references across UnBreakView.  Reacquire the view and all
                // geometry from the document after the rebuild.
                brokenState = CaptureBrokenViewState(view);
                if (brokenState != null && brokenState.WasBroken)
                {
                    view = SuspendBrokenViewAndReacquire(model, drawing, viewIdentity, view, brokenState);
                    brokenViewWasSuspended = true;
                    selection = model.SelectionManager as SelectionMgr;
                    viewScale = view.ScaleDecimal;
                    if (viewScale <= 0) throw new InvalidOperationException("Ti le view sau UnBreak khong hop le.");
                    transform = view.ModelToViewTransform as MathTransform;
                    if (transform == null) throw new InvalidOperationException("Khong doc duoc he toa do view moi sau UnBreak.");
                    Debug.WriteLine("[DIM MAT CAT BREAK TX] fresh view reacquired after UnBreak: " + view.Name);
                }

                Profile.Plan plan = ReadSectionPlan(view, math, transform, seedLine, seedArc);
                restoreView = true;
                ActivateSectionViewSketch(model, drawing, view);
                selection = model.SelectionManager as SelectionMgr;
                SelectData data = selection == null ? null : selection.CreateSelectData() as SelectData;
                if (data == null) throw new InvalidOperationException("Khong tao duoc SelectData.");
                data.View = view;

                // Preflight above must pass before removing the old view dimensions.
                curvedReplacement = plan.Curved != null;
                if (curvedReplacement)
                {
                    oldDimensions = SnapshotSectionDimensions(view);
                    protectedSectionDimensions = oldDimensions;
                    oldConstruction = SnapshotSectionSegments(view.GetSketch() as Sketch)
                        .Where(s => s.Layer == CurvedSketchName(view) && s.ConstructionGeometry).ToList();
                    Debug.WriteLine("[DIM MAT CAT CONVERT] preserve old dimensions=" + oldDimensions.Count);
                }
                else
                {
                    deleted = DeleteDisplayDimensionsInView(model, view);
                    if (CountViewDimensions(view) != 0)
                        throw new InvalidOperationException("Chua xoa het DIM cu trong view; dung de tranh tao trung.");
                }
                // Deletion can invalidate COM edge references. Recollect and rebuild
                // the plan from the saved seed rather than relying on selection.
                plan = ReadSectionPlan(view, math, transform, seedLine, seedArc);
                DisableEdgeSelectionFilter();
                EnableNativeVirtualSharpDisplay(model);
                previousInput = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                preferenceChanged = true;
                if (!curvedReplacement) RemoveCurvedSectionSketch(model, view);
                double cx = plan.Boundary.Average(c => (c.A.X + c.B.X) * .5);
                double cy = plan.Boundary.Average(c => (c.A.Y + c.B.Y) * .5);
                Debug.WriteLine("[DIM MAT CAT PLAN] " + plan.Shape + ", seed="
                    + (seedLine != null ? EdgeSummary(seedLine) : "arc R=" + seedArc.RadiusMm.ToString("0.###"))
                    + ", t=" + (plan.Thickness * 1000 / viewScale).ToString("0.###")
                    + ", spans=" + plan.Lengths.Count + ", separateR=" + plan.DimensionArcs.Count);

                if (plan.Curved != null)
                    CreateCurvedSectionDimensions(model, view, data, plan, created, errors, cx, cy, newConstruction,
                        brokenState != null && brokenState.WasBroken);
                else
                {
                    for (int i = 0; i < plan.Lengths.Count; i++)
                    {
                        Profile.Length length = plan.Lengths[i];
                        string label = "L" + (i + 1) + " " + length.Rule;
                        double expected = length.Value / viewScale;
                        Debug.WriteLine("[DIM MAT CAT PLAN] " + label + ", mm=" + (expected * 1000).ToString("0.###")
                            + ", start=" + SectionPointText(length.Start.Position) + ", end=" + SectionPointText(length.End.Position));
                        PlannedDisplay item = TryCreateSectionDimension(model, created, errors, label, expected,
                            (int)swDimensionType_e.swLinearDimension,
                            () => CreateSectionLength(model, view, data, length, references, createdPoints, cx, cy));
                        if (item != null)
                            foreach (Profile.Reference reference in new[] { length.Start, length.End })
                            {
                                object value;
                                if (references.TryGetValue(reference, out value) && value is SketchPoint) item.Points.Add((SketchPoint)value);
                            }
                    }
                    foreach (Profile.Bend bend in plan.Bends)
                    {
                        string label = "Angle " + bend.AngleDegrees.ToString("0.###");
                        double expected = bend.AngleDegrees * Math.PI / 180;
                        TryCreateSectionDimension(model, created, errors, label, expected, (int)swDimensionType_e.swAngularDimension, () =>
                        {
                            model.ClearSelection2(true);
                            if (!SelectEdge(((EdgeInfo)bend.First.Source).Edge, false, data)
                                || !SelectEdge(((EdgeInfo)bend.Second.Source).Edge, true, data)) return null;
                            Profile.Point text = bend.Position + bend.Bisector * MmToM(DimOffsetMm * 1.8);
                            DisplayDimension display = model.AddDimension2(text.X, text.Y, 0) as DisplayDimension;
                            if (display != null && display.GetType() == (int)swDimensionType_e.swAngularDimension
                                && !SectionDimensionMatches(display, expected, (int)swDimensionType_e.swAngularDimension))
                            {
                                Dimension dimension = display.GetDimension2(0) as Dimension;
                                if (dimension != null && Math.Abs(dimension.GetSystemValue2("") + expected - Math.PI) < .001)
                                    display.SupplementaryAngle();
                            }
                            return display;
                        });
                    }
                    foreach (Profile.Curve curve in plan.DimensionArcs)
                    {
                        ArcInfo arc = (ArcInfo)curve.Source;
                        double radius = (curve.A - curve.Center).Length / viewScale;
                        double arcLength = radius * Math.Abs(curve.SweepAngleRadians);
                        Debug.WriteLine("[DIM MAT CAT PLAN] R=" + (radius * 1000).ToString("0.###")
                            + ", arc length=" + (arcLength * 1000).ToString("0.###") + ", picked skin");
                        TryCreateSectionDimension(model, created, errors, "R " + (radius * 1000).ToString("0.###"),
                            radius, (int)swDimensionType_e.swRadialDimension, () =>
                            {
                                double x, y;
                                GetArcDimensionPosition(arc, cx, cy, -12, out x, out y);
                                model.ClearSelection2(true);
                                if (!SelectEdge(arc.Edge, false, data)) return null;
                                DisplayDimension result = model.AddRadialDimension2(x, y, 0) as DisplayDimension;
                                if (result != null) ApplyCompactRadiusStyle(model, result);
                                return result;
                            });
                        TryCreateSectionDimension(model, created, errors, "Arc length " + (arcLength * 1000).ToString("0.###"),
                            arcLength, (int)swDimensionType_e.swArcLengthDimension, () =>
                            {
                                double x, y;
                                GetArcDimensionPosition(arc, cx, cy, 12, out x, out y);
                                DisplayDimension result = TryAddArcLengthByReferences(model, data, arc, x, y, true)
                                    ?? TryAddArcLengthByReferences(model, data, arc, x, y, false);
                                if (result == null)
                                {
                                    model.ClearSelection2(true);
                                    if (SelectEdge(arc.Edge, false, data)) result = model.Extension.AddPathLengthDim(x, y, 0) as DisplayDimension;
                                }
                                if (result != null && result.GetType() == (int)swDimensionType_e.swArcLengthDimension)
                                    ApplyRadialArcLengthLeader(model, result);
                                return result;
                            });
                    }
                }
                model.ClearSelection2(true);
                model.EditRebuild3();
                foreach (PlannedDisplay item in created.ToArray())
                    if (!SectionDimensionMatches(item.Display, item.Expected, item.Type))
                    {
                        DeleteDisplayDimension(model, item.Display);
                        created.Remove(item);
                        errors.Add(item.Label + ": sai gia tri/mat lien ket sau rebuild, da loai DIM vua tao.");
                    }

                // If this run started from a Broken View, commit the curved replacement while
                // the view is still unbroken.  BreakView can invalidate DisplayDimension and
                // SketchSegment RCWs just like UnBreakView invalidates Edge RCWs, so never keep
                // old construction/dimension COM objects across the restore boundary.
                if (curvedReplacement && brokenState != null && brokenState.WasBroken)
                {
                    if (errors.Count > 0 || created.Count == 0)
                        throw new InvalidOperationException("Bo DIM cong moi chua hop le; giu nguyen DIM cu.");
                    if (created.Any(n => oldDimensions.Any(o => SameSketchObject(o, n.Display))))
                        throw new InvalidOperationException("API tra ve DIM da co; khong thay the DIM cu.");
                    replacingOld = true;
                    foreach (DisplayDimension old in oldDimensions)
                    {
                        DeleteDisplayDimension(model, old);
                        if (SnapshotSectionDimensions(view).Any(d => SameSketchObject(d, old)))
                            throw new InvalidOperationException("Khong xoa duoc mot DIM cu truoc khi restore Broken View.");
                        deleted++;
                    }
                    foreach (SketchSegment old in oldConstruction)
                    {
                        if (!SnapshotSectionSegments(view.GetSketch() as Sketch).Any(s => SameSketchObject(s, old))) continue;
                        model.ClearSelection2(true);
                        if (!old.Select4(false, null)) throw new InvalidOperationException("Khong chon duoc construction cu de don dep.");
                        model.EditDelete();
                    }
                    model.EditRebuild3();
                    if (created.Any(d => !SectionDimensionMatches(d.Display, d.Expected, d.Type)))
                        throw new InvalidOperationException("DIM moi thay doi truoc khi restore Broken View.");
                    curvedReplacementCommitted = true;
                    Debug.WriteLine("[DIM MAT CAT CONVERT] replacement committed before Broken restore; old deleted=" + deleted);
                }

                // Reapply the original Broken View with a freshly reacquired View object.  Do not
                // use any COM object created before this topology change afterwards.  Validation
                // is therefore performed by re-enumerating the dimensions from the fresh view and
                // matching only type/value pairs.
                if (brokenState != null && brokenState.WasBroken)
                {
                    view = RestoreBrokenViewAndReacquire(model, drawing, viewIdentity, brokenState);
                    brokenViewWasRestored = true;

                    // IMPORTANT v18.5: after restoring Broken View, do not move any annotation.
                    // The command intentionally reproduces the native/manual SOLIDWORKS workflow:
                    // remove Break -> create native dimensions -> restore Break -> rebuild.
                    // SOLIDWORKS owns the final Broken View remap.  No GetPolylines7 mapper, proxy,
                    // overlay or Annotation.SetPosition2 post-processing is applied here.
                    Debug.WriteLine("[DIM MAT CAT BREAK TX] native restore complete; no post-break relocation.");

                    string freshReason;
                    if (!FreshDimensionSetMatches(view, created, out freshReason))
                        throw new InvalidOperationException("DIM sau khi restore Broken View khong khop: " + freshReason);
                    Debug.WriteLine("[DIM MAT CAT BREAK TX] post-restore fresh DIM validation OK: expected="
                        + created.Count + ", actualViewDims=" + SnapshotSectionDimensions(view).Count);
                }

                if (curvedReplacement && !curvedReplacementCommitted)
                {
                    if (errors.Count > 0 || created.Count == 0)
                        throw new InvalidOperationException("Bo DIM cong moi chua hop le; giu nguyen DIM cu.");
                    if (created.Any(n => oldDimensions.Any(o => SameSketchObject(o, n.Display))))
                        throw new InvalidOperationException("API tra ve DIM da co; khong thay the DIM cu.");
                    replacingOld = true;
                    // New dimensions have passed geometry/type/rebuild checks.
                    // Delete only the snapshot, never enumerate-and-delete the new set.
                    foreach (DisplayDimension old in oldDimensions)
                    {
                        DeleteDisplayDimension(model, old);
                        if (SnapshotSectionDimensions(view).Any(d => SameSketchObject(d, old)))
                            throw new InvalidOperationException("Khong xoa duoc mot DIM cu; giu bo DIM moi, can kiem tra DIM trung.");
                        deleted++;
                    }
                    foreach (SketchSegment old in oldConstruction)
                    {
                        if (!SnapshotSectionSegments(view.GetSketch() as Sketch).Any(s => SameSketchObject(s, old))) continue;
                        model.ClearSelection2(true);
                        if (!old.Select4(false, null)) throw new InvalidOperationException("Khong chon duoc construction cu de don dep.");
                        model.EditDelete();
                    }
                    model.EditRebuild3();
                    if (created.Any(d => !SectionDimensionMatches(d.Display, d.Expected, d.Type)))
                        throw new InvalidOperationException("DIM moi thay doi sau khi don construction cu; dung va kiem tra/Undo.");
                    curvedReplacementCommitted = true;
                    Debug.WriteLine("[DIM MAT CAT CONVERT] replacement complete; old deleted=" + deleted);
                }
                string summary = "DIM MAT CAT - " + plan.Shape + "\nMat tham chieu: theo canh/cung click"
                    + "\nDa xoa DIM cu trong view: " + deleted + "; tao dung: " + created.Count + "; chua tao duoc: " + errors.Count
                    + (brokenState != null && brokenState.WasBroken ? "\nBroken View: bo thu ngan -> DIM native -> restore thu ngan (khong relocate)." : "")
                    + "\nDrawing chua duoc luu.";
                if (errors.Count > 0) summary += "\n\n" + string.Join("\n", errors);
                Debug.WriteLine("[DIM MAT CAT] " + summary);
                Msg(summary, errors.Count == 0 ? swMessageBoxIcon_e.swMbInformation : swMessageBoxIcon_e.swMbWarning);
            }
            catch (Exception ex)
            {
                if (curvedReplacement && !replacingOld)
                {
                    foreach (PlannedDisplay item in created.ToArray())
                    {
                        if (!oldDimensions.Any(o => SameSketchObject(o, item.Display))) DeleteDisplayDimension(model, item.Display);
                        created.Remove(item);
                    }
                    DeleteOwnedSectionSegments(model, newConstruction);
                    Debug.WriteLine("[DIM MAT CAT CONVERT] failed before replacement; old dimensions preserved");
                }
                Debug.WriteLine("[DIM MAT CAT] " + build + " failed: " + ex);
                Msg("Chua hoan tat DIM mat cat: " + ex.Message + "\nDIM cu da xoa: " + deleted
                    + "; DIM moi da tao: " + created.Count + ". Drawing chua luu.", swMessageBoxIcon_e.swMbWarning);
            }
            finally
            {
                // Never leave the user's drawing unbroken. This is intentionally independent
                // from DIM success/failure. If the main transaction failed before normal restore,
                // try once more here using the original native break definition.
                if (brokenState != null && brokenState.WasBroken && !brokenViewWasRestored)
                {
                    try
                    {
                        view = RestoreBrokenViewAndReacquire(model, drawing, viewIdentity, brokenState);
                        brokenViewWasRestored = true;
                        Debug.WriteLine("[DIM MAT CAT BREAK TX] finally restore succeeded.");
                    }
                    catch (Exception restoreEx)
                    {
                        Debug.WriteLine("[DIM MAT CAT BREAK TX] CRITICAL restore failed: " + restoreEx);
                        Msg("Canh bao: khong restore duoc Broken View sau khi DIM. "
                            + "Hay Undo/kiem tra view truoc khi tiep tuc.\n" + restoreEx.Message,
                            swMessageBoxIcon_e.swMbStop);
                    }
                }

                protectedSectionDimensions = new List<DisplayDimension>();
                HashSet<SketchPoint> used = new HashSet<SketchPoint>(created.SelectMany(d => d.Points));
                foreach (SketchPoint point in createdPoints)
                    if (!used.Contains(point))
                        try { model.ClearSelection2(true); if (SelectReference(point, false, null)) model.EditDelete(); } catch { }
                if (preferenceChanged)
                    try { swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, previousInput); } catch { }
                try { model.SetPickMode(); model.ClearSelection2(true); } catch { }
                if (restoreView)
                    try
                    {
                        bool restored = previousViewName != null ? drawing.ActivateView(previousViewName)
                            : previousSheetName != null && drawing.ActivateSheet(previousSheetName);
                        Debug.WriteLine("[DIM MAT CAT SKETCH] restored previous view/sheet=" + restored);
                    }
                    catch (Exception ex) { Debug.WriteLine("[DIM MAT CAT SKETCH] restore failed: " + ex.Message); }
            }
        }

        private sealed class BrokenViewState
        {
            public bool WasBroken;
            public double Gap;
            public int ExpectedBreakCount;
            public readonly List<BreakLineState> Lines = new List<BreakLineState>();
        }

        private sealed class BreakLineState
        {
            public int Orientation;
            public double Position1;
            public double Position2;
            public int Style;
            public int ShapeIntensity;
            public bool BreakSketchBlocks;
        }

        private sealed class DrawingViewIdentity
        {
            public string SheetName;
            public string ViewName;
        }

        private static bool IsBrokenDrawingView(SolidWorks.Interop.sldworks.View view)
        {
            try
            {
                SolidWorks.Interop.sldworks.IView iview = view as SolidWorks.Interop.sldworks.IView;
                return iview != null && iview.IsBroken();
            }
            catch
            {
                return false;
            }
        }

        private sealed class BrokenArcTarget
        {
            public PlannedDisplay Item;
            public double X;
            public double Y;
            public double DeltaX;
            public double DeltaY;
        }

        private static BrokenArcRelocation CreateBrokenArcRelocation(
            string kind, ArcInfo arc, double preBreakX, double preBreakY)
        {
            if (arc == null) return null;
            return new BrokenArcRelocation
            {
                Kind = kind,
                CenterX = arc.CenterX,
                CenterY = arc.CenterY,
                StartX = arc.StartX,
                StartY = arc.StartY,
                EndX = arc.EndX,
                EndY = arc.EndY,
                RadiusMm = arc.RadiusMm,
                ArcLengthMm = arc.ArcLengthMm,
                PreBreakX = preBreakX,
                PreBreakY = preBreakY
            };
        }

        private List<BrokenDisplayEdgeRecord> ReadBrokenDisplayEdges(
            SolidWorks.Interop.sldworks.View view)
        {
            var result = new List<BrokenDisplayEdgeRecord>();
            if (!IsBrokenDrawingView(view)) return result;

            try
            {
                SolidWorks.Interop.sldworks.IView iview = view as SolidWorks.Interop.sldworks.IView;
                if (iview == null) return result;

                object polylinePayload;
                object edgePayload = iview.GetPolylines7(0, out polylinePayload);
                Array values = polylinePayload as Array;
                Array edges = edgePayload as Array;
                double[] origin = view.Position as double[];
                double scale = view.ScaleDecimal;

                if (values == null || edges == null || values.Rank != 1 || edges.Rank != 1 ||
                    origin == null || origin.Length < 2 || scale <= 0)
                {
                    Debug.WriteLine("[DIM MAT CAT POST BREAK] GetPolylines7 payload invalid.");
                    return result;
                }

                double[] raw = new double[values.Length];
                int valueLower = values.GetLowerBound(0);
                for (int i = 0; i < raw.Length; i++)
                    raw[i] = Convert.ToDouble(values.GetValue(valueLower + i),
                        System.Globalization.CultureInfo.InvariantCulture);

                int cursor = 0;
                int edgeIndex = 0;
                int edgeLower = edges.GetLowerBound(0);
                while (cursor < raw.Length && edgeIndex < edges.Length)
                {
                    int primitive = (int)Math.Round(raw[cursor]);
                    int headerLength;
                    int pointCountOffset;
                    if (primitive == 0)
                    {
                        headerLength = 9;
                        pointCountOffset = 8;
                    }
                    else if (primitive == 1)
                    {
                        headerLength = 21;
                        pointCountOffset = 20;
                    }
                    else
                    {
                        Debug.WriteLine("[DIM MAT CAT POST BREAK] unsupported polyline primitive="
                            + primitive + " at cursor=" + cursor);
                        result.Clear();
                        return result;
                    }

                    if (cursor + pointCountOffset >= raw.Length)
                    {
                        result.Clear();
                        return result;
                    }

                    int pointCount = (int)Math.Round(raw[cursor + pointCountOffset]);
                    int dataStart = cursor + headerLength;
                    int next = dataStart + pointCount * 3;
                    if (pointCount < 2 || next > raw.Length)
                    {
                        result.Clear();
                        return result;
                    }

                    var record = new BrokenDisplayEdgeRecord
                    {
                        Edge = edges.GetValue(edgeLower + edgeIndex) as Edge,
                        PrimitiveType = primitive
                    };
                    for (int p = 0; p < pointCount; p++)
                    {
                        double xLocal = raw[dataStart + p * 3];
                        double yLocal = raw[dataStart + p * 3 + 1];
                        record.Points.Add(new Profile.Point(
                            origin[0] + scale * xLocal,
                            origin[1] + scale * yLocal));
                    }
                    if (record.Edge != null) result.Add(record);

                    cursor = next;
                    edgeIndex++;
                }

                if (cursor != raw.Length || edgeIndex != edges.Length || result.Count != edges.Length)
                {
                    Debug.WriteLine("[DIM MAT CAT POST BREAK] GetPolylines7 parser mismatch; cursor="
                        + cursor + "/" + raw.Length + ", edges=" + edgeIndex + "/" + edges.Length
                        + ", records=" + result.Count);
                    result.Clear();
                    return result;
                }

                Debug.WriteLine("[DIM MAT CAT POST BREAK] native display records=" + result.Count
                    + ", rawDoubles=" + raw.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT POST BREAK] GetPolylines7 failed: "
                    + ex.GetBaseException().Message);
                result.Clear();
            }
            return result;
        }

        private static double BrokenPointDistance(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool BrokenArcSignatureMatches(ArcInfo current, BrokenArcRelocation saved)
        {
            if (current == null || saved == null) return false;

            const double sheetTol = 0.00010; // 0.10 mm on sheet
            const double radiusTolMm = 0.10;
            const double lengthTolMm = 0.25;

            if (BrokenPointDistance(current.CenterX, current.CenterY, saved.CenterX, saved.CenterY) > sheetTol)
                return false;
            if (Math.Abs(current.RadiusMm - saved.RadiusMm) > radiusTolMm)
                return false;
            if (Math.Abs(current.ArcLengthMm - saved.ArcLengthMm) > lengthTolMm)
                return false;

            double forward = BrokenPointDistance(current.StartX, current.StartY, saved.StartX, saved.StartY)
                           + BrokenPointDistance(current.EndX, current.EndY, saved.EndX, saved.EndY);
            double reverse = BrokenPointDistance(current.StartX, current.StartY, saved.EndX, saved.EndY)
                           + BrokenPointDistance(current.EndX, current.EndY, saved.StartX, saved.StartY);
            return Math.Min(forward, reverse) <= sheetTol * 2.0;
        }

        private bool TryGetBrokenArcDisplayDelta(
            SolidWorks.Interop.sldworks.View view,
            MathUtility math,
            MathTransform transform,
            BrokenArcRelocation saved,
            List<BrokenDisplayEdgeRecord> records,
            out Profile.Point delta)
        {
            delta = new Profile.Point(0, 0);
            if (view == null || math == null || transform == null || saved == null || records == null)
                return false;

            BrokenDisplayEdgeRecord bestRecord = null;
            double bestError = double.MaxValue;
            foreach (BrokenDisplayEdgeRecord record in records)
            {
                if (record == null || record.Edge == null || record.Points.Count < 2 || record.PrimitiveType != 1)
                    continue;

                ArcInfo current = null;
                try { current = MakeArcInfo(record.Edge, math, transform); }
                catch { }
                if (!BrokenArcSignatureMatches(current, saved))
                    continue;

                Profile.Point displayA = record.Points[0];
                Profile.Point displayB = record.Points[record.Points.Count - 1];

                Profile.Point forwardA = new Profile.Point(displayA.X - saved.StartX, displayA.Y - saved.StartY);
                Profile.Point forwardB = new Profile.Point(displayB.X - saved.EndX, displayB.Y - saved.EndY);
                Profile.Point reverseA = new Profile.Point(displayA.X - saved.EndX, displayA.Y - saved.EndY);
                Profile.Point reverseB = new Profile.Point(displayB.X - saved.StartX, displayB.Y - saved.StartY);

                double forwardError = BrokenPointDistance(forwardA.X, forwardA.Y, forwardB.X, forwardB.Y);
                double reverseError = BrokenPointDistance(reverseA.X, reverseA.Y, reverseB.X, reverseB.Y);
                Profile.Point first = forwardError <= reverseError ? forwardA : reverseA;
                Profile.Point second = forwardError <= reverseError ? forwardB : reverseB;
                double error = Math.Min(forwardError, reverseError);

                if (error < bestError)
                {
                    bestError = error;
                    bestRecord = record;
                    delta = new Profile.Point((first.X + second.X) * 0.5,
                                              (first.Y + second.Y) * 0.5);
                }
            }

            // The same model edge must be a rigid translation in Broken View.  Reject anything
            // that looks cropped/split instead of guessing a placement.
            if (bestRecord == null || bestError > MmToM(0.05))
            {
                delta = new Profile.Point(0, 0);
                return false;
            }
            return true;
        }

        private static bool TryGetAnnotationPosition(DisplayDimension display, out double x, out double y)
        {
            x = 0; y = 0;
            try
            {
                Annotation annotation = display == null ? null : display.GetAnnotation() as Annotation;
                double[] position = annotation == null ? null : annotation.GetPosition() as double[];
                if (position == null || position.Length < 2) return false;
                x = position[0];
                y = position[1];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SearchBrokenArcAssignment(
            int index,
            List<BrokenArcTarget> targets,
            List<DisplayDimension> candidates,
            bool[] used,
            int[] current,
            ref double bestCost,
            int[] best)
        {
            if (index >= targets.Count)
            {
                double cost = 0.0;
                for (int i = 0; i < targets.Count; i++)
                {
                    double x, y;
                    if (!TryGetAnnotationPosition(candidates[current[i]], out x, out y))
                        cost += 1000.0;
                    else
                        cost += BrokenPointDistance(x, y, targets[i].X, targets[i].Y);
                }
                if (cost < bestCost)
                {
                    bestCost = cost;
                    Array.Copy(current, best, current.Length);
                }
                return;
            }

            for (int c = 0; c < candidates.Count; c++)
            {
                if (used[c]) continue;
                used[c] = true;
                current[index] = c;
                SearchBrokenArcAssignment(index + 1, targets, candidates, used, current, ref bestCost, best);
                used[c] = false;
            }
        }

        private void ApplyBrokenArcRelocationPass(
            SolidWorks.Interop.sldworks.View view,
            List<BrokenArcTarget> targets,
            int pass)
        {
            List<DisplayDimension> actual = SnapshotSectionDimensions(view);
            var pending = new List<BrokenArcTarget>(targets);

            while (pending.Count > 0)
            {
                BrokenArcTarget seed = pending[0];
                var group = pending.Where(t => t.Item.Type == seed.Item.Type
                    && Math.Abs(t.Item.Expected - seed.Item.Expected) <= .00001).ToList();
                foreach (BrokenArcTarget t in group) pending.Remove(t);

                var candidates = actual.Where(d => SectionDimensionMatches(d,
                    seed.Item.Expected, seed.Item.Type)).ToList();
                if (candidates.Count < group.Count)
                    throw new InvalidOperationException("Khong tim du fresh DIM cong de dat lai vi tri sau Broken View: "
                        + seed.Item.Label);

                // Normally counts are equal.  If the drawing contains another manual dimension
                // with the same value/type, keep only the nearest candidates around our targets.
                if (candidates.Count > group.Count)
                {
                    candidates = candidates.OrderBy(d =>
                    {
                        double x, y;
                        if (!TryGetAnnotationPosition(d, out x, out y)) return double.MaxValue;
                        return group.Min(t => BrokenPointDistance(x, y, t.X, t.Y));
                    }).Take(group.Count).ToList();
                }

                int n = group.Count;
                var used = new bool[n];
                var current = new int[n];
                var best = Enumerable.Repeat(-1, n).ToArray();
                double bestCost = double.MaxValue;
                SearchBrokenArcAssignment(0, group, candidates, used, current, ref bestCost, best);
                if (best.Any(i => i < 0))
                    throw new InvalidOperationException("Khong ghep duoc fresh DIM cong sau Broken View: " + seed.Item.Label);

                for (int i = 0; i < n; i++)
                {
                    DisplayDimension display = candidates[best[i]];
                    Annotation annotation = display.GetAnnotation() as Annotation;
                    if (annotation == null)
                        throw new InvalidOperationException("Fresh DIM cong khong co Annotation: " + group[i].Item.Label);
                    annotation.SetPosition2(group[i].X, group[i].Y, 0);
                    Debug.WriteLine("[DIM MAT CAT POST BREAK] pass=" + pass
                        + ", kind=" + group[i].Item.BrokenArc.Kind
                        + ", label=" + group[i].Item.Label
                        + ", delta=(" + (group[i].DeltaX * 1000).ToString("0.###")
                        + "," + (group[i].DeltaY * 1000).ToString("0.###") + ")mm"
                        + ", target=(" + (group[i].X * 1000).ToString("0.###")
                        + "," + (group[i].Y * 1000).ToString("0.###") + ")mm");
                    actual.Remove(display);
                }
            }
        }

        private void RelocateBrokenArcAnnotations(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            List<PlannedDisplay> planned)
        {
            var items = planned.Where(p => p.BrokenArc != null).ToList();
            if (items.Count == 0) return;
            if (!IsBrokenDrawingView(view))
                throw new InvalidOperationException("Can dat lai DIM cong nhung fresh view khong o trang thai Broken.");

            List<BrokenDisplayEdgeRecord> records = ReadBrokenDisplayEdges(view);
            if (records.Count == 0)
                throw new InvalidOperationException("Khong doc duoc display geometry sau Broken View.");

            MathUtility math = swApp.GetMathUtility() as MathUtility;
            MathTransform transform = view.ModelToViewTransform as MathTransform;
            if (math == null || transform == null)
                throw new InvalidOperationException("Khong doc duoc he toa do fresh Broken View de dat lai DIM cong.");

            var targets = new List<BrokenArcTarget>();
            foreach (PlannedDisplay item in items)
            {
                Profile.Point delta;
                if (!TryGetBrokenArcDisplayDelta(view, math, transform, item.BrokenArc, records, out delta))
                    throw new InvalidOperationException("Khong tim duoc rigid display delta cho " + item.Label);

                targets.Add(new BrokenArcTarget
                {
                    Item = item,
                    DeltaX = delta.X,
                    DeltaY = delta.Y,
                    X = item.BrokenArc.PreBreakX + delta.X,
                    Y = item.BrokenArc.PreBreakY + delta.Y
                });
            }

            // First position pass, one rebuild, then a second lock pass.  The second SetPosition2
            // happens after SOLIDWORKS has completed its native Broken View relayout.
            ApplyBrokenArcRelocationPass(view, targets, 1);
            model.EditRebuild3();
            view = ReacquireDrawingView((DrawingDoc)model, CaptureDrawingViewIdentity((DrawingDoc)model, view));
            ApplyBrokenArcRelocationPass(view, targets, 2);
            model.GraphicsRedraw2();
        }

        private static int SafeBreakLineCount(SolidWorks.Interop.sldworks.View view)
        {
            try
            {
                SolidWorks.Interop.sldworks.IView iview = view as SolidWorks.Interop.sldworks.IView;
                if (iview == null) return 0;
                int size;
                return iview.GetBreakLineCount2(out size);
            }
            catch
            {
                return 0;
            }
        }

        private static BrokenViewState CaptureBrokenViewState(SolidWorks.Interop.sldworks.View view)
        {
            var state = new BrokenViewState();
            state.WasBroken = IsBrokenDrawingView(view);
            if (!state.WasBroken)
            {
                Debug.WriteLine("[DIM MAT CAT BREAK TX] view is not broken; old DIM path unchanged.");
                return state;
            }

            SolidWorks.Interop.sldworks.IView iview = view as SolidWorks.Interop.sldworks.IView;
            if (iview == null)
                throw new InvalidOperationException("Khong truy cap duoc IView de luu Broken View.");

            try { state.Gap = iview.BreakLineGap; }
            catch (Exception ex) { throw new InvalidOperationException("Khong doc duoc BreakLineGap.", ex); }

            int infoSize;
            state.ExpectedBreakCount = iview.GetBreakLineCount2(out infoSize);
            Array lines = iview.GetBreakLines() as Array;
            if (state.ExpectedBreakCount <= 0 || lines == null || lines.Length != state.ExpectedBreakCount)
                throw new InvalidOperationException("Broken View dang bat nhung khong doc duoc day du break line.");

            foreach (object raw in lines)
            {
                SolidWorks.Interop.sldworks.IBreakLine line = raw as SolidWorks.Interop.sldworks.IBreakLine;
                if (line == null)
                    throw new InvalidOperationException("Khong cast duoc mot break line sang IBreakLine.");

                var item = new BreakLineState();
                try
                {
                    item.Orientation = Convert.ToInt32(line.Orientation);
                    item.Position1 = line.GetPosition(0);
                    item.Position2 = line.GetPosition(1);
                    item.Style = Convert.ToInt32(line.Style);
                    item.ShapeIntensity = Convert.ToInt32(line.ShapeIntensity);
                    item.BreakSketchBlocks = line.BreakSketchBlocks;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Khong doc duoc day du thuoc tinh break line.", ex);
                }
                state.Lines.Add(item);
            }

            if (state.Lines.Count != state.ExpectedBreakCount)
                throw new InvalidOperationException("So break line capture khong khop voi view.");

            Debug.WriteLine("[DIM MAT CAT BREAK TX] captured: count=" + state.ExpectedBreakCount
                + ", gapMm=" + (state.Gap * 1000.0).ToString("0.###"));
            for (int i = 0; i < state.Lines.Count; i++)
            {
                BreakLineState b = state.Lines[i];
                Debug.WriteLine("[DIM MAT CAT BREAK TX] line[" + i + "] orientation=" + b.Orientation
                    + ", p1=" + b.Position1.ToString("G17")
                    + ", p2=" + b.Position2.ToString("G17")
                    + ", style=" + b.Style
                    + ", intensity=" + b.ShapeIntensity
                    + ", breakSketchBlocks=" + b.BreakSketchBlocks);
            }
            return state;
        }

        private static DrawingViewIdentity CaptureDrawingViewIdentity(DrawingDoc drawing,
            SolidWorks.Interop.sldworks.View view)
        {
            Sheet sheet = drawing == null ? null : drawing.GetCurrentSheet() as Sheet;
            string sheetName = sheet == null ? null : sheet.GetName();
            if (view == null || string.IsNullOrEmpty(view.Name) || string.IsNullOrEmpty(sheetName))
                throw new InvalidOperationException("Khong luu duoc sheet/view identity truoc khi thay doi Broken View.");
            return new DrawingViewIdentity { SheetName = sheetName, ViewName = view.Name };
        }

        private static SolidWorks.Interop.sldworks.View ReacquireDrawingView(DrawingDoc drawing,
            DrawingViewIdentity identity)
        {
            if (drawing == null || identity == null)
                throw new InvalidOperationException("Khong co view identity de re-query Drawing View.");
            if (!drawing.ActivateSheet(identity.SheetName))
                throw new InvalidOperationException("Khong activate duoc sheet khi re-query view: " + identity.SheetName);

            for (SolidWorks.Interop.sldworks.View candidate = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                candidate != null; candidate = candidate.GetNextView() as SolidWorks.Interop.sldworks.View)
                if (string.Equals(candidate.Name, identity.ViewName, StringComparison.Ordinal))
                    return candidate;

            throw new InvalidOperationException("Khong tim lai duoc Drawing View sau rebuild: " + identity.ViewName);
        }

        private static SolidWorks.Interop.sldworks.View SuspendBrokenViewAndReacquire(ModelDoc2 model,
            DrawingDoc drawing, DrawingViewIdentity identity, SolidWorks.Interop.sldworks.View view,
            BrokenViewState state)
        {
            if (state == null || !state.WasBroken)
                return ReacquireDrawingView(drawing, identity);

            if (!drawing.ActivateView(identity.ViewName))
                throw new InvalidOperationException("Khong activate duoc view truoc khi UnBreakView.");

            Debug.WriteLine("[DIM MAT CAT BREAK TX] UnBreakView begin. count=" + SafeBreakLineCount(view));
            drawing.UnBreakView();
            model.EditRebuild3();

            // Leave view-sketch context before touching the regenerated view again.  Any View,
            // Edge, SketchSegment or SelectData object obtained before UnBreakView is now treated
            // as stale and is never reused.
            try { model.ClearSelection2(true); } catch { }
            if (!drawing.ActivateSheet(identity.SheetName))
                throw new InvalidOperationException("Khong thoat duoc view context sau UnBreakView.");
            model.EditRebuild3();

            SolidWorks.Interop.sldworks.View fresh = ReacquireDrawingView(drawing, identity);
            if (IsBrokenDrawingView(fresh))
                throw new InvalidOperationException("UnBreakView khong tat duoc trang thai thu ngan tren fresh view.");

            Debug.WriteLine("[DIM MAT CAT BREAK TX] UnBreakView OK. fresh retainedBreakLines="
                + SafeBreakLineCount(fresh));
            return fresh;
        }

        private static void SelectDrawingViewForBreakCommand(ModelDoc2 model,
            DrawingDoc drawing, DrawingViewIdentity identity)
        {
            if (model == null || drawing == null || identity == null)
                throw new InvalidOperationException("Thieu context de select Drawing View cho BreakView.");

            if (!drawing.ActivateSheet(identity.SheetName))
                throw new InvalidOperationException("Khong activate duoc sheet truoc khi select Drawing View cho BreakView.");

            try { model.ClearSelection2(true); } catch { }

            ModelDocExtension ext = model.Extension;
            bool selected = false;
            try
            {
                selected = ext != null && ext.SelectByID2(
                    identity.ViewName,
                    "DRAWINGVIEW",
                    0.0, 0.0, 0.0,
                    false,
                    0,
                    null,
                    0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[DIM MAT CAT BREAK TX] SelectByID2(DRAWINGVIEW) failed: "
                    + ex.GetBaseException().Message);
            }

            if (!selected)
                throw new InvalidOperationException("Khong select duoc Drawing View tren sheet truoc BreakView: "
                    + identity.ViewName);

            Debug.WriteLine("[DIM MAT CAT BREAK TX] drawing view selected on sheet for BreakView: "
                + identity.ViewName);
        }

        private static SolidWorks.Interop.sldworks.View RestoreBrokenViewAndReacquire(ModelDoc2 model,
            DrawingDoc drawing, DrawingViewIdentity identity, BrokenViewState state)
        {
            if (state == null || !state.WasBroken)
                return ReacquireDrawingView(drawing, identity);

            // Exit the drawing-view sketch before BreakView.  v18 called BreakView while the view
            // sketch was still active; on the tested SOLIDWORKS build that command did nothing.
            try { model.ClearSelection2(true); } catch { }
            if (!drawing.ActivateSheet(identity.SheetName))
                throw new InvalidOperationException("Khong activate duoc sheet truoc khi restore Broken View.");
            model.EditRebuild3();

            SolidWorks.Interop.sldworks.View fresh = ReacquireDrawingView(drawing, identity);
            if (IsBrokenDrawingView(fresh))
            {
                Debug.WriteLine("[DIM MAT CAT BREAK TX] restore skipped: fresh view already broken.");
                return fresh;
            }

            SolidWorks.Interop.sldworks.IView iview = fresh as SolidWorks.Interop.sldworks.IView;
            if (iview == null)
                throw new InvalidOperationException("Khong truy cap duoc fresh IView khi restore Broken View.");
            try { iview.BreakLineGap = state.Gap; } catch { }

            int retained = SafeBreakLineCount(fresh);
            if (retained == state.ExpectedBreakCount)
            {
                Debug.WriteLine("[DIM MAT CAT BREAK TX] BreakView begin on fresh view. retainedBreakLines=" + retained);
                // IDrawingDoc.BreakView is a drawing command.  It must run from sheet context
                // with the drawing view selected.  ActivateView enters the view-sketch context;
                // v18.2 did that and then cleared selection, so BreakView had no target and the
                // view stayed unbroken even though GetBreakLines still returned the definition.
                SelectDrawingViewForBreakCommand(model, drawing, identity);
                drawing.BreakView();
                model.EditRebuild3();
            }
            else if (retained == 0)
            {
                Debug.WriteLine("[DIM MAT CAT BREAK TX] no retained definitions; recreate on fresh view with InsertBreak3.");
                fresh = ReacquireDrawingView(drawing, identity);
                iview = fresh as SolidWorks.Interop.sldworks.IView;
                if (iview == null)
                    throw new InvalidOperationException("Khong truy cap duoc fresh IView truoc InsertBreak3.");
                if (!drawing.ActivateView(identity.ViewName))
                    throw new InvalidOperationException("Khong activate duoc fresh view truoc InsertBreak3.");
                try { iview.BreakLineGap = state.Gap; } catch { }
                foreach (BreakLineState b in state.Lines)
                {
                    object inserted = iview.InsertBreak3(
                        b.Orientation,
                        b.Position1,
                        b.Position2,
                        b.Style,
                        b.ShapeIntensity,
                        b.BreakSketchBlocks);
                    if (inserted == null)
                        throw new InvalidOperationException("InsertBreak3 tra ve null khi recreate Broken View.");
                }
                model.EditRebuild3();
            }
            else
            {
                throw new InvalidOperationException("So break definition tren fresh view khong ro rang: expected="
                    + state.ExpectedBreakCount + ", retained=" + retained + ".");
            }

            // Re-query again AFTER BreakView/InsertBreak3. Never inspect the pre-break RCW.
            try { model.ClearSelection2(true); } catch { }
            if (!drawing.ActivateSheet(identity.SheetName))
                throw new InvalidOperationException("Khong thoat duoc view context sau restore Broken View.");
            model.EditRebuild3();
            fresh = ReacquireDrawingView(drawing, identity);

            // InsertBreak3 may recreate the definitions without applying them immediately on some
            // releases.  If so, one fresh BreakView call is safe now because every COM reference
            // was reacquired after the insertion.
            if (!IsBrokenDrawingView(fresh) && SafeBreakLineCount(fresh) == state.ExpectedBreakCount)
            {
                Debug.WriteLine("[DIM MAT CAT BREAK TX] definitions exist but view is unbroken; apply BreakView with selected drawing view.");
                SelectDrawingViewForBreakCommand(model, drawing, identity);
                drawing.BreakView();
                model.EditRebuild3();
                drawing.ActivateSheet(identity.SheetName);
                model.EditRebuild3();
                fresh = ReacquireDrawingView(drawing, identity);
            }

            if (!IsBrokenDrawingView(fresh))
                throw new InvalidOperationException("Khong restore duoc trang thai Broken View tren fresh view.");

            int restoredCount = SafeBreakLineCount(fresh);
            if (restoredCount != state.ExpectedBreakCount)
                throw new InvalidOperationException("So break line sau restore khong khop: expected="
                    + state.ExpectedBreakCount + ", actual=" + restoredCount + ".");

            SolidWorks.Interop.sldworks.IView finalView = fresh as SolidWorks.Interop.sldworks.IView;
            try { if (finalView != null) finalView.BreakLineGap = state.Gap; } catch { }
            model.EditRebuild3();
            Debug.WriteLine("[DIM MAT CAT BREAK TX] restore OK on fresh view. count=" + restoredCount
                + ", gapMm=" + (state.Gap * 1000.0).ToString("0.###"));
            return ReacquireDrawingView(drawing, identity);
        }

        private bool FreshDimensionSetMatches(SolidWorks.Interop.sldworks.View view,
            List<PlannedDisplay> planned, out string reason)
        {
            reason = null;
            if (view == null)
            {
                reason = "fresh view=null";
                return false;
            }

            List<DisplayDimension> actual = SnapshotSectionDimensions(view);
            var remaining = new List<DisplayDimension>(actual);
            foreach (PlannedDisplay item in planned)
            {
                int match = -1;
                for (int i = 0; i < remaining.Count; i++)
                {
                    try
                    {
                        if (SectionDimensionMatches(remaining[i], item.Expected, item.Type))
                        {
                            match = i;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
                if (match < 0)
                {
                    reason = item.Label + " khong tim thay tren fresh view sau Broken restore";
                    return false;
                }
                remaining.RemoveAt(match);
            }
            return true;
        }

        private static bool SameSketchObject(object first, object second)
        {
            if (first == null || second == null) return false;
            if (ReferenceEquals(first, second) || first.Equals(second)) return true;
            IntPtr a = IntPtr.Zero, b = IntPtr.Zero;
            try
            {
                a = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(first);
                b = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(second);
                return a == b;
            }
            finally
            {
                if (a != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(a);
                if (b != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(b);
            }
        }
        private static void ValidateDrawingSketchContext(ModelDoc2 model, DrawingDoc drawing)
        {
            Sketch active = model.SketchManager.ActiveSketch as Sketch;
            if (active == null) return;
            // GetFirstView includes the sheet sketch. A native drawing sketch
            // being active is normal, not evidence of an unrelated edit session.
            for (var candidate = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
                candidate != null; candidate = candidate.GetNextView() as SolidWorks.Interop.sldworks.View)
                if (SameSketchObject(active, candidate.GetSketch()))
                {
                    Debug.WriteLine("[DIM MAT CAT SKETCH] existing native sheet/view sketch accepted: " + candidate.Name);
                    return;
                }
            throw new InvalidOperationException("Dang sua sketch/block khac sketch nen cua drawing view; thoat che do sua do truoc khi DIM.");
        }
        private static Sketch ActivateSectionViewSketch(ModelDoc2 model, DrawingDoc drawing,
            SolidWorks.Interop.sldworks.View view)
        {
            if (!drawing.ActivateView(view.Name))
                throw new InvalidOperationException("Khong kich hoat duoc view DIM: " + view.Name);
            Sketch expected = view.GetSketch() as Sketch;
            Sketch active = model.SketchManager.ActiveSketch as Sketch;
            bool matches = SameSketchObject(expected, active);
            Debug.WriteLine("[DIM MAT CAT SKETCH] target=" + view.Name + ", active matches view=" + matches);
            if (!matches)
                throw new InvalidOperationException("Sketch dang hoat dong khong phai sketch cua view DIM; dung truoc khi ve.");
            return expected;
        }

        private static string CurvedSketchName(SolidWorks.Interop.sldworks.View view)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return "TAI_SECTION_R_V5_" + BitConverter.ToString(hash.ComputeHash(
                    System.Text.Encoding.UTF8.GetBytes(view.Name))).Replace("-", "").Substring(0, 16);
        }
        private void RemoveCurvedSectionSketch(ModelDoc2 model, SolidWorks.Interop.sldworks.View view)
        {
            // Delete only our tagged construction segments in this view sketch.
            // A drawing view's sketch is not a disposable part sketch feature.
            Sketch sketch = view.GetSketch() as Sketch;
            Array objects = sketch == null ? null : sketch.GetSketchSegments() as Array;
            if (objects == null) return;
            foreach (SketchSegment segment in objects.Cast<object>().OfType<SketchSegment>())
                if (segment.Layer == CurvedSketchName(view) && segment.ConstructionGeometry)
                {
                    model.ClearSelection2(true);
                    if (!segment.Select4(false, null)) throw new InvalidOperationException("Khong chon duoc duong DIM R cu.");
                    model.EditDelete();
                }
        }
        private static double[] CurvedSketchPoint(SolidWorks.Interop.sldworks.View view, Profile.Point p)
        {
            double[] origin = view.Position as double[];
            if (origin == null || origin.Length < 2 || view.ScaleDecimal <= 0)
                throw new InvalidOperationException("Khong doc duoc toa do sketch cua view.");
            return new[] { (p.X - origin[0]) / view.ScaleDecimal, (p.Y - origin[1]) / view.ScaleDecimal, 0.0 };
        }
        private Edge CurvedSourceEdge(Profile.Curve curve)
        {
            EdgeInfo line = curve.Source as EdgeInfo;
            if (line != null) return line.Edge;
            ArcInfo arc = curve.Source as ArcInfo;
            return arc == null ? null : arc.Edge;
        }
        private void CurvedConstraint(ModelDoc2 model, string kind, Func<bool> first, Func<bool> second)
        {
            Sketch sketch = model.SketchManager.ActiveSketch as Sketch;
            CheckCurvedSketchStatus(sketch, "before " + kind);
            model.ClearSelection2(true);
            if (!first() || !second()) throw new InvalidOperationException("Khong chon duoc tham chieu " + kind);
            model.SketchAddConstraints(kind);
            CheckCurvedSketchStatus(sketch, "after " + kind);
        }
        private static List<DisplayDimension> SnapshotSectionDimensions(SolidWorks.Interop.sldworks.View view)
        {
            Array items = view.GetAnnotations() as Array;
            return items == null ? new List<DisplayDimension>() : items.Cast<object>().OfType<Annotation>()
                .Where(a => a.GetType() == (int)swAnnotationType_e.swDisplayDimension)
                .Select(a => a.GetSpecificAnnotation() as DisplayDimension).Where(d => d != null).ToList();
        }
        private static List<SketchSegment> SnapshotSectionSegments(Sketch sketch)
        {
            Array items = sketch == null ? null : sketch.GetSketchSegments() as Array;
            return items == null ? new List<SketchSegment>() : items.Cast<object>().OfType<SketchSegment>().ToList();
        }
        private static void CheckCurvedSketchStatus(Sketch sketch, string step)
        {
            if (sketch == null) throw new InvalidOperationException("Khong co sketch tai " + step);
            int status = sketch.GetConstrainedStatus();
            Debug.WriteLine("[DIM MAT CAT CONVERT] " + step + " status=" + ((swConstrainedStatus_e)status));
            if (status == (int)swConstrainedStatus_e.swOverConstrained || status == (int)swConstrainedStatus_e.swNoSolution ||
                status == (int)swConstrainedStatus_e.swInvalidSolution)
                throw new InvalidOperationException("Sketch loi tai " + step + ": " + ((swConstrainedStatus_e)status));
        }
        private void DeleteOwnedSectionSegments(ModelDoc2 model, List<SketchSegment> owned)
        {
            foreach (SketchSegment segment in owned.ToArray().Reverse())
                try { model.ClearSelection2(true); if (segment.Select4(false, null)) model.EditDelete(); }
                catch (Exception ex) { Debug.WriteLine("[DIM MAT CAT CONVERT] cleanup failed: " + ex.Message); }
            owned.Clear();
        }
        private static bool CurvedPointMatches(SketchPoint p, double[] expected)
        {
            return p != null && Math.Sqrt(Math.Pow(p.X - expected[0], 2) + Math.Pow(p.Y - expected[1], 2)) <= .00001;
        }
        private static bool ConvertedCircleMatches(SketchSegment segment, double[] center, double radius)
        {
            SketchArc arc = segment as SketchArc;
            return arc != null && CurvedPointMatches(arc.GetCenterPoint2() as SketchPoint, center) &&
                Math.Abs(arc.GetRadius() - radius) <= .00001;
        }
        private SketchSegment ConvertSectionArc(ModelDoc2 model, SolidWorks.Interop.sldworks.View view, SelectData data,
            Profile.Curve arc, Sketch sketch, List<SketchSegment> owned, string layer)
        {
            var before = SnapshotSectionSegments(sketch);
            bool ok = false;
            var added = new List<SketchSegment>();
            bool previousAddToDB = model.SketchManager.AddToDB;
            try
            {
                model.SketchManager.AddToDB = false;
                model.ClearSelection2(true);
                if (!SelectEdge(CurvedSourceEdge(arc), false, data)) throw new InvalidOperationException("Khong chon duoc cung R de convert.");
                ok = model.SketchManager.SketchUseEdge3(false, false);
            }
            finally
            {
                model.SketchManager.AddToDB = previousAddToDB;
                added = SnapshotSectionSegments(sketch).Where(s => !before.Any(b => SameSketchObject(s, b))).ToList();
                foreach (SketchSegment s in added) { owned.Add(s); s.ConstructionGeometry = true; s.Layer = layer; }
                Debug.WriteLine("[DIM MAT CAT CONVERT] SketchUseEdge3=" + ok + ", new segments=" + added.Count);
            }
            if (!ok || added.Count != 1 || !(added[0] is SketchArc))
                throw new InvalidOperationException("Convert phai tao dung mot cung moi; khong dung lai entity cu.");
            SketchSegment converted = added[0];
            if (!SameSketchObject(converted.GetSketch(), sketch) || !ConvertedCircleMatches(converted,
                CurvedSketchPoint(view, arc.Center), (arc.A - arc.Center).Length / viewScale))
                throw new InvalidOperationException("Cung convert sai sketch/tam/ban kinh.");
            Debug.WriteLine("[DIM MAT CAT CONVERT] source R verified; relations=" + converted.GetRelationsCount());
            if (converted.GetRelationsCount() == 0) throw new InvalidOperationException("Cung convert khong co relation nguon de kiem chung.");
            CheckCurvedSketchStatus(sketch, "convert source R");
            return converted;
        }
        private SketchSegment ConvertAndExtendSectionArc(ModelDoc2 model, SolidWorks.Interop.sldworks.View view,
            SelectData data, Profile.CurvedSection c, Sketch sketch, List<SketchSegment> owned, string layer, out bool direct)
        {
            return ConvertAndExtendSectionArcSupport(model, view, data, c.Arc, c.Start, c.End,
                sketch, owned, layer, out direct);
        }
        private SketchSegment ConvertAndExtendSectionArcSupport(ModelDoc2 model, SolidWorks.Interop.sldworks.View view,
            SelectData data, Profile.Curve sourceArc, Profile.Point start, Profile.Point end,
            Sketch sketch, List<SketchSegment> owned, string layer, out bool direct)
        {
            if (sourceArc == null || !sourceArc.IsArc)
                throw new InvalidOperationException("Thieu cung nguon cho curved support.");

            SketchSegment converted = ConvertSectionArc(model, view, data, sourceArc, sketch, owned, layer);
            double[] p = CurvedSketchPoint(view, start), q = CurvedSketchPoint(view, end);
            SketchArc arc = converted as SketchArc;
            if (arc == null) throw new InvalidOperationException("Cung convert khong phai SketchArc.");
            SketchPoint a = arc.GetStartPoint2() as SketchPoint, b = arc.GetEndPoint2() as SketchPoint;
            if (a == null || b == null) throw new InvalidOperationException("Thieu dau mut cung convert.");

            double forward = Math.Pow(a.X - p[0], 2) + Math.Pow(a.Y - p[1], 2)
                + Math.Pow(b.X - q[0], 2) + Math.Pow(b.Y - q[1], 2);
            double reverse = Math.Pow(b.X - p[0], 2) + Math.Pow(b.Y - p[1], 2)
                + Math.Pow(a.X - q[0], 2) + Math.Pow(a.Y - q[1], 2);
            if (reverse < forward) { SketchPoint swap = a; a = b; b = swap; }

            direct = false;
            try
            {
                bool movedA = CurvedPointMatches(a, p) || a.SetCoords(p[0], p[1], 0);
                bool movedB = CurvedPointMatches(b, q) || b.SetCoords(q[0], q[1], 0);
                model.EditRebuild3();
                direct = movedA && movedB && CurvedPointMatches(a, p) && CurvedPointMatches(b, q) &&
                    ConvertedCircleMatches(converted, CurvedSketchPoint(view, sourceArc.Center),
                        (sourceArc.A - sourceArc.Center).Length / viewScale) &&
                    converted.GetRelationsCount() > 0;
                if (direct) CheckCurvedSketchStatus(sketch, "direct extension");
            }
            catch (Exception ex)
            {
                direct = false;
                Debug.WriteLine("[DIM MAT CAT CONVERT] direct extension blocked: " + ex.Message);
            }

            Debug.WriteLine("[DIM MAT CAT CONVERT] direct extended endpoints valid=" + direct);
            if (direct) return converted;

            // Discard only this extension attempt.  Reconvert the original model
            // arc as a pristine co-radial anchor, then create an auxiliary arc
            // for the envelope.  This is done independently for every arc in a
            // multi-arc chain; no arc is chosen by name or nearest distance.
            model.ClearSelection2(true);
            if (!converted.Select4(false, null))
                throw new InvalidOperationException("Khong don duoc cung thu keo dai.");
            model.EditDelete();
            if (SnapshotSectionSegments(sketch).Any(s => SameSketchObject(s, converted)))
                throw new InvalidOperationException("Cung thu keo dai chua xoa duoc; dung truoc fallback.");
            owned.Remove(converted);
            converted = ConvertSectionArc(model, view, data, sourceArc, sketch, owned, layer);
            Debug.WriteLine("[DIM MAT CAT CONVERT] fallback: preserve converted anchor; extend an auxiliary co-radial arc");
            return converted;
        }
        private DisplayDimension CurvedDriven(DisplayDimension display)
        {
            if (display != null && !protectedSectionDimensions.Any(d => SameSketchObject(d, display)))
            {
                Dimension dimension = display.GetDimension2(0) as Dimension;
                if (dimension != null) dimension.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriven;
            }
            return display;
        }
        private void CreateCurvedSectionDimensions(ModelDoc2 model, SolidWorks.Interop.sldworks.View view,
            SelectData data, Profile.Plan plan, List<PlannedDisplay> created, List<string> errors, double cx, double cy,
            List<SketchSegment> ownedSegments, bool brokenNativeArcReferences)
        {
            Profile.CurvedSection c = plan.Curved;
            bool committed = false;
            int before = created.Count;
            int errorBefore = errors.Count;
            try
            {
                Sketch sketch = ActivateSectionViewSketch(model, (DrawingDoc)model, view);
                CheckCurvedSketchStatus(sketch, "baseline before creating anything");
                LayerMgr layers = model.GetLayerManager() as LayerMgr;
                if (layers == null)
                    throw new InvalidOperationException("Khong doc duoc layer manager de danh dau sketch DIM.");
                string layerName = CurvedSketchName(view);
                if (layers.GetLayer(layerName) == null)
                    layers.AddLayer(layerName, "TAI generated curved section construction; do not use for manual geometry", 8421504, 0, 0);
                if (layers.GetLayer(layerName) == null)
                    throw new InvalidOperationException("Khong tao duoc layer sketch DIM.");

                int arcCount = c.Supports.Count(s => s.IsArc);
                Debug.WriteLine("[DIM MAT CAT CURVED CHAIN] create supports=" + c.Supports.Count
                    + ", arcs=" + arcCount);

                var arcAnchors = new Dictionary<int, SketchSegment>();
                var arcDirect = new Dictionary<int, bool>();
                for (int i = 0; i < c.Supports.Count; i++)
                {
                    if (!c.Supports[i].IsArc) continue;
                    bool direct;
                    SketchSegment anchor = ConvertAndExtendSectionArcSupport(model, view, data,
                        c.Supports[i], c.Nodes[i].Position, c.Nodes[i + 1].Position,
                        sketch, ownedSegments, layerName, out direct);
                    arcAnchors[i] = anchor;
                    arcDirect[i] = direct;
                    Debug.WriteLine("[DIM MAT CAT CURVED CHAIN] prepared arc index=" + i
                        + ", direct=" + direct);
                }

                var segments = new List<SketchSegment>();
                var starts = new List<SketchPoint>();
                var ends = new List<SketchPoint>();
                for (int i = 0; i < c.Supports.Count; i++)
                {
                    Profile.Curve support = c.Supports[i];
                    double[] p = CurvedSketchPoint(view, c.Nodes[i].Position),
                             q = CurvedSketchPoint(view, c.Nodes[i + 1].Position);
                    SketchSegment segment;
                    if (support.IsArc)
                    {
                        SketchSegment anchor = arcAnchors[i];
                        bool direct = arcDirect[i];
                        Profile.Point midPoint = c.SupportMids.Count > i
                            ? c.SupportMids[i]
                            : c.Mid;
                        double[] mid = CurvedSketchPoint(view, midPoint);
                        segment = direct ? anchor : CreateSectionWithoutInference(model, () =>
                            model.SketchManager.Create3PointArc(
                                p[0], p[1], 0,
                                q[0], q[1], 0,
                                mid[0], mid[1], 0) as SketchSegment);
                    }
                    else
                    {
                        segment = CreateSectionWithoutInference(model, () =>
                            model.SketchManager.CreateLine(p[0], p[1], 0, q[0], q[1], 0) as SketchSegment);
                    }

                    if (segment == null)
                        throw new InvalidOperationException("Khong tao duoc duong sketch phu bi " + i);
                    segment.ConstructionGeometry = true;
                    if (!ownedSegments.Any(s => SameSketchObject(s, segment)))
                        ownedSegments.Add(segment);
                    if (!SameSketchObject(segment.GetSketch(), sketch))
                        throw new InvalidOperationException("Duong phu vua tao khong thuoc sketch cua view DIM.");
                    segment.Layer = layerName;
                    segments.Add(segment);

                    SketchArc arc = segment as SketchArc;
                    SketchLine line = segment as SketchLine;
                    starts.Add(arc != null ? arc.GetStartPoint2() as SketchPoint :
                        line == null ? null : line.GetStartPoint2() as SketchPoint);
                    ends.Add(arc != null ? arc.GetEndPoint2() as SketchPoint :
                        line == null ? null : line.GetEndPoint2() as SketchPoint);
                    if (starts[i] == null || ends[i] == null)
                        throw new InvalidOperationException("Thieu dau mut sketch.");

                    // A three-point arc may reverse its parameter direction.
                    double ds = Math.Pow(starts[i].X - p[0], 2) + Math.Pow(starts[i].Y - p[1], 2);
                    double de = Math.Pow(ends[i].X - p[0], 2) + Math.Pow(ends[i].Y - p[1], 2);
                    if (de < ds)
                    {
                        SketchPoint swap = starts[i];
                        starts[i] = ends[i];
                        ends[i] = swap;
                    }

                    Edge source = CurvedSourceEdge(support);
                    Debug.WriteLine("[DIM MAT CAT CONVERT] support index=" + i + ", arc=" + support.IsArc);
                    if (support.IsArc)
                    {
                        if (!arcDirect[i])
                        {
                            SketchSegment anchor = arcAnchors[i];
                            CurvedConstraint(model, "sgCORADIAL",
                                () => segment.Select4(false, null),
                                () => anchor.Select4(true, null));
                        }
                    }
                    else
                    {
                        CurvedConstraint(model, "sgCOLINEAR",
                            () => segment.Select4(false, null),
                            () => SelectEdge(source, true, data));
                    }
                }

                for (int i = 1; i < segments.Count; i++)
                {
                    int j = i;
                    Debug.WriteLine("[DIM MAT CAT CONVERT] joint index=" + j);
                    if (!SameSketchObject(ends[j - 1], starts[j]))
                        CurvedConstraint(model, "sgCOINCIDENT",
                            () => ends[j - 1].Select4(false, null),
                            () => starts[j].Select4(true, null));
                }

                CurvedConstraint(model, "sgCOINCIDENT",
                    () => starts[0].Select4(false, null),
                    () => SelectEdge(CurvedSourceEdge(c.Nodes[0].Second), true, data));
                CurvedConstraint(model, "sgCOINCIDENT",
                    () => ends[ends.Count - 1].Select4(false, null),
                    () => SelectEdge(CurvedSourceEdge(c.Nodes.Last().Second), true, data));

                int sketchStatus = sketch.GetConstrainedStatus();
                if (sketchStatus == (int)swConstrainedStatus_e.swOverConstrained ||
                    sketchStatus == (int)swConstrainedStatus_e.swNoSolution ||
                    sketchStatus == (int)swConstrainedStatus_e.swInvalidSolution)
                    throw new InvalidOperationException("Sketch phu bi co rang buoc mau thuan/khong co nghiem.");

                for (int i = 0; i < segments.Count; i++)
                {
                    double[] a = CurvedSketchPoint(view, c.Nodes[i].Position),
                             b = CurvedSketchPoint(view, c.Nodes[i + 1].Position);
                    if (Distance2D(starts[i].X, starts[i].Y, a[0], a[1]) > .00001 ||
                        Distance2D(ends[i].X, ends[i].Y, b[0], b[1]) > .00001)
                        throw new InvalidOperationException("Sketch phu bi doi moc sau khi rang buoc; khong tao DIM sai.");
                }

                for (int i = 0; i < segments.Count; i++)
                {
                    int j = i;
                    Profile.Point center = (c.Nodes[i].Position + c.Nodes[i + 1].Position) * .5;
                    Profile.Point vector = c.Nodes[i + 1].Position - c.Nodes[i].Position;
                    Profile.Point normal = new Profile.Point(-vector.Y, vector.X).Unit;
                    if (Profile.Dot(center - new Profile.Point(cx, cy), normal) < 0)
                        normal = normal * -1;
                    Profile.Point text = center + normal * MmToM(DimOffsetMm);

                    if (c.Supports[i].IsArc)
                    {
                        double arcLength = c.SupportArcLengths.Count > i
                            ? c.SupportArcLengths[i]
                            : c.ArcLength;
                        double expectedArcLength = arcLength / viewScale;
                        Debug.WriteLine("[DIM MAT CAT CURVED CHAIN] arc index=" + i
                            + ", arcLengthMm=" + (expectedArcLength * 1000).ToString("0.###"));

                        Profile.Curve arcSupport = c.Supports[i];
                        ArcInfo sourceArc = arcSupport.Source as ArcInfo;

                        PlannedDisplay arcLengthItem = TryCreateSectionDimension(model, created, errors,
                            arcCount > 1 ? "Curved chain arc length " + i : "Curved web arc length",
                            expectedArcLength,
                            (int)swDimensionType_e.swArcLengthDimension, () =>
                            {
                                DisplayDimension d = null;
                                if (brokenNativeArcReferences)
                                {
                                    // Broken View workaround: create the native Arc Length DIM
                                    // directly from the real Drawing Edge + its two vertices while
                                    // the view is temporarily unbroken.  Do NOT attach the DIM to
                                    // SketchUseEdge3 construction; that sketch is not remapped by
                                    // Broken View the same way as the model edge.
                                    if (sourceArc == null || sourceArc.Edge == null)
                                    {
                                        Debug.WriteLine("[DIM MAT CAT ARC REF] arc index=" + j
                                            + ", original-edge unavailable; arc-length DIM skipped.");
                                        return null;
                                    }
                                    d = TryAddArcLengthByReferences(model, data, sourceArc,
                                        text.X, text.Y, true)
                                        ?? TryAddArcLengthByReferences(model, data, sourceArc,
                                            text.X, text.Y, false);
                                    if (d != null)
                                        Debug.WriteLine("[DIM MAT CAT ARC REF] arc index=" + j
                                            + ", reference=original-drawing-edge+vertices");
                                }
                                else
                                {
                                    // Non-Broken views keep the proven legacy path unchanged.
                                    model.ClearSelection2(true);
                                    if (!segments[j].Select4(false, null) ||
                                        !starts[j].Select4(true, null) ||
                                        !ends[j].Select4(true, null)) return null;
                                    d = model.AddDimension2(text.X, text.Y, 0) as DisplayDimension;
                                    if (d != null && d.GetType() != (int)swDimensionType_e.swArcLengthDimension)
                                    {
                                        DeleteDisplayDimension(model, d);
                                        d = null;
                                    }
                                    if (d == null)
                                    {
                                        model.ClearSelection2(true);
                                        segments[j].Select4(false, null);
                                        starts[j].Select4(true, null);
                                        ends[j].Select4(true, null);
                                        int error = 0;
                                        d = model.Extension.AddSpecificDimension(text.X, text.Y, 0,
                                            (int)swDimensionType_e.swArcLengthDimension, ref error) as DisplayDimension;
                                    }
                                }
                                if (d != null && d.GetType() == (int)swDimensionType_e.swArcLengthDimension)
                                    ApplyRadialArcLengthLeader(model, d);
                                return CurvedDriven(d);
                            });

                        double radius = (arcSupport.A - arcSupport.Center).Length / viewScale;

                        // IMPORTANT: Radius DIM attaches to the original Drawing Edge.
                        // Its annotation is repositioned after BreakView by the exact display
                        // translation of that same edge.
                        ArcInfo radiusSourceArc = sourceArc;
                        double radiusTextX = text.X - normal.X * MmToM(20);
                        double radiusTextY = text.Y - normal.Y * MmToM(20);
                        PlannedDisplay radiusItem = TryCreateSectionDimension(model, created, errors,
                            arcCount > 1 ? "Curved chain radius " + i : "Curved web radius",
                            radius,
                            (int)swDimensionType_e.swRadialDimension, () =>
                            {
                                model.ClearSelection2(true);
                                if (radiusSourceArc == null || radiusSourceArc.Edge == null)
                                {
                                    Debug.WriteLine("[DIM MAT CAT R REF] arc index=" + j
                                        + ", original-edge unavailable; radius DIM skipped.");
                                    return null;
                                }
                                if (!SelectEdge(radiusSourceArc.Edge, false, data))
                                {
                                    Debug.WriteLine("[DIM MAT CAT R REF] arc index=" + j
                                        + ", SelectEdge(original-edge) failed.");
                                    return null;
                                }
                                Debug.WriteLine("[DIM MAT CAT R REF] arc index=" + j
                                    + ", reference=original-drawing-edge, Rmm="
                                    + (radius * 1000).ToString("0.###"));
                                DisplayDimension d = model.AddRadialDimension2(
                                    radiusTextX, radiusTextY, 0) as DisplayDimension;
                                if (d != null) ApplyCompactRadiusStyle(model, d);
                                return CurvedDriven(d);
                            });

                    }
                    else
                    {
                        double value = vector.Length / viewScale;
                        Debug.WriteLine("[DIM MAT CAT CURVED] flange " + i
                            + " mm=" + (value * 1000).ToString("0.###"));
                        TryCreateSectionDimension(model, created, errors,
                            "Curved flange " + i,
                            value,
                            (int)swDimensionType_e.swLinearDimension, () =>
                            {
                                model.ClearSelection2(true);
                                if (!segments[j].Select4(false, null)) return null;
                                return CurvedDriven(model.AddDimension2(text.X, text.Y, 0) as DisplayDimension);
                            });
                    }
                }

                for (int i = 0; i < c.Angles.Count; i++)
                {
                    if (c.Angles[i] == 0)
                    {
                        Debug.WriteLine("[DIM MAT CAT CURVED JOINT] skip angular dimension index=" + i);
                        continue;
                    }
                    SketchSegment first = segments[i], second = segments[i + 1];
                    if (c.Supports[i].IsArc)
                        first = CreateCurvedTangent(model, view, data, c.Supports[i],
                            c.Nodes[i + 1].Position, ownedSegments);
                    if (c.Supports[i + 1].IsArc)
                        second = CreateCurvedTangent(model, view, data, c.Supports[i + 1],
                            c.Nodes[i + 1].Position, ownedSegments);
                    double expected = c.Angles[i] * Math.PI / 180;
                    Profile.Point text = c.Nodes[i + 1].Position +
                        new Profile.Point(MmToM(12), MmToM(12));
                    TryCreateSectionDimension(model, created, errors,
                        "Curved bend " + i,
                        expected,
                        (int)swDimensionType_e.swAngularDimension, () =>
                        {
                            model.ClearSelection2(true);
                            if (!first.Select4(false, null) || !second.Select4(true, null)) return null;
                            DisplayDimension d = model.AddDimension2(text.X, text.Y, 0) as DisplayDimension;
                            if (d != null && d.GetType() == (int)swDimensionType_e.swAngularDimension &&
                                !SectionDimensionMatches(d, expected, (int)swDimensionType_e.swAngularDimension))
                            {
                                Dimension value = d.GetDimension2(0) as Dimension;
                                if (value != null && Math.Abs(value.GetSystemValue2("") + expected - Math.PI) < .001)
                                    d.SupplementaryAngle();
                            }
                            return CurvedDriven(d);
                        });
                }

                if (errors.Count != errorBefore)
                    throw new InvalidOperationException("Co DIM sketch cong khong dat kiem tra; huy bo nhom DIM vua tao.");
                model.ClearSelection2(true);
                model.EditRebuild3();
                if (created.Skip(before).Any(d => !SectionDimensionMatches(d.Display, d.Expected, d.Type)))
                    throw new InvalidOperationException("DIM sketch cong sai sau rebuild; huy nhom vua tao.");
                committed = true;
            }
            finally
            {
                model.ClearSelection2(true);
                if (!committed)
                {
                    foreach (PlannedDisplay item in created.Skip(before).ToArray())
                    {
                        DeleteDisplayDimension(model, item.Display);
                        created.Remove(item);
                    }
                    DeleteOwnedSectionSegments(model, ownedSegments);
                }
            }
        }
        private static SketchSegment CreateSectionWithoutInference(ModelDoc2 model, Func<SketchSegment> create)
        {
            bool previous = model.SketchManager.AddToDB;
            try { model.SketchManager.AddToDB = true; return create(); }
            finally { model.SketchManager.AddToDB = previous; }
        }
        private SketchSegment CreateCurvedTangent(ModelDoc2 model, SolidWorks.Interop.sldworks.View view,
            SelectData data, Profile.Curve arc, Profile.Point junction, List<SketchSegment> ownedSegments)
        {
            Profile.Point p = (arc.A - junction).Length < (arc.B - junction).Length ? arc.A : arc.B;
            Profile.Point r = p - arc.Center;
            Profile.Point tangent = new Profile.Point(-r.Y, r.X).Unit;
            double[] a = CurvedSketchPoint(view, p), b = CurvedSketchPoint(view, p + tangent * MmToViewM(10));
            SketchSegment line = CreateSectionWithoutInference(model, () => model.SketchManager.CreateLine(a[0], a[1], 0, b[0], b[1], 0) as SketchSegment);
            if (line == null) throw new InvalidOperationException("Khong tao duoc line tiep tuyen de DIM goc.");
            line.ConstructionGeometry = true;
            ownedSegments.Add(line);
            line.Layer = CurvedSketchName(view);
            Edge source = CurvedSourceEdge(arc);
            SketchPoint point = ((SketchLine)line).GetStartPoint2() as SketchPoint;
            Vertex anchor = null;
            MathUtility math = swApp.GetMathUtility() as MathUtility;
            foreach (Vertex v in new[] { source.GetStartVertex() as Vertex, source.GetEndVertex() as Vertex })
            {
                double[] q = v == null ? null : TransformPoint(math, view.ModelToViewTransform as MathTransform, v.GetPoint() as double[]);
                if (q != null && Distance2D(q[0], q[1], p.X, p.Y) <= MmToViewM(.01)) { anchor = v; break; }
            }
            if (anchor == null) throw new InvalidOperationException("Khong tim duoc vertex tiep tuyen de rang buoc DIM goc.");
            CurvedConstraint(model, "sgCOINCIDENT", () => point.Select4(false, null), () => SelectReference(anchor, true, data));
            CurvedConstraint(model, "sgTANGENT", () => line.Select4(false, null), () => SelectEdge(source, true, data));
            return line;
        }
        private Profile.Plan ReadSectionPlan(SolidWorks.Interop.sldworks.View view, MathUtility math,
            MathTransform transform, EdgeInfo seedLine, ArcInfo seedArc)
        {
            var lines = CollectVisibleLineEdges(view, math, transform);
            var arcs = CollectVisibleArcEdges(view, math, transform);
            object seed = seedLine != null ? (object)FindMatchingEdgeGeometry(lines, seedLine) : FindMatchingArcGeometry(arcs, seedArc);
            if (seed == null) throw new InvalidOperationException("Khong tim duoc canh/cung click trong hinh hoc view.");
            var geometry = new List<Profile.Curve>();
            foreach (EdgeInfo edge in lines)
                geometry.Add(new Profile.Curve { A = new Profile.Point(edge.X1, edge.Y1), B = new Profile.Point(edge.X2, edge.Y2), Source = edge });
            foreach (ArcInfo arc in arcs)
                if (!IsFullCircleArc(arc))
                    geometry.Add(new Profile.Curve
                    {
                        A = new Profile.Point(arc.StartX, arc.StartY),
                        B = new Profile.Point(arc.EndX, arc.EndY),
                        Center = new Profile.Point(arc.CenterX, arc.CenterY),
                        IsArc = true,
                        SweepAngleRadians = arc.SweepAngleRad,
                        Source = arc
                    });
            // Keep geometry validation strict. The wider tolerance is used only
            // by the planner when an exact continuation does not exist, which
            // repairs tiny Drawing-API endpoint gaps without loosening thickness,
            // tangency, parallelism, or dimension geometry checks.
            double geometryTol = MmToViewM(.01);
            double topologyRepairTol = MmToViewM(.20);

            Debug.WriteLine(
                "[DIM MAT CAT GEO] VIEW=" + view.Name
                + ", scale=" + viewScale.ToString("0.######")
                + ", lines=" + lines.Count
                + ", arcs=" + arcs.Count
                + ", curves=" + geometry.Count
                + ", geometryTolModel=0.01 mm"
                + ", topologyRepairModel=0.20 mm"
                + ", geometryTolSheet=" + (geometryTol * 1000.0).ToString("0.######") + " mm"
                + ", topologyRepairSheet=" + (topologyRepairTol * 1000.0).ToString("0.######") + " mm");

            // Raw diagnostic stays on the strict tolerance so OPEN/BRANCH lines
            // describe the geometry SolidWorks actually returned.
            DebugSectionConnectivity(geometry, geometryTol);

            Profile.Plan plan;
            string reason;
            if (!Profile.TryBuild(geometry, seed, geometryTol, topologyRepairTol, out plan, out reason))
                throw new InvalidOperationException(reason);
            return plan;
        }

        private void DebugSectionConnectivity(List<Profile.Curve> geometry, double tolerance)
        {
            if (geometry == null)
                return;

            Debug.WriteLine("[DIM MAT CAT GEO] ===== CONNECTIVITY CHECK =====");

            for (int i = 0; i < geometry.Count; i++)
            {
                Profile.Curve curve = geometry[i];

                double curveLengthSheet = (curve.B - curve.A).Length * 1000.0;
                double curveLengthModel = viewScale > 0.0
                    ? curveLengthSheet / viewScale
                    : curveLengthSheet;

                Debug.WriteLine(
                    "[DIM MAT CAT GEO] CURVE #" + i
                    + " type=" + (curve.IsArc ? "ARC" : "LINE")
                    + ", chordModel=" + curveLengthModel.ToString("0.######") + " mm"
                    + ", A_sheet=(" + (curve.A.X * 1000.0).ToString("0.###")
                    + "," + (curve.A.Y * 1000.0).ToString("0.###") + ")mm"
                    + ", B_sheet=(" + (curve.B.X * 1000.0).ToString("0.###")
                    + "," + (curve.B.Y * 1000.0).ToString("0.###") + ")mm");

                Profile.Point[] ends = { curve.A, curve.B };

                for (int e = 0; e < ends.Length; e++)
                {
                    Profile.Point point = ends[e];
                    int connectedCount = 0;
                    double nearestDistance = double.MaxValue;
                    int nearestCurveIndex = -1;
                    string nearestEnd = "";

                    for (int j = 0; j < geometry.Count; j++)
                    {
                        if (j == i)
                            continue;

                        Profile.Curve other = geometry[j];
                        double da = (other.A - point).Length;
                        double db = (other.B - point).Length;

                        if (da <= tolerance)
                            connectedCount++;
                        if (db <= tolerance)
                            connectedCount++;

                        if (da < nearestDistance)
                        {
                            nearestDistance = da;
                            nearestCurveIndex = j;
                            nearestEnd = "A";
                        }

                        if (db < nearestDistance)
                        {
                            nearestDistance = db;
                            nearestCurveIndex = j;
                            nearestEnd = "B";
                        }
                    }

                    if (connectedCount != 1)
                    {
                        double nearestGapSheetMm = nearestDistance == double.MaxValue
                            ? double.MaxValue
                            : nearestDistance * 1000.0;
                        double nearestGapModelMm = nearestGapSheetMm == double.MaxValue
                            ? double.MaxValue
                            : (viewScale > 0.0 ? nearestGapSheetMm / viewScale : nearestGapSheetMm);

                        Debug.WriteLine(
                            "[DIM MAT CAT GEO] " + (connectedCount == 0 ? "OPEN" : "BRANCH")
                            + " curve=#" + i
                            + ", end=" + (e == 0 ? "A" : "B")
                            + ", pointSheet=(" + (point.X * 1000.0).ToString("0.###")
                            + "," + (point.Y * 1000.0).ToString("0.###") + ")mm"
                            + ", connections=" + connectedCount
                            + ", nearestCurve=#" + nearestCurveIndex
                            + ", nearestEnd=" + nearestEnd
                            + ", nearestGapSheet="
                            + (nearestGapSheetMm == double.MaxValue ? "NONE" : nearestGapSheetMm.ToString("0.######") + " mm")
                            + ", nearestGapModel="
                            + (nearestGapModelMm == double.MaxValue ? "NONE" : nearestGapModelMm.ToString("0.######") + " mm"));
                    }
                }
            }

            Debug.WriteLine("[DIM MAT CAT GEO] ===== END CONNECTIVITY =====");
        }

        private PlannedDisplay TryCreateSectionDimension(ModelDoc2 model, List<PlannedDisplay> created,
            List<string> errors, string label, double expected, int type, Func<DisplayDimension> create)
        {
            DisplayDimension display = null;
            try
            {
                display = create();
                if (protectedSectionDimensions.Any(d => SameSketchObject(d, display)))
                    throw new InvalidOperationException("API tra ve DIM cu; khong thay doi/xoa DIM do.");
                if (!SectionDimensionMatches(display, expected, type))
                    throw new InvalidOperationException("Khong tao duoc DIM dung loai, gia tri va lien ket.");
                var item = new PlannedDisplay { Display = display, Expected = expected, Type = type, Label = label };
                created.Add(item);
                return item;
            }
            catch (Exception ex)
            {
                if (display != null && !protectedSectionDimensions.Any(d => SameSketchObject(d, display))) DeleteDisplayDimension(model, display);
                errors.Add(label + ": " + ex.Message);
                Debug.WriteLine("[DIM MAT CAT] " + label + " failed: " + ex);
                return null;
            }
        }

        private DisplayDimension CreateSectionLength(ModelDoc2 model, SolidWorks.Interop.sldworks.View view, SelectData data,
            Profile.Length length, Dictionary<Profile.Reference, object> cache, List<SketchPoint> createdPoints, double cx, double cy)
        {
            Profile.Point direction = length.Side.Direction;
            Profile.Point normal = new Profile.Point(-direction.Y, direction.X);
            Profile.Point midpoint = (length.Start.Position + length.End.Position) * .5;
            double sign = Profile.Dot(midpoint - new Profile.Point(cx, cy), normal) >= 0 ? 1 : -1;
            Profile.Point text = midpoint + normal * (sign * MmToM(DimOffsetMm));
            EdgeInfo firstBoundary = SectionBoundary(length.Start, direction), lastBoundary = SectionBoundary(length.End, direction);
            if (firstBoundary != null && lastBoundary != null)
            {
                model.ClearSelection2(true);
                if (!SelectEdge(firstBoundary.Edge, false, data) || !SelectEdge(lastBoundary.Edge, true, data)) return null;
                return model.AddDimension2(text.X, text.Y, 0) as DisplayDimension;
            }
            object first = ResolveSectionReference(model, view, data, length.Start, cache, createdPoints);
            object last = ResolveSectionReference(model, view, data, length.End, cache, createdPoints);
            if (first == null || last == null) return null;
            model.ClearSelection2(true);
            if (!SelectReference(first, false, data) || !SelectReference(last, true, data)) return null;
            // Only use H/V for an actually axis-aligned segment. A shallow bend
            // must stay aligned; sheet-coordinate tolerances must not flatten it.
            if (Math.Abs(direction.Y) < 1e-8) return model.AddHorizontalDimension2(text.X, text.Y, 0) as DisplayDimension;
            if (Math.Abs(direction.X) < 1e-8) return model.AddVerticalDimension2(text.X, text.Y, 0) as DisplayDimension;
            if (Math.Abs(Profile.Dot(length.End.Position - length.Start.Position, normal)) > MmToViewM(.01))
                throw new InvalidOperationException("Hai moc khong cung duong do tren mat da chon.");
            return model.AddDimension2(text.X, text.Y, 0) as DisplayDimension;
        }
        private EdgeInfo SectionBoundary(Profile.Reference reference, Profile.Point axis)
        {
            // At a large-R tangent the perpendicular line is beyond the tangent.
            // It must not be used as the dimension boundary.
            if (reference.IsTangent) return null;
            foreach (Profile.Curve curve in new[] { reference.First, reference.Second })
            {
                EdgeInfo edge = curve == null ? null : curve.Source as EdgeInfo;
                if (edge != null && Math.Abs(edge.DirX * axis.X + edge.DirY * axis.Y) < 1e-8) return edge;
            }
            return null;
        }
        private object ResolveSectionReference(ModelDoc2 model, SolidWorks.Interop.sldworks.View view, SelectData data,
            Profile.Reference reference, Dictionary<Profile.Reference, object> cache, List<SketchPoint> createdPoints)
        {
            object result;
            if (cache.TryGetValue(reference, out result)) return result;
            EdgeInfo first = reference.First.Source as EdgeInfo;
            if (first == null) return null;
            MathUtility math = swApp.GetMathUtility() as MathUtility;
            MathTransform transform = view.ModelToViewTransform as MathTransform;
            foreach (Vertex vertex in new[] { first.Edge.GetStartVertex() as Vertex, first.Edge.GetEndVertex() as Vertex })
            {
                if (vertex == null) continue;
                double[] p = TransformPoint(math, transform, vertex.GetPoint() as double[]);
                if (p != null && Distance2D(p[0], p[1], reference.Position.X, reference.Position.Y) <= MmToViewM(.01))
                { cache[reference] = vertex; return vertex; }
            }
            if (reference.IsEnd || reference.IsTangent) return null;
            EdgeInfo second = reference.Second.Source as EdgeInfo;
            if (second == null) return null;
            var before = GetUserSketchPoints(view.GetSketch() as Sketch);
            VirtualSharpReference sharp = CreateNativeVirtualSharp(model, view, data, first, second);
            if (sharp == null || sharp.Point == null) return null;
            cache[reference] = sharp.Point;
            if (!before.Any(p => ReferenceEquals(p, sharp.Point) || p.Equals(sharp.Point))) createdPoints.Add(sharp.Point);
            return sharp.Point;
        }
        private bool SectionDimensionMatches(DisplayDimension display, double expected, int requiredType)
        {
            try
            {
                if (display == null) return false;
                int actualType = display.GetType();
                bool linear = requiredType == (int)swDimensionType_e.swLinearDimension;
                if (actualType != requiredType && !(linear && (actualType == (int)swDimensionType_e.swHorLinearDimension
                    || actualType == (int)swDimensionType_e.swVertLinearDimension))) return false;
                Annotation annotation = display.GetAnnotation() as Annotation;
                if (annotation == null || annotation.IsDangling()) return false;
                Dimension dimension = display.GetDimension2(0) as Dimension;
                if (dimension == null) return false;
                double actual = dimension.GetSystemValue2("");
                return Math.Abs(actual - expected) <= (requiredType == (int)swDimensionType_e.swAngularDimension ? .05 * Math.PI / 180 : .00001);
            }
            catch { return false; }
        }
        private static int CountViewDimensions(SolidWorks.Interop.sldworks.View view)
        {
            Array annotations = view.GetAnnotations() as Array;
            return annotations == null ? 0 : annotations.Cast<object>().OfType<Annotation>()
                .Count(a => a.GetType() == (int)swAnnotationType_e.swDisplayDimension);
        }
        private string SectionPointText(Profile.Point p)
        { return "(" + (p.X * 1000 / viewScale).ToString("0.###") + "," + (p.Y * 1000 / viewScale).ToString("0.###") + ")"; }
    }
}
