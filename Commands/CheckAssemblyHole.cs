using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    /// <summary>
    /// Kiem tra do dong tam cua cac lo thuoc nhieu component trong Assembly.
    /// Lenh chi tao mot 3D Sketch tren Assembly de danh dau NG, khong sua Part.
    /// </summary>
    public sealed class CheckAssemblyHole
    {
        private const string MarkerFeaturePrefix = "CHECK-HOLE-NG";
        private const double PositionToleranceM = 0.00010;       // 0.10 mm
        private const double AngleToleranceDeg = 0.20;
        private const double SearchPositionToleranceM = 0.0030; // 3.00 mm
        private const double SearchAngleToleranceDeg = 1.00;
        // Mat xoan/cong lam truc tru do API tra ve thay doi theo phap tuyen cuc bo.
        // Chi mo rong nguong TIM CAP cho lo da di qua nhanh fallback; dung sai danh
        // gia OK/NG ben duoi van giu nguyen 0.10 mm va 0.20 do.
        private const double CurvedHoleSearchAngleToleranceDeg = 3.00;
        private const double AdjacentFaceGapM = 0.0050;          // 5.00 mm
        private const double RecoveryMaximumPositionM = 0.0250; // 25.00 mm
        private const double RecoveryPitchFraction = 0.45;
        private const double PatternDirectionToleranceDeg = 3.00;
        // Dung sai 3 do chi dung de nhan hai huong pattern tuong ung.
        // Khong dung no de gop ung vien huong: voi hang dai, sai huong nho
        // cung lam cac lo o xa bi lech khoi PatternRowToleranceM.
        private const double PatternDirectionMergeToleranceDeg = 0.05;
        private const double PatternRowToleranceM = 0.0040;      // 4.00 mm
        private const double PatternMinimumStepM = 0.0050;       // 5.00 mm
        private const int WholeRowMinimumHoleCount = 3;
        private const double WholeRowMinimumOverlapFraction = 0.60;
        private const double SameHolePositionToleranceM = 0.00002;
        private const double SameHoleAngleToleranceDeg = 0.10;
        private const double SameHoleAxialGapM = 0.002;
        private const double FullCylinderMinimumSweepRad = 5.20;
        private const double SlotEndMinimumSweepRad = 2.40;
        private const double SlotEndMaximumSweepRad = 3.90;
        private const double SlotMaximumCenterDistanceM = 0.250;
        private const double SlotPairToleranceM = 0.00050;       // 0.50 mm
        // Cac gia tri nay chi dung cho detector lo meo DEBUG, khong tham gia CHECK HOLE hien tai.
        private const int DeformedDebugDefaultSampleCount = 16;
        private const int DeformedDebugAdaptiveSampleCount = 24;
        private const double DeformedDebugMaximumLoopSizeM = 0.500;
        private const double DeformedDebugMaximumPerimeterM = 2.000;
        private const double DeformedDebugDuplicatePositionM = 0.00030;
        private const double DeformedDebugPairMaximumGapM = 0.050;
        // Cac nguong sau chi dung de tim tham chieu cho DEFORMED_VALIDATE.
        // Chung khong tham gia ket qua OK/NG cua CHECK HOLE.
        private const double DeformedValidationSearchPositionM = 0.0250; // 25.00 mm
        private const double DeformedValidationSearchAngleDeg = 25.0;
        private const double DeformedValidationMaximumDiameterDifferenceFraction = 0.75;
        private const double DeformedValidationAmbiguousScoreRatio = 1.15;
        // Chi ap dung khi tim stack co lo meo da phuc hoi. Khong thay doi nguong
        // tim cap cua cylinder/SLOT hien co.
        private const double RecoveredDeformedSearchAngleToleranceDeg = 25.0;
        private const double RecoveredDuplicatePositionToleranceM = 0.00050;
        private const double RecoveredDuplicateAngleToleranceDeg = 10.0;
        private const double RecoveredDuplicateDiameterFraction = 0.35;
        // Recovery rieng cho lo da vuot khoi cua so tim nhanh. Khong thay doi
        // SearchPositionToleranceM va cac dung sai production hien co.
        private const double FarMisalignmentSearchM = 0.0300;    // 30.00 mm
        // FAR_RECOVERY chi dung nguong nay de tim cap lech xa. Nguong tim cap
        // production 0.2 mm va SearchAngleToleranceDeg van giu nguyen.
        private const double FarMisalignmentSearchAngleDeg = 5.0;
        // Chi dung de phat hien quan he occurrence cho FAR_RECOVERY. Khong thay doi
        // ngưong NORMAL 1 do, va khong tham gia danh gia OK/NG.
        private const double RelationshipAnchorSearchAngleDeg = 5.0;
        private const double SpatialGridCellSizeM = 0.0100;      // 10.00 mm
        private const int TrustedOccurrenceMinimumAnchorCount = 2;
        private const double FarAmbiguousMinimumScoreGapM = 0.00025;
        private const double FarAmbiguousScoreRatio = 1.15;
        private static readonly bool VerboseHoleDebug = false;

        private readonly ISldWorks swApp;
        private readonly MathUtility mathUtility;
        // Chi ton tai trong mot lan chay CHECK HOLE. Khong cache qua rebuild/model change.
        private RunProfile activeProfile;

        private sealed class RunProfile
        {
            public long ValidationMs;
            public long ResolveMs;
            public long MarkerSearchMs;
            public long MarkerDeleteMs;
            public long ComponentTraversalMs;
            public long BodyEnumerationMs;
            public long AnalyticReadMs;
            public long DeformedDetectMs;
            public long LogicalMergeMs;
            public long SlotMergeMs;
            public long DeformedValidationMs;
            public long DeformedInjectMs;
            public long SpatialIndexMs;
            public long NormalStackMs;
            public long LegacyRecoveryMs;
            public long RelationshipAnchorMs;
            public long FarRecoveryMs;
            public long EvaluateMs;
            public long PatternMs;
            public long PatternPrepareMs;
            public long PatternAssignmentMs;
            public long PatternEvaluationMs;
            public long MarkerPrepareMs;
            public long MarkerOpenSketchMs;
            public long MarkerGeometryMs;
            public long MarkerCloseSketchMs;
            public long MarkerEndSketchSetupMs;
            // Cac timer nay nam ben trong markerClose/markerFeature, chi dung de
            // tach API cham. Khong cong lai vao tong profile de tranh double-count.
            public long MarkerEndSketchApiMs;
            public long MarkerSketchSolverMs;
            public long MarkerFeatureTreeUpdateMs;
            public long MarkerDisplayUpdateMs;
            public long MarkerPostCloseLookupMs;
            public long MarkerRenameMs;
            public long MarkerColorMs;
            public long MarkerSelectionMs;
            public long MarkerRebuildMs = 0;
            public long MarkerGraphicsMs;
            public long MarkerFeatureMs;
            public long MarkerCleanupMs;
            public long RebuildGraphicsMs;
            public long FinalSummaryUiMs;
            public long DebugLoggingMs = 0;

            public long ComponentGetBodiesCalls;
            public long FaceEnumerationCalls;
            public long SurfaceCalls;
            public long EdgeCalls;
            public long TransformCalls;
            public long SelectionCalls;
            public long SketchCreateCalls;
            public long FeatureCalls;
            public long RebuildCalls = 0;
            public long GraphicsRefreshCalls;
        }

        public event Action<string> ResultTextChanged;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(Keys key);

        public CheckAssemblyHole(ISldWorks app)
        {
            swApp = app;
            mathUtility = app == null ? null : app.GetMathUtility() as MathUtility;
        }

        public void Run()
        {
            RunProfile profile = new RunProfile();
            activeProfile = profile;
            Stopwatch runWatch = Stopwatch.StartNew();
            Stopwatch validationWatch = Stopwatch.StartNew();
            PublishResultText("Dang kiem tra lo tren Assembly...");
            ModelDoc2 model = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            AssemblyDoc assembly = model as AssemblyDoc;
            if (model == null || assembly == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                validationWatch.Stop();
                profile.ValidationMs = validationWatch.ElapsedMilliseconds;
                MessageBox.Show(
                    "Hay mo Assembly can kiem tra truoc khi chay CHECK HOLE.",
                    "CHECK HOLE",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                runWatch.Stop();
                WriteRunProfile(profile, runWatch.ElapsedMilliseconds);
                activeProfile = null;
                return;
            }

            if (GetActiveSketch(model) != null)
            {
                validationWatch.Stop();
                profile.ValidationMs = validationWatch.ElapsedMilliseconds;
                MessageBox.Show(
                    "Hay thoat khoi Sketch dang edit truoc khi chay CHECK HOLE.",
                    "CHECK HOLE",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                runWatch.Stop();
                WriteRunProfile(profile, runWatch.ElapsedMilliseconds);
                activeProfile = null;
                return;
            }

            validationWatch.Stop();
            profile.ValidationMs = validationWatch.ElapsedMilliseconds;

            Cursor previousCursor = Cursor.Current;
            bool oldCommandInProgress = false;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                try
                {
                    oldCommandInProgress = swApp.CommandInProgress;
                    swApp.CommandInProgress = true;
                }
                catch { }

                Debug.WriteLine("[CHECK HOLE ASSY] ===== RUN START =====");
                Debug.WriteLine("[CHECK HOLE ASSY] assembly=" + SafeModelTitle(model)
                    + ", configuration=" + SafeConfigurationName(model));
                Debug.WriteLine("[CHECK HOLE ASSY] tolerance position="
                    + FormatMm(PositionToleranceM) + "mm, angle="
                    + AngleToleranceDeg.ToString("0.###", CultureInfo.InvariantCulture) + "deg");
                Debug.WriteLine("[CHECK HOLE ASSY] search position="
                    + FormatMm(SearchPositionToleranceM) + "mm, angle="
                    + SearchAngleToleranceDeg.ToString("0.###", CultureInfo.InvariantCulture)
                    + "deg, adjacentFaceGap=" + FormatMm(AdjacentFaceGapM) + "mm");

                Stopwatch resolveWatch = Stopwatch.StartNew();
                string resolveError;
                if (!ResolveAllLightweightComponents(assembly, out resolveError))
                {
                    resolveWatch.Stop();
                    profile.ResolveMs = resolveWatch.ElapsedMilliseconds;
                    Debug.WriteLine("[CHECK HOLE ASSY] RESOLVE aborted: " + resolveError);
                    MessageBox.Show(
                        resolveError,
                        "CHECK HOLE",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
                resolveWatch.Stop();
                profile.ResolveMs = resolveWatch.ElapsedMilliseconds;

                DeleteOldMarkers(model);

                Stopwatch readWatch = Stopwatch.StartNew();
                DeformedHoleDebugDetector deformedDebug;
                List<HoleAxis> rawHoles = ReadAssemblyHoles(assembly, out deformedDebug);
                readWatch.Stop();
                profile.ComponentTraversalMs = readWatch.ElapsedMilliseconds;
                if (IsCanceled())
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] Canceled while reading components.");
                    MessageBox.Show("Da huy CHECK HOLE.", "CHECK HOLE");
                    return;
                }

                Stopwatch mergeWatch = Stopwatch.StartNew();
                List<HoleAxis> holes = MergeSameComponentCylinders(rawHoles);
                mergeWatch.Stop();
                profile.LogicalMergeMs = mergeWatch.ElapsedMilliseconds;
                Debug.WriteLine("[CHECK HOLE ASSY] logical holes after merge=" + holes.Count);
                if (VerboseHoleDebug)
                {
                    for (int holeIndex = 0; holeIndex < holes.Count; holeIndex++)
                        Debug.WriteLine("[CHECK HOLE ASSY] HOLE #" + (holeIndex + 1) + " " + DescribeHole(holes[holeIndex]));
                }

                // Doi chieu DEBUG sau khi cylinder/SLOT da merge, truoc khi production injection.
                Stopwatch validationDeformedWatch = Stopwatch.StartNew();
                deformedDebug.DebugValidateRecoveredDeformedHoles(holes);
                validationDeformedWatch.Stop();
                profile.DeformedValidationMs = validationDeformedWatch.ElapsedMilliseconds;

                // Production integration: chi them cac cap mouth hop le sau khi cylinder
                // va SLOT da merge. Tat ca stack/pattern/marker phia sau van dung engine cu.
                Stopwatch injectWatch = Stopwatch.StartNew();
                deformedDebug.InjectRecoveredDeformedHoles(holes);
                injectWatch.Stop();
                profile.DeformedInjectMs = injectWatch.ElapsedMilliseconds;
                Debug.WriteLine("[CHECK HOLE ASSY] logical holes after deformed injection=" + holes.Count);

                Stopwatch spatialWatch = Stopwatch.StartNew();
                HoleSpatialIndex spatialIndex = new HoleSpatialIndex(holes, SpatialGridCellSizeM);
                spatialWatch.Stop();
                profile.SpatialIndexMs = spatialWatch.ElapsedMilliseconds;
                Stopwatch normalWatch = Stopwatch.StartNew();
                List<HoleStack> primaryStacks = BuildCandidateStacks(holes, spatialIndex);
                normalWatch.Stop();
                profile.NormalStackMs = normalWatch.ElapsedMilliseconds;
                Stopwatch recoveryWatch = Stopwatch.StartNew();
                List<HoleStack> stacks = BuildRecoveryCandidateStacks(holes, primaryStacks, spatialIndex);
                recoveryWatch.Stop();
                profile.LegacyRecoveryMs = recoveryWatch.ElapsedMilliseconds;
                Stopwatch farWatch = Stopwatch.StartNew();
                stacks = BuildFarMisalignmentStacks(holes, primaryStacks, stacks, spatialIndex);
                farWatch.Stop();
                profile.FarRecoveryMs = farWatch.ElapsedMilliseconds;
                LogStackCoverage(holes, stacks);
                Stopwatch evaluateWatch = Stopwatch.StartNew();
                List<HoleStackResult> results = new List<HoleStackResult>(stacks.Count);
                List<HoleStackResult> ngResults = new List<HoleStackResult>();
                foreach (HoleStack stack in stacks)
                {
                    HoleStackResult result = EvaluateStack(stack);
                    results.Add(result);
                    if (result.IsNg)
                        ngResults.Add(result);
                }
                evaluateWatch.Stop();
                profile.EvaluateMs = evaluateWatch.ElapsedMilliseconds;
                Stopwatch patternWatch = Stopwatch.StartNew();
                List<PatternIssue> patternIssues = BuildPatternIssues(holes, results);
                patternWatch.Stop();
                profile.PatternMs = patternWatch.ElapsedMilliseconds;

                if (ngResults.Count > 0 || patternIssues.Count > 0)
                    CreateNgMarkerSketch(model, ngResults, patternIssues);

                Stopwatch graphicsWatch = Stopwatch.StartNew();
                try
                {
                    profile.GraphicsRefreshCalls++;
                    model.GraphicsRedraw2();
                }
                catch { }
                graphicsWatch.Stop();
                profile.RebuildGraphicsMs += graphicsWatch.ElapsedMilliseconds;
                profile.MarkerGraphicsMs += graphicsWatch.ElapsedMilliseconds;

                int okCount = results.Count - ngResults.Count;
                string message = "Da kiem tra " + results.Count + " cum lo giua cac component."
                    + System.Environment.NewLine + "OK: " + okCount
                     + System.Environment.NewLine + "NG: " + ngResults.Count
                    + System.Environment.NewLine + "NG vi tri/pitch hang lo: " + patternIssues.Count
                    + System.Environment.NewLine + "Dung sai dong tam: "
                    + FormatMm(PositionToleranceM) + " mm"
                    + System.Environment.NewLine + "Dung sai lech goc: "
                    + AngleToleranceDeg.ToString("0.###", CultureInfo.InvariantCulture) + " do";
                if (ngResults.Count > 0 || patternIssues.Count > 0)
                    message += System.Environment.NewLine + "Da tao 3D Sketch " + MarkerFeaturePrefix + " tren Assembly.";
                else
                    message += System.Environment.NewLine + "Khong co vi tri NG can danh dau.";

                string detailText = message + System.Environment.NewLine + System.Environment.NewLine
                    + "Ky hieu tren 3D Sketch:"
                    + System.Environment.NewLine + "+ = tam tham chieu mong doi"
                    + System.Environment.NewLine + "X = tam lo thuc te bi lech"
                    + System.Environment.NewLine + "Duong do = huong va khoang lech"
                    + System.Environment.NewLine + "Chi so sanh cac lop vat lieu ke nhau (khe ho toi da "
                    + FormatMm(AdjacentFaceGapM) + " mm).";

                List<string> issueDescriptions = BuildIssueDescriptions(ngResults, patternIssues, 8);
                if (issueDescriptions.Count > 0)
                    detailText += System.Environment.NewLine + System.Environment.NewLine
                        + "Chi tiet NG:"
                        + System.Environment.NewLine
                        + string.Join(System.Environment.NewLine, issueDescriptions);

                Stopwatch finalSummaryWatch = Stopwatch.StartNew();
                PublishResultText(detailText);

                MessageBox.Show(
                    message,
                    "CHECK HOLE",
                    MessageBoxButtons.OK,
                    (ngResults.Count > 0 || patternIssues.Count > 0)
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information);
                finalSummaryWatch.Stop();
                profile.FinalSummaryUiMs = finalSummaryWatch.ElapsedMilliseconds;

                Debug.WriteLine("[CHECK HOLE ASSY] rawCylinder=" + rawHoles.Count
                    + ", logicalHole=" + holes.Count
                    + ", stack=" + results.Count
                    + ", ng=" + ngResults.Count
                    + ", patternNg=" + patternIssues.Count
                    + ", elapsedMs=" + runWatch.ElapsedMilliseconds);
                Debug.WriteLine("[CHECK HOLE ASSY] ===== RUN END =====");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] ERROR: " + ex);
                PublishResultText("CHECK HOLE bi loi:" + System.Environment.NewLine + ex.Message);
                MessageBox.Show(
                    "Loi khi CHECK HOLE:" + System.Environment.NewLine + ex.Message,
                    "CHECK HOLE",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                runWatch.Stop();
                Debug.WriteLine("[CHECK HOLE ASSY] FINALLY elapsedMs=" + runWatch.ElapsedMilliseconds);
                WriteRunProfile(profile, runWatch.ElapsedMilliseconds);
                try { swApp.CommandInProgress = oldCommandInProgress; } catch { }
                Cursor.Current = previousCursor;
                activeProfile = null;
            }
        }

        private void PublishResultText(string text)
        {
            try { ResultTextChanged?.Invoke(text ?? string.Empty); }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] Cannot publish task pane result: " + ex.Message);
            }
        }

        private static long AddProfileValue(long value)
        {
            return value < 0 ? 0 : value;
        }

        private void WriteRunProfile(RunProfile profile, long totalMs)
        {
            if (profile == null)
                return;

            // Cac timer analytic/deformed/slot/anchor la timer long nhau de chan doan,
            // khong cong vao measuredMs de tranh double-count.
            long measuredMs = AddProfileValue(profile.ValidationMs)
                + AddProfileValue(profile.ResolveMs)
                + AddProfileValue(profile.MarkerSearchMs)
                + AddProfileValue(profile.MarkerDeleteMs)
                + AddProfileValue(profile.ComponentTraversalMs)
                + AddProfileValue(profile.LogicalMergeMs)
                + AddProfileValue(profile.DeformedValidationMs)
                + AddProfileValue(profile.DeformedInjectMs)
                + AddProfileValue(profile.SpatialIndexMs)
                + AddProfileValue(profile.NormalStackMs)
                + AddProfileValue(profile.LegacyRecoveryMs)
                + AddProfileValue(profile.FarRecoveryMs)
                + AddProfileValue(profile.EvaluateMs)
                + AddProfileValue(profile.PatternMs)
                + AddProfileValue(profile.MarkerPrepareMs)
                + AddProfileValue(profile.MarkerOpenSketchMs)
                + AddProfileValue(profile.MarkerGeometryMs)
                + AddProfileValue(profile.MarkerCloseSketchMs)
                + AddProfileValue(profile.MarkerFeatureMs)
                + AddProfileValue(profile.MarkerCleanupMs)
                + AddProfileValue(profile.RebuildGraphicsMs)
                + AddProfileValue(profile.FinalSummaryUiMs)
                + AddProfileValue(profile.DebugLoggingMs);
            long unaccountedMs = Math.Max(0, totalMs - measuredMs);
            double unaccountedPercent = totalMs <= 0 ? 0.0 : unaccountedMs * 100.0 / totalMs;

            Debug.WriteLine("[CHECK HOLE ASSY] PROFILE");
            Debug.WriteLine("validationMs=" + profile.ValidationMs
                + ", resolveMs=" + profile.ResolveMs
                + ", markerSearchMs=" + profile.MarkerSearchMs
                + ", markerDeleteMs=" + profile.MarkerDeleteMs
                + ", componentTraversalMs=" + profile.ComponentTraversalMs
                + ", bodyEnumerationMs=" + profile.BodyEnumerationMs
                + ", analyticReadMs=" + profile.AnalyticReadMs
                + ", deformedDetectMs=" + profile.DeformedDetectMs
                + ", logicalMergeMs=" + profile.LogicalMergeMs
                + ", slotMergeMs=" + profile.SlotMergeMs
                + ", deformedInjectMs=" + profile.DeformedInjectMs);
            Debug.WriteLine("normalStackMs=" + profile.NormalStackMs
                + ", legacyRecoveryMs=" + profile.LegacyRecoveryMs
                + ", relationshipAnchorMs=" + profile.RelationshipAnchorMs
                + ", farRecoveryMs=" + profile.FarRecoveryMs
                + ", evaluateMs=" + profile.EvaluateMs
                + ", patternMs=" + profile.PatternMs
                + ", patternPrepareMs=" + profile.PatternPrepareMs
                + ", patternAssignmentMs=" + profile.PatternAssignmentMs
                + ", patternEvaluationMs=" + profile.PatternEvaluationMs);
            Debug.WriteLine("markerPrepareMs=" + profile.MarkerPrepareMs
                + ", markerOpenSketchMs=" + profile.MarkerOpenSketchMs
                + ", markerGeometryMs=" + profile.MarkerGeometryMs
                + ", markerCloseSketchMs=" + profile.MarkerCloseSketchMs
                + ", markerEndSketchSetupMs=" + profile.MarkerEndSketchSetupMs
                + ", markerEndSketchApiMs=" + profile.MarkerEndSketchApiMs
                + ", markerSketchSolverMs=" + profile.MarkerSketchSolverMs
                + ", markerFeatureTreeUpdateMs=" + profile.MarkerFeatureTreeUpdateMs
                + ", markerDisplayUpdateMs=" + profile.MarkerDisplayUpdateMs
                + ", markerPostCloseLookupMs=" + profile.MarkerPostCloseLookupMs
                + ", markerRenameMs=" + profile.MarkerRenameMs
                + ", markerColorMs=" + profile.MarkerColorMs
                + ", markerSelectionMs=" + profile.MarkerSelectionMs
                + ", markerRebuildMs=" + profile.MarkerRebuildMs
                + ", markerGraphicsMs=" + profile.MarkerGraphicsMs
                + ", markerFeatureMs=" + profile.MarkerFeatureMs
                + ", markerCleanupMs=" + profile.MarkerCleanupMs
                + ", rebuildGraphicsMs=" + profile.RebuildGraphicsMs
                + ", finalSummaryUiMs=" + profile.FinalSummaryUiMs
                + ", debugLoggingMs=" + profile.DebugLoggingMs
                + ", otherMs=" + unaccountedMs
                + ", TOTAL=" + totalMs);
            Debug.WriteLine("[CHECK HOLE ASSY] PROFILE COM"
                + " componentGetBodiesCalls=" + profile.ComponentGetBodiesCalls
                + ", faceEnumerationCalls=" + profile.FaceEnumerationCalls
                + ", surfaceCalls=" + profile.SurfaceCalls
                + ", edgeCalls=" + profile.EdgeCalls
                + ", transformCalls=" + profile.TransformCalls
                + ", selectionCalls=" + profile.SelectionCalls
                + ", sketchCreateCalls=" + profile.SketchCreateCalls
                + ", featureCalls=" + profile.FeatureCalls
                + ", rebuildCalls=" + profile.RebuildCalls
                + ", graphicsRefreshCalls=" + profile.GraphicsRefreshCalls);
            Debug.WriteLine("[CHECK HOLE ASSY] PROFILE ACCOUNTING"
                + " totalMs=" + totalMs
                + ", measuredMs=" + measuredMs
                + ", unaccountedMs=" + unaccountedMs
                + ", unaccountedPercent=" + unaccountedPercent.ToString("0.0", CultureInfo.InvariantCulture));
        }

        private static bool ResolveAllLightweightComponents(
            AssemblyDoc assembly,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            if (assembly == null)
            {
                errorMessage = "Khong tim thay Assembly de bo Lightweight.";
                return false;
            }

            int before;
            try
            {
                before = assembly.GetLightWeightComponentCount();
            }
            catch (Exception ex)
            {
                errorMessage = "Khong doc duoc trang thai Lightweight: " + ex.Message;
                return false;
            }

            Debug.WriteLine("[CHECK HOLE ASSY] RESOLVE lightweightBefore=" + before);
            if (before <= 0)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] RESOLVE skipped: assembly already resolved.");
                return true;
            }

            int status;
            try
            {
                status = assembly.ResolveAllLightWeightComponents(false);
                Application.DoEvents();
            }
            catch (Exception ex)
            {
                errorMessage = "Khong the bo Lightweight: " + ex.Message;
                return false;
            }

            int after;
            try
            {
                after = assembly.GetLightWeightComponentCount();
            }
            catch (Exception ex)
            {
                errorMessage = "Da goi bo Lightweight nhung khong kiem tra lai duoc: " + ex.Message;
                return false;
            }

            Debug.WriteLine("[CHECK HOLE ASSY] RESOLVE status=" + status
                + ", lightweightAfter=" + after);

            if (status != (int)swComponentResolveStatus_e.swResolveOk || after > 0)
            {
                errorMessage = "Khong bo duoc toan bo Lightweight. Status=" + status
                    + ", con lai=" + after
                    + ". Hay kich hoat Assembly trong cua so rieng va thu lai.";
                return false;
            }

            Debug.WriteLine("[CHECK HOLE ASSY] RESOLVE completed. No rebuild and no save requested.");
            return true;
        }

        private List<HoleAxis> ReadAssemblyHoles(
            AssemblyDoc assembly,
            out DeformedHoleDebugDetector deformedDebug)
        {
            List<HoleAxis> result = new List<HoleAxis>();
            deformedDebug = new DeformedHoleDebugDetector(this);
            object[] componentObjects = ToObjectArray(assembly.GetComponents(false));
            Debug.WriteLine("[CHECK HOLE ASSY] component occurrences=" + componentObjects.Length);

            for (int componentIndex = 0; componentIndex < componentObjects.Length; componentIndex++)
            {
                if ((componentIndex & 7) == 0)
                {
                    Application.DoEvents();
                    if (IsCanceled())
                        break;
                }

                Component2 component = componentObjects[componentIndex] as Component2;
                if (!IsUsableComponent(component))
                {
                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] skip component index=" + componentIndex
                            + ", name=" + SafeComponentName(component)
                            + ", reason=suppressed/envelope/unavailable");
                    continue;
                }

                MathTransform transform = null;
                try
                {
                    if (activeProfile != null) activeProfile.TransformCalls++;
                    transform = component.Transform2;
                }
                catch { }
                if (transform == null)
                {
                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] skip component=" + SafeComponentName(component)
                            + ", reason=no assembly transform");
                    continue;
                }

                string occurrence = SafeComponentName(component);
                string path = SafeComponentPath(component);
                string referencedConfiguration = SafeReferencedConfiguration(component);
                object[] bodies = GetComponentBodies(component);
                int before = result.Count;

                if (bodies.Length == 0 && VerboseHoleDebug)
                    Debug.WriteLine("[CHECK HOLE ASSY] component=" + occurrence + ", reason=no solid body");

                for (int bodyIndex = 0; bodyIndex < bodies.Length; bodyIndex++)
                {
                    Stopwatch bodyEnumerationWatch = Stopwatch.StartNew();
                    object bodyObject = bodies[bodyIndex];
                    Body2 body = bodyObject as Body2;
                    if (body == null)
                    {
                        bodyEnumerationWatch.Stop();
                        if (activeProfile != null)
                            activeProfile.BodyEnumerationMs += bodyEnumerationWatch.ElapsedMilliseconds;
                        continue;
                    }

                    string bodyName = ResolveBodyIdentity(body, bodyIndex);

                    if (activeProfile != null) activeProfile.FaceEnumerationCalls++;
                    object[] faceObjects = ToObjectArray(body.GetFaces());
                    List<HoleAxis> bodyCylinders = new List<HoleAxis>();
                    foreach (object faceObject in faceObjects)
                    {
                        Face2 face = faceObject as Face2;
                        HoleAxis cylinder;
                        if (TryReadHoleCylinder(face, transform, occurrence, path, bodyName, out cylinder))
                        {
                            result.Add(cylinder);
                            bodyCylinders.Add(cylinder);
                        }
                    }

                    // Detector chi doc topology va tao record managed-memory; khong sua Body.
                    Stopwatch deformedWatch = Stopwatch.StartNew();
                    deformedDebug.ProcessBodyOccurrence(
                        body,
                        faceObjects,
                        bodyIndex,
                        transform,
                        occurrence,
                        path,
                        referencedConfiguration,
                        bodyCylinders);
                    deformedWatch.Stop();
                    if (activeProfile != null)
                        activeProfile.DeformedDetectMs += deformedWatch.ElapsedMilliseconds;
                    bodyEnumerationWatch.Stop();
                    if (activeProfile != null)
                        activeProfile.BodyEnumerationMs += bodyEnumerationWatch.ElapsedMilliseconds;
                }

                Debug.WriteLine("[CHECK HOLE ASSY] component=" + occurrence
                    + ", bodies=" + bodies.Length
                    + ", fullCylinder=" + (result.Count - before));
            }

            deformedDebug.LogRunSummary();
            return result;
        }

        private bool TryReadHoleCylinder(
            Face2 face,
            MathTransform transform,
            string occurrence,
            string path,
            string bodyName,
            out HoleAxis hole)
        {
            hole = null;
            if (face == null || transform == null || mathUtility == null)
                return false;

            Stopwatch analyticWatch = Stopwatch.StartNew();
            try
            {
                if (activeProfile != null) activeProfile.SurfaceCalls++;
                Surface surface = face.GetSurface() as Surface;
                if (surface == null || !surface.IsCylinder())
                    return false;

                // FaceInSurfaceSense=true la mat phia trong cua tru, phu hop voi thanh lo.
                if (!face.FaceInSurfaceSense())
                    return false;

                double[] cylinder = ToDoubleArray(surface.CylinderParams);
                if (cylinder.Length < 7 || cylinder[6] <= 1e-8)
                    return false;

                double[] localOrigin = { cylinder[0], cylinder[1], cylinder[2] };
                double[] localAxis = { cylinder[3], cylinder[4], cylinder[5] };
                double[] origin = TransformPoint(localOrigin, transform);
                double[] axis = TransformVector(localAxis, transform);
                axis = Normalize(axis);
                if (!IsPoint(origin) || !IsDirection(axis))
                    return false;
                CanonicalizeDirection(axis);

                List<double[]> circularEdgeCenters = new List<double[]>();
                if (activeProfile != null) activeProfile.EdgeCalls++;
                foreach (object edgeObject in ToObjectArray(face.GetEdges()))
                {
                    Edge edge = edgeObject as Edge;
                    Curve curve = edge == null ? null : edge.GetCurve() as Curve;
                    if (curve == null || !curve.IsCircle())
                        continue;

                    double[] circle = ToDoubleArray(curve.CircleParams);
                    if (circle.Length < 7)
                        continue;
                    double[] center = TransformPoint(
                        new[] { circle[0], circle[1], circle[2] },
                        transform);
                    if (IsPoint(center) && !ContainsNearPoint(circularEdgeCenters, center, 1e-8))
                        circularEdgeCenters.Add(center);
                }

                double minProjection = double.MaxValue;
                double maxProjection = double.MinValue;
                bool usedCurvedBoundaryFallback = false;
                int curvedBoundarySampleCount = 0;
                if (circularEdgeCenters.Count >= 2)
                {
                    // Giu nguyen logic cu: lay hai tam cua hai bien tron.
                    foreach (double[] center in circularEdgeCenters)
                    {
                        double projection = Dot(center, axis);
                        minProjection = Math.Min(minProjection, projection);
                        maxProjection = Math.Max(maxProjection, projection);
                    }
                }
                else
                {
                    // Mat cong/xoan co the lam bien giao cua lo khong con duoc API bao la Circle.
                    // Chi dung nhanh du phong nay khi logic cu khong lay du hai tam bien tron.
                    if (!TryReadCurvedCylinderAxialRange(
                        face,
                        transform,
                        axis,
                        out minProjection,
                        out maxProjection,
                        out curvedBoundarySampleCount))
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] CURVED_CYLINDER_FALLBACK rejected. component="
                            + occurrence + ", circleEdges=" + circularEdgeCenters.Count);
                        return false;
                    }
                    usedCurvedBoundaryFallback = true;
                }
                double axialLength = maxProjection - minProjection;
                if (axialLength <= 1e-8)
                    return false;

                double area = face.GetArea();
                double sweep = area / (cylinder[6] * axialLength);
                bool isFullCylinder = !double.IsNaN(sweep) && !double.IsInfinity(sweep)
                    && sweep >= FullCylinderMinimumSweepRad;
                bool isSlotEnd = !double.IsNaN(sweep) && !double.IsInfinity(sweep)
                    && sweep >= SlotEndMinimumSweepRad
                    && sweep <= SlotEndMaximumSweepRad;
                if (!isFullCylinder && !isSlotEnd)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] skip partial cylinder. component="
                        + occurrence + ", sweep="
                        + sweep.ToString("0.###", CultureInfo.InvariantCulture));
                    return false;
                }

                double[] linePoint = Subtract(origin, Scale(axis, Dot(origin, axis)));
                double middleProjection = (minProjection + maxProjection) * 0.5;
                hole = new HoleAxis
                {
                    ComponentOccurrence = occurrence,
                    ComponentPath = path,
                    BodyName = bodyName,
                    Point = linePoint,
                    Direction = axis,
                    Center = Add(linePoint, Scale(axis, middleProjection)),
                    MinProjection = minProjection,
                    MaxProjection = maxProjection,
                    RadiusM = cylinder[6],
                    SourceFaceCount = 1,
                    SweepRad = sweep,
                    IsSlotEnd = isSlotEnd,
                    IsCurvedBoundaryFallback = usedCurvedBoundaryFallback,
                    Source = usedCurvedBoundaryFallback
                        ? HoleSource.CurvedCylinder
                        : HoleSource.AnalyticCylinder
                };
                if (VerboseHoleDebug)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] CYLINDER accepted " + DescribeHole(hole)
                        + ", sweep=" + sweep.ToString("0.###", CultureInfo.InvariantCulture) + "rad"
                        + (usedCurvedBoundaryFallback
                            ? ", source=CURVED_BOUNDARY_FALLBACK, samples=" + curvedBoundarySampleCount
                            : ", source=CIRCULAR_EDGES"));
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] read cylinder failed. component="
                    + occurrence + ", error=" + ex.Message);
                return false;
            }
            finally
            {
                analyticWatch.Stop();
                if (activeProfile != null)
                    activeProfile.AnalyticReadMs += analyticWatch.ElapsedMilliseconds;
            }
        }

        private bool TryReadCurvedCylinderAxialRange(
            Face2 face,
            MathTransform transform,
            double[] axis,
            out double minProjection,
            out double maxProjection,
            out int sampleCount)
        {
            minProjection = double.MaxValue;
            maxProjection = double.MinValue;
            sampleCount = 0;
            if (face == null || transform == null || !IsDirection(axis))
                return false;

            // Tessellation cua chinh mat tru bao gom cac diem tren bien trim thuc te.
            // Chieu cac diem nay len truc tru se cho hai dau cua lo ngay ca khi tam
            // bien giao khong the doc bang Curve.CircleParams.
            try
            {
                double[] tessellation = ToDoubleArray(face.GetTessTriangles(true));
                for (int index = 0; index + 2 < tessellation.Length; index += 3)
                {
                    double[] point = TransformPoint(
                        new[]
                        {
                            tessellation[index],
                            tessellation[index + 1],
                            tessellation[index + 2]
                        },
                        transform);
                    if (!IsPoint(point))
                        continue;

                    double projection = Dot(point, axis);
                    minProjection = Math.Min(minProjection, projection);
                    maxProjection = Math.Max(maxProjection, projection);
                    sampleCount++;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] CURVED_CYLINDER_FALLBACK tessellation failed: "
                    + ex.Message);
            }

            // Du phong nhe neu tessellation khong co san: lay cac dinh bien cua mat tru.
            if (sampleCount < 2)
            {
                if (activeProfile != null) activeProfile.EdgeCalls++;
                foreach (object edgeObject in ToObjectArray(face.GetEdges()))
                {
                    Edge edge = edgeObject as Edge;
                    if (edge == null)
                        continue;

                    AddCylinderBoundaryVertexProjection(
                        edge.GetStartVertex() as Vertex,
                        transform,
                        axis,
                        ref minProjection,
                        ref maxProjection,
                        ref sampleCount);
                    AddCylinderBoundaryVertexProjection(
                        edge.GetEndVertex() as Vertex,
                        transform,
                        axis,
                        ref minProjection,
                        ref maxProjection,
                        ref sampleCount);
                }
            }

            return sampleCount >= 2
                && minProjection < double.MaxValue
                && maxProjection > double.MinValue
                && maxProjection - minProjection > 1e-8;
        }

        private void AddCylinderBoundaryVertexProjection(
            Vertex vertex,
            MathTransform transform,
            double[] axis,
            ref double minProjection,
            ref double maxProjection,
            ref int sampleCount)
        {
            if (vertex == null)
                return;
            try
            {
                double[] localPoint = ToDoubleArray(vertex.GetPoint());
                double[] point = TransformPoint(localPoint, transform);
                if (!IsPoint(point))
                    return;

                double projection = Dot(point, axis);
                minProjection = Math.Min(minProjection, projection);
                maxProjection = Math.Max(maxProjection, projection);
                sampleCount++;
            }
            catch
            {
                // Khong lam anh huong logic cu neu mot vertex khong doc duoc.
            }
        }

        private List<HoleAxis> MergeSameComponentCylinders(List<HoleAxis> raw)
        {
            List<HoleAxis> merged = new List<HoleAxis>();
            foreach (IGrouping<string, HoleAxis> componentGroup in raw.GroupBy(
                item => item.ComponentOccurrence,
                StringComparer.OrdinalIgnoreCase))
            {
                foreach (HoleAxis item in componentGroup)
                {
                    HoleAxis match = merged.FirstOrDefault(candidate =>
                        string.Equals(candidate.ComponentOccurrence, item.ComponentOccurrence, StringComparison.OrdinalIgnoreCase)
                        && AxisAngleDeg(candidate.Direction, item.Direction) <= SameHoleAngleToleranceDeg
                        && TransverseCenterDistance(candidate, item) <= SameHolePositionToleranceM
                        && AxialGap(candidate, item) <= SameHoleAxialGapM);

                    if (match == null)
                    {
                        merged.Add(item.Clone());
                        if (VerboseHoleDebug)
                            Debug.WriteLine("[CHECK HOLE ASSY] MERGE new logical hole: " + DescribeHole(item));
                        continue;
                    }

                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] MERGE same component cylinder: component="
                            + item.ComponentOccurrence + ", distance=" + FormatMm(TransverseCenterDistance(match, item))
                            + "mm, angle=" + AxisAngleDeg(match.Direction, item.Direction)
                                .ToString("0.###", CultureInfo.InvariantCulture)
                            + "deg, axialGap=" + FormatMm(AxialGap(match, item)) + "mm");
                    match.MinProjection = Math.Min(match.MinProjection, item.MinProjection);
                    match.MaxProjection = Math.Max(match.MaxProjection, item.MaxProjection);
                    match.RadiusM = Math.Min(match.RadiusM, item.RadiusM);
                    match.SourceFaceCount += item.SourceFaceCount;
                    match.Center = Add(
                        match.Point,
                        Scale(match.Direction, (match.MinProjection + match.MaxProjection) * 0.5));
                }
            }
            Stopwatch slotWatch = Stopwatch.StartNew();
            List<HoleAxis> slotMerged = MergeSlotEnds(merged);
            slotWatch.Stop();
            if (activeProfile != null)
                activeProfile.SlotMergeMs += slotWatch.ElapsedMilliseconds;
            return slotMerged;
        }

        private List<HoleAxis> MergeSlotEnds(List<HoleAxis> source)
        {
            List<HoleAxis> result = source.Where(item => !item.IsSlotEnd).ToList();
            List<HoleAxis> ends = source.Where(item => item.IsSlotEnd).ToList();
            HashSet<HoleAxis> used = new HashSet<HoleAxis>();

            foreach (HoleAxis first in ends)
            {
                if (used.Contains(first))
                    continue;

                HoleAxis second = ends
                    .Where(item => !ReferenceEquals(item, first)
                        && !used.Contains(item)
                        && string.Equals(item.ComponentOccurrence, first.ComponentOccurrence, StringComparison.OrdinalIgnoreCase)
                        && AxisAngleDeg(item.Direction, first.Direction) <= SameHoleAngleToleranceDeg
                        && Math.Abs(item.RadiusM - first.RadiusM) <= SlotPairToleranceM
                        && Math.Abs(item.MinProjection - first.MinProjection) <= SlotPairToleranceM
                        && Math.Abs(item.MaxProjection - first.MaxProjection) <= SlotPairToleranceM
                        && AxialGap(item, first) <= SameHoleAxialGapM)
                    .Select(item => new
                    {
                        Hole = item,
                        Distance = TransverseCenterDistance(first, item)
                    })
                    .Where(item => item.Distance >= Math.Max(first.RadiusM, item.Hole.RadiusM) * 1.25
                        && item.Distance <= SlotMaximumCenterDistanceM)
                    .OrderBy(item => item.Distance)
                    .Select(item => item.Hole)
                    .FirstOrDefault();

                if (second == null)
                {
                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] SLOT end unmatched: " + DescribeHole(first));
                    continue;
                }

                used.Add(first);
                used.Add(second);
                double[] center = Scale(Add(first.Center, second.Center), 0.5);
                double[] direction = Normalize(Add(first.Direction, second.Direction));
                if (!IsDirection(direction))
                    direction = ClonePoint(first.Direction);
                CanonicalizeDirection(direction);
                double minProjection = Math.Min(first.MinProjection, second.MinProjection);
                double maxProjection = Math.Max(first.MaxProjection, second.MaxProjection);
                HoleAxis slot = new HoleAxis
                {
                    ComponentOccurrence = first.ComponentOccurrence,
                    ComponentPath = first.ComponentPath,
                    BodyName = first.BodyName,
                    Direction = direction,
                    Center = center,
                    Point = Subtract(center, Scale(direction, Dot(center, direction))),
                    MinProjection = minProjection,
                    MaxProjection = maxProjection,
                    RadiusM = Math.Min(first.RadiusM, second.RadiusM),
                    SourceFaceCount = first.SourceFaceCount + second.SourceFaceCount,
                    IsSlot = true,
                    Source = HoleSource.Slot,
                    SweepRad = first.SweepRad + second.SweepRad,
                    IsCurvedBoundaryFallback = first.IsCurvedBoundaryFallback
                        || second.IsCurvedBoundaryFallback
                };
                result.Add(slot);
                if (VerboseHoleDebug)
                    Debug.WriteLine("[CHECK HOLE ASSY] SLOT accepted: " + DescribeHole(slot)
                        + ", firstEnd=" + FormatPointMm(first.Center)
                        + ", secondEnd=" + FormatPointMm(second.Center)
                        + ", midpoint=" + FormatPointMm(slot.Center)
                        + ", endDistance=" + FormatMm(TransverseCenterDistance(first, second)) + "mm");
            }

            Debug.WriteLine("[CHECK HOLE ASSY] SLOT summary: ends=" + ends.Count
                + ", slots=" + result.Count(item => item.IsSlot)
                + ", unmatchedEnds=" + (ends.Count - used.Count));
            return result;
        }

        private List<HoleStack> BuildCandidateStacks(
            List<HoleAxis> holes,
            HoleSpatialIndex spatialIndex)
        {
            spatialIndex.ResetStats();
            int count = holes.Count;
            List<HolePairCandidate> candidates = new List<HolePairCandidate>();
            int comparedPairs = 0;
            int sameComponentPairs = 0;
            int rejectedAngle = 0;
            int rejectedPosition = 0;
            int rejectedNotAdjacent = 0;
            int acceptedPairs = 0;
            int detailedRejectLogs = 0;
            for (int i = 0; i < count; i++)
            {
                if ((i & 31) == 0)
                {
                    Application.DoEvents();
                    if (IsCanceled())
                        break;
                }

                foreach (int j in spatialIndex.QueryForHole(i, SearchPositionToleranceM, AdjacentFaceGapM))
                {
                    if (j <= i)
                        continue;
                    comparedPairs++;
                    if (string.Equals(
                        holes[i].ComponentOccurrence,
                        holes[j].ComponentOccurrence,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        sameComponentPairs++;
                        continue;
                    }

                    double angle = AxisAngleDeg(holes[i].Direction, holes[j].Direction);
                    double position = TransverseCenterDistance(holes[i], holes[j]);
                    double axialGap = AxialGap(holes[i], holes[j]);
                    if (!IsSearchAngleCompatible(holes[i], holes[j], angle))
                    {
                        rejectedAngle++;
                        continue;
                    }
                    if (position > SearchPositionToleranceM)
                    {
                        rejectedPosition++;
                        if (VerboseHoleDebug && detailedRejectLogs < 200 && axialGap <= AdjacentFaceGapM)
                        {
                            Debug.WriteLine("[CHECK HOLE ASSY] PAIR reject position: "
                                + holes[i].ComponentOccurrence + " <-> " + holes[j].ComponentOccurrence
                                + ", offset=" + FormatMm(position) + "mm"
                                + ", angle=" + angle.ToString("0.###", CultureInfo.InvariantCulture) + "deg"
                                + ", axialGap=" + FormatMm(axialGap) + "mm"
                                + ", centerA=" + FormatPointMm(holes[i].Center)
                                + ", centerB=" + FormatPointMm(holes[j].Center));
                            detailedRejectLogs++;
                        }
                        continue;
                    }
                    if (!AreHoleLayersAdjacent(holes[i], holes[j], out axialGap))
                    {
                        rejectedNotAdjacent++;
                        if (VerboseHoleDebug && detailedRejectLogs < 200)
                        {
                            Debug.WriteLine("[CHECK HOLE ASSY] PAIR reject not adjacent: "
                                + holes[i].ComponentOccurrence + " <-> " + holes[j].ComponentOccurrence
                                + ", faceGap=" + FormatMm(axialGap) + "mm"
                                + ", max=" + FormatMm(AdjacentFaceGapM) + "mm");
                            detailedRejectLogs++;
                        }
                        continue;
                    }

                    acceptedPairs++;
                    candidates.Add(new HolePairCandidate
                    {
                        FirstIndex = i,
                        SecondIndex = j,
                        PositionM = position,
                        AngleDeg = angle,
                        AxialGapM = axialGap
                    });
                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] PAIR accepted: "
                            + holes[i].ComponentOccurrence + " <-> " + holes[j].ComponentOccurrence
                            + ", offset=" + FormatMm(position) + "mm"
                            + ", angle=" + angle.ToString("0.###", CultureInfo.InvariantCulture) + "deg"
                            + ", axialGap=" + FormatMm(axialGap) + "mm");
                }
            }

            candidates = candidates
                .OrderBy(item => item.PositionM)
                .ThenBy(item => item.AngleDeg)
                .ThenBy(item => item.AxialGapM)
                .ToList();

            HoleCluster[] clusterOf = new HoleCluster[count];
            List<HoleCluster> clusters = new List<HoleCluster>();
            for (int i = 0; i < count; i++)
            {
                HoleCluster cluster = new HoleCluster();
                cluster.MemberIndexes.Add(i);
                cluster.ComponentOccurrences.Add(holes[i].ComponentOccurrence);
                clusterOf[i] = cluster;
                clusters.Add(cluster);
            }

            int clusterMerges = 0;
            int rejectedDuplicateComponent = 0;
            int rejectedTransitiveGeometry = 0;
            foreach (HolePairCandidate candidate in candidates)
            {
                HoleCluster firstCluster = clusterOf[candidate.FirstIndex];
                HoleCluster secondCluster = clusterOf[candidate.SecondIndex];
                if (ReferenceEquals(firstCluster, secondCluster))
                    continue;

                if (firstCluster.ComponentOccurrences.Overlaps(secondCluster.ComponentOccurrences))
                {
                    rejectedDuplicateComponent++;
                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] CLUSTER reject duplicate component: "
                            + holes[candidate.FirstIndex].ComponentOccurrence + " <-> "
                            + holes[candidate.SecondIndex].ComponentOccurrence);
                    continue;
                }

                if (!CanMergeHoleClusters(firstCluster, secondCluster, holes))
                {
                    rejectedTransitiveGeometry++;
                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] CLUSTER reject transitive geometry: "
                            + holes[candidate.FirstIndex].ComponentOccurrence + " <-> "
                            + holes[candidate.SecondIndex].ComponentOccurrence);
                    continue;
                }

                if (firstCluster.MemberIndexes.Count < secondCluster.MemberIndexes.Count)
                {
                    HoleCluster temporary = firstCluster;
                    firstCluster = secondCluster;
                    secondCluster = temporary;
                }

                foreach (int memberIndex in secondCluster.MemberIndexes)
                {
                    firstCluster.MemberIndexes.Add(memberIndex);
                    clusterOf[memberIndex] = firstCluster;
                }
                firstCluster.ComponentOccurrences.UnionWith(secondCluster.ComponentOccurrences);
                clusters.Remove(secondCluster);
                clusterMerges++;
            }

            List<HoleStack> stacks = clusters
                .Where(cluster => cluster.ComponentOccurrences.Count >= 2)
                .Select(cluster =>
                {
                    HoleStack stack = new HoleStack();
                    foreach (int memberIndex in cluster.MemberIndexes)
                        stack.Holes.Add(holes[memberIndex]);
                    return stack;
                })
                .ToList();

            Debug.WriteLine("[CHECK HOLE ASSY] PAIR summary: compared=" + comparedPairs
                + ", broadPhaseCandidates=" + spatialIndex.LastQueryCandidateCount
                + ", sameComponent=" + sameComponentPairs
                + ", rejectAngle=" + rejectedAngle
                + ", rejectPosition=" + rejectedPosition
                + ", rejectNotAdjacent=" + rejectedNotAdjacent
                + ", candidate=" + acceptedPairs
                + ", clusterMerge=" + clusterMerges
                + ", rejectDuplicateComponent=" + rejectedDuplicateComponent
                + ", rejectTransitiveGeometry=" + rejectedTransitiveGeometry
                + ", stacks=" + stacks.Count);
            return stacks;
        }

        /// <summary>
        /// Luot 2 chi xu ly nhung lo chua duoc ghep o luot nhanh 3 mm.
        /// Mot lo bi bo lai co the ghep vao cum da co, hoac ghep voi lo bi bo lai khac.
        /// De tranh ghep nham lo pattern ke ben, cap phai la gan nhat hai chieu va
        /// khoang cach khong vuot qua 45% buoc lo cuc bo (toi da 25 mm).
        /// </summary>
        private List<HoleStack> BuildRecoveryCandidateStacks(
            List<HoleAxis> holes,
            List<HoleStack> primaryStacks,
            HoleSpatialIndex spatialIndex)
        {
            spatialIndex.ResetStats();
            if (holes == null || holes.Count == 0)
                return new List<HoleStack>();

            int count = holes.Count;
            bool[] primaryMember = new bool[count];
            HoleCluster[] clusterOf = new HoleCluster[count];
            List<HoleCluster> clusters = new List<HoleCluster>();

            for (int stackIndex = 0; stackIndex < primaryStacks.Count; stackIndex++)
            {
                HoleCluster cluster = new HoleCluster();
                cluster.PrimaryStackIds.Add(stackIndex);
                foreach (HoleAxis hole in primaryStacks[stackIndex].Holes)
                {
                    int holeIndex = holes.IndexOf(hole);
                    if (holeIndex < 0 || clusterOf[holeIndex] != null)
                        continue;
                    primaryMember[holeIndex] = true;
                    cluster.MemberIndexes.Add(holeIndex);
                    cluster.ComponentOccurrences.Add(holes[holeIndex].ComponentOccurrence);
                    clusterOf[holeIndex] = cluster;
                }
                if (cluster.MemberIndexes.Count > 0)
                    clusters.Add(cluster);
            }

            for (int index = 0; index < count; index++)
            {
                if (clusterOf[index] != null)
                    continue;
                HoleCluster cluster = new HoleCluster();
                cluster.MemberIndexes.Add(index);
                cluster.ComponentOccurrences.Add(holes[index].ComponentOccurrence);
                clusterOf[index] = cluster;
                clusters.Add(cluster);
            }

            int unmatchedBefore = primaryMember.Count(value => !value);
            if (unmatchedBefore == 0)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] RECOVERY skipped: no unmatched hole.");
                return primaryStacks;
            }

            double[] localPitch = new double[count];
            for (int index = 0; index < count; index++)
                localPitch[index] = EstimateLocalHolePitch(index, holes);

            List<HolePairCandidate> candidates = new List<HolePairCandidate>();
            int geometryCandidates = 0;
            int rejectedNotMutual = 0;
            int detailedLogs = 0;
            for (int firstIndex = 0; firstIndex < count; firstIndex++)
            {
                if ((firstIndex & 31) == 0)
                {
                    Application.DoEvents();
                    if (IsCanceled())
                        break;
                }

                foreach (int secondIndex in spatialIndex.QueryForHole(
                    firstIndex,
                    RecoveryMaximumPositionM,
                    AdjacentFaceGapM))
                {
                    if (secondIndex <= firstIndex)
                        continue;
                    // Khong thay doi hai cum ma luot nhanh da xac dinh.
                    if (primaryMember[firstIndex] && primaryMember[secondIndex])
                        continue;
                    if (string.Equals(
                        holes[firstIndex].ComponentOccurrence,
                        holes[secondIndex].ComponentOccurrence,
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    double angle = AxisAngleDeg(holes[firstIndex].Direction, holes[secondIndex].Direction);
                    double axialGap = AxialGap(holes[firstIndex], holes[secondIndex]);
                    double position = TransverseCenterDistance(holes[firstIndex], holes[secondIndex]);
                    double recoveryLimit = GetRecoveryPositionLimit(
                        localPitch[firstIndex],
                        localPitch[secondIndex]);
                    if (!IsSearchAngleCompatible(holes[firstIndex], holes[secondIndex], angle)
                        || !AreHoleLayersAdjacent(holes[firstIndex], holes[secondIndex], out axialGap)
                        || position > recoveryLimit)
                        continue;

                    geometryCandidates++;
                    int nearestFromFirst = FindNearestRecoveryIndex(
                        firstIndex,
                        holes[secondIndex].ComponentOccurrence,
                        holes,
                        primaryMember,
                        localPitch,
                        spatialIndex);
                    int nearestFromSecond = FindNearestRecoveryIndex(
                        secondIndex,
                        holes[firstIndex].ComponentOccurrence,
                        holes,
                        primaryMember,
                        localPitch,
                        spatialIndex);
                    if (nearestFromFirst != secondIndex || nearestFromSecond != firstIndex)
                    {
                        rejectedNotMutual++;
                        continue;
                    }

                    candidates.Add(new HolePairCandidate
                    {
                        FirstIndex = firstIndex,
                        SecondIndex = secondIndex,
                        PositionM = position,
                        AngleDeg = angle,
                        AxialGapM = axialGap
                    });
                    if (VerboseHoleDebug && detailedLogs < 300)
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] RECOVERY pair accepted: "
                            + holes[firstIndex].ComponentOccurrence + " <-> "
                            + holes[secondIndex].ComponentOccurrence
                            + ", offset=" + FormatMm(position) + "mm"
                            + ", limit=" + FormatMm(recoveryLimit) + "mm"
                            + ", pitchA=" + FormatOptionalMm(localPitch[firstIndex])
                            + ", pitchB=" + FormatOptionalMm(localPitch[secondIndex])
                            + ", angle=" + angle.ToString("0.###", CultureInfo.InvariantCulture) + "deg");
                        detailedLogs++;
                    }
                }
            }

            candidates = candidates
                .OrderBy(item => item.PositionM)
                .ThenBy(item => item.AngleDeg)
                .ThenBy(item => item.AxialGapM)
                .ToList();

            int clusterMerges = 0;
            int rejectedPrimaryBridge = 0;
            int rejectedDuplicateComponent = 0;
            int rejectedGeometry = 0;
            foreach (HolePairCandidate candidate in candidates)
            {
                HoleCluster firstCluster = clusterOf[candidate.FirstIndex];
                HoleCluster secondCluster = clusterOf[candidate.SecondIndex];
                if (ReferenceEquals(firstCluster, secondCluster))
                    continue;

                // Khong cho luot phuc hoi noi hai cum hop le cu thanh mot cum lon.
                if (firstCluster.PrimaryStackIds.Count > 0
                    && secondCluster.PrimaryStackIds.Count > 0)
                {
                    rejectedPrimaryBridge++;
                    continue;
                }
                if (firstCluster.ComponentOccurrences.Overlaps(secondCluster.ComponentOccurrences))
                {
                    rejectedDuplicateComponent++;
                    continue;
                }
                if (!CanMergeRecoveryClusters(firstCluster, secondCluster, holes, localPitch))
                {
                    rejectedGeometry++;
                    continue;
                }

                if (firstCluster.MemberIndexes.Count < secondCluster.MemberIndexes.Count)
                {
                    HoleCluster temporary = firstCluster;
                    firstCluster = secondCluster;
                    secondCluster = temporary;
                }
                foreach (int memberIndex in secondCluster.MemberIndexes)
                {
                    firstCluster.MemberIndexes.Add(memberIndex);
                    clusterOf[memberIndex] = firstCluster;
                }
                firstCluster.ComponentOccurrences.UnionWith(secondCluster.ComponentOccurrences);
                firstCluster.PrimaryStackIds.UnionWith(secondCluster.PrimaryStackIds);
                clusters.Remove(secondCluster);
                clusterMerges++;
            }

            List<HoleStack> stacks = clusters
                .Where(cluster => cluster.ComponentOccurrences.Count >= 2)
                .Select(cluster =>
                {
                    HoleStack stack = new HoleStack();
                    foreach (int memberIndex in cluster.MemberIndexes)
                        stack.Holes.Add(holes[memberIndex]);
                    return stack;
                })
                .ToList();

            HashSet<HoleAxis> recoveredMembers = new HashSet<HoleAxis>();
            foreach (HoleStack stack in stacks)
            {
                foreach (HoleAxis hole in stack.Holes)
                    recoveredMembers.Add(hole);
            }
            int unmatchedAfter = holes.Count(hole => !recoveredMembers.Contains(hole));
            Debug.WriteLine("[CHECK HOLE ASSY] RECOVERY summary: unmatchedBefore=" + unmatchedBefore
                + ", geometryCandidate=" + geometryCandidates
                + ", rejectNotMutual=" + rejectedNotMutual
                + ", mutualCandidate=" + candidates.Count
                + ", clusterMerge=" + clusterMerges
                + ", rejectPrimaryBridge=" + rejectedPrimaryBridge
                + ", rejectDuplicateComponent=" + rejectedDuplicateComponent
                + ", rejectGeometry=" + rejectedGeometry
                + ", unmatchedAfter=" + unmatchedAfter
                + ", stacks=" + stacks.Count);
            return stacks;
        }

        private static int FindNearestRecoveryIndex(
            int sourceIndex,
            string targetOccurrence,
            List<HoleAxis> holes,
            bool[] primaryMember,
            double[] localPitch,
            HoleSpatialIndex spatialIndex)
        {
            int bestIndex = -1;
            double bestPosition = double.MaxValue;
            double bestAngle = double.MaxValue;
            double bestAxialGap = double.MaxValue;
            foreach (int index in spatialIndex.QueryForHole(
                sourceIndex,
                RecoveryMaximumPositionM,
                AdjacentFaceGapM))
            {
                if (index == sourceIndex
                    || (primaryMember[sourceIndex] && primaryMember[index])
                    || !string.Equals(
                        holes[index].ComponentOccurrence,
                        targetOccurrence,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                double angle = AxisAngleDeg(holes[sourceIndex].Direction, holes[index].Direction);
                double axialGap = AxialGap(holes[sourceIndex], holes[index]);
                double position = TransverseCenterDistance(holes[sourceIndex], holes[index]);
                double recoveryLimit = GetRecoveryPositionLimit(
                    localPitch[sourceIndex],
                    localPitch[index]);
                if (!IsSearchAngleCompatible(holes[sourceIndex], holes[index], angle)
                    || !AreHoleLayersAdjacent(holes[sourceIndex], holes[index], out axialGap)
                    || position > recoveryLimit)
                    continue;

                if (position < bestPosition - 1e-10
                    || (Math.Abs(position - bestPosition) <= 1e-10 && angle < bestAngle - 1e-8)
                    || (Math.Abs(position - bestPosition) <= 1e-10
                        && Math.Abs(angle - bestAngle) <= 1e-8
                        && axialGap < bestAxialGap))
                {
                    bestIndex = index;
                    bestPosition = position;
                    bestAngle = angle;
                    bestAxialGap = axialGap;
                }
            }
            return bestIndex;
        }

        private static bool CanMergeRecoveryClusters(
            HoleCluster first,
            HoleCluster second,
            List<HoleAxis> holes,
            double[] localPitch)
        {
            foreach (int firstIndex in first.MemberIndexes)
            {
                foreach (int secondIndex in second.MemberIndexes)
                {
                    HoleAxis firstHole = holes[firstIndex];
                    HoleAxis secondHole = holes[secondIndex];
                    if (!IsSearchAngleCompatible(firstHole, secondHole))
                        return false;
                    double limit = GetRecoveryPositionLimit(
                        localPitch[firstIndex],
                        localPitch[secondIndex]);
                    if (TransverseCenterDistance(firstHole, secondHole) > limit)
                        return false;
                }
            }
            return true;
        }

        private static double EstimateLocalHolePitch(int sourceIndex, List<HoleAxis> holes)
        {
            double best = double.PositiveInfinity;
            HoleAxis source = holes[sourceIndex];
            for (int index = 0; index < holes.Count; index++)
            {
                if (index == sourceIndex
                    || !string.Equals(
                        source.ComponentOccurrence,
                        holes[index].ComponentOccurrence,
                        StringComparison.OrdinalIgnoreCase)
                    || !IsSearchAngleCompatible(source, holes[index]))
                    continue;

                double distance = TransverseCenterDistance(source, holes[index]);
                if (distance > SameHolePositionToleranceM && distance < best)
                    best = distance;
            }
            return best;
        }

        private static double GetRecoveryPositionLimit(double firstPitch, double secondPitch)
        {
            double pitch = double.PositiveInfinity;
            if (!double.IsInfinity(firstPitch) && firstPitch > 0)
                pitch = firstPitch;
            if (!double.IsInfinity(secondPitch) && secondPitch > 0)
                pitch = Math.Min(pitch, secondPitch);
            if (double.IsInfinity(pitch))
                return RecoveryMaximumPositionM;
            return Math.Min(
                RecoveryMaximumPositionM,
                Math.Max(SearchPositionToleranceM, pitch * RecoveryPitchFraction));
        }

        private static string FormatOptionalMm(double valueM)
        {
            return double.IsInfinity(valueM) || double.IsNaN(valueM)
                ? "N/A"
                : FormatMm(valueM) + "mm";
        }

        private static bool CanMergeHoleClusters(
            HoleCluster first,
            HoleCluster second,
            List<HoleAxis> holes)
        {
            foreach (int firstIndex in first.MemberIndexes)
            {
                foreach (int secondIndex in second.MemberIndexes)
                {
                    HoleAxis firstHole = holes[firstIndex];
                    HoleAxis secondHole = holes[secondIndex];
                    if (!IsSearchAngleCompatible(firstHole, secondHole))
                        return false;
                    if (TransverseCenterDistance(firstHole, secondHole) > SearchPositionToleranceM)
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Recovery xa chi lam viec tren cap occurrence da co it nhat hai anchor
        /// tu NORMAL stack. Khong duoc ghep lo bat ky chi vi nam trong ban kinh 30 mm.
        /// </summary>
        private List<HoleStack> BuildFarMisalignmentStacks(
            List<HoleAxis> holes,
            List<HoleStack> primaryStacks,
            List<HoleStack> existingStacks,
            HoleSpatialIndex spatialIndex)
        {
            Stopwatch watch = Stopwatch.StartNew();
            spatialIndex.ResetStats();
            List<HoleStack> stacks = existingStacks == null
                ? new List<HoleStack>()
                : new List<HoleStack>(existingStacks);
            Stopwatch anchorWatch = Stopwatch.StartNew();
            Dictionary<string, TrustedOccurrencePair> trusted =
                BuildTrustedOccurrencePairs(holes, primaryStacks, spatialIndex);
            anchorWatch.Stop();
            if (activeProfile != null)
                activeProfile.RelationshipAnchorMs += anchorWatch.ElapsedMilliseconds;
            Dictionary<HoleAxis, HoleStack> stackByHole = new Dictionary<HoleAxis, HoleStack>();
            foreach (HoleStack stack in stacks)
            {
                foreach (HoleAxis hole in stack.Holes)
                    stackByHole[hole] = stack;
            }
            int farMatches = 0;
            int ambiguous = 0;
            int insufficientAnchors = 0;
            int acceptedExistingStack = 0;
            int acceptedUnmatched = 0;
            int rejectAngle = 0;
            int rejectDistance = 0;
            int rejectAdjacency = 0;
            int rejectSize = 0;
            int rejectDuplicateOccurrence = 0;

            foreach (TrustedOccurrencePair trust in trusted.Values.OrderBy(item => item.OccurrenceA))
            {
                int normalAnchorCount = trust.NormalAnchors.Count;
                int relationshipAnchorCount = trust.RelationshipAnchors.Count;
                int anchorCount = trust.Anchors.Count();
                if (!trust.IsProductionTrusted)
                {
                    insufficientAnchors++;
                    if (VerboseHoleDebug)
                        Debug.WriteLine("[CHECK HOLE ASSY] FAR_PAIR"
                            + " occurrenceA=" + trust.OccurrenceA
                            + ", occurrenceB=" + trust.OccurrenceB
                            + ", normalAnchorCount=" + normalAnchorCount
                            + ", relationshipAnchorCount=" + relationshipAnchorCount
                            + ", totalAnchorCount=" + anchorCount
                            + ", status=INSUFFICIENT_ANCHORS, productionResult=NONE");
                    continue;
                }

                List<int> holesA = Enumerable.Range(0, holes.Count).Where(index => string.Equals(
                    holes[index].ComponentOccurrence, trust.OccurrenceA,
                    StringComparison.OrdinalIgnoreCase)).ToList();
                List<int> holesB = Enumerable.Range(0, holes.Count).Where(index => string.Equals(
                    holes[index].ComponentOccurrence, trust.OccurrenceB,
                    StringComparison.OrdinalIgnoreCase)).ToList();
                HashSet<HoleAxis> pairedA = new HashSet<HoleAxis>(trust.Anchors.Select(item => item.HoleA));
                HashSet<HoleAxis> pairedB = new HashSet<HoleAxis>(trust.Anchors.Select(item => item.HoleB));
                List<int> remainingA = holesA.Where(index => !pairedA.Contains(holes[index])).ToList();
                List<int> remainingB = holesB.Where(index => !pairedB.Contains(holes[index])).ToList();
                if (VerboseHoleDebug)
                    Debug.WriteLine("[CHECK HOLE ASSY] FAR_PAIR"
                        + " occurrenceA=" + trust.OccurrenceA
                        + ", occurrenceB=" + trust.OccurrenceB
                        + ", normalAnchorCount=" + normalAnchorCount
                        + ", relationshipAnchorCount=" + relationshipAnchorCount
                        + ", totalAnchorCount=" + anchorCount
                        + ", holesA=" + holesA.Count
                        + ", holesB=" + holesB.Count
                        + ", alreadyPairedA=" + pairedA.Count
                        + ", alreadyPairedB=" + pairedB.Count
                        + ", remainingA=" + remainingA.Count
                        + ", remainingB=" + remainingB.Count);
                if (remainingA.Count == 0 || remainingB.Count == 0)
                    continue;

                List<FarMatchCandidate> candidates = BuildFarPairCandidates(
                    holes, remainingA, remainingB, trust, stackByHole, spatialIndex,
                    ref rejectAngle, ref rejectDistance, ref rejectAdjacency, ref rejectSize,
                    ref rejectDuplicateOccurrence);
                if (candidates.Count == 0)
                    continue;

                bool uniqueLeftover = remainingA.Count == 1 && remainingB.Count == 1;
                List<FarMatchCandidate> assignments = SolveFarOneToOneAssignment(
                    remainingA, remainingB, candidates);
                foreach (FarMatchCandidate candidate in assignments)
                {
                    if (!uniqueLeftover && IsFarAssignmentAmbiguous(candidate, candidates))
                    {
                        ambiguous++;
                        Debug.WriteLine("[CHECK HOLE ASSY] FAR_RECOVERY AMBIGUOUS"
                            + " occurrenceA=" + trust.OccurrenceA
                            + ", occurrenceB=" + trust.OccurrenceB
                            + ", source=" + holes[candidate.SourceIndex].ComponentOccurrence
                            + ", productionResult=NONE");
                        continue;
                    }

                    HoleAxis source = holes[candidate.SourceIndex];
                    HoleAxis target = holes[candidate.TargetIndex];
                    HoleStack sourceStack;
                    HoleStack targetStack;
                    stackByHole.TryGetValue(source, out sourceStack);
                    stackByHole.TryGetValue(target, out targetStack);
                    if (!TryAttachFarMatch(stacks, stackByHole, source, target,
                        sourceStack, targetStack, out targetStack))
                    {
                        rejectDuplicateOccurrence++;
                        continue;
                    }

                    targetStack.MatchSource = HoleMatchSource.FarMisalignmentRecovery;
                    targetStack.FarActualHoles.Add(source);
                    targetStack.FarMatches.Add(candidate);
                    targetStack.FarMarkerRelations.Add(new FarMarkerRelation
                    {
                        SourceHole = source,
                        CounterpartHole = target,
                        TransverseOffsetM = candidate.PositionM
                    });
                    farMatches++;
                    if (candidate.TargetInExistingStack || sourceStack != null)
                        acceptedExistingStack++;
                    else
                        acceptedUnmatched++;
                    Debug.WriteLine("[CHECK HOLE ASSY] FAR_RECOVERY MATCH"
                        + " anchorCount=" + anchorCount
                        + ", assignment=" + (uniqueLeftover ? "UNIQUE_LEFTOVER" : "GLOBAL_ASSIGNMENT")
                        + ", source=" + source.ComponentOccurrence
                        + ", counterpart=" + target.ComponentOccurrence
                        + ", sourceCenter=" + FormatPointMm(source.Center)
                        + ", counterpartCenter=" + FormatPointMm(target.Center)
                        + ", transverseOffset=" + FormatMm(candidate.PositionM) + "mm"
                        + ", matchTarget=" + (candidate.TargetInExistingStack ? "EXISTING_STACK" : "NEW_PAIR")
                        + ", reason=FAR_MISALIGNMENT, finalResult=NG");
                }
            }

            watch.Stop();
            Debug.WriteLine("[CHECK HOLE ASSY] FAR_RECOVERY SUMMARY"
                + " trustedOccurrencePairs=" + trusted.Values.Count(item => item.IsProductionTrusted)
                + ", farMatches=" + farMatches
                + ", ambiguous=" + ambiguous
                + ", broadPhaseCandidates=" + spatialIndex.LastQueryCandidateCount
                + ", rejectAngle=" + rejectAngle
                + ", rejectDistance=" + rejectDistance
                + ", rejectAdjacency=" + rejectAdjacency
                + ", rejectSize=" + rejectSize
                + ", rejectDuplicateOccurrence=" + rejectDuplicateOccurrence
                + ", acceptedExistingStack=" + acceptedExistingStack
                + ", acceptedUnmatched=" + acceptedUnmatched
                + ", insufficientAnchors=" + insufficientAnchors
                + ", zeroAnchorProductionMatches=0"
                + ", elapsedMs=" + watch.ElapsedMilliseconds);
            return stacks;
        }

        /// <summary>
        /// Tao candidate chi trong mot cap occurrence da duoc anchor boi NORMAL.
        /// Day la broad phase 30 mm, con quyet dinh van dua tren layer ke nhau,
        /// huong lo, va mot-doi-mot; khong co bat ky quy tac ten part nao.
        /// </summary>
        private static List<FarMatchCandidate> BuildFarPairCandidates(
            List<HoleAxis> holes,
            List<int> remainingA,
            List<int> remainingB,
            TrustedOccurrencePair trust,
            Dictionary<HoleAxis, HoleStack> stackByHole,
            HoleSpatialIndex spatialIndex,
            ref int rejectAngle,
            ref int rejectDistance,
            ref int rejectAdjacency,
            ref int rejectSize,
            ref int rejectDuplicateOccurrence)
        {
            List<FarMatchCandidate> result = new List<FarMatchCandidate>();
            HashSet<int> allowedB = new HashSet<int>(remainingB);
            foreach (int sourceIndex in remainingA)
            {
                HoleAxis source = holes[sourceIndex];
                if (source == null || !IsPoint(source.Center) || !IsDirection(source.Direction)
                    || source.RadiusM <= 0 || double.IsNaN(source.RadiusM)
                    || double.IsInfinity(source.RadiusM))
                {
                    rejectSize++;
                    continue;
                }

                IEnumerable<int> broadPhase = spatialIndex == null
                    ? remainingB
                    : spatialIndex.QueryForHole(sourceIndex, FarMisalignmentSearchM, AdjacentFaceGapM);
                foreach (int targetIndex in broadPhase)
                {
                    if (!allowedB.Contains(targetIndex))
                        continue;
                    HoleAxis target = holes[targetIndex];
                    if (target == null || !IsPoint(target.Center) || !IsDirection(target.Direction)
                        || target.RadiusM <= 0 || double.IsNaN(target.RadiusM)
                        || double.IsInfinity(target.RadiusM))
                    {
                        rejectSize++;
                        continue;
                    }

                    double angleDeg = AxisAngleDeg(source.Direction, target.Direction);
                    if (double.IsNaN(angleDeg) || double.IsInfinity(angleDeg)
                        || angleDeg > FarMisalignmentSearchAngleDeg)
                    {
                        rejectAngle++;
                        continue;
                    }

                    double positionM = TransverseCenterDistance(source, target);
                    if (double.IsNaN(positionM) || double.IsInfinity(positionM)
                        || positionM <= SearchPositionToleranceM
                        || positionM > FarMisalignmentSearchM)
                    {
                        rejectDistance++;
                        continue;
                    }

                    double faceGapM;
                    if (!AreHoleLayersAdjacent(source, target, out faceGapM))
                    {
                        rejectAdjacency++;
                        continue;
                    }

                    HoleStack sourceStack;
                    HoleStack targetStack;
                    stackByHole.TryGetValue(source, out sourceStack);
                    stackByHole.TryGetValue(target, out targetStack);
                    if ((sourceStack != null && StackContainsOccurrence(sourceStack, target.ComponentOccurrence))
                        || (targetStack != null && StackContainsOccurrence(targetStack, source.ComponentOccurrence)))
                    {
                        rejectDuplicateOccurrence++;
                        continue;
                    }

                    double patternResidualM = ReadAnchorPatternResidual(source, target, trust);
                    double radiusDifferenceM = Math.Abs(source.RadiusM - target.RadiusM);
                    // Pattern residual chi la chi phi phu: geometry va adjacency van la dieu kien chinh.
                    double score = positionM + faceGapM * 0.25 + angleDeg * 0.00010
                        + radiusDifferenceM * 0.10
                        + (double.IsInfinity(patternResidualM) ? 0.010 : patternResidualM * 0.25);
                    result.Add(new FarMatchCandidate
                    {
                        SourceIndex = sourceIndex,
                        TargetIndex = targetIndex,
                        Trust = trust,
                        PositionM = positionM,
                        AxialSeparationM = AxialGap(source, target),
                        FaceGapM = faceGapM,
                        AngleDeg = angleDeg,
                        PatternResidualM = patternResidualM,
                        TargetInExistingStack = targetStack != null,
                        Score = score
                    });
                }
            }
            return result;
        }

        /// <summary>Giai assignment tong chi phi nho nhat, cho phep mot lo con lai khong duoc ghep.</summary>
        private static List<FarMatchCandidate> SolveFarOneToOneAssignment(
            List<int> remainingA,
            List<int> remainingB,
            List<FarMatchCandidate> candidates)
        {
            List<FarMatchCandidate> result = new List<FarMatchCandidate>();
            int size = Math.Max(remainingA.Count, remainingB.Count);
            if (size == 0)
                return result;

            Dictionary<string, FarMatchCandidate> byPair = candidates.ToDictionary(
                item => item.SourceIndex + "\u001f" + item.TargetIndex,
                item => item);
            double unmatchedCost = FarMisalignmentSearchM * 4.0;
            double[,] cost = new double[size, size];
            for (int row = 0; row < size; row++)
                for (int column = 0; column < size; column++)
                    cost[row, column] = unmatchedCost;
            for (int row = 0; row < remainingA.Count; row++)
            {
                for (int column = 0; column < remainingB.Count; column++)
                {
                    FarMatchCandidate candidate;
                    if (byPair.TryGetValue(remainingA[row] + "\u001f" + remainingB[column], out candidate))
                        cost[row, column] = candidate.Score;
                }
            }

            int[] assignment = SolveHungarian(cost);
            for (int row = 0; row < remainingA.Count; row++)
            {
                int column = assignment[row];
                if (column < 0 || column >= remainingB.Count)
                    continue;
                FarMatchCandidate candidate;
                if (byPair.TryGetValue(remainingA[row] + "\u001f" + remainingB[column], out candidate)
                    && candidate.Score < unmatchedCost)
                    result.Add(candidate);
            }
            return result;
        }

        // Hungarian algorithm, row -> column. Chi dung managed data; khong goi API SolidWorks.
        private static int[] SolveHungarian(double[,] cost)
        {
            int count = cost.GetLength(0);
            double[] u = new double[count + 1];
            double[] v = new double[count + 1];
            int[] p = new int[count + 1];
            int[] way = new int[count + 1];
            for (int row = 1; row <= count; row++)
            {
                p[0] = row;
                int column0 = 0;
                double[] min = Enumerable.Repeat(double.PositiveInfinity, count + 1).ToArray();
                bool[] used = new bool[count + 1];
                do
                {
                    used[column0] = true;
                    int row0 = p[column0];
                    double delta = double.PositiveInfinity;
                    int column1 = 0;
                    for (int column = 1; column <= count; column++)
                    {
                        if (used[column])
                            continue;
                        double current = cost[row0 - 1, column - 1] - u[row0] - v[column];
                        if (current < min[column])
                        {
                            min[column] = current;
                            way[column] = column0;
                        }
                        if (min[column] < delta)
                        {
                            delta = min[column];
                            column1 = column;
                        }
                    }
                    for (int column = 0; column <= count; column++)
                    {
                        if (used[column])
                        {
                            u[p[column]] += delta;
                            v[column] -= delta;
                        }
                        else
                            min[column] -= delta;
                    }
                    column0 = column1;
                }
                while (p[column0] != 0);

                do
                {
                    int column1 = way[column0];
                    p[column0] = p[column1];
                    column0 = column1;
                }
                while (column0 != 0);
            }

            int[] result = Enumerable.Repeat(-1, count).ToArray();
            for (int column = 1; column <= count; column++)
            {
                if (p[column] > 0)
                    result[p[column] - 1] = column - 1;
            }
            return result;
        }

        private static bool IsFarAssignmentAmbiguous(
            FarMatchCandidate selected,
            List<FarMatchCandidate> candidates)
        {
            double alternative = candidates.Where(item => item != selected
                    && (item.SourceIndex == selected.SourceIndex || item.TargetIndex == selected.TargetIndex))
                .Select(item => item.Score)
                .DefaultIfEmpty(double.PositiveInfinity)
                .Min();
            selected.SecondBestScore = alternative;
            return IsFarCandidateAmbiguous(selected.Score, alternative);
        }

        private static bool TryAttachFarMatch(
            List<HoleStack> stacks,
            Dictionary<HoleAxis, HoleStack> stackByHole,
            HoleAxis source,
            HoleAxis target,
            HoleStack sourceStack,
            HoleStack targetStack,
            out HoleStack attachedStack)
        {
            attachedStack = null;
            if (source == null || target == null || source == target
                || string.Equals(source.ComponentOccurrence, target.ComponentOccurrence,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            if (sourceStack != null && targetStack != null && !ReferenceEquals(sourceStack, targetStack))
            {
                if (sourceStack.Holes.Any(first => targetStack.Holes.Any(second =>
                    string.Equals(first.ComponentOccurrence, second.ComponentOccurrence,
                        StringComparison.OrdinalIgnoreCase))))
                    return false;
                foreach (HoleAxis hole in sourceStack.Holes)
                    if (!targetStack.Holes.Contains(hole))
                        targetStack.Holes.Add(hole);
                foreach (HoleAxis hole in sourceStack.FarActualHoles)
                    targetStack.FarActualHoles.Add(hole);
                targetStack.FarMatches.AddRange(sourceStack.FarMatches);
                targetStack.FarMarkerRelations.AddRange(sourceStack.FarMarkerRelations);
                stacks.Remove(sourceStack);
                sourceStack = targetStack;
            }

            attachedStack = targetStack ?? sourceStack;
            if (attachedStack == null)
            {
                attachedStack = new HoleStack();
                stacks.Add(attachedStack);
            }
            if (StackContainsOccurrence(attachedStack, source.ComponentOccurrence)
                && !attachedStack.Holes.Contains(source))
                return false;
            if (StackContainsOccurrence(attachedStack, target.ComponentOccurrence)
                && !attachedStack.Holes.Contains(target))
                return false;
            if (!attachedStack.Holes.Contains(source))
                attachedStack.Holes.Add(source);
            if (!attachedStack.Holes.Contains(target))
                attachedStack.Holes.Add(target);
            foreach (HoleAxis hole in attachedStack.Holes)
                stackByHole[hole] = attachedStack;
            return true;
        }

        private static bool StackContainsOccurrence(HoleStack stack, string occurrence)
        {
            return stack != null && stack.Holes.Any(item => item != null
                && string.Equals(item.ComponentOccurrence, occurrence, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, TrustedOccurrencePair> BuildTrustedOccurrencePairs(
            List<HoleAxis> holes,
            List<HoleStack> primaryStacks,
            HoleSpatialIndex spatialIndex)
        {
            Dictionary<string, TrustedOccurrencePair> result =
                new Dictionary<string, TrustedOccurrencePair>(StringComparer.OrdinalIgnoreCase);
            foreach (HoleStack stack in primaryStacks ?? new List<HoleStack>())
            {
                for (int firstIndex = 0; firstIndex < stack.Holes.Count; firstIndex++)
                {
                    for (int secondIndex = firstIndex + 1; secondIndex < stack.Holes.Count; secondIndex++)
                    {
                        HoleAxis first = stack.Holes[firstIndex];
                        HoleAxis second = stack.Holes[secondIndex];
                        string occurrenceA;
                        string occurrenceB;
                        bool sameOrder;
                        string key = BuildOccurrencePairKey(
                            first.ComponentOccurrence,
                            second.ComponentOccurrence,
                            out occurrenceA,
                            out occurrenceB,
                            out sameOrder);
                        TrustedOccurrencePair trust;
                        if (!result.TryGetValue(key, out trust))
                        {
                            trust = new TrustedOccurrencePair
                            {
                                OccurrenceA = occurrenceA,
                                OccurrenceB = occurrenceB
                            };
                            result[key] = trust;
                        }
                        trust.AddNormalAnchor(sameOrder ? first : second, sameOrder ? second : first);
                    }
                }
            }

            AddRelationshipAnchors(holes, spatialIndex, result);
            foreach (TrustedOccurrencePair trust in result.Values)
            {
                trust.IsTrusted = trust.IsProductionTrusted;
            }
            return result;
        }

        /// <summary>
        /// Anchor hinh hoc chi de phat hien quan he giua hai occurrence. Ket qua
        /// NORMAL, marker va dung sai production khong su dung ham nay.
        /// </summary>
        private static void AddRelationshipAnchors(
            List<HoleAxis> holes,
            HoleSpatialIndex spatialIndex,
            Dictionary<string, TrustedOccurrencePair> trusted)
        {
            if (holes == null || spatialIndex == null || trusted == null)
                return;

            int candidatePairs = 0;
            int acceptedAnchors = 0;
            int occurrencePairsUpgraded = 0;
            int rejectAngle = 0;
            int rejectPosition = 0;
            int rejectAdjacency = 0;
            int rejectNotMutual = 0;
            List<RelationshipAnchorCandidate> candidates = new List<RelationshipAnchorCandidate>();

            for (int firstIndex = 0; firstIndex < holes.Count; firstIndex++)
            {
                HoleAxis first = holes[firstIndex];
                if (first == null || !IsPoint(first.Center) || !IsDirection(first.Direction))
                    continue;
                foreach (int secondIndex in spatialIndex.QueryForHole(
                    firstIndex, SearchPositionToleranceM, AdjacentFaceGapM))
                {
                    if (secondIndex <= firstIndex)
                        continue;
                    HoleAxis second = holes[secondIndex];
                    if (second == null || !IsPoint(second.Center) || !IsDirection(second.Direction)
                        || string.Equals(first.ComponentOccurrence, second.ComponentOccurrence,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    string occurrenceA;
                    string occurrenceB;
                    bool sameOrder;
                    string key = BuildOccurrencePairKey(first.ComponentOccurrence, second.ComponentOccurrence,
                        out occurrenceA, out occurrenceB, out sameOrder);
                    TrustedOccurrencePair currentTrust;
                    if (trusted.TryGetValue(key, out currentTrust)
                        && currentTrust.NormalAnchors.Count >= TrustedOccurrenceMinimumAnchorCount)
                        continue;

                    double angleDeg = AxisAngleDeg(first.Direction, second.Direction);
                    if (double.IsNaN(angleDeg) || double.IsInfinity(angleDeg)
                        || angleDeg > RelationshipAnchorSearchAngleDeg)
                    {
                        rejectAngle++;
                        continue;
                    }
                    double positionM = TransverseCenterDistance(first, second);
                    if (double.IsNaN(positionM) || double.IsInfinity(positionM)
                        || positionM > SearchPositionToleranceM)
                    {
                        rejectPosition++;
                        continue;
                    }
                    double faceGapM;
                    if (!AreHoleLayersAdjacent(first, second, out faceGapM))
                    {
                        rejectAdjacency++;
                        continue;
                    }

                    candidatePairs++;
                    candidates.Add(new RelationshipAnchorCandidate
                    {
                        FirstIndex = firstIndex,
                        SecondIndex = secondIndex,
                        OccurrencePairKey = key,
                        Score = positionM + faceGapM * 0.25 + angleDeg * 0.00010
                    });
                }
            }

            // Mot lo chi duoc dung de chung minh mot quan he khi doi tac cua no
            // cung chon no la candidate tot nhat. Khong dung "nearest" mot chieu.
            Dictionary<int, RelationshipAnchorCandidate> bestByHole = new Dictionary<int, RelationshipAnchorCandidate>();
            foreach (RelationshipAnchorCandidate candidate in candidates)
            {
                RelationshipAnchorCandidate current;
                if (!bestByHole.TryGetValue(candidate.FirstIndex, out current)
                    || candidate.Score < current.Score)
                    bestByHole[candidate.FirstIndex] = candidate;
                if (!bestByHole.TryGetValue(candidate.SecondIndex, out current)
                    || candidate.Score < current.Score)
                    bestByHole[candidate.SecondIndex] = candidate;
            }

            foreach (RelationshipAnchorCandidate candidate in candidates)
            {
                RelationshipAnchorCandidate firstBest;
                RelationshipAnchorCandidate secondBest;
                if (!bestByHole.TryGetValue(candidate.FirstIndex, out firstBest)
                    || !bestByHole.TryGetValue(candidate.SecondIndex, out secondBest)
                    || !ReferenceEquals(firstBest, candidate)
                    || !ReferenceEquals(secondBest, candidate))
                {
                    rejectNotMutual++;
                    continue;
                }

                HoleAxis first = holes[candidate.FirstIndex];
                HoleAxis second = holes[candidate.SecondIndex];
                string occurrenceA;
                string occurrenceB;
                bool sameOrder;
                string key = BuildOccurrencePairKey(first.ComponentOccurrence, second.ComponentOccurrence,
                    out occurrenceA, out occurrenceB, out sameOrder);
                TrustedOccurrencePair trust;
                if (!trusted.TryGetValue(key, out trust))
                {
                    trust = new TrustedOccurrencePair
                    {
                        OccurrenceA = occurrenceA,
                        OccurrenceB = occurrenceB
                    };
                    trusted[key] = trust;
                }
                bool wasTrusted = trust.IsProductionTrusted;
                trust.AddRelationshipAnchor(sameOrder ? first : second, sameOrder ? second : first);
                acceptedAnchors++;
                if (!wasTrusted && trust.IsProductionTrusted)
                    occurrencePairsUpgraded++;
            }

            Debug.WriteLine("[CHECK HOLE ASSY] RELATIONSHIP_ANCHOR SUMMARY"
                + " candidatePairs=" + candidatePairs
                + ", acceptedAnchors=" + acceptedAnchors
                + ", occurrencePairsUpgraded=" + occurrencePairsUpgraded
                + ", rejectAngle=" + rejectAngle
                + ", rejectPosition=" + rejectPosition
                + ", rejectAdjacency=" + rejectAdjacency
                + ", rejectNotMutual=" + rejectNotMutual);
        }

        private static bool IsStrongSingleAnchorTrust(
            List<HoleAxis> holes,
            TrustedOccurrencePair trust)
        {
            if (trust == null || trust.NormalAnchors.Count != 1)
                return false;
            TrustedHoleAnchor anchor = trust.NormalAnchors[0];
            if (!IsSearchAngleCompatible(anchor.HoleA, anchor.HoleB)
                || !AreFarHoleSizesCompatible(anchor.HoleA, anchor.HoleB))
                return false;
            double gap;
            if (!AreHoleLayersAdjacent(anchor.HoleA, anchor.HoleB, out gap))
                return false;

            int signatureA = holes.Count(item => string.Equals(
                item.ComponentOccurrence, trust.OccurrenceA, StringComparison.OrdinalIgnoreCase)
                && AreFarHoleSizesCompatible(item, anchor.HoleA));
            int signatureB = holes.Count(item => string.Equals(
                item.ComponentOccurrence, trust.OccurrenceB, StringComparison.OrdinalIgnoreCase)
                && AreFarHoleSizesCompatible(item, anchor.HoleB));
            return signatureA == 1 && signatureB == 1;
        }

        private static string BuildOccurrencePairKey(
            string first,
            string second,
            out string occurrenceA,
            out string occurrenceB,
            out bool sameOrder)
        {
            sameOrder = string.Compare(first, second, StringComparison.OrdinalIgnoreCase) <= 0;
            occurrenceA = sameOrder ? first : second;
            occurrenceB = sameOrder ? second : first;
            return (occurrenceA ?? "") + "\u001f" + (occurrenceB ?? "");
        }

        private static TrustedOccurrencePair FindTrustedOccurrencePair(
            Dictionary<string, TrustedOccurrencePair> trusted,
            string firstOccurrence,
            string secondOccurrence)
        {
            if (trusted == null || string.IsNullOrWhiteSpace(firstOccurrence)
                || string.IsNullOrWhiteSpace(secondOccurrence))
                return null;
            string occurrenceA;
            string occurrenceB;
            bool sameOrder;
            string key = BuildOccurrencePairKey(
                firstOccurrence,
                secondOccurrence,
                out occurrenceA,
                out occurrenceB,
                out sameOrder);
            TrustedOccurrencePair result;
            return trusted.TryGetValue(key, out result) ? result : null;
        }

        private static bool AreFarHoleSizesCompatible(HoleAxis first, HoleAxis second)
        {
            if (first == null || second == null || first.RadiusM <= 0 || second.RadiusM <= 0)
                return false;
            double maximum = Math.Max(first.RadiusM, second.RadiusM);
            return Math.Abs(first.RadiusM - second.RadiusM)
                <= Math.Max(0.00050, maximum * RecoveredDuplicateDiameterFraction);
        }

        private static double ReadAnchorPatternResidual(
            HoleAxis source,
            HoleAxis target,
            TrustedOccurrencePair trust)
        {
            double best = double.PositiveInfinity;
            bool sourceIsA = string.Equals(source.ComponentOccurrence, trust.OccurrenceA,
                StringComparison.OrdinalIgnoreCase);
            foreach (TrustedHoleAnchor anchor in trust.Anchors)
            {
                HoleAxis sourceAnchor = sourceIsA ? anchor.HoleA : anchor.HoleB;
                HoleAxis targetAnchor = sourceIsA ? anchor.HoleB : anchor.HoleA;
                double[] sourceVector = RemoveAxialComponent(
                    Subtract(source.Center, sourceAnchor.Center),
                    source.Direction);
                double[] targetVector = RemoveAxialComponent(
                    Subtract(target.Center, targetAnchor.Center),
                    target.Direction);
                best = Math.Min(best, Length(Subtract(sourceVector, targetVector)));
            }
            return best;
        }

        private static double[] RemoveAxialComponent(double[] vector, double[] direction)
        {
            double[] normalized = Normalize(direction);
            return Subtract(vector, Scale(normalized, Dot(vector, normalized)));
        }

        private static bool IsFarCandidateAmbiguous(double best, double second)
        {
            if (double.IsInfinity(second))
                return false;
            return second - best < FarAmbiguousMinimumScoreGapM
                || second <= best * FarAmbiguousScoreRatio;
        }

        private HoleStackResult EvaluateStack(HoleStack stack)
        {
            bool containsRecoveredDeformed = stack != null
                && stack.Holes.Any(item => item != null
                    && item.Source == HoleSource.RecoveredDeformed);
            IEnumerable<HoleAxis> referenceCandidates = containsRecoveredDeformed
                && stack.Holes.Any(item => item != null
                    && item.Source != HoleSource.RecoveredDeformed)
                    ? stack.Holes.Where(item => item != null
                        && item.Source != HoleSource.RecoveredDeformed)
                    : stack.Holes;
            // Lo duoc them boi FAR_RECOVERY la tam thuc te bi lech. Neu stack
            // van con tam tham chieu production thi khong cho no tro thanh medoid.
            if (stack.FarActualHoles.Count > 0
                && referenceCandidates.Any(item => !stack.FarActualHoles.Contains(item)))
                referenceCandidates = referenceCandidates.Where(
                    item => !stack.FarActualHoles.Contains(item));
            HoleAxis reference = referenceCandidates.First();
            double bestScore = double.MaxValue;
            foreach (HoleAxis candidate in referenceCandidates)
            {
                double score = 0;
                foreach (HoleAxis other in stack.Holes)
                    score += TransverseCenterDistance(candidate, other);
                if (score < bestScore)
                {
                    bestScore = score;
                    reference = candidate;
                }
            }

            HoleStackResult result = new HoleStackResult
            {
                Stack = stack,
                Reference = reference
            };
            foreach (HoleAxis item in stack.Holes)
            {
                double offset = TransverseCenterDistance(reference, item);
                double angle = AxisAngleDeg(reference.Direction, item.Direction);
                result.MaxOffsetM = Math.Max(result.MaxOffsetM, offset);
                result.MaxAngleDeg = Math.Max(result.MaxAngleDeg, angle);
                bool positionNg = offset > PositionToleranceM;
                bool angleNg = !containsRecoveredDeformed && angle > AngleToleranceDeg;
                if (positionNg || angleNg)
                    result.Outliers.Add(item);

                if (VerboseHoleDebug)
                    Debug.WriteLine("[CHECK HOLE ASSY] STACK member: reference="
                        + reference.ComponentOccurrence + ", component=" + item.ComponentOccurrence
                        + ", offset=" + FormatMm(offset) + "mm"
                        + ", angle=" + angle.ToString("0.###", CultureInfo.InvariantCulture) + "deg"
                        + ", status=" + (positionNg || angleNg ? "NG" : "OK")
                        + ", center=" + FormatPointMm(item.Center));
            }
            result.IsNg = result.Outliers.Count > 0;
            if (result.IsNg && stack.MatchSource == HoleMatchSource.FarMisalignmentRecovery)
                result.Reason = "FAR_MISALIGNMENT";

            Debug.WriteLine("[CHECK HOLE ASSY] stack components="
                + string.Join(", ", stack.Holes.Select(item => item.ComponentOccurrence))
                + ", maxOffset=" + FormatMm(result.MaxOffsetM) + "mm"
                + ", maxAngle=" + result.MaxAngleDeg.ToString("0.###", CultureInfo.InvariantCulture)
                + "deg, status=" + (result.IsNg ? "NG" : "OK"));
            if (containsRecoveredDeformed)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] DEFORMED_STACK"
                    + " components=" + string.Join(" <-> ", stack.Holes.Select(item => item.ComponentOccurrence))
                    + ", sources=" + string.Join(" <-> ", stack.Holes.Select(GetHoleSourceName))
                    + ", transverseOffset=" + FormatMm(result.MaxOffsetM) + "mm"
                    + ", angleDifference=" + result.MaxAngleDeg.ToString("0.###", CultureInfo.InvariantCulture) + "deg"
                    + ", positionNG=" + (result.MaxOffsetM > PositionToleranceM).ToString().ToLowerInvariant()
                    + ", anglePolicy=DIAGNOSTIC_ONLY_FOR_DEFORMED"
                    + ", finalResult=" + (result.IsNg ? "NG" : "OK"));
            }
            return result;
        }

        private static List<string> BuildIssueDescriptions(
            List<HoleStackResult> ngResults,
            List<PatternIssue> patternIssues,
            int maximumLines)
        {
            List<string> descriptions = new List<string>();
            if (maximumLines <= 0)
                return descriptions;

            foreach (HoleStackResult result in ngResults ?? new List<HoleStackResult>())
            {
                if (result == null || result.Reference == null)
                    continue;
                foreach (HoleAxis outlier in result.Outliers)
                {
                    double offset = TransverseCenterDistance(result.Reference, outlier);
                    double angle = AxisAngleDeg(result.Reference.Direction, outlier.Direction);
                    descriptions.Add("- DONG TAM: "
                        + result.Reference.ComponentOccurrence + " <-> " + outlier.ComponentOccurrence
                        + ", lech=" + FormatMm(offset) + " mm"
                        + ", goc=" + angle.ToString("0.###", CultureInfo.InvariantCulture) + " do");
                    if (descriptions.Count >= maximumLines)
                        return descriptions;
                }
            }

            foreach (PatternIssue issue in patternIssues ?? new List<PatternIssue>())
            {
                descriptions.Add("- " + issue.Kind + ": "
                    + issue.FirstComponent + " <-> " + issue.SecondComponent
                    + ", vi tri=" + FormatMm(issue.PositionErrorM) + " mm"
                    + ", pitch=" + FormatMm(issue.PitchErrorM) + " mm"
                    + ", hang=" + FormatMm(issue.RowErrorM) + " mm");
                if (descriptions.Count >= maximumLines)
                    return descriptions;
            }
            return descriptions;
        }

        /// <summary>
        /// Kiem tra chuoi lo sau khi da tim duoc it nhat mot lo neo dong tam.
        /// Khong phu thuoc feature Linear Pattern: hang lo duoc nhan dang tu tam lo thuc te.
        /// </summary>
        private List<PatternIssue> BuildPatternIssues(
            List<HoleAxis> holes,
            List<HoleStackResult> stackResults)
        {
            List<PatternIssue> issues = new List<PatternIssue>();
            HashSet<string> issueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Stopwatch patternPrepareWatch = Stopwatch.StartNew();
            List<HoleStackResult> validStacks = stackResults
                .Where(item => !item.IsNg && item.Stack != null && item.Stack.Holes.Count >= 2)
                .ToList();
            Dictionary<string, List<PatternAnchorPair>> pairs = BuildPatternAnchorPairs(validStacks);
            patternPrepareWatch.Stop();
            if (activeProfile != null)
                activeProfile.PatternPrepareMs += patternPrepareWatch.ElapsedMilliseconds;
            int comparedRows = 0;
            foreach (KeyValuePair<string, List<PatternAnchorPair>> pairEntry in pairs)
            {
                List<PatternAnchorPair> anchors = pairEntry.Value;
                if (anchors.Count == 0)
                    continue;

                string firstComponent = anchors[0].First.ComponentOccurrence;
                string secondComponent = anchors[0].Second.ComponentOccurrence;
                Stopwatch patternAssignmentWatch = Stopwatch.StartNew();
                List<HoleAxis> firstHoles = holes.Where(item => string.Equals(
                    item.ComponentOccurrence, firstComponent, StringComparison.OrdinalIgnoreCase)).ToList();
                List<HoleAxis> secondHoles = holes.Where(item => string.Equals(
                    item.ComponentOccurrence, secondComponent, StringComparison.OrdinalIgnoreCase)).ToList();
                List<PatternRowCandidate> candidates = BuildPatternRowCandidates(
                    anchors, firstHoles, secondHoles);
                List<PatternRowCandidate> acceptedRows = new List<PatternRowCandidate>();

                foreach (PatternRowCandidate candidate in candidates
                    .OrderByDescending(item => item.Score))
                {
                    if (acceptedRows.Any(existing => SamePhysicalPatternRow(existing, candidate)))
                        continue;
                    acceptedRows.Add(candidate);
                    comparedRows++;
                    patternAssignmentWatch.Stop();
                    if (activeProfile != null)
                        activeProfile.PatternAssignmentMs += patternAssignmentWatch.ElapsedMilliseconds;
                    Stopwatch patternEvaluationWatch = Stopwatch.StartNew();
                    ComparePatternRows(
                        candidate.Anchor.First,
                        candidate.Anchor.Second,
                        candidate.Direction,
                        candidate.FirstRow,
                        candidate.SecondRow,
                        issues,
                        issueKeys);
                    patternEvaluationWatch.Stop();
                    if (activeProfile != null)
                        activeProfile.PatternEvaluationMs += patternEvaluationWatch.ElapsedMilliseconds;
                    patternAssignmentWatch = Stopwatch.StartNew();
                }
                patternAssignmentWatch.Stop();
                if (activeProfile != null)
                    activeProfile.PatternAssignmentMs += patternAssignmentWatch.ElapsedMilliseconds;

                if (VerboseHoleDebug)
                    Debug.WriteLine("[CHECK HOLE ASSY] PATTERN pair=" + pairEntry.Key
                        + ", anchors=" + anchors.Count
                        + ", candidates=" + candidates.Count
                        + ", acceptedRows=" + acceptedRows.Count);
            }

            // Nhan them truong hop hai hang lo tren hai lop ke nhau co cung so lo,
            // cung pitch va cung huong, nhung ca hang bi dich nen khong con lo neo
            // dong tam. Nhanh nay chay sau logic co neo va khong thay doi logic cu.
            Stopwatch wholeRowEvaluationWatch = Stopwatch.StartNew();
            int wholeRowIssues = BuildShiftedWholeRowIssues(holes, issues, issueKeys);
            wholeRowEvaluationWatch.Stop();
            if (activeProfile != null)
                activeProfile.PatternEvaluationMs += wholeRowEvaluationWatch.ElapsedMilliseconds;

            Debug.WriteLine("[CHECK HOLE ASSY] PATTERN summary: componentPairs=" + pairs.Count
                + ", validStacks=" + validStacks.Count
                + ", comparedRows=" + comparedRows
                + ", wholeRowIssues=" + wholeRowIssues
                + ", issues=" + issues.Count);
            if (VerboseHoleDebug)
            {
                foreach (PatternIssue issue in issues)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] PATTERN " + issue.Kind
                        + ": " + issue.FirstComponent + " <-> " + issue.SecondComponent
                        + ", position=" + FormatMm(issue.PositionErrorM) + "mm"
                        + ", pitch=" + FormatMm(issue.PitchErrorM) + "mm"
                        + ", row=" + FormatMm(issue.RowErrorM) + "mm"
                        + ", expected=" + FormatPointMm(issue.ExpectedCenter)
                        + ", actual=" + FormatPointMm(issue.ActualCenter));
                }
            }
            return issues;
        }

        private static int BuildShiftedWholeRowIssues(
            List<HoleAxis> holes,
            List<PatternIssue> issues,
            HashSet<string> issueKeys)
        {
            List<IndependentPatternRow> rows = BuildIndependentPatternRows(holes);
            HashSet<string> acceptedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            int compared = 0;

            for (int firstIndex = 0; firstIndex < rows.Count; firstIndex++)
            {
                IndependentPatternRow first = rows[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < rows.Count; secondIndex++)
                {
                    IndependentPatternRow second = rows[secondIndex];
                    if (string.Equals(first.ComponentOccurrence, second.ComponentOccurrence,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (first.Holes.Count != second.Holes.Count
                        || first.Holes.Count < WholeRowMinimumHoleCount)
                        continue;
                    if (AxisAngleDeg(first.AxisDirection, second.AxisDirection) > SearchAngleToleranceDeg
                        || OrientedAxisAngleDeg(first.Direction, second.Direction) > PatternDirectionToleranceDeg)
                        continue;

                    compared++;
                    double[] direction = AlignAndAverageRowDirection(first.Direction, second.Direction);
                    if (!IsDirection(direction))
                        continue;

                    List<HoleAxis> firstOrdered = first.Holes
                        .OrderBy(item => Dot(item.Center, direction)).ToList();
                    List<HoleAxis> secondOrdered = second.Holes
                        .OrderBy(item => Dot(item.Center, direction)).ToList();

                    string rowPairKey = BuildIndependentRowPairKey(firstOrdered, secondOrdered);
                    if (!acceptedPairs.Add(rowPairKey))
                        continue;

                    double overlapFraction = GetProjectedOverlapFraction(firstOrdered, secondOrdered, direction);
                    if (overlapFraction < WholeRowMinimumOverlapFraction)
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] WHOLE ROW reject overlap: "
                            + first.ComponentOccurrence + " <-> " + second.ComponentOccurrence
                            + ", overlap=" + overlapFraction.ToString("0.###", CultureInfo.InvariantCulture));
                        continue;
                    }

                    if (HasNearConcentricAnchor(firstOrdered, secondOrdered))
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] WHOLE ROW skip; anchor exists: "
                            + first.ComponentOccurrence + " <-> " + second.ComponentOccurrence);
                        continue;
                    }

                    bool compatible = true;
                    double maximumPitchDifference = 0.0;
                    double maximumFaceGap = 0.0;
                    for (int index = 0; index < firstOrdered.Count; index++)
                    {
                        double faceGapM;
                        if (!AreHoleLayersAdjacent(firstOrdered[index], secondOrdered[index], out faceGapM)
                            || Math.Abs(firstOrdered[index].RadiusM - secondOrdered[index].RadiusM) > SlotPairToleranceM)
                        {
                            compatible = false;
                            break;
                        }
                        maximumFaceGap = Math.Max(maximumFaceGap, faceGapM);
                        if (index == 0)
                            continue;
                        double firstPitch = Math.Abs(Dot(
                            Subtract(firstOrdered[index].Center, firstOrdered[index - 1].Center), direction));
                        double secondPitch = Math.Abs(Dot(
                            Subtract(secondOrdered[index].Center, secondOrdered[index - 1].Center), direction));
                        maximumPitchDifference = Math.Max(maximumPitchDifference,
                            Math.Abs(firstPitch - secondPitch));
                    }
                    if (!compatible || maximumPitchDifference > PositionToleranceM)
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] WHOLE ROW reject adjacency/size/pitch: "
                            + first.ComponentOccurrence + " <-> " + second.ComponentOccurrence
                            + ", compatible=" + compatible
                            + ", pitchDelta=" + FormatMm(maximumPitchDifference) + "mm");
                        continue;
                    }

                    double[] commonAxis = AlignAndAverageRowDirection(
                        first.AxisDirection, second.AxisDirection);
                    double maximumAcross = 0.0;
                    double minimumShift = double.PositiveInfinity;
                    for (int index = 0; index < firstOrdered.Count; index++)
                    {
                        double[] delta = Subtract(secondOrdered[index].Center, firstOrdered[index].Center);
                        double[] planar = Subtract(delta, Scale(commonAxis, Dot(delta, commonAxis)));
                        double along = Dot(planar, direction);
                        double across = Length(Subtract(planar, Scale(direction, along)));
                        maximumAcross = Math.Max(maximumAcross, across);
                        minimumShift = Math.Min(minimumShift, Length(planar));
                    }
                    if (maximumAcross > PatternRowToleranceM || minimumShift <= PositionToleranceM)
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] WHOLE ROW reject row/shift: "
                            + first.ComponentOccurrence + " <-> " + second.ComponentOccurrence
                            + ", across=" + FormatMm(maximumAcross) + "mm"
                            + ", minimumShift=" + FormatMm(minimumShift) + "mm");
                        continue;
                    }

                    Debug.WriteLine("[CHECK HOLE ASSY] WHOLE ROW accepted: "
                        + first.ComponentOccurrence + " <-> " + second.ComponentOccurrence
                        + ", holes=" + firstOrdered.Count
                        + ", overlap=" + overlapFraction.ToString("0.###", CultureInfo.InvariantCulture)
                        + ", pitchDelta=" + FormatMm(maximumPitchDifference) + "mm"
                        + ", faceGap=" + FormatMm(maximumFaceGap) + "mm");

                    for (int index = 0; index < firstOrdered.Count; index++)
                    {
                        HoleAxis expected = firstOrdered[index];
                        HoleAxis actual = secondOrdered[index];
                        double positionError = TransverseCenterDistance(expected, actual);
                        if (positionError <= PositionToleranceM)
                            continue;
                        PatternIssue issue = new PatternIssue
                        {
                            FirstComponent = first.ComponentOccurrence,
                            SecondComponent = second.ComponentOccurrence,
                            Kind = "NG DICH CA HANG",
                            AnchorDirection = ClonePoint(commonAxis),
                            ExpectedHole = expected,
                            ActualHole = actual,
                            ExpectedCenter = ClonePoint(expected.Center),
                            ActualCenter = ClonePoint(actual.Center),
                            PositionErrorM = positionError,
                            PitchErrorM = maximumPitchDifference,
                            RowErrorM = maximumAcross
                        };
                        if (issueKeys.Add(BuildPatternIssueKey(issue)))
                        {
                            issues.Add(issue);
                            added++;
                        }
                    }
                }
            }

            Debug.WriteLine("[CHECK HOLE ASSY] WHOLE ROW summary: rows=" + rows.Count
                + ", compared=" + compared + ", issues=" + added);
            return added;
        }

        private static List<IndependentPatternRow> BuildIndependentPatternRows(List<HoleAxis> holes)
        {
            List<IndependentPatternRow> result = new List<IndependentPatternRow>();
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, HoleAxis> componentGroup in holes
                .GroupBy(item => item.ComponentOccurrence ?? "", StringComparer.OrdinalIgnoreCase))
            {
                List<HoleAxis> componentHoles = componentGroup.ToList();
                for (int firstIndex = 0; firstIndex < componentHoles.Count; firstIndex++)
                {
                    HoleAxis anchor = componentHoles[firstIndex];
                    for (int secondIndex = firstIndex + 1; secondIndex < componentHoles.Count; secondIndex++)
                    {
                        HoleAxis second = componentHoles[secondIndex];
                        if (AxisAngleDeg(anchor.Direction, second.Direction) > SearchAngleToleranceDeg)
                            continue;
                        double[] delta = Subtract(second.Center, anchor.Center);
                        double[] planar = Subtract(delta,
                            Scale(anchor.Direction, Dot(delta, anchor.Direction)));
                        double length = Length(planar);
                        if (length < PatternMinimumStepM)
                            continue;
                        double[] direction = Scale(planar, 1.0 / length);
                        CanonicalizeDirection(direction);
                        List<HoleAxis> rowHoles = componentHoles
                            .Where(item => AxisAngleDeg(anchor.Direction, item.Direction) <= SearchAngleToleranceDeg
                                && DistanceFromPatternRow(item.Center, anchor.Center,
                                    anchor.Direction, direction) <= PatternRowToleranceM)
                            .OrderBy(item => Dot(item.Center, direction))
                            .ToList();
                        if (rowHoles.Count < WholeRowMinimumHoleCount)
                            continue;
                        string key = componentGroup.Key + "|" + string.Join(";", rowHoles
                            .Select(item => QuantizePoint(item.Center, SameHolePositionToleranceM))
                            .OrderBy(item => item, StringComparer.Ordinal));
                        if (!keys.Add(key))
                            continue;
                        result.Add(new IndependentPatternRow
                        {
                            ComponentOccurrence = componentGroup.Key,
                            AxisDirection = ClonePoint(anchor.Direction),
                            Direction = direction,
                            Holes = rowHoles
                        });
                    }
                }
            }
            return result;
        }

        private static double[] AlignAndAverageRowDirection(double[] first, double[] second)
        {
            double[] alignedSecond = ClonePoint(second);
            if (Dot(first, alignedSecond) < 0.0)
                alignedSecond = Scale(alignedSecond, -1.0);
            double[] result = Normalize(Add(first, alignedSecond));
            if (!IsDirection(result))
                result = Normalize(first);
            CanonicalizeDirection(result);
            return result;
        }

        private static bool HasNearConcentricAnchor(
            List<HoleAxis> first,
            List<HoleAxis> second)
        {
            foreach (HoleAxis firstHole in first)
            {
                foreach (HoleAxis secondHole in second)
                {
                    double faceGapM;
                    if (AreHoleLayersAdjacent(firstHole, secondHole, out faceGapM)
                        && AxisAngleDeg(firstHole.Direction, secondHole.Direction) <= SearchAngleToleranceDeg
                        && TransverseCenterDistance(firstHole, secondHole) <= PositionToleranceM)
                        return true;
                }
            }
            return false;
        }

        private static double GetProjectedOverlapFraction(
            List<HoleAxis> first,
            List<HoleAxis> second,
            double[] direction)
        {
            double firstMin = first.Min(item => Dot(item.Center, direction));
            double firstMax = first.Max(item => Dot(item.Center, direction));
            double secondMin = second.Min(item => Dot(item.Center, direction));
            double secondMax = second.Max(item => Dot(item.Center, direction));
            double overlap = Math.Max(0.0, Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin));
            double shorterSpan = Math.Min(firstMax - firstMin, secondMax - secondMin);
            return shorterSpan <= SameHolePositionToleranceM ? 0.0 : overlap / shorterSpan;
        }

        private static string BuildIndependentRowPairKey(
            List<HoleAxis> first,
            List<HoleAxis> second)
        {
            string firstKey = (first.Count == 0 ? "" : first[0].ComponentOccurrence) + ":"
                + string.Join(";", first.Select(item => QuantizePoint(item.Center, 0.0001)));
            string secondKey = (second.Count == 0 ? "" : second[0].ComponentOccurrence) + ":"
                + string.Join(";", second.Select(item => QuantizePoint(item.Center, 0.0001)));
            return string.Compare(firstKey, secondKey, StringComparison.OrdinalIgnoreCase) <= 0
                ? firstKey + "<->" + secondKey
                : secondKey + "<->" + firstKey;
        }

        private static Dictionary<string, List<PatternAnchorPair>> BuildPatternAnchorPairs(
            List<HoleStackResult> validStacks)
        {
            Dictionary<string, List<PatternAnchorPair>> result =
                new Dictionary<string, List<PatternAnchorPair>>(StringComparer.OrdinalIgnoreCase);
            foreach (HoleStackResult stackResult in validStacks)
            {
                List<HoleAxis> stackHoles = stackResult.Stack.Holes
                    .OrderBy(item => item.ComponentOccurrence, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                for (int firstIndex = 0; firstIndex < stackHoles.Count; firstIndex++)
                {
                    for (int secondIndex = firstIndex + 1; secondIndex < stackHoles.Count; secondIndex++)
                    {
                        HoleAxis first = stackHoles[firstIndex];
                        HoleAxis second = stackHoles[secondIndex];
                        if (string.Equals(first.ComponentOccurrence, second.ComponentOccurrence,
                            StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (string.Compare(first.ComponentOccurrence, second.ComponentOccurrence,
                            StringComparison.OrdinalIgnoreCase) > 0)
                        {
                            HoleAxis swap = first;
                            first = second;
                            second = swap;
                        }
                        string key = first.ComponentOccurrence + " <-> " + second.ComponentOccurrence;
                        List<PatternAnchorPair> list;
                        if (!result.TryGetValue(key, out list))
                        {
                            list = new List<PatternAnchorPair>();
                            result.Add(key, list);
                        }
                        if (list.Any(item => Distance(item.First.Center, first.Center) <= SameHolePositionToleranceM
                            && Distance(item.Second.Center, second.Center) <= SameHolePositionToleranceM))
                            continue;
                        list.Add(new PatternAnchorPair { First = first, Second = second });
                    }
                }
            }
            return result;
        }

        private static List<PatternRowCandidate> BuildPatternRowCandidates(
            List<PatternAnchorPair> anchors,
            List<HoleAxis> firstHoles,
            List<HoleAxis> secondHoles)
        {
            List<PatternRowCandidate> result = new List<PatternRowCandidate>();
            foreach (PatternAnchorPair anchor in anchors)
            {
                List<PatternVector> firstVectors = GetPatternVectors(firstHoles, anchor.First);
                List<PatternVector> secondVectors = GetPatternVectors(secondHoles, anchor.Second);
                List<double[]> directions = MatchPatternDirections(firstVectors, secondVectors);

                // Sai so pitch co the cong don lon hon gioi han recovery, vi du
                // 240/480/720 mm so voi 250/500/750 mm. Neu chi ghep tung vector
                // giua hai part thi huong hang chinh co the bi loai truoc khi den
                // buoc so sanh theo thu tu. Bo sung huong doc lap tu moi part;
                // dieu kien hang >= 3 lo ben duoi se loai cac huong noi ngau nhien.
                AddIndependentPatternDirections(directions, firstVectors);
                AddIndependentPatternDirections(directions, secondVectors);

                foreach (double[] direction in directions)
                {
                    List<PatternVector> firstRow = BuildPatternRowSigned(firstHoles, anchor.First, direction);
                    List<PatternVector> secondRow = BuildPatternRowSigned(secondHoles, anchor.Second, direction);
                    if (firstRow.Count < 2 || secondRow.Count < 2)
                        continue;

                    int commonAnchorSupport = CountAnchorSupportOnRow(anchors, anchor, direction);
                    // Mot neo duy nhat khong du de xac nhan hang chi co hai lo.
                    // Hang 2 lo chi duoc chap nhan khi co them mot neo dong tam tren cung hang.
                    if (commonAnchorSupport < 2 && Math.Min(firstRow.Count, secondRow.Count) < 3)
                        continue;

                    double firstNearest = firstRow.Where(item => Math.Abs(item.DistanceM) >= PatternMinimumStepM)
                        .Select(item => Math.Abs(item.DistanceM)).DefaultIfEmpty(double.MaxValue).Min();
                    double secondNearest = secondRow.Where(item => Math.Abs(item.DistanceM) >= PatternMinimumStepM)
                        .Select(item => Math.Abs(item.DistanceM)).DefaultIfEmpty(double.MaxValue).Min();
                    double nearestDifference = Math.Abs(firstNearest - secondNearest);
                    double nearestGate = Math.Max(RecoveryMaximumPositionM,
                        Math.Max(firstNearest, secondNearest) * RecoveryPitchFraction);
                    if (nearestDifference > nearestGate)
                        continue;

                    PatternRowCandidate candidate = new PatternRowCandidate
                    {
                        Anchor = anchor,
                        Direction = direction,
                        FirstRow = firstRow,
                        SecondRow = secondRow,
                        CommonAnchorSupport = commonAnchorSupport
                    };
                    candidate.Score = commonAnchorSupport * 10000
                        + Math.Min(firstRow.Count, secondRow.Count) * 100
                        + Math.Max(firstRow.Count, secondRow.Count);
                    result.Add(candidate);
                }
            }
            return result;
        }

        private static void AddIndependentPatternDirections(
            List<double[]> directions,
            List<PatternVector> vectors)
        {
            foreach (PatternVector vector in vectors)
            {
                if (vector == null || !IsDirection(vector.Direction))
                    continue;
                double[] direction = Normalize(vector.Direction);
                if (directions.Any(existing => OrientedAxisAngleDeg(existing, direction)
                    <= PatternDirectionMergeToleranceDeg))
                    continue;
                directions.Add(direction);
            }
        }

        private static int CountAnchorSupportOnRow(
            List<PatternAnchorPair> anchors,
            PatternAnchorPair origin,
            double[] direction)
        {
            int count = 0;
            foreach (PatternAnchorPair item in anchors)
            {
                double firstRowError = DistanceFromPatternRow(
                    item.First.Center, origin.First.Center, origin.First.Direction, direction);
                double secondRowError = DistanceFromPatternRow(
                    item.Second.Center, origin.Second.Center, origin.Second.Direction, direction);
                if (firstRowError <= PositionToleranceM * 2.0
                    && secondRowError <= PositionToleranceM * 2.0)
                    count++;
            }
            return count;
        }

        private static double DistanceFromPatternRow(
            double[] point,
            double[] origin,
            double[] holeAxis,
            double[] rowDirection)
        {
            double[] delta = Subtract(point, origin);
            double[] planar = Subtract(delta, Scale(holeAxis, Dot(delta, holeAxis)));
            return Length(Subtract(planar, Scale(rowDirection, Dot(planar, rowDirection))));
        }

        private static bool SamePhysicalPatternRow(
            PatternRowCandidate first,
            PatternRowCandidate second)
        {
            // Chi coi la cung mot hang khi hai huong gan nhu trung nhau.
            // PatternDirectionToleranceDeg (3 do) chi la dung sai nhan dang
            // giua hai component; neu dung o day, hang ngan co diem neo cao
            // co the loai mat huong chinh xac cua hang pattern dai.
            if (OrientedAxisAngleDeg(first.Direction, second.Direction) > PatternDirectionMergeToleranceDeg)
                return false;
            double firstDistance = DistanceFromPatternRow(
                second.Anchor.First.Center,
                first.Anchor.First.Center,
                first.Anchor.First.Direction,
                first.Direction);
            double secondDistance = DistanceFromPatternRow(
                second.Anchor.Second.Center,
                first.Anchor.Second.Center,
                first.Anchor.Second.Direction,
                first.Direction);
            return firstDistance <= PatternRowToleranceM && secondDistance <= PatternRowToleranceM;
        }

        private static List<PatternVector> GetPatternVectors(List<HoleAxis> holes, HoleAxis anchor)
        {
            List<PatternVector> result = new List<PatternVector>();
            foreach (HoleAxis hole in holes)
            {
                if (!string.Equals(hole.ComponentOccurrence, anchor.ComponentOccurrence, StringComparison.OrdinalIgnoreCase)
                    || ReferenceEquals(hole, anchor)
                    || AxisAngleDeg(hole.Direction, anchor.Direction) > SearchAngleToleranceDeg)
                    continue;

                double[] delta = Subtract(hole.Center, anchor.Center);
                double[] projected = Subtract(delta, Scale(anchor.Direction, Dot(delta, anchor.Direction)));
                double length = Length(projected);
                if (length < PatternMinimumStepM)
                    continue;
                result.Add(new PatternVector
                {
                    Hole = hole,
                    Direction = Scale(projected, 1.0 / length),
                    DistanceM = length
                });
            }
            return result;
        }

        private static List<double[]> MatchPatternDirections(
            List<PatternVector> first,
            List<PatternVector> second)
        {
            List<double[]> directions = new List<double[]>();
            foreach (PatternVector firstVector in first)
            {
                foreach (PatternVector secondVector in second)
                {
                    if (OrientedAngleDeg(firstVector.Direction, secondVector.Direction)
                        > PatternDirectionToleranceDeg)
                        continue;
                    double distanceDifference = Math.Abs(firstVector.DistanceM - secondVector.DistanceM);
                    double distanceGate = Math.Max(
                        RecoveryMaximumPositionM,
                        Math.Max(firstVector.DistanceM, secondVector.DistanceM) * RecoveryPitchFraction);
                    if (distanceDifference > distanceGate)
                        continue;
                    double[] direction = Normalize(Add(firstVector.Direction, secondVector.Direction));
                    if (!IsDirection(direction))
                        continue;
                    if (directions.Any(existing => OrientedAngleDeg(existing, direction)
                        <= PatternDirectionMergeToleranceDeg))
                        continue;
                    directions.Add(direction);
                }
            }
            return directions;
        }

        private static List<PatternVector> BuildPatternRowSigned(
            List<HoleAxis> holes,
            HoleAxis anchor,
            double[] rowDirection)
        {
            List<PatternVector> row = new List<PatternVector>
            {
                new PatternVector { Hole = anchor, Direction = rowDirection, DistanceM = 0.0 }
            };
            foreach (HoleAxis hole in holes)
            {
                if (ReferenceEquals(hole, anchor)
                    || AxisAngleDeg(hole.Direction, anchor.Direction) > SearchAngleToleranceDeg)
                    continue;
                double[] delta = Subtract(hole.Center, anchor.Center);
                double[] projected = Subtract(delta, Scale(anchor.Direction, Dot(delta, anchor.Direction)));
                double distanceAlong = Dot(projected, rowDirection);
                if (Math.Abs(distanceAlong) < PatternMinimumStepM)
                    continue;
                double rowError = Length(Subtract(projected, Scale(rowDirection, distanceAlong)));
                if (rowError > PatternRowToleranceM)
                    continue;
                row.Add(new PatternVector
                {
                    Hole = hole,
                    Direction = rowDirection,
                    DistanceM = distanceAlong,
                    RowErrorM = rowError
                });
            }
            return row.OrderBy(item => item.DistanceM)
                .GroupBy(item => Math.Round(item.DistanceM / SameHolePositionToleranceM))
                .Select(group => group.OrderBy(item => item.RowErrorM).First())
                .ToList();
        }

        private static List<PatternVector> BuildPatternRow(
            List<HoleAxis> holes,
            HoleAxis anchor,
            double[] rowDirection)
        {
            List<PatternVector> row = new List<PatternVector>
            {
                new PatternVector { Hole = anchor, Direction = rowDirection, DistanceM = 0.0 }
            };
            foreach (HoleAxis hole in holes)
            {
                if (!string.Equals(hole.ComponentOccurrence, anchor.ComponentOccurrence, StringComparison.OrdinalIgnoreCase)
                    || ReferenceEquals(hole, anchor)
                    || AxisAngleDeg(hole.Direction, anchor.Direction) > SearchAngleToleranceDeg)
                    continue;

                double[] delta = Subtract(hole.Center, anchor.Center);
                double[] projected = Subtract(delta, Scale(anchor.Direction, Dot(delta, anchor.Direction)));
                double distanceAlong = Dot(projected, rowDirection);
                if (distanceAlong < PatternMinimumStepM)
                    continue;
                double rowError = Length(Subtract(projected, Scale(rowDirection, distanceAlong)));
                if (rowError > PatternRowToleranceM)
                    continue;
                row.Add(new PatternVector
                {
                    Hole = hole,
                    Direction = rowDirection,
                    DistanceM = distanceAlong,
                    RowErrorM = rowError
                });
            }
            return row
                .OrderBy(item => item.DistanceM)
                .GroupBy(item => Math.Round(item.DistanceM / SameHolePositionToleranceM))
                .Select(group => group.OrderBy(item => item.RowErrorM).First())
                .ToList();
        }

        private static void ComparePatternRows(
            HoleAxis firstAnchor,
            HoleAxis secondAnchor,
            double[] rowDirection,
            List<PatternVector> firstRow,
            List<PatternVector> secondRow,
            List<PatternIssue> issues,
            HashSet<string> issueKeys)
        {
            ComparePatternRowSide(firstAnchor, secondAnchor, rowDirection,
                firstRow.Where(item => item.DistanceM > PatternMinimumStepM * 0.5)
                    .OrderBy(item => item.DistanceM).ToList(),
                secondRow.Where(item => item.DistanceM > PatternMinimumStepM * 0.5)
                    .OrderBy(item => item.DistanceM).ToList(), issues, issueKeys);
            ComparePatternRowSide(firstAnchor, secondAnchor, Scale(rowDirection, -1.0),
                firstRow.Where(item => item.DistanceM < -PatternMinimumStepM * 0.5)
                    .OrderByDescending(item => item.DistanceM)
                    .Select(item => new PatternVector { Hole = item.Hole, Direction = Scale(rowDirection, -1.0), DistanceM = -item.DistanceM, RowErrorM = item.RowErrorM }).ToList(),
                secondRow.Where(item => item.DistanceM < -PatternMinimumStepM * 0.5)
                    .OrderByDescending(item => item.DistanceM)
                    .Select(item => new PatternVector { Hole = item.Hole, Direction = Scale(rowDirection, -1.0), DistanceM = -item.DistanceM, RowErrorM = item.RowErrorM }).ToList(),
                issues, issueKeys);
        }

        private static void ComparePatternRowSide(
            HoleAxis firstAnchor,
            HoleAxis secondAnchor,
            double[] rowDirection,
            List<PatternVector> firstRow,
            List<PatternVector> secondRow,
            List<PatternIssue> issues,
            HashSet<string> issueKeys)
        {
            if (firstRow.Count == 0 || secondRow.Count == 0)
                return;

            PatternAssignmentResult assignment = BuildAnchorGuidedPatternAssignment(
                firstAnchor, secondAnchor, rowDirection, firstRow, secondRow);
            Debug.WriteLine("[CHECK HOLE ASSY] PATTERN_ASSIGNMENT"
                + " componentA=" + firstAnchor.ComponentOccurrence
                + " componentB=" + secondAnchor.ComponentOccurrence
                + " anchorCount=1"
                + " sourceCount=" + firstRow.Count
                + " targetCount=" + secondRow.Count
                + " matchedCount=" + assignment.Matches.Count
                + " gapSourceCount=" + assignment.GapSourceCount
                + " gapTargetCount=" + assignment.GapTargetCount
                + " ambiguousCount=" + assignment.AmbiguousCount
                + " mode=ANCHOR_GUIDED_MONOTONIC"
                + " rejectExpectedPosition=" + assignment.RejectExpectedPosition
                + " rejectPitchShift=" + assignment.RejectPitchShift
                + " rejectAmbiguous=" + assignment.RejectAmbiguous
                + " gapAccepted=" + assignment.GapAccepted);
            if (assignment.GapAccepted > 0)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] PATTERN_UNMATCHED"
                    + " componentA=" + firstAnchor.ComponentOccurrence
                    + " componentB=" + secondAnchor.ComponentOccurrence
                    + " gapSourceCount=" + assignment.GapSourceCount
                    + " gapTargetCount=" + assignment.GapTargetCount
                    + " productionResult=NONE");
            }

            foreach (PatternRowMatch match in assignment.AllMatches)
            {
                if (match.IsAmbiguous)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] PATTERN_UNMATCHED status=AMBIGUOUS"
                        + ", source=" + FormatPointMm(match.First.Hole.Center)
                        + ", target=" + FormatPointMm(match.Second.Hole.Center)
                        + ", confidence=" + match.Confidence.ToString("0.###", CultureInfo.InvariantCulture));
                    continue;
                }

                double comparedFaceGapM;
                if (!AreHoleLayersAdjacent(match.First.Hole, match.Second.Hole, out comparedFaceGapM))
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] PATTERN skip non-adjacent logical holes: "
                        + match.First.Hole.ComponentOccurrence + " " + (match.First.Hole.IsSlot ? "SLOT" : "ROUND")
                        + " center=" + FormatPointMm(match.First.Hole.Center)
                        + " <-> "
                        + match.Second.Hole.ComponentOccurrence + " " + (match.Second.Hole.IsSlot ? "SLOT" : "ROUND")
                        + " center=" + FormatPointMm(match.Second.Hole.Center)
                        + ", faceGap=" + FormatMm(comparedFaceGapM) + "mm"
                        + ", max=" + FormatMm(AdjacentFaceGapM) + "mm");
                    continue;
                }

                PatternIssue issue = new PatternIssue
                {
                    FirstComponent = firstAnchor.ComponentOccurrence,
                    SecondComponent = secondAnchor.ComponentOccurrence,
                    AnchorDirection = ClonePoint(firstAnchor.Direction),
                    ExpectedHole = match.First.Hole,
                    ActualHole = match.Second.Hole,
                    ExpectedCenter = ClonePoint(match.ExpectedCenter),
                    ActualCenter = ClonePoint(match.Second.Hole.Center),
                    PositionErrorM = match.AlongResidualM,
                    RowErrorM = match.AcrossResidualM
                };

                if (!IsPoint(issue.ExpectedCenter) || !IsPoint(issue.ActualCenter))
                    continue;

                issue.PitchErrorM = Math.Abs(match.Second.DistanceM - match.PreviousSecondDistanceM
                    - (match.First.DistanceM - match.PreviousFirstDistanceM));

                bool badPosition = issue.PositionErrorM > PositionToleranceM;
                bool badPitch = issue.PitchErrorM > PositionToleranceM;
                bool badRow = issue.RowErrorM > PositionToleranceM;
                if (!badPosition && !badPitch && !badRow)
                    continue;
                Debug.WriteLine("[CHECK HOLE ASSY] PATTERN relative row: "
                    + firstAnchor.ComponentOccurrence + " <-> " + secondAnchor.ComponentOccurrence
                    + ", source=" + FormatPointMm(match.First.Hole.Center)
                    + ", target=" + FormatPointMm(match.Second.Hole.Center)
                    + ", rowDelta=" + FormatMm(issue.RowErrorM) + "mm"
                    + ", alongDelta=" + FormatMm(issue.PositionErrorM) + "mm"
                    + ", pitchDelta=" + FormatMm(issue.PitchErrorM) + "mm"
                    + ", localPitch=" + FormatMm(match.LocalPitchM) + "mm"
                    + ", confidence=" + match.Confidence.ToString("0.###", CultureInfo.InvariantCulture));
                issue.Kind = badRow
                    ? "NG ROW"
                    : (badPosition && badPitch ? "NG POSITION+PITCH" : (badPitch ? "NG PITCH" : "NG POSITION"));

                string key = BuildPatternIssueKey(issue);
                if (issueKeys.Add(key))
                    issues.Add(issue);
            }
        }

        private static PatternAssignmentResult BuildAnchorGuidedPatternAssignment(
            HoleAxis firstAnchor,
            HoleAxis secondAnchor,
            double[] rowDirection,
            List<PatternVector> firstRow,
            List<PatternVector> secondRow)
        {
            int firstCount = firstRow.Count;
            int secondCount = secondRow.Count;
            PatternMatchCandidate[,] candidates = new PatternMatchCandidate[firstCount, secondCount];
            PatternAssignmentResult result = new PatternAssignmentResult();
            for (int firstIndex = 0; firstIndex < firstCount; firstIndex++)
            {
                for (int secondIndex = 0; secondIndex < secondCount; secondIndex++)
                {
                    PatternMatchCandidate candidate = BuildPatternMatchCandidate(
                        firstAnchor, secondAnchor, rowDirection, firstRow, firstIndex, secondRow, secondIndex);
                    candidates[firstIndex, secondIndex] = candidate;
                    if (!candidate.IsValid)
                    {
                        result.RejectExpectedPosition++;
                        if (candidate.IsPitchShift)
                            result.RejectPitchShift++;
                    }
                }
            }

            const double gapCost = 1.05;
            double[,] cost = new double[firstCount + 1, secondCount + 1];
            PatternAssignmentStep[,] steps = new PatternAssignmentStep[firstCount + 1, secondCount + 1];
            for (int firstIndex = 0; firstIndex <= firstCount; firstIndex++)
                for (int secondIndex = 0; secondIndex <= secondCount; secondIndex++)
                    cost[firstIndex, secondIndex] = double.PositiveInfinity;
            cost[0, 0] = 0.0;
            for (int firstIndex = 0; firstIndex <= firstCount; firstIndex++)
            {
                for (int secondIndex = 0; secondIndex <= secondCount; secondIndex++)
                {
                    double current = cost[firstIndex, secondIndex];
                    if (double.IsInfinity(current))
                        continue;
                    if (firstIndex < firstCount && secondIndex < secondCount)
                    {
                        PatternMatchCandidate candidate = candidates[firstIndex, secondIndex];
                        if (candidate.IsValid && current + candidate.Cost < cost[firstIndex + 1, secondIndex + 1])
                        {
                            cost[firstIndex + 1, secondIndex + 1] = current + candidate.Cost;
                            steps[firstIndex + 1, secondIndex + 1] = PatternAssignmentStep.Match;
                        }
                    }
                    if (firstIndex < firstCount && current + gapCost < cost[firstIndex + 1, secondIndex])
                    {
                        cost[firstIndex + 1, secondIndex] = current + gapCost;
                        steps[firstIndex + 1, secondIndex] = PatternAssignmentStep.GapTarget;
                    }
                    if (secondIndex < secondCount && current + gapCost < cost[firstIndex, secondIndex + 1])
                    {
                        cost[firstIndex, secondIndex + 1] = current + gapCost;
                        steps[firstIndex, secondIndex + 1] = PatternAssignmentStep.GapSource;
                    }
                }
            }

            List<PatternRowMatch> reverseMatches = new List<PatternRowMatch>();
            int rowFirst = firstCount;
            int rowSecond = secondCount;
            while (rowFirst > 0 || rowSecond > 0)
            {
                PatternAssignmentStep step = steps[rowFirst, rowSecond];
                if (step == PatternAssignmentStep.Match)
                {
                    PatternMatchCandidate candidate = candidates[rowFirst - 1, rowSecond - 1];
                    reverseMatches.Add(new PatternRowMatch
                    {
                        First = firstRow[rowFirst - 1],
                        Second = secondRow[rowSecond - 1],
                        ExpectedCenter = candidate.ExpectedCenter,
                        AlongResidualM = candidate.AlongResidualM,
                        AcrossResidualM = candidate.AcrossResidualM,
                        LocalPitchM = candidate.LocalPitchM,
                        Confidence = candidate.Confidence
                    });
                    rowFirst--;
                    rowSecond--;
                }
                else if (step == PatternAssignmentStep.GapTarget)
                {
                    result.GapSourceCount++;
                    rowFirst--;
                }
                else
                {
                    result.GapTargetCount++;
                    rowSecond--;
                }
            }

            reverseMatches.Reverse();
            for (int index = 0; index < reverseMatches.Count; index++)
            {
                PatternRowMatch match = reverseMatches[index];
                match.PreviousFirstDistanceM = index == 0 ? 0.0 : reverseMatches[index - 1].First.DistanceM;
                match.PreviousSecondDistanceM = index == 0 ? 0.0 : reverseMatches[index - 1].Second.DistanceM;
                result.AllMatches.Add(match);
                int firstIndex = firstRow.IndexOf(match.First);
                int secondIndex = secondRow.IndexOf(match.Second);
                match.IsAmbiguous = IsAmbiguousPatternMatch(candidates, firstIndex, secondIndex);
                if (match.IsAmbiguous)
                {
                    result.AmbiguousCount++;
                    result.RejectAmbiguous++;
                }
                else
                    result.Matches.Add(match);
            }
            result.GapAccepted = result.GapSourceCount + result.GapTargetCount;
            return result;
        }

        private static PatternMatchCandidate BuildPatternMatchCandidate(
            HoleAxis firstAnchor,
            HoleAxis secondAnchor,
            double[] rowDirection,
            List<PatternVector> firstRow,
            int firstIndex,
            List<PatternVector> secondRow,
            int secondIndex)
        {
            PatternVector first = firstRow[firstIndex];
            PatternVector second = secondRow[secondIndex];
            double[] firstDelta = Subtract(first.Hole.Center, firstAnchor.Center);
            double[] firstProjected = Subtract(firstDelta,
                Scale(firstAnchor.Direction, Dot(firstDelta, firstAnchor.Direction)));
            double firstAlong = Dot(firstProjected, rowDirection);
            double[] firstAcross = Subtract(firstProjected, Scale(rowDirection, firstAlong));
            double[] expectedCenter = Add(Add(secondAnchor.Center, Scale(rowDirection, firstAlong)), firstAcross);
            double[] secondDelta = Subtract(second.Hole.Center, secondAnchor.Center);
            double[] secondProjected = Subtract(secondDelta,
                Scale(secondAnchor.Direction, Dot(secondDelta, secondAnchor.Direction)));
            double secondAlong = Dot(secondProjected, rowDirection);
            double[] secondAcross = Subtract(secondProjected, Scale(rowDirection, secondAlong));
            double alongResidual = Math.Abs(secondAlong - firstAlong);
            double acrossResidual = Length(Subtract(secondAcross, firstAcross));
            double residual = Math.Sqrt(alongResidual * alongResidual + acrossResidual * acrossResidual);
            double firstPitch = GetLocalPatternStep(firstRow, firstIndex);
            double secondPitch = GetLocalPatternStep(secondRow, secondIndex);
            double localPitch = GetFiniteLocalPitch(firstPitch, secondPitch);
            PatternMatchCandidate result = new PatternMatchCandidate
            {
                ExpectedCenter = expectedCenter,
                AlongResidualM = alongResidual,
                AcrossResidualM = acrossResidual,
                LocalPitchM = localPitch
            };
            if (double.IsInfinity(localPitch) || localPitch <= PatternMinimumStepM || !IsPoint(expectedCenter))
                return result;

            double positionGate = localPitch * RecoveryPitchFraction;
            result.IsPitchShift = residual >= localPitch * 0.75;
            if (residual > positionGate)
                return result;

            result.IsValid = true;
            result.Cost = residual / positionGate;
            result.Confidence = Math.Max(0.0, 1.0 - result.Cost);
            return result;
        }

        private static double GetFiniteLocalPitch(double first, double second)
        {
            bool firstValid = !double.IsInfinity(first) && !double.IsNaN(first) && first > PatternMinimumStepM;
            bool secondValid = !double.IsInfinity(second) && !double.IsNaN(second) && second > PatternMinimumStepM;
            if (firstValid && secondValid)
                return Math.Min(first, second);
            if (firstValid)
                return first;
            return secondValid ? second : double.PositiveInfinity;
        }

        private static bool IsAmbiguousPatternMatch(
            PatternMatchCandidate[,] candidates,
            int firstIndex,
            int secondIndex)
        {
            PatternMatchCandidate selected = candidates[firstIndex, secondIndex];
            if (selected == null || !selected.IsValid)
                return true;
            double secondBest = double.PositiveInfinity;
            int firstCount = candidates.GetLength(0);
            int secondCount = candidates.GetLength(1);
            for (int index = 0; index < secondCount; index++)
            {
                if (index == secondIndex || candidates[firstIndex, index] == null || !candidates[firstIndex, index].IsValid)
                    continue;
                secondBest = Math.Min(secondBest, candidates[firstIndex, index].Cost);
            }
            for (int index = 0; index < firstCount; index++)
            {
                if (index == firstIndex || candidates[index, secondIndex] == null || !candidates[index, secondIndex].IsValid)
                    continue;
                secondBest = Math.Min(secondBest, candidates[index, secondIndex].Cost);
            }
            return !double.IsInfinity(secondBest)
                && secondBest - selected.Cost <= 0.10;
        }

        private static double GetLocalPatternStep(List<PatternVector> row, int index)
        {
            double best = double.PositiveInfinity;
            if (index > 0)
                best = Math.Min(best, Math.Abs(row[index].DistanceM - row[index - 1].DistanceM));
            if (index + 1 < row.Count)
                best = Math.Min(best, Math.Abs(row[index + 1].DistanceM - row[index].DistanceM));
            return best;
        }

        private static string BuildPatternIssueKey(PatternIssue issue)
        {
            double[] point = IsPoint(issue.ActualCenter) ? issue.ActualCenter : issue.ExpectedCenter;
            string first = issue.FirstComponent ?? "";
            string second = issue.SecondComponent ?? "";
            if (string.Compare(first, second, StringComparison.OrdinalIgnoreCase) > 0)
            {
                string swap = first;
                first = second;
                second = swap;
            }
            // Mot vi tri vat ly chi duoc danh dau mot lan, khong lap theo anchor/kind.
            return first + "|" + second + "|"
                + QuantizePoint(point, 0.0005);
        }

        private static string QuantizePoint(double[] point, double unitM)
        {
            if (!IsPoint(point))
                return "NONE";
            return Math.Round(point[0] / unitM).ToString(CultureInfo.InvariantCulture) + ","
                + Math.Round(point[1] / unitM).ToString(CultureInfo.InvariantCulture) + ","
                + Math.Round(point[2] / unitM).ToString(CultureInfo.InvariantCulture);
        }

        private static double OrientedAngleDeg(double[] first, double[] second)
        {
            double value = Math.Max(-1.0, Math.Min(1.0, Dot(first, second)));
            return Math.Acos(value) * 180.0 / Math.PI;
        }

        private static double OrientedAxisAngleDeg(double[] first, double[] second)
        {
            double value = Math.Max(-1.0, Math.Min(1.0, Math.Abs(Dot(first, second))));
            return Math.Acos(value) * 180.0 / Math.PI;
        }

        private void CreateNgMarkerSketch(
            ModelDoc2 model,
            List<HoleStackResult> ngResults,
            List<PatternIssue> patternIssues)
        {
            SketchManager manager = model.SketchManager;
            bool opened = false;
            bool originalAddToDb = false;
            bool originalDisplayWhenAdded = true;
            bool hasOriginalAddToDb = false;
            bool hasOriginalDisplayWhenAdded = false;
            Sketch sketch = null;
            Feature markerFeature = null;
            int red = ColorTranslator.ToWin32(Color.Red);
            Stopwatch markerPrepareWatch = Stopwatch.StartNew();
            HashSet<string> featureNamesBefore = GetTopLevelFeatureNames(model);
            HashSet<HoleAxis> holesAlreadyMarked = new HashSet<HoleAxis>();
            HashSet<string> markerPoints = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> markerLines = new HashSet<string>(StringComparer.Ordinal);
            int duplicateCrossSkipped = 0;
            int duplicateLineSkipped = 0;
            int farLines = 0;
            int normalLines = 0;
            int deformedLines = 0;
            int patternLines = 0;
            markerPrepareWatch.Stop();
            if (activeProfile != null)
                activeProfile.MarkerPrepareMs += markerPrepareWatch.ElapsedMilliseconds;
            Stopwatch markerWatch = Stopwatch.StartNew();
            Stopwatch geometryWatch = null;
            // Chi hien thi ket qua da duoc engine xac nhan. Khong tim lai cap lo
            // trong luc ve sketch vi dieu do co the tao marker sai hoac trung lap.
            Debug.WriteLine("[CHECK HOLE ASSY] marker mode=NORMAL_FAR_DEFORMED_BATCH");
            try
            {
                // Luu va phuc hoi dung trang thai SketchManager. Khong de lai
                // side effect cho cac lenh ve sketch sau CHECK HOLE.
                try
                {
                    originalAddToDb = manager.AddToDB;
                    hasOriginalAddToDb = true;
                }
                catch { }
                try
                {
                    originalDisplayWhenAdded = manager.DisplayWhenAdded;
                    hasOriginalDisplayWhenAdded = true;
                }
                catch { }
                Stopwatch openSketchWatch = Stopwatch.StartNew();
                if (activeProfile != null) activeProfile.SelectionCalls++;
                model.ClearSelection2(true);
                manager.Insert3DSketch(true);
                if (activeProfile != null) activeProfile.SketchCreateCalls++;
                opened = true;
                sketch = GetActiveSketch(model);
                try { manager.AddToDB = true; } catch { }
                try { manager.DisplayWhenAdded = false; } catch { }
                openSketchWatch.Stop();
                if (activeProfile != null)
                    activeProfile.MarkerOpenSketchMs += openSketchWatch.ElapsedMilliseconds;

                geometryWatch = Stopwatch.StartNew();

                foreach (HoleStackResult result in ngResults)
                {
                    if (result == null || result.Stack == null || result.Reference == null)
                        continue;
                    Debug.WriteLine("[CHECK HOLE ASSY] MARK stack reference="
                        + result.Reference.ComponentOccurrence
                        + ", center=" + FormatPointMm(result.Reference.Center)
                        + ", outliers=" + result.Outliers.Count);

                    // FAR NG: FarActualHoles la tam lo thuc te bi lech. Danh dau tai
                    // lo thuc te; chi noi ve tam tham chieu khi lay duoc cap mieng lo
                    // doi dien tren hai lop ke nhau.
                    if (result.Stack.MatchSource == HoleMatchSource.FarMisalignmentRecovery)
                    {
                        foreach (FarMarkerRelation relation in result.Stack.FarMarkerRelations)
                        {
                            if (relation == null || relation.SourceHole == null || relation.CounterpartHole == null)
                                continue;
                            double[] expectedMouth;
                            double[] actualMouth;
                            bool hasFacingPair = TryGetClosestMouthPair(
                                relation.CounterpartHole, relation.SourceHole,
                                out expectedMouth, out actualMouth);
                            if (!hasFacingPair)
                            {
                                Debug.WriteLine("[CHECK HOLE ASSY] MARK FAR skipped invalid exact pair source="
                                    + relation.SourceHole.ComponentOccurrence + ", counterpart="
                                    + relation.CounterpartHole.ComponentOccurrence);
                                continue;
                            }

                            bool actualCreated = CreateDistinctColoredPoint(manager, actualMouth, red, markerPoints);
                            bool counterpartCreated = CreateDistinctColoredPoint(manager, expectedMouth, red, markerPoints);
                            if (!actualCreated) duplicateCrossSkipped++;
                            if (!counterpartCreated) duplicateCrossSkipped++;
                            bool lineCreated = CreateDistinctColoredLine(manager, expectedMouth, actualMouth, red, markerLines);
                            if (!lineCreated) duplicateLineSkipped++;
                            else farLines++;
                            holesAlreadyMarked.Add(relation.SourceHole);
                            Debug.WriteLine("[CHECK HOLE ASSY] MARK FAR source="
                                + relation.SourceHole.ComponentOccurrence + ", counterpart="
                                + relation.CounterpartHole.ComponentOccurrence + ", actualMouth="
                                + FormatPointMm(actualMouth) + ", counterpartMouth="
                                + FormatPointMm(expectedMouth) + ", offset="
                                + FormatMm(relation.TransverseOffsetM) + "mm, referenceLine=True, lineCreated="
                                + lineCreated.ToString().ToLowerInvariant());
                        }
                        continue;
                    }

                    foreach (HoleAxis outlier in result.Outliers)
                    {
                        double[] referenceMouth;
                        double[] actualMouth;
                        if (!TryGetClosestMouthPair(
                            result.Reference,
                            outlier,
                            out referenceMouth,
                            out actualMouth))
                        {
                            Debug.WriteLine("[CHECK HOLE ASSY] MARK skipped invalid mouth pair="
                                + result.Reference.ComponentOccurrence + " <-> "
                                + outlier.ComponentOccurrence);
                            continue;
                        }

                        // Deformed NG: marker phai bam dung mieng lo recovered/thuc te
                        // cua occurrence do. Khong ve line tham chieu de tranh line treo
                        // khi hai mouth khong nam tren cung lop hien thi.
                        if (outlier.Source == HoleSource.RecoveredDeformed)
                        {
                            bool deformedReferenceCreated = CreateDistinctColoredPoint(manager, referenceMouth, red, markerPoints);
                            bool deformedActualCreated = CreateDistinctColoredPoint(manager, actualMouth, red, markerPoints);
                            if (!deformedReferenceCreated) duplicateCrossSkipped++;
                            if (!deformedActualCreated) duplicateCrossSkipped++;
                            bool deformedLineCreated = CreateDistinctColoredLine(manager, referenceMouth, actualMouth, red, markerLines);
                            if (!deformedLineCreated) duplicateLineSkipped++;
                            else deformedLines++;
                            holesAlreadyMarked.Add(outlier);
                            Debug.WriteLine("[CHECK HOLE ASSY] MARK DEFORMED reference="
                                + result.Reference.ComponentOccurrence + ", actual=" + outlier.ComponentOccurrence
                                + ", referenceMouth=" + FormatPointMm(referenceMouth)
                                + ", actualMouth=" + FormatPointMm(actualMouth)
                                + ", lineCreated=" + deformedLineCreated.ToString().ToLowerInvariant());
                            continue;
                        }

                        // Normal NG: giu dung behavior cu - dat marker tai hai mieng lo
                        // va noi cap lo da duoc stack xac nhan.
                        bool referenceCreated = CreateDistinctColoredPoint(manager, referenceMouth, red, markerPoints);
                        bool actualCreated = CreateDistinctColoredPoint(manager, actualMouth, red, markerPoints);
                        if (!referenceCreated) duplicateCrossSkipped++;
                        if (!actualCreated) duplicateCrossSkipped++;
                        bool lineCreated = CreateDistinctColoredLine(manager, referenceMouth, actualMouth, red, markerLines);
                        if (!lineCreated) duplicateLineSkipped++;
                        else normalLines++;
                        holesAlreadyMarked.Add(outlier);
                        Debug.WriteLine("[CHECK HOLE ASSY] MARK NORMAL reference="
                            + result.Reference.ComponentOccurrence + ", outlier=" + outlier.ComponentOccurrence
                            + ", lineCreated=" + lineCreated.ToString().ToLowerInvariant());
                    }
                }

                foreach (PatternIssue issue in patternIssues)
                {
                    // Cung mot lo co the bi bat boi ca check dong tam va check pitch.
                    // Chi ve mot bo marker de tranh dau +/X bi lap va che kin lo.
                    if (issue.ActualHole != null && holesAlreadyMarked.Contains(issue.ActualHole))
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] MARK pattern skipped duplicate hole="
                            + issue.ActualHole.ComponentOccurrence
                            + ", center=" + FormatPointMm(issue.ActualHole.Center));
                        continue;
                    }
                    double[] expectedMouth;
                    double[] actualMouth;
                    if (issue.ExpectedHole == null
                        || issue.ActualHole == null
                        || !TryGetClosestMouthPair(
                            issue.ExpectedHole,
                            issue.ActualHole,
                            out expectedMouth,
                            out actualMouth))
                    {
                        Debug.WriteLine("[CHECK HOLE ASSY] MARK pattern skipped invalid mouth pair="
                            + issue.FirstComponent + " <-> " + issue.SecondComponent);
                        continue;
                    }

                    double[] expectedOnExpectedMouth = ProjectPointToPlaneAlongDirection(
                        issue.ExpectedCenter,
                        expectedMouth,
                        issue.ExpectedHole.Direction);
                    double[] actualOnActualMouth = ProjectPointToPlaneAlongDirection(
                        issue.ActualCenter,
                        actualMouth,
                        issue.ActualHole.Direction);
                    if (!IsPoint(expectedOnExpectedMouth) || !IsPoint(actualOnActualMouth))
                        continue;

                    bool expectedCreated = CreateDistinctColoredPoint(manager, expectedOnExpectedMouth, red, markerPoints);
                    bool actualCreated = CreateDistinctColoredPoint(manager, actualOnActualMouth, red, markerPoints);
                    if (!expectedCreated) duplicateCrossSkipped++;
                    if (!actualCreated) duplicateCrossSkipped++;
                    bool lineCreated = CreateDistinctColoredLine(manager, expectedOnExpectedMouth, actualOnActualMouth, red, markerLines);
                    if (!lineCreated) duplicateLineSkipped++;
                    else patternLines++;
                    if (issue.ActualHole != null)
                        holesAlreadyMarked.Add(issue.ActualHole);
                    Debug.WriteLine("[CHECK HOLE ASSY] MARK pattern pair="
                        + issue.FirstComponent + " <-> " + issue.SecondComponent
                        + ", expectedMouth=" + FormatPointMm(expectedOnExpectedMouth)
                        + ", actualMouth=" + FormatPointMm(actualOnActualMouth));
                }
            }
            finally
            {
                if (geometryWatch != null)
                {
                    geometryWatch.Stop();
                    if (activeProfile != null)
                        activeProfile.MarkerGeometryMs += geometryWatch.ElapsedMilliseconds;
                }
                Stopwatch closeSketchWatch = Stopwatch.StartNew();
                Stopwatch endSketchSetupWatch = Stopwatch.StartNew();
                // Giu DisplayWhenAdded=false trong luc ket thuc sketch de tranh
                // update man hinh cho tung entity. Sketch van duoc batch nhu cu.
                try { manager.AddToDB = false; } catch { }
                endSketchSetupWatch.Stop();
                if (activeProfile != null)
                    activeProfile.MarkerEndSketchSetupMs += endSketchSetupWatch.ElapsedMilliseconds;

                Stopwatch endSketchApiWatch = Stopwatch.StartNew();
                if (opened)
                {
                    try
                    {
                        manager.Insert3DSketch(true);
                        if (activeProfile != null) activeProfile.SketchCreateCalls++;
                    }
                    catch { }
                }
                endSketchApiWatch.Stop();
                if (activeProfile != null)
                {
                    activeProfile.MarkerEndSketchApiMs += endSketchApiWatch.ElapsedMilliseconds;
                    // Solver cua SolidWorks xay ra dong bo trong Insert3DSketch.
                    // API khong tach duoc solver rieng, nen gia tri nay la attribution
                    // (nested), khong cong vao tong thoi gian marker.
                    activeProfile.MarkerSketchSolverMs += endSketchApiWatch.ElapsedMilliseconds;
                }

                Stopwatch displayUpdateWatch = Stopwatch.StartNew();
                try
                {
                    manager.AddToDB = hasOriginalAddToDb ? originalAddToDb : false;
                }
                catch { }
                try
                {
                    manager.DisplayWhenAdded = hasOriginalDisplayWhenAdded
                        ? originalDisplayWhenAdded
                        : true;
                }
                catch { }
                displayUpdateWatch.Stop();
                if (activeProfile != null)
                    activeProfile.MarkerDisplayUpdateMs += displayUpdateWatch.ElapsedMilliseconds;
                closeSketchWatch.Stop();
                if (activeProfile != null)
                    activeProfile.MarkerCloseSketchMs += closeSketchWatch.ElapsedMilliseconds;

                Stopwatch markerFeatureWatch = Stopwatch.StartNew();
                // Feature cua 3D Sketch chi on dinh sau khi thoat che do edit.
                Stopwatch postCloseLookupWatch = Stopwatch.StartNew();
                markerFeature = GetSketchFeature(sketch);
                postCloseLookupWatch.Stop();
                if (activeProfile != null)
                    activeProfile.MarkerPostCloseLookupMs += postCloseLookupWatch.ElapsedMilliseconds;
                if (markerFeature == null)
                {
                    Stopwatch featureTreeWatch = Stopwatch.StartNew();
                    markerFeature = FindNewSketchFeature(model, featureNamesBefore);
                    featureTreeWatch.Stop();
                    if (activeProfile != null)
                        activeProfile.MarkerFeatureTreeUpdateMs += featureTreeWatch.ElapsedMilliseconds;
                }
                if (markerFeature != null)
                {
                    Stopwatch renameWatch = Stopwatch.StartNew();
                    try
                    {
                        markerFeature.Name = MarkerFeaturePrefix + "-"
                            + (ngResults.Count + patternIssues.Count)
                                .ToString("000", CultureInfo.InvariantCulture);
                    }
                    catch { }
                    renameWatch.Stop();
                    if (activeProfile != null)
                        activeProfile.MarkerRenameMs += renameWatch.ElapsedMilliseconds;
                    Debug.WriteLine("[CHECK HOLE ASSY] marker feature="
                        + SafeFeatureName(markerFeature)
                        + ", type=" + SafeFeatureType(markerFeature));
                }
                else
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] marker feature acquisition FAILED after closing 3D sketch.");
                }
                Stopwatch colorWatch = Stopwatch.StartNew();
                ApplyRedFeatureColor(markerFeature);
                colorWatch.Stop();
                if (activeProfile != null)
                    activeProfile.MarkerColorMs += colorWatch.ElapsedMilliseconds;
                markerFeatureWatch.Stop();
                if (activeProfile != null)
                    activeProfile.MarkerFeatureMs += markerFeatureWatch.ElapsedMilliseconds;

                Stopwatch markerCleanupWatch = Stopwatch.StartNew();
                try
                {
                    if (activeProfile != null) activeProfile.SelectionCalls++;
                    model.ClearSelection2(true);
                }
                catch { }
                markerCleanupWatch.Stop();
                if (activeProfile != null)
                {
                    activeProfile.MarkerCleanupMs += markerCleanupWatch.ElapsedMilliseconds;
                    activeProfile.MarkerSelectionMs += markerCleanupWatch.ElapsedMilliseconds;
                }
                markerWatch.Stop();
                Debug.WriteLine("[CHECK HOLE ASSY] MARKER SUMMARY"
                    + " crossCount=" + markerPoints.Count
                    + ", lineCount=" + markerLines.Count
                    + ", duplicateCrossSkipped=" + duplicateCrossSkipped
                    + ", duplicateLineSkipped=" + duplicateLineSkipped
                    + ", farLines=" + farLines
                    + ", normalLines=" + normalLines
                    + ", deformedLines=" + deformedLines
                    + ", patternLines=" + patternLines
                    + ", elapsedMs=" + markerWatch.ElapsedMilliseconds);
                if (activeProfile != null)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] MARKER PROFILE"
                        + " features=" + (markerFeature == null ? 0 : 1)
                        + ", entities=" + (markerPoints.Count + markerLines.Count)
                        + ", crossEntities=" + markerPoints.Count
                        + ", connectingLineEntities=" + markerLines.Count
                        + ", openSketchMs=" + activeProfile.MarkerOpenSketchMs
                        + ", geometryMs=" + activeProfile.MarkerGeometryMs
                        + ", endSketchSetupMs=" + activeProfile.MarkerEndSketchSetupMs
                        + ", endSketchApiMs=" + activeProfile.MarkerEndSketchApiMs
                        + ", sketchSolverMs=" + activeProfile.MarkerSketchSolverMs
                        + ", featureTreeUpdateMs=" + activeProfile.MarkerFeatureTreeUpdateMs
                        + ", displayUpdateMs=" + activeProfile.MarkerDisplayUpdateMs
                        + ", postCloseLookupMs=" + activeProfile.MarkerPostCloseLookupMs
                        + ", postCloseMs=" + activeProfile.MarkerPostCloseLookupMs
                        + ", renameMs=" + activeProfile.MarkerRenameMs
                        + ", colorMs=" + activeProfile.MarkerColorMs
                        + ", selectionMs=" + activeProfile.MarkerSelectionMs
                        + ", rebuildMs=" + activeProfile.MarkerRebuildMs
                        + ", graphicsMs=" + activeProfile.MarkerGraphicsMs
                        + ", totalMarkerMs=" + markerWatch.ElapsedMilliseconds);
                }
            }
        }

        private HashSet<string> GetTopLevelFeatureNames(ModelDoc2 model)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (model == null)
                return result;
            for (Feature feature = model.FirstFeature() as Feature;
                feature != null;
                feature = feature.GetNextFeature() as Feature)
            {
                if (activeProfile != null) activeProfile.FeatureCalls++;
                string name = SafeFeatureName(feature);
                if (!string.IsNullOrWhiteSpace(name))
                    result.Add(name);
            }
            return result;
        }

        private Feature FindNewSketchFeature(
            ModelDoc2 model,
            HashSet<string> featureNamesBefore)
        {
            if (model == null)
                return null;
            Feature fallback = null;
            for (Feature feature = model.FirstFeature() as Feature;
                feature != null;
                feature = feature.GetNextFeature() as Feature)
            {
                if (activeProfile != null) activeProfile.FeatureCalls++;
                string name = SafeFeatureName(feature);
                if (featureNamesBefore != null && featureNamesBefore.Contains(name))
                    continue;
                string type = SafeFeatureType(feature);
                Debug.WriteLine("[CHECK HOLE ASSY] new feature candidate name=" + name
                    + ", type=" + type);
                if (type.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0
                    && type.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0)
                    return feature;
                if (type.IndexOf("ProfileFeature", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("Sketch", StringComparison.OrdinalIgnoreCase) >= 0)
                    fallback = feature;
            }
            return fallback;
        }

        private void CreatePlus(
            SketchManager manager,
            double[] center,
            double[] u,
            double[] v,
            double halfSize,
            int color)
        {
            CreateColoredLine(manager, Add(center, Scale(u, -halfSize)), Add(center, Scale(u, halfSize)), color);
            CreateColoredLine(manager, Add(center, Scale(v, -halfSize)), Add(center, Scale(v, halfSize)), color);
        }

        private void CreateX(
            SketchManager manager,
            double[] center,
            double[] u,
            double[] v,
            double halfSize,
            int color)
        {
            double[] diagonal1 = Normalize(Add(u, v));
            double[] diagonal2 = Normalize(Subtract(u, v));
            CreateColoredLine(manager, Add(center, Scale(diagonal1, -halfSize)), Add(center, Scale(diagonal1, halfSize)), color);
            CreateColoredLine(manager, Add(center, Scale(diagonal2, -halfSize)), Add(center, Scale(diagonal2, halfSize)), color);
        }

        private void CreateAxisLine(
            SketchManager manager,
            double[] center,
            double[] direction,
            double halfSize,
            int color)
        {
            CreateColoredLine(
                manager,
                Add(center, Scale(direction, -halfSize)),
                Add(center, Scale(direction, halfSize)),
                color);
        }

        private void CreateColoredLine(SketchManager manager, double[] start, double[] end, int color)
        {
            if (manager == null || !IsPoint(start) || !IsPoint(end) || Distance(start, end) <= 1e-10)
                return;
            if (activeProfile != null) activeProfile.SketchCreateCalls++;
            SketchSegment segment = manager.CreateLine(
                start[0], start[1], start[2],
                end[0], end[1], end[2]);
            if (segment != null)
            {
                try { segment.Color = color; } catch { }
            }
        }

        private bool CreateDistinctColoredLine(
            SketchManager manager,
            double[] start,
            double[] end,
            int color,
            HashSet<string> createdLines)
        {
            if (createdLines == null)
            {
                CreateColoredLine(manager, start, end, color);
                return true;
            }
            string key = BuildUnorderedMarkerKey(start, end);
            if (!createdLines.Add(key))
                return false;
            CreateColoredLine(manager, start, end, color);
            return true;
        }

        private void CreateColoredPoint(SketchManager manager, double[] point, int color)
        {
            if (manager == null || !IsPoint(point))
                return;
            if (activeProfile != null) activeProfile.SketchCreateCalls++;
            SketchPoint sketchPoint = manager.CreatePoint(point[0], point[1], point[2]);
            if (sketchPoint != null)
            {
                try { sketchPoint.Color = color; } catch { }
            }
        }

        private bool CreateDistinctColoredPoint(
            SketchManager manager,
            double[] point,
            int color,
            HashSet<string> createdPoints)
        {
            if (createdPoints == null)
            {
                CreateColoredPoint(manager, point, color);
                return true;
            }
            string key = QuantizePoint(point, 0.0001);
            if (!createdPoints.Add(key))
                return false;
            CreateColoredPoint(manager, point, color);
            return true;
        }

        private static string BuildUnorderedMarkerKey(double[] first, double[] second)
        {
            string firstKey = QuantizePoint(first, 0.0001);
            string secondKey = QuantizePoint(second, 0.0001);
            if (string.CompareOrdinal(firstKey, secondKey) > 0)
            {
                string swap = firstKey;
                firstKey = secondKey;
                secondKey = swap;
            }
            return firstKey + "|" + secondKey;
        }

        private static void ApplyRedFeatureColor(Feature feature)
        {
            if (feature == null)
                return;
            double[] material =
            {
                1.0, 0.0, 0.0,
                0.5, 1.0, 0.5,
                0.5, 0.0, 0.0
            };
            try
            {
                feature.SetMaterialPropertyValues2(
                    material,
                    (int)swInConfigurationOpts_e.swAllConfiguration,
                    null);
                Debug.WriteLine("[CHECK HOLE ASSY] marker 3D sketch default color=RED, feature="
                    + SafeFeatureName(feature));
                object readBack = feature.GetMaterialPropertyValues2(
                    (int)swInConfigurationOpts_e.swAllConfiguration,
                    null);
                double[] values = ToDoubleArray(readBack);
                Debug.WriteLine("[CHECK HOLE ASSY] marker color readback="
                    + (values.Length >= 3
                        ? values[0].ToString("0.###", CultureInfo.InvariantCulture) + ","
                            + values[1].ToString("0.###", CultureInfo.InvariantCulture) + ","
                            + values[2].ToString("0.###", CultureInfo.InvariantCulture)
                        : "<none>"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] set red marker feature failed: " + ex.Message);
            }
        }

        private static void ApplyRedSketchSegmentColor(Sketch sketch, int red)
        {
            if (sketch == null)
                return;
            try
            {
                object[] segments = sketch.GetSketchSegments() as object[];
                if (segments == null)
                    return;
                int applied = 0;
                foreach (object item in segments)
                {
                    SketchSegment segment = item as SketchSegment;
                    if (segment == null)
                        continue;
                    try
                    {
                        segment.Color = red;
                        applied++;
                    }
                    catch { }
                }
                Debug.WriteLine("[CHECK HOLE ASSY] marker segment color=RED, count=" + applied);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] set red marker segment failed: " + ex.Message);
            }
        }

        private void DeleteOldMarkers(ModelDoc2 model)
        {
            if (model == null)
                return;
            Stopwatch searchWatch = Stopwatch.StartNew();
            List<Feature> oldFeatures = new List<Feature>();
            for (Feature feature = model.FirstFeature() as Feature;
                feature != null;
                feature = feature.GetNextFeature() as Feature)
            {
                if (activeProfile != null) activeProfile.FeatureCalls++;
                string name = SafeFeatureName(feature);
                if (name.StartsWith(MarkerFeaturePrefix, StringComparison.OrdinalIgnoreCase))
                    oldFeatures.Add(feature);
            }
            searchWatch.Stop();
            if (activeProfile != null)
                activeProfile.MarkerSearchMs += searchWatch.ElapsedMilliseconds;

            Stopwatch deleteWatch = Stopwatch.StartNew();
            foreach (Feature feature in oldFeatures)
            {
                try
                {
                    if (activeProfile != null) activeProfile.SelectionCalls++;
                    model.ClearSelection2(true);
                    if (activeProfile != null) activeProfile.SelectionCalls++;
                    if (feature.Select2(false, 0))
                    {
                        bool deleted = false;
                        try { deleted = model.Extension.DeleteSelection2(0); } catch { }
                        if (!deleted)
                            model.EditDelete();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] delete old marker failed. feature="
                    + SafeFeatureName(feature) + ", error=" + ex.Message);
                }
            }
            deleteWatch.Stop();
            if (activeProfile != null)
                activeProfile.MarkerDeleteMs += deleteWatch.ElapsedMilliseconds;
            Debug.WriteLine("[CHECK HOLE ASSY] old marker features found=" + oldFeatures.Count);
            try
            {
                if (activeProfile != null) activeProfile.SelectionCalls++;
                model.ClearSelection2(true);
            }
            catch { }
        }

        private static void LogStackCoverage(List<HoleAxis> holes, List<HoleStack> stacks)
        {
            HashSet<HoleAxis> grouped = new HashSet<HoleAxis>();
            for (int stackIndex = 0; stackIndex < stacks.Count; stackIndex++)
            {
                HoleStack stack = stacks[stackIndex];
                if (VerboseHoleDebug)
                    Debug.WriteLine("[CHECK HOLE ASSY] STACK #" + (stackIndex + 1)
                        + ", members=" + stack.Holes.Count + ", components="
                        + string.Join(", ", stack.Holes.Select(item => item.ComponentOccurrence)));
                foreach (HoleAxis hole in stack.Holes)
                    grouped.Add(hole);
            }

            int unmatched = 0;
            foreach (HoleAxis hole in holes)
            {
                if (grouped.Contains(hole))
                    continue;
                unmatched++;
                if (VerboseHoleDebug)
                    Debug.WriteLine("[CHECK HOLE ASSY] UNMATCHED logical hole: " + DescribeHole(hole));
            }
            Debug.WriteLine("[CHECK HOLE ASSY] coverage grouped=" + grouped.Count
                + ", unmatched=" + unmatched + ", total=" + holes.Count);
        }

        private static string DescribeHole(HoleAxis hole)
        {
            if (hole == null)
                return "<null>";
            return "component=" + hole.ComponentOccurrence
                + ", center=" + FormatPointMm(hole.Center)
                + ", axis=" + FormatVector(hole.Direction)
                + ", radius=" + FormatMm(hole.RadiusM) + "mm"
                + ", axialLength=" + FormatMm(hole.AxialLength) + "mm"
                + ", faces=" + hole.SourceFaceCount
                + ", source=" + GetHoleSourceName(hole)
                + ", type=" + (hole.IsSlot ? "SLOT" : (hole.IsSlotEnd ? "SLOT-END" : "ROUND"))
                + ", path=" + (hole.ComponentPath ?? "");
        }

        private static string GetHoleSourceName(HoleAxis hole)
        {
            return hole == null ? "Unknown" : hole.Source.ToString();
        }

        private static string FormatPointMm(double[] point)
        {
            if (!IsPoint(point))
                return "<invalid>";
            return "(" + FormatMm(point[0]) + ", " + FormatMm(point[1]) + ", "
                + FormatMm(point[2]) + ")mm";
        }

        private static string FormatVector(double[] vector)
        {
            if (!IsPoint(vector))
                return "<invalid>";
            return "(" + vector[0].ToString("0.######", CultureInfo.InvariantCulture)
                + ", " + vector[1].ToString("0.######", CultureInfo.InvariantCulture)
                + ", " + vector[2].ToString("0.######", CultureInfo.InvariantCulture) + ")";
        }

        private static string SafeModelTitle(ModelDoc2 model)
        {
            try { return model == null ? "" : model.GetTitle() ?? ""; }
            catch { return ""; }
        }

        private static string SafeConfigurationName(ModelDoc2 model)
        {
            try
            {
                Configuration configuration = model == null || model.ConfigurationManager == null
                    ? null
                    : model.ConfigurationManager.ActiveConfiguration;
                return configuration == null ? "" : configuration.Name ?? "";
            }
            catch { return ""; }
        }

        private static bool IsUsableComponent(Component2 component)
        {
            if (component == null)
                return false;
            try
            {
                if (component.IsSuppressed() || component.IsEnvelope())
                    return false;
            }
            catch { return false; }
            return true;
        }

        private object[] GetComponentBodies(Component2 component)
        {
            if (component == null)
                return new object[0];
            try
            {
                if (activeProfile != null) activeProfile.ComponentGetBodiesCalls++;
                object bodyInfo;
                return ToObjectArray(component.GetBodies3((int)swBodyType_e.swSolidBody, out bodyInfo));
            }
            catch
            {
                try
                {
                    if (activeProfile != null) activeProfile.ComponentGetBodiesCalls++;
                    return ToObjectArray(((dynamic)component).GetBodies2((int)swBodyType_e.swSolidBody));
                }
                catch { return new object[0]; }
            }
        }

        private double[] TransformPoint(double[] point, MathTransform transform)
        {
            try
            {
                if (activeProfile != null) activeProfile.TransformCalls++;
                MathPoint mathPoint = mathUtility.CreatePoint(point) as MathPoint;
                mathPoint = mathPoint == null ? null : mathPoint.MultiplyTransform(transform) as MathPoint;
                return ToDoubleArray(mathPoint == null ? null : mathPoint.ArrayData);
            }
            catch { return null; }
        }

        private double[] TransformVector(double[] vector, MathTransform transform)
        {
            try
            {
                if (activeProfile != null) activeProfile.TransformCalls++;
                MathVector mathVector = mathUtility.CreateVector(vector) as MathVector;
                mathVector = mathVector == null ? null : mathVector.MultiplyTransform(transform) as MathVector;
                return ToDoubleArray(mathVector == null ? null : mathVector.ArrayData);
            }
            catch { return null; }
        }

        private static Sketch GetActiveSketch(ModelDoc2 model)
        {
            if (model == null)
                return null;
            try { return ((dynamic)model).GetActiveSketch2() as Sketch; }
            catch
            {
                try { return ((dynamic)model.SketchManager).ActiveSketch as Sketch; }
                catch { return null; }
            }
        }

        private static Feature GetSketchFeature(Sketch sketch)
        {
            if (sketch == null)
                return null;
            try { return ((dynamic)sketch).GetFeature() as Feature; }
            catch { return null; }
        }

        private static string SafeComponentName(Component2 component)
        {
            try { return component.Name2 ?? ""; }
            catch { return ""; }
        }

        private static string SafeComponentPath(Component2 component)
        {
            try { return component.GetPathName() ?? ""; }
            catch { return ""; }
        }

        private static string SafeBodyName(Body2 body)
        {
            try { return body == null ? "" : body.Name ?? ""; }
            catch { return ""; }
        }

        private static string ResolveBodyIdentity(Body2 body, int bodyIndex)
        {
            string name = SafeBodyName(body);
            return string.IsNullOrWhiteSpace(name)
                ? "BODY#" + bodyIndex.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        private static string SafeReferencedConfiguration(Component2 component)
        {
            try { return component == null ? "" : component.ReferencedConfiguration ?? ""; }
            catch { return ""; }
        }

        private static string SafeFeatureName(Feature feature)
        {
            try { return feature == null ? "" : feature.Name ?? ""; }
            catch { return ""; }
        }

        private static string SafeFeatureType(Feature feature)
        {
            try { return feature == null ? "" : feature.GetTypeName2() ?? ""; }
            catch { return ""; }
        }

        private static object[] ToObjectArray(object value)
        {
            if (value == null)
                return new object[0];
            object[] objects = value as object[];
            if (objects != null)
                return objects;
            Array array = value as Array;
            if (array == null)
                return new[] { value };
            object[] result = new object[array.Length];
            for (int index = 0; index < array.Length; index++)
                result[index] = array.GetValue(index);
            return result;
        }

        private static double[] ToDoubleArray(object value)
        {
            double[] doubles = value as double[];
            if (doubles != null)
                return doubles;
            Array array = value as Array;
            if (array == null)
                return new double[0];
            double[] result = new double[array.Length];
            for (int index = 0; index < array.Length; index++)
                result[index] = Convert.ToDouble(array.GetValue(index), CultureInfo.InvariantCulture);
            return result;
        }

        private static bool IsCanceled()
        {
            return (GetAsyncKeyState(Keys.Escape) & 0x8000) != 0;
        }

        private static bool ContainsNearPoint(List<double[]> points, double[] value, double tolerance)
        {
            return points.Any(point => Distance(point, value) <= tolerance);
        }

        private static void BuildPerpendicularFrame(double[] axis, out double[] u, out double[] v)
        {
            double[] helper = Math.Abs(axis[2]) < 0.8
                ? new[] { 0.0, 0.0, 1.0 }
                : new[] { 1.0, 0.0, 0.0 };
            u = Normalize(Cross(axis, helper));
            v = Normalize(Cross(axis, u));
        }

        private double[] TransformDirection(double[] direction, MathTransform transform)
        {
            if (!IsDirection(direction) || transform == null || mathUtility == null)
                return null;
            try
            {
                MathVector vector = mathUtility.CreateVector(direction) as MathVector;
                vector = vector == null ? null : vector.MultiplyTransform(transform) as MathVector;
                double[] result = ToDoubleArray(vector == null ? null : vector.ArrayData);
                return IsDirection(result) ? Normalize(result) : null;
            }
            catch
            {
                return null;
            }
        }

        private bool TryBuildViewFacingFrame(ModelDoc2 model, out double[] u, out double[] v)
        {
            u = null;
            v = null;
            if (model == null)
                return false;

            try
            {
                ModelView activeView = model.ActiveView as ModelView;
                MathTransform modelToView = activeView == null ? null : activeView.Transform;
                MathTransform viewToModel = modelToView == null ? null : modelToView.Inverse() as MathTransform;
                if (viewToModel == null)
                    return false;

                double[] horizontal = TransformDirection(new[] { 1.0, 0.0, 0.0 }, viewToModel);
                double[] vertical = TransformDirection(new[] { 0.0, 1.0, 0.0 }, viewToModel);
                if (!IsDirection(horizontal) || !IsDirection(vertical))
                    return false;

                // Loai sai so so hoc cua transform de hai truc marker vuong goc that su.
                horizontal = Normalize(horizontal);
                vertical = Subtract(vertical, Scale(horizontal, Dot(vertical, horizontal)));
                vertical = Normalize(vertical);
                if (!IsDirection(vertical))
                    return false;

                u = horizontal;
                v = vertical;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] active-view marker frame failed: " + ex.Message);
                return false;
            }
        }

        private static double[] ClosestPointOnLineToPoint(double[] linePoint, double[] direction, double[] point)
        {
            return Add(linePoint, Scale(direction, Dot(Subtract(point, linePoint), direction)));
        }

        private static double TransverseCenterDistance(HoleAxis first, HoleAxis second)
        {
            double[] secondDirection = second.Direction;
            if (Dot(first.Direction, secondDirection) < 0)
                secondDirection = Scale(secondDirection, -1.0);

            double[] commonDirection = Normalize(Add(first.Direction, secondDirection));
            if (!IsDirection(commonDirection))
                commonDirection = first.Direction;

            double[] delta = Subtract(second.Center, first.Center);
            double[] transverse = Subtract(
                delta,
                Scale(commonDirection, Dot(delta, commonDirection)));
            return Length(transverse);
        }

        private static double AxialGap(HoleAxis first, HoleAxis second)
        {
            double centerSeparation = Math.Abs(Dot(Subtract(second.Center, first.Center), first.Direction));
            return Math.Max(0, centerSeparation - first.AxialLength * 0.5 - second.AxialLength * 0.5);
        }

        /// <summary>
        /// Lay hai mieng lo gan nhau nhat cua cap lo da duoc detection xac nhan.
        /// Ham nay chi phuc vu hien thi, khong ghep cap va khong loc adjacency lan nua.
        /// </summary>
        private static bool TryGetClosestMouthPair(
            HoleAxis first,
            HoleAxis second,
            out double[] firstMouth,
            out double[] secondMouth)
        {
            firstMouth = null;
            secondMouth = null;
            if (first == null || second == null
                || !IsPoint(first.Point) || !IsPoint(second.Point)
                || !IsDirection(first.Direction) || !IsDirection(second.Direction))
                return false;

            double[][] firstEnds =
            {
                Add(first.Point, Scale(first.Direction, first.MinProjection)),
                Add(first.Point, Scale(first.Direction, first.MaxProjection))
            };
            double[][] secondEnds =
            {
                Add(second.Point, Scale(second.Direction, second.MinProjection)),
                Add(second.Point, Scale(second.Direction, second.MaxProjection))
            };

            double bestDistance = double.PositiveInfinity;
            for (int firstIndex = 0; firstIndex < firstEnds.Length; firstIndex++)
            {
                for (int secondIndex = 0; secondIndex < secondEnds.Length; secondIndex++)
                {
                    double distance = Distance(firstEnds[firstIndex], secondEnds[secondIndex]);
                    if (distance >= bestDistance)
                        continue;
                    bestDistance = distance;
                    firstMouth = firstEnds[firstIndex];
                    secondMouth = secondEnds[secondIndex];
                }
            }

            return IsPoint(firstMouth) && IsPoint(secondMouth);
        }

        private static bool TryGetFacingMouthPair(
            HoleAxis first,
            HoleAxis second,
            out double[] firstMouth,
            out double[] secondMouth)
        {
            firstMouth = null;
            secondMouth = null;
            if (first == null || second == null
                || !IsPoint(first.Point) || !IsPoint(second.Point)
                || !IsDirection(first.Direction) || !IsDirection(second.Direction)
                || AxisAngleDeg(first.Direction, second.Direction) > SearchAngleToleranceDeg)
                return false;

            double faceGapM;
            if (!AreHoleLayersAdjacent(first, second, out faceGapM))
                return false;

            double[][] firstEnds =
            {
                Add(first.Point, Scale(first.Direction, first.MinProjection)),
                Add(first.Point, Scale(first.Direction, first.MaxProjection))
            };
            double[][] secondEnds =
            {
                Add(second.Point, Scale(second.Direction, second.MinProjection)),
                Add(second.Point, Scale(second.Direction, second.MaxProjection))
            };

            double bestDistance = double.PositiveInfinity;
            for (int firstIndex = 0; firstIndex < firstEnds.Length; firstIndex++)
            {
                for (int secondIndex = 0; secondIndex < secondEnds.Length; secondIndex++)
                {
                    double distance = Distance(firstEnds[firstIndex], secondEnds[secondIndex]);
                    if (distance >= bestDistance)
                        continue;
                    bestDistance = distance;
                    firstMouth = firstEnds[firstIndex];
                    secondMouth = secondEnds[secondIndex];
                }
            }

            return IsPoint(firstMouth)
                && IsPoint(secondMouth)
                && bestDistance <= AdjacentFaceGapM + SearchPositionToleranceM;
        }

        private static bool TryIntersectAxisWithPlane(
            double[] axisPoint,
            double[] axisDirection,
            double[] planePoint,
            double[] planeNormal,
            out double[] intersection)
        {
            intersection = null;
            if (!IsPoint(axisPoint) || !IsPoint(planePoint)
                || !IsDirection(axisDirection) || !IsDirection(planeNormal))
                return false;

            double[] direction = Normalize(axisDirection);
            double[] normal = Normalize(planeNormal);
            double denominator = Dot(direction, normal);
            if (Math.Abs(denominator) <= 1e-8)
                return false;

            double parameter = Dot(Subtract(planePoint, axisPoint), normal) / denominator;
            intersection = Add(axisPoint, Scale(direction, parameter));
            return IsPoint(intersection);
        }

        private static double[] ProjectPointToPlaneAlongDirection(
            double[] point,
            double[] planePoint,
            double[] planeNormal)
        {
            if (!IsPoint(point) || !IsPoint(planePoint) || !IsDirection(planeNormal))
                return null;
            double[] normal = Normalize(planeNormal);
            double signedDistance = Dot(Subtract(point, planePoint), normal);
            return Subtract(point, Scale(normal, signedDistance));
        }

        /// <summary>
        /// Hai lo chi duoc phep so sanh khi khoang trong giua hai doan tru theo
        /// truc khoan nam trong gioi han mat ke. Doan tru chinh la chieu day vat
        /// lieu thuc te, vi vay khong can doan be day tu ten vat lieu.
        /// </summary>
        private static bool AreHoleLayersAdjacent(
            HoleAxis first,
            HoleAxis second,
            out double faceGapM)
        {
            faceGapM = double.PositiveInfinity;
            if (first == null || second == null)
                return false;

            faceGapM = AxialGap(first, second);
            return !double.IsNaN(faceGapM)
                && !double.IsInfinity(faceGapM)
                && faceGapM <= AdjacentFaceGapM;
        }

        private static double AxisAngleDeg(double[] first, double[] second)
        {
            double value = Math.Max(-1.0, Math.Min(1.0, Math.Abs(Dot(first, second))));
            return Math.Acos(value) * 180.0 / Math.PI;
        }

        private static bool IsSearchAngleCompatible(HoleAxis first, HoleAxis second)
        {
            return IsSearchAngleCompatible(
                first,
                second,
                AxisAngleDeg(first.Direction, second.Direction));
        }

        private static bool IsSearchAngleCompatible(
            HoleAxis first,
            HoleAxis second,
            double angleDeg)
        {
            double limit;
            if (first != null && second != null
                && (first.Source == HoleSource.RecoveredDeformed
                    || second.Source == HoleSource.RecoveredDeformed))
                limit = RecoveredDeformedSearchAngleToleranceDeg;
            else if (first != null && second != null
                && (first.IsCurvedBoundaryFallback || second.IsCurvedBoundaryFallback))
                limit = CurvedHoleSearchAngleToleranceDeg;
            else
                limit = SearchAngleToleranceDeg;
            return angleDeg <= limit;
        }

        private static void CanonicalizeDirection(double[] direction)
        {
            int index = 0;
            if (Math.Abs(direction[1]) > Math.Abs(direction[index]))
                index = 1;
            if (Math.Abs(direction[2]) > Math.Abs(direction[index]))
                index = 2;
            if (direction[index] < 0)
            {
                direction[0] = -direction[0];
                direction[1] = -direction[1];
                direction[2] = -direction[2];
            }
        }

        private static double[] Add(double[] first, double[] second)
        {
            return new[] { first[0] + second[0], first[1] + second[1], first[2] + second[2] };
        }

        private static double[] ClonePoint(double[] value)
        {
            return value == null || value.Length < 3
                ? null
                : new[] { value[0], value[1], value[2] };
        }

        private static double[] Subtract(double[] first, double[] second)
        {
            return new[] { first[0] - second[0], first[1] - second[1], first[2] - second[2] };
        }

        private static double[] Scale(double[] vector, double scale)
        {
            return new[] { vector[0] * scale, vector[1] * scale, vector[2] * scale };
        }

        private static double Dot(double[] first, double[] second)
        {
            return first[0] * second[0] + first[1] * second[1] + first[2] * second[2];
        }

        private static double[] Cross(double[] first, double[] second)
        {
            return new[]
            {
                first[1] * second[2] - first[2] * second[1],
                first[2] * second[0] - first[0] * second[2],
                first[0] * second[1] - first[1] * second[0]
            };
        }

        private static double Length(double[] vector)
        {
            return Math.Sqrt(Dot(vector, vector));
        }

        private static double Distance(double[] first, double[] second)
        {
            return Length(Subtract(first, second));
        }

        private static double[] Normalize(double[] vector)
        {
            if (vector == null || vector.Length < 3)
                return null;
            double length = Length(vector);
            if (length <= 1e-12)
                return null;
            return Scale(vector, 1.0 / length);
        }

        private static bool IsPoint(double[] value)
        {
            return value != null && value.Length >= 3
                && !double.IsNaN(value[0]) && !double.IsInfinity(value[0])
                && !double.IsNaN(value[1]) && !double.IsInfinity(value[1])
                && !double.IsNaN(value[2]) && !double.IsInfinity(value[2]);
        }

        private static bool IsDirection(double[] value)
        {
            return IsPoint(value) && Length(value) > 1e-10;
        }

        private static string FormatMm(double valueM)
        {
            return (valueM * 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Detector doc topology cho lo meo tren mat cong/xoan. Lop nay khong tao
        /// feature, khong rebuild, khong save; production chi dung record da tinh san.
        /// </summary>
        private sealed class DeformedHoleDebugDetector
        {
            private const string Prefix = "[CHECK HOLE ASSY] DEFORMED_HOLE";
            private const string ValidatePrefix = "[CHECK HOLE ASSY] DEFORMED_VALIDATE";
            private readonly CheckAssemblyHole owner;
            private readonly Dictionary<string, DeformedBodyCache> bodyCache =
                new Dictionary<string, DeformedBodyCache>(StringComparer.OrdinalIgnoreCase);
            private readonly List<DeformedRecoveredHole> recoveredHoles =
                new List<DeformedRecoveredHole>();
            private readonly Stopwatch totalWatch = Stopwatch.StartNew();
            private int occurrenceCount;
            private int uniqueBodyCount;
            private int cacheHitCount;
            private int faceCount;
            private int innerLoopCount;
            private int cheapRejectedCount;
            private int geometryRejectedCount;
            private int candidateCount;
            private int duplicateCylinderCount;
            private int pairedCount;
            private int singleCount;
            private long detectionElapsedMilliseconds;

            public long DetectionElapsedMilliseconds
            {
                get { return detectionElapsedMilliseconds; }
            }

            public DeformedHoleDebugDetector(CheckAssemblyHole owner)
            {
                this.owner = owner;
            }

            public void ProcessBodyOccurrence(
                Body2 body,
                object[] faceObjects,
                int bodyIndex,
                MathTransform transform,
                string occurrence,
                string path,
                string configuration,
                IList<HoleAxis> existingCylinders)
            {
                if (body == null || transform == null || owner == null || IsCanceled())
                    return;

                occurrenceCount++;
                string bodyName = ResolveBodyIdentity(body, bodyIndex);
                string cacheKey = (path ?? "") + "|" + (configuration ?? "") + "|"
                    + bodyIndex.ToString(CultureInfo.InvariantCulture) + "|" + bodyName;
                DeformedBodyCache cache;
                bool cacheHit = bodyCache.TryGetValue(cacheKey, out cache);
                Stopwatch bodyWatch = Stopwatch.StartNew();
                if (!cacheHit)
                {
                    cache = ScanUniqueBody(faceObjects, cacheKey, bodyName, occurrence);
                    bodyCache[cacheKey] = cache;
                    uniqueBodyCount++;
                }
                else
                {
                    cacheHitCount++;
                }

                List<DeformedWorldCandidate> visible = new List<DeformedWorldCandidate>();
                foreach (DeformedLoopCandidate local in cache.Candidates)
                {
                    double[] center = owner.TransformPoint(local.Center, transform);
                    double[] normal = owner.TransformVector(local.Normal, transform);
                    normal = IsDirection(normal) ? Normalize(normal) : null;
                    if (!IsPoint(center) || !IsDirection(normal))
                        continue;

                    bool duplicateCylinder = IsDuplicateOfExistingCylinder(
                        center,
                        normal,
                        local,
                        existingCylinders);
                    if (duplicateCylinder)
                    {
                        duplicateCylinderCount++;
                        if (VerboseHoleDebug)
                            Debug.WriteLine(Prefix + " CANDIDATE component=" + occurrence
                                + ", body=" + bodyName
                                + ", loop=" + local.LoopId
                                + ", status=DUPLICATE_CYLINDER"
                                + ", center=" + FormatPointMm(center)
                                + ", perimeter=" + FormatMm(local.PerimeterM) + "mm");
                        continue;
                    }

                    visible.Add(new DeformedWorldCandidate
                    {
                        Local = local,
                        Center = center,
                        Normal = normal
                    });
                    candidateCount++;
                    if (VerboseHoleDebug)
                        Debug.WriteLine(Prefix + " CANDIDATE component=" + occurrence
                            + ", body=" + bodyName
                            + ", loop=" + local.LoopId
                            + ", status=" + local.Status
                            + ", confidence=" + local.Confidence
                            + ", center=" + FormatPointMm(center)
                            + ", perimeter=" + FormatMm(local.PerimeterM) + "mm"
                            + ", area=" + (local.AreaM2 * 1000000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm2"
                            + ", planarity=" + FormatMm(local.PlanarityErrorM) + "mm"
                            + ", samples=" + local.SampleCount
                            + ", cache=" + (cacheHit ? "HIT" : "MISS"));
                }

                PairAndLog(visible, occurrence, path, bodyName);
                bodyWatch.Stop();
                detectionElapsedMilliseconds += bodyWatch.ElapsedMilliseconds;
                Debug.WriteLine(Prefix + " BODY_SUMMARY component=" + occurrence
                    + ", path=" + path
                    + ", configuration=" + configuration
                    + ", body=" + bodyName
                    + ", faces=" + cache.FaceCount
                    + ", innerLoops=" + cache.InnerLoopCount
                    + ", analyzed=" + cache.Candidates.Count
                    + ", visible=" + visible.Count
                    + ", cache=" + (cacheHit ? "HIT" : "MISS")
                    + ", elapsedMs=" + bodyWatch.ElapsedMilliseconds);
            }

            public void LogRunSummary()
            {
                totalWatch.Stop();
                Debug.WriteLine(Prefix + " RUN_SUMMARY occurrences=" + occurrenceCount
                    + ", uniqueBodies=" + uniqueBodyCount
                    + ", cacheHits=" + cacheHitCount
                    + ", faces=" + faceCount
                    + ", innerLoops=" + innerLoopCount
                    + ", cheapRejected=" + cheapRejectedCount
                    + ", geometryRejected=" + geometryRejectedCount
                    + ", candidates=" + candidateCount
                    + ", duplicateCylinder=" + duplicateCylinderCount
                    + ", paired=" + pairedCount
                    + ", single=" + singleCount
                    + ", elapsedMs=" + totalWatch.ElapsedMilliseconds
                    + ", mode=RECOVERY_RECORDS_READY");
            }

            public void InjectRecoveredDeformedHoles(IList<HoleAxis> logicalHoles)
            {
                Stopwatch watch = Stopwatch.StartNew();
                int injected = 0;
                int duplicateSkipped = 0;
                int rejected = 0;
                if (logicalHoles == null)
                {
                    Debug.WriteLine("[CHECK HOLE ASSY] DEFORMED_INJECT SUMMARY recoveredPaired="
                        + recoveredHoles.Count + ", injected=0, duplicateSkipped=0, rejected="
                        + recoveredHoles.Count + ", elapsedMs=0");
                    return;
                }

                foreach (DeformedRecoveredHole recovered in recoveredHoles)
                {
                    if (recovered == null || recovered.IsAmbiguous)
                    {
                        rejected++;
                        LogInjection(recovered, "REJECTED", recovered == null
                            ? "NULL_RECORD"
                            : "AMBIGUOUS_PAIR");
                        continue;
                    }

                    HoleAxis productionHole;
                    string conversionReason;
                    if (!TryConvertRecoveredHole(recovered, out productionHole, out conversionReason))
                    {
                        rejected++;
                        LogInjection(recovered, "REJECTED", conversionReason);
                        continue;
                    }

                    HoleAxis duplicate = logicalHoles.FirstOrDefault(item =>
                        IsRecoveredDuplicateOfKnownHole(productionHole, item));
                    if (duplicate != null)
                    {
                        duplicateSkipped++;
                        LogInjection(recovered, "DUPLICATE", "ANALYTIC_PRIORITY:"
                            + GetHoleSourceName(duplicate));
                        continue;
                    }

                    logicalHoles.Add(productionHole);
                    injected++;
                    LogInjection(recovered, "INJECTED", "VALID_PAIRED_MOUTHS");
                }

                watch.Stop();
                Debug.WriteLine("[CHECK HOLE ASSY] DEFORMED_INJECT SUMMARY recoveredPaired="
                    + recoveredHoles.Count
                    + ", injected=" + injected
                    + ", duplicateSkipped=" + duplicateSkipped
                    + ", rejected=" + rejected
                    + ", elapsedMs=" + watch.ElapsedMilliseconds);
            }

            private static bool TryConvertRecoveredHole(
                DeformedRecoveredHole recovered,
                out HoleAxis hole,
                out string reason)
            {
                hole = null;
                reason = "INVALID_GEOMETRY";
                if (recovered == null
                    || !IsPoint(recovered.MouthCenterA)
                    || !IsPoint(recovered.MouthCenterB)
                    || !IsPoint(recovered.RecoveredCenter)
                    || !IsDirection(recovered.RecoveredDirection)
                    || recovered.ApproximateDiameterM <= 1e-8)
                    return false;

                double[] direction = Normalize(recovered.RecoveredDirection);
                CanonicalizeDirection(direction);
                double[] center = ClonePoint(recovered.RecoveredCenter);
                double[] point = Subtract(center, Scale(direction, Dot(center, direction)));
                double firstProjection = Dot(recovered.MouthCenterA, direction);
                double secondProjection = Dot(recovered.MouthCenterB, direction);
                double minProjection = Math.Min(firstProjection, secondProjection);
                double maxProjection = Math.Max(firstProjection, secondProjection);
                if (maxProjection - minProjection <= 1e-8)
                {
                    reason = "ZERO_PROJECTED_MOUTH_GAP";
                    return false;
                }

                hole = new HoleAxis
                {
                    ComponentOccurrence = recovered.ComponentOccurrence,
                    ComponentPath = recovered.ComponentPath,
                    BodyName = recovered.BodyName,
                    Source = HoleSource.RecoveredDeformed,
                    RecoveredLoopIdA = recovered.LoopIdA,
                    RecoveredLoopIdB = recovered.LoopIdB,
                    Point = point,
                    Direction = direction,
                    Center = center,
                    MinProjection = minProjection,
                    MaxProjection = maxProjection,
                    RadiusM = recovered.ApproximateDiameterM * 0.5,
                    SourceFaceCount = 2,
                    SweepRad = Math.PI * 2.0
                };
                reason = "OK";
                return true;
            }

            private static bool IsRecoveredDuplicateOfKnownHole(
                HoleAxis recovered,
                HoleAxis known)
            {
                if (recovered == null || known == null
                    || known.Source == HoleSource.RecoveredDeformed
                    || !string.Equals(recovered.ComponentOccurrence,
                        known.ComponentOccurrence,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.IsNullOrWhiteSpace(recovered.BodyName)
                    && !string.IsNullOrWhiteSpace(known.BodyName)
                    && !string.Equals(recovered.BodyName, known.BodyName,
                        StringComparison.OrdinalIgnoreCase))
                    return false;
                if (AxisAngleDeg(recovered.Direction, known.Direction)
                    > RecoveredDuplicateAngleToleranceDeg)
                    return false;
                if (TransverseCenterDistance(recovered, known)
                    > RecoveredDuplicatePositionToleranceM)
                    return false;
                if (AxialGap(recovered, known) > SameHoleAxialGapM)
                    return false;

                if (known.IsSlot || known.Source == HoleSource.Slot)
                    return true;
                double diameterDifferenceFraction = Math.Abs(
                    recovered.RadiusM - known.RadiusM)
                    / Math.Max(recovered.RadiusM, known.RadiusM);
                return diameterDifferenceFraction <= RecoveredDuplicateDiameterFraction;
            }

            private static void LogInjection(
                DeformedRecoveredHole recovered,
                string result,
                string reason)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] DEFORMED_INJECT"
                    + " component=" + (recovered == null ? "N/A" : recovered.ComponentOccurrence)
                    + ", body=" + (recovered == null ? "N/A" : recovered.BodyName)
                    + ", loops=" + (recovered == null ? "N/A" : recovered.LoopIdA + "<->" + recovered.LoopIdB)
                    + ", center=" + (recovered == null ? "N/A" : FormatPointMm(recovered.RecoveredCenter))
                    + ", direction=" + (recovered == null ? "N/A" : FormatDirection(recovered.RecoveredDirection))
                    + ", equivalentDiameter=" + (recovered == null ? "N/A" : FormatMm(recovered.ApproximateDiameterM) + "mm")
                    + ", source=RecoveredDeformed"
                    + ", result=" + result
                    + ", reason=" + reason);
            }

            public void DebugValidateRecoveredDeformedHoles(IList<HoleAxis> logicalHoles)
            {
                Stopwatch watch = Stopwatch.StartNew();
                List<DeformedValidationMatch> accepted = new List<DeformedValidationMatch>();
                int ambiguousCount = 0;
                int noReferenceCount = 0;
                IList<HoleAxis> trusted = (logicalHoles ?? new List<HoleAxis>())
                    .Where(item => item != null
                        && !item.IsSlot
                        && IsPoint(item.Center)
                        && IsDirection(item.Direction)
                        && item.RadiusM > 1e-8)
                    .ToList();

                Dictionary<DeformedRecoveredHole, List<DeformedValidationMatch>> candidateMap =
                    new Dictionary<DeformedRecoveredHole, List<DeformedValidationMatch>>();
                foreach (DeformedRecoveredHole recovered in recoveredHoles)
                {
                    List<DeformedValidationMatch> candidates = trusted
                        .Where(reference => !string.Equals(
                            reference.ComponentOccurrence,
                            recovered.ComponentOccurrence,
                            StringComparison.OrdinalIgnoreCase))
                        .Select(reference => BuildValidationCandidate(recovered, reference))
                        .Where(item => item != null)
                        .OrderBy(item => item.Score)
                        .ToList();
                    candidateMap[recovered] = candidates;
                }

                Dictionary<HoleAxis, DeformedRecoveredHole> reverseBest =
                    new Dictionary<HoleAxis, DeformedRecoveredHole>();
                foreach (HoleAxis reference in trusted)
                {
                    DeformedValidationMatch bestForReference = candidateMap.Values
                        .SelectMany(items => items)
                        .Where(item => ReferenceEquals(item.Reference, reference))
                        .OrderBy(item => item.Score)
                        .FirstOrDefault();
                    if (bestForReference != null)
                        reverseBest[reference] = bestForReference.Recovered;
                }

                foreach (DeformedRecoveredHole recovered in recoveredHoles)
                {
                    List<DeformedValidationMatch> candidates = candidateMap[recovered];
                    if (candidates.Count == 0)
                    {
                        noReferenceCount++;
                        LogNoReference(recovered, "NO_PLAUSIBLE_TRUSTED_ROUND_CYLINDER");
                        continue;
                    }

                    DeformedValidationMatch best = candidates[0];
                    DeformedValidationMatch second = candidates.Count > 1 ? candidates[1] : null;
                    best.SecondBestScore = second == null ? double.PositiveInfinity : second.Score;
                    bool similarScore = second != null
                        && second.Score <= best.Score * DeformedValidationAmbiguousScoreRatio;
                    DeformedRecoveredHole reverse;
                    bool mutualBest = reverseBest.TryGetValue(best.Reference, out reverse)
                        && ReferenceEquals(reverse, recovered);
                    if (similarScore || !mutualBest)
                    {
                        ambiguousCount++;
                        LogAmbiguous(best, second, mutualBest);
                        continue;
                    }

                    accepted.Add(best);
                    LogValidationMatch(best);
                }

                watch.Stop();
                LogValidationSummary(
                    recoveredHoles.Count,
                    accepted,
                    ambiguousCount,
                    noReferenceCount,
                    watch.ElapsedMilliseconds);
            }

            private static DeformedValidationMatch BuildValidationCandidate(
                DeformedRecoveredHole recovered,
                HoleAxis reference)
            {
                if (recovered == null || reference == null)
                    return null;

                double angle = AxisAngleDeg(recovered.RecoveredDirection, reference.Direction);
                if (angle > DeformedValidationSearchAngleDeg)
                    return null;

                double[] delta = Subtract(recovered.RecoveredCenter, reference.Center);
                double axial = Dot(delta, reference.Direction);
                double[] transverseVector = Subtract(delta, Scale(reference.Direction, axial));
                double transverse = Length(transverseVector);
                if (transverse > DeformedValidationSearchPositionM)
                    return null;

                double axialSeparation = Math.Abs(axial);
                double faceGap = Math.Max(
                    0,
                    axialSeparation
                    - recovered.MouthGapM * 0.5
                    - reference.AxialLength * 0.5);
                if (faceGap > AdjacentFaceGapM)
                    return null;

                double referenceDiameter = reference.RadiusM * 2.0;
                double diameterDifference = Math.Abs(
                    recovered.ApproximateDiameterM - referenceDiameter);
                double diameterDifferenceFraction = diameterDifference
                    / Math.Max(recovered.ApproximateDiameterM, referenceDiameter);
                if (diameterDifferenceFraction > DeformedValidationMaximumDiameterDifferenceFraction)
                    return null;

                // Vi tri ngang la thanh phan quan trong nhat. Cac thanh phan con lai
                // chi giup chon dung lo trong hang pattern lap lai.
                double score = 4.0 * transverse / DeformedValidationSearchPositionM
                    + faceGap / Math.Max(AdjacentFaceGapM, 1e-12)
                    + diameterDifferenceFraction
                    + 0.5 * angle / DeformedValidationSearchAngleDeg;
                return new DeformedValidationMatch
                {
                    Recovered = recovered,
                    Reference = reference,
                    TransverseOffsetM = transverse,
                    AxialSeparationM = axialSeparation,
                    FaceGapM = faceGap,
                    AngleDifferenceDeg = angle,
                    ReferenceDiameterM = referenceDiameter,
                    DiameterDifferenceM = diameterDifference,
                    DiameterDifferencePercent = diameterDifferenceFraction * 100.0,
                    Score = score
                };
            }

            private static void LogValidationMatch(DeformedValidationMatch match)
            {
                DeformedRecoveredHole recovered = match.Recovered;
                HoleAxis reference = match.Reference;
                Debug.WriteLine(ValidatePrefix + " MATCH");
                Debug.WriteLine(ValidatePrefix + " deformedComponent=" + recovered.ComponentOccurrence);
                Debug.WriteLine(ValidatePrefix + " referenceComponent=" + reference.ComponentOccurrence);
                Debug.WriteLine(ValidatePrefix + " body=" + recovered.BodyName);
                Debug.WriteLine(ValidatePrefix + " loops=" + recovered.LoopIdA + "<->" + recovered.LoopIdB);
                Debug.WriteLine(ValidatePrefix + " recoveredCenter=" + FormatPointMm(recovered.RecoveredCenter));
                Debug.WriteLine(ValidatePrefix + " referenceCenter=" + FormatPointMm(reference.Center));
                Debug.WriteLine(ValidatePrefix + " recoveredDirection=" + FormatDirection(recovered.RecoveredDirection));
                Debug.WriteLine(ValidatePrefix + " referenceDirection=" + FormatDirection(reference.Direction));
                Debug.WriteLine(ValidatePrefix + " diameterRecovered=" + FormatMm(recovered.ApproximateDiameterM) + "mm");
                Debug.WriteLine(ValidatePrefix + " diameterReference=" + FormatMm(match.ReferenceDiameterM) + "mm");
                Debug.WriteLine(ValidatePrefix + " diameterDifference=" + FormatMm(match.DiameterDifferenceM) + "mm");
                Debug.WriteLine(ValidatePrefix + " diameterDifferencePercent="
                    + match.DiameterDifferencePercent.ToString("0.###", CultureInfo.InvariantCulture) + "%");
                Debug.WriteLine(ValidatePrefix + " mouthGap=" + FormatMm(recovered.MouthGapM) + "mm");
                Debug.WriteLine(ValidatePrefix + " axialSeparation=" + FormatMm(match.AxialSeparationM) + "mm");
                Debug.WriteLine(ValidatePrefix + " faceGap=" + FormatMm(match.FaceGapM) + "mm");
                Debug.WriteLine(ValidatePrefix + " transverseOffset=" + FormatMm(match.TransverseOffsetM) + "mm");
                Debug.WriteLine(ValidatePrefix + " angleDifference="
                    + match.AngleDifferenceDeg.ToString("0.###", CultureInfo.InvariantCulture) + "deg");
                Debug.WriteLine(ValidatePrefix + " bestScore="
                    + match.Score.ToString("0.######", CultureInfo.InvariantCulture));
                Debug.WriteLine(ValidatePrefix + " secondBestScore="
                    + FormatValidationScore(match.SecondBestScore));
                Debug.WriteLine(ValidatePrefix + " within0.10mm="
                    + (match.TransverseOffsetM <= PositionToleranceM).ToString().ToLowerInvariant());
                Debug.WriteLine(ValidatePrefix + " status=VALIDATE_ONLY");
            }

            private static void LogAmbiguous(
                DeformedValidationMatch best,
                DeformedValidationMatch second,
                bool mutualBest)
            {
                Debug.WriteLine(ValidatePrefix + " AMBIGUOUS");
                Debug.WriteLine(ValidatePrefix + " deformedComponent=" + best.Recovered.ComponentOccurrence);
                Debug.WriteLine(ValidatePrefix + " center=" + FormatPointMm(best.Recovered.RecoveredCenter));
                Debug.WriteLine(ValidatePrefix + " bestReference=" + best.Reference.ComponentOccurrence);
                Debug.WriteLine(ValidatePrefix + " bestOffset=" + FormatMm(best.TransverseOffsetM) + "mm");
                Debug.WriteLine(ValidatePrefix + " secondReference="
                    + (second == null ? "NONE" : second.Reference.ComponentOccurrence));
                Debug.WriteLine(ValidatePrefix + " secondOffset="
                    + (second == null ? "N/A" : FormatMm(second.TransverseOffsetM) + "mm"));
                Debug.WriteLine(ValidatePrefix + " mutualBest=" + mutualBest.ToString().ToLowerInvariant());
            }

            private static void LogNoReference(DeformedRecoveredHole recovered, string reason)
            {
                Debug.WriteLine(ValidatePrefix + " NO_REFERENCE");
                Debug.WriteLine(ValidatePrefix + " component=" + recovered.ComponentOccurrence);
                Debug.WriteLine(ValidatePrefix + " center=" + FormatPointMm(recovered.RecoveredCenter));
                Debug.WriteLine(ValidatePrefix + " diameter=" + FormatMm(recovered.ApproximateDiameterM) + "mm");
                Debug.WriteLine(ValidatePrefix + " reason=" + reason);
            }

            private static void LogValidationSummary(
                int recoveredCount,
                List<DeformedValidationMatch> matches,
                int ambiguousCount,
                int noReferenceCount,
                long elapsedMs)
            {
                List<double> offsets = matches.Select(item => item.TransverseOffsetM).ToList();
                double minimum = offsets.Count == 0 ? double.NaN : offsets.Min();
                double mean = offsets.Count == 0 ? double.NaN : offsets.Average();
                double rms = offsets.Count == 0
                    ? double.NaN
                    : Math.Sqrt(offsets.Average(item => item * item));
                double maximum = offsets.Count == 0 ? double.NaN : offsets.Max();
                Debug.WriteLine(ValidatePrefix + " SUMMARY");
                Debug.WriteLine(ValidatePrefix + " recoveredHoles=" + recoveredCount);
                Debug.WriteLine(ValidatePrefix + " matchedToTrustedCylinder=" + matches.Count);
                Debug.WriteLine(ValidatePrefix + " ambiguous=" + ambiguousCount);
                Debug.WriteLine(ValidatePrefix + " noReference=" + noReferenceCount);
                Debug.WriteLine(ValidatePrefix + " offsetMin=" + FormatOptionalMm(minimum));
                Debug.WriteLine(ValidatePrefix + " offsetMean=" + FormatOptionalMm(mean));
                Debug.WriteLine(ValidatePrefix + " offsetRms=" + FormatOptionalMm(rms));
                Debug.WriteLine(ValidatePrefix + " offsetMax=" + FormatOptionalMm(maximum));
                Debug.WriteLine(ValidatePrefix + " within0.05mm="
                    + offsets.Count(item => item <= 0.00005));
                Debug.WriteLine(ValidatePrefix + " within0.10mm="
                    + offsets.Count(item => item <= PositionToleranceM));
                Debug.WriteLine(ValidatePrefix + " over0.10mm="
                    + offsets.Count(item => item > PositionToleranceM));
                Debug.WriteLine(ValidatePrefix + " mode=VALIDATE_ONLY_NO_RESULT_INJECTION");
                Debug.WriteLine(ValidatePrefix + " elapsedMs=" + elapsedMs);
            }

            private static string FormatOptionalMm(double value)
            {
                return double.IsNaN(value) || double.IsInfinity(value)
                    ? "N/A"
                    : FormatMm(value) + "mm";
            }

            private static string FormatValidationScore(double value)
            {
                return double.IsNaN(value) || double.IsInfinity(value)
                    ? "N/A"
                    : value.ToString("0.######", CultureInfo.InvariantCulture);
            }

            private static string FormatDirection(double[] direction)
            {
                if (!IsDirection(direction))
                    return "(invalid)";
                return "("
                    + direction[0].ToString("0.######", CultureInfo.InvariantCulture) + ","
                    + direction[1].ToString("0.######", CultureInfo.InvariantCulture) + ","
                    + direction[2].ToString("0.######", CultureInfo.InvariantCulture) + ")";
            }

            private DeformedBodyCache ScanUniqueBody(
                object[] faceObjects,
                string cacheKey,
                string bodyName,
                string occurrence)
            {
                Stopwatch watch = Stopwatch.StartNew();
                DeformedBodyCache cache = new DeformedBodyCache { CacheKey = cacheKey };
                HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
                int loopSerial = 0;
                foreach (object faceObject in faceObjects ?? new object[0])
                {
                    if (IsCanceled())
                        break;
                    Face2 face = faceObject as Face2;
                    if (face == null)
                        continue;
                    cache.FaceCount++;
                    faceCount++;

                    object[] loops;
                    try { loops = ToObjectArray(face.GetLoops()); }
                    catch { continue; }
                    foreach (object loopObject in loops)
                    {
                        Loop2 loop = loopObject as Loop2;
                        if (loop == null || SafeIsOuter(loop) || SafeIsSingular(loop))
                            continue;
                        loopSerial++;
                        cache.InnerLoopCount++;
                        innerLoopCount++;

                        DeformedCheapLoop cheap;
                        string rejectReason;
                        if (!TryBuildCheapLoop(
                            loop,
                            occurrenceForDebug: occurrence,
                            loopId: loopSerial,
                            cheap: out cheap,
                            reason: out rejectReason))
                        {
                            cheapRejectedCount++;
                            Debug.WriteLine(Prefix + " REJECT body=" + bodyName
                                + ", loop=" + loopSerial
                                + ", phase=CHEAP, reason=" + rejectReason);
                            continue;
                        }

                        DeformedLoopCandidate candidate;
                        if (!TryAnalyzeLoop(face, cheap, loopSerial, out candidate, out rejectReason))
                        {
                            geometryRejectedCount++;
                            Debug.WriteLine(Prefix + " REJECT body=" + bodyName
                                + ", loop=" + loopSerial
                                + ", phase=GEOMETRY, reason=" + rejectReason);
                            continue;
                        }

                        string signature = BuildCandidateSignature(candidate);
                        if (!signatures.Add(signature))
                        {
                            cheapRejectedCount++;
                            Debug.WriteLine(Prefix + " REJECT body=" + bodyName
                                + ", loop=" + loopSerial
                                + ", phase=DEDUP, reason=DUPLICATE_LOOP_SIGNATURE");
                            continue;
                        }
                        cache.Candidates.Add(candidate);
                    }
                }
                watch.Stop();
                Debug.WriteLine(Prefix + " CACHE_BUILD key=" + cacheKey
                    + ", faces=" + cache.FaceCount
                    + ", innerLoops=" + cache.InnerLoopCount
                    + ", candidates=" + cache.Candidates.Count
                    + ", elapsedMs=" + watch.ElapsedMilliseconds);
                return cache;
            }

            private bool TryBuildCheapLoop(
                Loop2 loop,
                string occurrenceForDebug,
                int loopId,
                out DeformedCheapLoop cheap,
                out string reason)
            {
                cheap = null;
                reason = "UNKNOWN";
                object[] coedgeObjects;
                try { coedgeObjects = ToObjectArray(loop.GetCoEdges()); }
                catch
                {
                    reason = "GET_COEDGES_FAILED";
                    LogCheapLoopDebug(
                        occurrenceForDebug, loopId, 0, 0, false,
                        0, 0, 0, 0, false, reason);
                    return false;
                }
                if (coedgeObjects.Length == 0 || coedgeObjects.Length > 256)
                {
                    reason = "EDGE_COUNT_OUT_OF_RANGE:" + coedgeObjects.Length;
                    LogCheapLoopDebug(
                        occurrenceForDebug, loopId, coedgeObjects.Length, 0, false,
                        0, 0, 0, 0, false, reason);
                    return false;
                }

                List<DeformedEdgeSegment> segments = new List<DeformedEdgeSegment>();
                List<double[]> roughPoints = new List<double[]>();
                double totalLength = 0;
                double chordLength = 0;
                double totalAnalyticLength = 0;
                double totalFallbackLength = 0;
                bool closedSingleEdge = false;
                foreach (object coedgeObject in coedgeObjects)
                {
                    CoEdge coedge = coedgeObject as CoEdge;
                    Edge edge = coedge == null ? null : coedge.GetEdge() as Edge;
                    Curve curve = edge == null ? null : edge.GetCurve() as Curve;
                    CurveParamData parameter = edge == null ? null : edge.GetCurveParams3();
                    if (edge == null || curve == null || parameter == null)
                    {
                        reason = "EDGE_GEOMETRY_UNAVAILABLE";
                        LogCheapLoopDebug(
                            occurrenceForDebug, loopId, coedgeObjects.Length, segments.Count,
                            closedSingleEdge, totalAnalyticLength, totalFallbackLength,
                            roughPoints.Count, ReadMaximumSize(roughPoints), false, reason);
                        return false;
                    }

                    double u0 = parameter.UMinValue;
                    double u1 = parameter.UMaxValue;
                    bool coedgeSense = true;
                    try { coedgeSense = coedge.GetSense(); } catch { }
                    if (!coedgeSense)
                    {
                        double swap = u0;
                        u0 = u1;
                        u1 = swap;
                    }
                    double[] start = EvaluateEdge(edge, u0);
                    double[] end = EvaluateEdge(edge, u1);
                    if (!IsPoint(start) || !IsPoint(end))
                    {
                        reason = "EDGE_ENDPOINT_EVALUATION_FAILED";
                        LogCheapLoopDebug(
                            occurrenceForDebug, loopId, coedgeObjects.Length, segments.Count,
                            closedSingleEdge, totalAnalyticLength, totalFallbackLength,
                            roughPoints.Count, ReadMaximumSize(roughPoints), false, reason);
                        return false;
                    }

                    bool edgeIsClosed = Distance(start, end) <= 1e-9;
                    if (coedgeObjects.Length == 1 && edgeIsClosed)
                        closedSingleEdge = true;

                    double analyticLength = double.NaN;
                    try
                    {
                        analyticLength = Math.Abs(curve.GetLength3(
                            parameter.UMinValue,
                            parameter.UMaxValue));
                    }
                    catch { }
                    bool analyticLengthValid = IsPositiveFinite(analyticLength);
                    if (analyticLengthValid)
                        totalAnalyticLength += analyticLength;

                    List<double[]> edgeRoughPoints;
                    double fallbackLength;
                    if (!TrySampleEdgeForCheapPass(
                        edge,
                        u0,
                        u1,
                        edgeIsClosed ? 8 : 4,
                        out edgeRoughPoints,
                        out fallbackLength))
                    {
                        reason = "EDGE_FALLBACK_SAMPLING_FAILED";
                        LogCheapLoopDebug(
                            occurrenceForDebug, loopId, coedgeObjects.Length, segments.Count,
                            closedSingleEdge, totalAnalyticLength, totalFallbackLength,
                            roughPoints.Count, ReadMaximumSize(roughPoints), false, reason);
                        return false;
                    }
                    totalFallbackLength += fallbackLength;
                    AddDistinctRoughPoints(roughPoints, edgeRoughPoints);

                    double length = analyticLengthValid ? analyticLength : fallbackLength;
                    if (!IsPositiveFinite(length))
                        continue;

                    segments.Add(new DeformedEdgeSegment
                    {
                        Edge = edge,
                        UStart = u0,
                        UEnd = u1,
                        LengthM = length
                    });
                    totalLength += length;
                    chordLength += Distance(start, end);
                }

                double maximumSize = ReadMaximumSize(roughPoints);
                if (segments.Count == 0
                    || CountDistinctPoints(roughPoints) < 3
                    || !IsPositiveFinite(totalLength)
                    || maximumSize <= 1e-10)
                {
                    reason = "EMPTY_OR_ZERO_LENGTH_LOOP";
                    LogCheapLoopDebug(
                        occurrenceForDebug, loopId, coedgeObjects.Length, segments.Count,
                        closedSingleEdge, totalAnalyticLength, totalFallbackLength,
                        roughPoints.Count, maximumSize, false, reason);
                    return false;
                }

                if (maximumSize > DeformedDebugMaximumLoopSizeM)
                {
                    reason = "BOUNDING_SIZE_TOO_LARGE:" + FormatMm(maximumSize) + "mm";
                    LogCheapLoopDebug(
                        occurrenceForDebug, loopId, coedgeObjects.Length, segments.Count,
                        closedSingleEdge, totalAnalyticLength, totalFallbackLength,
                        roughPoints.Count, maximumSize, false, reason);
                    return false;
                }
                if (totalLength > DeformedDebugMaximumPerimeterM)
                {
                    reason = "PERIMETER_TOO_LARGE:" + FormatMm(totalLength) + "mm";
                    LogCheapLoopDebug(
                        occurrenceForDebug, loopId, coedgeObjects.Length, segments.Count,
                        closedSingleEdge, totalAnalyticLength, totalFallbackLength,
                        roughPoints.Count, maximumSize, false, reason);
                    return false;
                }

                cheap = new DeformedCheapLoop
                {
                    Segments = segments,
                    PerimeterM = totalLength,
                    ChordSumM = chordLength,
                    BoundingMaximumM = maximumSize
                };
                reason = "OK";
                LogCheapLoopDebug(
                    occurrenceForDebug, loopId, coedgeObjects.Length, segments.Count,
                    closedSingleEdge, totalAnalyticLength, totalFallbackLength,
                    roughPoints.Count, maximumSize, true, reason);
                return true;
            }

            private static bool TrySampleEdgeForCheapPass(
                Edge edge,
                double uStart,
                double uEnd,
                int intervalCount,
                out List<double[]> points,
                out double fallbackLength)
            {
                points = new List<double[]>();
                fallbackLength = 0;
                intervalCount = Math.Max(4, intervalCount);
                double[] previous = null;
                for (int index = 0; index <= intervalCount; index++)
                {
                    double fraction = index / (double)intervalCount;
                    double parameter = uStart + (uEnd - uStart) * fraction;
                    double[] point = EvaluateEdge(edge, parameter);
                    if (!IsPoint(point))
                        return false;
                    points.Add(point);
                    if (previous != null)
                        fallbackLength += Distance(previous, point);
                    previous = point;
                }
                return IsPositiveFinite(fallbackLength);
            }

            private static void AddDistinctRoughPoints(
                List<double[]> destination,
                IEnumerable<double[]> source)
            {
                foreach (double[] point in source ?? Enumerable.Empty<double[]>())
                {
                    if (!IsPoint(point))
                        continue;
                    bool exists = destination.Any(item => Distance(item, point) <= 1e-10);
                    if (!exists)
                        destination.Add(point);
                }
            }

            private static int CountDistinctPoints(List<double[]> points)
            {
                List<double[]> distinct = new List<double[]>();
                AddDistinctRoughPoints(distinct, points);
                return distinct.Count;
            }

            private static double ReadMaximumSize(List<double[]> points)
            {
                if (points == null || points.Count == 0)
                    return 0;
                double[] min;
                double[] max;
                ReadBounds(points, out min, out max);
                return Math.Max(
                    max[0] - min[0],
                    Math.Max(max[1] - min[1], max[2] - min[2]));
            }

            private static bool IsPositiveFinite(double value)
            {
                return !double.IsNaN(value)
                    && !double.IsInfinity(value)
                    && value > 1e-10;
            }

            private static void LogCheapLoopDebug(
                string component,
                int loopId,
                int coedgeCount,
                int segmentCount,
                bool closedSingleEdge,
                double analyticLengthM,
                double fallbackLengthM,
                int roughPointCount,
                double boundingMaximumM,
                bool accepted,
                string reason)
            {
                if (!VerboseHoleDebug)
                    return;
                Debug.WriteLine(Prefix + " LOOP_DEBUG"
                    + " component=" + (component ?? "")
                    + ", loop=" + loopId
                    + ", coedges=" + coedgeCount
                    + ", segments=" + segmentCount
                    + ", closedSingleEdge=" + closedSingleEdge.ToString().ToLowerInvariant()
                    + ", analyticLength=" + FormatMm(analyticLengthM) + "mm"
                    + ", fallbackLength=" + FormatMm(fallbackLengthM) + "mm"
                    + ", roughPointCount=" + roughPointCount
                    + ", bbox=" + FormatMm(boundingMaximumM) + "mm"
                    + ", result=" + (accepted ? "ACCEPT" : "REJECT")
                    + ", reason=" + (reason ?? ""));
            }

            private bool TryAnalyzeLoop(
                Face2 face,
                DeformedCheapLoop cheap,
                int loopId,
                out DeformedLoopCandidate candidate,
                out string reason)
            {
                candidate = null;
                reason = "UNKNOWN";
                int sampleCount = cheap.Segments.Count > 8
                    || cheap.PerimeterM > Math.Max(cheap.ChordSumM * 1.05, 0.050)
                        ? DeformedDebugAdaptiveSampleCount
                        : DeformedDebugDefaultSampleCount;
                List<double[]> samples = SampleLoopByApproximateArcLength(cheap.Segments, sampleCount);
                if (samples.Count < 8)
                {
                    reason = "INSUFFICIENT_SAMPLES:" + samples.Count;
                    return false;
                }

                double[] mean;
                double[] normal;
                double[] u;
                double[] v;
                double planarity;
                if (!TryBuildPcaFrame(samples, out mean, out normal, out u, out v, out planarity))
                {
                    reason = "PCA_FAILED";
                    return false;
                }

                double[] faceNormal = SafeFaceNormal(face);
                bool normalReliable = IsDirection(faceNormal);
                if (normalReliable && Dot(normal, faceNormal) < 0)
                    normal = Scale(normal, -1);

                double signedArea;
                double[] centroid = ComputePolygonCentroid3d(samples, mean, u, v, out signedArea);
                double area = Math.Abs(signedArea);
                if (!IsPoint(centroid) || area <= 1e-12)
                {
                    reason = "INVALID_POLYGON_AREA";
                    return false;
                }

                double span = cheap.BoundingMaximumM;
                double planarLimit = Math.Max(0.00002, span * 0.002);
                double deformedLimit = Math.Max(0.00010, span * 0.020);
                string status;
                string confidence;
                if (planarity <= planarLimit)
                {
                    status = "LOOP_PAIRED";
                    confidence = normalReliable ? "HIGH" : "MEDIUM";
                }
                else if (planarity <= deformedLimit)
                {
                    status = "DEFORMED_LOOP";
                    confidence = normalReliable ? "MEDIUM" : "LOW";
                }
                else
                {
                    status = "LOW_CONFIDENCE";
                    confidence = "LOW";
                }

                candidate = new DeformedLoopCandidate
                {
                    LoopId = loopId,
                    Center = centroid,
                    Normal = normal,
                    SupportingNormalReliable = normalReliable,
                    PerimeterM = cheap.PerimeterM,
                    AreaM2 = area,
                    PlanarityErrorM = planarity,
                    BoundingMaximumM = span,
                    SampleCount = samples.Count,
                    Status = status,
                    Confidence = confidence
                };
                return true;
            }

            private void PairAndLog(
                List<DeformedWorldCandidate> candidates,
                string occurrence,
                string componentPath,
                string bodyName)
            {
                if (candidates.Count == 0)
                    return;
                int[] best = Enumerable.Repeat(-1, candidates.Count).ToArray();
                double[] bestScore = Enumerable.Repeat(double.MaxValue, candidates.Count).ToArray();
                double[] secondScore = Enumerable.Repeat(double.MaxValue, candidates.Count).ToArray();
                for (int first = 0; first < candidates.Count; first++)
                {
                    for (int second = 0; second < candidates.Count; second++)
                    {
                        if (first == second)
                            continue;
                        DeformedWorldCandidate a = candidates[first];
                        DeformedWorldCandidate b = candidates[second];
                        double normalDot = Dot(a.Normal, b.Normal);
                        double parallel = Math.Abs(normalDot);
                        if (parallel < Math.Cos(30.0 * Math.PI / 180.0))
                            continue;
                        if (a.Local.SupportingNormalReliable && b.Local.SupportingNormalReliable && normalDot > -0.5)
                            continue;

                        double distance = Distance(a.Center, b.Center);
                        if (distance > DeformedDebugPairMaximumGapM)
                            continue;
                        double transverse = Length(Subtract(
                            Subtract(b.Center, a.Center),
                            Scale(a.Normal, Dot(Subtract(b.Center, a.Center), a.Normal))));
                        double sizeDifference = Math.Abs(a.Local.PerimeterM - b.Local.PerimeterM)
                            / Math.Max(a.Local.PerimeterM, b.Local.PerimeterM);
                        if (sizeDifference > 0.35 || transverse > Math.Max(0.002, a.Local.BoundingMaximumM * 0.25))
                            continue;

                        double score = distance + transverse * 4.0 + sizeDifference * 0.010;
                        if (score < bestScore[first])
                        {
                            secondScore[first] = bestScore[first];
                            bestScore[first] = score;
                            best[first] = second;
                        }
                        else if (score < secondScore[first])
                        {
                            secondScore[first] = score;
                        }
                    }
                }

                HashSet<int> paired = new HashSet<int>();
                for (int first = 0; first < candidates.Count; first++)
                {
                    int second = best[first];
                    if (second < 0 || second <= first || best[second] != first)
                        continue;
                    bool ambiguous = secondScore[first] <= bestScore[first] * 1.15
                        || secondScore[second] <= bestScore[second] * 1.15;
                    DeformedWorldCandidate a = candidates[first];
                    DeformedWorldCandidate b = candidates[second];
                    paired.Add(first);
                    paired.Add(second);
                    pairedCount++;
                    double[] recoveredDirection = ClonePoint(a.Normal);
                    if (Dot(recoveredDirection, b.Normal) > 0)
                        recoveredDirection = Scale(recoveredDirection, -1);
                    recoveredDirection = Normalize(Subtract(recoveredDirection, b.Normal));
                    if (!IsDirection(recoveredDirection))
                        recoveredDirection = Normalize(a.Normal);
                    CanonicalizeDirection(recoveredDirection);
                    double equivalentDiameterA = 2.0 * Math.Sqrt(a.Local.AreaM2 / Math.PI);
                    double equivalentDiameterB = 2.0 * Math.Sqrt(b.Local.AreaM2 / Math.PI);
                    recoveredHoles.Add(new DeformedRecoveredHole
                    {
                        ComponentOccurrence = occurrence,
                        ComponentPath = componentPath,
                        BodyName = bodyName,
                        MouthCenterA = ClonePoint(a.Center),
                        MouthCenterB = ClonePoint(b.Center),
                        RecoveredCenter = Scale(Add(a.Center, b.Center), 0.5),
                        RecoveredDirection = recoveredDirection,
                        MouthGapM = Distance(a.Center, b.Center),
                        PerimeterAM = a.Local.PerimeterM,
                        PerimeterBM = b.Local.PerimeterM,
                        AreaAM2 = a.Local.AreaM2,
                        AreaBM2 = b.Local.AreaM2,
                        ApproximateDiameterM = (equivalentDiameterA + equivalentDiameterB) * 0.5,
                        PlanarityErrorM = Math.Max(a.Local.PlanarityErrorM, b.Local.PlanarityErrorM),
                        Confidence = ambiguous ? "LOW" : CombineConfidence(a.Local.Confidence, b.Local.Confidence),
                        IsAmbiguous = ambiguous,
                        LoopIdA = a.Local.LoopId,
                        LoopIdB = b.Local.LoopId
                    });
                    Debug.WriteLine(Prefix + " PAIR component=" + occurrence
                        + ", body=" + bodyName
                        + ", loops=" + a.Local.LoopId + "<->" + b.Local.LoopId
                        + ", status=" + (ambiguous ? "LOW_CONFIDENCE" : "OPPOSITE_NORMAL")
                        + ", center=" + FormatPointMm(Scale(Add(a.Center, b.Center), 0.5))
                        + ", mouthGap=" + FormatMm(Distance(a.Center, b.Center)) + "mm"
                        + ", mutualBest=true"
                        + ", ambiguous=" + ambiguous);
                }

                for (int index = 0; index < candidates.Count; index++)
                {
                    if (paired.Contains(index))
                        continue;
                    singleCount++;
                    Debug.WriteLine(Prefix + " SINGLE component=" + occurrence
                        + ", body=" + bodyName
                        + ", loop=" + candidates[index].Local.LoopId
                        + ", status=SINGLE_MOUTH"
                        + ", center=" + FormatPointMm(candidates[index].Center));
                }
            }

            private static string CombineConfidence(string first, string second)
            {
                int rank = Math.Min(ConfidenceRank(first), ConfidenceRank(second));
                return rank >= 2 ? "HIGH" : (rank == 1 ? "MEDIUM" : "LOW");
            }

            private static int ConfidenceRank(string value)
            {
                if (string.Equals(value, "HIGH", StringComparison.OrdinalIgnoreCase))
                    return 2;
                if (string.Equals(value, "MEDIUM", StringComparison.OrdinalIgnoreCase))
                    return 1;
                return 0;
            }

            private bool IsDuplicateOfExistingCylinder(
                double[] center,
                double[] normal,
                DeformedLoopCandidate candidate,
                IList<HoleAxis> cylinders)
            {
                foreach (HoleAxis cylinder in cylinders ?? new List<HoleAxis>())
                {
                    if (cylinder == null || !IsPoint(cylinder.Center) || !IsDirection(cylinder.Direction))
                        continue;
                    double angle = AxisAngleDeg(normal, cylinder.Direction);
                    if (angle > 15.0)
                        continue;
                    double[] delta = Subtract(center, cylinder.Center);
                    double axial = Dot(delta, cylinder.Direction);
                    double transverse = Length(Subtract(delta, Scale(cylinder.Direction, axial)));
                    if (transverse <= Math.Max(DeformedDebugDuplicatePositionM, cylinder.RadiusM * 0.15)
                        && Math.Abs(axial) <= cylinder.AxialLength * 0.5 + 0.002)
                        return true;
                }
                return false;
            }

            private static List<double[]> SampleLoopByApproximateArcLength(
                List<DeformedEdgeSegment> segments,
                int targetCount)
            {
                List<double[]> result = new List<double[]>();
                double total = segments.Sum(item => item.LengthM);
                if (total <= 1e-12)
                    return result;
                for (int sampleIndex = 0; sampleIndex < targetCount; sampleIndex++)
                {
                    double target = total * sampleIndex / targetCount;
                    double accumulated = 0;
                    DeformedEdgeSegment selected = segments[segments.Count - 1];
                    double localFraction = 1.0;
                    foreach (DeformedEdgeSegment segment in segments)
                    {
                        if (target <= accumulated + segment.LengthM || ReferenceEquals(segment, segments[segments.Count - 1]))
                        {
                            selected = segment;
                            localFraction = segment.LengthM <= 1e-12
                                ? 0
                                : (target - accumulated) / segment.LengthM;
                            break;
                        }
                        accumulated += segment.LengthM;
                    }
                    localFraction = Math.Max(0, Math.Min(1, localFraction));
                    double parameter = selected.UStart + (selected.UEnd - selected.UStart) * localFraction;
                    double[] point = EvaluateEdge(selected.Edge, parameter);
                    if (IsPoint(point) && (result.Count == 0 || Distance(result[result.Count - 1], point) > 1e-10))
                        result.Add(point);
                }
                return result;
            }

            private static double[] EvaluateEdge(Edge edge, double parameter)
            {
                if (edge == null)
                    return null;
                try
                {
                    double[] values = ToDoubleArray(edge.Evaluate2(parameter, 0));
                    return values.Length >= 3 ? new[] { values[0], values[1], values[2] } : null;
                }
                catch { return null; }
            }

            private static bool TryBuildPcaFrame(
                List<double[]> points,
                out double[] mean,
                out double[] normal,
                out double[] u,
                out double[] v,
                out double planarity)
            {
                mean = new double[3];
                normal = null;
                u = null;
                v = null;
                planarity = double.MaxValue;
                foreach (double[] point in points)
                    mean = Add(mean, point);
                mean = Scale(mean, 1.0 / points.Count);

                double[,] covariance = new double[3, 3];
                foreach (double[] point in points)
                {
                    double[] delta = Subtract(point, mean);
                    for (int row = 0; row < 3; row++)
                        for (int column = 0; column < 3; column++)
                            covariance[row, column] += delta[row] * delta[column];
                }
                double[] eigenvalues;
                double[,] eigenvectors;
                JacobiEigenSymmetric3(covariance, out eigenvalues, out eigenvectors);
                int[] order = { 0, 1, 2 };
                Array.Sort(order, (first, second) => eigenvalues[first].CompareTo(eigenvalues[second]));
                normal = Normalize(new[]
                {
                    eigenvectors[0, order[0]], eigenvectors[1, order[0]], eigenvectors[2, order[0]]
                });
                u = Normalize(new[]
                {
                    eigenvectors[0, order[2]], eigenvectors[1, order[2]], eigenvectors[2, order[2]]
                });
                if (!IsDirection(normal) || !IsDirection(u))
                    return false;
                v = Normalize(Cross(normal, u));
                u = Normalize(Cross(v, normal));
                if (!IsDirection(v) || !IsDirection(u))
                    return false;

                double sumSquare = 0;
                foreach (double[] point in points)
                {
                    double distance = Dot(Subtract(point, mean), normal);
                    sumSquare += distance * distance;
                }
                planarity = Math.Sqrt(sumSquare / points.Count);
                return true;
            }

            private static void JacobiEigenSymmetric3(double[,] source, out double[] values, out double[,] vectors)
            {
                double[,] matrix = (double[,])source.Clone();
                vectors = new double[3, 3];
                for (int index = 0; index < 3; index++)
                    vectors[index, index] = 1.0;
                for (int iteration = 0; iteration < 24; iteration++)
                {
                    int p = 0;
                    int q = 1;
                    double maximum = Math.Abs(matrix[0, 1]);
                    if (Math.Abs(matrix[0, 2]) > maximum) { p = 0; q = 2; maximum = Math.Abs(matrix[0, 2]); }
                    if (Math.Abs(matrix[1, 2]) > maximum) { p = 1; q = 2; maximum = Math.Abs(matrix[1, 2]); }
                    if (maximum <= 1e-24)
                        break;
                    double angle = 0.5 * Math.Atan2(2.0 * matrix[p, q], matrix[q, q] - matrix[p, p]);
                    double cosine = Math.Cos(angle);
                    double sine = Math.Sin(angle);
                    for (int index = 0; index < 3; index++)
                    {
                        double mp = matrix[index, p];
                        double mq = matrix[index, q];
                        matrix[index, p] = cosine * mp - sine * mq;
                        matrix[index, q] = sine * mp + cosine * mq;
                    }
                    for (int index = 0; index < 3; index++)
                    {
                        double mp = matrix[p, index];
                        double mq = matrix[q, index];
                        matrix[p, index] = cosine * mp - sine * mq;
                        matrix[q, index] = sine * mp + cosine * mq;
                    }
                    for (int index = 0; index < 3; index++)
                    {
                        double vp = vectors[index, p];
                        double vq = vectors[index, q];
                        vectors[index, p] = cosine * vp - sine * vq;
                        vectors[index, q] = sine * vp + cosine * vq;
                    }
                }
                values = new[] { matrix[0, 0], matrix[1, 1], matrix[2, 2] };
            }

            private static double[] ComputePolygonCentroid3d(
                List<double[]> points,
                double[] origin,
                double[] u,
                double[] v,
                out double signedArea)
            {
                double areaTwice = 0;
                double centroidX = 0;
                double centroidY = 0;
                for (int index = 0; index < points.Count; index++)
                {
                    double[] firstDelta = Subtract(points[index], origin);
                    double[] secondDelta = Subtract(points[(index + 1) % points.Count], origin);
                    double x0 = Dot(firstDelta, u);
                    double y0 = Dot(firstDelta, v);
                    double x1 = Dot(secondDelta, u);
                    double y1 = Dot(secondDelta, v);
                    double cross = x0 * y1 - x1 * y0;
                    areaTwice += cross;
                    centroidX += (x0 + x1) * cross;
                    centroidY += (y0 + y1) * cross;
                }
                signedArea = areaTwice * 0.5;
                if (Math.Abs(areaTwice) <= 1e-18)
                    return null;
                centroidX /= 3.0 * areaTwice;
                centroidY /= 3.0 * areaTwice;
                return Add(origin, Add(Scale(u, centroidX), Scale(v, centroidY)));
            }

            private static double[] SafeFaceNormal(Face2 face)
            {
                try
                {
                    double[] normal = ToDoubleArray(face == null ? null : face.Normal);
                    return IsDirection(normal) ? Normalize(normal) : null;
                }
                catch { return null; }
            }

            private static bool SafeIsOuter(Loop2 loop)
            {
                try { return loop.IsOuter(); }
                catch { return false; }
            }

            private static bool SafeIsSingular(Loop2 loop)
            {
                try { return loop.IsSingular(); }
                catch { return false; }
            }

            private static string SafeBodyName(Body2 body)
            {
                try { return body == null ? "" : body.Name ?? ""; }
                catch { return ""; }
            }

            private static void ReadBounds(List<double[]> points, out double[] min, out double[] max)
            {
                min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
                max = new[] { double.MinValue, double.MinValue, double.MinValue };
                foreach (double[] point in points)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        min[axis] = Math.Min(min[axis], point[axis]);
                        max[axis] = Math.Max(max[axis], point[axis]);
                    }
                }
            }

            private static string BuildCandidateSignature(DeformedLoopCandidate candidate)
            {
                return Math.Round(candidate.Center[0], 6).ToString(CultureInfo.InvariantCulture) + "|"
                    + Math.Round(candidate.Center[1], 6).ToString(CultureInfo.InvariantCulture) + "|"
                    + Math.Round(candidate.Center[2], 6).ToString(CultureInfo.InvariantCulture) + "|"
                    + Math.Round(candidate.PerimeterM, 6).ToString(CultureInfo.InvariantCulture);
            }
        }

        private sealed class DeformedBodyCache
        {
            public string CacheKey;
            public int FaceCount;
            public int InnerLoopCount;
            public readonly List<DeformedLoopCandidate> Candidates = new List<DeformedLoopCandidate>();
        }

        private sealed class DeformedCheapLoop
        {
            public List<DeformedEdgeSegment> Segments;
            public double PerimeterM;
            public double ChordSumM;
            public double BoundingMaximumM;
        }

        private sealed class DeformedEdgeSegment
        {
            public Edge Edge;
            public double UStart;
            public double UEnd;
            public double LengthM;
        }

        private sealed class DeformedLoopCandidate
        {
            public int LoopId;
            public double[] Center;
            public double[] Normal;
            public bool SupportingNormalReliable;
            public double PerimeterM;
            public double AreaM2;
            public double PlanarityErrorM;
            public double BoundingMaximumM;
            public int SampleCount;
            public string Status;
            public string Confidence;
        }

        private sealed class DeformedWorldCandidate
        {
            public DeformedLoopCandidate Local;
            public double[] Center;
            public double[] Normal;
        }

        private sealed class DeformedRecoveredHole
        {
            public string ComponentOccurrence;
            public string ComponentPath;
            public string BodyName;
            public double[] MouthCenterA;
            public double[] MouthCenterB;
            public double[] RecoveredCenter;
            public double[] RecoveredDirection;
            public double MouthGapM;
            public double PerimeterAM;
            public double PerimeterBM;
            public double AreaAM2;
            public double AreaBM2;
            public double ApproximateDiameterM;
            public double PlanarityErrorM;
            public string Confidence;
            public bool IsAmbiguous;
            public int LoopIdA;
            public int LoopIdB;
        }

        private sealed class DeformedValidationMatch
        {
            public DeformedRecoveredHole Recovered;
            public HoleAxis Reference;
            public double TransverseOffsetM;
            public double AxialSeparationM;
            public double FaceGapM;
            public double AngleDifferenceDeg;
            public double ReferenceDiameterM;
            public double DiameterDifferenceM;
            public double DiameterDifferencePercent;
            public double Score;
            public double SecondBestScore;
        }

        private enum HoleSource
        {
            AnalyticCylinder,
            CurvedCylinder,
            Slot,
            RecoveredDeformed
        }

        private sealed class HoleAxis
        {
            public string ComponentOccurrence;
            public string ComponentPath;
            public string BodyName;
            public HoleSource Source;
            public int RecoveredLoopIdA;
            public int RecoveredLoopIdB;
            public double[] Point;
            public double[] Direction;
            public double[] Center;
            public double MinProjection;
            public double MaxProjection;
            public double RadiusM;
            public int SourceFaceCount;
            public double SweepRad;
            public bool IsSlotEnd;
            public bool IsSlot;
            public bool IsCurvedBoundaryFallback;
            public double AxialLength { get { return Math.Max(0, MaxProjection - MinProjection); } }

            public HoleAxis Clone()
            {
                return new HoleAxis
                {
                    ComponentOccurrence = ComponentOccurrence,
                    ComponentPath = ComponentPath,
                    BodyName = BodyName,
                    Source = Source,
                    RecoveredLoopIdA = RecoveredLoopIdA,
                    RecoveredLoopIdB = RecoveredLoopIdB,
                    Point = (double[])Point.Clone(),
                    Direction = (double[])Direction.Clone(),
                    Center = (double[])Center.Clone(),
                    MinProjection = MinProjection,
                    MaxProjection = MaxProjection,
                    RadiusM = RadiusM,
                    SourceFaceCount = SourceFaceCount,
                    SweepRad = SweepRad,
                    IsSlotEnd = IsSlotEnd,
                    IsSlot = IsSlot,
                    IsCurvedBoundaryFallback = IsCurvedBoundaryFallback
                };
            }
        }

        private sealed class HoleStack
        {
            public readonly List<HoleAxis> Holes = new List<HoleAxis>();
            public HoleMatchSource MatchSource = HoleMatchSource.Normal;
            public readonly HashSet<HoleAxis> FarActualHoles = new HashSet<HoleAxis>();
            public readonly List<FarMatchCandidate> FarMatches = new List<FarMatchCandidate>();
            public readonly List<FarMarkerRelation> FarMarkerRelations = new List<FarMarkerRelation>();
        }

        private enum HoleMatchSource
        {
            Normal,
            FarMisalignmentRecovery
        }

        private sealed class TrustedHoleAnchor
        {
            public HoleAxis HoleA;
            public HoleAxis HoleB;
        }

        private sealed class RelationshipAnchorCandidate
        {
            public int FirstIndex;
            public int SecondIndex;
            public string OccurrencePairKey;
            public double Score;
        }

        private sealed class TrustedOccurrencePair
        {
            public string OccurrenceA;
            public string OccurrenceB;
            public bool IsTrusted;
            public readonly List<TrustedHoleAnchor> NormalAnchors = new List<TrustedHoleAnchor>();
            public readonly List<TrustedHoleAnchor> RelationshipAnchors = new List<TrustedHoleAnchor>();

            // FAR set-difference can use both types only after the safe gate below.
            public IEnumerable<TrustedHoleAnchor> Anchors
            {
                get { return NormalAnchors.Concat(RelationshipAnchors); }
            }

            public bool IsProductionTrusted
            {
                get
                {
                    int total = NormalAnchors.Count + RelationshipAnchors.Count;
                    return total >= TrustedOccurrenceMinimumAnchorCount
                        && (NormalAnchors.Count >= 1 || RelationshipAnchors.Count >= 2);
                }
            }

            public void AddNormalAnchor(HoleAxis holeA, HoleAxis holeB)
            {
                AddAnchor(NormalAnchors, holeA, holeB);
            }

            public void AddRelationshipAnchor(HoleAxis holeA, HoleAxis holeB)
            {
                AddAnchor(RelationshipAnchors, holeA, holeB);
            }

            private static void AddAnchor(List<TrustedHoleAnchor> anchors, HoleAxis holeA, HoleAxis holeB)
            {
                if (holeA == null || holeB == null || anchors.Any(item =>
                    ReferenceEquals(item.HoleA, holeA) && ReferenceEquals(item.HoleB, holeB)))
                    return;
                anchors.Add(new TrustedHoleAnchor { HoleA = holeA, HoleB = holeB });
            }

            public bool Contains(string occurrence)
            {
                return string.Equals(OccurrenceA, occurrence, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(OccurrenceB, occurrence, StringComparison.OrdinalIgnoreCase);
            }

            public string Other(string occurrence)
            {
                return string.Equals(OccurrenceA, occurrence, StringComparison.OrdinalIgnoreCase)
                    ? OccurrenceB
                    : OccurrenceA;
            }
        }

        private sealed class FarMatchCandidate
        {
            public int SourceIndex;
            public int TargetIndex;
            public TrustedOccurrencePair Trust;
            public double PositionM;
            public double AxialSeparationM;
            public double FaceGapM;
            public double AngleDeg;
            public double PatternResidualM;
            public bool TargetInExistingStack;
            public double Score;
            public double SecondBestScore = double.PositiveInfinity;
        }

        // Quan he marker chi dung cho hien thi. Cap nay duoc luu ngay luc
        // FAR_RECOVERY chap nhan assignment de khong mat counterpart sau khi gop stack.
        private sealed class FarMarkerRelation
        {
            public HoleAxis SourceHole;
            public HoleAxis CounterpartHole;
            public double TransverseOffsetM;
        }

        private sealed class HolePairCandidate
        {
            public int FirstIndex;
            public int SecondIndex;
            public double PositionM;
            public double AngleDeg;
            public double AxialGapM;
        }

        private sealed class HoleCluster
        {
            public readonly List<int> MemberIndexes = new List<int>();
            public readonly HashSet<string> ComponentOccurrences = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<int> PrimaryStackIds = new HashSet<int>();
        }

        private sealed class HoleStackResult
        {
            public HoleStack Stack;
            public HoleAxis Reference;
            public readonly List<HoleAxis> Outliers = new List<HoleAxis>();
            public double MaxOffsetM;
            public double MaxAngleDeg;
            public bool IsNg;
            public string Reason;
        }

        private struct SpatialCellKey : IEquatable<SpatialCellKey>
        {
            public int X;
            public int Y;
            public int Z;

            public bool Equals(SpatialCellKey other)
            {
                return X == other.X && Y == other.Y && Z == other.Z;
            }

            public override bool Equals(object obj)
            {
                return obj is SpatialCellKey && Equals((SpatialCellKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 397) ^ Y;
                    hash = (hash * 397) ^ Z;
                    return hash;
                }
            }
        }

        /// <summary>
        /// Broad phase thuần managed. Ban kinh truy van duoc mo rong theo nua
        /// chieu dai truc lon nhat de khong loai nham cap co center cach nhau
        /// theo chieu day vat lieu nhung hai mieng lo van ke nhau.
        /// </summary>
        private sealed class HoleSpatialIndex
        {
            private readonly List<HoleAxis> holes;
            private readonly double cellSizeM;
            private readonly double maximumHalfAxialLengthM;
            private readonly Dictionary<SpatialCellKey, List<int>> cells =
                new Dictionary<SpatialCellKey, List<int>>();

            public long LastQueryCandidateCount { get; private set; }

            public HoleSpatialIndex(List<HoleAxis> holes, double cellSizeM)
            {
                this.holes = holes ?? new List<HoleAxis>();
                this.cellSizeM = Math.Max(cellSizeM, 1e-6);
                maximumHalfAxialLengthM = this.holes.Count == 0
                    ? 0
                    : this.holes.Max(item => item == null ? 0 : item.AxialLength * 0.5);
                for (int index = 0; index < this.holes.Count; index++)
                {
                    HoleAxis hole = this.holes[index];
                    if (hole == null || !IsPoint(hole.Center))
                        continue;
                    SpatialCellKey key = ReadKey(hole.Center);
                    List<int> bucket;
                    if (!cells.TryGetValue(key, out bucket))
                    {
                        bucket = new List<int>();
                        cells[key] = bucket;
                    }
                    bucket.Add(index);
                }
            }

            public void ResetStats()
            {
                LastQueryCandidateCount = 0;
            }

            public IEnumerable<int> QueryForHole(
                int sourceIndex,
                double transverseLimitM,
                double adjacentGapM)
            {
                if (sourceIndex < 0 || sourceIndex >= holes.Count)
                    yield break;
                HoleAxis source = holes[sourceIndex];
                if (source == null || !IsPoint(source.Center))
                    yield break;

                double radius = Math.Max(0, transverseLimitM)
                    + Math.Max(0, adjacentGapM)
                    + source.AxialLength * 0.5
                    + maximumHalfAxialLengthM
                    + 1e-9;
                SpatialCellKey minimum = ReadKey(new[]
                {
                    source.Center[0] - radius,
                    source.Center[1] - radius,
                    source.Center[2] - radius
                });
                SpatialCellKey maximum = ReadKey(new[]
                {
                    source.Center[0] + radius,
                    source.Center[1] + radius,
                    source.Center[2] + radius
                });

                double radiusSquared = radius * radius;
                for (int x = minimum.X; x <= maximum.X; x++)
                {
                    for (int y = minimum.Y; y <= maximum.Y; y++)
                    {
                        for (int z = minimum.Z; z <= maximum.Z; z++)
                        {
                            List<int> bucket;
                            if (!cells.TryGetValue(new SpatialCellKey { X = x, Y = y, Z = z }, out bucket))
                                continue;
                            foreach (int index in bucket)
                            {
                                LastQueryCandidateCount++;
                                if (DistanceSquared(source.Center, holes[index].Center) <= radiusSquared)
                                    yield return index;
                            }
                        }
                    }
                }
            }

            private SpatialCellKey ReadKey(double[] point)
            {
                return new SpatialCellKey
                {
                    X = (int)Math.Floor(point[0] / cellSizeM),
                    Y = (int)Math.Floor(point[1] / cellSizeM),
                    Z = (int)Math.Floor(point[2] / cellSizeM)
                };
            }

            private static double DistanceSquared(double[] first, double[] second)
            {
                double x = first[0] - second[0];
                double y = first[1] - second[1];
                double z = first[2] - second[2];
                return x * x + y * y + z * z;
            }
        }

        private sealed class PatternVector
        {
            public HoleAxis Hole;
            public double[] Direction;
            public double DistanceM;
            public double RowErrorM;
        }

        private sealed class PatternAnchorPair
        {
            public HoleAxis First;
            public HoleAxis Second;
        }

        private sealed class PatternRowCandidate
        {
            public PatternAnchorPair Anchor;
            public double[] Direction;
            public List<PatternVector> FirstRow;
            public List<PatternVector> SecondRow;
            public int CommonAnchorSupport;
            public int Score;
        }

        private enum PatternAssignmentStep
        {
            None,
            Match,
            GapSource,
            GapTarget
        }

        private sealed class PatternMatchCandidate
        {
            public bool IsValid;
            public bool IsPitchShift;
            public double Cost;
            public double Confidence;
            public double LocalPitchM;
            public double AlongResidualM;
            public double AcrossResidualM;
            public double[] ExpectedCenter;
        }

        private sealed class PatternRowMatch
        {
            public PatternVector First;
            public PatternVector Second;
            public bool IsAmbiguous;
            public double Confidence;
            public double LocalPitchM;
            public double AlongResidualM;
            public double AcrossResidualM;
            public double PreviousFirstDistanceM;
            public double PreviousSecondDistanceM;
            public double[] ExpectedCenter;
        }

        private sealed class PatternAssignmentResult
        {
            public readonly List<PatternRowMatch> Matches = new List<PatternRowMatch>();
            public readonly List<PatternRowMatch> AllMatches = new List<PatternRowMatch>();
            public int GapSourceCount;
            public int GapTargetCount;
            public int AmbiguousCount;
            public int RejectExpectedPosition;
            public int RejectPitchShift;
            public int RejectAmbiguous;
            public int GapAccepted;
        }

        private sealed class IndependentPatternRow
        {
            public string ComponentOccurrence;
            public double[] AxisDirection;
            public double[] Direction;
            public List<HoleAxis> Holes;
        }

        private sealed class PatternIssue
        {
            public string FirstComponent;
            public string SecondComponent;
            public string Kind;
            public double[] AnchorDirection;
            public HoleAxis ExpectedHole;
            public HoleAxis ActualHole;
            public double[] ExpectedCenter;
            public double[] ActualCenter;
            public double PositionErrorM;
            public double PitchErrorM;
            public double RowErrorM;
        }

        private sealed class DisjointSet
        {
            private readonly int[] parent;
            private readonly byte[] rank;

            public DisjointSet(int count)
            {
                parent = new int[count];
                rank = new byte[count];
                for (int index = 0; index < count; index++)
                    parent[index] = index;
            }

            public int Find(int value)
            {
                if (parent[value] != value)
                    parent[value] = Find(parent[value]);
                return parent[value];
            }

            public void Union(int first, int second)
            {
                int firstRoot = Find(first);
                int secondRoot = Find(second);
                if (firstRoot == secondRoot)
                    return;
                if (rank[firstRoot] < rank[secondRoot])
                    parent[firstRoot] = secondRoot;
                else if (rank[firstRoot] > rank[secondRoot])
                    parent[secondRoot] = firstRoot;
                else
                {
                    parent[secondRoot] = firstRoot;
                    rank[firstRoot]++;
                }
            }
        }
    }
}
