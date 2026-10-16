// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Đặt layer BẰNG HÌNH HỌC (phương án 06/10/2026): SolidWorks chỉ ghi hình
    /// cắt thô; XToolPro đã đọc từ model đâu là cạnh MARK, đường uốn, vòng phải
    /// xoá (<see cref="DxfSemanticPlan"/>, cùng khung toạ độ với DXF thô) và
    /// ở đây:
    ///   1. kiểm chứng phép chiếu: hình DXF phải nằm trên cạnh nhìn thấy đã chiếu
    ///      (bù một tịnh tiến nếu kích thước khớp mà vị trí lệch);
    ///   2. cắt bỏ hình ngoài khung view (view cha của view cắt/chi tiết);
    ///   3. nét nằm trọn trên cạnh MARK → LASER_MARK; trên vòng xoá → xoá; trên
    ///      đường uốn → BEND; còn lại là hình cắt → CUT. Đường uốn / sketch MARK
    ///      không có nét nào thì TỰ GHI;
    ///   4. chia tỉ lệ view, dời về gốc, bỏ chữ lạ.
    /// Không vẽ sketch tạm, không layer tạm, không map, không phụ thuộc màu/kiểu nét.
    /// </summary>
    internal static partial class DxfLayerFinalizer
    {
        /// <summary>Nét nằm «trên» cạnh khi mọi điểm mẫu cách cạnh ≤ ngần này (mm, khung thô).</summary>
        private const double SemanticOnEdgeTolerance = 0.03;
        private const double SemanticMatchTolerance = 0.05;
        private const double SemanticMinimumMatch = 0.95;
        private const double SemanticMinimumMatchWithBox = 0.6;
        private const double SemanticCropMarginMm = 2.0;

        /// <summary>Hai cạnh chiếu cách nhau ≤ ngần này (mm THẬT) là một — laser không phân biệt được.</summary>
        private const double SemanticCoincidentToleranceMm = 0.005;

        public static DxfSemanticResult ApplySemanticPlan(
            string filePath,
            DxfSemanticPlan plan,
            DxfManufacturingLayerSettings layerSettings)
        {
            var result = new DxfSemanticResult();
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (!File.Exists(filePath)) throw new FileNotFoundException("DXF not found.", filePath);
            DxfManufacturingLayerSettings settings = DxfManufacturingLayerSettings.Normalize(layerSettings);

            Encoding encoding = Encoding.GetEncoding(932);
            var lines = File.ReadAllLines(filePath, encoding).ToList();
            if (lines.Count % 2 != 0)
                throw new InvalidDataException("DXF group pairs are unbalanced.");

            double scale = plan.ViewScale > 0 ? plan.ViewScale : 1.0;
            // Bend/center/construction lines are not exported unless identified as MARK.
            plan.Bends.Clear();
            var drawingItems = VisibleDrawingItems(plan);
            var drawingIndex = new LaserSegmentIndex(ToSegments(drawingItems), SemanticOnEdgeTolerance);
            var rawVisibility = new RawDrawingLineVisibility(lines);
            var originalRecords = CollectEntityRecords(lines);
            var planMarkIndex = new LaserSegmentIndex(ToSegments(plan.Marks), SemanticOnEdgeTolerance);

            // ---------------------------------------------------------- 2. cắt khung view
            // (trước kiểm chứng: hình của view cha không được tính vào tỉ lệ khớp;
            // ở chế độ cắt khung, DXF thô phải trùng hệ sheet — không bù tịnh tiến).
            List<EntitySamples> entities = SampleEntities(lines);
            double[] planBox = BoundsOf(plan.VisibleEdges.SelectMany(item => item.Points).Select(p => new DxfPoint2D { X = p[0], Y = p[1] }));
            var doomed = new HashSet<int>();   // record index
            if (plan.CropToView && planBox != null)
            {
                double x0 = planBox[0] - SemanticCropMarginMm, y0 = planBox[1] - SemanticCropMarginMm;
                double x1 = planBox[2] + SemanticCropMarginMm, y1 = planBox[3] + SemanticCropMarginMm;
                foreach (EntitySamples entity in entities)
                {
                    if (entity.IsText || entity.Points.Count == 0) continue;
                    if (entity.Points.All(p => p.X < x0 || p.X > x1 || p.Y < y0 || p.Y > y1))
                    {
                        doomed.Add(entity.RecordIndex);
                        result.CroppedEntities++;
                    }
                }
                entities = entities.Where(e => !doomed.Contains(e.RecordIndex)).ToList();
            }

            // ---------------------------------------------------------- 1. kiểm chứng
            // Mọi nét SolidWorks ghi phải nằm trên thứ XToolPro biết: cạnh nhìn thấy,
            // đường uốn, vòng xoá, cạnh MARK.
            // Ignore decoration when checking placement; the independent complete
            // geometry gate still requires every expected Drawing curve afterward.
            var verificationEntities = entities.Where(e => e.IsText || e.Points.Count == 0 ||
                (e.Points.All(p => planMarkIndex.IsNear(p, SemanticOnEdgeTolerance))) ||
                (rawVisibility.IsVisibleContinuous(lines, originalRecords[e.RecordIndex]) &&
                 (!plan.CropToView || e.Points.All(p => drawingIndex.IsNear(p, SemanticOnEdgeTolerance))))).ToList();
            List<LaserSegment> visible = ExpectedSegments(plan);
            var visibleIndex = new LaserSegmentIndex(visible, SemanticMatchTolerance);
            double[] dxfBox = BoundsOf(verificationEntities.Where(e => IsCutCandidate(e, settings)).SelectMany(e => e.Points));
            double offsetX = 0, offsetY = 0;
            if (dxfBox != null && planBox != null && plan.VisibleEdges.Count > 0)
            {
                double ratio = MatchRatio(verificationEntities, visibleIndex, settings);
                if (ratio < SemanticMinimumMatch && !plan.CropToView &&
                    Math.Abs((dxfBox[2] - dxfBox[0]) - (planBox[2] - planBox[0])) <= 0.1 &&
                    Math.Abs((dxfBox[3] - dxfBox[1]) - (planBox[3] - planBox[1])) <= 0.1)
                {
                    // Cùng kích thước, khác vị trí: DXF thô đặt gốc khác sheet → dời kế hoạch.
                    offsetX = dxfBox[0] - planBox[0];
                    offsetY = dxfBox[1] - planBox[1];
                    ShiftPlan(plan, offsetX, offsetY);
                    visible = ExpectedSegments(plan);
                    visibleIndex = new LaserSegmentIndex(visible, SemanticMatchTolerance);
                    ratio = MatchRatio(verificationEntities, visibleIndex, settings);
                }
                result.MatchRatio = ratio;
                result.OffsetXMm = offsetX;
                result.OffsetYMm = offsetY;
                // Đạt khi hầu hết nét nằm trên cạnh; hoặc khi khung bao TRÙNG cả kích
                // thước lẫn vị trí (view cắt: nét mặt cắt không phải «cạnh nhìn thấy»
                // nhưng hình vẫn đúng chỗ, đúng cỡ).
                bool sameBox = Math.Abs(dxfBox[0] - planBox[0]) <= 0.1 && Math.Abs(dxfBox[1] - planBox[1]) <= 0.1 &&
                               Math.Abs(dxfBox[2] - planBox[2]) <= 0.1 && Math.Abs(dxfBox[3] - planBox[3]) <= 0.1;
                result.Verified = ratio >= SemanticMinimumMatch || (sameBox && ratio >= SemanticMinimumMatchWithBox);
            }
            else
            {
                result.Verified = plan.VisibleEdges.Count == 0;   // không có gì để so (view trống)
                result.MatchRatio = result.Verified ? 1 : 0;
            }

            if (!result.Verified)
            {
                result.Message = "hình DXF không khớp cạnh view đã chiếu (" +
                                 (result.MatchRatio * 100).ToString("0.#", CultureInfo.InvariantCulture) +
                                 " % điểm nằm trên cạnh; DXF " + Describe(dxfBox) + ", chiếu " + Describe(planBox) + ")";
                return result;
            }

            // ---------------------------------------------------------- 3. đặt layer
            // Vòng xoá TRÙNG KHÍT một cạnh MARK trên hình chiếu (lỗ MARK thẳng xuyên tấm: MarkRimSelector
            // giữ vòng miệng, bỏ vòng mặt sau — hai vòng chiếu ĐÚNG lên nhau) không được xoá gì: xoá theo nó
            // là xoá luôn vòng MARK duy nhất SolidWorks vẽ (4119 chi tiết 22/30/46/50/51 mất vòng Ø6 ở view
            // 1:13–1:15, DXF Check lệch đúng π·6 mm).
            double coincident = SemanticCoincidentToleranceMm * scale;
            List<DxfSemanticItem> eraseItems = plan.Erase
                .Where(erase => !plan.Marks.Any(mark => SameProjectedEdge(erase, mark, coincident)))
                .ToList();
            result.CoincidentEraseIgnored = plan.Erase.Count - eraseItems.Count;
            var markIndex = new LaserSegmentIndex(ToSegments(plan.Marks), SemanticOnEdgeTolerance);
            var eraseIndex = new LaserSegmentIndex(ToSegments(eraseItems), SemanticOnEdgeTolerance);
            // Để so «bên nào sát hơn» chính xác: đường tròn kín so GIẢI TÍCH (tâm, bán kính), cạnh khác theo dây cung.
            var markShapes = new ShapeSet(plan.Marks);
            var eraseShapes = new ShapeSet(eraseItems);
            var bendIndex = new LaserSegmentIndex(ToSegments(plan.Bends), SemanticOnEdgeTolerance);
            // Cạnh MODEL nhìn thấy (biên, lỗ…). Đường uốn là sketch, không phải cạnh model: nét nằm trên
            // đường uốn MÀ cũng nằm trên một cạnh model là CẠNH BIÊN trùng đường uốn (tấm cuốn/uốn ở đầu
            // tấm, chi tiết 13 của 4115) → giữ CUT, không thì biên ngoài bị hở.
            drawingIndex = new LaserSegmentIndex(ToSegments(VisibleDrawingItems(plan)), SemanticOnEdgeTolerance);
            var modelEdgeIndex = drawingIndex;
            var markHit = new bool[plan.Marks.Count];
            var markSamples = plan.Marks.Select(MarkSamplePoints).ToList();
            var markSampleHit = plan.Marks.Select(_ => new bool[MarkSampleCount]).ToList();
            var bendHit = new bool[plan.Bends.Count];
            var relayer = new Dictionary<int, string>();
            foreach (EntitySamples entity in entities)
            {
                if (entity.IsText || entity.Points.Count == 0 || doomed.Contains(entity.RecordIndex)) continue;
                bool onErase = eraseItems.Count > 0 && entity.Points.All(p => eraseIndex.IsNear(p, SemanticOnEdgeTolerance));
                bool onMark = plan.Marks.Count > 0 && entity.Points.All(p => markIndex.IsNear(p, SemanticOnEdgeTolerance));
                if (onErase && onMark)
                {
                    // Dung sai 0,03 mm GIẤY = 0,45 mm thật ở view 1:15: một nét có thể «nằm trên» cả vòng
                    // xoá lẫn cạnh MARK. Bên khớp SÁT hơn thắng (vòng trong của lỗ côn chỉ lệch 0,05 mm vẫn
                    // phân biệt được); sát ngang nhau trong 0,005 mm thật → MARK (mất một nét khắc là lỗi nặng
                    // hơn khắc thừa — bước gỡ nét chồng dọn vòng thừa).
                    double toErase = eraseShapes.Deviation(entity.Points);
                    double toMark = markShapes.Deviation(entity.Points);
                    onErase = toErase < toMark - coincident;
                    if (!onErase) result.MarkOverEraseEntities++;
                }
                if (onErase)
                {
                    doomed.Add(entity.RecordIndex);
                    result.ErasedEntities++;
                    continue;
                }
                if (onMark)
                {
                    relayer[entity.RecordIndex] = settings.LaserMark.Name;
                    result.MarkEntities++;
                    NoteMarkHits(markSamples, markSampleHit, markHit, entity.Points);
                    continue;
                }
                if (plan.Bends.Count > 0 && entity.Points.All(p => bendIndex.IsNear(p, SemanticOnEdgeTolerance)) &&
                    plan.VisibleEdges.Count > 0 && entity.Points.All(p => modelEdgeIndex.IsNear(p, SemanticOnEdgeTolerance)))
                {
                    // Đường uốn nằm trên biên cắt: không vẽ BEND (máy uốn ở mép), không tự ghi thêm.
                    NoteHits(plan.Bends, bendHit, entity.Points);
                    result.BendOnBoundary++;
                    if (!IsLayer(entity.Layer, settings.PartInfo.Name))
                    {
                        relayer[entity.RecordIndex] = settings.Cut.Name;
                        result.CutEntities++;
                    }
                    continue;
                }
                if (plan.Bends.Count > 0 && entity.Points.All(p => bendIndex.IsNear(p, SemanticOnEdgeTolerance)))
                {
                    relayer[entity.RecordIndex] = settings.Bend.Name;
                    result.BendEntitiesFound++;
                    NoteHits(plan.Bends, bendHit, entity.Points);
                    continue;
                }
                if (!IsLayer(entity.Layer, settings.PartInfo.Name) &&
                    rawVisibility.IsVisibleContinuous(lines, originalRecords[entity.RecordIndex]) &&
                    entity.Points.All(p => drawingIndex.IsNear(p, SemanticOnEdgeTolerance)))
                {
                    relayer[entity.RecordIndex] = settings.Cut.Name;
                    result.CutEntities++;
                }
                else
                {
                    doomed.Add(entity.RecordIndex);
                    result.NonVisibleEntitiesRemoved++;
                }
            }
            result.MarksWithoutEntity = markHit.Count(hit => !hit);
            if (result.MarksWithoutEntity > 0)
            {
                result.MissingMarkDetail = string.Join("; ", plan.Marks
                    .Where((item, i) => !markHit[i])
                    .Select(item =>
                    {
                        double[] box = BoundsOf(item.Points.Select(p => new DxfPoint2D { X = p[0], Y = p[1] }));
                        return item.Source + " " + Describe(box);
                    }));
            }

            // Hình nằm trong lòng một vòng MARK (lỗ xuyên Ø12 trong vòng khắc Ø20) KHÔNG bị xoá: vòng MARK chỉ
            // đổi layer của chính nó, lỗ bên trong vẫn là nét cắt (quy ước người dùng 10/10/2026).

            // Chữ lạ (chú thích uốn, nhãn view…) và thực thể không phải hình (INSERT mũi
            // tên mặt cắt, HATCH, DIMENSION…): laser không cần.
            if (plan.RemoveStrayText)
            {
                foreach (EntitySamples entity in entities)
                {
                    if (doomed.Contains(entity.RecordIndex)) continue;
                    bool strayText = entity.IsText && !IsLayer(entity.Layer, settings.PartInfo.Name);
                    bool nonGeometry = !entity.IsText && (!GeometryTypes.Contains(entity.Type) || entity.Points.Count == 0);
                    if (!strayText && !nonGeometry) continue;
                    doomed.Add(entity.RecordIndex);
                    result.StrayTextRemoved++;
                }
            }

            List<DxfRecord> records = CollectEntityRecords(lines);
            foreach (KeyValuePair<int, string> pair in relayer)
            {
                DxfRecord record = records[pair.Key];
                SetGroupValue(lines, record.Start, record.EndExclusive, "8", pair.Value);
                SyncPolylineChildLayers(lines, records, pair.Key, pair.Value);
            }
            // Xoá từ cuối lên để chỉ số không trôi; POLYLINE kéo theo VERTEX/SEQEND.
            foreach (int index in doomed.OrderByDescending(i => i))
            {
                DxfRecord record = records[index];
                int endExclusive = record.EndExclusive;
                if (IsDxfType(record, "POLYLINE"))
                {
                    int cursor = index + 1;
                    while (cursor < records.Count && IsDxfType(records[cursor], "VERTEX")) cursor++;
                    if (cursor < records.Count && IsDxfType(records[cursor], "SEQEND")) cursor++;
                    if (cursor > index + 1) endExclusive = records[cursor - 1].EndExclusive;
                }
                lines.RemoveRange(record.Start, endExclusive - record.Start);
            }

            // ---------------------------------------------------------- 3b. tự ghi
            var handles = new DxfHandleAllocator(lines);
            var toWrite = new List<KeyValuePair<DxfInjectMark, string>>();
            for (int i = 0; i < plan.Bends.Count; i++)
            {
                if (bendHit[i] || plan.Bends[i].Points.Count < 2) continue;
                toWrite.Add(new KeyValuePair<DxfInjectMark, string>(
                    new DxfInjectMark { Kind = "LINE", Points = { plan.Bends[i].Points[0], plan.Bends[i].Points[plan.Bends[i].Points.Count - 1] } },
                    settings.Bend.Name));
                result.BendEntitiesInjected++;
            }
            // Cạnh MARK của model không có nét nào (SolidWorks không vẽ, hoặc nét đã bị bước khác bỏ):
            // XToolPro TỰ GHI lên LASER_MARK từ chính hình chiếu của cạnh model (như đường uốn) — vòng/ô MARK
            // không bao giờ được mất hay hở. Cạnh trùng khít nhau (hai vòng của lỗ thẳng) chỉ ghi một lần.
            var injectedMarks = new List<DxfSemanticItem>();
            for (int i = 0; i < plan.Marks.Count; i++)
            {
                DxfSemanticItem mark = plan.Marks[i];
                if (markHit[i] || mark.Points.Count < 2) continue;
                if (injectedMarks.Any(done => SameProjectedEdge(mark, done, coincident))) continue;
                toWrite.Add(new KeyValuePair<DxfInjectMark, string>(ToInjectMark(mark), settings.LaserMark.Name));
                injectedMarks.Add(mark);
                result.MarkEntitiesInjected++;
            }
            foreach (DxfSemanticItem mark in plan.UserMarks)
            {
                if (mark.Points.Count < 2) continue;
                var inject = new DxfInjectMark { Kind = mark.Kind };
                inject.Points.AddRange(mark.Points);
                toWrite.Add(new KeyValuePair<DxfInjectMark, string>(inject, settings.LaserMark.Name));
                result.UserMarkEntitiesInjected++;
            }
            if (toWrite.Count > 0)
            {
                int insertAt = FindEntitiesEnd(lines);
                if (insertAt < 0) throw new InvalidDataException("DXF không có section ENTITIES.");
                bool modern = GetAcadVersionNumber(lines) >= 1012;
                List<DxfRecord> existing = CollectEntityRecords(lines);
                string owner = existing.Count > 0
                    ? GetGroupValue(lines, existing[0].Start, existing[0].EndExclusive, "330")
                    : null;
                var added = new List<string>();
                foreach (KeyValuePair<DxfInjectMark, string> item in toWrite)
                    WriteMarkEntities(item.Key, p => p, 1.0, item.Value, modern, owner, handles, added);
                lines.InsertRange(insertAt, added);
                handles.WriteHandleSeed(lines);
                EnsureLayerRecord(lines, settings.Bend.Name, settings.Bend.Color, handles);
                EnsureLayerRecord(lines, settings.LaserMark.Name, settings.LaserMark.Color, handles);
                handles.WriteHandleSeed(lines);
            }

            // ---------------------------------------------------------- 4. tỉ lệ + gốc
            // Hình về kích thước THẬT (÷ tỉ lệ view). Chữ PART_INFO KHÔNG phóng theo: ITool ghi nó
            // trên giấy với chiều cao cài đặt (70 mm) — phóng theo view 1:6 thì thành 420 mm, view 2:1
            // còn 35 mm (lỗi «tỉ lệ sai» 06/10). Chữ giữ cỡ và giữ vị trí so với TÂM hình như cơ chế cũ
            // (view và sheet đặt 1:1 rồi mới lưu DXF).
            double k = 1.0 / scale;
            double tx = 0, ty = 0;
            List<EntitySamples> after = SampleEntities(lines);
            double[] geometryBox = BoundsOf(after.Where(e => !e.IsText).SelectMany(e => e.Points));
            if (plan.MoveToOrigin && geometryBox != null)
            {
                tx = -geometryBox[0] * k;
                ty = -geometryBox[1] * k;
            }
            string partInfoLayer = settings.PartInfo.Name;
            if (Math.Abs(k - 1) > 1e-12 || Math.Abs(tx) > 1e-12 || Math.Abs(ty) > 1e-12)
            {
                TransformEntities(lines, k, tx, ty, layer => !IsLayer(layer, partInfoLayer));
                double cx = geometryBox == null ? 0 : (geometryBox[0] + geometryBox[2]) / 2;
                double cy = geometryBox == null ? 0 : (geometryBox[1] + geometryBox[3]) / 2;
                TransformEntities(lines, 1.0, (k - 1) * cx + tx, (k - 1) * cy + ty, layer => IsLayer(layer, partInfoLayer));
            }

            // DXF ra kích thước THẬT; SolidWorks ghi $DIMLFAC = 1/tỉ lệ sheet (vd 5 cho sheet 1:5) → đo
            // bằng AutoCAD ra gấp 5 lần (báo 06/10). Hệ số đo kích thước về 1, cả header lẫn mọi DIMSTYLE.
            NormalizeDimensionScale(lines);
            NormalizeVisibleLinePatterns(lines, settings);

            string tempPath = filePath + ".semantic.tmp";
            try
            {
                File.WriteAllLines(tempPath, lines, encoding);
                File.Replace(tempPath, filePath, null);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            return result;
        }

        // ------------------------------------------------------------------
        private sealed class EntitySamples
        {
            public int RecordIndex;
            public string Type;
            public string Layer;
            public bool IsText;
            public List<DxfPoint2D> Points = new List<DxfPoint2D>();
        }

        private static List<EntitySamples> SampleEntities(List<string> lines)
        {
            var result = new List<EntitySamples>();
            List<DxfRecord> records = CollectEntityRecords(lines);
            for (int index = 0; index < records.Count; index++)
            {
                DxfRecord record = records[index];
                if (IsDxfType(record, "VERTEX") || IsDxfType(record, "SEQEND")) continue;
                var entity = new EntitySamples
                {
                    RecordIndex = index,
                    Type = (record.Type ?? "").Trim().ToUpperInvariant(),
                    Layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0"
                };
                entity.IsText = entity.Type == "TEXT" || entity.Type == "MTEXT";
                if (!entity.IsText && GeometryTypes.Contains(entity.Type))
                {
                    if (TryTessellateEntity(lines, record, LaserChordTolerance, CandidateSampleSpacing, out List<List<DxfPoint2D>> polylines))
                        foreach (List<DxfPoint2D> polyline in polylines)
                        {
                            for (int i = 0; i < polyline.Count; i++)
                            {
                                entity.Points.Add(polyline[i]);
                                // Endpoints alone can both touch real edges while an
                                // unrelated diagonal crosses the whole inside of a part.
                                if (i + 1 < polyline.Count &&
                                    (entity.Type == "LINE" || entity.Type == "POLYLINE" || entity.Type == "LWPOLYLINE"))
                                    for (int n = 1; n < 16; n++)
                                        entity.Points.Add(new DxfPoint2D {
                                            X = polyline[i].X + (polyline[i+1].X-polyline[i].X)*n/16,
                                            Y = polyline[i].Y + (polyline[i+1].Y-polyline[i].Y)*n/16 });
                            }
                        }
                }
                result.Add(entity);
            }
            return result;
        }

        private static bool IsCutCandidate(EntitySamples entity, DxfManufacturingLayerSettings settings)
        {
            return !entity.IsText && entity.Points.Count > 0 && !IsLayer(entity.Layer, settings.PartInfo.Name);
        }

        private static bool IsLayer(string layer, string name) =>
            string.Equals(layer, name, StringComparison.OrdinalIgnoreCase);

        private static double MatchRatio(List<EntitySamples> entities, LaserSegmentIndex index, DxfManufacturingLayerSettings settings)
        {
            int total = 0, near = 0;
            foreach (EntitySamples entity in entities)
            {
                if (!IsCutCandidate(entity, settings)) continue;
                foreach (DxfPoint2D point in entity.Points)
                {
                    total++;
                    if (index.IsNear(point, SemanticMatchTolerance)) near++;
                }
            }
            return total == 0 ? 0 : near / (double)total;
        }

        /// <summary>Đường tròn kín (hoặc cung khi allowArc) từ điểm mẫu: tâm từ 3 điểm, mọi điểm cách tâm như nhau.</summary>
        private static bool TryFitCircle(IList<double[]> points, out double cx, out double cy, out double radius, bool allowArc = false)
        {
            cx = cy = radius = 0;
            if (points == null || points.Count < 8) return false;
            double[] first = points[0], last = points[points.Count - 1];
            bool closed = Math.Abs(first[0] - last[0]) <= 0.02 && Math.Abs(first[1] - last[1]) <= 0.02;
            if (!closed && !allowArc) return false;
            double[] a = points[0], b = points[points.Count / 3], c = points[2 * points.Count / 3];
            double d = 2 * (a[0] * (b[1] - c[1]) + b[0] * (c[1] - a[1]) + c[0] * (a[1] - b[1]));
            if (Math.Abs(d) < 1e-9) return false;
            double a2 = a[0] * a[0] + a[1] * a[1], b2 = b[0] * b[0] + b[1] * b[1], c2 = c[0] * c[0] + c[1] * c[1];
            cx = (a2 * (b[1] - c[1]) + b2 * (c[1] - a[1]) + c2 * (a[1] - b[1])) / d;
            cy = (a2 * (c[0] - b[0]) + b2 * (a[0] - c[0]) + c2 * (b[0] - a[0])) / d;
            radius = Math.Sqrt((a[0] - cx) * (a[0] - cx) + (a[1] - cy) * (a[1] - cy));
            if (radius < 0.05) return false;
            foreach (double[] p in points)
            {
                double r = Math.Sqrt((p[0] - cx) * (p[0] - cx) + (p[1] - cy) * (p[1] - cy));
                if (Math.Abs(r - radius) > 0.05) return false;
            }
            return true;
        }

        /// <summary>Khoảng cách xa nhất từ một điểm mẫu của nét tới cạnh gần nhất trong <paramref name="index"/> (mm giấy).</summary>
        /// <summary>
        /// Tập cạnh đã chiếu để đo «nét này cách tập cạnh bao xa»: đường tròn kín đo GIẢI TÍCH (|khoảng cách tới
        /// tâm − bán kính|, không dính sai số dây cung của 72 điểm mẫu), cạnh khác đo tới dây cung.
        /// </summary>
        private sealed class ShapeSet
        {
            private readonly List<double[]> _circles = new List<double[]>();   // cx, cy, r
            private readonly LaserSegmentIndex _segments;

            public ShapeSet(IEnumerable<DxfSemanticItem> items)
            {
                var others = new List<DxfSemanticItem>();
                foreach (DxfSemanticItem item in items)
                {
                    if (TryExactCircle(item.Points, out double cx, out double cy, out double radius)) _circles.Add(new[] { cx, cy, radius });
                    else others.Add(item);
                }
                _segments = new LaserSegmentIndex(ToSegments(others), SemanticOnEdgeTolerance);
            }

            /// <summary>Khoảng cách xa nhất từ một điểm mẫu của nét tới hình gần nhất của tập (mm giấy); +∞ khi có điểm xa hơn dung sai.</summary>
            public double Deviation(List<DxfPoint2D> points)
            {
                double worst = 0;
                foreach (DxfPoint2D point in points)
                {
                    double best = _segments.NearestDistance(point, SemanticOnEdgeTolerance);
                    foreach (double[] c in _circles)
                    {
                        double d = Math.Abs(Math.Sqrt((point.X - c[0]) * (point.X - c[0]) + (point.Y - c[1]) * (point.Y - c[1])) - c[2]);
                        if (d < best) best = d;
                    }
                    if (best > worst) worst = best;
                }
                return worst;
            }
        }

        /// <summary>
        /// Hai cạnh đã chiếu TRÙNG nhau (≤ <paramref name="tolerance"/> mm giấy): đường tròn kín so tâm và
        /// bán kính (điểm mẫu của hai vòng có thể lệch pha nhau), cạnh khác so khung bao rồi từng điểm hai chiều.
        /// </summary>
        internal static bool SameProjectedEdge(DxfSemanticItem a, DxfSemanticItem b, double tolerance)
        {
            if (a == null || b == null || a.Points.Count == 0 || b.Points.Count == 0) return false;
            bool circleA = TryExactCircle(a.Points, out double ax, out double ay, out double ar);
            bool circleB = TryExactCircle(b.Points, out double bx, out double by, out double br);
            if (circleA || circleB)
            {
                return circleA && circleB &&
                       Math.Abs(ax - bx) <= tolerance && Math.Abs(ay - by) <= tolerance && Math.Abs(ar - br) <= tolerance;
            }
            // Đường cong lấy mẫu theo dây cung 0,01 mm thật: nới gấp ba cho phần sai số dây cung.
            double loose = tolerance * 3;
            double[] boxA = BoundsOf(a.Points.Select(p => new DxfPoint2D { X = p[0], Y = p[1] }));
            double[] boxB = BoundsOf(b.Points.Select(p => new DxfPoint2D { X = p[0], Y = p[1] }));
            for (int i = 0; i < 4; i++)
                if (Math.Abs(boxA[i] - boxB[i]) > loose) return false;
            return PointsOnPolyline(a.Points, b.Points, loose) && PointsOnPolyline(b.Points, a.Points, loose);
        }

        private static bool PointsOnPolyline(List<double[]> points, List<double[]> polyline, double tolerance)
        {
            double limit = tolerance * tolerance;
            foreach (double[] p in points)
            {
                var point = new DxfPoint2D { X = p[0], Y = p[1] };
                bool near = false;
                for (int i = 0; i < polyline.Count && !near; i++)
                {
                    double[] s = polyline[i], e = polyline[i + 1 < polyline.Count ? i + 1 : i];
                    var segment = new DxfLineSegment2D
                    {
                        Start = new DxfPoint2D { X = s[0], Y = s[1] },
                        End = new DxfPoint2D { X = e[0], Y = e[1] }
                    };
                    near = DistanceToSegmentSquared(point, segment) <= limit;
                }
                if (!near) return false;
            }
            return true;
        }

        /// <summary>
        /// Đường tròn KÍN đúng nghĩa từ điểm mẫu của cạnh model (SampleEdge3D lấy ≥ 73 điểm trên vòng, điểm cuối =
        /// điểm đầu): sai lệch bán kính chỉ còn sai số dấu phẩy động. Chặt hơn <see cref="TryFitCircle"/> (0,05 mm).
        /// </summary>
        private static bool TryExactCircle(IList<double[]> points, out double cx, out double cy, out double radius)
        {
            cx = cy = radius = 0;
            if (points == null || points.Count < 8) return false;
            double[] first = points[0], last = points[points.Count - 1];
            if (Math.Abs(first[0] - last[0]) > 1e-6 || Math.Abs(first[1] - last[1]) > 1e-6) return false;
            if (!TryFitCircle(points, out cx, out cy, out radius)) return false;
            double limit = Math.Max(1e-6, radius * 1e-4);
            foreach (double[] p in points)
            {
                double r = Math.Sqrt((p[0] - cx) * (p[0] - cx) + (p[1] - cy) * (p[1] - cy));
                if (Math.Abs(r - radius) > limit) return false;
            }
            return true;
        }

        /// <summary>Cạnh MARK đã chiếu → hình ghi vào DXF: CIRCLE khi là vòng kín, LINE khi thẳng, còn lại LWPOLYLINE qua điểm mẫu.</summary>
        private static DxfInjectMark ToInjectMark(DxfSemanticItem item)
        {
            List<double[]> p = item.Points;
            if (TryExactCircle(p, out double cx, out double cy, out double radius))
                return new DxfInjectMark { Kind = "CIRCLE", Points = { new[] { cx, cy, 0.0 }, new[] { cx + radius, cy, 0.0 } } };
            double[] a = p[0], b = p[p.Count - 1];
            double dx = b[0] - a[0], dy = b[1] - a[1];
            double length = Math.Sqrt(dx * dx + dy * dy);
            bool straight = length > 1e-9 && p.All(q => Math.Abs((q[0] - a[0]) * dy - (q[1] - a[1]) * dx) / length <= Math.Max(1e-6, length * 1e-6));
            if (straight)
                return new DxfInjectMark { Kind = "LINE", Points = { a, b } };
            var polyline = new DxfInjectMark { Kind = "POLYLINE" };
            polyline.Points.AddRange(p);
            return polyline;
        }

        /// <summary>
        /// Cạnh MARK nào có điểm GIỮA nằm trên nét LASER_MARK vừa đặt (≤ dung sai, đo tới dây cung của nét). Chặt hơn
        /// <see cref="NoteHits"/> (0,28 mm giấy = 4,2 mm thật ở 1:15, đủ để cạnh đối diện của ô MARK 2 mm «che» một
        /// cạnh đã mất) — cạnh không có nét phải được nhận ra để tự ghi lại.
        /// </summary>
        /// <summary>Số điểm kiểm trên mỗi cạnh MARK (1/4, 1/2, 3/4 chiều dài).</summary>
        private const int MarkSampleCount = 3;

        /// <summary>
        /// Điểm 1/4, 1/2, 3/4 CHIỀU DÀI của cạnh MARK. Trước v150 chỉ lấy p[Count/2]: cạnh thẳng chỉ có 2 điểm
        /// (GetTessPts của spline thẳng) thì đó là ĐẦU MÚT — chạm cạnh kề nên cạnh SolidWorks không vẽ vẫn bị coi
        /// là «đã có nét», không được tự ghi → ô MARK hở.
        /// </summary>
        private static DxfPoint2D[] MarkSamplePoints(DxfSemanticItem item)
        {
            var samples = new DxfPoint2D[MarkSampleCount];
            List<double[]> p = item.Points;
            if (p.Count == 0) return new DxfPoint2D[0];
            double total = 0;
            for (int i = 1; i < p.Count; i++) total += Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
            for (int s = 0; s < MarkSampleCount; s++)
            {
                double target = total * (s + 1) / (MarkSampleCount + 1), walked = 0;
                samples[s] = new DxfPoint2D { X = p[p.Count - 1][0], Y = p[p.Count - 1][1] };
                if (p.Count == 1 || total <= 0) { samples[s] = new DxfPoint2D { X = p[0][0], Y = p[0][1] }; continue; }
                for (int i = 1; i < p.Count; i++)
                {
                    double length = Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]));
                    if (walked + length >= target && length > 0)
                    {
                        double t = (target - walked) / length;
                        samples[s] = new DxfPoint2D { X = p[i - 1][0] + ((p[i][0] - p[i - 1][0]) * t), Y = p[i - 1][1] + ((p[i][1] - p[i - 1][1]) * t) };
                        break;
                    }
                    walked += length;
                }
            }
            return samples;
        }

        private static double Sq(double v) => v * v;

        /// <summary>Cạnh MARK «đã có nét» khi CẢ BA điểm kiểm nằm trên nét (một hay nhiều nét) của SolidWorks.</summary>
        private static void NoteMarkHits(List<DxfPoint2D[]> samples, List<bool[]> sampleHits, bool[] hits, List<DxfPoint2D> points)
        {
            double tol2 = SemanticOnEdgeTolerance * SemanticOnEdgeTolerance;
            for (int i = 0; i < samples.Count; i++)
            {
                if (hits[i] || samples[i].Length == 0) continue;
                for (int s = 0; s < samples[i].Length; s++)
                {
                    if (sampleHits[i][s]) continue;
                    for (int k = 0; k < points.Count; k++)
                    {
                        var segment = new DxfLineSegment2D { Start = points[k], End = points[k + 1 < points.Count ? k + 1 : k] };
                        if (DistanceToSegmentSquared(samples[i][s], segment) <= tol2) { sampleHits[i][s] = true; break; }
                    }
                }
                hits[i] = sampleHits[i].All(hit => hit);
            }
        }

        private static void NoteHits(List<DxfSemanticItem> items, bool[] hits, List<DxfPoint2D> points)
        {
            double tol2 = (CandidateSampleSpacing + SemanticOnEdgeTolerance) * (CandidateSampleSpacing + SemanticOnEdgeTolerance);
            for (int i = 0; i < items.Count; i++)
            {
                if (hits[i]) continue;
                List<double[]> p = items[i].Points;
                if (p.Count == 0) continue;
                double[] mid = p[p.Count / 2];
                foreach (DxfPoint2D point in points)
                {
                    double dx = point.X - mid[0], dy = point.Y - mid[1];
                    if (dx * dx + dy * dy <= tol2) { hits[i] = true; break; }
                }
            }
        }

        private static List<LaserSegment> ExpectedSegments(DxfSemanticPlan plan)
        {
            var segments = ToSegments(plan.VisibleEdges);
            segments.AddRange(ToSegments(plan.Marks));
            segments.AddRange(ToSegments(plan.Erase));
            segments.AddRange(ToSegments(plan.Bends));
            segments.AddRange(ToSegments(plan.UserMarks));
            return segments;
        }

        private static List<LaserSegment> ToSegments(IEnumerable<DxfSemanticItem> items)
        {
            var segments = new List<LaserSegment>();
            foreach (DxfSemanticItem item in items)
            {
                List<double[]> p = item.Points;
                if (p.Count == 1)
                {
                    var single = new DxfPoint2D { X = p[0][0], Y = p[0][1] };
                    segments.Add(new LaserSegment { Segment = new DxfLineSegment2D { Start = single, End = single }, IsStraight = true });
                    continue;
                }
                for (int i = 1; i < p.Count; i++)
                {
                    segments.Add(new LaserSegment
                    {
                        Segment = new DxfLineSegment2D
                        {
                            Start = new DxfPoint2D { X = p[i - 1][0], Y = p[i - 1][1] },
                            End = new DxfPoint2D { X = p[i][0], Y = p[i][1] }
                        },
                        IsStraight = p.Count == 2
                    });
                }
            }
            return segments;
        }

        private static void ShiftPlan(DxfSemanticPlan plan, double dx, double dy)
        {
            foreach (List<DxfSemanticItem> list in new[] { plan.VisibleEdges, plan.Marks, plan.Erase, plan.Bends, plan.UserMarks })
                foreach (DxfSemanticItem item in list)
                    foreach (double[] p in item.Points)
                    {
                        p[0] += dx;
                        p[1] += dy;
                    }
        }

        private static double[] BoundsOf(IEnumerable<DxfPoint2D> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;
            foreach (DxfPoint2D p in points)
            {
                any = true;
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return any ? new[] { minX, minY, maxX, maxY } : null;
        }

        private static string Describe(double[] box) =>
            box == null
                ? "trống"
                : (box[2] - box[0]).ToString("0.##", CultureInfo.InvariantCulture) + "×" +
                  (box[3] - box[1]).ToString("0.##", CultureInfo.InvariantCulture) + " mm tại (" +
                  box[0].ToString("0.##", CultureInfo.InvariantCulture) + "," + box[1].ToString("0.##", CultureInfo.InvariantCulture) + ")";

        private static void SyncPolylineChildLayers(List<string> lines, List<DxfRecord> records, int index, string layer)
        {
            if (!IsDxfType(records[index], "POLYLINE")) return;
            for (int cursor = index + 1; cursor < records.Count; cursor++)
            {
                if (!IsDxfType(records[cursor], "VERTEX") && !IsDxfType(records[cursor], "SEQEND")) break;
                SetGroupValue(lines, records[cursor].Start, records[cursor].EndExclusive, "8", layer);
            }
        }

        // ------------------------------------------------------------------
        // Phép đồng dạng x' = k·x + t trên mọi thực thể (kể cả VERTEX, chữ).
        // ------------------------------------------------------------------
        private static readonly HashSet<string> VectorOnlyTypes11 =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MTEXT", "ELLIPSE" };   // 11/21 là VECTOR; SPLINE 11/21 là điểm khớp

        private static void TransformEntities(List<string> lines, double k, double tx, double ty, Func<string, bool> includeLayer = null)
        {
            foreach (DxfRecord record in CollectEntityRecords(lines))
            {
                if (includeLayer != null &&
                    !includeLayer(GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0"))
                    continue;
                string type = (record.Type ?? "").Trim().ToUpperInvariant();
                double normalZ = GetNormalZ(lines, record.Start, record.EndExclusive);
                bool ocsMirrored = normalZ < 0 && (type == "ARC" || type == "CIRCLE" || type == "LWPOLYLINE" || type == "TEXT" || type == "MTEXT");
                double ocsTx = ocsMirrored ? -tx : tx;
                bool splineTangents = type == "SPLINE";
                for (int i = record.Start; i + 1 < record.EndExclusive; i += 2)
                {
                    string code = lines[i].Trim();
                    switch (code)
                    {
                        case "10": Scale(lines, i, k, ocsTx); break;
                        case "20": Scale(lines, i, k, ty); break;
                        case "11":
                            if (VectorOnlyTypes11.Contains(type)) Scale(lines, i, k, 0);
                            else Scale(lines, i, k, ocsTx);
                            break;
                        case "21":
                            if (VectorOnlyTypes11.Contains(type)) Scale(lines, i, k, 0);
                            else Scale(lines, i, k, ty);
                            break;
                        case "12": case "13": Scale(lines, i, k, splineTangents ? 0 : ocsTx); break;
                        case "22": case "23": Scale(lines, i, k, splineTangents ? 0 : ty); break;
                        case "40":
                            if (type != "SPLINE" && type != "ELLIPSE") Scale(lines, i, k, 0);   // bán kính / chiều cao chữ
                            break;
                        case "41":
                            if (type == "INSERT" || type == "TEXT" || type == "MTEXT") Scale(lines, i, k, 0);
                            break;
                        case "42":
                            if (type == "INSERT") Scale(lines, i, k, 0);
                            break;
                        case "43":
                            if (type == "INSERT" || type == "LWPOLYLINE") Scale(lines, i, k, 0);
                            break;
                    }
                }
            }
        }

        /// <summary>$DIMLFAC (header) và nhóm 144 (DIMLFAC) của mọi bản ghi DIMSTYLE → 1.</summary>
        internal static void NormalizeDimensionScale(List<string> lines)
        {
            string section = string.Empty;
            string record = string.Empty;
            for (int i = 0; i + 1 < lines.Count; i += 2)
            {
                string code = lines[i].Trim();
                string value = lines[i + 1].Trim();
                if (code == "0" && value == "SECTION" && i + 3 < lines.Count)
                {
                    section = lines[i + 3].Trim();
                    continue;
                }
                if (code == "0" && value == "ENDSEC")
                {
                    section = string.Empty;
                    continue;
                }
                if (section == "HEADER" && code == "9" && value == "$DIMLFAC" && i + 3 < lines.Count)
                {
                    lines[i + 3] = "1.0";
                    continue;
                }
                if (section != "TABLES") continue;
                if (code == "0") record = value;
                else if (code == "144" && record == "DIMSTYLE") lines[i + 1] = "1.0";
            }
        }

        private static void Scale(List<string> lines, int codeIndex, double k, double t)
        {
            if (double.TryParse(lines[codeIndex + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                lines[codeIndex + 1] = (value * k + t).ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
