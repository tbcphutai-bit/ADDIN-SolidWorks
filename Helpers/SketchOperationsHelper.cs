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
        public double ModelX;
        public double ModelY;
        public double ModelZ;
        public bool HasModelCoords;
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
        public double ModelX1, ModelY1, ModelZ1;
        public double ModelX2, ModelY2, ModelZ2;
        public double ModelX3, ModelY3, ModelZ3;
        public bool HasModelCoords;
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

            MathTransform s2m = null;
            try
            {
                MathTransform m2s = swSketch.ModelToSketchTransform;
                if (m2s != null) s2m = m2s.Inverse() as MathTransform;
            }
            catch { }

            ISldWorks swApp = SwAddin.InstanceSwApp;
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

            for (int i = 0; i < slots.Length; i++)
            {
                ISketchSlot slot = slots[i] as ISketchSlot;
                if (slot == null) continue;

                object[] pts = slot.GetSlotPoints() as object[];
                SketchPoint p0 = (pts != null && pts.Length > 0) ? pts[0] as SketchPoint : null;
                SketchPoint p1 = (pts != null && pts.Length > 1) ? pts[1] as SketchPoint : null;
                SketchPoint p2 = (pts != null && pts.Length > 2) ? pts[2] as SketchPoint : null;

                double x1 = p0?.X ?? 0, y1 = p0?.Y ?? 0, z1 = p0?.Z ?? 0;
                double x2 = p1?.X ?? 0, y2 = p1?.Y ?? 0, z2 = p1?.Z ?? 0;
                double x3 = p2?.X ?? 0, y3 = p2?.Y ?? 0, z3 = p2?.Z ?? 0;

                double mx1 = 0, my1 = 0, mz1 = 0;
                double mx2 = 0, my2 = 0, mz2 = 0;
                double mx3 = 0, my3 = 0, mz3 = 0;
                bool hasModel = false;

                if (s2m != null && mathUtility != null)
                {
                    try
                    {
                        MathPoint mp1 = mathUtility.CreatePoint(new double[] { x1, y1, 0.0 }) as MathPoint;
                        MathPoint mpm1 = mp1 != null ? mp1.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a1 = mpm1 != null ? mpm1.ArrayData as double[] : null;
                        if (a1 != null && a1.Length >= 3) { mx1 = a1[0]; my1 = a1[1]; mz1 = a1[2]; }

                        MathPoint mp2 = mathUtility.CreatePoint(new double[] { x2, y2, 0.0 }) as MathPoint;
                        MathPoint mpm2 = mp2 != null ? mp2.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a2 = mpm2 != null ? mpm2.ArrayData as double[] : null;
                        if (a2 != null && a2.Length >= 3) { mx2 = a2[0]; my2 = a2[1]; mz2 = a2[2]; }

                        if (p2 != null)
                        {
                            MathPoint mp3 = mathUtility.CreatePoint(new double[] { x3, y3, 0.0 }) as MathPoint;
                            MathPoint mpm3 = mp3 != null ? mp3.MultiplyTransform(s2m) as MathPoint : null;
                            double[] a3 = mpm3 != null ? mpm3.ArrayData as double[] : null;
                            if (a3 != null && a3.Length >= 3) { mx3 = a3[0]; my3 = a3[1]; mz3 = a3[2]; }
                        }
                        hasModel = true;
                    }
                    catch { }
                }

                list.Add(new SketchSlotSnapshot
                {
                    CreationType = slot.CreationType,
                    LengthType = slot.LengthType,
                    Length = slot.Length,
                    Width = slot.Width,
                    X1 = x1, Y1 = y1, Z1 = z1,
                    X2 = x2, Y2 = y2, Z2 = z2,
                    X3 = x3, Y3 = y3, Z3 = z3,
                    ModelX1 = mx1, ModelY1 = my1, ModelZ1 = mz1,
                    ModelX2 = mx2, ModelY2 = my2, ModelZ2 = mz2,
                    ModelX3 = mx3, ModelY3 = my3, ModelZ3 = mz3,
                    HasModelCoords = hasModel,
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

            MathTransform s2m = null;
            try
            {
                MathTransform m2s = swSketch.ModelToSketchTransform;
                if (m2s != null) s2m = m2s.Inverse() as MathTransform;
            }
            catch { }

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return list;

            ISldWorks swApp = SwAddin.InstanceSwApp;
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

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

                double mx = 0, my = 0, mz = 0;
                bool hasModel = false;
                if (s2m != null && mathUtility != null)
                {
                    try
                    {
                        MathPoint mp2D = mathUtility.CreatePoint(new double[] { pt.X, pt.Y, 0.0 }) as MathPoint;
                        MathPoint mp3D = mp2D != null ? mp2D.MultiplyTransform(s2m) as MathPoint : null;
                        double[] a = mp3D != null ? mp3D.ArrayData as double[] : null;
                        if (a != null && a.Length >= 3)
                        {
                            mx = a[0]; my = a[1]; mz = a[2];
                            hasModel = true;
                        }
                    }
                    catch { }
                }

                list.Add(new SketchPointSnapshot
                {
                    Id1 = id1,
                    Id2 = id2,
                    Index = i,
                    X = pt.X,
                    Y = pt.Y,
                    ModelX = mx,
                    ModelY = my,
                    ModelZ = mz,
                    HasModelCoords = hasModel
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
        /// Mặc định: Lật đối xứng qua trục Y của Sketch (newX = -x, newY = y) hoặc theo tọa độ 3D bất biến
        /// </summary>
        public static void MutateSketchPoints(ModelDoc2 partDoc, Sketch swSketch, List<SketchPointSnapshot> pristinePoints = null, PlaneData mirrorPlane = null)
        {
            MutateSketchPoints(partDoc, swSketch, 0.0, 0.0, 0.0, 0.0, pristinePoints, mirrorPlane);
        }

        /// <summary>
        /// Di chuyển toàn bộ các điểm trong Sketch phản chiếu qua tọa độ 3D không gian bất biến (hoặc trục 2D ax1, ay1 -> ax2, ay2)
        /// </summary>
        public static void MutateSketchPoints(
            ModelDoc2 partDoc, 
            Sketch swSketch, 
            double ax1, double ay1, 
            double ax2, double ay2, 
            List<SketchPointSnapshot> pristinePoints = null,
            PlaneData mirrorPlane = null)
        {
            if (partDoc == null || swSketch == null) return;

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return;

            MathTransform m2s = null;
            try { m2s = swSketch.ModelToSketchTransform; } catch { }

            ISldWorks swApp = SwAddin.InstanceSwApp;
            if (swApp == null)
            {
                try
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                catch { }
            }
            IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

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

            bool isParallelPlane = false;
            if (mirrorPlane != null && mirrorPlane.Normal != null && mirrorPlane.Normal.Length >= 3 && m2s != null && mathUtility != null)
            {
                try
                {
                    MathTransform s2m = m2s.Inverse() as MathTransform;
                    if (s2m != null)
                    {
                        MathVector zVec = mathUtility.CreateVector(new double[] { 0, 0, 1 }) as MathVector;
                        MathVector nVec = (zVec != null) ? zVec.MultiplyTransform(s2m) as MathVector : null;
                        double[] n = (nVec != null) ? nVec.ArrayData as double[] : null;
                        if (n != null && n.Length >= 3)
                        {
                            double dot = n[0] * mirrorPlane.Normal[0] + n[1] * mirrorPlane.Normal[1] + n[2] * mirrorPlane.Normal[2];
                            isParallelPlane = (Math.Abs(Math.Abs(dot) - 1.0) < 0.05);
                        }
                    }
                }
                catch { }
            }

            CreateMirrorPartPackage.LogDebug($"[MUTATION_AXIS] axis1=({ax1:F6},{ay1:F6}) axis2=({ax2:F6},{ay2:F6}) len={len:F6} useGeneralLine={useGeneralLine} nx={nx:F6} ny={ny:F6} isParallel={isParallelPlane} pristineCount={pristinePoints?.Count ?? 0} hasMirrorPlane={mirrorPlane != null}");
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

                double newX = 0, newY = 0;
                bool mapped3D = false;

                // [3D INVARIANT POINT MAPPING]
                // Nếu có mirrorPlane và tọa độ Model 3D nguyên bản, chiếu điểm 3D qua mirrorPlane rồi chuyển về hệ tọa độ Sketch đang mở.
                // Giải pháp này độc lập 100% với việc SolidWorks có đảo trục UV của mặt phẳng hay không!
                if (mirrorPlane != null && snap != null && snap.HasModelCoords && m2s != null && mathUtility != null)
                {
                    try
                    {
                        double dot = (snap.ModelX - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                   + (snap.ModelY - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                   + (snap.ModelZ - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                        double mxMirr = snap.ModelX - 2.0 * dot * mirrorPlane.Normal[0];
                        double myMirr = snap.ModelY - 2.0 * dot * mirrorPlane.Normal[1];
                        double mzMirr = snap.ModelZ - 2.0 * dot * mirrorPlane.Normal[2];

                        MathPoint mpMirr = mathUtility.CreatePoint(new double[] { mxMirr, myMirr, mzMirr }) as MathPoint;
                        MathPoint mp2D = mpMirr != null ? mpMirr.MultiplyTransform(m2s) as MathPoint : null;
                        double[] arr2D = mp2D != null ? mp2D.ArrayData as double[] : null;
                        if (arr2D != null && arr2D.Length >= 2)
                        {
                            newX = arr2D[0];
                            newY = arr2D[1];
                            mapped3D = true;
                            CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_3D_{ptIdx++}] (modelSrc: {snap.ModelX * 1000.0:F3},{snap.ModelY * 1000.0:F3},{snap.ModelZ * 1000.0:F3} -> modelTgt: {mxMirr * 1000.0:F3},{myMirr * 1000.0:F3},{mzMirr * 1000.0:F3}) -> skTgt: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");
                        }
                    }
                    catch { }
                }

                if (!mapped3D)
                {
                    if (useGeneralLine)
                    {
                        double dist = (x - ax1) * nx + (y - ay1) * ny;
                        newX = x - 2.0 * dist * nx;
                        newY = y - 2.0 * dist * ny;
                    }
                    else
                    {
                        if (isParallelPlane)
                        {
                            newX = x;
                            newY = y;
                        }
                        else
                        {
                            newX = -x;
                            newY = y;
                        }
                    }
                    CreateMirrorPartPackage.LogDebug($"[MUTATION_PT_{ptIdx++}] (source: {x * 1000.0:F3}, {y * 1000.0:F3}mm, live: {swPt.X * 1000.0:F3}, {swPt.Y * 1000.0:F3}mm) -> target: ({newX * 1000.0:F3}, {newY * 1000.0:F3}mm)");
                }

                swPt.X = newX;
                swPt.Y = newY;
            }

            partDoc.InsertSketch2(true);
        }

        /// <summary>
        /// Phục hồi chính xác tọa độ các điểm Sketch về snapshot đã lưu (nếu bị xô lệch do Rebuild feature thất bại)
        /// </summary>
        public static bool RestoreSketchPoints(
            ModelDoc2 partDoc,
            Feature sketchFeat,
            List<SketchPointSnapshot> snapshots)
        {
            if (partDoc == null || sketchFeat == null || snapshots == null || snapshots.Count == 0) return false;
            Sketch swSketch = sketchFeat.GetSpecificFeature2() as Sketch;
            if (swSketch == null) return false;

            object[] sketchPointsObj = swSketch.GetSketchPoints2() as object[];
            if (sketchPointsObj == null) return false;

            bool anyShifted = false;
            for (int i = 0; i < sketchPointsObj.Length; i++)
            {
                SketchPoint swPt = sketchPointsObj[i] as SketchPoint;
                if (swPt == null) continue;
                SketchPointSnapshot snap = null;
                try
                {
                    int[] idArr = swPt.GetID() as int[];
                    if (idArr != null && idArr.Length >= 2)
                    {
                        snap = snapshots.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                    }
                }
                catch { }
                if (snap == null && i < snapshots.Count) snap = snapshots[i];
                if (snap != null)
                {
                    if (Math.Abs(swPt.X - snap.X) > 1e-6 || Math.Abs(swPt.Y - snap.Y) > 1e-6)
                    {
                        anyShifted = true;
                        break;
                    }
                }
            }

            if (!anyShifted) return true;

            CreateMirrorPartPackage.LogDebug($"[RESTORE_SKETCH] Sketch points in {sketchFeat.Name} shifted during candidate evaluation. Restoring pristine coordinates...");
            sketchFeat.Select2(false, 0);
            partDoc.EditSketch();

            Sketch activeSk = sketchFeat.GetSpecificFeature2() as Sketch;
            if (activeSk != null && activeSk.RelationManager != null)
            {
                try { activeSk.RelationManager.DeleteAllRelations(); } catch { }
            }

            object[] activePts = activeSk != null ? activeSk.GetSketchPoints2() as object[] : sketchPointsObj;
            if (activePts != null)
            {
                for (int i = 0; i < activePts.Length; i++)
                {
                    SketchPoint swPt = activePts[i] as SketchPoint;
                    if (swPt == null) continue;
                    SketchPointSnapshot snap = null;
                    try
                    {
                        int[] idArr = swPt.GetID() as int[];
                        if (idArr != null && idArr.Length >= 2)
                        {
                            snap = snapshots.Find(p => p.Id1 == idArr[0] && p.Id2 == idArr[1]);
                        }
                    }
                    catch { }
                    if (snap == null && i < snapshots.Count) snap = snapshots[i];
                    if (snap != null)
                    {
                        swPt.X = snap.X;
                        swPt.Y = snap.Y;
                    }
                }
            }

            partDoc.InsertSketch2(true);
            partDoc.ForceRebuild3(false);
            return true;
        }

        /// <summary>
        /// Tái tạo các rãnh Slot đối xứng hoàn chỉnh với đầy đủ ràng buộc hình học và kích thước nguyên bản
        /// </summary>
        public static void RecreateMirroredSlots(
            ModelDoc2 partDoc, 
            Sketch swSketch, 
            double ax1, double ay1, 
            double ax2, double ay2, 
            List<SketchSlotSnapshot> pristineSlots,
            PlaneData mirrorPlane = null)
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
            ISldWorks swApp = SwAddin.InstanceSwApp;
            if (swApp == null)
            {
                try
                {
                    swApp = System.Runtime.InteropServices.Marshal.GetActiveObject("SldWorks.Application") as ISldWorks;
                }
                catch { }
            }
            bool oldAutoRel = true;
            bool oldInference = true;
            if (swApp != null)
            {
                try
                {
                    oldAutoRel = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations);
                    oldInference = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchAutomaticRelations, false);
                    swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSketchInference, false);
                }
                catch { }
            }

            try
            {
                MathTransform m2s = null;
                try { m2s = swSketch.ModelToSketchTransform; } catch { }
                IMathUtility mathUtility = swApp != null ? swApp.GetMathUtility() as IMathUtility : null;

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

                    double newX1 = 0, newY1 = 0;
                    double newX2 = 0, newY2 = 0;
                    double newX3 = 0, newY3 = 0;
                    bool mapped3D = false;

                    if (mirrorPlane != null && slotSnap.HasModelCoords && m2s != null && mathUtility != null)
                    {
                        try
                        {
                            // Map P1
                            double dot1 = (slotSnap.ModelX1 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                        + (slotSnap.ModelY1 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                        + (slotSnap.ModelZ1 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                            double mx1 = slotSnap.ModelX1 - 2.0 * dot1 * mirrorPlane.Normal[0];
                            double my1 = slotSnap.ModelY1 - 2.0 * dot1 * mirrorPlane.Normal[1];
                            double mz1 = slotSnap.ModelZ1 - 2.0 * dot1 * mirrorPlane.Normal[2];
                            MathPoint mp1 = mathUtility.CreatePoint(new double[] { mx1, my1, mz1 }) as MathPoint;
                            MathPoint p2D1 = mp1 != null ? mp1.MultiplyTransform(m2s) as MathPoint : null;
                            double[] a1 = p2D1 != null ? p2D1.ArrayData as double[] : null;

                            // Map P2
                            double dot2 = (slotSnap.ModelX2 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                        + (slotSnap.ModelY2 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                        + (slotSnap.ModelZ2 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                            double mx2 = slotSnap.ModelX2 - 2.0 * dot2 * mirrorPlane.Normal[0];
                            double my2 = slotSnap.ModelY2 - 2.0 * dot2 * mirrorPlane.Normal[1];
                            double mz2 = slotSnap.ModelZ2 - 2.0 * dot2 * mirrorPlane.Normal[2];
                            MathPoint mp2 = mathUtility.CreatePoint(new double[] { mx2, my2, mz2 }) as MathPoint;
                            MathPoint p2D2 = mp2 != null ? mp2.MultiplyTransform(m2s) as MathPoint : null;
                            double[] a2 = p2D2 != null ? p2D2.ArrayData as double[] : null;

                            if (a1 != null && a1.Length >= 2 && a2 != null && a2.Length >= 2)
                            {
                                newX1 = a1[0]; newY1 = a1[1];
                                newX2 = a2[0]; newY2 = a2[1];

                                if (slotSnap.CreationType == (int)swSketchSlotCreationType_e.swSketchSlotCreationType_3pointarc)
                                {
                                    double dot3 = (slotSnap.ModelX3 - mirrorPlane.Origin[0]) * mirrorPlane.Normal[0]
                                                + (slotSnap.ModelY3 - mirrorPlane.Origin[1]) * mirrorPlane.Normal[1]
                                                + (slotSnap.ModelZ3 - mirrorPlane.Origin[2]) * mirrorPlane.Normal[2];
                                    double mx3 = slotSnap.ModelX3 - 2.0 * dot3 * mirrorPlane.Normal[0];
                                    double my3 = slotSnap.ModelY3 - 2.0 * dot3 * mirrorPlane.Normal[1];
                                    double mz3 = slotSnap.ModelZ3 - 2.0 * dot3 * mirrorPlane.Normal[2];
                                    MathPoint mp3 = mathUtility.CreatePoint(new double[] { mx3, my3, mz3 }) as MathPoint;
                                    MathPoint p2D3 = mp3 != null ? mp3.MultiplyTransform(m2s) as MathPoint : null;
                                    double[] a3 = p2D3 != null ? p2D3.ArrayData as double[] : null;
                                    if (a3 != null && a3.Length >= 2) { newX3 = a3[0]; newY3 = a3[1]; }
                                }
                                mapped3D = true;
                            }
                        }
                        catch { }
                    }

                    if (!mapped3D)
                    {
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
                    }

                    // Lưu ý: slotSnap.X1/Y1 và X2/Y2 từ GetSlotPoints() luôn là 2 tâm cung tròn (arc centers).
                    // Do đó với straight slot, bắt buộc phải dùng CenterCenter để SolidWorks không tự offset thêm Width/2.
                    int slotLenType = (slotSnap.CreationType == (int)swSketchSlotLengthType_e.swSketchSlotLengthType_CenterCenter)
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

                    CreateMirrorPartPackage.LogDebug($"[SLOT_RECREATED_{i}] type={slotSnap.CreationType} L={slotSnap.Length * 1000.0:F2}mm W={slotSnap.Width * 1000.0:F2}mm P1=({newX1 * 1000.0:F2},{newY1 * 1000.0:F2}) P2=({newX2 * 1000.0:F2},{newY2 * 1000.0:F2}) mapped3D={mapped3D} created={newSlot != null}");
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

                    if (!success)
                    {
                        CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ModifyDefinition failed with reversed direction. Trying BothDirections fallback...");
                        try
                        {
                            def.BothDirections = true;
                            def.SetEndCondition(false, (int)swEndConditions_e.swEndCondThroughAll);
                            success = cutFeature.ModifyDefinition(def, partDoc, null);
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] BothDirections fallback result={success}");
                        }
                        catch (Exception ex)
                        {
                            CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] BothDirections fallback exception: {ex.Message}");
                        }
                    }

                    def.ReleaseSelectionAccess();
                    
                    CreateMirrorPartPackage.LogDebug($"[REVERSE_DIR] ModifyDefinition={success}");
                    return success;
                }
            }
            return false;
        }

        /// <summary>
        /// Kiểm tra danh sách sketch segment có phải là biên dạng mở (open profile) hay không.
        /// Một biên dạng mở có ít nhất 1 đỉnh bậc 1 (chỉ nối với 1 đoạn thẳng, ví dụ đường cắt đơn hoặc polyline hở).
        /// </summary>
        public static bool IsOpenProfileSegments(List<SketchSegment> profileSegments)
        {
            if (profileSegments == null || profileSegments.Count == 0) return false;

            // Trường hợp phổ biến nhất: 1 đoạn sketch đơn (thường là SketchLine cắt mở)
            if (profileSegments.Count == 1 && profileSegments[0] is SketchLine)
            {
                return true;
            }

            try
            {
                Dictionary<SketchPoint, int> pointUsage = new Dictionary<SketchPoint, int>();
                foreach (var seg in profileSegments)
                {
                    SketchPoint sp = null, ep = null;
                    if (seg is SketchLine line)
                    {
                        sp = line.GetStartPoint2() as SketchPoint;
                        ep = line.GetEndPoint2() as SketchPoint;
                    }
                    else if (seg is SketchArc arc)
                    {
                        if (arc.IsCircle() == 1)
                        {
                            // Đường tròn hoàn chỉnh là biên dạng kín tuyệt đối, không có điểm mút mở
                            continue;
                        }
                        sp = arc.GetStartPoint2() as SketchPoint;
                        ep = arc.GetEndPoint2() as SketchPoint;
                    }
                    else if (seg is SketchSpline spl)
                    {
                        object[] splPts = spl.GetPoints2() as object[];
                        if (splPts != null && splPts.Length >= 2)
                        {
                            sp = splPts[0] as SketchPoint;
                            ep = splPts[splPts.Length - 1] as SketchPoint;
                        }
                    }

                    if (sp != null)
                    {
                        pointUsage[sp] = pointUsage.ContainsKey(sp) ? pointUsage[sp] + 1 : 1;
                    }
                    if (ep != null)
                    {
                        pointUsage[ep] = pointUsage.ContainsKey(ep) ? pointUsage[ep] + 1 : 1;
                    }
                }

                foreach (var kvp in pointUsage)
                {
                    if (kvp.Value == 1) return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Kiểm tra sketch có phải là biên dạng mở (open profile) hay không.
        /// </summary>
        public static bool IsOpenProfileSketch(Sketch sk)
        {
            if (sk == null) return false;
            try
            {
                object[] contours = sk.GetSketchContours() as object[];
                if (contours != null && contours.Length > 0)
                {
                    bool hasClosed = false;
                    bool hasOpen = false;
                    foreach (object item in contours)
                    {
                        SketchContour contour = item as SketchContour;
                        if (contour != null)
                        {
                            if (contour.IsClosed()) hasClosed = true;
                            else hasOpen = true;
                        }
                    }
                    if (hasClosed && !hasOpen) return false;
                    if (hasOpen && !hasClosed) return true;
                }

                object[] segs = sk.GetSketchSegments() as object[];
                if (segs == null || segs.Length == 0) return false;

                List<SketchSegment> activeSegs = new List<SketchSegment>();
                foreach (object s in segs)
                {
                    SketchSegment seg = s as SketchSegment;
                    if (seg != null && !seg.ConstructionGeometry)
                    {
                        activeSegs.Add(seg);
                    }
                }

                return IsOpenProfileSegments(activeSegs);
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Đảo chiều vùng cắt (Flip Side to Cut) của Extrude Cut
        /// </summary>
        public static bool ToggleFlipSideToCut(ModelDoc2 partDoc, Feature cutFeature)
        {
            if (partDoc == null || cutFeature == null) return false;

            IExtrudeFeatureData2 ext = cutFeature.GetDefinition() as IExtrudeFeatureData2;
            if (ext != null)
            {
                bool access = ext.AccessSelections(partDoc, null);
                CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] Feature {cutFeature.Name} AccessSelections={access}");
                if (access)
                {
                    ext.FlipSideToCut = !ext.FlipSideToCut;
                    CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] Feature {cutFeature.Name} new FlipSideToCut={ext.FlipSideToCut}");
                    bool success = cutFeature.ModifyDefinition(ext, partDoc, null);
                    ext.ReleaseSelectionAccess();
                    CreateMirrorPartPackage.LogDebug($"[FLIP_SIDE_TO_CUT] ModifyDefinition={success}");
                    return success;
                }
            }
            return false;
        }
    }
}
