using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.DxfChecking
{
    internal sealed class DxfBomCheckItem
    {
        public int BomRow;
        public string ItemNumber, PartNumber, Component, DrawingPath, DxfPath, Sheet, View;
        public string Status = "ERROR", Message = "";
        public DxfCheckResult Result;
        // In-memory geometry snapshots contain no SOLIDWORKS objects. Keep them
        // internal so saved JSON never attempts to serialize evaluator delegates.
        internal IReadOnlyList<DxfEdge> PreviewDrawingEdges, PreviewDxfEdges;
    }
    internal sealed class DxfBomBatchResult
    {
        public string BomPath, ReportPath, JsonPath, Message = "";
        public int SelectedRows, ProcessedRows;
        public bool Canceled;
        public List<DxfBomCheckItem> Items = new List<DxfBomCheckItem>();
        public int Passed { get { return Items.Count(i => i.Status == "PASS"); } }
        public int Failed { get { return Items.Count(i => i.Status == "FAIL"); } }
        public int Warnings { get { return Items.Count(i => i.Status == "WARN"); } }
        public int Errors { get { return Items.Count(i => i.Status == "ERROR"); } }
        public int Skipped { get { return Items.Count(i => i.Status == "SKIP"); } }
    }

    internal sealed class CheckDxfBomBatch
    {
        private readonly ISldWorks app;
        private readonly DxfBomDrawingSourceResolver drawingSource;
        public CheckDxfBomBatch(ISldWorks application, DataGridView grid)
        { app=application; drawingSource=new DxfBomDrawingSourceResolver(application,grid); }

        public DxfBomBatchResult RunSilent(DataGridView grid, string bomPath, Action<int> begin,
            Action<int,int> progress, Action finish, Func<bool> cancel, string outputDirectory=null)
        {
            // Silent controls the current session's presentation. It does not
            // create another SOLIDWORKS process or invoke the Drawing checker.
            using (var display = new DrawingCommandDisplayScope(app))
                return Run(grid, bomPath, begin, progress, finish, cancel, outputDirectory);
        }

        public DxfBomBatchResult Run(DataGridView grid, string bomPath, Action<int> begin,
            Action<int,int> progress, Action finish, Func<bool> cancel, string outputDirectory=null)
        {
            var batch=new DxfBomBatchResult { BomPath=bomPath };
            if(app==null || grid==null || string.IsNullOrWhiteSpace(bomPath))
            { batch.Message="Hãy Load BOM chi tiết trước khi kiểm tra DXF."; return batch; }
            grid.EndEdit();
            var rows=grid.Rows.Cast<DataGridViewRow>().Where(r=>!r.IsNewRow && Convert.ToBoolean(r.Cells[0].Value??false)).ToList();
            batch.SelectedRows=rows.Count;
            if(rows.Count==0) { batch.Message="Hãy tick các chi tiết BOM cần kiểm tra."; return batch; }
            ModelDoc2 original=app.ActiveDoc as ModelDoc2;
            var bomDocument=app.GetOpenDocumentByName(bomPath) as ModelDoc2;
            if(bomDocument==null) { batch.Message="Drawing chứa BOM đã đóng. Hãy mở và Load BOM lại."; return batch; }
            var options=new DrawingDxfCheckOptions();
            var timer=System.Diagnostics.Stopwatch.StartNew();
            try
            {
                begin?.Invoke(rows.Count);
                foreach(var row in rows)
                {
                    if(cancel!=null && cancel()) { batch.Canceled=true; break; }
                    var numbers=row.Cells[1].Tag as BomNumberValues;
                    var item=new DxfBomCheckItem { BomRow=row.Index, ItemNumber=numbers==null?"":numbers.ItemNumber,
                        PartNumber=Convert.ToString(row.Cells[1].Value??"").Trim(), Component=Convert.ToString(row.Cells[5].Value??"") };
                    ModelDoc2 drawingModel=null; bool owned=false; string previousSheet=null;
                    try
                    {
                        string partPath;
                        item.DrawingPath=drawingSource.ResolveBomDrawingPath(row,bomDocument,out partPath);
                        if(string.IsNullOrWhiteSpace(item.DrawingPath)) throw new FileNotFoundException("Không tìm thấy Drawing của dòng BOM này.");
                        drawingModel=app.GetOpenDocumentByName(item.DrawingPath) as ModelDoc2;
                        if(drawingModel==null)
                        {
                            int errors=0,warnings=0;
                            drawingModel=app.OpenDoc6(item.DrawingPath,(int)swDocumentTypes_e.swDocDRAWING,
                                (int)(swOpenDocOptions_e.swOpenDocOptions_Silent|swOpenDocOptions_e.swOpenDocOptions_ReadOnly),"",ref errors,ref warnings) as ModelDoc2;
                            owned=drawingModel!=null;
                            if(drawingModel==null) throw new InvalidOperationException("Không mở được Drawing; error="+errors+", warning="+warnings);
                        }
                        int activationError=0;
                        app.ActivateDoc3(drawingModel.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref activationError);
                        if(app.IsSame(app.ActiveDoc,drawingModel)!=(int)swObjectEquality.swObjectSame) throw new InvalidOperationException("Không kích hoạt được Drawing.");
                        var drawing=(DrawingDoc)drawingModel;
                        previousSheet=((Sheet)drawing.GetCurrentSheet()).GetName();
                        // Each Flat View checks the model/drawing rebuild state and
                        // updates its own display geometry before extraction. Keep the
                        // normal path fast and never save the source documents.
                        int compared=0;
                        foreach(string sheet in (string[])drawing.GetSheetNames())
                        {
                            if(cancel!=null && cancel()) { batch.Canceled=true; break; }
                            drawing.ActivateSheet(sheet);
                            string displayedDxf=drawingSource.ReadDisplayedDxfName(drawingModel);
                            var first=drawing.GetFirstView() as IView;
                            for(var view=first==null?null:first.GetNextView() as IView;view!=null;view=view.GetNextView() as IView)
                            {
                                if(!view.IsFlatPatternView())continue;
                                var check=new DxfBomCheckItem { BomRow=item.BomRow, ItemNumber=item.ItemNumber, PartNumber=item.PartNumber,
                                    Component=item.Component, DrawingPath=item.DrawingPath, Sheet=sheet, View=view.GetName2() };
                                try
                                {
                                    string referenced=view.GetReferencedModelName();
                                    if(!string.IsNullOrWhiteSpace(partPath) && !SamePath(referenced,partPath))
                                        throw new InvalidOperationException("Flat View tham chiếu model khác với dòng BOM; không ghép DXF.");
                                    string job=Property(view.ReferencedDocument as ModelDoc2,view.ReferencedConfiguration,"手配番号");
                                    if(string.IsNullOrWhiteSpace(job))job=Property(bomDocument,"","手配番号");
                                    string expected=NormalizeDxfStem(string.IsNullOrWhiteSpace(job)?item.PartNumber:job+"/"+item.PartNumber);
                                    string stem=NormalizeDxfStem(displayedDxf);
                                    if(string.IsNullOrWhiteSpace(stem))stem=expected;
                                    if(string.IsNullOrWhiteSpace(stem))throw new InvalidOperationException("Không có tên DXF trong Drawing hoặc BOM.");
                                    if(!string.IsNullOrWhiteSpace(job) && !string.Equals(stem,expected,StringComparison.OrdinalIgnoreCase))
                                        throw new InvalidOperationException("Tên DXF trên Drawing khác số chi tiết BOM: "+stem+" / "+expected);
                                    check.DxfPath=FindDxf(stem,Path.GetDirectoryName(item.DrawingPath),Path.GetDirectoryName(bomPath));
                                    if(check.DxfPath==null)throw new FileNotFoundException("Không tìm thấy "+stem+".dxf cạnh Drawing hoặc trong thư mục DXF của công trình.");
                                    var command=new CheckDxfDrawingViewCommand(app,drawingModel,view);
                                    check.Result=command.Run(check.DxfPath,options);
                                    check.PreviewDrawingEdges=command.PreviewDrawingEdges;
                                    check.PreviewDxfEdges=command.PreviewDxfEdges;
                                    check.Status=check.Result.Status;
                                    check.Message=(check.Status=="WARN"?"Hình học khớp; cần sửa chất lượng nét cắt. ":"")+string.Join("; ",check.Result.Mismatches.Select(m=>m.Description).Distinct());
                                }
                                catch(Exception ex) { check.Message=ex.Message; }
                                batch.Items.Add(check);compared++;
                            }
                        }
                        if(compared==0 && !batch.Canceled) { item.Status="SKIP";item.Message="Drawing không có Flat Pattern View được SolidWorks nhận diện.";batch.Items.Add(item); }
                    }
                    catch(Exception ex) { item.Message=ex.Message;batch.Items.Add(item); }
                    finally
                    {
                        if(drawingModel!=null)
                        {
                            if(!owned && !string.IsNullOrWhiteSpace(previousSheet)) try{((DrawingDoc)drawingModel).ActivateSheet(previousSheet);}catch{}
                            if(owned)try{app.CloseDoc(drawingModel.GetTitle());}catch{}
                        }
                        batch.ProcessedRows++;
                        progress?.Invoke(batch.ProcessedRows,rows.Count);
                    }
                    if(batch.Canceled)break;
                }
                string root=outputDirectory??Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"TAI_TOOL","CHECK_DXF_DRAWING");
                batch.Message=(batch.Canceled?"Đã dừng. ":"")+batch.ProcessedRows+"/"+rows.Count+" dòng BOM: "+batch.Passed+" PASS, "+batch.Failed+" FAIL, "+batch.Warnings+" WARN, "+batch.Errors+" ERROR, "+batch.Skipped+" không có Flat View.";
                WriteReports(batch, options, root);
                System.Diagnostics.Trace.WriteLine("[CHECK DXF BOM] "+batch.Message+"; Seconds="+timer.Elapsed.TotalSeconds+"; Report="+batch.ReportPath);
            }
            finally
            {
                if(original!=null)try{int errors=0;app.ActivateDoc3(original.GetTitle(),false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref errors);}catch{}
                finish?.Invoke();
            }
            return batch;
        }
        internal static void WriteReports(DxfBomBatchResult batch, DrawingDxfCheckOptions options, string root)
        {
            // Report output is independent of the completed checks and cached
            // preview. A locked/unwritable destination must not discard either.
            try
            {
                string output=Path.Combine(root,DateTime.Now.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture)+"_"+Guid.NewGuid().ToString("N").Substring(0,6));
                Directory.CreateDirectory(output);
                batch.ReportPath=Path.Combine(output,"CHECK_DXF_DRAWING.xlsx");
                batch.JsonPath=Path.Combine(output,"CHECK_DXF_DRAWING.json");
                DxfCheckExport.WriteBomDrawingExcel(batch,options.ToleranceMm);
                File.WriteAllText(batch.JsonPath,new JavaScriptSerializer { MaxJsonLength=50000000 }.Serialize(batch),new UTF8Encoding(true));
            }
            catch (Exception error)
            {
                if (!File.Exists(batch.ReportPath)) batch.ReportPath = null;
                if (!File.Exists(batch.JsonPath)) batch.JsonPath = null;
                batch.Message += " Chưa lưu được báo cáo: " + error.Message;
                System.Diagnostics.Trace.WriteLine("[CHECK DXF REPORT ERROR] " + error);
            }
        }
        private static bool SamePath(string first,string second) { return !string.IsNullOrEmpty(first) && string.Equals(Path.GetFullPath(first),Path.GetFullPath(second),StringComparison.OrdinalIgnoreCase); }
        private static string Property(ModelDoc2 model,string configuration,string name)
        {
            if(model==null)return "";
            foreach(string cfg in new[]{configuration??"",""}.Distinct()) { string raw,resolved;bool resolvedFlag,linked;model.Extension.CustomPropertyManager[cfg].Get6(name,true,out raw,out resolved,out resolvedFlag,out linked);string value=string.IsNullOrWhiteSpace(resolved)?raw:resolved;if(!string.IsNullOrWhiteSpace(value))return value.Trim(); }
            return "";
        }
        internal static string NormalizeDxfStem(string value)
        {
            string stem=Regex.Replace(value??"",@"\s+","").Replace('／','/').Replace('/','-').Replace('－','-').Replace('−','-');
            if(stem.EndsWith(".dxf",StringComparison.OrdinalIgnoreCase))stem=stem.Substring(0,stem.Length-4);
            if(stem.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Tên DXF không hợp lệ: "+value);
            return stem;
        }
        internal static string FindDxf(string stem,string drawingDirectory,string bomDirectory)
        {
            foreach(string directory in new[]{drawingDirectory,bomDirectory}.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string direct=ExactFile(directory,stem);if(direct!=null)return direct;
                foreach(string child in new[]{Path.GetFileName(directory),"DXF","DXF_OUTPUT"}.Distinct(StringComparer.OrdinalIgnoreCase))
                { string conventional=ExactFile(Path.Combine(directory,child),stem);if(conventional!=null)return conventional; }
                var matches=Directory.GetDirectories(directory).Where(p=>!Path.GetFileName(p).StartsWith("CHECK_",StringComparison.OrdinalIgnoreCase))
                    .Select(p=>ExactFile(p,stem)).Where(p=>p!=null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if(matches.Count>1)throw new InvalidDataException("Có nhiều DXF cùng tên "+stem+" trong các thư mục con; cần giữ một bộ DXF chính.");
                if(matches.Count==1)return matches[0];
            }
            return null;
        }
        private static string ExactFile(string directory,string stem)
        { return Directory.Exists(directory)?Directory.GetFiles(directory,"*.*").FirstOrDefault(p=>Path.GetExtension(p).Equals(".dxf",StringComparison.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(p).Equals(stem,StringComparison.OrdinalIgnoreCase)):null; }
    }
}
