using System;
using System.Collections.Generic;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    public class SketchPointSnapshot
    {
        public int Id1;
        public int Id2;
        public int Index;
        public double X;
        public double Y;
    }

    public class SketchSlotSnapshot
    {
        public int CreationType;
        public int LengthType;
        public double Length;
        public double Width;
        public double X1, Y1, Z1;
        public double X2, Y2, Z2;
        public double X3, Y3, Z3;
        public int CenterArcDirection;
    }

    public static class SketchOperationsHelper
    {
        /// <summary>
        /// Lưu lại thông số các rãnh Slot nguyên bản trong Sketch trước khi rebuild
        /// </summary>
        public static List<SketchSlotSnapshot> CapturePristineSketchSlots(Sketch swSketch)
        {
            List<SketchSlotSnapshot> list = new List<SketchSlotSnapshot>();
            if (swSketch == null) return list;

            int slotCount = 0;
            try { slotCount = swSketch.GetSketchSlotCount(); } catch { }
            if (slotCount <= 0) return list;

            object[] slots = swSketch.GetSketchSlots() as object[];
            if (slots == null) return list;

            for (int i = 0; i < slots.Length; i++)
            {
                ISketchSlot slot = slots[i] as ISketchSlot;
                if (slot == null) continue;

                object[] pts = slot.GetSlotPoints() as object[];
                SketchPoint p0 = (pts != null && pts.Length > 0) ? pts[0] as SketchPoint : null;
                SketchPoint p1 = (pts != null && pts.Length > 1) ? pts[1] as SketchPoint : null;
                SketchPoint p2 = (pts != null && pts.Length > 2) ? pts[2] as SketchPoint : null;

                list.Add(new SketchSlotSnapshot
                {
                    CreationType = slot.CreationType,
                    LengthType = slot.LengthType,
                    Length = slot.Length,
                    Width = slot.Width,
                    X1 = p0?.X ?? 0,
                    Y1 = p0?.Y ?? 0,
                    Z1 = p0?.Z ?? 0,
                    X2 = p1?.X ?? 0,
                    Y2 = p1?.Y ?? 0,
                    Z2 = p1?.Z ?? 0,
                    X3 = p2?.X ?? 0,
                    Y3 = p2?.Y ?? 0,
                    Z3 = p2?.Z ?? 0,
                    CenterArcDirection = slot.CenterArcDirection
                });
            }

            return list;
        }

        /// <summary>
        /// Lưu lại bản đồ tọa độ sạch nguyên bản của mọi điểm trong Sketch trước khi có bất kỳ Feature nào Rebuild
        /// </summary>
        public static List<SketchPointSnapshot> CapturePristineSketchPoints(Sketch swSketch)
        {
            List<SketchPointSnapshot> list = new List<SketchPointSnapshot>();
            if (swSketch == null) return list;

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return list;

            for (int i = 0; i < sketchPointsObj.Length; i++)
            {
                SketchPoint pt = sketchPointsObj[i] as SketchPoint;
                if (pt == null) continue;

                int id1 = 0, id2 = 0;
                try
                {
                    int[] idArr = pt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2)
                    {
                        id1 = idArr[0];
                        id2 = idArr[1];
                    }
                }
                catch { }

                list.Add(new SketchPointSnapshot
                {
                    Id1 = id1,
                    Id2 = id2,
                    Index = i,
                    X = pt.X,
                    Y = pt.Y
                });
            }

            return list;
        }

        /// <summary>
        /// Mở Sketch và Xóa toàn bộ Ràng buộc (Relations) + Kích thước (Dimensions)
        /// Trả về đối tượng Sketch để tiếp tục thực hiện Bước 2 (Dịch chuyển Điểm)
        /// </summary>
        public static Sketch FreeSketchForMutation(ModelDoc2 partDoc, Feature sketchFeat)
        {
            if (partDoc == null || sketchFeat == null) return null;

            Sketch swSketch = sketchFeat.GetSpecificFeature2() as Sketch;
            if (swSketch == null) return null;

            // 1. Kích hoạt chế độ Edit Sketch (Bắt buộc phải mở Sketch mới can thiệp được)
            sketchFeat.Select2(false, 0);
            partDoc.EditSketch();

            // 2. XÓA TOÀN BỘ KÍCH THƯỚC (DIMENSIONS)
            DisplayDimension dispDim = sketchFeat.GetFirstDisplayDimension() as DisplayDimension;
            List<string> dimNames = new List<string>();
            List<DisplayDimension> dispDims = new List<DisplayDimension>();
            
            while (dispDim != null)
            {
                Dimension dim = dispDim.GetDimension() as Dimension;
                if (dim != null)
                {
                    dimNames.Add(dim.Name + "@" + sketchFeat.Name);
                    dispDims.Add(dispDim);
                }
                dispDim = sketchFeat.GetNextDisplayDimension(dispDim) as DisplayDimension;
            }

            CreateMirrorPartPackage.LogDebug($"[MUTATION] Sketch {sketchFeat.Name} has {dimNames.Count} dimensions.");
            
            if (dimNames.Count > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (string dimName in dimNames)
                {
                    bool sel = partDoc.Extension.SelectByID2(dimName, "DIMENSION", 0, 0, 0, true, 0, null, 0);
                    CreateMirrorPartPackage.LogDebug($"[MUTATION] Select {dimName} -> {sel}");
                }
                
                // Thử select qua DisplayDimension.Select
                foreach (DisplayDimension dd in dispDims)
                {
                    Annotation ann = dd.GetAnnotation() as Annotation;
                    if (ann != null)
                    {
                        bool selAnn = ann.Select3(true, null);
                        CreateMirrorPartPackage.LogDebug($"[MUTATION] Select Annotation -> {selAnn}");
                    }
                }

                bool delSuccess = partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                CreateMirrorPartPackage.LogDebug($"[MUTATION] DeleteSelection2 -> {delSuccess}");
            }

            // 3. XÓA TOÀN BỘ RÀNG BUỘC HÌNH HỌC (RELATIONS) - NATIVE API
            ISketchRelationManager relMgr = swSketch.RelationManager;
            if (relMgr != null)
            {
                // Gọi API gốc của SolidWorks để tận diệt mọi Relation (kể cả external, dangling)
                relMgr.DeleteAllRelations();
                CreateMirrorPartPackage.LogDebug($"[MUTATION] Deleted all relations via native API.");
            }

            // CHÚ Ý: CHÚNG TA KHÔNG THOÁT SKETCH Ở ĐÂY!
            // Giữ nguyên trạng thái Edit Sketch để Bước 2 ngay lập tức can thiệp vào tọa độ điểm.
            
            return swSketch;
        }

        /// <summary>
        /// Di chuyển toàn bộ các điểm trong Sketch qua mặt phẳng đối xứng (Bảo toàn Internal ID)
        /// Mặc định: Lật đối xứng qua trục Y của Sketch (newX = -x, newY = y)
        /// </summary>
        public static void MutateSketchPoints(ModelDoc2 partDoc, Sketch swSketch, List<SketchPointSnapshot> pristinePoints = null)
        {
            if (partDoc == null || swSketch == null) return;

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return;

            int ptIdx = 0;
            foreach (object ptObj in sketchPointsObj)
            {
                SketchPoint swPt = ptObj as SketchPoint;
                if (swPt == null) continue;

                SketchPointSnapshot snap = null;
                try
                {
                    int[] idArr = swPt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2 && pristinePoints != null)
                    {
                        snap = pristinePoints.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                    }
                }
                catch { }

                if (snap == null && pristinePoints != null && ptIdx < pristinePoints.Count)
                {
                    snap = pristinePoints[ptIdx];
                }

                double x = (snap != null) ? snap.X : swPt.X;
                double y = (snap != null) ? snap.Y : swPt.Y;

                double newX = -x;
                double newY = y; 

                CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_{ptIdx++}] (source: {x * 1000.0:F3}, {y * 1000.0:F3}mm, live: {swPt.X * 1000.0:F3}, {swPt.Y * 1000.0:F3}mm) -> target: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");

                swPt.X = newX;
                swPt.Y = newY;
            }

            partDoc.InsertSketch2(true); 
        }

        /// <summary>
        /// Di chuyển toàn bộ các điểm trong Sketch phản chiếu qua một trục 2D bất kỳ (ax1, ay1) -> (ax2, ay2)
        /// </summary>
        public static void MutateSketchPoints(ModelDoc2 partDoc, Sketch swSketch, double ax1, double ay1, double ax2, double ay2, List<SketchPointSnapshot> pristinePoints = null)
        {
            if (partDoc == null || swSketch == null) return;

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return;

            double dx = ax2 - ax1;
            double dy = ay2 - ay1;
            double len = Math.Sqrt(dx * dx + dy * dy);

            bool useGeneralLine = (len > 1e-9);
            double nx = 0, ny = 0;
            if (useGeneralLine)
            {
                double ux = dx / len;
                double uy = dy / len;
                nx = -uy;
                ny = ux;
            }

            CreateMirrorPartPackage.LogDebug($"[MUTATION_AXIS] axis1=({ax1:F6},{ay1:F6}) axis2=({ax2:F6},{ay2:F6}) len={len:F6} useGeneralLine={useGeneralLine} nx={nx:F6} ny={ny:F6} pristineCount={pristinePoints?.Count ?? 0}");
            int ptIdx = 0;
            foreach (object ptObj in sketchPointsObj)
            {
                SketchPoint swPt = ptObj as SketchPoint;
                if (swPt == null) continue;

                // Ưu tiên 1: Tìm tọa độ gốc nguyên bản theo ID
                SketchPointSnapshot snap = null;
                try
                {
                    int[] idArr = swPt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2 && pristinePoints != null)
                    {
                        snap = pristinePoints.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                    }
                }
                catch { }

                // Ưu tiên 2: Tìm theo Index nếu không khớp ID
                if (snap == null && pristinePoints != null && ptIdx < pristinePoints.Count)
                {
                    snap = pristinePoints[ptIdx];
                }

                double x = (snap != null) ? snap.X : swPt.X;
                double y = (snap != null) ? snap.Y : swPt.Y;

                double newX, newY;
                if (useGeneralLine)
                {
                    double dist = (x - ax1) * nx + (y - ay1) * ny;
                    newX = x - 2.0 * dist * nx;
                    newY = y - 2.0 * dist * ny;
                }
                else
                {
                    newX = -x;
                    newY = y;
                }

                CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_{ptIdx++}] (source: {x * 1000.0:F3}, {y * 1000.0:F3}mm, live: {swPt.X * 1000.0:F3}, {swPt.Y * 1000.0:F3}mm) -> target: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");

                swPt.X = newX;
                swPt.Y = newY;
            }

            partDoc.InsertSketch2(true);
        }

        /// <summary>
        /// Tái tạo các rãnh Slot đối xứng hoàn chỉnh với đầy đủ ràng buộc hình học và kích thước nguyên bản
        /// </summary>
        public static void RecreateMirroredSlots(ModelDoc2 partDoc, Sketch swSketch, double ax1, double ay1, double ax2, double ay2, List<SketchSlotSnapshot> pristineSlots)
        {
            if (partDoc == null || swSketch == null || pristineSlots == null || pristineSlots.Count == 0) return;

            // 1. Xóa các đoạn vẽ cũ và điểm cũ trong Sketch đang mở
            object[] segs = swSketch.GetSketchSegments() as object[];
            if (segs != null && segs.Length > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (object sObj in segs)
                {
                    SketchSegment s = sObj as SketchSegment;
                    if (s != null)
                    {
                        s.Select4(true, null);
                    }
                }
                partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }

            object[] remainingPts = swSketch.GetSketchPoints2() as object[];
            if (remainingPts != null && remainingPts.Length > 0)
            {
                partDoc.ClearSelection2(true);
                foreach (object pObj in remainingPts)
                {
                    SketchPoint p = pObj as SketchPoint;
                    if (p != null)
                    {
                        p.Select4(true, null);
                    }
                }
                partDoc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            }

            // 2. Tắt tạm thời Automatic Relations và Inference để SolidWorks không bắt dính/lệch tâm vào các cạnh lân cận
            ISldWorks swApp = null;
            bool oldAutoRel = true;
            bool oldInference = true;
            try
            {
                swApp = SwAddin.InstanceSwApp;
                if (swApp == null)
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                if (swApp != null)
                {
                    oldAutoRel = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
                    oldInference = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                }
            }
            catch { }

            try
            {
                // 3. Chuẩn bị trục đối xứng 2D
                double dx = ax2 - ax1;
                double dy = ay2 - ay1;
                double len = Math.Sqrt(dx * dx + dy * dy);

                bool useGeneralLine = (len > 1e-9);
                double nx = 0, ny = 0;
                if (useGeneralLine)
                {
                    double ux = dx / len;
                    double uy = dy / len;
                    nx = -uy;
                    ny = ux;
                }

                // 4. Tái tạo từng Slot bằng API chuẩn CreateSketchSlot
                for (int i = 0; i < pristineSlots.Count; i++)
                {
                    SketchSlotSnapshot slotSnap = pristineSlots[i];

                    double newX1, newY1;
                    double newX2, newY2;
                    double newX3 = 0, newY3 = 0;

                    if (useGeneralLine)
                    {
                        double dist1 = (slotSnap.X1 - ax1) * nx + (slotSnap.Y1 - ay1) * ny;
                        newX1 = slotSnap.X1 - 2.0 * dist1 * nx;
                        newY1 = slotSnap.Y1 - 2.0 * dist1 * ny;

                        double dist2 = (slotSnap.X2 - ax1) * nx + (slotSnap.Y2 - ay1) * ny;
                        newX2 = slotSnap.X2 - 2.0 * dist2 * nx;
                        newY2 = slotSnap.Y2 - 2.0 * dist2 * ny;

                        if (slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_3pointarc)
                        {
                            double dist3 = (slotSnap.X3 - ax1) * nx + (slotSnap.Y3 - ay1) * ny;
                            newX3 = slotSnap.X3 - 2.0 * dist3 * nx;
                            newY3 = slotSnap.Y3 - 2.0 * dist3 * ny;
                        }
                    }
                    else
                    {
                        newX1 = -slotSnap.X1;
                        newY1 = slotSnap.Y1;

                        newX2 = -slotSnap.X2;
                        newY2 = slotSnap.Y2;

                        newX3 = -slotSnap.X3;
                        newY3 = slotSnap.Y3;
                    }

                    // Lưu ý: slotSnap.X1/Y1 và X2/Y2 từ GetSlotPoints() luôn là 2 tâm cung tròn (arc centers).
                    // Do đó với straight slot, bắt buộc phải dùng CenterCenter để SolidWorks không tự offset thêm Width/2.
                    int slotLenType = (slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_line)
                        ? (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter
                        : slotSnap.LengthType;

                    SketchSlot newSlot = partDoc.SketchManager.CreateSketchSlot(
                        slotSnap.CreationType,
                        slotLenType,
                        slotSnap.Width,
                        newX1, newY1, 0.0,
                        newX2, newY2, 0.0,
                        newX3, newY3, 0.0,
                        slotSnap.CenterArcDirection,
                        false);

                    // Khóa cứng (Fix) 2 tâm cung tròn để Slot có đầy đủ ràng buộc vị trí, không bị dịch chuyển/dưới định nghĩa
                    if (newSlot != null)
                    {
                        try
                        {
                            object[] pts = newSlot.GetSlotPoints() as object[];
                            if (pts != null)
                            {
                                for (int pIdx = 0; pIdx < Math.Min(2, pts.Length); pIdx++)
                                {
                                    SketchPoint sp = pts[pIdx] as SketchPoint;
                                    if (sp != null)
                                    {
                                        partDoc.ClearSelection2(true);
                                        sp.Select4(false, null);
                                        partDoc.SketchAddConstraints("sgFIXED");
                                    }
                                }
                                partDoc.ClearSelection2(true);
                            }
                        }
                        catch { }
                    }

                    CreateMirrorPartPackage.LogDebug($"[SLOT_RECREATED_{i}] type={slotSnap.CreationType} L={slotSnap.Length * 1000.0:F2}mm W={slotSnap.Width * 1000.0:F2}mm P1=({newX1 * 1000.0:F2},{newY1 * 1000.0:F2}) P2=({newX2 * 1000.0:F2},{newY2 * 1000.0:F2}) created={newSlot != null}");
                }
            }
            finally
            {
                if (swApp != null)
                {
                    try
                    {
                        swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, oldAutoRel);
                        swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, oldInference);
                    }
                    catch { }
                }
            }

            partDoc.InsertSketch2(true);
        }

        /// <summary>
        /// Lật hướng mũi tên lệnh Extrude Cut
        /// </summary>
        public static bool ReverseExtrudeDirection(ModelDoc2 partDoc, Feature cutFeature)
        {
            if (partDoc == null || cutFeature == null) return false;

            IExtrudeFeatureData2 def = cutFeature.GetDefinition() as IExtrudeFeatureData2;
            if (def != null)
            {
                bool access = def.AccessSelections(partDoc, null);
                CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] Feature {cutFeature.Name} AccessSelections={access}");
                if (access)
                {
                    // Chỉ đảo duy nhất hướng đùn, giữ nguyên mọi thông số khác
                    def.ReverseDirection = !def.ReverseDirection;
                    
                    // Thử check EndCondition
                    int endCond = def.GetEndCondition(true);
                    CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] EndCondition={endCond} ReverseDirection={def.ReverseDirection}");
                    
                    bool success = cutFeature.ModifyDefinition(def, partDoc, null);
                    if (!success && endCond == (int)swEndConditions_e.swEndCondUpToNext)
                    {
                        CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] UpToNext failed with reversed direction. Trying ThroughAll fallback...");
                        try
                        {
                            def.SetEndCondition(true, (int)swEndConditions_e.swEndCondThroughAll);
                            success = cutFeature.ModifyDefinition(def, partDoc, null);
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ThroughAll fallback result={success}");
                        }
                        catch (Exception ex)
                        {
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ThroughAll fallback exception: {ex.Message}");
                        }
                    }

                    def.ReleaseSelectionAccess();
                    
                    CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ModifyDefinition={success}");
                    return success;
                }
            }
            return false;
        }
    }
}
