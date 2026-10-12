using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    // Standard pink RGB(255,192,192) face colour. The staging Part is a copy of
    // the source, so its faces keep the source's colours by face identity; after
    // reflection that is the wrong hand. Every source pink face is located on
    // the mirrored body by surface type, reflected bounding box and area, and
    // must match exactly one face. Stale pink on any other face is removed.
    internal static class StandardPinkFaces71
    {
        internal sealed class PinkFace
        {
            internal int Surface;
            internal double Area;
            internal double[] ReflectedBox, Material;
            // Points on the source face, reflected: the box of a re-built spline face is not
            // tight, so two equal faces can report boxes that differ by more than a millimetre.
            internal List<double[]> ReflectedPoints = new List<double[]>();
            // The source face itself, as a persistent reference: in the mirrored Part the
            // face with that identity inherits the colour, wherever it now lies.
            internal byte[] SourceIdentity;
        }

        private static bool IsStandardPink(double[] m)
        {
            return m != null && m.Length >= 3 && Math.Abs(m[0] - 1.0) <= 0.02 &&
                Math.Abs(m[1] - 192.0 / 255.0) <= 0.02 && Math.Abs(m[2] - 192.0 / 255.0) <= 0.02;
        }

        private static List<Face2> Faces(ModelDoc2 model)
        {
            var bodies = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];
            return bodies.Cast<Body2>().SelectMany(b => (b.GetFaces() as object[] ?? new object[0]).Cast<Face2>()).ToList();
        }

        private static double[] Material(Face2 face)
        {
            if (!face.HasMaterialPropertyValues()) return null;
            return face.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[] ??
                face.MaterialPropertyValues as double[];
        }

        internal static List<PinkFace> CaptureSource(MirrorInPlaceExecutionContextV7 context)
        {
            var result = new List<PinkFace>();
            foreach (Face2 face in Faces(context.SourceDocument))
            {
                double[] material = Material(face);
                double[] box = face.GetBox() as double[];
                if (!IsStandardPink(material) || box == null || box.Length < 6) continue;
                double[] a = context.Reflection.ReflectPoint(new[] { box[0], box[1], box[2] });
                double[] b = context.Reflection.ReflectPoint(new[] { box[3], box[4], box[5] });
                var pink = new PinkFace
                {
                    Surface = ((Surface)face.GetSurface()).Identity(), Area = face.GetArea(), Material = (double[])material.Clone(),
                    ReflectedBox = new[] { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Min(a[2], b[2]),
                        Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Max(a[2], b[2]) }
                };
                try { pink.SourceIdentity = context.SourceDocument.Extension.GetPersistReference3(face) as byte[]; }
                catch (System.Runtime.InteropServices.COMException) { pink.SourceIdentity = null; }
                var surface = (Surface)face.GetSurface();
                double[] uv = face.GetUVBounds() as double[];
                if (uv != null && uv.Length >= 4)
                    for (int i = 1; i <= 5; i++)
                        for (int j = 1; j <= 5; j++)
                        {
                            var raw = surface.Evaluate(uv[0] + (uv[1] - uv[0]) * i / 6.0, uv[2] + (uv[3] - uv[2]) * j / 6.0, 0, 0) as double[];
                            var on = raw == null || raw.Length < 3 ? null : face.GetClosestPointOn(raw[0], raw[1], raw[2]) as double[];
                            if (on != null && on.Length >= 3) pink.ReflectedPoints.Add(context.Reflection.ReflectPoint(new[] { on[0], on[1], on[2] }));
                        }
                result.Add(pink);
            }
            MirrorV7Diagnostics.Log("[PINK71][SOURCE] standardPinkFaces=" + result.Count + " rgb=255,192,192");
            return result;
        }

        private static List<Face2> Matches(List<Face2> faces, PinkFace pink)
        {
            var exact = faces.Where(face =>
            {
                double[] box = face.GetBox() as double[];
                return box != null && box.Length >= 6 && ((Surface)face.GetSurface()).Identity() == pink.Surface &&
                    Enumerable.Range(0, 6).All(i => Math.Abs(box[i] - pink.ReflectedBox[i]) <= 1e-6) &&
                    Math.Abs(face.GetArea() - pink.Area) <= Math.Max(1e-12, pink.Area * 1e-4);
            }).ToList();
            if (exact.Count > 0) return exact;
            // A natively re-folded face can come back as another surface type,
            // with a box and an area that moved by numerical noise (native: area
            // 1e-4 on a 44 m radius bend). Appearance only, and the caller still
            // demands exactly one face: same planar/curved kind, box within
            // 1e-4 m, area within 1e-3.
            bool planar = pink.Surface == (int)swSurfaceTypes_e.PLANE_TYPE;
            var loose = faces.Where(face =>
            {
                double[] box = face.GetBox() as double[];
                return box != null && box.Length >= 6 &&
                    (((Surface)face.GetSurface()).Identity() == (int)swSurfaceTypes_e.PLANE_TYPE) == planar &&
                    Enumerable.Range(0, 6).All(i => Math.Abs(box[i] - pink.ReflectedBox[i]) <= 1e-4) &&
                    Math.Abs(face.GetArea() - pink.Area) <= Math.Max(1e-12, pink.Area * 1e-3);
            }).ToList();
            MirrorV7Diagnostics.Log("[PINK71][LOOSE_MATCH] surface=" + pink.Surface + " area_mm2=" + (pink.Area * 1e6).ToString("F3") +
                " candidates=" + loose.Count + " limits=box1E-4,area1E-3");
            if (loose.Count > 0 || pink.ReflectedPoints.Count < 9) return loose;
            // No box agrees: the face is found by where it lies. Same planar/curved kind,
            // area within 1e-3, and every reflected point of the source face on it within
            // 1e-4 m (the other side of the sheet is a whole thickness away).
            var located = faces.Where(face =>
                (((Surface)face.GetSurface()).Identity() == (int)swSurfaceTypes_e.PLANE_TYPE) == planar &&
                Math.Abs(face.GetArea() - pink.Area) <= Math.Max(1e-12, pink.Area * 1e-3) &&
                pink.ReflectedPoints.All(p =>
                {
                    var near = face.GetClosestPointOn(p[0], p[1], p[2]) as double[];
                    return near != null && near.Length >= 3 && Math.Sqrt((near[0] - p[0]) * (near[0] - p[0]) +
                        (near[1] - p[1]) * (near[1] - p[1]) + (near[2] - p[2]) * (near[2] - p[2])) <= 1e-4;
                })).ToList();
            MirrorV7Diagnostics.Log("[PINK71][POINT_MATCH] surface=" + pink.Surface + " points=" + pink.ReflectedPoints.Count +
                " candidates=" + located.Count + " limits=point1E-4,area1E-3");
            return located;
        }

        internal static void Apply(ModelDoc2 model, List<PinkFace> expected)
        {
            var faces = Faces(model);
            int removed = 0;
            foreach (Face2 face in faces.Where(f => IsStandardPink(Material(f))))
            {
                face.RemoveMaterialProperty2((int)swInConfigurationOpts_e.swAllConfiguration, null);
                removed++;
            }
            // Also by identity, without asking the face for its colour first: natively
            // the inherited face once reported no colour here and was pink again after
            // the Part was saved and reopened (two pink faces).
            int removedByIdentity = 0;
            foreach (PinkFace pink in expected)
            {
                if (pink.SourceIdentity == null || pink.SourceIdentity.Length == 0) continue;
                int state;
                Face2 inherited = null;
                try { inherited = model.Extension.GetObjectByPersistReference3(pink.SourceIdentity, out state) as Face2; }
                catch (System.Runtime.InteropServices.COMException) { inherited = null; }
                if (inherited == null) continue;
                inherited.RemoveMaterialProperty2((int)swInConfigurationOpts_e.swAllConfiguration, null);
                removedByIdentity++;
            }
            var used = new List<Face2>();
            foreach (PinkFace pink in expected)
            {
                var candidates = Matches(faces, pink).Where(f => !used.Contains(f)).ToList();
                if (candidates.Count != 1)
                    throw new InvalidOperationException("PINK71 source pink face has no unique mirrored face. candidates=" +
                        candidates.Count + " surface=" + pink.Surface + " area_mm2=" + (pink.Area * 1e6).ToString("F3"));
                candidates[0].SetMaterialPropertyValues2(pink.Material, (int)swInConfigurationOpts_e.swAllConfiguration, null);
                if (!IsStandardPink(Material(candidates[0])))
                    throw new InvalidOperationException("PINK71 cannot set the pink colour on the mirrored face.");
                used.Add(candidates[0]);
            }
            MirrorV7Diagnostics.Log("[PINK71][APPLY] sourcePink=" + expected.Count + " inheritedRemoved=" + removed + " removedByIdentity=" + removedByIdentity +
                " mapped=" + used.Count + " match=SURFACE_TYPE_REFLECTED_BOX_AREA result=PASS");
        }

        internal static void Verify(ModelDoc2 model, List<PinkFace> expected)
        {
            var faces = Faces(model);
            var pinkNow = faces.Where(f => IsStandardPink(Material(f))).ToList();
            int located = expected.Count(pink => Matches(pinkNow, pink).Count == 1);
            bool pass = pinkNow.Count == expected.Count && located == expected.Count;
            MirrorV7Diagnostics.Log("[PINK71][VERIFY_AFTER_REOPEN] expected=" + expected.Count + " pinkFaces=" + pinkNow.Count +
                " onMirroredFace=" + located + " result=" + (pass ? "PASS" : "FAIL"));
            if (!pass) throw new InvalidOperationException("PINK71 pink faces are not on the mirrored faces after reopening. expected=" +
                expected.Count + " found=" + pinkNow.Count + " located=" + located);
        }
    }

    // Native SOLIDWORKS status-bar progress. Never fails the mirror: a progress
    // API problem is ignored. Steps are stage counts, not time estimates.
    public static class MirrorProgress71
    {
        private static UserProgressBar bar;
        private static int total, position;
        // An outer command (Mirror Assembly) owns the bar: the Part mirror
        // inside it then only updates the title and cannot restart or end it.
        private static bool owned;
        private static string ownerTitle = "";

        public static void Start(ISldWorks app, int steps, string title)
        {
            if (owned) return;
            try
            {
                End();
                UserProgressBar created;
                if (app == null || !app.GetUserProgressBar(out created) || created == null) return;
                total = Math.Max(1, steps); position = 0; bar = created;
                bar.Start(0, total, title);
            }
            catch { bar = null; }
        }

        public static void Step(string title)
        {
            if (bar == null) return;
            try
            {
                if (owned) { bar.UpdateTitle(ownerTitle + " - " + title); return; }
                position = Math.Min(position + 1, total);
                bar.UpdateTitle(title + " (" + position + "/" + total + ")");
                bar.UpdateProgress(position);
            }
            catch { }
        }

        public static void End()
        {
            if (owned) return;
            try { if (bar != null) bar.End(); } catch { }
            bar = null;
        }

        public static void StartOwned(ISldWorks app, int steps, string title)
        {
            owned = false;
            Start(app, steps, title);
            owned = true; ownerTitle = title;
        }

        public static void StepOwned(string title)
        {
            if (bar == null) return;
            try
            {
                position = Math.Min(position + 1, total);
                ownerTitle = title + " (" + position + "/" + total + ")";
                bar.UpdateTitle(ownerTitle);
                bar.UpdateProgress(position);
            }
            catch { }
        }

        // `callNative` false after a native fault: no further CAD call is made.
        public static void EndOwned(bool callNative)
        {
            owned = false;
            if (callNative) End(); else bar = null;
        }
    }
    public static class MirrorInPlaceOrchestratorV7
    {
        public const string Version = "V7-PARAMETRIC-PREFLIGHT-70-DEPENDENCY-CONFIGURATION";

        // Reuse the original cache/diagnose/replay pipeline without inventing an
        // assembly component or changing the user's reflection plane.
        // Two tiers for a Part with an in-context surface chain: first the chain
        // is re-attached feature by feature (it keeps driving the cut); when
        // that cannot be completed, the whole mirror is run again from the
        // untouched source with a fixed reflected cutting surface per Cut With
        // Surface, so that the cut rebuilds without error.
        public static MirrorInPlacePreparationResultV7 ExecuteLegacyPart(ISldWorks app, ModelDoc2 source,
            CanonicalPartMirrorPlaneV7 plane, string requestedOutputPath)
        {
            RollbackReplayEngineV7.FixedCutToolMode71 = false;
            try { return ExecuteLegacyPartCore71(app, source, plane, requestedOutputPath); }
            catch (Exception first)
            {
                // A run that already used the fixed-surface tier (Lofted Bend base) is not repeated.
                bool alreadyFixedTier = RollbackReplayEngineV7.FixedCutToolMode71;
                RollbackReplayEngineV7.FixedCutToolMode71 = false;
                if (alreadyFixedTier || RollbackReplayEngineV7.IsUnsafeNativeFailure66(first) || !RollbackReplayEngineV7.SurfaceChainSeen71) throw;
                MirrorV7Diagnostics.Log("[SURFCHAIN71][SECOND_TIER] reason=" + (first.Message ?? "").Split('\r', '\n')[0] +
                    " action=RERUN_WITH_FIXED_CUTTING_SURFACE");
                RollbackReplayEngineV7.FixedCutToolMode71 = true;
                try { return ExecuteLegacyPartCore71(app, source, plane, requestedOutputPath); }
                finally { RollbackReplayEngineV7.FixedCutToolMode71 = false; }
            }
        }

        private static MirrorInPlacePreparationResultV7 ExecuteLegacyPartCore71(ISldWorks app, ModelDoc2 source,
            CanonicalPartMirrorPlaneV7 plane, string requestedOutputPath)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (source == null || source.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Mirror requires a saved Part.");
            var baseline = MirrorSourceBaselineV7.Capture(source);
            requestedOutputPath = Path.GetFullPath(requestedOutputPath);
            if (!string.Equals(Path.GetExtension(requestedOutputPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("File ket qua phai co duoi .SLDPRT: " + requestedOutputPath);
            if (string.Equals(requestedOutputPath, baseline.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Khong duoc ghi de Part goc. Hay chon ten file mirror moi.");
            if (File.Exists(requestedOutputPath))
                throw new InvalidOperationException("File ket qua da ton tai. Hay chon ten moi: " + requestedOutputPath);

            string runDirectory = Path.Combine(Path.GetDirectoryName(requestedOutputPath),
                "Mirror_Diagnostics", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runDirectory);
            string candidatePath = Path.Combine(runDirectory, "Mirror_Candidate.SLDPRT");
            string reportPath = Path.Combine(runDirectory, "MirrorVerification.xml");
            bool unsafeNativeFailure66 = false;
            try
            {
                MirrorV7Diagnostics.Log("[INPLACE][START] version=" + Version +
                    " mode=STANDALONE_LEGACY_CORE plane=" + plane);
                var graph = FeatureTreeScannerV7.Scan(source);
                foreach (var node in graph.Nodes)
                    if (node != null && node.Role != MirrorV7FeatureRole.System)
                        node.PersistentReference = PersistentReferenceServiceV7.Capture(
                            source, node.Feature, node.Name, "INPLACE_SOURCE_FEATURE");
                var context = new MirrorInPlaceExecutionContextV7
                {
                    SwApp = app, SourceDocument = source, WorkingDocument = source,
                    SourceBaseline = baseline, SourceGraph = graph,
                    Reflection = PartReflectionTransformV7.CreateCanonical(plane),
                    Plane = plane, RequestedOutputPath = requestedOutputPath
                };
                // One step per top-level body-changing feature plus five stages.
                MirrorProgress71.Start(app, 5 + graph.Nodes.Count(n => n.Depth == 0 && !n.IsSuppressed &&
                    (n.Role == MirrorV7FeatureRole.BodyFeature || n.Role == MirrorV7FeatureRole.SheetMetal ||
                     n.Role == MirrorV7FeatureRole.Pattern) && n.TypeName != "SheetMetal" && n.TypeName != "FlatPattern"),
                    "Mirror Part");
                MirrorProgress71.Step("Mirror Part: doc Part nguon");
                var oracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context, "INPUT_FULL_PART_BEFORE_STAGING");
                string otherConfigurations71 = "KHONG CO";
                var pinkFaces71 = StandardPinkFaces71.CaptureSource(context);
                var result = SheetMetalMirrorServiceV6.ExecuteV6MirrorPipeline(app, null, null,
                    new ExplicitSavePathProvider(candidatePath), source,
                    new PlaneData { Origin = context.Reflection.Origin, Normal = context.Reflection.Normal },
                    copy =>
                    {
                        context.WorkingDocument = copy;
                        MirrorProgress71.Step("Mirror Part: kiem tra hinh hoc va cay feature");
                        EnsureBaselineConfiguration71(copy, baseline.ConfigurationName, "FINAL_VERIFY");
                        RollbackReplayEngineV7.PrepareFlatFixedFaces71(copy, null);
                        RestoreFlatPatternState71(context.SourceGraph, copy, "FINAL_VERIFY");
                        BreakExternalReferences71(copy);
                        BaseSketchMutationEngineV7.VerifyCurrentSolids(context, oracle);
                        // What exists in another configuration only (flat-pattern holes,
                        // cuts, a Boss): proved there too, then back to this one.
                        if (RollbackReplayEngineV7.HasOtherConfigurations71)
                        {
                            List<string> failed71;
                            try { failed71 = RollbackReplayEngineV7.VerifyOtherConfigurations71(context, copy); }
                            finally
                            {
                                EnsureBaselineConfiguration71(copy, baseline.ConfigurationName, "AFTER_OTHER_CONFIGURATIONS");
                                RestoreFlatPatternState71(context.SourceGraph, copy, "AFTER_OTHER_CONFIGURATIONS");
                            }
                            otherConfigurations71 = failed71.Count == 0 ? "PASS" : "CHUA DUNG - " + string.Join(" | ", failed71);
                            // The other configurations were shown and edited: the working one is proved once more.
                            BaseSketchMutationEngineV7.VerifyCurrentSolids(context, oracle);
                        }
                        RemoveEmptyCutListItems71(context, copy);
                        RegisterRegeneratedUnderRedrawnProfile71(context, copy);
                        ValidateCompleteFeatureTree(context, copy);
                        // Appearance only, after geometry and tree are proved;
                        // the pipeline saves the staging Part right after this.
                        StandardPinkFaces71.Apply(copy, pinkFaces71);
                        RollbackReplayEngineV7.HideLeftoverFixedSurfaces71(copy);
                        baseline.AssertUnchanged(source);
                    }, (replacement, copy) => RegisterNativeReplacement(context, replacement, copy),
                    replay => ImportFlangeProfileMappings50(context, replay));
                if (!result.Success)
                    throw new InvalidOperationException(result.Message ?? "Legacy feature replay did not produce a verified Part.");
                context.WorkingPath = result.MirrorPartPath;
                MirrorProgress71.Step("Mirror Part: luu, mo lai va kiem tra");
                VerifyPublishedOutput(app, context, oracle, reopened => StandardPinkFaces71.Verify(reopened, pinkFaces71));
                MirrorProgress71.Step("Mirror Part: xuat file");
                baseline.AssertUnchanged(source);
                new XDocument(new XElement("MirrorVerification",
                    new XAttribute("version", Version), new XAttribute("status", "VERIFIED_BEFORE_PUBLICATION"),
                    new XElement("Source", baseline.SourcePath), new XElement("Output", requestedOutputPath),
                    new XElement("Plane", plane), new XElement("Geometry", "PASS"),
                    new XElement("FeatureIdentity", "PASS"), new XElement("Independence", "PASS"),
                    new XElement("OtherConfigurations", otherConfigurations71)))
                    .Save(reportPath);
                File.Copy(candidatePath, requestedOutputPath, false);
                MirrorV7Diagnostics.Log("[INPLACE][OUTPUT_PUBLISHED] path=" + requestedOutputPath);
                MirrorV7Diagnostics.Log("[INPLACE][OTHER_CONFIGURATIONS] " + otherConfigurations71);
                MirrorV7Diagnostics.Log("[INPLACE][ACCEPTED_LOCAL_DEVIATION] largest_um=" + (ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71 * 1e6).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " limit_um=" +
                    (ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71 * 1e6).ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                return new MirrorInPlacePreparationResultV7
                {
                    WorkingPath = candidatePath, ReportPath = reportPath, RequestedOutputPath = requestedOutputPath,
                    FeatureCount = graph.Nodes.Count, SourceUnchanged = true, MutationExecuted = true,
                    BaseGeometryVerified = true, OutputPublished = true, OtherConfigurations = otherConfigurations71
                };
            }
            catch (Exception ex)
            {
                unsafeNativeFailure66 = RollbackReplayEngineV7.IsUnsafeNativeFailure66(ex);
                if (ex is ADDIN.Helpers.BodyValidationUnavailable69 && !unsafeNativeFailure66)
                    new XDocument(new XElement("MirrorVerification",
                        new XAttribute("version", Version), new XAttribute("status", "VERIFICATION_UNAVAILABLE"),
                        new XElement("Source", baseline.SourcePath), new XElement("Output", requestedOutputPath),
                        new XElement("OutputPublished", false), new XElement("Geometry", "NOT_PROVED"),
                        new XElement("Reason", ex.Message))).Save(reportPath);
                MirrorV7Diagnostics.Log("[INPLACE][LEGACY_CORE_FAILED] outputNotPublished=True reason=" + ex.Message +
                    " diagnostics=" + runDirectory);
                throw;
            }
            finally
            {
                if (unsafeNativeFailure66)
                    MirrorV7Diagnostics.Log("[INPLACE66][NATIVE_STOP] cleanupCadCalls=0 sourceLiveAuditSkipped=True sourceFileWrites=0");
                else
                {
                    MirrorProgress71.End();
                    Activate(app, source);
                    baseline.AssertUnchanged(source);
                }
            }
        }

        public static MirrorInPlacePreparationResultV7 Prepare(ISldWorks app, ModelDoc2 source,
            CanonicalPartMirrorPlaneV7 plane, string requestedOutputPath)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (source == null || source.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("In-place mirror requires a saved Part document.");

            MirrorSourceBaselineV7 baseline = MirrorSourceBaselineV7.Capture(source);
            requestedOutputPath = Path.GetFullPath(requestedOutputPath);
            if (!string.Equals(Path.GetExtension(requestedOutputPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedOutputPath, baseline.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output must be a new .SLDPRT path and cannot overwrite the source Part.");
            if (File.Exists(requestedOutputPath))
                throw new InvalidOperationException("Output already exists. Choose a new file name.");

            MirrorWorkingCopyV7 working = null;
            ModelDoc2 copy = null;
            string reportPath = null;
            bool outputCreatedByRun = false;
            bool unsafeNativeFailure66 = false;
            try
            {
                MirrorV7Diagnostics.Log("[INPLACE][START] version=" + Version + " source=\"" + baseline.SourcePath +
                    "\" plane=" + plane + " mirrorPartApi=False mirrorBodyApi=False");
                MirrorV7ModelGraph sourceGraph = FeatureTreeScannerV7.Scan(source);
                // The scanner deliberately does not retain document-scoped identity data.
                // Freeze it now, while every node still belongs to the source document, so
                // the byte-for-byte staging copy can be matched without using localized names.
                foreach (MirrorV7FeatureNode node in sourceGraph.Nodes)
                {
                    if (node == null || node.Role == MirrorV7FeatureRole.System) continue;
                    node.PersistentReference = PersistentReferenceServiceV7.Capture(
                        source, node.Feature, node.Name, "INPLACE_SOURCE_FEATURE");
                }
                BaseFeaturePrescriptionV7 prescription = BaseFeatureDiagnosticEngineV7.Analyze(sourceGraph);
                working = MirrorWorkingCopyV7.Create(app, baseline);
                copy = working.Document;
                Activate(app, copy);
                MirrorV7ModelGraph workingGraph = FeatureTreeScannerV7.Scan(copy);
                CrossDocumentReferenceResultV7 identity = CrossDocumentReferenceAuditV7.Run(sourceGraph, copy);
                if (!identity.Success)
                    throw new InvalidOperationException("The staging Part does not preserve source feature identities.");

                MirrorInPlaceExecutionContextV7 context = new MirrorInPlaceExecutionContextV7
                {
                    SwApp = app,
                    SourceDocument = source,
                    WorkingDocument = copy,
                    SourceBaseline = baseline,
                    SourceGraph = sourceGraph,
                    WorkingGraph = workingGraph,
                    Reflection = PartReflectionTransformV7.CreateCanonical(plane),
                    Plane = plane,
                    WorkingPath = working.WorkingPath,
                    RequestedOutputPath = requestedOutputPath,
                    BasePrescription = prescription
                };
                // Capture the complete source result before moving the rollback bar.  This is
                // the authoritative publication oracle: downstream features are allowed to
                // follow their reflected parents naturally, but a file is never published
                // unless the complete rebuilt body set is the true opposite hand.
                List<Body2> finalBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context);
                var sequence = RollbackReplayEngineV7.CaptureAll(context);
                if (!prescription.SupportedBaseType)
                    throw new InvalidOperationException("Base feature is not supported for editable in-place reflection: " +
                        prescription.FeatureName + " [" + prescription.FeatureType + "].");
                context.BaseMutation = BaseSketchMutationEngineV7.Execute(context);
                if (context.BaseMutation == null || !context.BaseMutation.BaseGeometryVerified)
                    throw new InvalidOperationException("Reflected Base feature did not pass its geometry oracle.");

                FeatureReplayJournalV7 journal = RollbackReplayEngineV7.ExecuteAll(context, sequence);
                SemanticValidatorV7.AssertPublishable(journal);
                foreach (FeatureReplayCheckpointV7 item in sequence.Where(x => x.WasNativeReplacement))
                    RegisterNativeReplacement(context, item.Feature, context.WorkingDocument);
                foreach (var item in context.Dependencies64.Recipes.Values.Where(x => x.WasNativeReplacement && !sequence.Contains(x)))
                    RegisterNativeReplacement(context, item.Feature, context.WorkingDocument);

                RestoreRollbackToEndAndRebuild(context);
                BaseSketchMutationEngineV7.VerifyCurrentSolids(context, finalBodyOracle);
                ValidateCompleteFeatureTree(context, context.WorkingDocument);
                reportPath = WriteReport(context, journal, working.RunId);
                baseline.AssertUnchanged(source);

                SaveWorkingDocument(context.WorkingDocument);
                app.CloseDoc(context.WorkingDocument.GetTitle());
                copy = null;
                VerifyPublishedOutput(app, context, finalBodyOracle);
                baseline.AssertUnchanged(source);
                File.Copy(working.WorkingPath, requestedOutputPath, false);
                outputCreatedByRun = true;
                MirrorV7Diagnostics.Log("[INPLACE][OUTPUT_PUBLISHED] path=" + requestedOutputPath);
                MirrorV7Diagnostics.Log("[INPLACE][ACCEPTED_LOCAL_DEVIATION] largest_um=" + (ADDIN.Helpers.NativeBodyCoincidence67.LargestAcceptedDeviation71 * 1e6).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " limit_um=" +
                    (ADDIN.Helpers.NativeBodyCoincidence67.LocalDeviationLimit71 * 1e6).ToString("F0", System.Globalization.CultureInfo.InvariantCulture));

                int planned = journal.Entries.Count(x => x.Status == MirrorV7ReplayStatus.NotProcessed);
                int unsupported = journal.Entries.Count(x => x.Status == MirrorV7ReplayStatus.Unsupported);
                MirrorV7Diagnostics.Log("[INPLACE][PREPARED] working=\"" + working.WorkingPath +
                    "\" report=\"" + reportPath + "\" features=" + journal.Entries.Count +
                    " planned=" + planned + " unsupported=" + unsupported +
                    " mutationExecuted=" + (context.BaseMutation != null) + " outputPublished=True output=\"" +
                    requestedOutputPath + "\"");
                return new MirrorInPlacePreparationResultV7
                {
                    WorkingPath = working.WorkingPath,
                    ReportPath = reportPath,
                    RequestedOutputPath = requestedOutputPath,
                    FeatureCount = context.SourceGraph.Count,
                    PlannedHandlerCount = planned,
                    UnsupportedCount = unsupported,
                    SourceUnchanged = true,
                    MutationExecuted = context.BaseMutation != null,
                    BaseGeometryVerified = context.BaseMutation != null && context.BaseMutation.BaseGeometryVerified,
                    OutputPublished = true
                };
            }
            catch (Exception ex)
            {
                unsafeNativeFailure66 = RollbackReplayEngineV7.IsUnsafeNativeFailure66(ex);
                if (outputCreatedByRun && File.Exists(requestedOutputPath))
                {
                    try { File.Delete(requestedOutputPath); }
                    catch (Exception cleanupError)
                    {
                        MirrorV7Diagnostics.Log("[INPLACE][OUTPUT_CLEANUP_WARNING] path=\"" +
                            requestedOutputPath + "\" message=" + cleanupError.Message);
                    }
                }
                throw;
            }
            finally
            {
                if (unsafeNativeFailure66)
                    MirrorV7Diagnostics.Log("[INPLACE66][NATIVE_STOP] cleanupCadCalls=0 closeSkipped=True sourceLiveAuditSkipped=True sourceFileWrites=0");
                else
                {
                    if (copy != null)
                    {
                        try { app.CloseDoc(copy.GetTitle()); }
                        catch (Exception ex) { MirrorV7Diagnostics.Log("[INPLACE][CLOSE_WARNING] " + ex.Message); }
                    }
                    baseline.AssertUnchanged(source);
                    Activate(app, source);
                }
            }
        }

        private static string WriteReport(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayJournalV7 journal, string runId)
        {
            XElement root = new XElement("MirrorInPlacePreparation",
                new XAttribute("version", Version),
                new XAttribute("runId", runId),
                new XAttribute("status", "GEOMETRY_VERIFIED_PENDING_SAVE_AND_REOPEN"),
                new XAttribute("mirrorPartApi", false),
                new XAttribute("mirrorFeatureApi", false),
                new XAttribute("mirrorBodyApi", false),
                new XAttribute("source", context.SourceBaseline.SourcePath),
                new XAttribute("working", context.WorkingPath),
                new XAttribute("requestedOutput", context.RequestedOutputPath),
                new XAttribute("plane", context.Plane));
            BaseFeaturePrescriptionV7 p = context.BasePrescription;
            root.Add(new XElement("BasePrescription",
                new XAttribute("name", p.FeatureName ?? string.Empty),
                new XAttribute("type", p.FeatureType ?? string.Empty),
                new XAttribute("treeOrder", p.TreeOrder),
                new XAttribute("sheetMetal", p.IsSheetMetal),
                new XAttribute("supportedBaseType", p.SupportedBaseType),
                new XAttribute("needSketchMutation", p.NeedSketchMutation),
                new XAttribute("needDirectionDiagnosis", p.NeedDirectionDiagnosis),
                new XAttribute("needThicknessSideDiagnosis", p.NeedThicknessSideDiagnosis),
                new XElement("Reason", p.Reason ?? string.Empty)));
            if (context.BaseMutation != null)
                root.Add(new XElement("BaseMutation",
                    new XAttribute("base", context.BaseMutation.BaseFeatureName ?? string.Empty),
                    new XAttribute("sketch", context.BaseMutation.SketchFeatureName ?? string.Empty),
                    new XAttribute("points", context.BaseMutation.PointCount),
                    new XAttribute("movingPoints", context.BaseMutation.MovingPointCount),
                    new XAttribute("strategy", context.BaseMutation.Strategy ?? string.Empty),
                    new XAttribute("setCoordsRejected", context.BaseMutation.SetCoordsRejectedCount),
                    new XAttribute("maximumPointErrorSI", context.BaseMutation.MaximumPointErrorMetres),
                    new XAttribute("sketchVerified", context.BaseMutation.SketchVerified),
                    new XAttribute("geometryVerified", context.BaseMutation.BaseGeometryVerified),
                    new XAttribute("saved", context.BaseMutation.Saved)));
            XElement features = new XElement("FeatureDispatchPlan");
            root.Add(features);
            foreach (MirrorV7FeatureResult entry in journal.Entries)
                features.Add(new XElement("Feature",
                    new XAttribute("name", entry.FeatureName ?? string.Empty),
                    new XAttribute("type", entry.FeatureType ?? string.Empty),
                    new XAttribute("status", entry.Status),
                    new XElement("Message", entry.Message ?? string.Empty)));
            XElement mapping = new XElement("FeatureIdentityMap", new XAttribute("configuration", context.SourceBaseline.ConfigurationName));
            foreach (var node in context.SourceGraph.Nodes.Where(n => n.PersistentReference != null))
            {
                int state;
                PersistReferenceV7 replacement;
                bool recreated = context.ReplacementReferences.TryGetValue(node.TreeOrder, out replacement);
                var target = PersistentReferenceServiceV7.Resolve(context.WorkingDocument,
                    recreated ? replacement : node.PersistentReference, out state) as Feature;
                if (target == null) throw new InvalidOperationException("Output feature identity lost: " + node.Name);
                var targetRef = PersistentReferenceServiceV7.Capture(context.WorkingDocument, target, target.Name, "OUTPUT_MAP");
                if (targetRef == null) throw new InvalidOperationException("Output reference cannot be captured: " + node.Name);
                mapping.Add(new XElement("Feature", new XAttribute("sourceName", node.Name),
                    new XAttribute("targetName", target.Name), new XAttribute("type", node.TypeName),
                    new XAttribute("nativeReplacement", recreated),
                    new XElement("SourceReference", Convert.ToBase64String(node.PersistentReference.Data)),
                    new XElement("TargetReference", Convert.ToBase64String(targetRef.Data))));
            }
            root.Add(mapping);
            root.Add(new XElement("PublicationGate", "PASS_COMPLETE_BODY_ORACLE_AND_FEATURE_TREE"));
            string path = Path.Combine(Path.GetDirectoryName(context.WorkingPath), "MirrorInPlacePlan.xml");
            new XDocument(root).Save(path);
            return path;
        }

        private static void Activate(ISldWorks app, ModelDoc2 model)
        {
            if (model == null) return;
            int error = 0;
            ModelDoc2 active = app.ActivateDoc3(model.GetTitle(), false,
                (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as ModelDoc2;
            if (active == null || error != 0)
                throw new InvalidOperationException("Cannot activate document: " + model.GetTitle() + ", error=" + error);
        }

        private static void RestoreRollbackToEndAndRebuild(MirrorInPlaceExecutionContextV7 context)
        {
            FeatureManager manager = context.WorkingDocument.FeatureManager;
            if (manager == null || !manager.EditRollback(
                (int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, string.Empty))
                throw new InvalidOperationException("Cannot restore the complete feature tree after reflection.");
            bool rebuilt = context.WorkingDocument.EditRebuild3();
            MirrorV7Diagnostics.Log("[INPLACE][FULL_TREE_REBUILD] success=" + rebuilt);
            // EditRebuild3 can return false because of a document-level warning.  The feature
            // audit and the bidirectional body oracle below are the authoritative checks.
        }

        private static void RegisterNativeReplacement(MirrorInPlaceExecutionContextV7 context,
            MirrorV7FeatureNode replacement, ModelDoc2 target)
        {
            if (context == null || replacement == null || replacement.Feature == null || target == null)
                throw new InvalidOperationException("Native replacement registration requires a live feature and document.");
            MirrorV7FeatureNode source = context.SourceGraph.Nodes.FirstOrDefault(n =>
                n.TreeOrder == replacement.TreeOrder);
            if (source == null || source.Role == MirrorV7FeatureRole.System ||
                source.Depth != replacement.Depth ||
                !string.Equals(source.Name, replacement.Name, StringComparison.Ordinal) ||
                !string.Equals(source.TypeName, replacement.TypeName, StringComparison.Ordinal) ||
                context.ReplacementReferences.ContainsKey(source.TreeOrder))
                throw new InvalidOperationException("Native replacement does not match one unique source node: " +
                    replacement.Name + " order=" + replacement.TreeOrder);
            PersistReferenceV7 reference = PersistentReferenceServiceV7.Capture(target,
                replacement.Feature, source.Name, "NATIVE_REPLACEMENT");
            int state;
            Feature resolved = reference == null ? null :
                PersistentReferenceServiceV7.Resolve(target, reference, out state) as Feature;
            if (resolved == null || !SingleSketchTargetBuilderV7.SameComObject(resolved, replacement.Feature))
                throw new InvalidOperationException("Native replacement identity cannot be captured: " + source.Name);
            context.ReplacementReferences.Add(source.TreeOrder, reference);
            MirrorV7Diagnostics.Log("[FEATURE42][REPLACEMENT_REGISTERED] name=" + source.Name +
                " type=" + source.TypeName + " order=" + source.TreeOrder);
        }

        // Intermittent native observation: at final verification the staging
        // Part was occasionally found flattened (FlatPattern unsuppressed, flat
        // body) although every replay step had passed, on Parts that carry a
        // derived flat-pattern configuration. Verify in the source-active
        // configuration: record what is active and switch back if it differs.
        private static void EnsureBaselineConfiguration71(ModelDoc2 model, string baseline, string stage)
        {
            if (model == null || string.IsNullOrWhiteSpace(baseline)) return;
            var active = model.ConfigurationManager.ActiveConfiguration;
            string name = active == null ? "" : active.Name ?? "";
            bool matches = string.Equals(name, baseline, StringComparison.Ordinal);
            bool restored = false;
            if (!matches)
            {
                restored = model.ShowConfiguration2(baseline) && model.EditRebuild3();
                var after = model.ConfigurationManager.ActiveConfiguration;
                if (!restored || after == null || !string.Equals(after.Name, baseline, StringComparison.Ordinal))
                    throw new InvalidOperationException("CONFIG71 cannot return to the source-active configuration: " + baseline);
            }
            MirrorV7Diagnostics.Log("[CONFIG71][ACTIVE] stage=" + stage + " active=" + name + " baseline=" + baseline +
                " matches=" + matches + " restored=" + restored);
        }

        // Intermittent native observation (three runs): with the source-active
        // configuration still active, the staging Part reached final
        // verification with its FlatPattern unsuppressed, i.e. as a flat body,
        // after every replay step had passed. The step that leaves it so is
        // not identified. The correct state is known, so it is restored and
        // logged: a FlatPattern that is suppressed in the source must be
        // suppressed in the result. The tree gate still compares suppression.
        private static void RestoreFlatPatternState71(MirrorV7ModelGraph sourceGraph, ModelDoc2 model, string stage)
        {
            if (sourceGraph == null || model == null) return;
            var sourceFlats = sourceGraph.Nodes.Where(n => n.Depth == 0 && n.TypeName == "FlatPattern").ToList();
            var targetFlats = new List<Feature>();
            for (var feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                if (feature.GetTypeName2() == "FlatPattern") targetFlats.Add(feature);
            if (sourceFlats.Count != targetFlats.Count) return;
            int restored = 0;
            for (int i = 0; i < sourceFlats.Count; i++)
            {
                if (!sourceFlats[i].IsSuppressed || targetFlats[i].IsSuppressed()) continue;
                if (!targetFlats[i].SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null))
                    throw new InvalidOperationException("FLAT71 cannot re-suppress the FlatPattern: " + targetFlats[i].Name);
                restored++;
            }
            if (restored > 0 && !model.EditRebuild3())
                throw new InvalidOperationException("FLAT71 rebuild failed after re-suppressing the FlatPattern.");
            MirrorV7Diagnostics.Log("[FLAT71][STATE] stage=" + stage + " flatPatterns=" + targetFlats.Count +
                " reSuppressed=" + restored + " cause=" + (restored > 0 ? "UNIDENTIFIED_PIPELINE_STEP" : "NONE"));
        }

        // The mirrored Part must stand alone. In-context links of the source
        // (sketch relations and features that point at other Parts through an
        // assembly) would keep the result tied to the ORIGINAL assembly, whose
        // neighbours are not mirrored. They are broken in the result only,
        // before the final geometry and tree checks, so those checks cover the
        // state that is saved. The source Part is never touched.
        private static void BreakExternalReferences71(ModelDoc2 copy)
        {
            // Counted per feature: the document-level count answered 0 natively
            // for a Part whose features list in-context references.
            Func<int> count = () =>
            {
                int total = 0;
                for (var feature = copy.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                    total += Math.Max(0, feature.ListExternalFileReferencesCount());
                return total;
            };
            int before = count();
            if (before <= 0) return;
            copy.Extension.BreakAllExternalFileReferences2(false);
            if (!copy.EditRebuild3())
                throw new InvalidOperationException("EXTREF71 rebuild failed after breaking the external references of the result.");
            MirrorV7Diagnostics.Log("[EXTREF71][BROKEN_IN_RESULT] featureReferencesBefore=" + before +
                " listedAfter=" + count() + " sourceTouched=False");
        }

        private static HashSet<string> DependencyPaths71(ModelDoc2 model)
        {
            var raw = model.Extension.GetDependencies(true, false, false, false, false) as Array;
            var values = raw == null ? new string[0] : raw.Cast<object>().Select(v => Convert.ToString(v) ?? "").ToArray();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < values.Length; i += 2)
                if (!values[i].EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) && !values[i].EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                    paths.Add(Path.GetFullPath(values[i]));
            return paths;
        }

        // After "break all external references", save and reopen, SOLIDWORKS
        // still NAMES the assembly an in-context sketch entity came from
        // (native: document reference count 0, GetDependencies still returns
        // the assembly; the link sits on the converted sketch entity itself,
        // not on a relation that could be removed). The result does not follow
        // that assembly any more. Accepted only when the document reports no
        // live external reference and every named file was already a parent of
        // the SOURCE Part, i.e. the mirror added no dependency of its own.
        private static bool OnlyBrokenInheritedLinks71(ModelDoc2 published, ModelDoc2 source)
        {
            int live = published.Extension.ListExternalFileReferencesCount();
            var named = DependencyPaths71(published);
            var inherited = DependencyPaths71(source);
            bool pass = live == 0 && named.Count > 0 && named.All(inherited.Contains);
            MirrorV7Diagnostics.Log("[EXTREF71][BROKEN_LINKS_ONLY] liveReferences=" + live + " namedParents=" + named.Count +
                " allInheritedFromSource=" + named.All(inherited.Contains) + " result=" + (pass ? "PASS" : "FAIL") +
                " parents=" + string.Join("|", named.Select(Path.GetFileName)));
            return pass;
        }

        // A profile this run had to draw again (its points could not be moved) is
        // made of new sketch entities, and the bend SOLIDWORKS generates from such
        // an entity is generated again with a new identity (natively the bend of a
        // base flange whose profile is one arc). It is the same bend when it sits
        // under the same owner, with the same type and name, at the same place
        // among the bends of that owner. The entry the Flat-Pattern keeps for that
        // bend is generated again with it, under a renumbered label; as for a
        // regenerated flange bend (RegisterFlatBends52) it is the one Flat-Pattern
        // entry whose parent is the mapped bend. Both are registered as native
        // replacements, so every later identity check (also after save and reopen)
        // follows the new features.
        // An empty cut-list item the source Part does not have is a leftover of the
        // replay: natively a Boss that stood as a second body for a moment made
        // SOLIDWORKS open a second cut-list item, and the item stayed, with no
        // body and only its two default properties, after the bodies were one
        // again (the tree then had one node more than the source and the Part was
        // refused at the very end). Such an item is deleted. An item that holds a
        // body, or one the source has under the same name, is never touched.
        private static void RemoveEmptyCutListItems71(MirrorInPlaceExecutionContextV7 context, ModelDoc2 target)
        {
            if (context.SourceGraph == null || target == null) return;
            var inSource = new HashSet<string>(context.SourceGraph.Nodes.Where(n => n.TypeName == "CutListFolder").Select(n => n.Name), StringComparer.Ordinal);
            var leftovers = new List<string>();
            for (var feature = target.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
            {
                if (feature.GetTypeName2() != "SolidBodyFolder") continue;
                for (var item = feature.GetFirstSubFeature() as Feature; item != null; item = item.GetNextSubFeature() as Feature)
                {
                    if (item.GetTypeName2() != "CutListFolder" || inSource.Contains(item.Name) || leftovers.Contains(item.Name)) continue;
                    var folder = item.GetSpecificFeature2() as BodyFolder;
                    if (folder != null && folder.GetBodyCount() == 0) leftovers.Add(item.Name);
                }
            }
            foreach (string name in leftovers)
            {
                var item = ((PartDoc)target).FeatureByName(name) as Feature;
                target.ClearSelection2(true);
                bool removed = item != null && item.Select2(false, 0) && target.Extension.DeleteSelection2(0) &&
                    ((PartDoc)target).FeatureByName(name) == null;
                MirrorV7Diagnostics.Log("[CUTLIST71][EMPTY_ITEM] name=" + name + " bodies=0 inSource=False removed=" + removed);
            }
            if (leftovers.Count > 0) target.ClearSelection2(true);
        }

        private static void RegisterRegeneratedUnderRedrawnProfile71(MirrorInPlaceExecutionContextV7 context, ModelDoc2 target)
        {
            if (RollbackReplayEngineV7.RedrawnProfileOwners71.Count == 0 || context.SourceGraph == null) return;
            MirrorV7ModelGraph sourceGraph = context.SourceGraph, targetGraph = FeatureTreeScannerV7.Scan(target);
            Func<MirrorV7FeatureNode, MirrorV7FeatureNode> inTarget = node =>
            {
                int state; PersistReferenceV7 replacement;
                var live = PersistentReferenceServiceV7.Resolve(target,
                    context.ReplacementReferences.TryGetValue(node.TreeOrder, out replacement) ? replacement : node.PersistentReference, out state) as Feature;
                return live == null ? null : targetGraph.Nodes.FirstOrDefault(n => SingleSketchTargetBuilderV7.SameComObject(n.Feature, live));
            };
            for (int index = 0; index < sourceGraph.Count; index++)
            {
                MirrorV7FeatureNode a = sourceGraph.Nodes[index];
                if (a.Depth == 0 || a.Role != MirrorV7FeatureRole.SheetMetal || a.TypeName != "SketchBend" || a.PersistentReference == null ||
                    context.ReplacementReferences.ContainsKey(a.TreeOrder) || inTarget(a) != null) continue;
                MirrorV7FeatureNode owner = null;
                for (int k = index - 1; k >= 0 && owner == null && sourceGraph.Nodes[k].Depth >= a.Depth - 1; k--)
                    if (sourceGraph.Nodes[k].Depth == a.Depth - 1) owner = sourceGraph.Nodes[k];
                if (owner == null || owner.PersistentReference == null || !RollbackReplayEngineV7.RedrawnProfileOwners71.Contains(owner.Name)) continue;
                MirrorV7FeatureNode ownerTarget = inTarget(owner);
                if (ownerTarget == null) continue;
                Func<MirrorV7ModelGraph, MirrorV7FeatureNode, List<MirrorV7FeatureNode>> bends = (graph, parent) =>
                {
                    var list = new List<MirrorV7FeatureNode>();
                    for (int k = graph.Nodes.IndexOf(parent) + 1; k < graph.Count && graph.Nodes[k].Depth > parent.Depth; k++)
                        if (graph.Nodes[k].Depth == parent.Depth + 1 && graph.Nodes[k].TypeName == a.TypeName) list.Add(graph.Nodes[k]);
                    return list;
                };
                List<MirrorV7FeatureNode> before = bends(sourceGraph, owner), after = bends(targetGraph, ownerTarget);
                int place = before.IndexOf(a);
                if (place < 0 || before.Count != after.Count || !string.Equals(after[place].Name, a.Name, StringComparison.Ordinal)) continue;
                MirrorV7FeatureNode bend = after[place];
                RegisterNativeReplacement(context, bend, target);
                MirrorV7Diagnostics.Log("[TREE71][REGENERATED_UNDER_REDRAWN_PROFILE] source=" + a.Name + " order=" + a.TreeOrder + " type=" + a.TypeName +
                    " owner=" + owner.Name + " evidence=SAME_OWNER_TYPE_NAME_AND_PLACE registered=True");
                foreach (var flat in sourceGraph.Nodes.Where(n => n.TypeName == "UiBend" && n.PersistentReference != null &&
                    (n.ParentFeatureNames.Contains(a.Name) || n.Name.IndexOf("<" + a.Name + ">", StringComparison.Ordinal) >= 0)).ToList())
                {
                    if (context.ReplacementReferences.ContainsKey(flat.TreeOrder) || inTarget(flat) != null) continue;
                    var flatOwner = sourceGraph.Nodes.Take(flat.TreeOrder).LastOrDefault(n => n.Depth < flat.Depth);
                    var outputOwner = flatOwner == null || flatOwner.TypeName != "FlatPattern" || flatOwner.PersistentReference == null ? null : inTarget(flatOwner);
                    if (outputOwner == null || outputOwner.TypeName != "FlatPattern") continue;
                    var candidates = targetGraph.Nodes.Skip(outputOwner.TreeOrder + 1).TakeWhile(n => n.Depth > outputOwner.Depth)
                        .Where(n => n.TypeName == "UiBend" && n.Depth == outputOwner.Depth + 1 &&
                            (n.Feature.GetParents() as object[] ?? new object[0]).OfType<Feature>()
                                .Any(p => SingleSketchTargetBuilderV7.SameComObject(p, bend.Feature))).ToList();
                    int state;
                    PersistReferenceV7 reference = candidates.Count != 1 || candidates[0].IsSuppressed != flat.IsSuppressed ? null :
                        PersistentReferenceServiceV7.Capture(target, candidates[0].Feature, flat.Name, "REGENERATED_FLAT_BEND");
                    if (reference == null || !SingleSketchTargetBuilderV7.SameComObject(candidates[0].Feature,
                        PersistentReferenceServiceV7.Resolve(target, reference, out state)))
                    {
                        MirrorV7Diagnostics.Log("[TREE71][REGENERATED_FLAT_ENTRY] source=" + flat.Name + " candidates=" + candidates.Count + " registered=False");
                        continue;
                    }
                    context.ReplacementReferences.Add(flat.TreeOrder, reference);
                    context.ReplacementTargetNames.Add(flat.TreeOrder, candidates[0].Name);
                    context.GeneratedBendOwners.Add(flat.TreeOrder, a.TreeOrder);
                    MirrorV7Diagnostics.Log("[TREE71][REGENERATED_FLAT_ENTRY] source=" + flat.Name + " target=" + candidates[0].Name + " bend=" + a.Name +
                        " evidence=MAPPED_BEND_AND_FLAT_PATTERN registered=True");
                }
            }
        }

        private static void ValidateCompleteFeatureTree(MirrorInPlaceExecutionContextV7 context, ModelDoc2 target)
        {
            MirrorV7ModelGraph sourceGraph = context.SourceGraph;
            MirrorV7ModelGraph targetGraph = FeatureTreeScannerV7.Scan(target);
            // Flat-sketch cache entries are regenerated by SOLIDWORKS and are
            // counted separately from design features on both sides.
            HashSet<int> sourceCache71 = FeatureTreeLayout50.FlatSketchCacheNodes71(sourceGraph);
            HashSet<int> targetCache71 = FeatureTreeLayout50.FlatSketchCacheNodes71(targetGraph);
            if (sourceGraph.Count - sourceCache71.Count != targetGraph.Count - targetCache71.Count)
                throw new InvalidOperationException("Feature tree count changed. source=" + sourceGraph.Count +
                    " target=" + targetGraph.Count + " sourceFlatSketchCache=" + sourceCache71.Count +
                    " targetFlatSketchCache=" + targetCache71.Count);
            var mapped = new Dictionary<int, MirrorV7FeatureNode>();
            var used = new HashSet<int>();
            for (int i = 0; i < sourceGraph.Count; i++)
            {
                MirrorV7FeatureNode a = sourceGraph.Nodes[i];
                if (a.Role == MirrorV7FeatureRole.System) continue;
                if (a.PersistentReference == null)
                    throw new InvalidOperationException("Source feature identity unavailable: " + a.Name);
                int state;
                PersistReferenceV7 replacement;
                bool recreated = context.ReplacementReferences.TryGetValue(a.TreeOrder, out replacement);
                Feature resolved = PersistentReferenceServiceV7.Resolve(target,
                    recreated ? replacement : a.PersistentReference, out state) as Feature;
                MirrorV7FeatureNode b = resolved == null ? null : targetGraph.Nodes.FirstOrDefault(n =>
                    SingleSketchTargetBuilderV7.SameComObject(n.Feature, resolved));
                if (b == null && !recreated && sourceCache71.Contains(a.TreeOrder))
                {
                    MirrorV7Diagnostics.Log("[TREE71][FLAT_SKETCH_CACHE_DROPPED] source=" + a.Name +
                        " order=" + a.TreeOrder + " state=" + state + " evidence=REGENERATED_BY_FLAT_PATTERN");
                    continue;
                }
                if (b == null) throw new InvalidOperationException("Feature identity missing in output: " + a.Name +
                    " nativeReplacement=" + recreated + " state=" + state);
                if (!used.Add(b.TreeOrder))
                    throw new InvalidOperationException("Multiple source identities resolve to output feature: " + b.Name);
                mapped.Add(a.TreeOrder, b);
                string expectedName;
                if (context.ReplacementTargetNames.TryGetValue(a.TreeOrder, out expectedName))
                {
                    bool mappedFlatBend70 = a.Role == MirrorV7FeatureRole.SheetMetal && a.TypeName == "UiBend" &&
                        context.GeneratedBendOwners.ContainsKey(a.TreeOrder);
                    if (!recreated || (a.Role != MirrorV7FeatureRole.Sketch && !mappedFlatBend70) || string.IsNullOrWhiteSpace(expectedName))
                        throw new InvalidOperationException("FLANGE51 invalid replacement name registration: " + a.Name);
                }
                else expectedName = a.Name;
                // An auxiliary surface feature this run suppressed on purpose is the one allowed state change.
                bool suppressedAuxiliary71 = !a.IsSuppressed && b.IsSuppressed && RollbackReplayEngineV7.SuppressedAuxiliary71.Contains(b.Name);
                if (!string.Equals(a.TypeName, b.TypeName, StringComparison.Ordinal) ||
                    (a.IsSuppressed != b.IsSuppressed && !suppressedAuxiliary71) ||
                    (recreated && !string.Equals(expectedName, b.Name, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Feature tree identity changed at order " + i +
                        ": source=" + a.Name + " [" + a.TypeName + "] target=" +
                        b.Name + " [" + b.TypeName + "]. sourceSuppressed=" + a.IsSuppressed +
                        " targetSuppressed=" + b.IsSuppressed + " activeConfiguration=" +
                        (target.ConfigurationManager.ActiveConfiguration == null ? "" :
                            target.ConfigurationManager.ActiveConfiguration.Name));
                if (recreated)
                    MirrorV7Diagnostics.Log("[FEATURE42][REPLACEMENT_RESOLVED] name=" + a.Name +
                        " order=" + a.TreeOrder + " state=" + state);
                if (b.IsSuppressed || b.Role == MirrorV7FeatureRole.System) continue;
                bool warning;
                int error = b.Feature.GetErrorCode2(out warning);
                if (error != 0 && !warning)
                    throw new InvalidOperationException("Active feature has rebuild error: " + b.Name +
                        " [" + b.TypeName + "] error=" + error);
            }
            if (targetGraph.Nodes.Any(n => n.Role != MirrorV7FeatureRole.System && !used.Contains(n.TreeOrder) &&
                !targetCache71.Contains(n.TreeOrder)))
                throw new InvalidOperationException("Output contains unmapped non-system features.");
            foreach (var pair in context.GeneratedBendOwners)
            {
                var bend = mapped[pair.Key];
                var owner = mapped[pair.Value];
                if (!(bend.Feature.GetParents() as object[] ?? new object[0]).OfType<Feature>()
                    .Any(p => SingleSketchTargetBuilderV7.SameComObject(p, owner.Feature)))
                    throw new InvalidOperationException("FLANGE52 regenerated bend lost its registered parent: " + bend.Name);
            }
            var nativeProfileOwners70 = new Dictionary<int, int>();
            foreach (var pair70 in context.ExternalFlangeProfiles)
            {
                var profile70 = mapped[pair70.Key];
                var flange70 = mapped[pair70.Value];
                var owner70 = profile70.Feature.GetOwnerFeature() as Feature;
                bool absorbed70 = false;
                if (owner70 != null && SingleSketchTargetBuilderV7.SameComObject(owner70, flange70.Feature))
                    for (var child70 = flange70.Feature.GetFirstSubFeature() as Feature; child70 != null;
                        child70 = child70.GetNextSubFeature() as Feature)
                        if (SingleSketchTargetBuilderV7.SameComObject(child70, profile70.Feature))
                        { absorbed70 = true; break; }
                if (absorbed70)
                {
                    nativeProfileOwners70.Add(pair70.Key, pair70.Value);
                    MirrorV7Diagnostics.Log("[TREE70][PROFILE_OWNER_PASS] sourceId=" + pair70.Key +
                        " ownerId=" + pair70.Value + " evidence=NATIVE_OWNER_AND_SUBFEATURE");
                }
                else if (owner70 != null)
                    throw new InvalidOperationException("TREE70 registered profile has an unrelated/inconsistent native owner: " + profile70.Name);
                // Without BOTH native ownership proofs, retain the original
                // direct-consumer/parent gates. Empty dependency lists alone
                // never qualify an otherwise unbound profile.
            }
            var nativeHingeParents70 = new Dictionary<int, string[]>();
            foreach (var pair70 in context.NativeFlangeHingeParents70)
            {
                if (mapped[pair70.Key].TypeName != "EdgeFlange" || pair70.Value == null || pair70.Value.Length == 0 ||
                    pair70.Value.Distinct().Count() != pair70.Value.Length)
                    throw new InvalidOperationException("TREE70 native hinge proof registration invalid.");
                var mandatory70 = pair70.Value.Select(id => mapped[id].Name).ToArray();
                if (mandatory70.Any(name => !mapped[pair70.Key].ParentFeatureNames.Contains(name)))
                    throw new InvalidOperationException("TREE70 native flange hinge lost a mandatory source parent.");
                nativeHingeParents70.Add(pair70.Key, mandatory70);
            }
            FeatureTreeLayout50.Verify(sourceGraph, targetGraph, mapped, context.ExternalFlangeProfiles,
                context.ReplacementTargetNames, nativeProfileOwners70, nativeHingeParents70);
            MirrorV7Diagnostics.Log("[TREE50][LAYOUT_PASS] mapped=" + mapped.Count +
                " registeredExternalProfiles=" + context.ExternalFlangeProfiles.Count);
            MirrorV7Diagnostics.Log("[INPLACE][FULL_TREE_VERIFY] featureCount=" + targetGraph.Count +
                " result=PASS");
        }

        private static void ImportFlangeProfileMappings50(MirrorInPlaceExecutionContextV7 context,
            MirrorInPlaceExecutionContextV7 replay)
        {
            foreach (var replacement in replay.ReplacementReferences)
            {
                var source = context.SourceGraph.Nodes.Single(n => n.TreeOrder == replacement.Key);
                var captured = replay.SourceGraph.Nodes.Single(n => n.TreeOrder == replacement.Key);
                if (source.Name != captured.Name || source.TypeName != captured.TypeName ||
                    source.Depth != captured.Depth || source.IsSuppressed != captured.IsSuppressed ||
                    context.ReplacementReferences.ContainsKey(replacement.Key))
                    throw new InvalidOperationException("FLANGE50 replay/publication identity mismatch: " + source.Name);
                int state;
                var feature = PersistentReferenceServiceV7.Resolve(replay.WorkingDocument,
                    replacement.Value, out state) as Feature;
                string expectedName;
                bool profile = replay.ReplacementTargetNames.TryGetValue(replacement.Key, out expectedName);
                bool bend = source.TypeName == "OneBend" && replay.GeneratedBendOwners.ContainsKey(replacement.Key);
                if (!profile) expectedName = source.Name;
                if ((!profile && !bend) || (profile && source.Role != MirrorV7FeatureRole.Sketch) ||
                    string.IsNullOrWhiteSpace(expectedName) ||
                    feature == null || feature.Name != expectedName ||
                    FeatureTypeHelperV7.GetEffectiveType(feature) != source.TypeName)
                    throw new InvalidOperationException("FLANGE50 profile replacement cannot be resolved: " + source.Name);
                context.ReplacementReferences.Add(replacement.Key, replacement.Value);
                if (profile) context.ReplacementTargetNames.Add(replacement.Key, expectedName);
            }
            foreach (var pair in replay.ExternalFlangeProfiles)
            {
                var owner = context.SourceGraph.Nodes.Single(n => n.TreeOrder == pair.Value);
                var capturedOwner = replay.SourceGraph.Nodes.Single(n => n.TreeOrder == pair.Value);
                if (owner.Name != capturedOwner.Name || owner.TypeName != "EdgeFlange" ||
                    !context.ReplacementReferences.ContainsKey(pair.Key))
                    throw new InvalidOperationException("FLANGE50 owner registration is invalid.");
                context.ExternalFlangeProfiles.Add(pair.Key, pair.Value);
            }
            foreach (var pair70 in replay.NativeFlangeHingeParents70)
            {
                var owner70 = context.SourceGraph.Nodes.Single(n => n.TreeOrder == pair70.Key);
                if (owner70.TypeName != "EdgeFlange" || pair70.Value == null || pair70.Value.Length == 0 ||
                    pair70.Value.Any(id => !context.SourceGraph.Nodes.Any(n => n.TreeOrder == id && n.TreeOrder < owner70.TreeOrder)))
                    throw new InvalidOperationException("FLANGE70 source hinge identity import failed.");
                context.NativeFlangeHingeParents70.Add(pair70.Key, (int[])pair70.Value.Clone());
            }
            foreach (var pair in replay.GeneratedBendOwners)
            {
                var owner = context.SourceGraph.Nodes.Single(n => n.TreeOrder == pair.Value);
                if (owner.TypeName != "EdgeFlange" || !context.ReplacementReferences.ContainsKey(pair.Key))
                    throw new InvalidOperationException("FLANGE52 invalid bend owner registration.");
                context.GeneratedBendOwners.Add(pair.Key, pair.Value);
            }
            RegisterFlatBends52(context, replay.WorkingDocument);
            MirrorV7Diagnostics.Log("[FLANGE52][LEDGER_IMPORTED] replacements=" + replay.ReplacementReferences.Count +
                " external=" + replay.ExternalFlangeProfiles.Count);
        }

        private static void RegisterFlatBends52(MirrorInPlaceExecutionContextV7 context, ModelDoc2 model)
        {
            if (context.GeneratedBendOwners.Count == 0) return;
            var source = context.SourceGraph;
            var target = FeatureTreeScannerV7.Scan(model);
            foreach (var pair in context.GeneratedBendOwners.ToArray())
            {
                var bend = source.Nodes.Single(n => n.TreeOrder == pair.Key);
                if (bend.TypeName != "OneBend") continue;
                int state;
                var mappedBend = PersistentReferenceServiceV7.Resolve(model,
                    context.ReplacementReferences[pair.Key], out state) as Feature;
                if (mappedBend == null) throw new InvalidOperationException("FLANGE52 regenerated bend disappeared.");
                foreach (var flat in source.Nodes.Where(n => n.TypeName == "UiBend" && n.ParentFeatureNames.Contains(bend.Name)))
                {
                    var flatOwner = source.Nodes.Take(flat.TreeOrder).LastOrDefault(n => n.Depth < flat.Depth);
                    if (flatOwner == null || flatOwner.TypeName != "FlatPattern" || flatOwner.PersistentReference == null)
                        throw new InvalidOperationException("FLANGE52 source flat bend owner unavailable.");
                    var resolvedOwner = PersistentReferenceServiceV7.Resolve(model, flatOwner.PersistentReference, out state) as Feature;
                    var outputOwner = target.Nodes.SingleOrDefault(n =>
                        SingleSketchTargetBuilderV7.SameComObject(n.Feature, resolvedOwner));
                    if (outputOwner == null || outputOwner.TypeName != "FlatPattern")
                        throw new InvalidOperationException("FLANGE52 target flat pattern identity unavailable.");
                    var candidates = target.Nodes.Skip(outputOwner.TreeOrder + 1)
                        .TakeWhile(n => n.Depth > outputOwner.Depth)
                        .Where(n => n.TypeName == "UiBend" && n.Depth == outputOwner.Depth + 1 &&
                            (n.Feature.GetParents() as object[] ?? new object[0]).OfType<Feature>()
                                .Any(p => SingleSketchTargetBuilderV7.SameComObject(p, mappedBend))).ToList();
                    if (candidates.Count != 1 || candidates[0].IsSuppressed != flat.IsSuppressed)
                        throw new InvalidOperationException("FLANGE52 flat bend mapping missing/ambiguous: " + flat.Name +
                            " candidates=" + candidates.Count);
                    var reference = PersistentReferenceServiceV7.Capture(model, candidates[0].Feature,
                        flat.Name, "REGENERATED_FLAT_BEND");
                    if (reference == null || !SingleSketchTargetBuilderV7.SameComObject(candidates[0].Feature,
                        PersistentReferenceServiceV7.Resolve(model, reference, out state)))
                        throw new InvalidOperationException("FLANGE52 flat bend identity cannot be retained.");
                    context.ReplacementReferences.Add(flat.TreeOrder, reference);
                    // A native FlatPattern rebuild can renumber UiBend labels.
                    // The unique mapped bend parent AND original FlatPattern
                    // owner establish identity; the generated label does not.
                    context.ReplacementTargetNames.Add(flat.TreeOrder, candidates[0].Name);
                    context.GeneratedBendOwners.Add(flat.TreeOrder, bend.TreeOrder);
                    MirrorV7Diagnostics.Log("[FLANGE52][FLAT_BEND_REGISTERED] source=" + flat.Name +
                        " target=" + candidates[0].Name + " owner=" + bend.Name +
                        " evidence=MAPPED_BEND_AND_FLAT_PATTERN sourceId=" + flat.TreeOrder);
                }
            }
        }

        private static void SaveWorkingDocument(ModelDoc2 model)
        {
            int errors = 0, warnings = 0;
            if (!model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0)
                throw new InvalidOperationException("Cannot save verified working Part. errors=" + errors +
                    " warnings=" + warnings);
            MirrorV7Diagnostics.Log("[INPLACE][STAGING_SAVED] errors=" + errors + " warnings=" + warnings);
        }

        private static void VerifyPublishedOutput(ISldWorks app, MirrorInPlaceExecutionContextV7 context,
            List<Body2> finalBodyOracle, Action<ModelDoc2> verifyAppearance71 = null)
        {
            // A single bounded repair on the private candidate. Pass 2 is read/verify only:
            // do not publish merely because the unsaved in-memory dependency list is empty.
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && app.GetOpenDocumentByName(context.WorkingPath) != null)
                    throw new InvalidOperationException("Candidate is still loaded after CloseDoc; disk reopen cannot be certified.");
                int errors = 0, warnings = 0;
                ModelDoc2 published = app.OpenDoc6(context.WorkingPath,
                    (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    context.SourceBaseline.ConfigurationName, ref errors, ref warnings);
                if (published == null)
                    throw new InvalidOperationException("Cannot reopen published Part. errors=" + errors +
                        " warnings=" + warnings);
                try
                {
                    Activate(app, published);
                    if (errors != 0) throw new InvalidOperationException("Reopen error=" + errors);
                    if (!string.Equals(Path.GetFullPath(published.GetPathName()),
                        Path.GetFullPath(context.WorkingPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Reopened document is not the private mirror candidate.");
                    context.WorkingDocument = published;
                    if (!published.EditRebuild3())
                        throw new InvalidOperationException("Reopened candidate failed to rebuild.");
                    BaseSketchMutationEngineV7.VerifyCurrentSolids(context, finalBodyOracle);
                    ValidateCompleteFeatureTree(context, published);
                    if (verifyAppearance71 != null) verifyAppearance71(published);
                    if (!CrossDocumentReferenceAuditV7.Run(context.SourceGraph, published,
                        context.ReplacementReferences, context.ReplacementTargetNames).Success)
                        throw new InvalidOperationException("Feature identity mapping failed after reopening.");
                    ExternalDependencyAuditResultV7 dependencyAudit = ExternalDependencyAuditV7.Run(published);
                    if (!dependencyAudit.Success && pass == 0)
                    {
                        MirrorV7Diagnostics.Log("[FEATURE43][SOURCE_DEPENDENCY_BASELINE] readOnly=True");
                        ExternalDependencyAuditV7.Run(context.SourceDocument);
                        ExternalReferenceBreakResultV7 breakResult = ExternalReferenceBreakServiceV7.Run(
                            published, context.SourceDocument);
                        if (breakResult.Executed)
                        {
                            BaseSketchMutationEngineV7.VerifyCurrentSolids(context, finalBodyOracle);
                            ValidateCompleteFeatureTree(context, published);
                            if (!CrossDocumentReferenceAuditV7.Run(context.SourceGraph, published,
                                context.ReplacementReferences, context.ReplacementTargetNames).Success)
                                throw new InvalidOperationException("Feature identity mapping changed after breaking in-context references.");
                            SaveWorkingDocument(published);
                            context.SourceBaseline.AssertUnchanged(context.SourceDocument);
                            MirrorV7Diagnostics.Log("[FEATURE43][DETACH_SAVED] geometry=True featureTree=True identity=True next=CLOSE_REOPEN");
                            continue; // finally closes the candidate before the second pass.
                        }
                    }
                    if (!dependencyAudit.Success && pass == 1 && OnlyBrokenInheritedLinks71(published, context.SourceDocument))
                    {
                        MirrorV7Diagnostics.Log("[INPLACE][STAGING_REOPEN_PASS] reopen=True geometry=True featureTree=True independent=LINKS_BROKEN_PARENT_STILL_NAMED");
                        return;
                    }
                    if (!dependencyAudit.Success)
                    {
                        MirrorV7Diagnostics.Log("[FEATURE43][DETACH_REOPEN_FAILED] forbidden=" + dependencyAudit.Forbidden +
                            " outputNotPublished=True see=REFERENCE_OWNER/AUXILIARY_REFERENCE");
                        throw new InvalidOperationException("Result retains forbidden external file references; independence not verified. forbidden=" +
                            dependencyAudit.Forbidden + ". See [PHASE6B][DEPENDENCY_FORBIDDEN] in MirrorPartDebug.log.");
                    }
                    if (pass == 1)
                        MirrorV7Diagnostics.Log("[FEATURE43][DETACH_REOPEN_PASS] geometry=True featureTree=True identity=True dependencies=PASS");
                    MirrorV7Diagnostics.Log("[INPLACE][STAGING_REOPEN_PASS] reopen=True geometry=True featureTree=True independent=True");
                    return;
                }
                finally
                {
                    app.CloseDoc(published.GetTitle());
                    context.WorkingDocument = null;
                }
            }
            throw new InvalidOperationException("Candidate verification did not finish; output is not published.");
        }
    }
}
