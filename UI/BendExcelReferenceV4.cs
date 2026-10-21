// Read-only transcription of selected *verified text cells* in the user's original
// 深曲げ限界(4).xlsx. This is UI provenance, NOT a tool-id->limit rule engine.
// Do not infer max flange depth W/L or approval from picture pixels.
using System;
using System.Collections.Generic;

namespace ADDIN.UI
{
    internal sealed class BendExcelFamilyReferenceV4
    {
        internal readonly string Family, System, CellRange;
        internal readonly int ReferenceLengthMm;
        internal BendExcelFamilyReferenceV4(string family, string system, int length, string cells)
        { Family = family; System = system; ReferenceLengthMm = length; CellRange = cells; }
    }

    internal static class BendExcelReferenceV4
    {
        internal const string WorkbookName = "深曲げ限界(4).xlsx";
        internal const string RequiredCollisionCaveat = "深曲げ限界!N47: 金型・設備に干渉しない事が条件となる";
        internal const string DeepLimitST = "深曲げ限界!C48:D48: ST3.2 / 6000 (theo bảng)";
        internal const string DeepLimitSUS = "深曲げ限界!F48:G48: SUS3.0 / 6000 (theo bảng)";

        internal static readonly IReadOnlyList<BendExcelFamilyReferenceV4> KnownFamilies =
            Array.AsReadOnly(new[] {
                new BendExcelFamilyReferenceV4("Sash / サッシ型", "HDS / HD", 6000, "金型限界表!B3:B11"),
                new BendExcelFamilyReferenceV4("Gooseneck HDS / グース型", "HDS", 4200, "金型限界表!J3:J11"),
                new BendExcelFamilyReferenceV4("Special Gooseneck / 特グース型", "HDS", 4200, "金型限界表!R3:R11"),
                new BendExcelFamilyReferenceV4("Straight / 直剣型", "HDS", 4200, "金型限界表!B33:B41"),
                new BendExcelFamilyReferenceV4("Gooseneck HD / グース型", "HD", 6000, "金型限界表!J33:J41")
            });
    }
}
