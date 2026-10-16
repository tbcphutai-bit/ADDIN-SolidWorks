using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        public bool EnableDiagnostics { get; set; }
        public bool AutoCreateMarkers { get; set; } = true;
        // Keep the proven close behavior until the deferred rebuild path has a CAD benchmark.
        public bool UseDeferredMarkerRebuild { get; set; }
        private bool running;
        private bool cancelRequested;
        private bool closeRequested;
        private long lastMessagePump;
        private CheckHoleResultsForm resultsForm;
        private CheckHoleReport lastReport;
        private ModelDoc2 reportModel;
        private string reportState;
        private readonly Dictionary<string, Component2> reportComponents =
            new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, MarkerRow> markerRows = new Dictionary<int, MarkerRow>();
        public event Action<Control> ResultsViewReady;

        private sealed class MarkerRow
        {
            public HoleStackResult Stack;
            public HoleAxis Actual;
            public PatternIssue Pattern;
        }

        private void EnsureResultsForm()
        {
            if (resultsForm != null && !resultsForm.IsDisposed) return;
            resultsForm = new CheckHoleResultsForm(
                (diagnostic, markers) => { EnableDiagnostics = diagnostic; AutoCreateMarkers = markers; Run(); },
                () => cancelRequested = true, FindReportRow, MarkReportRows);
            resultsForm.FormClosed += (s, e) =>
            {
                resultsForm = null; lastReport = null; reportModel = null; reportState = null;
                markerRows.Clear(); reportComponents.Clear();
                missingPatternBodies.Clear();
                skippedPatternHoles.Clear();
            };
            resultsForm.SetOptions(EnableDiagnostics, AutoCreateMarkers);
            resultsForm.PublishNotice = PublishResultText;
            resultsForm.TopLevel = false;
            resultsForm.FormBorderStyle = FormBorderStyle.None;
            resultsForm.MinimumSize = System.Drawing.Size.Empty;
            resultsForm.Show3dControls();
            ResultsViewReady?.Invoke(resultsForm);
        }

        private bool CheckCanceled()
        {
            if (IsCanceled()) cancelRequested = true;
            long now = Stopwatch.GetTimestamp();
            if (running && now - lastMessagePump >= Stopwatch.Frequency / 10)
            {
                lastMessagePump = now;
                Application.DoEvents();
                if (IsCanceled()) cancelRequested = true;
            }
            return cancelRequested;
        }

        public void CloseResults()
        {
            closeRequested = true;
            cancelRequested = true;
            if (!running && resultsForm != null && !resultsForm.IsDisposed) resultsForm.Close();
        }

        private void FinishResultsUi()
        {
            if (resultsForm == null || resultsForm.IsDisposed) return;
            resultsForm.SetBusy(false, null);
            if (closeRequested) resultsForm.Close();
        }

        private void ThrowIfCanceled()
        {
            if (CheckCanceled()) throw new OperationCanceledException("Đã hủy CHECK HOLE; kết quả chưa hoàn tất.");
        }

        private string CaptureModelState(ModelDoc2 model)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try { return ReadModelState(model); }
            finally { if (activeProfile != null) activeProfile.StateCaptureMs += watch.ElapsedMilliseconds; }
        }

        private string ReadModelState(ModelDoc2 model)
        {
            if (model == null) throw new InvalidOperationException("Assembly đã đóng.");
            AssemblyDoc assembly = model as AssemblyDoc;
            if (assembly == null) throw new InvalidOperationException("Cần mở đúng Assembly đã kiểm tra.");
            var state = new StringBuilder();
            state.Append(model.GetUpdateStamp()).Append('|').Append(SafeConfigurationName(model));
            var components = ToObjectArray(assembly.GetComponents(false)).OfType<Component2>()
                .OrderBy(SafeComponentName, StringComparer.OrdinalIgnoreCase);
            var stamps = new Dictionary<ModelDoc2, int>();
            foreach (Component2 component in components)
            {
                state.Append('\n').Append(SafeComponentName(component)).Append('|')
                    .Append(component.IsSuppressed()).Append('|').Append(component.IsEnvelope()).Append('|')
                    .Append(SafeReferencedConfiguration(component));
                if (!IsUsableComponent(component)) continue;
                MathTransform transform = component.Transform2;
                if (transform == null) throw new InvalidOperationException("Không đọc được transform của component.");
                foreach (double value in ToDoubleArray(transform.ArrayData))
                    state.Append('|').Append(value.ToString("R", CultureInfo.InvariantCulture));
                ModelDoc2 part = component.GetModelDoc2() as ModelDoc2;
                if (part == null) { state.Append("|UNAVAILABLE"); continue; }
                int stamp;
                if (!stamps.TryGetValue(part, out stamp)) { stamp = part.GetUpdateStamp(); stamps.Add(part, stamp); }
                state.Append('|').Append(stamp);
            }
            return state.ToString();
        }

        private void ValidateReportState()
        {
            if (lastReport == null || reportModel == null || reportState == null)
                throw new InvalidOperationException("Chưa có kết quả hoàn tất. Hãy chạy CHECK HOLE.");
            ModelDoc2 active = swApp.ActiveDoc as ModelDoc2;
            if (active == null || !active.Equals(reportModel))
                throw new InvalidOperationException("Hãy kích hoạt đúng Assembly đã kiểm tra.");
            if (!running && swApp.CommandInProgress)
                throw new InvalidOperationException("Hãy hoàn tất lệnh SOLIDWORKS đang chạy trước khi thao tác với kết quả.");
            if (!string.Equals(reportState, CaptureModelState(reportModel), StringComparison.Ordinal))
                throw new InvalidOperationException("Model đã thay đổi. Hãy bấm Kiểm tra lại trước khi tìm vị trí hoặc vẽ NG.");
            if (GetActiveSketch(reportModel) != null)
                throw new InvalidOperationException("Hãy thoát Sketch đang edit trước khi thao tác với kết quả.");
        }

        private CheckHoleReport BuildReport(string title, string configuration, List<HoleAxis> holes,
            List<HoleStackResult> results, List<PatternIssue> patterns, double engineSeconds)
        {
            markerRows.Clear();
            var report = new CheckHoleReport { AssemblyTitle = title, Configuration = configuration,
                LogicalHoles = holes.Count, StackCount = results.Count, NgStacks = results.Count(x => x.IsNg),
                PatternNg = patterns.Count(x => !x.IsMissingHole), MissingHoles = patterns.Count(x => x.IsMissingHole), EngineSeconds = engineSeconds };
            var grouped = new HashSet<HoleAxis>();
            foreach (HoleStackResult result in results)
            {
                bool deformed = result.Stack.Holes.Any(x => x.Source == HoleSource.RecoveredDeformed);
                foreach (HoleAxis hole in result.Stack.Holes)
                {
                    grouped.Add(hole);
                    if (ReferenceEquals(hole, result.Reference)) continue;
                    bool ng = result.Outliers.Contains(hole);
                    double offset = TransverseCenterDistance(result.Reference, hole);
                    double angle = AxisAngleDeg(result.Reference.Direction, hole.Direction);
                    string reason = ng ? (offset > PositionToleranceM ? "Lệch tâm vượt dung sai" : "Lệch góc vượt dung sai") : "Trong dung sai đã đánh giá";
                    if (deformed) reason += "; góc trong cụm có lỗ biến dạng chưa được kết luận";
                    var row = new CheckHoleReportRow { Id = report.Rows.Count, Status = ng ? "NG" : "OK",
                        Kind = hole.IsSlot ? "SLOT" : hole.Source == HoleSource.RecoveredDeformed ? "Biến dạng" : "Tròn",
                        ReferenceComponent = result.Reference.ComponentOccurrence, ActualComponent = hole.ComponentOccurrence,
                        OffsetMm = offset * 1000.0, AngleDeg = deformed ? (double?)null : angle, Reason = reason,
                        ReferenceCenter = ClonePoint(result.Reference.Center), ActualCenter = ClonePoint(hole.Center),
                        ViewAxis = ClonePoint(result.Reference.Direction), ViewRadiusM = Math.Max(hole.RadiusM, result.Reference.RadiusM), CanMark = ng };
                    report.Rows.Add(row);
                    if (ng) markerRows.Add(row.Id, new MarkerRow { Stack = result, Actual = hole });
                }
            }
            foreach (PatternIssue issue in patterns)
            {
                var row = new CheckHoleReportRow { Id = report.Rows.Count, Status = "NG", Kind = issue.IsMissingHole ? "Thiếu lỗ" : "Pattern",
                    ReferenceComponent = issue.FirstComponent, ActualComponent = issue.SecondComponent,
                    OffsetMm = issue.IsMissingHole ? (double?)null : issue.PositionErrorM * 1000.0,
                    Reason = issue.IsMissingHole ? (string.IsNullOrEmpty(issue.SkippedPatternName) ? "Thiếu lỗ đối ứng trên " + issue.SecondComponent
                        : "Thiếu lỗ do skip instance #" + (issue.SkippedInstanceIndex + 1) + " của pattern " + issue.SkippedPatternName + " trên " + issue.SecondComponent)
                        + "; pitch chuẩn=" + FormatMm(issue.ExpectedPitchM) + " mm"
                        : issue.Kind + "; pitch=" + FormatMm(issue.PitchErrorM) + " mm; lệch hàng=" + FormatMm(issue.RowErrorM) + " mm",
                    ReferenceCenter = ClonePoint(issue.IsMissingHole && issue.ExpectedHole != null ? issue.ExpectedHole.Center : issue.ExpectedCenter),
                    ActualCenter = ClonePoint(issue.IsMissingHole ? issue.ExpectedCenter : issue.ActualCenter),
                    ViewAxis = issue.IsMissingHole ? MissingHoleViewAxis(issue) : issue.ExpectedHole == null ? null : ClonePoint(issue.ExpectedHole.Direction),
                    OrientFromReferenceSide = issue.IsMissingHole,
                    ViewRadiusM = issue.ExpectedHole == null ? 0 : issue.ExpectedHole.RadiusM, CanMark = true };
                report.Rows.Add(row); markerRows.Add(row.Id, new MarkerRow { Pattern = issue });
            }
            foreach (HoleAxis hole in holes.Where(x => !grouped.Contains(x)))
                report.Rows.Add(new CheckHoleReportRow { Id = report.Rows.Count, Status = "Chưa ghép",
                    Kind = hole.IsSlot ? "SLOT" : hole.Source == HoleSource.RecoveredDeformed ? "Biến dạng" : "Tròn",
                    ActualComponent = hole.ComponentOccurrence, ActualCenter = ClonePoint(hole.Center), ViewAxis = ClonePoint(hole.Direction), ViewRadiusM = hole.RadiusM,
                    Reason = "Chưa tìm được đối ứng; cần xem xét chức năng của lỗ." });
            report.GroupedHoles = grouped.Count;
            report.UnmatchedHoles = holes.Count - grouped.Count;
            return report;
        }

        private void FindReportRow(CheckHoleReportRow row)
        {
            if (running || row == null) return;
            try
            {
                ValidateReportState();
                reportModel.ClearSelection2(true);
                bool append = false;
                foreach (string occurrence in new[] { row.ReferenceComponent, row.ActualComponent }.Where(x => !string.IsNullOrEmpty(x)).Distinct())
                {
                    Component2 component;
                    if (reportComponents.TryGetValue(occurrence, out component) && component.Select4(append, null, false)) append = true;
                }
                double[] actual = row.ActualCenter;
                if (!IsPoint(actual)) throw new InvalidOperationException("Không có tọa độ hợp lệ cho dòng này.");
                double[] reference = IsPoint(row.ReferenceCenter) ? row.ReferenceCenter : actual;
                if (row.OrientFromReferenceSide) OrientViewAxis(row.ViewAxis, false);
                else OrientViewAlongHole(row.ViewAxis);
                double margin = Math.Max(0.003, row.ViewRadiusM * 1.5);
                ZoomToReportPoints(reference, actual, margin);
                reportModel.GraphicsRedraw2();
            }
            catch (Exception ex) { PublishResultText("Không tìm được vị trí lỗ: " + ex.Message); }
        }

        private void OrientViewAlongHole(double[] axis)
        {
            OrientViewAxis(axis, true);
        }

        private void ZoomToReportPoints(double[] reference, double[] actual, double margin)
        {
            ModelView view = reportModel.ActiveView as ModelView;
            if (view == null || mathUtility == null) throw new InvalidOperationException("Không lấy được khung nhìn 3D.");
            // ViewZoomTo2 expects points rotated by Orientation3, not raw model coordinates.
            // Translation and Scale2 are deliberately excluded, as in the API's zoom-region example.
            MathTransform orientation = view.Orientation3;
            double[] first = TransformPoint(reference, orientation);
            double[] second = TransformPoint(actual, orientation);
            if (!IsPoint(first) || !IsPoint(second)) throw new InvalidOperationException("Không chuyển được vị trí lỗ sang khung nhìn.");
            reportModel.ViewZoomTo2(Math.Min(first[0], second[0]) - margin, Math.Min(first[1], second[1]) - margin,
                Math.Min(first[2], second[2]) - margin, Math.Max(first[0], second[0]) + margin,
                Math.Max(first[1], second[1]) + margin, Math.Max(first[2], second[2]) + margin);
        }

        private void OrientViewAxis(double[] axis, bool preserveCurrentSide)
        {
            if (!IsDirection(axis) || mathUtility == null) return;
            ModelView view = reportModel.ActiveView as ModelView;
            if (view == null) return;
            double[] normal = Normalize(axis);
            MathVector inView = mathUtility.CreateVector(normal) as MathVector;
            inView = inView == null ? null : inView.MultiplyTransform(view.Orientation3) as MathVector;
            double[] visible = ToDoubleArray(inView == null ? null : inView.ArrayData);
            if (preserveCurrentSide && visible.Length >= 3 && visible[2] < 0) normal = Scale(normal, -1);
            double[] right, up;
            BuildPerpendicularFrame(normal, out right, out up);
            MathTransform frame = mathUtility.ComposeTransform(
                mathUtility.CreateVector(right) as MathVector, mathUtility.CreateVector(up) as MathVector,
                mathUtility.CreateVector(normal) as MathVector, mathUtility.CreateVector(new double[3]) as MathVector, 1.0);
            if (frame == null) throw new InvalidOperationException("Không tạo được hướng nhìn theo trục lỗ.");
            MathTransform orientation = frame.Inverse() as MathTransform;
            if (orientation == null) throw new InvalidOperationException("Không xác định được hướng nhìn theo trục lỗ.");
            view.Orientation3 = orientation;
        }

        private void MarkReportRows(IList<int> ids)
        {
            if (running) return;
            bool oldCommand = false;
            bool hasOldCommand = false;
            Cursor oldCursor = Cursor.Current;
            try
            {
                ValidateReportState();
                running = true; cancelRequested = false;
                activeProfile = new RunProfile();
                oldCommand = swApp.CommandInProgress; hasOldCommand = true; swApp.CommandInProgress = true;
                Cursor.Current = Cursors.WaitCursor;
                resultsForm.SetBusy(true, "Đang tạo sketch NG...");
                CreateReportMarkers(ids);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK HOLE ASSY] MARKER_ONLY ERROR: " + ex);
                if (lastReport != null && resultsForm != null) resultsForm.SetReport(lastReport);
                PublishResultText("Không vẽ được NG: " + ex.Message);
            }
            finally
            {
                if (hasOldCommand) { try { swApp.CommandInProgress = oldCommand; } catch { } }
                Cursor.Current = oldCursor; activeProfile = null; running = false;
                FinishResultsUi();
            }
        }

        private void CreateReportMarkers(IList<int> ids)
        {
            ValidateReportState();
            var chosen = ids == null ? markerRows.Values.ToList()
                : ids.Distinct().Where(markerRows.ContainsKey).Select(x => markerRows[x]).ToList();
            if (chosen.Count == 0) throw new InvalidOperationException("Hãy chọn ít nhất một dòng NG.");
            var stackResults = new List<HoleStackResult>();
            foreach (var group in chosen.Where(x => x.Stack != null).GroupBy(x => x.Stack))
            {
                var copy = new HoleStackResult { Stack = group.Key.Stack, Reference = group.Key.Reference, IsNg = true,
                    MaxOffsetM = group.Key.MaxOffsetM, MaxAngleDeg = group.Key.MaxAngleDeg, Reason = group.Key.Reason };
                copy.Outliers.AddRange(group.Select(x => x.Actual).Distinct()); stackResults.Add(copy);
            }
            var patterns = chosen.Where(x => x.Pattern != null).Select(x => x.Pattern).Distinct().ToList();
            Stopwatch watch = Stopwatch.StartNew();
            HashSet<string> before = GetTopLevelFeatureNames(reportModel);
            Feature feature = null;
            try
            {
                feature = CreateNgMarkerSketch(reportModel, stackResults, patterns);
                if (feature == null) throw new InvalidOperationException("Không tìm được feature của sketch NG mới.");
                string keep = SafeFeatureName(feature);
                if (string.IsNullOrWhiteSpace(keep)) throw new InvalidOperationException("Không xác nhận được sketch NG mới.");
                DeleteOldMarkers(reportModel, keep);
                feature.Name = MarkerFeaturePrefix + "-" + (stackResults.Count + patterns.Count).ToString("000", CultureInfo.InvariantCulture);
                reportModel.GraphicsRedraw2();
                watch.Stop();
                lastReport.MarkerStatus = "Đã vẽ " + chosen.Count + " dòng NG thành sketch. Thời gian vẽ: "
                    + watch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " s.";
                reportState = CaptureModelState(reportModel);
                resultsForm.SetReport(lastReport);
                PublishResultText(lastReport.Summary);
                Debug.WriteLine("[CHECK HOLE ASSY] MARKER_ONLY selectedRows=" + chosen.Count + ", totalMs=" + watch.ElapsedMilliseconds);
            }
            catch
            {
                // Only remove the new feature. Previous successful markers survive a failed draw.
                feature = feature ?? FindNewSketchFeature(reportModel, before);
                if (feature != null)
                {
                    try { reportModel.ClearSelection2(true); if (feature.Select2(false, 0)) reportModel.Extension.DeleteSelection2(0); }
                    catch (Exception cleanup) { Debug.WriteLine("[CHECK HOLE ASSY] marker rollback: " + cleanup.Message); }
                }
                // A failure can leave uncertain model state; require another check instead of trusting it.
                reportState = null;
                throw;
            }
        }

        private static void EnsureNgMarkerVisible(ModelDoc2 model, Feature feature)
        {
            if (model == null || feature == null) throw new InvalidOperationException("Không tìm được sketch NG để hiển thị.");
            model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swViewDisplayHideAllTypes, false);
            model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplaySketches, true);
            model.ClearSelection2(true);
            try
            {
                if (!feature.Select2(false, 0)) throw new InvalidOperationException("Không chọn được sketch NG để hiển thị.");
                model.UnblankSketch();
            }
            finally { model.ClearSelection2(true); }
            if (!model.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplaySketches)
                || model.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swViewDisplayHideAllTypes))
                throw new InvalidOperationException("Sketch NG đang bị ẩn bởi chế độ hiển thị của model.");
        }
    }
}
