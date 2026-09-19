using System;
using System.Collections.Generic;

namespace ADDIN.Commands.MirrorV7
{
    public static class SlotCopyComparisonV7
    {
        public static bool Near(double a, double b, double tolerance)
        {
            if (double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b)) return false;
            return Math.Abs(a - b) <= tolerance;
        }

        public static bool SameSlotGeometry(SketchSlotSnapshotV7 a, SketchSlotSnapshotV7 b, double tolerance)
        {
            if (a == null || b == null || a.CreationType != b.CreationType || a.LengthType != b.LengthType || a.CenterArcDirection != b.CenterArcDirection || !Near(a.Length, b.Length, tolerance) || !Near(a.Width, b.Width, tolerance) || a.SlotPointSketchCoords.Count != b.SlotPointSketchCoords.Count) return false;
            for (int i = 0; i < a.SlotPointSketchCoords.Count; i++) if (!Near(a.SlotPointSketchCoords[i], b.SlotPointSketchCoords[i], tolerance)) return false;
            return true;
        }

        public static bool MatchesSyntheticSlotPoint(SketchEntitySnapshotV7 point, SketchSnapshotV7 snapshot, double tolerance)
        {
            if (point == null || snapshot == null || point.Kind != SketchEntityKindV7.Point || !point.HasIds || point.Id2 >= 0) return false;
            foreach (SketchSlotSnapshotV7 slot in snapshot.Slots)
                for (int i = 0; i + 2 < slot.SlotPointSketchCoords.Count; i += 3)
                    if (Near(point.SketchX, slot.SlotPointSketchCoords[i], tolerance) && Near(point.SketchY, slot.SlotPointSketchCoords[i + 1], tolerance) && Near(point.SketchZ, slot.SlotPointSketchCoords[i + 2], tolerance)) return true;
            return false;
        }

        public static List<SketchEntitySnapshotV7> GetRegularPoints(SketchSnapshotV7 snapshot, double tolerance, out int syntheticCount)
        {
            List<SketchEntitySnapshotV7> result = new List<SketchEntitySnapshotV7>(); syntheticCount = 0;
            if (snapshot == null) return result;
            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
            {
                if (entity.Kind != SketchEntityKindV7.Point) continue;
                if (MatchesSyntheticSlotPoint(entity, snapshot, tolerance)) syntheticCount++; else result.Add(entity);
            }
            return result;
        }
    }
}
