using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal partial class DxfOutPut
    {
        internal static double[] ContinuousDrawingCurves(IDrawingDoc drawing, double[] buffer)
        {
            if (drawing == null) throw new InvalidDataException("Không đọc được kiểu nét của Drawing.");
            var styles = new Dictionary<Tuple<int, int>, bool>();
            return DrawingCurveVisibility.Filter(buffer, (style, font) =>
            {
                var key = Tuple.Create(style, font);
                if (!styles.TryGetValue(key, out bool continuous))
                {
                    // Legacy LineStyle is a pen/style index; map it to the font id.
                    int id = style == -1 ? font : drawing.GetLineFontId(style);
                    object raw = drawing.GetLineFontInfo2(id);
                    var values = (raw as Array)?.Cast<object>().Select(Convert.ToDouble).ToArray();
                    continuous = DrawingCurveVisibility.IsContinuousPattern(values);
                    styles.Add(key, continuous);
                }
                return continuous;
            });
        }
    }

    internal static class DrawingCurveVisibility
    {
        internal static bool IsContinuousPattern(double[] values)
        {
            if (values == null || values.Length < 2 || !Finite(values[0]) || values[0] < 1 || values[0] != Math.Truncate(values[0]))
                throw new InvalidDataException("Không xác định được kiểu nét Drawing; dừng để tránh xuất nét khuất.");
            int count = checked((int)values[0]);
            if (count > values.Length - 1) throw new InvalidDataException("Mẫu kiểu nét Drawing không đầy đủ.");
            for (int i = 1; i <= count; i++)
                if (!Finite(values[i])) throw new InvalidDataException("Kiểu nét Drawing không hợp lệ.");
                else if (values[i] <= 0) return false; // negative = gap, zero = dot
            return true;
        }

        internal static double[] Filter(double[] buffer, Func<int, int, bool> continuous)
        {
            if (buffer == null || buffer.Length == 0 || buffer.Any(v => !Finite(v)))
                throw new InvalidDataException("Thiếu dữ liệu cạnh Drawing.");
            int end = buffer.Length;
            while (end > 0 && buffer[end - 1] == 0) end--;
            var output = new List<double>(buffer.Length);
            int index = 0;
            while (index < end)
            {
                int start = index;
                int type = Integer(buffer, ref index), size = Integer(buffer, ref index);
                if (type < 0 || type > 3 || size < 0 || (long)index + size + 7 > buffer.Length)
                    throw new InvalidDataException("Curve Drawing sai định dạng.");
                index += size;
                int style = ExactInteger(buffer[index + 1]), font = ExactInteger(buffer[index + 2]);
                index += 6;
                int count = Integer(buffer, ref index);
                if (count < 0 || (long)index + count * 3L > buffer.Length)
                    throw new InvalidDataException("Curve Drawing thiếu điểm.");
                index += count * 3;
                if (continuous(style, font)) for (int i = start; i < index; i++) output.Add(buffer[i]);
            }
            return output.ToArray();
        }
        private static int Integer(double[] data, ref int index)
        {
            if (index >= data.Length) throw new InvalidDataException("Curve Drawing bị cắt ngắn.");
            return ExactInteger(data[index++]);
        }
        private static int ExactInteger(double value)
        {
            if (!Finite(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                throw new InvalidDataException("Thuộc tính curve Drawing không hợp lệ.");
            return (int)value;
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
