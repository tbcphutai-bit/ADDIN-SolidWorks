// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// User-configurable names and AutoCAD indexed colours for the four
    /// manufacturing layers produced by the Drawing-based DXF exporter.
    /// </summary>
    public sealed class DxfManufacturingLayerSettings
    {
        public DxfLayerMapOption Cut { get; set; } =
            new DxfLayerMapOption("CUT", 7);

        public DxfLayerMapOption Bend { get; set; } =
            new DxfLayerMapOption("BEND", 6);

        public DxfLayerMapOption LaserMark { get; set; } =
            new DxfLayerMapOption("LASER_MARK", 2);

        public DxfLayerMapOption PartInfo { get; set; } =
            new DxfLayerMapOption("PART_INFO", 213, 0xA953A0);

        /// <summary>
        /// Installed Windows font family used by PART_INFO. SOLIDWORKS often
        /// exports Drawing notes with its fixed TXT style, so the final DXF
        /// pass resolves this family to the real TTF/TTC/OTF file.
        /// </summary>
        public string PartInfoFontName { get; set; } = "Arial";

        public static DxfManufacturingLayerSettings CreateDefault() =>
            new DxfManufacturingLayerSettings();

        public static DxfManufacturingLayerSettings Normalize(
            DxfManufacturingLayerSettings source)
        {
            DxfManufacturingLayerSettings defaults = CreateDefault();
            source = source ?? defaults;

            var result = new DxfManufacturingLayerSettings
            {
                Cut = NormalizeOption(source.Cut, defaults.Cut),
                Bend = NormalizeOption(source.Bend, defaults.Bend),
                LaserMark = NormalizeOption(
                    source.LaserMark,
                    defaults.LaserMark),
                PartInfo = NormalizeOption(source.PartInfo, defaults.PartInfo),
                PartInfoFontName = string.IsNullOrWhiteSpace(
                        source.PartInfoFontName)
                    ? defaults.PartInfoFontName
                    : source.PartInfoFontName.Trim()
            };

            // Corrupt or hand-edited settings must not produce duplicate layer
            // table records. The UI prevents duplicates during normal use.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            EnsureUnique(result.Cut, defaults.Cut.Name, "CUT", used);
            EnsureUnique(result.Bend, defaults.Bend.Name, "BEND", used);
            EnsureUnique(
                result.LaserMark,
                defaults.LaserMark.Name,
                "LASER_MARK",
                used);
            EnsureUnique(
                result.PartInfo,
                defaults.PartInfo.Name,
                "PART_INFO",
                used);
            return result;
        }

        public DxfManufacturingLayerSettings Clone() => Normalize(this);

        public IEnumerable<DxfLayerMapOption> All
        {
            get
            {
                yield return Cut;
                yield return Bend;
                yield return LaserMark;
                yield return PartInfo;
            }
        }

        public bool ContainsLayer(string layerName) =>
            !string.IsNullOrWhiteSpace(layerName) &&
            All.Any(option => string.Equals(
                option.Name,
                layerName,
                StringComparison.OrdinalIgnoreCase));

        private static DxfLayerMapOption NormalizeOption(
            DxfLayerMapOption source,
            DxfLayerMapOption fallback)
        {
            string name = string.IsNullOrWhiteSpace(source?.Name)
                ? fallback.Name
                : source.Name.Trim();
            int color = source == null
                ? fallback.Color
                : Math.Max(1, Math.Min(255, source.Color));
            int? trueColorRgb = source?.TrueColorRgb;
            if (trueColorRgb <= 0 || trueColorRgb > 0xFFFFFF)
                trueColorRgb = null;
            return new DxfLayerMapOption(name, color, trueColorRgb);
        }

        private static void EnsureUnique(
            DxfLayerMapOption option,
            string defaultName,
            string suffix,
            ISet<string> used)
        {
            if (used.Add(option.Name)) return;

            option.Name = defaultName;
            if (used.Add(option.Name)) return;

            int index = 2;
            string candidate;
            do
            {
                candidate = defaultName + "_" + suffix + "_" + index++;
            }
            while (!used.Add(candidate));
            option.Name = candidate;
        }
    }

    public sealed class DxfLayerMapOption
    {
        public DxfLayerMapOption()
        {
        }

        public DxfLayerMapOption(
            string name,
            int color,
            int? trueColorRgb = null)
        {
            Name = name;
            Color = color;
            TrueColorRgb = trueColorRgb;
        }

        public string Name { get; set; }
        public int Color { get; set; }

        /// <summary>
        /// Optional DXF true colour stored as 0xRRGGBB. Color remains populated
        /// with the nearest ACI value for older CAD/CAM compatibility.
        /// </summary>
        public int? TrueColorRgb { get; set; }
    }
}
