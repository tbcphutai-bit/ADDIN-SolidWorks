using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace ADDIN.Commands.DxfChecking
{
    internal static partial class DxfCheckExport
    {
        internal static void WriteDrawingBomExcel(DrawingBatchCheckResult batch)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(batch.ReportPath)));
            var fields = batch.Items.SelectMany(i => i.Fields).Select(f => f.FieldName).Distinct().ToList();
            var summary = new List<object[]> {
                new[] { "INo BOM", "部品番号", "KẾT QUẢ", "COMPONENT", "DRAWING", "GHI CHÚ" }.Concat(fields).Cast<object>().ToArray()
            };
            var detail = new List<object[]> {
                new object[] { "INo BOM", "部品番号", "COMPONENT", "DRAWING", "TRƯỜNG", "GIÁ TRỊ BOM", "GIÁ TRỊ DRAWING", "KẾT QUẢ", "NGUỒN", "NỘI DUNG" }
            };
            foreach (var item in batch.Items)
            {
                summary.Add(new object[] { item.BomItemNumber, item.PartNumber, item.Status.ToString(), item.Component, item.DrawingPath, item.Note }
                    .Concat(fields.Select(name => (object)string.Join("; ", item.Fields.Where(f => f.FieldName == name).Select(f => f.DrawingValue)))).ToArray());
                foreach (var field in item.Fields)
                    detail.Add(new object[] { item.BomItemNumber, item.PartNumber, item.Component, item.DrawingPath, field.FieldName,
                        field.BomValue, field.DrawingValue, field.Status.ToString(), field.Source, field.Message });
            }
            string context = batch.ProcessedCount + "/" + batch.TotalSelected + " dòng BOM: " + batch.OkCount + " OK, " + batch.NgCount + " NG, " + batch.WarningCount + " Warning.";
            Atomic(batch.ReportPath, temp =>
            {
                using (var file = File.Create(temp)) using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                {
                    Entry(zip, "[Content_Types].xml", new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Types",
                        Content("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml"), Content("Default", "Extension", "xml", "application/xml"),
                        Content("Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                        Content("Override", "PartName", "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
                        Content("Override", "PartName", "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                        Content("Override", "PartName", "/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                    Entry(zip, "_rels/.rels", new XElement(P + "Relationships", Relation("rId1", "officeDocument", "xl/workbook.xml")));
                    Entry(zip, "xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R), new XElement(S + "sheets", SheetName("TỔNG HỢP", 1), SheetName("CHI TIẾT KIỂM TRA", 2))));
                    Entry(zip, "xl/_rels/workbook.xml.rels", new XElement(P + "Relationships", Relation("rId1", "worksheet", "worksheets/sheet1.xml"), Relation("rId2", "worksheet", "worksheets/sheet2.xml"), Relation("rId3", "styles", "styles.xml")));
                    Entry(zip, "xl/styles.xml", Styles());
                    Entry(zip, "xl/worksheets/sheet1.xml", Worksheet("CHECK DRAWING THEO BOM", context, summary,
                        new double[] { 12, 14, 14, 35, 65, 45 }.Concat(fields.Select(f => 25.0)).ToArray(), null, 5));
                    Entry(zip, "xl/worksheets/sheet2.xml", Worksheet("CHI TIẾT KIỂM TRA DRAWING", context, detail,
                        new double[] { 12, 14, 35, 65, 25, 35, 35, 14, 25, 75 }, null, 9));
                }
            });
        }

        internal static void WriteBomDrawingExcel(DxfBomBatchResult batch,double tolerance)
        {
            var summary=new List<object[]> { new object[] { "INo BOM","部品番号","KẾT QUẢ","COMPONENT","DRAWING","SHEET","VIEW","DXF","LỆCH MAX (mm)","NỘI DUNG","HÌNH HỌC","NÉT CẮT","DỊCH X (mm)","DỊCH Y (mm)","CONFIG","DRAWING SHA256","DXF SHA256","ADDIN BUILD" } };
            var detail=new List<object[]> { new object[] { "INo BOM","部品番号","DRAWING","DXF","SHEET","VIEW","MÃ","LOẠI","LỆCH (mm)","MÔ TẢ","DRAWING X (mm)","DRAWING Y (mm)","DXF X (mm, dịch nếu căn được)","DXF Y (mm, dịch nếu căn được)" } };
            foreach(var item in batch.Items)
            {
                summary.Add(new object[] {item.ItemNumber,item.PartNumber,item.Status,item.Component,item.DrawingPath,item.Sheet,item.View,item.DxfPath,item.Result==null?null:(object)item.Result.MaximumDeviation,item.Message,
                    item.Result==null?"":item.Result.GeometryPassed?"PASS":"FAIL",item.Result==null?"":item.Result.ReviewRequired?"CHECK":item.Result.CutQualityPassed?"PASS":"WARN",
                    item.Result==null || !item.Result.AlignmentFound?null:(object)item.Result.Translation.OffsetX,item.Result==null || !item.Result.AlignmentFound?null:(object)item.Result.Translation.OffsetY,
                    item.Result?.Source?.Configuration,item.Result?.Source?.DrawingSha256,item.Result?.Source?.DxfSha256,item.Result?.Source?.AddinVersion});
                if(item.Result!=null)foreach(var m in item.Result.Mismatches)detail.Add(new object[] {item.ItemNumber,item.PartNumber,item.DrawingPath,item.DxfPath,item.Sheet,item.View,(m.ReviewIssue?"CHECK-":"ERR-")+m.Id.ToString("00"),(m.ReviewIssue?"CHECK: ":m.CutQualityIssue?"NÉT CẮT: ":"HÌNH HỌC: ")+m.Type,m.Deviation,m.Description,
                    m.HasDrawingPosition?(object)m.DrawingPosition.X:null,m.HasDrawingPosition?(object)m.DrawingPosition.Y:null,m.HasDxfPosition?(object)m.DxfPosition.X:null,m.HasDxfPosition?(object)m.DxfPosition.Y:null});
                else if(item.Status=="ERROR" || item.Status=="SKIP")detail.Add(new object[] {item.ItemNumber,item.PartNumber,item.DrawingPath,item.DxfPath,item.Sheet,item.View,"",item.Status,null,item.Message});
            }
            Atomic(batch.ReportPath,temp=>
            {
                using(var file=File.Create(temp))using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
                {
                    Entry(zip,"[Content_Types].xml",new XElement(XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types")+"Types",
                        Content("Default","Extension","rels","application/vnd.openxmlformats-package.relationships+xml"),Content("Default","Extension","xml","application/xml"),
                        Content("Override","PartName","/xl/workbook.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                        Content("Override","PartName","/xl/styles.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
                        Content("Override","PartName","/xl/worksheets/sheet1.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                        Content("Override","PartName","/xl/worksheets/sheet2.xml","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                    Entry(zip,"_rels/.rels",new XElement(P+"Relationships",Relation("rId1","officeDocument","xl/workbook.xml")));
                    Entry(zip,"xl/workbook.xml",new XElement(S+"workbook",new XAttribute(XNamespace.Xmlns+"r",R),new XElement(S+"sheets",SheetName("TỔNG HỢP",1),SheetName("CHI TIẾT LỖI",2))));
                    Entry(zip,"xl/_rels/workbook.xml.rels",new XElement(P+"Relationships",Relation("rId1","worksheet","worksheets/sheet1.xml"),Relation("rId2","worksheet","worksheets/sheet2.xml"),Relation("rId3","styles","styles.xml")));
                    Entry(zip,"xl/styles.xml",Styles());
                    string context=batch.Message+" Dung sai "+tolerance.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture)+" mm; DXF so với SLDDRW. BOM: "+batch.BomPath;
                    Entry(zip,"xl/worksheets/sheet1.xml",Worksheet("CHECK DXF ↔ DRAWING THEO BOM",context,summary,new double[]{12,14,14,35,65,12,22,65,20,75,14,14,20,20,30,70,70,42},null,9,-1,null,new[]{8,12,13}));
                    Entry(zip,"xl/worksheets/sheet2.xml",Worksheet("CHI TIẾT LỖI DXF / DRAWING",context,detail,new double[]{12,14,65,65,12,22,14,30,20,75,24,24,32,32},null,9,-1,null,new[]{8,10,11,12,13}));
                }
            });
        }
    }
}
