using System;
using System.Globalization;
using ADDIN.Commands;

namespace ADDIN.UI
{
    // Draft values are private to the native page. Cancel never changes the
    // task pane's saved inputs; only an accepted snapshot reaches the command.
    internal sealed class MakeHolePageValues
    {
        internal static readonly string[] Directions = { "Curve Flow", "Line Flow", "Spline Flow" };
        internal string SizeText = "4.2";
        internal string Direction = "Curve Flow";
        internal string HoleLabel = "";
        internal double EdgeOffsetMm = 20;
        internal double LeftOffsetMm = 50;
        internal double RightOffsetMm = 50;
        internal double PitchMm = 300;
        internal double ThicknessMm;

        internal MakeHolePageValues Copy() { return (MakeHolePageValues)MemberwiseClone(); }

        internal bool TryGetOptions(out MakeHoleOptions options, out string message)
        {
            options = null;
            string text = NormalizeSize(SizeText);
            double diameter;
            string loose = "None";
            bool slot = text.IndexOf('x') >= 0;
            if (slot)
            {
                string[] parts = text.Split('x');
                double a, b;
                if (parts.Length != 2 || !TryPositive(parts[0], out a) || !TryPositive(parts[1], out b) || a == b)
                {
                    message = "Kích thước lỗ dài: nhập rộng x dài, ví dụ 4.2x25 (mm).";
                    return false;
                }
                diameter = Math.Min(a, b);
                loose = Format(diameter) + "x" + Format(Math.Max(a, b));
                text = loose;
            }
            else if (!TryPositive(text, out diameter))
            {
                message = "Nhập đường kính lớn hơn 0, hoặc lỗ dài như 4.2x25 (mm).";
                return false;
            }
            if (!Positive(EdgeOffsetMm) || !Positive(LeftOffsetMm) || !Positive(RightOffsetMm) || !Positive(PitchMm))
            {
                message = "Khoảng cách cạnh, hai đầu và bước lỗ phải lớn hơn 0.";
                return false;
            }
            if (Array.IndexOf(Directions, Direction) < 0)
            {
                message = "Chọn hướng Curve Flow, Line Flow hoặc Spline Flow.";
                return false;
            }
            options = new MakeHoleOptions
            {
                HoleType = slot ? "Loose" : "Circle", LooseType = loose,
                Direction = Direction, DiameterMm = diameter,
                EdgeOffsetMm = EdgeOffsetMm, LeftOffsetMm = LeftOffsetMm,
                RightOffsetMm = RightOffsetMm, PitchMm = PitchMm,
                ThicknessMm = Positive(ThicknessMm) ? ThicknessMm : 0,
                Material = "", FaceColor = "", SigmaType = "", Paint = false,
                HoleSizeText = text, PaintNameText = "",
                HoleLabel = string.IsNullOrWhiteSpace(HoleLabel) ? null : HoleLabel.Trim()
            };
            message = "Sẵn sàng. Bấm ✓ để tạo lỗ; ✕ để hủy.";
            return true;
        }

        internal static bool Positive(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        private static bool TryPositive(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && Positive(value);
        }
        private static string NormalizeSize(string text)
        {
            text = (text ?? "").Trim();
            if (text.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 2);
            return text.Replace(" ", "").Replace("*", "x").Replace("X", "x").Replace(",", ".");
        }
        private static string Format(double value) { return value.ToString("0.###############", CultureInfo.InvariantCulture); }
    }
}
