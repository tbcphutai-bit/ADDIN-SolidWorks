using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Environment = System.Environment;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using static ADDIN.Commands.CircleFaceTubeLayout.TubeLayoutSolver;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    internal sealed class CircleFaceTubeLayoutCommand
    {
        private readonly ISldWorks app;
        private readonly List<Body2> graphics = new List<Body2>();
        private ModelDoc2 model;
        private SelectionMgr selection;
        private Face2 face;
        private Modeler modeler;
        private Component2 component;
        private MathTransform placement;
        private double[] placementData;
        private MathUtility math;
        private string configuration;
        private string logError;

        private object DisplayContext => component == null ? (object)model : component;
        private StreamWriter writer;

        public CircleFaceTubeLayoutCommand(ISldWorks app) { this.app = app; }

        public void Run(IWin32Window owner)
        {
            string path = null;
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TAI_TOOL", "CircleFaceTubeLayout");
                Directory.CreateDirectory(folder);
                path = Path.Combine(folder, "TUBE_FACE_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + ".log");
                writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
                    new UTF8Encoding(false)) { AutoFlush = true };
                Log("START solver=2 addin=" + GetType().Assembly.FullName);
                Log("SOLIDWORKS revision=" + app?.RevisionNumber());
                model = app?.ActiveDoc as ModelDoc2;
                if (model == null || (model.GetType() != (int)swDocumentTypes_e.swDocPART &&
                    model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY))
                    throw new LayoutException("Open a Part or Assembly and select one sheet face.");
                configuration = model.IGetActiveConfiguration()?.Name;
                Log("DOCUMENT path=" + model.GetPathName() + " config=" + configuration);
                EnsureAssemblyEditTarget();
                if (model.SketchManager.ActiveSketch != null)
                    throw new LayoutException("Exit the active sketch first.");
                selection = (SelectionMgr)model.SelectionManager;
                if (selection.GetSelectedObjectCount2(-1) != 1 ||
                    selection.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelFACES)
                    throw new LayoutException("Select exactly one Part face.");
                face = selection.GetSelectedObject6(1, -1) as Face2;
                if (face == null) throw new LayoutException("The selected face is unavailable.");
                var reference = model.Extension.GetPersistReference3(face) as byte[];
                Log("FACE_REFERENCE base64=" + (reference == null ? "unavailable" : Convert.ToBase64String(reference)));
                modeler = app.IGetModeler();
                if (modeler == null) throw new LayoutException("SolidWorks Modeler is unavailable.");
                var geometry = new SwFaceLayout(FaceInPartCoordinates(), modeler, Log);

                using (var dialog = new CircleFaceTubeLayoutForm(path, geometry.Rails, (count, diameter, margin, flip) =>
                {
                    ClearPreview();
                    Log("PREVIEW_BEGIN");
                    try
                    {
                        EnsureModel();
                        TubeLayout layout = geometry.Analyze(count, diameter, margin, flip);
                        ShowPreview(geometry.Rails, layout);
                        Log("PREVIEW_END result=" + (layout.Issue ?? "verified"));
                        return layout;
                    }
                    catch (Exception ex)
                    {
                        Log("PREVIEW_FAILED " + ex);
                        ClearPreview();
                        throw;
                    }
                }))
                {
                    if (dialog.ShowDialog(owner) != DialogResult.OK)
                    {
                        Log("CANCEL");
                        return;
                    }
                    ClearPreview();
                    EnsureModel();
                    TubeLayout accepted = dialog.Accepted;
                    if (accepted == null || accepted.Issue != null)
                        throw new LayoutException("Only a verified preview can be committed.");
                    Feature created = CreateSketch(accepted);
                    Log("SUCCESS construction_axes=" + accepted.Axes.Length);
                    if (CircleFaceTubeLayoutForm.ShowCreated(owner, created.Name, accepted.Axes.Length, LogStatus()))
                    {
                        EnsureModel();
                        UndoSketch(created);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("FAILED " + ex);
                MessageBox.Show(owner, ex.Message + Environment.NewLine + "Debug: " + (path ?? "log unavailable") + LogStatus(),
                    "Circle Along Face", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                try { ClearPreview(); }
                finally
                {
                    try { writer?.Dispose(); }
                    catch (IOException ex) { System.Diagnostics.Trace.TraceError("Circle Along Face log close: " + ex); }
                }
            }
        }

        private Face2 FaceInPartCoordinates()
        {
            if (model.GetType() == (int)swDocumentTypes_e.swDocPART) return face;
            component = selection.GetSelectedObjectsComponent3(1, -1);
            var part = component?.GetModelDoc2() as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new LayoutException("Select a face on a resolved Part component.");
            if (component.GetParent() != null)
                throw new LayoutException("Select a top-level Part component, or open the subassembly or Part.");
            if (part.IGetActiveConfiguration()?.Name != component.ReferencedConfiguration)
                throw new LayoutException("Open the component Part in its referenced configuration, then run the command again.");
            placement = component.Transform2;
            placementData = placement?.ArrayData as double[];
            math = app.IGetMathUtility();
            if (math == null || placementData == null || placementData.Length != 16 ||
                Math.Abs(Math.Abs(placementData[12]) - 1) > 1e-9)
                throw new LayoutException("The component has no supported rigid placement.");
            Log("COMPONENT name=" + component.Name2 + " file=" + component.GetPathName() +
                " config=" + component.ReferencedConfiguration + " part_to_assembly=" +
                string.Join(",", placementData.Select(x => x.ToString("R", CultureInfo.InvariantCulture))));
            return part.Extension.GetCorrespondingEntity2(face) as Face2
                ?? throw new LayoutException("Could not map the component face to the Part. Open the Part and select its face.");
        }

        private void EnsureAssemblyEditTarget()
        {
            if (!(model is AssemblyDoc assembly)) return;
            Component2 target = assembly.GetEditTargetComponent();
            if (target != null && !target.IsRoot() && !ReferenceEquals(assembly.IGetEditTarget2(), model))
                throw new LayoutException("Exit Edit Component before creating an Assembly sketch.");
        }

        private void EnsureModel()
        {
            if (!ReferenceEquals(app.ActiveDoc, model) || model.SketchManager.ActiveSketch != null ||
                model.IGetActiveConfiguration()?.Name != configuration)
                throw new LayoutException("The active document, configuration, or sketch state changed. Run the command again.");
            EnsureAssemblyEditTarget();
            if (selection.GetSelectedObjectCount2(-1) != 1)
                throw new LayoutException("The selected face changed. Run the command again.");
            object current = selection.GetSelectedObject6(1, -1);
            if (!ReferenceEquals(current, face) && app.IsSame(current, face) != (int)swObjectEquality.swObjectSame)
                throw new LayoutException("The selected face changed. Run the command again.");
            if (component != null)
            {
                var currentPlacement = component.Transform2?.ArrayData as double[];
                if (currentPlacement == null || !placementData.SequenceEqual(currentPlacement))
                    throw new LayoutException("The component moved. Run the command again.");
            }
        }

        private Point3 ToDocument(Point3 point)
        {
            if (placement == null) return point;
            MathPoint local = math.CreatePoint(point.Array()) as MathPoint;
            if (local == null) throw new LayoutException("Could not create an Assembly preview point.");
            MathPoint transformed = null;
            try
            {
                transformed = local.IMultiplyTransform(placement);
                return Point3.Read(transformed?.ArrayData);
            }
            finally
            {
                SwFaceLayout.ReleaseTemporary(transformed);
                SwFaceLayout.ReleaseTemporary(local);
            }
        }

        private void ShowPreview(BoundaryRails rails, TubeLayout layout)
        {
            var lines = new List<Curve>();
            try
            {
                foreach (TubeAxis axis in layout.Axes) lines.Add(Line(axis.A, axis.B));
                Display(lines, layout.Issue == null ? Color.LimeGreen : Color.OrangeRed);
            }
            finally
            {
                foreach (Curve curve in lines) SwFaceLayout.ReleaseTemporary(curve);
            }

            DisplayRail(rails.Small, Color.DeepSkyBlue);
            DisplayRail(rails.Large, Color.Gold);
            model.GraphicsRedraw2();
        }

        private void DisplayRail(BoundaryRail rail, Color color)
        {
            var lines = new List<Curve>();
            try
            {
                Point3 start = rail.Start;
                for (int i = 1; i <= 64; i++)
                {
                    Point3 end = rail.At(rail.Length * i / 64);
                    lines.Add(Line(start, end));
                    start = end;
                }
                Display(lines, color);
            }
            finally
            {
                foreach (Curve curve in lines) SwFaceLayout.ReleaseTemporary(curve);
            }
        }

        private Curve Line(Point3 a, Point3 b)
        {
            a = ToDocument(a);
            b = ToDocument(b);
            Curve line = modeler.CreateLine(a.Array(), (b - a).Unit().Array()) as Curve;
            if (line == null) throw new LayoutException("Could not create a temporary preview line.");
            try
            {
                return line.CreateTrimmedCurve2(a.X, a.Y, a.Z, b.X, b.Y, b.Z)
                    ?? throw new LayoutException("Could not trim a temporary preview line.");
            }
            finally { SwFaceLayout.ReleaseTemporary(line); }
        }

        private void Display(List<Curve> lines, Color color)
        {
            int rgb = ColorTranslator.ToWin32(color);
            foreach (Curve curve in lines)
            {
                Body2 body = curve.CreateWireBody();
                if (body == null) throw new LayoutException("Could not create a temporary preview wire.");
                graphics.Add(body);
                int result = body.Display3(DisplayContext, rgb, (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
                if (result != 0) throw new LayoutException("Could not display the preview (error " + result + ").");
            }
        }

        private void ClearPreview()
        {
            if (graphics.Count == 0) return;
            foreach (Body2 body in graphics)
            {
                try { body.Hide(DisplayContext); }
                catch (Exception ex) { Log("PREVIEW_HIDE_FAILED " + ex); }
                finally { SwFaceLayout.ReleaseTemporary(body); }
            }
            graphics.Clear();
            try { model.GraphicsRedraw2(); }
            catch (Exception ex) { Log("REDRAW_FAILED " + ex); }
        }

        private Feature CreateSketch(TubeLayout layout)
        {
            if (model.IsOpenedReadOnly()) throw new LayoutException("The Part is open read-only.");
            var manager = model.SketchManager;
            var extension = model.Extension;
            bool add = manager.AddToDB, display = manager.DisplayWhenAdded;
            int inferencePreference = (int)swUserPreferenceToggle_e.swSketchInference;
            bool inference = app.GetUserPreferenceToggle(inferencePreference);
            bool suspended = false, recording = false;
            Sketch sketch = null;
            Feature feature = null;
            try
            {
                selection.SuspendSelectionList();
                suspended = true;
                extension.StartRecordingUndoObject();
                recording = true;
                manager.Insert3DSketch(false);
                sketch = manager.ActiveSketch;
                if (sketch == null || !sketch.Is3D())
                    throw new LayoutException("SolidWorks did not start a new 3D sketch.");
                // Direct database writes can suppress sketch Undo. Use the
                // normal sketch path and check the resulting coordinates.
                manager.AddToDB = false;
                if (inference) app.SetUserPreferenceToggle(inferencePreference, false);
                foreach (TubeAxis axis in layout.Axes)
                {
                    var a = ToDocument(axis.A);
                    var b = ToDocument(axis.B);
                    Log(F("COMMIT_AXIS A_doc_m={0} B_doc_m={1}", a, b));
                    SketchSegment line = manager.CreateLine(a.X, a.Y, a.Z, b.X, b.Y, b.Z);
                    if (!(line is SketchLine created))
                        throw new LayoutException("SolidWorks did not create a 3D sketch line.");
                    line.ConstructionGeometry = true;
                    if (!line.ConstructionGeometry || !MatchesEndpoints(created, a, b))
                        throw new LayoutException("SolidWorks changed a construction axis during creation.");
                }
                string name = "TUBE_AXES_3D_" + layout.Axes.Length;
                if (model.FeatureManager.IsNameUsed((int)swNameType_e.swFeatureName, name))
                    name += "_" + DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture);
                feature = FindSketch(sketch);
                if (feature != null) feature.Name = name;
                manager.AddToDB = add;
                manager.DisplayWhenAdded = display;
                manager.Insert3DSketch(false);
                if (manager.ActiveSketch != null) throw new LayoutException("Could not exit the new 3D sketch.");
                if (feature == null)
                {
                    feature = FindSketch(sketch);
                    if (feature == null) throw new LayoutException("Could not identify the new sketch feature.");
                    feature.Name = name;
                }
                bool grouped = extension.FinishRecordingUndoObject2("Circle Along Face", false);
                recording = false;
                Log("UNDO native_group=" + grouped);
                Log("COMMIT sketch=" + feature.Name);
                return feature;
            }
            catch
            {
                try
                {
                    manager.AddToDB = add;
                    manager.DisplayWhenAdded = display;
                    if (sketch != null && manager.ActiveSketch != null) manager.Insert3DSketch(false);
                    bool recorded = recording && extension.FinishRecordingUndoObject2("Circle Along Face (rollback)", false);
                    recording = false;
                    if (sketch != null)
                    {
                        feature = feature ?? FindSketch(sketch);
                        if (recorded)
                        {
                            model.EditUndo2(1);
                            if (FindSketch(sketch) != null) throw new LayoutException("Undo did not remove the partial sketch.");
                        }
                        else
                        {
                            DeleteSketch(feature);
                        }
                        Log("ROLLBACK completed");
                    }
                }
                catch (Exception ex)
                {
                    Log("ROLLBACK_FAILED " + ex);
                    throw new LayoutException("Sketch creation failed and rollback could not be verified. Check the feature tree and debug log.");
                }
                throw;
            }
            finally
            {
                manager.AddToDB = add;
                manager.DisplayWhenAdded = display;
                if (inference) app.SetUserPreferenceToggle(inferencePreference, true);
                if (suspended) selection.ResumeSelectionList2(false);
                model.GraphicsRedraw2();
            }
        }

        private void UndoSketch(Feature feature)
        {
            selection.SuspendSelectionList();
            try
            {
                DeleteSketch(feature);
                Log("UNDO sketch_removed=true");
            }
            finally
            {
                selection.ResumeSelectionList2(false);
                model.GraphicsRedraw2();
            }
        }

        private void DeleteSketch(Feature feature)
        {
            model.ClearSelection2(true);
            if (feature == null || feature.GetTypeName2() != "3DProfileFeature" ||
                !feature.Select2(false, 0) || !model.Extension.DeleteSelection2(0))
                throw new LayoutException("Could not remove the command's sketch.");
        }

        private bool MatchesEndpoints(SketchLine line, Point3 a, Point3 b)
        {
            var start = line.GetStartPoint2() as SketchPoint;
            var end = line.GetEndPoint2() as SketchPoint;
            if (start == null || end == null) return false;
            var p = new Point3(start.X, start.Y, start.Z);
            var q = new Point3(end.X, end.Y, end.Z);
            const double tolerance = 1e-7;
            bool matches = ((p - a).Length <= tolerance && (q - b).Length <= tolerance) ||
                ((q - a).Length <= tolerance && (p - b).Length <= tolerance);
            if (!matches) Log(F("SKETCH_MISMATCH expected_A_m={0} expected_B_m={1} actual_A_m={2} actual_B_m={3}", a, b, p, q));
            return matches;
        }

        private Feature FindSketch(Sketch sketch)
        {
            for (int i = 0; ; i++)
            {
                Feature feature = model.IFeatureByPositionReverse(i);
                if (feature == null) return null;
                if (feature.GetTypeName2() == "3DProfileFeature" && ReferenceEquals(feature.GetSpecificFeature2(), sketch))
                    return feature;
            }
        }

        private string LogStatus() => logError == null ? "" : Environment.NewLine + "Debug log write failed: " + logError;

        private void Log(string message)
        {
            if (logError != null) return;
            try { writer?.WriteLine(DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + " " + message); }
            catch (IOException ex)
            {
                logError = ex.Message;
                System.Diagnostics.Trace.TraceError("Circle Along Face log write: " + ex);
            }
        }
    }
}
