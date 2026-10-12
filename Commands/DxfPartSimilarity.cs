using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.DxfChecking
{
    // Overlay data for CHECK DXF VIEW. File-to-file analysis is no longer available.
    internal sealed class DxfPartComparison
    {
        public DxfFileResult First, Second;
        public DxfTransform SecondToFirst;
        public double Score = 0, MaxBoundaryGap = 0, MeanBoundaryGap = 0, MaxInnerGap = 0, WidthDifference = 0, HeightDifference = 0;
        public int FirstOuterCount = 0, SecondOuterCount = 0, FirstInnerCount = 0, SecondInnerCount = 0, InternalDifferences = 0;
        public bool Exact;
        public DxfPoint DifferenceA, DifferenceB;
        public bool Mirrored { get { return SecondToFirst.Determinant < 0; } }
        public string Kind { get { return (Exact ? "Trùng" : "Gần trùng") + (Mirrored ? " (đối xứng)" : ""); } }
        public DxfFileResult Other(DxfFileResult file) { return file == First ? Second : First; }
        public DxfTransform Alignment(DxfFileResult file) { return file == First ? SecondToFirst : Inverse(SecondToFirst); }
        public DxfPoint DifferencePoint(DxfFileResult file)
        { return file == First ? DifferenceA : Inverse(SecondToFirst).Apply(DifferenceB); }
        public string Description
        {
            get {
                return "Biên ngoài giống " + Score.ToString("0.0", CultureInfo.InvariantCulture) + "%; lệch biên đo mẫu "
                    + MaxBoundaryGap.ToString("0.000", CultureInfo.InvariantCulture) + " mm; chênh kích thước sau căn: "
                    + WidthDifference.ToString("0.000", CultureInfo.InvariantCulture) + " × " + HeightDifference.ToString("0.000", CultureInfo.InvariantCulture)
                    + " mm. Biên bên trong: " + FirstInnerCount + " / " + SecondInnerCount + "; " + InternalDifferences + " biên khác."
                    + (Mirrored ? " Đã lật đối xứng khi đối chiếu." : "") + " Cần đối chiếu bản vẽ trước khi sử dụng.";
            }
        }
        internal static DxfTransform Inverse(DxfTransform t)
        {
            double det = t.Determinant;
            DxfTransform inverse = new DxfTransform { A = t.D / det, B = -t.B / det, C = -t.C / det, D = t.A / det };
            DxfPoint shift = inverse.Vector(new DxfPoint(-t.X, -t.Y)); inverse.X = shift.X; inverse.Y = shift.Y;
            return inverse;
        }
    }
}
