using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed class RoundHoleCheckResult
    {
        public int CheckedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int SkippedCount { get; set; }
        public int RoundHoleCount { get; set; }
        public int SlotHoleCount { get; set; }
        public int OkCount { get; set; }
        public int CheckCount { get; set; }
        public int NgCount { get; set; }
        public bool Canceled { get; set; }
        public List<RoundHoleRowResult> Results { get; } = new List<RoundHoleRowResult>();
        public HashSet<int> HighlightRowIndexes { get; } = new HashSet<int>();
    }

    public sealed class RoundHoleRowResult
    {
        public int BomRowIndex { get; set; }
        public string BuhinNo { get; set; }
        public string HoleType { get; set; }
        public double? R1Mm { get; set; }
        public double? R2Mm { get; set; }
        public double? DeltaRMm { get; set; }
        public string Note { get; set; }
        public string Status { get; set; }
        public string Configuration { get; set; }
        public string PartPath { get; set; }
        public int HoleNumber { get; set; }
        public string SheetName { get; set; }
        public string ViewName { get; set; }
        public string MarkerId { get; set; }
        public double? DrawingXmm { get; set; }
        public double? DrawingYmm { get; set; }

        // Model-space values are used only to map the checked loop back to
        // the corresponding visible edge in the drawing view.
        public double? CenterModelX { get; set; }
        public double? CenterModelY { get; set; }
        public double? CenterModelZ { get; set; }
        public double? Arc1CenterModelX { get; set; }
        public double? Arc1CenterModelY { get; set; }
        public double? Arc1CenterModelZ { get; set; }
        public double? Arc2CenterModelX { get; set; }
        public double? Arc2CenterModelY { get; set; }
        public double? Arc2CenterModelZ { get; set; }
        public RoundHolePreviewData PreviewData { get; set; }
    }

    public sealed class RoundHolePreviewData
    {
        public string BuhinNo { get; set; }
        public string PartPath { get; set; }
        public string Configuration { get; set; }
        public string DrawingViewName { get; set; }
        public string ProjectionSource { get; set; }
        public readonly List<RoundHolePreviewPath> Paths = new List<RoundHolePreviewPath>();
        public readonly List<RoundHolePreviewPath> DrawingPaths = new List<RoundHolePreviewPath>();
    }

    public sealed class RoundHolePreviewPath
    {
        public bool IsOuter { get; set; }
        public int HoleNumber { get; set; }
        public string Status { get; set; }
        public string MarkerId { get; set; }
        public readonly List<RoundHolePreviewPoint> Points = new List<RoundHolePreviewPoint>();
    }

    public sealed class RoundHolePreviewPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double ModelX { get; set; }
        public double ModelY { get; set; }
        public double ModelZ { get; set; }
    }

    public sealed class CheckRoundRunner
    {
        private const double OkToleranceMm = 0.05;
        private const double CheckToleranceMm = 0.10;
        private const double CenterGroupToleranceM = 0.00002;

        private readonly ISldWorks swApp;
        private readonly DataGridView gridBom;

        public CheckRoundRunner(ISldWorks app, DataGridView grid)
        {
            swApp = app;
            gridBom = grid;
        }

        public RoundHoleCheckResult Run(
            Action<int> progressStarted,
            Action<int, int> progressChanged,
            Func<bool> isCancellationRequested)
        {
            RoundHoleCheckResult result = new RoundHoleCheckResult();
            HashSet<string> checkedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ModelDoc2 originalDocument = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            bool oldCommandInProgress = false;

            Debug.WriteLine("[CHECK ROUND] ===== RUN START =====");
            Debug.WriteLine("[CHECK ROUND] Chi kiem tra hinh hoc lo trong Flat-Pattern; khong kiem tra DIM/kich thuoc.");

            try
            {
                if (swApp == null || gridBom == null)
                    return result;

                oldCommandInProgress = swApp.CommandInProgress;
                swApp.CommandInProgress = true;
                result.CheckedCount = CountCheckedRows();
                progressStarted?.Invoke(result.CheckedCount);

                foreach (DataGridViewRow row in gridBom.Rows)
                {
                    if (IsCancellationRequested(isCancellationRequested))
                    {
                        result.Canceled = true;
                        break;
                    }

                    if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                        continue;

                    Debug.WriteLine("[CHECK ROUND] Row start. index=" + row.Index
                        + ", buhinNo=" + GetCellText(row, 1)
                        + ", file=" + GetCellText(row, 5)
                        + ", tag=" + (row.Tag == null ? "null" : row.Tag.GetType().FullName));

                    List<RoundCheckTarget> targets = GetTargets(row);
                    if (targets.Count == 0)
                    {
                        result.SkippedCount++;
                        Debug.WriteLine("[CHECK ROUND] Row skipped: khong co duong dan part.");
                    }

                    foreach (RoundCheckTarget target in targets)
                    {
                        if (IsCancellationRequested(isCancellationRequested))
                        {
                            result.Canceled = true;
                            break;
                        }

                        string key = (target.PartPath ?? "").Trim().ToUpperInvariant()
                            + "|" + (target.Configuration ?? "").Trim().ToUpperInvariant();
                        if (key == "|" || !checkedParts.Add(key))
                            continue;

                        List<RoundHoleRowResult> rows = CheckTarget(target, originalDocument);
                        if (rows.Count == 0)
                        {
                            Debug.WriteLine("[CHECK ROUND] Part khong co lo tron/lo dai can kiem tra: " + target.PartPath);
                            continue;
                        }

                        foreach (RoundHoleRowResult item in rows)
                        {
                            result.Results.Add(item);
                            CountResult(result, item);
                            if (item.Status == "NG" || item.Status == "CHECK")
                                result.HighlightRowIndexes.Add(row.Index);

                            Debug.WriteLine("[CHECK ROUND] Result. row=" + row.Index
                                + ", type=" + item.HoleType
                                + ", R1=" + FormatNullable(item.R1Mm)
                                + ", R2=" + FormatNullable(item.R2Mm)
                                + ", delta=" + FormatNullable(item.DeltaRMm)
                                + ", status=" + item.Status
                                + ", note=" + item.Note);
                        }
                    }

                    result.ProcessedCount++;
                    progressChanged?.Invoke(result.ProcessedCount, result.CheckedCount);
                    Application.DoEvents();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] RUN ERROR: " + ex);
                throw;
            }
            finally
            {
                try { swApp.CommandInProgress = oldCommandInProgress; } catch { }
                RestoreDocument(originalDocument);
                Debug.WriteLine("[CHECK ROUND] ===== RUN END ===== checkedRows=" + result.CheckedCount
                    + ", processed=" + result.ProcessedCount
                    + ", round=" + result.RoundHoleCount
                    + ", slot=" + result.SlotHoleCount
                    + ", OK=" + result.OkCount
                    + ", CHECK=" + result.CheckCount
                    + ", NG=" + result.NgCount
                    + ", skipped=" + result.SkippedCount
                    + ", canceled=" + result.Canceled);
            }

            return result;
        }

        private List<RoundHoleRowResult> CheckTarget(RoundCheckTarget target, ModelDoc2 originalDocument)
        {
            List<RoundHoleRowResult> rows = new List<RoundHoleRowResult>();
            string partPath = (target.PartPath ?? "").Trim();
            Debug.WriteLine("[CHECK ROUND] Part start. path=" + partPath
                + ", sourceConfig=" + target.Configuration);

            if (partPath.Length == 0 || !File.Exists(partPath)
                || !string.Equals(Path.GetExtension(partPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(CreateOperationalResult(target, "CHECK", "Khong tim thay file part hop le."));
                return rows;
            }

            ModelDoc2 alreadyOpen = null;
            try { alreadyOpen = swApp.GetOpenDocumentByName(partPath) as ModelDoc2; } catch { }
            bool alreadyOpenVisible = false;
            if (alreadyOpen != null)
            {
                try { alreadyOpenVisible = alreadyOpen.Visible; } catch { }
            }
            Debug.WriteLine("[CHECK ROUND] Existing document. found=" + (alreadyOpen != null)
                + ", visible=" + alreadyOpenVisible);
            if (alreadyOpenVisible)
            {
                Debug.WriteLine("[CHECK ROUND] Skip visible/open part de tranh thay doi trang thai: " + partPath);
                rows.Add(CreateOperationalResult(target, "CHECK",
                    "Part dang mo tren tab SOLIDWORKS; bo qua de tranh cap nhat chi tiet."));
                return rows;
            }

            int errors = 0;
            int warnings = 0;
            bool openedByChecker = false;
            bool visibilityChanged = false;
            ModelDoc2 part = null;
            string temporaryPartPath = "";
            string pathToOpen = partPath;

            try
            {
                if (alreadyOpen != null)
                {
                    temporaryPartPath = CreateTemporaryPartCopy(partPath);
                    if (string.IsNullOrWhiteSpace(temporaryPartPath))
                    {
                        rows.Add(CreateOperationalResult(target, "CHECK",
                            "Part dang duoc drawing nap ngam nhung khong tao duoc ban sao tam."));
                        return rows;
                    }

                    pathToOpen = temporaryPartPath;
                    Debug.WriteLine("[CHECK ROUND] Referenced part loaded in background. Use temporary copy="
                        + temporaryPartPath);
                }

                try
                {
                    swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                    visibilityChanged = true;
                }
                catch { }

                part = swApp.OpenDoc6(
                    pathToOpen,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;
                openedByChecker = part != null;

                Debug.WriteLine("[CHECK ROUND] Open read-only. success=" + (part != null)
                    + ", errors=" + errors + ", warnings=" + warnings);

                if (part == null)
                {
                    rows.Add(CreateOperationalResult(target, "CHECK", "Khong mo duoc part read-only."));
                    return rows;
                }

                string flatConfiguration = FindFlatConfiguration(part, target.Configuration);
                if (string.IsNullOrWhiteSpace(flatConfiguration))
                {
                    rows.Add(CreateOperationalResult(target, "CHECK", "Khong tim thay configuration SM-FLAT-PATTERN."));
                    return rows;
                }

                bool shown = TryShowConfiguration(part, flatConfiguration, "first");
                if (!shown && !string.IsNullOrWhiteSpace(target.Configuration))
                {
                    TryShowConfiguration(part, target.Configuration, "source-before-flat");
                    shown = TryShowConfiguration(part, flatConfiguration, "second");
                }

                if (!shown)
                {
                    Debug.WriteLine("[CHECK ROUND] Reopen temporary copy directly in Flat-Pattern.");
                    try
                    {
                        string title = part.GetTitle();
                        swApp.CloseDoc(title);
                        Debug.WriteLine("[CHECK ROUND] Close before direct-config reopen: " + title);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK ROUND] Close before reopen ERROR: " + ex.Message);
                    }

                    part = null;
                    openedByChecker = false;
                    errors = 0;
                    warnings = 0;
                    part = swApp.OpenDoc6(
                        pathToOpen,
                        (int)swDocumentTypes_e.swDocPART,
                        (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                        flatConfiguration,
                        ref errors,
                        ref warnings) as ModelDoc2;
                    openedByChecker = part != null;
                    Debug.WriteLine("[CHECK ROUND] Reopen direct Flat-Pattern. success=" + (part != null)
                        + ", errors=" + errors + ", warnings=" + warnings);
                    if (part != null)
                        shown = TryShowConfiguration(part, flatConfiguration, "direct-open");
                }

                if (!shown)
                {
                    rows.Add(CreateOperationalResult(target, "CHECK", "Khong chuyen duoc sang configuration Flat-Pattern."));
                    return rows;
                }

                target.FlatConfiguration = flatConfiguration;
                rows.AddRange(AnalyzeFlatPart(part, target));
                return rows;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Part ERROR: " + ex);
                rows.Add(CreateOperationalResult(target, "CHECK", ex.GetType().Name + ": " + ex.Message));
                return rows;
            }
            finally
            {
                if (openedByChecker && part != null)
                {
                    try
                    {
                        string title = part.GetTitle();
                        swApp.CloseDoc(title);
                        Debug.WriteLine("[CHECK ROUND] Close without save: " + title);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK ROUND] Close ERROR: " + ex.Message);
                    }
                }

                if (visibilityChanged)
                {
                    try { swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART); } catch { }
                }

                if (!string.IsNullOrWhiteSpace(temporaryPartPath))
                {
                    try
                    {
                        if (File.Exists(temporaryPartPath))
                            File.Delete(temporaryPartPath);
                        string directory = Path.GetDirectoryName(temporaryPartPath);
                        if (!string.IsNullOrWhiteSpace(directory)
                            && Directory.Exists(directory)
                            && Directory.GetFileSystemEntries(directory).Length == 0)
                        {
                            Directory.Delete(directory);
                        }
                        Debug.WriteLine("[CHECK ROUND] Delete temporary copy: " + temporaryPartPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK ROUND] Delete temporary copy ERROR: " + ex.Message);
                    }
                }

                RestoreDocument(originalDocument);
            }
        }

        private List<RoundHoleRowResult> AnalyzeFlatPart(ModelDoc2 model, RoundCheckTarget target)
        {
            List<RoundHoleRowResult> rows = new List<RoundHoleRowResult>();
            RoundHolePreviewData previewData = new RoundHolePreviewData
            {
                BuhinNo = target.BuhinNo,
                PartPath = target.PartPath,
                Configuration = target.FlatConfiguration ?? target.Configuration
            };
            PartDoc part = model as PartDoc;
            if (part == null)
            {
                rows.Add(CreateOperationalResult(target, "CHECK", "Document khong phai Part."));
                return rows;
            }

            object[] bodies = ToObjectArray(part.GetBodies2((int)swBodyType_e.swSolidBody, true));
            if (bodies.Length == 0)
            {
                rows.Add(CreateOperationalResult(target, "CHECK", "Khong tim thay solid body trong Flat-Pattern."));
                return rows;
            }

            int bodyIndex = 0;
            foreach (object bodyObject in bodies)
            {
                bodyIndex++;
                Body2 body = bodyObject as Body2;
                Face2 mainFace = FindLargestPlanarFace(body);
                if (mainFace == null)
                {
                    Debug.WriteLine("[CHECK ROUND] Body " + bodyIndex + ": khong co mat phang chinh.");
                    continue;
                }

                object[] loops = ToObjectArray(mainFace.GetLoops());
                Debug.WriteLine("[CHECK ROUND] Body " + bodyIndex + ": loops=" + loops.Length);
                int innerIndex = 0;
                PlaneProjector projector = CreatePlaneProjector(mainFace);
                foreach (object loopObject in loops)
                {
                    Loop2 loop = loopObject as Loop2;
                    if (loop == null)
                        continue;

                    RoundHolePreviewPath previewPath = CreatePreviewPath(loop, projector);
                    if (previewPath != null)
                        previewData.Paths.Add(previewPath);

                    if (loop.IsOuter())
                        continue;

                    innerIndex++;
                    RoundHoleRowResult row = AnalyzeHoleLoop(loop, target, bodyIndex, innerIndex);
                    if (row != null)
                    {
                        row.HoleNumber = rows.Count + 1;
                        rows.Add(row);
                        if (previewPath != null)
                        {
                            previewPath.HoleNumber = row.HoleNumber;
                            previewPath.Status = row.Status;
                        }
                    }
                }
            }

            bool hasAbnormal = false;
            foreach (RoundHoleRowResult row in rows)
            {
                if (row.Status == "NG" || row.Status == "CHECK")
                {
                    hasAbnormal = true;
                    break;
                }
            }
            if (hasAbnormal)
            {
                foreach (RoundHoleRowResult row in rows)
                {
                    if (row.Status == "NG" || row.Status == "CHECK")
                        row.PreviewData = previewData;
                }
            }

            return rows;
        }

        private RoundHoleRowResult AnalyzeHoleLoop(
            Loop2 loop,
            RoundCheckTarget target,
            int bodyIndex,
            int loopIndex)
        {
            object[] edges = ToObjectArray(loop.GetEdges());
            if (edges.Length == 0)
                return null;

            List<CircleInfo> circles = new List<CircleInfo>();
            List<LineInfo> lines = new List<LineInfo>();
            int otherCurveCount = 0;

            foreach (object edgeObject in edges)
            {
                Edge edge = edgeObject as Edge;
                if (edge == null)
                {
                    otherCurveCount++;
                    continue;
                }

                Curve curve = edge.GetCurve() as Curve;
                if (curve == null)
                {
                    otherCurveCount++;
                    continue;
                }

                CircleInfo circle;
                if (TryReadCircle(curve, out circle))
                {
                    circles.Add(circle);
                    continue;
                }

                LineInfo line;
                if (TryReadLine(edge, curve, out line))
                {
                    lines.Add(line);
                    continue;
                }

                otherCurveCount++;
            }

            Debug.WriteLine("[CHECK ROUND] Loop. body=" + bodyIndex
                + ", loop=" + loopIndex
                + ", edges=" + edges.Length
                + ", circle=" + circles.Count
                + ", line=" + lines.Count
                + ", other=" + otherCurveCount);

            // Loop chi co line la cutout dang polygon, khong phai lo can CHECK ROUND.
            // Loop co curve nhung khong con Circle/Arc la lo bi meo, khong co gia tri Phi/R.
            if (circles.Count == 0)
            {
                if (otherCurveCount > 0)
                {
                    RoundHoleRowResult irregular = CreateHoleResult(
                        target,
                        "IRREGULAR",
                        null,
                        null,
                        null,
                        "NG",
                        "Lo bi meo: curve khong con gia tri Phi/R trong Flat-Pattern."
                            + " Body=" + bodyIndex + ", Loop=" + loopIndex + ".");
                    SetLoopCenter(irregular, edges);
                    return irregular;
                }
                return null;
            }

            List<CircleGroup> groups = GroupCircles(circles);

            if (lines.Count == 0 && otherCurveCount == 0)
                return AnalyzeRoundHole(target, circles, groups, bodyIndex, loopIndex);

            if (lines.Count == 2 && otherCurveCount == 0 && circles.Count >= 2)
                return AnalyzeSlotHole(target, circles, groups, lines, bodyIndex, loopIndex);

            RoundHoleRowResult unknownHole = CreateHoleResult(
                target,
                "HOLE",
                null,
                null,
                null,
                "CHECK",
                "Loop co cung R nhung khong phai lo tron/lo dai tieu chuan; can kiem tra thu cong."
                    + " Body=" + bodyIndex + ", Loop=" + loopIndex + ".");
            SetAverageCenter(unknownHole, circles);
            return unknownHole;
        }

        private RoundHoleRowResult AnalyzeRoundHole(
            RoundCheckTarget target,
            List<CircleInfo> circles,
            List<CircleGroup> groups,
            int bodyIndex,
            int loopIndex)
        {
            double minRadius = double.MaxValue;
            double maxRadius = 0;
            foreach (CircleInfo circle in circles)
            {
                minRadius = Math.Min(minRadius, circle.RadiusM);
                maxRadius = Math.Max(maxRadius, circle.RadiusM);
            }

            double radiusDeltaMm = Math.Max(0, maxRadius - minRadius) * 1000.0;
            double centerDeltaMm = GetMaxCenterDistance(circles) * 1000.0;
            double deltaMm = Math.Max(radiusDeltaMm, centerDeltaMm);
            string status = GetToleranceStatus(deltaMm);
            string note;

            if (groups.Count != 1)
            {
                status = deltaMm > CheckToleranceMm ? "NG" : "CHECK";
                note = "Cac cung cua lo tron khong dong tam.";
            }
            else
            {
                note = status == "OK"
                    ? "Lo tron giu dung hinh hoc trong Flat-Pattern."
                    : "Ban kinh/tam cung cua lo tron khong dong deu.";
            }

            note += " Body=" + bodyIndex + ", Loop=" + loopIndex + ".";
            RoundHoleRowResult result = CreateHoleResult(
                target,
                "ROUND",
                AverageRadiusMm(circles),
                null,
                deltaMm,
                status,
                note);
            SetAverageCenter(result, circles);
            return result;
        }

        private RoundHoleRowResult AnalyzeSlotHole(
            RoundCheckTarget target,
            List<CircleInfo> circles,
            List<CircleGroup> groups,
            List<LineInfo> lines,
            int bodyIndex,
            int loopIndex)
        {
            if (groups.Count != 2)
            {
                RoundHoleRowResult invalidSlot = CreateHoleResult(
                    target,
                    "SLOT",
                    groups.Count > 0 ? groups[0].AverageRadiusM * 1000.0 : (double?)null,
                    groups.Count > 1 ? groups[1].AverageRadiusM * 1000.0 : (double?)null,
                    null,
                    "CHECK",
                    "Lo dai khong tim duoc dung hai dau R. Body=" + bodyIndex + ", Loop=" + loopIndex + ".");
                SetAverageCenter(invalidSlot, circles);
                return invalidSlot;
            }

            double r1Mm = groups[0].AverageRadiusM * 1000.0;
            double r2Mm = groups[1].AverageRadiusM * 1000.0;
            double deltaMm = Math.Max(
                Math.Abs(r1Mm - r2Mm),
                Math.Max(groups[0].RadiusSpreadM, groups[1].RadiusSpreadM) * 1000.0);

            double parallelError = GetParallelError(lines[0], lines[1]);
            bool sidesParallel = parallelError <= 0.0015;
            string status = GetToleranceStatus(deltaMm);
            string note;

            if (!sidesParallel)
            {
                status = "NG";
                note = "Hai canh thang cua lo dai khong song song.";
            }
            else
            {
                note = status == "OK"
                    ? "Hai dau R cua lo dai dong deu trong Flat-Pattern."
                    : "Hai dau R cua lo dai khong dong deu.";
            }

            note += " Body=" + bodyIndex + ", Loop=" + loopIndex + ".";
            RoundHoleRowResult result = CreateHoleResult(target, "SLOT", r1Mm, r2Mm, deltaMm, status, note);
            result.CenterModelX = (groups[0].X + groups[1].X) / 2.0;
            result.CenterModelY = (groups[0].Y + groups[1].Y) / 2.0;
            result.CenterModelZ = (groups[0].Z + groups[1].Z) / 2.0;
            result.Arc1CenterModelX = groups[0].X;
            result.Arc1CenterModelY = groups[0].Y;
            result.Arc1CenterModelZ = groups[0].Z;
            result.Arc2CenterModelX = groups[1].X;
            result.Arc2CenterModelY = groups[1].Y;
            result.Arc2CenterModelZ = groups[1].Z;
            return result;
        }

        private RoundHoleRowResult CreateHoleResult(
            RoundCheckTarget target,
            string holeType,
            double? r1Mm,
            double? r2Mm,
            double? deltaMm,
            string status,
            string note)
        {
            return new RoundHoleRowResult
            {
                BomRowIndex = target.BomRowIndex,
                BuhinNo = target.BuhinNo,
                HoleType = holeType,
                R1Mm = r1Mm,
                R2Mm = r2Mm,
                DeltaRMm = deltaMm,
                Note = note,
                Status = status,
                Configuration = target.FlatConfiguration ?? target.Configuration,
                PartPath = target.PartPath
            };
        }

        private RoundHoleRowResult CreateOperationalResult(
            RoundCheckTarget target,
            string status,
            string note)
        {
            return CreateHoleResult(target, "PART", null, null, null, status, note);
        }

        private string CreateTemporaryPartCopy(string partPath)
        {
            try
            {
                string directory = Path.Combine(
                    Path.GetTempPath(),
                    "TAI_CHECK_ROUND",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                // SOLIDWORKS khong cho mo hai document co cung title, ke ca khac path.
                // Dat ten tam khac title cua part dang duoc Drawing nap ngam.
                string temporaryName = "TAI_CHECK_" + Guid.NewGuid().ToString("N")
                    + "_" + Path.GetFileName(partPath);
                string temporaryPath = Path.Combine(directory, temporaryName);
                File.Copy(partPath, temporaryPath, true);
                return temporaryPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Create temporary copy ERROR: " + ex.Message);
                return "";
            }
        }

        private void SetAverageCenter(RoundHoleRowResult result, List<CircleInfo> circles)
        {
            if (result == null || circles == null || circles.Count == 0)
                return;

            double x = 0;
            double y = 0;
            double z = 0;
            foreach (CircleInfo circle in circles)
            {
                x += circle.X;
                y += circle.Y;
                z += circle.Z;
            }

            result.CenterModelX = x / circles.Count;
            result.CenterModelY = y / circles.Count;
            result.CenterModelZ = z / circles.Count;
        }

        private void SetLoopCenter(RoundHoleRowResult result, object[] edges)
        {
            if (result == null || edges == null || edges.Length == 0)
                return;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;
            bool found = false;

            foreach (object edgeObject in edges)
            {
                Edge edge = edgeObject as Edge;
                if (edge == null)
                    continue;

                double[] box = GetEdgeBounds(edge);
                if (box == null || box.Length < 6)
                    continue;

                minX = Math.Min(minX, box[0]);
                minY = Math.Min(minY, box[1]);
                minZ = Math.Min(minZ, box[2]);
                maxX = Math.Max(maxX, box[3]);
                maxY = Math.Max(maxY, box[4]);
                maxZ = Math.Max(maxZ, box[5]);
                found = true;
            }

            if (!found)
                return;

            result.CenterModelX = (minX + maxX) / 2.0;
            result.CenterModelY = (minY + maxY) / 2.0;
            result.CenterModelZ = (minZ + maxZ) / 2.0;
        }

        private double[] GetEdgeBounds(Edge edge)
        {
            if (edge == null)
                return null;

            Curve curve = null;
            try { curve = edge.GetCurve() as Curve; } catch { }
            if (curve == null)
                return null;

            double start = 0;
            double end = 0;
            bool closed = false;
            bool periodic = false;
            try
            {
                if (!curve.GetEndParams(out start, out end, out closed, out periodic))
                    return null;
            }
            catch
            {
                return null;
            }

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;
            bool found = false;
            const int sampleCount = 32;
            for (int i = 0; i <= sampleCount; i++)
            {
                double parameter = start + (end - start) * i / sampleCount;
                double[] point = null;
                try { point = curve.Evaluate(parameter) as double[]; } catch { }
                if (point == null || point.Length < 3)
                    continue;

                minX = Math.Min(minX, point[0]);
                minY = Math.Min(minY, point[1]);
                minZ = Math.Min(minZ, point[2]);
                maxX = Math.Max(maxX, point[0]);
                maxY = Math.Max(maxY, point[1]);
                maxZ = Math.Max(maxZ, point[2]);
                found = true;
            }

            return found
                ? new[] { minX, minY, minZ, maxX, maxY, maxZ }
                : null;
        }

        private PlaneProjector CreatePlaneProjector(Face2 face)
        {
            try
            {
                Surface surface = face == null ? null : face.GetSurface() as Surface;
                double[] values = surface == null ? null : surface.PlaneParams as double[];
                if (values == null || values.Length < 6)
                    return null;

                double[] normal = NormalizeVector(values[3], values[4], values[5]);
                if (normal == null)
                    return null;

                double rx = Math.Abs(normal[2]) < 0.9 ? 0.0 : 0.0;
                double ry = Math.Abs(normal[2]) < 0.9 ? 0.0 : 1.0;
                double rz = Math.Abs(normal[2]) < 0.9 ? 1.0 : 0.0;
                double[] axisU = NormalizeVector(
                    ry * normal[2] - rz * normal[1],
                    rz * normal[0] - rx * normal[2],
                    rx * normal[1] - ry * normal[0]);
                if (axisU == null)
                    return null;
                double[] axisV = NormalizeVector(
                    normal[1] * axisU[2] - normal[2] * axisU[1],
                    normal[2] * axisU[0] - normal[0] * axisU[2],
                    normal[0] * axisU[1] - normal[1] * axisU[0]);
                if (axisV == null)
                    return null;

                return new PlaneProjector
                {
                    OriginX = values[0],
                    OriginY = values[1],
                    OriginZ = values[2],
                    Ux = axisU[0],
                    Uy = axisU[1],
                    Uz = axisU[2],
                    Vx = axisV[0],
                    Vy = axisV[1],
                    Vz = axisV[2]
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Preview plane ERROR: " + ex.Message);
                return null;
            }
        }

        private RoundHolePreviewPath CreatePreviewPath(Loop2 loop, PlaneProjector projector)
        {
            if (loop == null || projector == null)
                return null;

            RoundHolePreviewPath path = new RoundHolePreviewPath();
            try { path.IsOuter = loop.IsOuter(); } catch { }
            object[] edges = ToObjectArray(loop.GetEdges());
            List<List<double[]>> edgePointSets = new List<List<double[]>>();
            foreach (object edgeObject in edges)
            {
                Edge edge = edgeObject as Edge;
                Curve curve = null;
                try { curve = edge == null ? null : edge.GetCurve() as Curve; } catch { }
                if (curve == null)
                    continue;

                List<double[]> edgePoints = SampleCurve(edge, curve);
                if (edgePoints.Count >= 2)
                    edgePointSets.Add(edgePoints);
            }

            List<double[]> connectedPoints = new List<double[]>();
            while (edgePointSets.Count > 0)
            {
                int bestIndex = 0;
                bool reverse = false;
                if (connectedPoints.Count > 0)
                {
                    double[] previous = connectedPoints[connectedPoints.Count - 1];
                    double bestDistance = double.MaxValue;
                    for (int i = 0; i < edgePointSets.Count; i++)
                    {
                        List<double[]> candidate = edgePointSets[i];
                        double toStart = DistanceSquared(previous, candidate[0]);
                        double toEnd = DistanceSquared(previous, candidate[candidate.Count - 1]);
                        if (toStart < bestDistance)
                        {
                            bestDistance = toStart;
                            bestIndex = i;
                            reverse = false;
                        }
                        if (toEnd < bestDistance)
                        {
                            bestDistance = toEnd;
                            bestIndex = i;
                            reverse = true;
                        }
                    }
                }

                List<double[]> edgePoints = edgePointSets[bestIndex];
                edgePointSets.RemoveAt(bestIndex);
                if (reverse)
                    edgePoints.Reverse();

                foreach (double[] point in edgePoints)
                {
                    if (connectedPoints.Count > 0
                        && DistanceSquared(connectedPoints[connectedPoints.Count - 1], point) < 1e-16)
                    {
                        continue;
                    }
                    connectedPoints.Add(point);
                }
            }

            foreach (double[] point in connectedPoints)
            {
                RoundHolePreviewPoint projected = projector.Project(point);
                if (projected != null)
                    path.Points.Add(projected);
            }
            if (path.Points.Count > 2)
            {
                RoundHolePreviewPoint first = path.Points[0];
                RoundHolePreviewPoint last = path.Points[path.Points.Count - 1];
                double dx = first.X - last.X;
                double dy = first.Y - last.Y;
                if (dx * dx + dy * dy <= 1e-10)
                {
                    path.Points.Add(new RoundHolePreviewPoint
                    {
                        X = first.X,
                        Y = first.Y,
                        ModelX = first.ModelX,
                        ModelY = first.ModelY,
                        ModelZ = first.ModelZ
                    });
                }
            }
            return path.Points.Count >= 2 ? path : null;
        }

        private List<double[]> SampleCurve(Edge edge, Curve curve)
        {
            List<double[]> points = new List<double[]>();
            if (edge == null || curve == null)
                return points;

            double start;
            double end;
            bool closed;
            bool periodic;
            try
            {
                CurveParamData edgeParams = edge.GetCurveParams3();
                if (edgeParams != null)
                {
                    start = edgeParams.UMinValue;
                    end = edgeParams.UMaxValue;
                    double[] startPoint = edgeParams.StartPoint as double[];
                    double[] endPoint = edgeParams.EndPoint as double[];
                    closed = DistanceSquared(startPoint, endPoint) <= 1e-16;
                    periodic = false;
                }
                else if (!curve.GetEndParams(out start, out end, out closed, out periodic))
                {
                    return points;
                }
            }
            catch
            {
                return points;
            }

            int sampleCount = 16;
            try
            {
                if (curve.IsLine())
                    sampleCount = 1;
                else if (curve.IsCircle())
                    sampleCount = closed ? 48 : 20;
            }
            catch { }

            for (int i = 0; i <= sampleCount; i++)
            {
                double parameter = start + (end - start) * i / sampleCount;
                double[] value = null;
                try { value = curve.Evaluate(parameter) as double[]; } catch { }
                if (value != null && value.Length >= 3)
                    points.Add(new[] { value[0], value[1], value[2] });
            }
            return points;
        }

        private static double DistanceSquared(double[] first, double[] second)
        {
            if (first == null || second == null || first.Length < 3 || second.Length < 3)
                return double.MaxValue;
            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            double dz = first[2] - second[2];
            return dx * dx + dy * dy + dz * dz;
        }

        private static double[] NormalizeVector(double x, double y, double z)
        {
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length < 1e-12)
                return null;
            return new[] { x / length, y / length, z / length };
        }

        private void CountResult(RoundHoleCheckResult result, RoundHoleRowResult row)
        {
            if (row.HoleType == "ROUND")
                result.RoundHoleCount++;
            else if (row.HoleType == "SLOT")
                result.SlotHoleCount++;

            if (row.Status == "NG")
                result.NgCount++;
            else if (row.Status == "CHECK")
                result.CheckCount++;
            else if (row.Status == "OK")
                result.OkCount++;
        }

        private List<RoundCheckTarget> GetTargets(DataGridViewRow row)
        {
            List<RoundCheckTarget> targets = new List<RoundCheckTarget>();
            AddTargets(targets, row.Tag, row);
            return targets;
        }

        private void AddTargets(List<RoundCheckTarget> targets, object source, DataGridViewRow row)
        {
            object[] sources = source as object[];
            if (sources != null)
            {
                foreach (object item in sources)
                    AddTargets(targets, item, row);
                return;
            }

            Component2 component = source as Component2;
            if (component != null)
            {
                string path = "";
                string configuration = "";
                string name = "";
                try { path = component.GetPathName() ?? ""; } catch { }
                try { configuration = component.ReferencedConfiguration ?? ""; } catch { }
                try { name = component.Name2 ?? ""; } catch { }
                targets.Add(CreateTarget(row, path, configuration, name));
                return;
            }

            string pathText = source as string;
            if (!string.IsNullOrWhiteSpace(pathText))
                targets.Add(CreateTarget(row, pathText, "", GetCellText(row, 5)));
        }

        private RoundCheckTarget CreateTarget(
            DataGridViewRow row,
            string path,
            string configuration,
            string componentName)
        {
            return new RoundCheckTarget
            {
                BomRowIndex = row.Index,
                BuhinNo = GetCellText(row, 1),
                BomFileName = GetCellText(row, 5),
                ComponentName = componentName ?? "",
                PartPath = path ?? "",
                Configuration = configuration ?? ""
            };
        }

        private int CountCheckedRows()
        {
            int count = 0;
            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (!row.IsNewRow && Convert.ToBoolean(row.Cells[0].Value ?? false))
                    count++;
            }
            return count;
        }

        private string FindFlatConfiguration(ModelDoc2 model, string sourceConfiguration)
        {
            object namesObject = null;
            try { namesObject = model.GetConfigurationNames(); } catch { }
            object[] names = ToObjectArray(namesObject);
            string source = (sourceConfiguration ?? "").Trim();
            string sourceUpper = source.ToUpperInvariant();
            string fallback = "";

            foreach (object nameObject in names)
            {
                string name = Convert.ToString(nameObject ?? "").Trim();
                string upper = name.ToUpperInvariant();
                if (!upper.Contains("FLAT-PATTERN"))
                    continue;

                if (fallback.Length == 0)
                    fallback = name;
                if (sourceUpper.Length > 0 && upper.StartsWith(sourceUpper, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            return fallback;
        }

        private bool TryShowConfiguration(ModelDoc2 model, string configurationName, string stage)
        {
            if (model == null || string.IsNullOrWhiteSpace(configurationName))
                return false;

            bool apiResult = false;
            string activeName = "";
            try { apiResult = model.ShowConfiguration2(configurationName); } catch { }
            try
            {
                Configuration active = model.ConfigurationManager == null
                    ? null
                    : model.ConfigurationManager.ActiveConfiguration;
                if (active != null)
                    activeName = active.Name ?? "";
            }
            catch { }

            bool activeMatches = string.Equals(
                activeName,
                configurationName,
                StringComparison.OrdinalIgnoreCase);
            Debug.WriteLine("[CHECK ROUND] Show config. stage=" + stage
                + ", requested=" + configurationName
                + ", apiResult=" + apiResult
                + ", active=" + activeName
                + ", activeMatches=" + activeMatches);
            return apiResult || activeMatches;
        }

        private Face2 FindLargestPlanarFace(Body2 body)
        {
            if (body == null)
                return null;

            Face2 largest = null;
            double largestArea = 0;
            object[] faces = ToObjectArray(body.GetFaces());
            foreach (object faceObject in faces)
            {
                Face2 face = faceObject as Face2;
                if (face == null)
                    continue;

                Surface surface = null;
                try { surface = face.GetSurface() as Surface; } catch { }
                if (surface == null)
                    continue;

                bool isPlane = false;
                try { isPlane = surface.IsPlane(); } catch { }
                if (!isPlane)
                    continue;

                double area = 0;
                try { area = face.GetArea(); } catch { }
                if (area > largestArea)
                {
                    largestArea = area;
                    largest = face;
                }
            }
            return largest;
        }

        private bool TryReadCircle(Curve curve, out CircleInfo circle)
        {
            circle = null;
            try
            {
                if (!curve.IsCircle())
                    return false;
                double[] values = curve.CircleParams as double[];
                if (values == null || values.Length < 7 || values[6] <= 0)
                    return false;
                circle = new CircleInfo
                {
                    X = values[0],
                    Y = values[1],
                    Z = values[2],
                    RadiusM = values[6]
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadLine(Edge edge, Curve curve, out LineInfo line)
        {
            line = null;
            try
            {
                if (!curve.IsLine())
                    return false;

                Vertex startVertex = edge.GetStartVertex() as Vertex;
                Vertex endVertex = edge.GetEndVertex() as Vertex;
                double[] start = startVertex == null ? null : startVertex.GetPoint() as double[];
                double[] end = endVertex == null ? null : endVertex.GetPoint() as double[];
                if (start == null || end == null || start.Length < 3 || end.Length < 3)
                    return false;

                line = new LineInfo
                {
                    Dx = end[0] - start[0],
                    Dy = end[1] - start[1],
                    Dz = end[2] - start[2]
                };
                return line.Length > 1e-10;
            }
            catch
            {
                return false;
            }
        }

        private List<CircleGroup> GroupCircles(List<CircleInfo> circles)
        {
            List<CircleGroup> groups = new List<CircleGroup>();
            foreach (CircleInfo circle in circles)
            {
                CircleGroup match = null;
                foreach (CircleGroup group in groups)
                {
                    if (Distance(circle.X, circle.Y, circle.Z, group.X, group.Y, group.Z) <= CenterGroupToleranceM)
                    {
                        match = group;
                        break;
                    }
                }

                if (match == null)
                {
                    match = new CircleGroup();
                    groups.Add(match);
                }
                match.Add(circle);
            }
            return groups;
        }

        private double GetMaxCenterDistance(List<CircleInfo> circles)
        {
            double maximum = 0;
            for (int i = 0; i < circles.Count; i++)
            {
                for (int j = i + 1; j < circles.Count; j++)
                {
                    maximum = Math.Max(maximum, Distance(
                        circles[i].X, circles[i].Y, circles[i].Z,
                        circles[j].X, circles[j].Y, circles[j].Z));
                }
            }
            return maximum;
        }

        private double AverageRadiusMm(List<CircleInfo> circles)
        {
            if (circles.Count == 0)
                return 0;
            double total = 0;
            foreach (CircleInfo circle in circles)
                total += circle.RadiusM;
            return total * 1000.0 / circles.Count;
        }

        private double GetParallelError(LineInfo first, LineInfo second)
        {
            double dot = Math.Abs(
                (first.Dx * second.Dx + first.Dy * second.Dy + first.Dz * second.Dz)
                / (first.Length * second.Length));
            return Math.Abs(1.0 - Math.Min(1.0, dot));
        }

        private string GetToleranceStatus(double deltaMm)
        {
            if (deltaMm <= OkToleranceMm)
                return "OK";
            if (deltaMm <= CheckToleranceMm)
                return "CHECK";
            return "NG";
        }

        private double Distance(
            double ax, double ay, double az,
            double bx, double by, double bz)
        {
            double dx = ax - bx;
            double dy = ay - by;
            double dz = az - bz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private object[] ToObjectArray(object value)
        {
            if (value == null)
                return new object[0];
            object[] objects = value as object[];
            if (objects != null)
                return objects;
            Array array = value as Array;
            if (array == null)
                return new object[0];
            object[] result = new object[array.Length];
            for (int i = 0; i < array.Length; i++)
                result[i] = array.GetValue(i);
            return result;
        }

        private bool IsCancellationRequested(Func<bool> callback)
        {
            return callback != null && callback();
        }

        private string GetCellText(DataGridViewRow row, int columnIndex)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Cells.Count)
                return "";
            return Convert.ToString(row.Cells[columnIndex].Value ?? "").Trim();
        }

        private string FormatNullable(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "-";
        }

        private void RestoreDocument(ModelDoc2 document)
        {
            if (swApp == null || document == null)
                return;
            try
            {
                int errors = 0;
                swApp.ActivateDoc3(
                    document.GetTitle(),
                    false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref errors);
                Debug.WriteLine("[CHECK ROUND] Restore document. title=" + document.GetTitle()
                    + ", errors=" + errors);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Restore document ERROR: " + ex.Message);
            }
        }

        private sealed class RoundCheckTarget
        {
            public int BomRowIndex;
            public string BuhinNo;
            public string BomFileName;
            public string ComponentName;
            public string PartPath;
            public string Configuration;
            public string FlatConfiguration;
        }

        private sealed class CircleInfo
        {
            public double X;
            public double Y;
            public double Z;
            public double RadiusM;
        }

        private sealed class CircleGroup
        {
            private readonly List<CircleInfo> circles = new List<CircleInfo>();
            public double X { get; private set; }
            public double Y { get; private set; }
            public double Z { get; private set; }
            public double AverageRadiusM { get; private set; }
            public double RadiusSpreadM { get; private set; }

            public void Add(CircleInfo circle)
            {
                circles.Add(circle);
                double totalX = 0;
                double totalY = 0;
                double totalZ = 0;
                double totalRadius = 0;
                double minRadius = double.MaxValue;
                double maxRadius = 0;
                foreach (CircleInfo item in circles)
                {
                    totalX += item.X;
                    totalY += item.Y;
                    totalZ += item.Z;
                    totalRadius += item.RadiusM;
                    minRadius = Math.Min(minRadius, item.RadiusM);
                    maxRadius = Math.Max(maxRadius, item.RadiusM);
                }
                X = totalX / circles.Count;
                Y = totalY / circles.Count;
                Z = totalZ / circles.Count;
                AverageRadiusM = totalRadius / circles.Count;
                RadiusSpreadM = Math.Max(0, maxRadius - minRadius);
            }
        }

        private sealed class LineInfo
        {
            public double Dx;
            public double Dy;
            public double Dz;
            public double Length => Math.Sqrt(Dx * Dx + Dy * Dy + Dz * Dz);
        }

        private sealed class PlaneProjector
        {
            public double OriginX;
            public double OriginY;
            public double OriginZ;
            public double Ux;
            public double Uy;
            public double Uz;
            public double Vx;
            public double Vy;
            public double Vz;

            public RoundHolePreviewPoint Project(double[] point)
            {
                if (point == null || point.Length < 3)
                    return null;
                double dx = point[0] - OriginX;
                double dy = point[1] - OriginY;
                double dz = point[2] - OriginZ;
                return new RoundHolePreviewPoint
                {
                    X = dx * Ux + dy * Uy + dz * Uz,
                    Y = dx * Vx + dy * Vy + dz * Vz,
                    ModelX = point[0],
                    ModelY = point[1],
                    ModelZ = point[2]
                };
            }
        }
    }

    public static class RoundHolePreviewDrawingAligner
    {
        public static int AlignToActiveDrawing(
            ISldWorks swApp,
            List<RoundHoleRowResult> results)
        {
            if (swApp == null || results == null)
                return 0;

            ModelDoc2 model = null;
            try { model = swApp.ActiveDoc as ModelDoc2; } catch { }
            DrawingDoc drawing = model as DrawingDoc;
            if (drawing == null)
            {
                Debug.WriteLine("[CHECK ROUND] Preview align skipped: active document is not Drawing.");
                return 0;
            }

            MathUtility mathUtility = null;
            try { mathUtility = swApp.IGetMathUtility(); } catch { }
            if (mathUtility == null)
                return 0;

            List<RoundHolePreviewData> previewData = results
                .Where(row => row != null && row.PreviewData != null)
                .Select(row => row.PreviewData)
                .Distinct()
                .ToList();
            if (previewData.Count == 0)
                return 0;

            string originalSheetName = GetCurrentSheetName(drawing);
            List<ViewCandidate> candidates = null;
            int alignedCount = 0;
            foreach (RoundHolePreviewData data in previewData)
            {
                if (TryAlignFromComponentDrawing(
                    swApp,
                    model,
                    drawing,
                    mathUtility,
                    data,
                    originalSheetName))
                {
                    alignedCount++;
                    continue;
                }

                if (candidates == null)
                    candidates = CollectViewCandidates(model, drawing);
                ViewMatch best = FindBestMatch(candidates, data.PartPath);
                if (best == null)
                {
                    data.ProjectionSource = "Mat phang cua Part (khong tim thay Drawing View)";
                    Debug.WriteLine("[CHECK ROUND] Preview align fallback. part=" + data.PartPath);
                    continue;
                }

                bool temporarilyUnsuppressed = false;
                try
                {
                    if (best.IsFlatPattern && best.IsSuppressed)
                    {
                        temporarilyUnsuppressed = TrySetViewSuppressed(
                            model,
                            drawing,
                            best,
                            false);
                        Debug.WriteLine("[CHECK ROUND] Preview temporary unsuppress. view="
                            + best.Name + ", success=" + temporarilyUnsuppressed);
                    }

                    RefreshMatchGeometry(best);
                    bool openedInPosition = TryProjectByOpenPartInPosition(
                        swApp,
                        model,
                        drawing,
                        mathUtility,
                        best,
                        data,
                        originalSheetName);

                    if (!openedInPosition && best.ViewTransform == null)
                    {
                        data.ProjectionSource = "Mat phang cua Part (khong lay duoc transform Drawing View)";
                        continue;
                    }

                    bool transformedAny = openedInPosition;
                    if (!openedInPosition)
                    {
                        foreach (RoundHolePreviewPath path in data.Paths)
                        {
                            if (path == null)
                                continue;
                            foreach (RoundHolePreviewPoint point in path.Points)
                            {
                                double[] transformed = TransformPoint(
                                    mathUtility,
                                    best.ComponentTransform,
                                    best.ViewTransform,
                                    point.ModelX,
                                    point.ModelY,
                                    point.ModelZ);
                                if (transformed == null || transformed.Length < 2)
                                    continue;
                                point.X = transformed[0];
                                point.Y = transformed[1];
                                transformedAny = true;
                            }
                        }
                    }

                    if (!transformedAny)
                    {
                        data.ProjectionSource = "Mat phang cua Part (loi bien doi Drawing View)";
                        continue;
                    }

                    data.DrawingViewName = best.Name;
                    List<RoundHolePreviewPath> drawingPaths = openedInPosition
                        ? new List<RoundHolePreviewPath>()
                        : CollectDrawingPaths(best, mathUtility);
                    if (!openedInPosition
                        && drawingPaths.Count < 3
                        && data.Paths.Count > drawingPaths.Count)
                    {
                        Debug.WriteLine("[CHECK ROUND] Preview visible edges rejected: too sparse. view="
                            + best.Name + ", paths=" + drawingPaths.Count
                            + ", modelPaths=" + data.Paths.Count);
                        drawingPaths.Clear();
                    }
                    data.DrawingPaths.Clear();
                    data.DrawingPaths.AddRange(drawingPaths);
                    data.ProjectionSource = openedInPosition
                        ? "Open Part In Position: " + best.Name
                        : (drawingPaths.Count > 0
                            ? "Drawing View: " + best.Name + " (visible edges)"
                            : "Drawing View: " + best.Name + " (model projection)");
                    alignedCount++;
                    Debug.WriteLine("[CHECK ROUND] Preview aligned. part=" + data.PartPath
                        + ", view=" + best.Name
                        + ", component=" + (best.ComponentTransform != null)
                        + ", flatPattern=" + best.IsFlatPattern
                        + ", visible=" + best.IsVisible
                        + ", openInPosition=" + openedInPosition
                        + ", visibleEdges=" + drawingPaths.Count);
                }
                finally
                {
                    if (temporarilyUnsuppressed)
                    {
                        bool restored = TrySetViewSuppressed(
                            model,
                            drawing,
                            best,
                            true);
                        Debug.WriteLine("[CHECK ROUND] Preview restore suppress. view="
                            + best.Name + ", success=" + restored);
                    }
                    RestoreSheetAndSelection(model, drawing, originalSheetName);
                }
            }

            return alignedCount;
        }

        private static bool TryAlignFromComponentDrawing(
            ISldWorks swApp,
            ModelDoc2 originalModel,
            DrawingDoc originalDrawing,
            MathUtility mathUtility,
            RoundHolePreviewData data,
            string originalSheetName)
        {
            if (swApp == null || originalModel == null || originalDrawing == null
                || mathUtility == null || data == null
                || string.IsNullOrWhiteSpace(data.PartPath))
                return false;

            string drawingPath = "";
            try { drawingPath = Path.ChangeExtension(data.PartPath, ".SLDDRW"); }
            catch { }
            if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
            {
                Debug.WriteLine("[CHECK ROUND] Component Drawing not found. part="
                    + data.PartPath + ", expected=" + drawingPath);
                return false;
            }

            ModelDoc2 existingDrawing = null;
            ModelDoc2 drawingModel = null;
            DrawingDoc componentDrawing = null;
            ViewMatch best = null;
            bool openedByAligner = false;
            bool existingVisible = false;
            bool temporarilyUnsuppressed = false;
            string componentOriginalSheet = "";
            string drawingTitle = "";
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                try { existingDrawing = swApp.GetOpenDocumentByName(drawingPath) as ModelDoc2; }
                catch { }
                if (existingDrawing != null)
                {
                    drawingModel = existingDrawing;
                    try { existingVisible = existingDrawing.Visible; } catch { }
                    int activateErrors = 0;
                    try
                    {
                        swApp.ActivateDoc3(
                            existingDrawing.GetTitle(),
                            false,
                            (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                            ref activateErrors);
                    }
                    catch { }
                }
                else
                {
                    int openErrors = 0;
                    int openWarnings = 0;
                    drawingModel = swApp.OpenDoc6(
                        drawingPath,
                        (int)swDocumentTypes_e.swDocDRAWING,
                        (int)(swOpenDocOptions_e.swOpenDocOptions_Silent
                            | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                        "",
                        ref openErrors,
                        ref openWarnings) as ModelDoc2;
                    openedByAligner = drawingModel != null;
                    Debug.WriteLine("[CHECK ROUND] Component Drawing open. path="
                        + drawingPath + ", success=" + (drawingModel != null)
                        + ", errors=" + openErrors + ", warnings=" + openWarnings);
                }

                componentDrawing = drawingModel as DrawingDoc;
                if (drawingModel == null || componentDrawing == null)
                    return false;

                try { drawingTitle = drawingModel.GetTitle() ?? ""; } catch { }
                componentOriginalSheet = GetCurrentSheetName(componentDrawing);
                List<ViewCandidate> candidates = CollectViewCandidates(
                    drawingModel,
                    componentDrawing);
                best = FindBestMatch(candidates, data.PartPath);
                if (best == null || !best.IsDirectReference)
                {
                    Debug.WriteLine("[CHECK ROUND] Component Drawing has no direct Part view. drawing="
                        + drawingPath + ", part=" + data.PartPath);
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(best.SheetName))
                {
                    try { componentDrawing.ActivateSheet(best.SheetName); } catch { }
                }
                if (best.IsSuppressed)
                {
                    temporarilyUnsuppressed = TrySetViewSuppressed(
                        drawingModel,
                        componentDrawing,
                        best,
                        false);
                    Debug.WriteLine("[CHECK ROUND] Component Drawing temporary unsuppress. view="
                        + best.Name + ", success=" + temporarilyUnsuppressed);
                }

                RefreshMatchGeometry(best);
                if (best.ViewTransform == null)
                {
                    Debug.WriteLine("[CHECK ROUND] Component Drawing view has no transform. view="
                        + best.Name + ", drawing=" + drawingPath);
                    return false;
                }

                List<ProjectedPreviewPoint> projected = new List<ProjectedPreviewPoint>();
                foreach (RoundHolePreviewPath path in data.Paths)
                {
                    if (path == null)
                        continue;
                    foreach (RoundHolePreviewPoint point in path.Points)
                    {
                        double[] transformed = TransformPoint(
                            mathUtility,
                            best.ComponentTransform,
                            best.ViewTransform,
                            point.ModelX,
                            point.ModelY,
                            point.ModelZ);
                        if (transformed == null || transformed.Length < 2)
                            continue;
                        projected.Add(new ProjectedPreviewPoint
                        {
                            Point = point,
                            X = transformed[0],
                            Y = transformed[1]
                        });
                    }
                }

                int expectedPointCount = data.Paths.Sum(
                    path => path == null ? 0 : path.Points.Count);
                if (expectedPointCount == 0 || projected.Count != expectedPointCount)
                {
                    Debug.WriteLine("[CHECK ROUND] Component Drawing projection incomplete. part="
                        + data.PartPath + ", projected=" + projected.Count
                        + "/" + expectedPointCount);
                    return false;
                }

                List<RoundHolePreviewPath> drawingPaths = CollectDrawingPaths(
                    best,
                    mathUtility);
                if (drawingPaths.Count < 3)
                {
                    Debug.WriteLine("[CHECK ROUND] Component Drawing visible edges too sparse. view="
                        + best.Name + ", paths=" + drawingPaths.Count);
                    drawingPaths.Clear();
                }

                foreach (ProjectedPreviewPoint item in projected)
                {
                    item.Point.X = item.X;
                    item.Point.Y = item.Y;
                }
                data.DrawingPaths.Clear();
                data.DrawingPaths.AddRange(drawingPaths);
                data.DrawingViewName = best.Name;
                data.ProjectionSource = "Component Drawing: " + best.Name;

                Debug.WriteLine("[CHECK ROUND] Component Drawing aligned. part="
                    + data.PartPath + ", drawing=" + drawingPath
                    + ", sheet=" + best.SheetName + ", view=" + best.Name
                    + ", config=" + data.Configuration
                    + ", flatPattern=" + best.IsFlatPattern
                    + ", suppressed=" + best.IsSuppressed
                    + ", visibleEdges=" + drawingPaths.Count
                    + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Component Drawing align ERROR. part="
                    + data.PartPath + ", drawing=" + drawingPath + ", error=" + ex);
                return false;
            }
            finally
            {
                if (temporarilyUnsuppressed && best != null
                    && drawingModel != null && componentDrawing != null)
                {
                    bool restored = TrySetViewSuppressed(
                        drawingModel,
                        componentDrawing,
                        best,
                        true);
                    Debug.WriteLine("[CHECK ROUND] Component Drawing restore suppress. view="
                        + best.Name + ", success=" + restored);
                }

                if (drawingModel != null && componentDrawing != null)
                {
                    RestoreSheetAndSelection(
                        drawingModel,
                        componentDrawing,
                        componentOriginalSheet);
                }

                int activateErrors = 0;
                try
                {
                    swApp.ActivateDoc3(
                        originalModel.GetTitle(),
                        false,
                        (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                        ref activateErrors);
                }
                catch { }
                RestoreSheetAndSelection(
                    originalModel,
                    originalDrawing,
                    originalSheetName);

                if (openedByAligner && !string.IsNullOrWhiteSpace(drawingTitle))
                {
                    try
                    {
                        swApp.CloseDoc(drawingTitle);
                        Debug.WriteLine("[CHECK ROUND] Component Drawing close without save. title="
                            + drawingTitle);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK ROUND] Component Drawing close ERROR. title="
                            + drawingTitle + ", error=" + ex.Message);
                    }
                }
                else if (existingDrawing != null && !existingVisible)
                {
                    try { existingDrawing.Visible = false; } catch { }
                }

                Debug.WriteLine("[CHECK ROUND] Component Drawing finished. part="
                    + data.PartPath + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
            }
        }

        private static List<ViewCandidate> CollectViewCandidates(
            ModelDoc2 model,
            DrawingDoc drawing)
        {
            List<ViewCandidate> candidates = new List<ViewCandidate>();
            string currentSheetName = GetCurrentSheetName(drawing);
            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Array sheetGroups = drawing.GetViews() as Array;
                if (sheetGroups != null)
                {
                    foreach (object groupObject in sheetGroups)
                    {
                        Array views = groupObject as Array;
                        if (views == null || views.Length == 0)
                            continue;

                        SolidWorks.Interop.sldworks.View sheetView =
                            views.GetValue(0) as SolidWorks.Interop.sldworks.View;
                        string sheetName = GetViewName(sheetView);
                        for (int index = 1; index < views.Length; index++)
                        {
                            AddViewCandidate(
                                candidates,
                                added,
                                views.GetValue(index) as SolidWorks.Interop.sldworks.View,
                                sheetName,
                                currentSheetName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Preview GetViews ERROR: " + ex.Message);
            }

            // GetViews() does not always return views suppressed in the drawing.
            // Drawing-view features are still available through the model feature tree.
            CollectViewCandidatesFromAllSheets(
                model,
                drawing,
                candidates,
                added,
                currentSheetName);

            if (candidates.Count > 0)
                return candidates;

            SolidWorks.Interop.sldworks.View view = null;
            try { view = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View; } catch { }
            if (view != null)
            {
                try { view = view.GetNextView() as SolidWorks.Interop.sldworks.View; }
                catch { view = null; }
            }

            while (view != null)
            {
                AddViewCandidate(candidates, added, view, currentSheetName, currentSheetName);

                try { view = view.GetNextView() as SolidWorks.Interop.sldworks.View; }
                catch { view = null; }
            }
            return candidates;
        }

        private static void CollectViewCandidatesFromAllSheets(
            ModelDoc2 model,
            DrawingDoc drawing,
            List<ViewCandidate> candidates,
            HashSet<string> added,
            string originalSheetName)
        {
            if (model == null || drawing == null)
                return;

            Array sheetNames = null;
            try { sheetNames = drawing.GetSheetNames() as Array; } catch { }
            if (sheetNames == null || sheetNames.Length == 0)
            {
                CollectViewCandidatesFromFeatures(
                    model,
                    candidates,
                    added,
                    originalSheetName);
                return;
            }

            try
            {
                foreach (object sheetObject in sheetNames)
                {
                    string sheetName = Convert.ToString(sheetObject) ?? "";
                    if (string.IsNullOrWhiteSpace(sheetName))
                        continue;

                    bool activated = false;
                    try { activated = drawing.ActivateSheet(sheetName); } catch { }
                    if (!activated)
                    {
                        Debug.WriteLine("[CHECK ROUND] Preview sheet activate failed. sheet="
                            + sheetName);
                        continue;
                    }

                    int beforeCount = candidates.Count;
                    SolidWorks.Interop.sldworks.View sheetView = null;
                    try { sheetView = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View; }
                    catch { }
                    SolidWorks.Interop.sldworks.View view = null;
                    try
                    {
                        view = sheetView == null
                            ? null
                            : sheetView.GetNextView() as SolidWorks.Interop.sldworks.View;
                    }
                    catch { }
                    while (view != null)
                    {
                        AddViewCandidate(
                            candidates,
                            added,
                            view,
                            sheetName,
                            originalSheetName);
                        try { view = view.GetNextView() as SolidWorks.Interop.sldworks.View; }
                        catch { view = null; }
                    }

                    // On some drawings a suppressed view is only exposed by the
                    // feature tree after its sheet becomes active.
                    CollectViewCandidatesFromFeatures(
                        model,
                        candidates,
                        added,
                        originalSheetName);
                    Debug.WriteLine("[CHECK ROUND] Preview sheet scanned. sheet="
                        + sheetName + ", added=" + (candidates.Count - beforeCount));
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalSheetName))
                {
                    try { drawing.ActivateSheet(originalSheetName); } catch { }
                }
                try { model.ClearSelection2(true); } catch { }
            }
        }

        private static void CollectViewCandidatesFromFeatures(
            ModelDoc2 model,
            List<ViewCandidate> candidates,
            HashSet<string> added,
            string currentSheetName)
        {
            if (model == null)
                return;

            Feature feature = null;
            try { feature = model.IFirstFeature(); } catch { }
            while (feature != null)
            {
                CollectViewCandidateFromFeature(
                    feature,
                    model,
                    candidates,
                    added,
                    currentSheetName);
                try { feature = feature.GetNextFeature() as Feature; }
                catch { feature = null; }
            }
        }

        private static void CollectViewCandidateFromFeature(
            Feature feature,
            ModelDoc2 model,
            List<ViewCandidate> candidates,
            HashSet<string> added,
            string currentSheetName)
        {
            if (feature == null)
                return;

            try
            {
                SolidWorks.Interop.sldworks.View view =
                    feature.GetSpecificFeature2() as SolidWorks.Interop.sldworks.View;
                if (view == null && model != null)
                {
                    // A suppressed Drawing View can return null from
                    // Feature.GetSpecificFeature2. Resolve it through selection.
                    view = ResolveDrawingViewByTreeName(model, feature.Name);
                    if (view != null)
                    {
                        Debug.WriteLine("[CHECK ROUND] Preview suppressed view resolved by name. feature="
                            + feature.Name + ", view=" + GetViewName(view));
                    }
                }
                if (view != null)
                {
                    string sheetName = GetViewSheetName(view);
                    AddViewCandidate(
                        candidates,
                        added,
                        view,
                        sheetName,
                        currentSheetName);
                    Debug.WriteLine("[CHECK ROUND] Preview feature view. feature="
                        + feature.Name + ", sheet=" + sheetName
                        + ", view=" + GetViewName(view)
                        + ", suppressState=" + GetViewSuppressState(view));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Preview feature view ERROR. feature="
                    + SafeFeatureName(feature) + ", error=" + ex.Message);
            }

            Feature subFeature = null;
            try { subFeature = feature.GetFirstSubFeature() as Feature; } catch { }
            while (subFeature != null)
            {
                CollectViewCandidateFromFeature(
                    subFeature,
                    model,
                    candidates,
                    added,
                    currentSheetName);
                try { subFeature = subFeature.GetNextSubFeature() as Feature; }
                catch { subFeature = null; }
            }
        }

        private static SolidWorks.Interop.sldworks.View ResolveDrawingViewByTreeName(
            ModelDoc2 model,
            string featureName)
        {
            if (model == null || string.IsNullOrWhiteSpace(featureName))
                return null;
            try
            {
                model.ClearSelection2(true);
                bool selected = model.Extension.SelectByID2(
                    featureName,
                    "DRAWINGVIEW",
                    0.0,
                    0.0,
                    0.0,
                    false,
                    0,
                    null,
                    0);
                if (!selected)
                    return null;
                SelectionMgr selectionManager = model.SelectionManager as SelectionMgr;
                return selectionManager == null
                    ? null
                    : selectionManager.GetSelectedObject6(1, -1)
                        as SolidWorks.Interop.sldworks.View;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }
        }

        private static string SafeFeatureName(Feature feature)
        {
            try { return feature == null ? "" : feature.Name ?? ""; }
            catch { return ""; }
        }

        private static string GetViewSheetName(SolidWorks.Interop.sldworks.View view)
        {
            try
            {
                Sheet sheet = view == null ? null : view.Sheet;
                return sheet == null ? "" : sheet.GetName();
            }
            catch
            {
                return "";
            }
        }

        private static void AddViewCandidate(
            List<ViewCandidate> candidates,
            HashSet<string> added,
            SolidWorks.Interop.sldworks.View view,
            string sheetName,
            string currentSheetName)
        {
            if (view == null)
                return;
            try
            {
                string name = GetViewName(view);
                string key = (sheetName ?? "") + "|" + name;
                if (!added.Add(key))
                    return;

                string referencedPath = "";
                try { referencedPath = view.GetReferencedModelName() ?? ""; } catch { }
                MathTransform viewTransform = null;
                try { viewTransform = view.ModelToViewTransform; } catch { }
                ViewCandidate candidate = new ViewCandidate
                {
                    View = view,
                    Name = name,
                    SheetName = sheetName ?? "",
                    ReferencedPath = referencedPath,
                    ViewTransform = viewTransform,
                    OutlineArea = GetOutlineArea(view),
                    IsVisible = GetViewVisible(view),
                    IsSuppressed = GetViewSuppressState(view) == 2,
                    IsFlatPattern = IsFlatPatternView(view),
                    IsCurrentSheet = string.Equals(
                        sheetName ?? "",
                        currentSheetName ?? "",
                        StringComparison.OrdinalIgnoreCase)
                };
                candidates.Add(candidate);
                Debug.WriteLine("[CHECK ROUND] Preview candidate. sheet=" + candidate.SheetName
                    + ", view=" + candidate.Name
                    + ", flatPattern=" + candidate.IsFlatPattern
                    + ", visible=" + candidate.IsVisible
                    + ", suppressed=" + candidate.IsSuppressed
                    + ", currentSheet=" + candidate.IsCurrentSheet
                    + ", ref=" + candidate.ReferencedPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Preview collect view ERROR: " + ex.Message);
            }
        }

        private static ViewMatch FindBestMatch(
            List<ViewCandidate> candidates,
            string partPath)
        {
            ViewMatch best = null;
            foreach (ViewCandidate candidate in candidates)
            {
                if (PathsEqual(candidate.ReferencedPath, partPath))
                {
                    DrawingComponent directRoot = null;
                    try { directRoot = candidate.View.RootDrawingComponent; } catch { }
                    DrawingComponent directDrawingComponent = FindDrawingComponent(directRoot, partPath);
                    Component2 directComponent = null;
                    try
                    {
                        directComponent = directDrawingComponent == null
                            ? null
                            : directDrawingComponent.Component;
                    }
                    catch { }
                    MathTransform directComponentTransform = GetComponentTransform(directComponent);
                    ViewMatch direct = new ViewMatch
                    {
                        Name = candidate.Name,
                        SheetName = candidate.SheetName,
                        View = candidate.View,
                        ViewTransform = candidate.ViewTransform,
                        Component = directComponent,
                        DrawingComponent = directDrawingComponent,
                        ComponentTransform = directComponentTransform,
                        OutlineArea = candidate.OutlineArea,
                        IsVisible = candidate.IsVisible,
                        IsSuppressed = candidate.IsSuppressed,
                        IsFlatPattern = candidate.IsFlatPattern,
                        IsCurrentSheet = candidate.IsCurrentSheet,
                        IsDirectReference = true
                    };
                    if (IsBetter(direct, best))
                        best = direct;
                }

                DrawingComponent root = null;
                try { root = candidate.View.RootDrawingComponent; } catch { }
                DrawingComponent matchedDrawingComponent = FindDrawingComponent(root, partPath);
                Component2 component = null;
                try
                {
                    component = matchedDrawingComponent == null
                        ? null
                        : matchedDrawingComponent.Component;
                }
                catch { }
                if (component == null)
                    continue;

                MathTransform componentTransform = GetComponentTransform(component);

                ViewMatch assembly = new ViewMatch
                {
                    Name = candidate.Name,
                    SheetName = candidate.SheetName,
                    View = candidate.View,
                    ViewTransform = candidate.ViewTransform,
                    Component = component,
                    DrawingComponent = matchedDrawingComponent,
                    ComponentTransform = componentTransform,
                    OutlineArea = candidate.OutlineArea,
                    IsVisible = candidate.IsVisible,
                    IsSuppressed = candidate.IsSuppressed,
                    IsFlatPattern = candidate.IsFlatPattern,
                    IsCurrentSheet = candidate.IsCurrentSheet,
                    IsDirectReference = false
                };
                if (IsBetter(assembly, best))
                    best = assembly;
            }
            return best;
        }

        private static MathTransform GetComponentTransform(Component2 component)
        {
            if (component == null)
                return null;
            MathTransform transform = null;
            try { transform = component.GetTotalTransform(false); } catch { }
            if (transform == null)
            {
                try { transform = component.Transform2; } catch { }
            }
            return transform;
        }

        private static bool TryProjectByOpenPartInPosition(
            ISldWorks swApp,
            ModelDoc2 drawingModel,
            DrawingDoc drawing,
            MathUtility mathUtility,
            ViewMatch match,
            RoundHolePreviewData data,
            string originalSheetName)
        {
            if (swApp == null || drawingModel == null || drawing == null
                || mathUtility == null || match == null || match.View == null
                || data == null || string.IsNullOrWhiteSpace(data.PartPath))
                return false;

            ModelDoc2 existingPart = null;
            ModelDoc2 openedPart = null;
            bool existingVisible = false;
            bool openedByCommand = false;
            string openedTitle = "";
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                try { existingPart = swApp.GetOpenDocumentByName(data.PartPath) as ModelDoc2; } catch { }
                if (existingPart != null)
                {
                    try { existingVisible = existingPart.Visible; } catch { }
                }

                if (!string.IsNullOrWhiteSpace(match.SheetName))
                    drawing.ActivateSheet(match.SheetName);
                drawingModel.ClearSelection2(true);

                bool selected = false;
                if (match.DrawingComponent != null)
                {
                    try { selected = match.DrawingComponent.Select(false, null); } catch { }
                }

                if (!selected && match.View != null)
                {
                    Array edges = null;
                    try
                    {
                        edges = match.View.GetVisibleEntities2(
                            match.Component,
                            (int)swViewEntityType_e.swViewEntityType_Edge) as Array;
                    }
                    catch { }
                    if (edges != null && edges.Length > 0)
                    {
                        try { selected = match.View.SelectEntity(edges.GetValue(0), false); }
                        catch { }
                    }
                }

                if (!selected)
                {
                    try
                    {
                        selected = drawingModel.Extension.SelectByID2(
                            match.Name,
                            "DRAWINGVIEW",
                            0.0,
                            0.0,
                            0.0,
                            false,
                            0,
                            null,
                            0);
                    }
                    catch { }
                }

                if (!selected)
                {
                    Debug.WriteLine("[CHECK ROUND] Open In Position skipped: khong chon duoc Drawing Component. view="
                        + match.Name + ", part=" + data.PartPath);
                    return false;
                }

                const int OpenPartFromDrawingCommandId = 3008;
                bool commandRan = false;
                try
                {
                    commandRan = swApp.RunCommand(OpenPartFromDrawingCommandId, "");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[CHECK ROUND] Open In Position command ERROR. view="
                        + match.Name + ", error=" + ex.Message);
                }
                if (!commandRan)
                {
                    Debug.WriteLine("[CHECK ROUND] Open In Position command disabled/failed. view="
                        + match.Name + ", part=" + data.PartPath);
                    return false;
                }

                DateTime waitUntil = DateTime.UtcNow.AddSeconds(5.0);
                while (DateTime.UtcNow < waitUntil)
                {
                    Application.DoEvents();
                    try { openedPart = swApp.ActiveDoc as ModelDoc2; } catch { }
                    if (openedPart != null)
                    {
                        string activePath = "";
                        int activeType = -1;
                        try { activePath = openedPart.GetPathName() ?? ""; } catch { }
                        try { activeType = openedPart.GetType(); } catch { }
                        if (activeType == (int)swDocumentTypes_e.swDocPART
                            && PathsEqual(activePath, data.PartPath))
                            break;
                    }
                    openedPart = null;
                    System.Threading.Thread.Sleep(20);
                }

                if (openedPart == null)
                {
                    Debug.WriteLine("[CHECK ROUND] Open In Position did not activate target part. view="
                        + match.Name + ", part=" + data.PartPath);
                    return false;
                }

                openedByCommand = existingPart == null;
                try { openedTitle = openedPart.GetTitle() ?? ""; } catch { }
                string activeConfiguration = "";
                try
                {
                    Configuration configuration = openedPart.ConfigurationManager.ActiveConfiguration;
                    activeConfiguration = configuration == null ? "" : configuration.Name ?? "";
                }
                catch { }

                ModelView modelView = null;
                MathTransform modelViewTransform = null;
                try { modelView = openedPart.ActiveView as ModelView; } catch { }
                try { modelViewTransform = modelView == null ? null : modelView.Transform; } catch { }
                if (modelViewTransform == null)
                {
                    Debug.WriteLine("[CHECK ROUND] Open In Position: khong lay duoc ModelView.Transform. part="
                        + data.PartPath);
                    return false;
                }

                List<ProjectedPreviewPoint> projected = new List<ProjectedPreviewPoint>();
                foreach (RoundHolePreviewPath path in data.Paths)
                {
                    if (path == null)
                        continue;
                    foreach (RoundHolePreviewPoint point in path.Points)
                    {
                        double[] transformed = TransformPoint(
                            mathUtility,
                            null,
                            modelViewTransform,
                            point.ModelX,
                            point.ModelY,
                            point.ModelZ);
                        if (transformed == null || transformed.Length < 2)
                            continue;
                        projected.Add(new ProjectedPreviewPoint
                        {
                            Point = point,
                            X = transformed[0],
                            Y = transformed[1]
                        });
                    }
                }

                int expectedPointCount = data.Paths.Sum(path => path == null ? 0 : path.Points.Count);
                if (expectedPointCount == 0 || projected.Count != expectedPointCount)
                {
                    Debug.WriteLine("[CHECK ROUND] Open In Position projection incomplete. part="
                        + data.PartPath + ", projected=" + projected.Count
                        + "/" + expectedPointCount);
                    return false;
                }

                double sourceAspect = GetPreviewAspect(data.Paths);
                double projectedAspect = GetProjectedAspect(projected);
                if (sourceAspect > 0.0
                    && projectedAspect > 0.0
                    && projectedAspect < sourceAspect * 0.25)
                {
                    Debug.WriteLine("[CHECK ROUND] Open In Position projection rejected: collapsed. part="
                        + data.PartPath + ", sourceAspect="
                        + sourceAspect.ToString("0.####", CultureInfo.InvariantCulture)
                        + ", projectedAspect="
                        + projectedAspect.ToString("0.####", CultureInfo.InvariantCulture));
                    return false;
                }

                foreach (ProjectedPreviewPoint item in projected)
                {
                    item.Point.X = item.X;
                    item.Point.Y = item.Y;
                }

                Debug.WriteLine("[CHECK ROUND] Open In Position projected. part="
                    + data.PartPath + ", view=" + match.Name
                    + ", config=" + activeConfiguration
                    + ", wasOpen=" + (existingPart != null)
                    + ", sourceAspect=" + sourceAspect.ToString("0.####", CultureInfo.InvariantCulture)
                    + ", projectedAspect=" + projectedAspect.ToString("0.####", CultureInfo.InvariantCulture)
                    + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Open In Position ERROR. part="
                    + data.PartPath + ", error=" + ex);
                return false;
            }
            finally
            {
                int activateErrors = 0;
                try
                {
                    swApp.ActivateDoc3(
                        drawingModel.GetTitle(),
                        false,
                        (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                        ref activateErrors);
                }
                catch { }

                RestoreSheetAndSelection(
                    drawingModel,
                    drawing,
                    originalSheetName);

                if (openedByCommand && !string.IsNullOrWhiteSpace(openedTitle))
                {
                    try
                    {
                        swApp.CloseDoc(openedTitle);
                        Debug.WriteLine("[CHECK ROUND] Open In Position close without save. title="
                            + openedTitle);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK ROUND] Open In Position close ERROR. title="
                            + openedTitle + ", error=" + ex.Message);
                    }
                }
                else if (existingPart != null && !existingVisible)
                {
                    try { existingPart.Visible = false; } catch { }
                }

                try { drawingModel.ClearSelection2(true); } catch { }
                Debug.WriteLine("[CHECK ROUND] Open In Position finished. part="
                    + data.PartPath + ", elapsedMs=" + stopwatch.ElapsedMilliseconds);
            }
        }

        private static double GetPreviewAspect(List<RoundHolePreviewPath> paths)
        {
            if (paths == null)
                return 0.0;
            List<double[]> points = new List<double[]>();
            foreach (RoundHolePreviewPath path in paths)
            {
                if (path == null)
                    continue;
                foreach (RoundHolePreviewPoint point in path.Points)
                    points.Add(new[] { point.X, point.Y });
            }
            return GetAspect(points);
        }

        private static double GetProjectedAspect(List<ProjectedPreviewPoint> points)
        {
            if (points == null)
                return 0.0;
            return GetAspect(points.Select(point => new[] { point.X, point.Y }));
        }

        private static double GetAspect(IEnumerable<double[]> points)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            int count = 0;
            foreach (double[] point in points)
            {
                if (point == null || point.Length < 2)
                    continue;
                minX = Math.Min(minX, point[0]);
                minY = Math.Min(minY, point[1]);
                maxX = Math.Max(maxX, point[0]);
                maxY = Math.Max(maxY, point[1]);
                count++;
            }
            if (count < 2)
                return 0.0;
            double width = Math.Abs(maxX - minX);
            double height = Math.Abs(maxY - minY);
            double longest = Math.Max(width, height);
            return longest <= 1e-12 ? 0.0 : Math.Min(width, height) / longest;
        }

        private sealed class ProjectedPreviewPoint
        {
            public RoundHolePreviewPoint Point;
            public double X;
            public double Y;
        }

        private static void RefreshMatchGeometry(ViewMatch match)
        {
            if (match == null || match.View == null)
                return;
            try { match.ViewTransform = match.View.ModelToViewTransform; } catch { }
            match.IsVisible = GetViewVisible(match.View);
            match.IsSuppressed = GetViewSuppressState(match.View) == 2;
            if (match.Component != null)
                match.ComponentTransform = GetComponentTransform(match.Component);
        }

        private static bool TrySetViewSuppressed(
            ModelDoc2 model,
            DrawingDoc drawing,
            ViewMatch match,
            bool suppress)
        {
            if (model == null || drawing == null || match == null
                || string.IsNullOrWhiteSpace(match.Name))
                return false;
            try
            {
                int desiredState = suppress ? 2 : 0;
                try
                {
                    match.View.SuppressState = desiredState;
                    Application.DoEvents();
                    RefreshMatchGeometry(match);
                    if (GetViewSuppressState(match.View) == desiredState)
                        return true;
                }
                catch (Exception directEx)
                {
                    Debug.WriteLine("[CHECK ROUND] Preview direct suppression ERROR. view="
                        + match.Name + ", suppress=" + suppress
                        + ", error=" + directEx.Message);
                }

                if (!string.IsNullOrWhiteSpace(match.SheetName))
                    drawing.ActivateSheet(match.SheetName);
                model.ClearSelection2(true);
                bool selected = model.Extension.SelectByID2(
                    match.Name,
                    "DRAWINGVIEW",
                    0.0,
                    0.0,
                    0.0,
                    false,
                    0,
                    null,
                    0);
                if (!selected)
                    return false;

                if (suppress)
                    drawing.SuppressView();
                else
                    drawing.UnsuppressView();

                Application.DoEvents();
                RefreshMatchGeometry(match);
                return GetViewSuppressState(match.View) == desiredState;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Preview set suppression ERROR. view="
                    + match.Name + ", suppress=" + suppress + ", error=" + ex.Message);
                return false;
            }
        }

        private static string GetCurrentSheetName(DrawingDoc drawing)
        {
            try
            {
                Sheet sheet = drawing == null ? null : drawing.GetCurrentSheet() as Sheet;
                return sheet == null ? "" : sheet.GetName();
            }
            catch
            {
                return "";
            }
        }

        private static void RestoreSheetAndSelection(
            ModelDoc2 model,
            DrawingDoc drawing,
            string originalSheetName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(originalSheetName))
                    drawing.ActivateSheet(originalSheetName);
            }
            catch { }
            try { model.ClearSelection2(true); } catch { }
        }

        private static List<RoundHolePreviewPath> CollectDrawingPaths(
            ViewMatch match,
            MathUtility mathUtility)
        {
            List<RoundHolePreviewPath> paths = new List<RoundHolePreviewPath>();
            if (match == null || match.View == null || match.ViewTransform == null)
                return paths;

            HashSet<Edge> usedEdges = new HashSet<Edge>();
            int[] entityTypes =
            {
                (int)swViewEntityType_e.swViewEntityType_Edge,
                (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge
            };
            foreach (int entityType in entityTypes)
            {
                Array entities = null;
                try
                {
                    entities = match.View.GetVisibleEntities2(
                        match.Component,
                        entityType) as Array;
                }
                catch { }
                if (entities == null)
                    continue;

                foreach (object entityObject in entities)
                {
                    Edge edge = entityObject as Edge;
                    if (edge == null || usedEdges.Contains(edge))
                        continue;
                    usedEdges.Add(edge);

                    Curve curve = null;
                    try { curve = edge.GetCurve() as Curve; } catch { }
                    if (curve == null)
                        continue;
                    List<double[]> modelPoints = SampleDrawingEdge(edge, curve);
                    if (modelPoints.Count < 2)
                        continue;

                    RoundHolePreviewPath path = new RoundHolePreviewPath();
                    foreach (double[] modelPoint in modelPoints)
                    {
                        double[] drawingPoint = TransformPoint(
                            mathUtility,
                            match.ComponentTransform,
                            match.ViewTransform,
                            modelPoint[0],
                            modelPoint[1],
                            modelPoint[2]);
                        if (drawingPoint == null || drawingPoint.Length < 2)
                            continue;
                        path.Points.Add(new RoundHolePreviewPoint
                        {
                            X = drawingPoint[0],
                            Y = drawingPoint[1],
                            ModelX = modelPoint[0],
                            ModelY = modelPoint[1],
                            ModelZ = modelPoint[2]
                        });
                    }
                    if (path.Points.Count >= 2)
                        paths.Add(path);
                }
            }
            return paths;
        }

        private static List<double[]> SampleDrawingEdge(Edge edge, Curve curve)
        {
            List<double[]> points = new List<double[]>();
            double start;
            double end;
            bool closed = false;
            try
            {
                CurveParamData edgeParams = edge.GetCurveParams3();
                if (edgeParams == null)
                    return points;
                start = edgeParams.UMinValue;
                end = edgeParams.UMaxValue;
                double[] first = edgeParams.StartPoint as double[];
                double[] last = edgeParams.EndPoint as double[];
                closed = PointsNear(first, last);
            }
            catch
            {
                return points;
            }

            int sampleCount = 20;
            try
            {
                if (curve.IsLine())
                    sampleCount = 1;
                else if (curve.IsCircle())
                    sampleCount = closed ? 48 : 24;
            }
            catch { }

            for (int i = 0; i <= sampleCount; i++)
            {
                double parameter = start + (end - start) * i / sampleCount;
                double[] value = null;
                try { value = curve.Evaluate(parameter) as double[]; } catch { }
                if (value != null && value.Length >= 3)
                    points.Add(new[] { value[0], value[1], value[2] });
            }
            return points;
        }

        private static bool PointsNear(double[] first, double[] second)
        {
            if (first == null || second == null || first.Length < 3 || second.Length < 3)
                return false;
            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            double dz = first[2] - second[2];
            return dx * dx + dy * dy + dz * dz <= 1e-16;
        }

        private static bool IsBetter(ViewMatch candidate, ViewMatch current)
        {
            if (candidate == null)
                return false;
            if (current == null)
                return true;
            // The checked coordinates belong to SM-FLAT-PATTERN. A suppressed
            // Flat-Pattern view is still a better transform source than a visible
            // folded/side view, otherwise all hole points can collapse to a line.
            if (candidate.IsFlatPattern != current.IsFlatPattern)
                return candidate.IsFlatPattern;
            if (candidate.IsDirectReference != current.IsDirectReference)
                return candidate.IsDirectReference;
            if (candidate.IsCurrentSheet != current.IsCurrentSheet)
                return candidate.IsCurrentSheet;
            if (candidate.IsVisible != current.IsVisible)
                return candidate.IsVisible;
            return candidate.OutlineArea > current.OutlineArea;
        }

        private static bool IsFlatPatternView(SolidWorks.Interop.sldworks.View view)
        {
            if (view == null)
                return false;
            try
            {
                if (view.IsFlatPatternView())
                    return true;
            }
            catch { }
            try
            {
                string configuration = view.ReferencedConfiguration ?? "";
                return configuration.IndexOf(
                    "FLAT-PATTERN",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static DrawingComponent FindDrawingComponent(
            DrawingComponent drawingComponent,
            string partPath)
        {
            if (drawingComponent == null)
                return null;
            Component2 component = null;
            try { component = drawingComponent.Component; } catch { }
            string path = "";
            try { path = component == null ? "" : component.GetPathName() ?? ""; } catch { }
            if (PathsEqual(path, partPath))
                return drawingComponent;

            Array children = null;
            try { children = drawingComponent.GetChildren() as Array; } catch { }
            if (children == null)
                return null;
            foreach (object childObject in children)
            {
                DrawingComponent found = FindDrawingComponent(
                    childObject as DrawingComponent,
                    partPath);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static double[] TransformPoint(
            MathUtility mathUtility,
            MathTransform componentTransform,
            MathTransform viewTransform,
            double x,
            double y,
            double z)
        {
            try
            {
                MathPoint point = mathUtility.CreatePoint(new[] { x, y, z }) as MathPoint;
                if (point == null)
                    return null;
                if (componentTransform != null)
                    point = point.MultiplyTransform(componentTransform) as MathPoint;
                if (point == null)
                    return null;
                point = point.MultiplyTransform(viewTransform) as MathPoint;
                return point == null ? null : point.ArrayData as double[];
            }
            catch
            {
                return null;
            }
        }

        private static bool GetViewVisible(SolidWorks.Interop.sldworks.View view)
        {
            int suppressState = GetViewSuppressState(view);
            if (suppressState == 2)
                return false;
            try { return view.GetVisible(); } catch { return false; }
        }

        private static int GetViewSuppressState(SolidWorks.Interop.sldworks.View view)
        {
            try { return view == null ? -1 : view.SuppressState; }
            catch { return -1; }
        }

        private static double GetOutlineArea(SolidWorks.Interop.sldworks.View view)
        {
            try
            {
                double[] outline = view.GetOutline() as double[];
                if (outline == null || outline.Length < 4)
                    return 0.0;
                return Math.Abs((outline[2] - outline[0]) * (outline[3] - outline[1]));
            }
            catch
            {
                return 0.0;
            }
        }

        private static string GetViewName(SolidWorks.Interop.sldworks.View view)
        {
            try { return Convert.ToString(((dynamic)view).Name ?? ""); } catch { }
            try { return Convert.ToString(((dynamic)view).GetName2() ?? ""); } catch { }
            return "";
        }

        private static bool PathsEqual(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
                return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(first).TrimEnd('\\'),
                    Path.GetFullPath(second).TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class ViewCandidate
        {
            public SolidWorks.Interop.sldworks.View View;
            public string Name;
            public string SheetName;
            public string ReferencedPath;
            public MathTransform ViewTransform;
            public double OutlineArea;
            public bool IsVisible;
            public bool IsSuppressed;
            public bool IsFlatPattern;
            public bool IsCurrentSheet;
        }

        private sealed class ViewMatch
        {
            public string Name;
            public string SheetName;
            public SolidWorks.Interop.sldworks.View View;
            public MathTransform ViewTransform;
            public Component2 Component;
            public DrawingComponent DrawingComponent;
            public MathTransform ComponentTransform;
            public double OutlineArea;
            public bool IsVisible;
            public bool IsSuppressed;
            public bool IsFlatPattern;
            public bool IsCurrentSheet;
            public bool IsDirectReference;
        }
    }

    public static class RoundHoleDrawingHighlighter
    {
        private const double MaximumCenterDistanceM = 0.002;
        private const string MarkerLayerName = "TAI_CHECK_ROUND";

        public static int Highlight(ISldWorks swApp, List<RoundHoleRowResult> results)
        {
            if (swApp == null || results == null)
                return 0;

            ModelDoc2 model = swApp.ActiveDoc as ModelDoc2;
            DrawingDoc drawing = model as DrawingDoc;
            if (model == null || drawing == null)
            {
                Debug.WriteLine("[CHECK ROUND] Highlight skipped: active document is not Drawing.");
                return 0;
            }

            List<RoundHoleRowResult> abnormal = new List<RoundHoleRowResult>();
            foreach (RoundHoleRowResult row in results)
            {
                if (row != null && (row.Status == "NG" || row.Status == "CHECK")
                    && row.CenterModelX.HasValue && row.CenterModelY.HasValue && row.CenterModelZ.HasValue)
                {
                    abnormal.Add(row);
                }
            }
            if (abnormal.Count == 0)
                return 0;

            MathUtility mathUtility = null;
            try { mathUtility = swApp.IGetMathUtility(); } catch { }
            if (mathUtility == null)
                return 0;

            int highlightedHoles = 0;
            int markerIndex = 0;
            string originalSheetName = GetCurrentSheetName(drawing);
            EnsureMarkerLayer(drawing);
            DeleteOldMarkers(model, drawing);

            SelectionMgr selectionManager = null;
            try { selectionManager = model.SelectionManager as SelectionMgr; } catch { }
            Debug.WriteLine("[CHECK ROUND] Highlight start. abnormal=" + abnormal.Count
                + ", selectionManager=" + (selectionManager != null));

            object[] sheetNames = GetSheetNames(drawing);
            foreach (object sheetNameObject in sheetNames)
            {
                string sheetName = Convert.ToString(sheetNameObject) ?? "";
                if (sheetName.Length == 0)
                    continue;
                try { drawing.ActivateSheet(sheetName); } catch { continue; }

                SolidWorks.Interop.sldworks.View view = null;
                try { view = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View; } catch { }
                if (view != null)
                {
                    try { view = view.GetNextView() as SolidWorks.Interop.sldworks.View; }
                    catch { view = null; }
                }

                while (view != null)
                {
                    try
                    {
                        MapAndHighlightView(
                            model,
                            view,
                            sheetName,
                            abnormal,
                            mathUtility,
                            selectionManager,
                            ref highlightedHoles,
                            ref markerIndex);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK ROUND] Highlight view ERROR: " + ex.Message);
                    }

                    try { view = view.GetNextView() as SolidWorks.Interop.sldworks.View; }
                    catch { view = null; }
                }
            }

            if (!string.IsNullOrWhiteSpace(originalSheetName))
            {
                try { drawing.ActivateSheet(originalSheetName); } catch { }
            }

            foreach (RoundHoleRowResult row in abnormal)
            {
                if (string.IsNullOrWhiteSpace(row.ViewName))
                    row.Note = (row.Note ?? "")
                        + " Khong tim thay Flat-Pattern Drawing View phu hop de gan Balloon.";
            }

            try
            {
                model.ClearSelection2(true);
                model.GraphicsRedraw2();
            }
            catch { }

            Debug.WriteLine("[CHECK ROUND] Highlight completed. holes=" + highlightedHoles);
            return highlightedHoles;
        }

        private static void MapAndHighlightView(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            string sheetName,
            List<RoundHoleRowResult> results,
            MathUtility mathUtility,
            SelectionMgr selectionManager,
            ref int highlightedHoles,
            ref int markerIndex)
        {
            if (!IsFlatPatternView(view))
                return;

            string viewName = GetViewName(view);
            MathTransform viewTransform = null;
            try { viewTransform = view.ModelToViewTransform; } catch { }
            if (viewTransform == null)
                return;

            Array components = null;
            try { components = view.GetVisibleComponents() as Array; } catch { }
            if (components == null)
                return;

            Debug.WriteLine("[CHECK ROUND] Highlight view. name=" + viewName
                + ", visibleComponents=" + components.Length);

            foreach (object componentObject in components)
            {
                Component2 component = componentObject as Component2;
                if (component == null)
                    continue;

                string componentPath = "";
                try { componentPath = component.GetPathName() ?? ""; } catch { }
                if (componentPath.Length == 0)
                    continue;

                List<RoundHoleRowResult> matchingRows = new List<RoundHoleRowResult>();
                foreach (RoundHoleRowResult row in results)
                {
                    if (string.IsNullOrWhiteSpace(row.ViewName) && PathsEqual(componentPath, row.PartPath))
                        matchingRows.Add(row);
                }
                if (matchingRows.Count == 0)
                    continue;

                MathTransform componentTransform = null;
                try { componentTransform = component.GetTotalTransform(false); } catch { }
                if (componentTransform == null)
                {
                    try { componentTransform = component.Transform2; } catch { }
                }
                if (componentTransform == null)
                {
                    componentTransform = GetFullComponentTransform(
                        view,
                        component,
                        componentPath);
                }

                List<VisibleCircleEdge> visibleCurves = CollectVisibleCurves(view, component);
                Debug.WriteLine("[CHECK ROUND] Highlight component. path=" + componentPath
                    + ", rows=" + matchingRows.Count
                    + ", curves=" + visibleCurves.Count
                    + ", componentTransform=" + (componentTransform != null));
                if (visibleCurves.Count == 0)
                    continue;

                HashSet<Edge> usedEdges = new HashSet<Edge>();
                SelectData selectData = null;
                if (selectionManager != null)
                {
                    try
                    {
                        selectData = selectionManager.CreateSelectData();
                        if (selectData != null)
                            selectData.View = view;
                    }
                    catch { selectData = null; }
                }

                foreach (RoundHoleRowResult row in matchingRows)
                {
                    double nearestDistance;
                    string coordinateSpace;
                    double[] drawingPoint;
                    List<VisibleCircleEdge> selected = FindEdgesForResult(
                        row,
                        visibleCurves,
                        mathUtility,
                        componentTransform,
                        viewTransform,
                        usedEdges,
                        out nearestDistance,
                        out coordinateSpace,
                        out drawingPoint);
                    Debug.WriteLine("[CHECK ROUND] Highlight map. buhin=" + row.BuhinNo
                        + ", hole=" + row.HoleNumber
                        + ", type=" + row.HoleType
                        + ", selected=" + selected.Count
                        + ", distanceMm=" + (double.IsInfinity(nearestDistance)
                            ? "-"
                            : (nearestDistance * 1000.0).ToString("0.###", CultureInfo.InvariantCulture))
                        + ", space=" + coordinateSpace);
                    if (selected.Count == 0)
                        continue;

                    bool selectedAny = false;
                    VisibleCircleEdge markerEdge = selected[0];
                    try
                    {
                        model.ClearSelection2(true);
                        Entity entity = markerEdge.Edge as Entity;
                        bool selectedOk = entity != null && selectData != null
                            && entity.Select4(false, selectData);
                        if (!selectedOk)
                            selectedOk = view.SelectEntity(markerEdge.Edge, false);
                        if (selectedOk)
                        {
                            selectedAny = true;
                            foreach (VisibleCircleEdge circle in selected)
                                usedEdges.Add(circle.Edge);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK ROUND] Highlight select ERROR: " + ex.Message);
                    }
                    if (!selectedAny)
                        continue;

                    markerIndex++;
                    string markerId = "NG-" + markerIndex;
                    bool markerCreated = CreateMarkerBalloon(
                        model,
                        view,
                        drawingPoint,
                        markerId,
                        markerIndex);
                    if (!markerCreated)
                    {
                        Debug.WriteLine("[CHECK ROUND] Balloon create failed. marker=" + markerId
                            + ", view=" + viewName);
                        markerIndex--;
                        continue;
                    }

                    row.SheetName = sheetName;
                    row.ViewName = viewName;
                    row.MarkerId = markerId;
                    if (drawingPoint != null)
                    {
                        row.DrawingXmm = drawingPoint[0] * 1000.0;
                        row.DrawingYmm = drawingPoint[1] * 1000.0;
                    }
                    highlightedHoles++;
                    Debug.WriteLine("[CHECK ROUND] Highlight hole. buhin=" + row.BuhinNo
                        + ", hole=" + row.HoleNumber + ", marker=" + markerId
                        + ", sheet=" + sheetName
                        + ", view=" + viewName);
                }
            }
        }

        private static bool IsFlatPatternView(SolidWorks.Interop.sldworks.View view)
        {
            if (view == null)
                return false;
            try
            {
                if (view.IsFlatPatternView())
                    return true;
            }
            catch { }

            try
            {
                string configuration = view.ReferencedConfiguration ?? "";
                return configuration.IndexOf(
                    "FLAT-PATTERN",
                    StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool CreateMarkerBalloon(
            ModelDoc2 model,
            SolidWorks.Interop.sldworks.View view,
            double[] drawingPoint,
            string markerId,
            int markerIndex)
        {
            if (model == null || view == null || drawingPoint == null || drawingPoint.Length < 2)
                return false;

            try
            {
                BalloonOptions options = model.Extension.CreateBalloonOptions() as BalloonOptions;
                if (options == null)
                    return false;

                options.Style = (int)swBalloonStyle_e.swBS_Circular;
                options.Size = (int)swBalloonFit_e.swBF_Tightest;
                options.UpperTextContent = (int)swBalloonTextContent_e.swBalloonTextCustom;
                options.UpperText = markerId;
                options.LowerTextContent = (int)swBalloonTextContent_e.swBalloonTextCustom;
                options.LowerText = "";
                options.ShowQuantity = false;
                options.Layername = MarkerLayerName;

                Note balloon = model.Extension.InsertBOMBalloon2(options) as Note;
                if (balloon == null)
                    return false;

                balloon.SetBomBalloonText(
                    (int)swBalloonTextContent_e.swBalloonTextCustom,
                    markerId,
                    (int)swBalloonTextContent_e.swBalloonTextCustom,
                    "");

                Annotation annotation = balloon.GetAnnotation() as Annotation;
                if (annotation == null)
                    return false;

                double[] markerPosition = GetMarkerPosition(view, drawingPoint, markerIndex);
                annotation.Layer = MarkerLayerName;
                annotation.Color = System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.Red);
                annotation.SetLeader3(
                    (int)swLeaderStyle_e.swSTRAIGHT,
                    (int)swLeaderSide_e.swLS_SMART,
                    true,
                    false,
                    false,
                    false);
                annotation.SetPosition2(markerPosition[0], markerPosition[1], 0.0);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Create Balloon ERROR: " + ex.Message);
                return false;
            }
            finally
            {
                try { model.ClearSelection2(true); } catch { }
            }
        }

        private static double[] GetMarkerPosition(
            SolidWorks.Interop.sldworks.View view,
            double[] drawingPoint,
            int markerIndex)
        {
            double x = drawingPoint[0];
            double y = drawingPoint[1];
            double[] outline = null;
            try { outline = view.GetOutline() as double[]; } catch { }
            if (outline == null || outline.Length < 4)
                return new[] { x + 0.010, y + 0.010, 0.0 };

            double left = outline[0];
            double bottom = outline[1];
            double right = outline[2];
            double top = outline[3];
            double margin = 0.010;
            double stagger = ((markerIndex - 1) % 5 - 2) * 0.006;
            double distanceLeft = Math.Abs(x - left);
            double distanceRight = Math.Abs(right - x);
            double distanceBottom = Math.Abs(y - bottom);
            double distanceTop = Math.Abs(top - y);
            double nearest = Math.Min(
                Math.Min(distanceLeft, distanceRight),
                Math.Min(distanceBottom, distanceTop));

            if (nearest == distanceTop)
                return new[] { x + stagger, top + margin, 0.0 };
            if (nearest == distanceBottom)
                return new[] { x + stagger, bottom - margin, 0.0 };
            if (nearest == distanceLeft)
                return new[] { left - margin, y + stagger, 0.0 };
            return new[] { right + margin, y + stagger, 0.0 };
        }

        private static void EnsureMarkerLayer(DrawingDoc drawing)
        {
            if (drawing == null)
                return;
            try
            {
                drawing.CreateLayer2(
                    MarkerLayerName,
                    "TAI TOOL CHECK ROUND markers",
                    System.Drawing.ColorTranslator.ToWin32(System.Drawing.Color.Red),
                    0,
                    0,
                    true,
                    true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Create marker layer ERROR: " + ex.Message);
            }
        }

        private static void DeleteOldMarkers(ModelDoc2 model, DrawingDoc drawing)
        {
            if (model == null || drawing == null)
                return;

            string originalSheetName = GetCurrentSheetName(drawing);
            int deleted = 0;
            foreach (object sheetNameObject in GetSheetNames(drawing))
            {
                string sheetName = Convert.ToString(sheetNameObject) ?? "";
                if (sheetName.Length == 0)
                    continue;
                try { drawing.ActivateSheet(sheetName); } catch { continue; }

                SolidWorks.Interop.sldworks.View view = null;
                try { view = drawing.GetFirstView() as SolidWorks.Interop.sldworks.View; } catch { }
                while (view != null)
                {
                    Array notes = null;
                    try { notes = view.GetNotes() as Array; } catch { }
                    if (notes != null)
                    {
                        foreach (object noteObject in notes)
                        {
                            Note note = noteObject as Note;
                            Annotation annotation = null;
                            try { annotation = note == null ? null : note.GetAnnotation() as Annotation; }
                            catch { }
                            if (annotation == null)
                                continue;

                            string layer = "";
                            try { layer = annotation.Layer ?? ""; } catch { }
                            if (!string.Equals(layer, MarkerLayerName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            try
                            {
                                model.ClearSelection2(true);
                                if (annotation.Select2(false, 0)
                                    && model.Extension.DeleteSelection2(0))
                                {
                                    deleted++;
                                }
                            }
                            catch { }
                        }
                    }

                    try { view = view.GetNextView() as SolidWorks.Interop.sldworks.View; }
                    catch { view = null; }
                }
            }

            if (!string.IsNullOrWhiteSpace(originalSheetName))
            {
                try { drawing.ActivateSheet(originalSheetName); } catch { }
            }
            try { model.ClearSelection2(true); } catch { }
            Debug.WriteLine("[CHECK ROUND] Deleted old Balloon markers=" + deleted);
        }

        private static string GetCurrentSheetName(DrawingDoc drawing)
        {
            try
            {
                Sheet sheet = drawing == null ? null : drawing.GetCurrentSheet() as Sheet;
                return sheet == null ? "" : sheet.GetName();
            }
            catch
            {
                return "";
            }
        }

        private static object[] GetSheetNames(DrawingDoc drawing)
        {
            try
            {
                Array names = drawing == null ? null : drawing.GetSheetNames() as Array;
                if (names == null)
                    return new object[0];
                object[] result = new object[names.Length];
                int index = 0;
                foreach (object name in names)
                    result[index++] = name;
                return result;
            }
            catch
            {
                return new object[0];
            }
        }

        private static List<VisibleCircleEdge> CollectVisibleCurves(
            SolidWorks.Interop.sldworks.View view,
            Component2 component)
        {
            List<VisibleCircleEdge> result = new List<VisibleCircleEdge>();
            int[] types =
            {
                (int)swViewEntityType_e.swViewEntityType_Edge,
                (int)swViewEntityType_e.swViewEntityType_SilhouetteEdge
            };

            foreach (int type in types)
            {
                Array entities = null;
                try { entities = view.GetVisibleEntities2(component, type) as Array; } catch { }
                if (entities == null)
                    continue;

                foreach (object entity in entities)
                {
                    Edge edge = entity as Edge;
                    Curve curve = null;
                    try { curve = edge == null ? null : edge.GetCurve() as Curve; } catch { }
                    if (curve == null)
                        continue;

                    double[] values = null;
                    bool isCircular = false;
                    bool isClosed = false;
                    try
                    {
                        isCircular = curve.IsCircle();
                        if (isCircular)
                            values = curve.CircleParams as double[];
                    }
                    catch { }

                    try
                    {
                        double start;
                        double end;
                        bool periodic;
                        curve.GetEndParams(out start, out end, out isClosed, out periodic);
                    }
                    catch { }
                    if (!isClosed)
                        isClosed = isCircular || IsTopologicallyClosed(edge);

                    if (!isCircular)
                    {
                        bool isLine = false;
                        try { isLine = curve.IsLine(); } catch { }
                        if (isLine)
                            continue;

                        double[] box = GetEdgeBounds(edge, curve);
                        if (box == null || box.Length < 6)
                            continue;
                        values = new double[7];
                        values[0] = (box[0] + box[3]) / 2.0;
                        values[1] = (box[1] + box[4]) / 2.0;
                        values[2] = (box[2] + box[5]) / 2.0;
                    }
                    else if (values == null || values.Length < 7)
                    {
                        continue;
                    }

                    bool duplicate = false;
                    foreach (VisibleCircleEdge existing in result)
                    {
                        if (ReferenceEquals(existing.Edge, edge))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (!duplicate)
                    {
                        result.Add(new VisibleCircleEdge
                        {
                            Edge = edge,
                            X = values[0],
                            Y = values[1],
                            Z = values[2],
                            RadiusM = Math.Abs(values[6]),
                            IsCircular = isCircular,
                            IsClosed = isClosed
                        });
                    }
                }
            }
            return result;
        }

        private static List<VisibleCircleEdge> FindEdgesForResult(
            RoundHoleRowResult row,
            List<VisibleCircleEdge> circles,
            MathUtility mathUtility,
            MathTransform componentTransform,
            MathTransform viewTransform,
            HashSet<Edge> usedEdges,
            out double nearestDistance,
            out string coordinateSpace,
            out double[] drawingPoint)
        {
            List<VisibleCircleEdge> selected = new List<VisibleCircleEdge>();
            nearestDistance = double.PositiveInfinity;
            coordinateSpace = "none";
            drawingPoint = GetDrawingPoint(
                row,
                mathUtility,
                componentTransform,
                viewTransform);
            if (string.Equals(row.HoleType, "SLOT", StringComparison.OrdinalIgnoreCase)
                && row.Arc1CenterModelX.HasValue && row.Arc2CenterModelX.HasValue)
            {
                double firstDistance;
                string firstSpace;
                VisibleCircleEdge first = FindNearest(
                    circles,
                    row.Arc1CenterModelX.Value,
                    row.Arc1CenterModelY.Value,
                    row.Arc1CenterModelZ.Value,
                    row.R1Mm,
                    null,
                    true,
                    false,
                    mathUtility,
                    componentTransform,
                    viewTransform,
                    usedEdges,
                    out firstDistance,
                    out firstSpace);
                if (first != null)
                    selected.Add(first);

                double secondDistance;
                string secondSpace;
                VisibleCircleEdge second = FindNearest(
                    circles,
                    row.Arc2CenterModelX.Value,
                    row.Arc2CenterModelY.Value,
                    row.Arc2CenterModelZ.Value,
                    row.R2Mm,
                    first,
                    true,
                    false,
                    mathUtility,
                    componentTransform,
                    viewTransform,
                    usedEdges,
                    out secondDistance,
                    out secondSpace);
                if (second != null)
                    selected.Add(second);

                nearestDistance = Math.Max(firstDistance, secondDistance);
                coordinateSpace = firstSpace + "/" + secondSpace;
            }
            else
            {
                bool requireCircular = !string.Equals(
                    row.HoleType,
                    "IRREGULAR",
                    StringComparison.OrdinalIgnoreCase);
                double distance;
                string space;
                VisibleCircleEdge circle = FindNearest(
                    circles,
                    row.CenterModelX.Value,
                    row.CenterModelY.Value,
                    row.CenterModelZ.Value,
                    row.R1Mm,
                    null,
                    requireCircular,
                    !requireCircular,
                    mathUtility,
                    componentTransform,
                    viewTransform,
                    usedEdges,
                    out distance,
                    out space);
                if (circle != null)
                    selected.Add(circle);
                nearestDistance = distance;
                coordinateSpace = space;
            }
            return selected;
        }

        private static VisibleCircleEdge FindNearest(
            List<VisibleCircleEdge> circles,
            double x,
            double y,
            double z,
            double? radiusMm,
            VisibleCircleEdge excluded,
            bool requireCircular,
            bool requireClosedIrregular,
            MathUtility mathUtility,
            MathTransform componentTransform,
            MathTransform viewTransform,
            HashSet<Edge> usedEdges,
            out double bestDistance,
            out string coordinateSpace)
        {
            VisibleCircleEdge best = null;
            double bestScore = double.MaxValue;
            bestDistance = double.PositiveInfinity;
            coordinateSpace = "none";
            foreach (VisibleCircleEdge circle in circles)
            {
                if (ReferenceEquals(circle, excluded))
                    continue;
                if (usedEdges != null && usedEdges.Contains(circle.Edge))
                    continue;
                if (requireCircular && !circle.IsCircular)
                    continue;
                if (requireClosedIrregular && !circle.IsClosed)
                    continue;

                string currentSpace;
                double distance = GetMinimumMappedDistance(
                    mathUtility,
                    componentTransform,
                    viewTransform,
                    x,
                    y,
                    z,
                    circle.X,
                    circle.Y,
                    circle.Z,
                    out currentSpace);
                double radiusDifference = radiusMm.HasValue
                    ? Math.Abs(circle.RadiusM - radiusMm.Value / 1000.0)
                    : 0;
                double score = distance + radiusDifference * 0.25;
                if (score < bestScore)
                {
                    best = circle;
                    bestScore = score;
                    bestDistance = distance;
                    coordinateSpace = currentSpace;
                }
            }
            if (requireClosedIrregular)
                return best;
            return bestDistance <= MaximumCenterDistanceM ? best : null;
        }

        private static double GetMinimumMappedDistance(
            MathUtility mathUtility,
            MathTransform componentTransform,
            MathTransform viewTransform,
            double targetX,
            double targetY,
            double targetZ,
            double candidateX,
            double candidateY,
            double candidateZ,
            out string coordinateSpace)
        {
            double[] targetLocal = { targetX, targetY, targetZ };
            double[] candidateLocal = { candidateX, candidateY, candidateZ };
            double[] targetAssembly = TransformPoint(
                mathUtility,
                componentTransform,
                targetX,
                targetY,
                targetZ);
            double[] candidateAssembly = TransformPoint(
                mathUtility,
                componentTransform,
                candidateX,
                candidateY,
                candidateZ);
            double[] targetDrawing = TransformPoint(
                mathUtility,
                viewTransform,
                targetAssembly ?? targetLocal);
            double[] candidateDrawingFromLocal = TransformPoint(
                mathUtility,
                viewTransform,
                candidateLocal);
            double[] candidateDrawingFromAssembly = TransformPoint(
                mathUtility,
                viewTransform,
                candidateAssembly ?? candidateLocal);

            double best = double.PositiveInfinity;
            coordinateSpace = "none";
            CompareDistance(targetLocal, candidateLocal, "part", ref best, ref coordinateSpace);
            CompareDistance(targetAssembly, candidateLocal, "assembly/raw", ref best, ref coordinateSpace);
            CompareDistance(targetAssembly, candidateAssembly, "assembly", ref best, ref coordinateSpace);
            CompareDistance(targetDrawing, candidateLocal, "drawing/raw", ref best, ref coordinateSpace);
            CompareDistance(targetDrawing, candidateDrawingFromLocal, "drawing/local", ref best, ref coordinateSpace);
            CompareDistance(targetDrawing, candidateDrawingFromAssembly, "drawing/assembly", ref best, ref coordinateSpace);
            return best;
        }

        private static void CompareDistance(
            double[] first,
            double[] second,
            string space,
            ref double best,
            ref string bestSpace)
        {
            if (first == null || second == null || first.Length < 3 || second.Length < 3)
                return;

            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            double dz = first[2] - second[2];
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance < best)
            {
                best = distance;
                bestSpace = space;
            }
        }

        private static double[] GetDrawingPoint(
            RoundHoleRowResult row,
            MathUtility mathUtility,
            MathTransform componentTransform,
            MathTransform viewTransform)
        {
            if (row == null || !row.CenterModelX.HasValue
                || !row.CenterModelY.HasValue || !row.CenterModelZ.HasValue)
            {
                return null;
            }

            double[] local =
            {
                row.CenterModelX.Value,
                row.CenterModelY.Value,
                row.CenterModelZ.Value
            };
            double[] assembly = TransformPoint(
                mathUtility,
                componentTransform,
                local[0],
                local[1],
                local[2]);
            return TransformPoint(
                mathUtility,
                viewTransform,
                assembly ?? local);
        }

        private static double[] GetEdgeBounds(Edge edge, Curve curve)
        {
            if (edge == null || curve == null)
                return null;

            double start = 0;
            double end = 0;
            bool closed = false;
            bool periodic = false;
            try
            {
                if (!curve.GetEndParams(out start, out end, out closed, out periodic))
                    return null;
            }
            catch
            {
                return null;
            }

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;
            bool found = false;
            const int sampleCount = 32;
            for (int i = 0; i <= sampleCount; i++)
            {
                double parameter = start + (end - start) * i / sampleCount;
                double[] point = null;
                try { point = curve.Evaluate(parameter) as double[]; } catch { }
                if (point == null || point.Length < 3)
                    continue;

                minX = Math.Min(minX, point[0]);
                minY = Math.Min(minY, point[1]);
                minZ = Math.Min(minZ, point[2]);
                maxX = Math.Max(maxX, point[0]);
                maxY = Math.Max(maxY, point[1]);
                maxZ = Math.Max(maxZ, point[2]);
                found = true;
            }

            return found
                ? new[] { minX, minY, minZ, maxX, maxY, maxZ }
                : null;
        }

        private static bool IsTopologicallyClosed(Edge edge)
        {
            if (edge == null)
                return false;

            try
            {
                CurveParamData curveParams = edge.GetCurveParams3();
                double[] start = curveParams == null ? null : curveParams.StartPoint as double[];
                double[] end = curveParams == null ? null : curveParams.EndPoint as double[];
                if (PointsCoincide(start, end))
                    return true;
            }
            catch { }

            try
            {
                Vertex startVertex = edge.GetStartVertex() as Vertex;
                Vertex endVertex = edge.GetEndVertex() as Vertex;
                if (startVertex != null && endVertex != null)
                {
                    if (ReferenceEquals(startVertex, endVertex))
                        return true;
                    return PointsCoincide(
                        startVertex.GetPoint() as double[],
                        endVertex.GetPoint() as double[]);
                }
            }
            catch { }
            return false;
        }

        private static bool PointsCoincide(double[] first, double[] second)
        {
            if (first == null || second == null || first.Length < 3 || second.Length < 3)
                return false;
            const double toleranceM = 0.000001;
            double dx = first[0] - second[0];
            double dy = first[1] - second[1];
            double dz = first[2] - second[2];
            return dx * dx + dy * dy + dz * dz <= toleranceM * toleranceM;
        }

        private static MathTransform GetFullComponentTransform(
            SolidWorks.Interop.sldworks.View view,
            Component2 visibleComponent,
            string componentPath)
        {
            if (view == null)
                return null;

            string visibleName = "";
            try { visibleName = visibleComponent == null ? "" : visibleComponent.Name2 ?? ""; } catch { }

            Array drawingComponents = null;
            try { drawingComponents = view.GetVisibleDrawingComponents() as Array; } catch { }
            if (drawingComponents == null)
                return null;

            Component2 pathFallback = null;
            foreach (object item in drawingComponents)
            {
                DrawingComponent drawingComponent = item as DrawingComponent;
                Component2 fullComponent = null;
                try { fullComponent = drawingComponent == null ? null : drawingComponent.Component; } catch { }
                if (fullComponent == null)
                    continue;

                string fullPath = "";
                string fullName = "";
                try { fullPath = fullComponent.GetPathName() ?? ""; } catch { }
                try { fullName = fullComponent.Name2 ?? ""; } catch { }
                if (!PathsEqual(fullPath, componentPath))
                    continue;

                if (pathFallback == null)
                    pathFallback = fullComponent;
                if (visibleName.Length > 0
                    && string.Equals(fullName, visibleName, StringComparison.OrdinalIgnoreCase))
                {
                    pathFallback = fullComponent;
                    break;
                }
            }

            if (pathFallback == null)
                return null;

            MathTransform transform = null;
            try { transform = pathFallback.GetTotalTransform(false); } catch { }
            if (transform == null)
            {
                try { transform = pathFallback.Transform2; } catch { }
            }
            return transform;
        }

        private static double[] TransformPoint(
            MathUtility mathUtility,
            MathTransform transform,
            double[] coordinates)
        {
            if (coordinates == null || coordinates.Length < 3)
                return null;
            return TransformPoint(
                mathUtility,
                transform,
                coordinates[0],
                coordinates[1],
                coordinates[2]);
        }

        private static double[] TransformPoint(
            MathUtility mathUtility,
            MathTransform transform,
            double x,
            double y,
            double z)
        {
            try
            {
                MathPoint point = mathUtility.CreatePoint(new[] { x, y, z }) as MathPoint;
                point = point == null ? null : point.MultiplyTransform(transform) as MathPoint;
                return point == null ? null : point.ArrayData as double[];
            }
            catch
            {
                return null;
            }
        }

        private static bool PathsEqual(string first, string second)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(first ?? "").TrimEnd('\\'),
                    Path.GetFullPath(second ?? "").TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(first ?? "", second ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string GetViewName(SolidWorks.Interop.sldworks.View view)
        {
            try { return Convert.ToString(((dynamic)view).Name ?? ""); } catch { }
            try { return Convert.ToString(((dynamic)view).GetName2() ?? ""); } catch { }
            return "";
        }

        private sealed class VisibleCircleEdge
        {
            public Edge Edge;
            public double X;
            public double Y;
            public double Z;
            public double RadiusM;
            public bool IsCircular;
            public bool IsClosed;
        }
    }

    public static class ExcelRoundHoleExporter
    {
        public static int Export(List<RoundHoleRowResult> results)
        {
            List<RoundHoleRowResult> abnormal = new List<RoundHoleRowResult>();
            if (results != null)
            {
                foreach (RoundHoleRowResult row in results)
                {
                    if (row != null && (row.Status == "NG" || row.Status == "CHECK"))
                        abnormal.Add(row);
                }
            }

            if (abnormal.Count == 0)
            {
                Debug.WriteLine("[CHECK ROUND] Excel skipped: khong co CHECK/NG.");
                return 0;
            }

            List<RoundHoleExcelGroup> groups = BuildGroups(abnormal);

            object excelObject = null;
            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                    throw new InvalidOperationException("Khong tim thay Microsoft Excel.");

                excelObject = Activator.CreateInstance(excelType);
                dynamic excel = excelObject;
                dynamic workbook = excel.Workbooks.Add();
                dynamic sheet = workbook.Worksheets[1];
                sheet.Name = "CHECK ROUND";

                string[] headers =
                {
                    "部品番号",
                    "Loai lo",
                    "R1 (mm)",
                    "R2 (mm)",
                    "Delta R (mm)",
                    "Note",
                    "Status",
                    "Configuration",
                    "Path"
                };

                headers = new string[]
                {
                    "\u90E8\u54C1\u756A\u53F7",
                    "Sheet",
                    "Drawing view",
                    "S\u1ed1 l\u1ed7 l\u1ed7i",
                    "Đánh dấu / Hole No.",
                    "Lo\u1ea1i l\u1ed7",
                    "V\u1ecb tr\u00ed X,Y (mm)",
                    "R1 (mm)",
                    "R2 (mm)",
                    "Delta R (mm)",
                    "Note",
                    "Status",
                    "Path"
                };

                for (int column = 0; column < headers.Length; column++)
                    sheet.Cells[1, column + 1] = headers[column];

                int rowIndex = 2;
                foreach (RoundHoleExcelGroup group in groups)
                {
                    sheet.Cells[rowIndex, 1] = group.BuhinNo;
                    sheet.Cells[rowIndex, 2] = JoinDistinctValues(
                        group.Rows,
                        delegate(RoundHoleRowResult row) { return row.SheetName; });
                    sheet.Cells[rowIndex, 3] = JoinDistinctValues(
                        group.Rows,
                        delegate(RoundHoleRowResult row) { return row.ViewName; });
                    sheet.Cells[rowIndex, 4] = group.Rows.Count;
                    sheet.Cells[rowIndex, 5] = JoinDistinctValues(
                        group.Rows,
                        delegate(RoundHoleRowResult row)
                        {
                            if (!string.IsNullOrWhiteSpace(row.MarkerId))
                                return row.MarkerId;
                            return row.HoleNumber > 0 ? "Hole-" + row.HoleNumber : "";
                        });
                    sheet.Cells[rowIndex, 6] = BuildTypeSummary(group.Rows);
                    sheet.Cells[rowIndex, 7] = JoinDistinctValues(
                        group.Rows,
                        delegate(RoundHoleRowResult row)
                        {
                            if (!row.DrawingXmm.HasValue || !row.DrawingYmm.HasValue)
                                return "";
                            return "(" + row.DrawingXmm.Value.ToString("0.###", CultureInfo.InvariantCulture)
                                + ", " + row.DrawingYmm.Value.ToString("0.###", CultureInfo.InvariantCulture) + ")";
                        });
                    sheet.Cells[rowIndex, 8] = JoinDistinctValues(
                        group.Rows,
                        delegate(RoundHoleRowResult row) { return FormatNullable(row.R1Mm); });
                    sheet.Cells[rowIndex, 9] = JoinDistinctValues(
                        group.Rows,
                        delegate(RoundHoleRowResult row) { return FormatNullable(row.R2Mm); });
                    sheet.Cells[rowIndex, 10] = JoinDistinctValues(
                        group.Rows,
                        delegate(RoundHoleRowResult row) { return FormatNullable(row.DeltaRMm); });
                    sheet.Cells[rowIndex, 11] = "S\u1ed1 l\u1ed7 l\u1ed7i: " + group.Rows.Count + ". "
                        + JoinDistinctValues(
                            group.Rows,
                            delegate(RoundHoleRowResult row) { return row.Note; });
                    sheet.Cells[rowIndex, 12] = group.Status;
                    sheet.Cells[rowIndex, 13] = group.PartPath;

                    dynamic rowRange = sheet.Range[sheet.Cells[rowIndex, 1], sheet.Cells[rowIndex, 13]];
                    rowRange.Interior.Color = group.Status == "NG" ? 13408767 : 10092543;
                    rowIndex++;
                }

                dynamic headerRange = sheet.Range[sheet.Cells[1, 1], sheet.Cells[1, 13]];
                headerRange.Font.Bold = true;
                headerRange.Interior.Color = 14277081;
                dynamic usedRange = sheet.Range[sheet.Cells[1, 1], sheet.Cells[rowIndex - 1, 13]];
                usedRange.Borders.LineStyle = 1;
                usedRange.VerticalAlignment = -4160;
                usedRange.Rows.RowHeight = 20;
                usedRange.Columns.AutoFit();
                sheet.Columns[1].ColumnWidth = 12;
                sheet.Columns[2].ColumnWidth = 14;
                sheet.Columns[3].ColumnWidth = 18;
                sheet.Columns[4].ColumnWidth = 10;
                sheet.Columns[5].ColumnWidth = 22;
                sheet.Columns[6].ColumnWidth = 18;
                sheet.Columns[7].ColumnWidth = 32;
                sheet.Columns[8].ColumnWidth = 11;
                sheet.Columns[9].ColumnWidth = 11;
                sheet.Columns[10].ColumnWidth = 13;
                sheet.Columns[11].ColumnWidth = 55;
                sheet.Columns[12].ColumnWidth = 10;
                sheet.Columns[13].ColumnWidth = 55;

                excel.Visible = true;
                excel.ActiveWindow.SplitRow = 1;
                excel.ActiveWindow.FreezePanes = true;
                Debug.WriteLine("[CHECK ROUND] Excel exported rows=" + groups.Count
                    + ", holes=" + abnormal.Count);
                return groups.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK ROUND] Excel ERROR: " + ex);
                MessageBox.Show(
                    "Khong xuat duoc Excel CHECK ROUND:\r\n" + ex.Message,
                    "CHECK ROUND",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                if (excelObject != null && Marshal.IsComObject(excelObject))
                {
                    try { Marshal.FinalReleaseComObject(excelObject); } catch { }
                }
                return 0;
            }
        }

        private static List<RoundHoleExcelGroup> BuildGroups(
            List<RoundHoleRowResult> abnormal)
        {
            List<RoundHoleExcelGroup> groups = new List<RoundHoleExcelGroup>();
            Dictionary<string, RoundHoleExcelGroup> byKey =
                new Dictionary<string, RoundHoleExcelGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (RoundHoleRowResult row in abnormal)
            {
                string path = row.PartPath ?? "";
                string key = path.Trim().Length > 0
                    ? path.Trim()
                    : "BUHIN:" + (row.BuhinNo ?? "");
                RoundHoleExcelGroup group;
                if (!byKey.TryGetValue(key, out group))
                {
                    group = new RoundHoleExcelGroup
                    {
                        BuhinNo = row.BuhinNo ?? "",
                        PartPath = path,
                        Status = row.Status ?? "CHECK"
                    };
                    byKey.Add(key, group);
                    groups.Add(group);
                }

                group.Rows.Add(row);
                if (row.Status == "NG")
                    group.Status = "NG";
            }
            return groups;
        }

        private static string JoinDistinctValues(
            List<RoundHoleRowResult> rows,
            Func<RoundHoleRowResult, string> selector)
        {
            List<string> values = new List<string>();
            foreach (RoundHoleRowResult row in rows)
            {
                string value = selector(row) ?? "";
                value = value.Trim();
                if (value.Length == 0)
                    continue;

                bool exists = false;
                foreach (string existing in values)
                {
                    if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    values.Add(value);
            }
            return string.Join(", ", values.ToArray());
        }

        private static string BuildTypeSummary(List<RoundHoleRowResult> rows)
        {
            List<string> order = new List<string>();
            Dictionary<string, int> counts =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (RoundHoleRowResult row in rows)
            {
                string type = string.IsNullOrWhiteSpace(row.HoleType)
                    ? "UNKNOWN"
                    : row.HoleType.Trim();
                int count;
                if (!counts.TryGetValue(type, out count))
                {
                    counts[type] = 1;
                    order.Add(type);
                }
                else
                {
                    counts[type] = count + 1;
                }
            }

            List<string> summary = new List<string>();
            foreach (string type in order)
                summary.Add(type + " x" + counts[type]);
            return string.Join(", ", summary.ToArray());
        }

        private static string FormatNullable(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
                : "";
        }

        private sealed class RoundHoleExcelGroup
        {
            public string BuhinNo;
            public string PartPath;
            public string Status;
            public readonly List<RoundHoleRowResult> Rows = new List<RoundHoleRowResult>();
        }
    }
}
