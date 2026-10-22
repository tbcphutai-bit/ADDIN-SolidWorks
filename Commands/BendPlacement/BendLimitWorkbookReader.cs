using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class BendLimitManifest
    {
        public int Version { get; set; }
        public string WorkbookSha256 { get; set; }
        public List<BendLimitSelection> Rules { get; set; }
    }
    internal sealed class BendLimitSelection
    {
        public string Id { get; set; }
        public string FamilyId { get; set; }
        public string Kind { get; set; }
        public string Sheet { get; set; }
        public string Locator { get; set; } // cell:A1 or drawing:xl/drawings/drawing1.xml#42
        public string ExpectedObjectSha256 { get; set; }
        public string Unit { get; set; }
        public string NumberPattern { get; set; }
        public string[] ToolIds { get; set; }
        // Optional *reviewed* numerical case conditions. Old manifests remain
        // reference-only; unsigned conditions can NEVER certify MAX.
        public string MachineSystem { get; set; }
        public string MaterialCode { get; set; }
        public string SectionKind { get; set; }
        public double? ThicknessMinMm { get; set; }
        public double? ThicknessMaxMm { get; set; }
        public double? AngleMinDeg { get; set; }
        public double? AngleMaxDeg { get; set; }
        public double? WMinMm { get; set; }
        public double? WMaxMm { get; set; }
        public double? LMinMm { get; set; }
        public double? LMaxMm { get; set; }
        public ToolReviewProof ApplicabilityReview { get; set; }
    }
    internal sealed class WorkbookSourceObject
    {
        public string Sheet { get; }
        public string Locator { get; }
        public string Sha256 { get; }
        public string Text { get; }
        public string Kind { get; }
        public bool Formula { get; }
        internal WorkbookSourceObject(string sheet,string locator,string sha,string text,string kind,bool formula=false)
        {Sheet=sheet;Locator=locator;Sha256=sha;Text=text;Kind=kind;Formula=formula;}
    }
    internal static class BendLimitWorkbookReader
    {
        private static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace A="http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace D="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        internal sealed class Result
        {
            internal string Fingerprint,WorkbookSha256;
            internal IReadOnlyList<WorkbookSourceObject> Objects;
            internal IReadOnlyList<BendLimitRuleDraft> Rules;
            internal IReadOnlyList<string> Reasons;
        }
        internal static Result Read(string workbookPath,string manifestPath,CancellationToken token,string reviewAuthoritiesPath=null)
        {
            string sha=null,mappingSha=null,reviewSha=null,reviewObservations=null;
            var objects=new List<WorkbookSourceObject>();var rules=new List<BendLimitRuleDraft>();var reasons=new List<string>();
            try
            {
                byte[] bytes=PlacementSourceIo.Read(workbookPath,32*1024*1024,token);sha=PlacementSourceIo.Hash(bytes);
                objects=ReadObjects(bytes,token).ToList();
                byte[] mapping=PlacementSourceIo.Read(manifestPath,1024*1024,token);mappingSha=PlacementSourceIo.Hash(mapping);
                var manifest=PlacementSourceIo.Json<BendLimitManifest>(mapping);
                if(manifest==null||manifest.Version!=1||manifest.Rules==null||manifest.Rules.Count>512||manifest.Rules.Any(r=>r==null||string.IsNullOrWhiteSpace(r.Id))||
                    manifest.Rules.GroupBy(r=>r.Id,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new InvalidDataException("Invalid limit mapping.");
                if(!ToolDatumEvidence.IsSha256(manifest.WorkbookSha256)||!string.Equals(sha,manifest.WorkbookSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("STALE_LIMIT_MAPPING");
                var verifier=ToolReviewVerifier.Load(reviewAuthoritiesPath,token);
                reviewSha=verifier.Fingerprint;
                foreach(var r in manifest.Rules)
                {
                    token.ThrowIfCancellationRequested();var problems=new List<string>();BendLimitKind kind;
                    if(!Enum.TryParse(r.Kind,false,out kind)||!Enum.GetNames(typeof(BendLimitKind)).Contains(r.Kind)) {reasons.Add("UNMAPPED_LIMIT_RULE");continue;}
                    var matches=objects.Where(o=>o.Sheet==r.Sheet&&o.Locator==r.Locator).ToArray();
                    var obj=matches.Length==1?matches[0]:null;
                    if(obj==null||string.IsNullOrWhiteSpace(r.FamilyId)||r.ToolIds==null||r.ToolIds.Length==0||r.ToolIds.Any(id=>!CompanyToolIds.All.Contains(id,StringComparer.OrdinalIgnoreCase))||
                        r.ToolIds.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=r.ToolIds.Length)problems.Add("UNMAPPED_LIMIT_RULE");
                    if(obj!=null&&!string.IsNullOrEmpty(r.ExpectedObjectSha256)&&!string.Equals(obj.Sha256,r.ExpectedObjectSha256,StringComparison.OrdinalIgnoreCase))problems.Add("STALE_LIMIT_OBJECT");
                    double? value=null;
                    // Source transcription and tool applicability are separate facts.
                    // A readable cell may be retained even while its tool family is unknown.
                    if(obj!=null&&!problems.Contains("STALE_LIMIT_OBJECT"))
                    {
                        if(obj.Formula)problems.Add("UNVERIFIED_CACHED_FORMULA");
                        else if(obj.Kind=="Image")problems.Add("UNVERIFIED_IMAGE_TRANSCRIPTION");
                        else if(obj.Kind=="UnsupportedCellType")problems.Add("UNSUPPORTED_LIMIT_CELL_TYPE");
                        else
                        {
                            string text=obj.Text?.Normalize(NormalizationForm.FormKC);double number;
                            if(!string.IsNullOrEmpty(r.NumberPattern))
                            {
                                if(r.NumberPattern.Length>512)throw new InvalidDataException("Limit regex budget.");
                                var matchesNumber=Regex.Matches(text??"",r.NumberPattern,RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
                                if(matchesNumber.Count==1&&matchesNumber[0].Groups["value"].Success)text=matchesNumber[0].Groups["value"].Value;else text=null;
                            }
                            if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&ToolPoint.Finite(number)&&number>=0&&(r.Unit=="mm"||r.Unit=="m")&&ToolPoint.Finite(number*(r.Unit=="m"?1000:1)))value=number*(r.Unit=="m"?1000:1);
                            else problems.Add("UNMAPPED_LIMIT_VALUE_OR_UNIT");
                        }
                    }
                    var conditions=new BendExcelCaseConditions(r);
                    ToolReviewValidation approval=ToolReviewValidation.Missing("MISSING_EXCEL_CASE_REVIEW");
                    if(kind==BendLimitKind.MaxBendLength && obj!=null && value.HasValue && problems.Count==0 && conditions.Complete &&
                        r.ApplicabilityReview!=null)
                    {
                        // The reviewed signature binds workbook and exact cell,
                        // numerical source limit, catalog identities and ALL W/L
                        // and material applicability conditions. It cannot be
                        // carried over to a changed source workbook or manifest.
                        string payload=ToolEvidenceCanonical.Hash("BendExcelCaseNumericV1",sha,r.Id,r.FamilyId,kind.ToString(),
                            r.Sheet,r.Locator,obj.Sha256,r.Unit,ToolEvidenceCanonical.Number(value.Value),
                            ToolEvidenceCanonical.Hash(r.ToolIds.OrderBy(x=>x,StringComparer.Ordinal).ToArray()),conditions.Binding);
                        approval=verifier.Verify("BendExcelCaseApplicability",payload,r.ApplicabilityReview,
                            Path.GetDirectoryName(Path.GetFullPath(manifestPath)),token);
                    }
                    if(approval.Status!=ToolReviewStatus.VerifiedExternalRecord)
                        problems.Add("UNVERIFIED_TOOL_FAMILY_MAPPING");
                    if(kind==BendLimitKind.DeepBendClearanceExample)problems.Add("REFERENCE_EXAMPLE_ONLY");
                    if(!conditions.Complete)problems.Add("MISSING_SIGNED_EXCEL_CASE_CONDITIONS");
                    rules.Add(new BendLimitRuleDraft(r.Id,r.FamilyId,kind,sha,r.Sheet,r.Locator,obj?.Sha256,r.Unit,value,r.ToolIds??new string[0],problems,approval,conditions));
                }
                reviewObservations=verifier.ObservationFingerprint;
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex){rules.Clear();reasons.Add(ex is InvalidDataException&&ex.Message=="STALE_LIMIT_MAPPING"?"STALE_LIMIT_MAPPING":"MISSING_OR_INVALID_LIMIT_SOURCE");reasons.Add("UNMAPPED_LIMIT_RULE");}
            return new Result {WorkbookSha256=sha,Objects=PlacementLists.Copy(objects),Rules=PlacementLists.Copy(rules),Reasons=PlacementLists.Reasons(reasons),
                Fingerprint=PlacementSourceIo.Hash((workbookPath??"")+sha+mappingSha+reviewSha+reviewObservations+
                    string.Join("|",reasons)+string.Join("|",rules.Select(r=>r.ApplicabilityReview.Status.ToString()))) };
        }
        internal static IReadOnlyList<WorkbookSourceObject> ReadObjects(byte[] bytes,CancellationToken token)
        {
            var result=new List<WorkbookSourceObject>();
            using(var memory=new MemoryStream(bytes,false))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read))
            {
                if(zip.Entries.Count>4096||zip.Entries.Sum(e=>e.Length)>128*1024*1024)throw new InvalidDataException("Workbook package budget.");
                var workbook=Xml(zip,"xl/workbook.xml");var relations=Relations(zip,"xl/workbook.xml");
                var strings=zip.GetEntry("xl/sharedStrings.xml")==null?new string[0]:Xml(zip,"xl/sharedStrings.xml").Root.Elements(S+"si").Select(Text).ToArray();
                foreach(var sheet in workbook.Root.Element(S+"sheets").Elements(S+"sheet"))
                {
                    token.ThrowIfCancellationRequested();string name=(string)sheet.Attribute("name"),rid=(string)sheet.Attribute(R+"id");string part;
                    if(!relations.TryGetValue(rid,out part))throw new InvalidDataException("Workbook sheet relationship missing.");
                    var xml=Xml(zip,part);
                    foreach(var cell in xml.Descendants(S+"c"))
                    {
                        string address=(string)cell.Attribute("r"),type=(string)cell.Attribute("t"),text=(string)cell.Element(S+"v");
                        if(type=="s"){int n;if(!int.TryParse(text,out n)||n<0||n>=strings.Length)throw new InvalidDataException("Invalid shared string index.");text=strings[n];}
                        else if(type=="inlineStr")text=Text(cell.Element(S+"is"));
                        if(text==null)continue;
                        string kind=type==null||type=="n"||type=="s"||type=="str"||type=="inlineStr"?"Cell":"UnsupportedCellType";
                        result.Add(new WorkbookSourceObject(name,"cell:"+address,PlacementSourceIo.Hash(cell.ToString(SaveOptions.DisableFormatting)),text,kind,cell.Element(S+"f")!=null));
                    }
                    var sheetRelations=Relations(zip,part);
                    foreach(var drawing in xml.Descendants(S+"drawing"))
                    {
                        string drawingPart;if(!sheetRelations.TryGetValue((string)drawing.Attribute(R+"id"),out drawingPart))throw new InvalidDataException("Drawing relationship missing.");
                        var drawingXml=Xml(zip,drawingPart);var drawingRelations=Relations(zip,drawingPart);
                        foreach(var shape in drawingXml.Descendants().Where(n=>n.Name==D+"sp"||n.Name==D+"pic"))
                        {
                            var identity=shape.Descendants(D+"cNvPr").FirstOrDefault();string id=(string)identity?.Attribute("id");if(id==null)throw new InvalidDataException("Drawing object lacks stable ID.");
                            string text=string.Concat(shape.Descendants(A+"t").Select(t=>t.Value));string kind=shape.Name==D+"pic"?"Image":"DrawingText",hash;
                            if(kind=="Image")
                            {
                                var blip=shape.Descendants(A+"blip").SingleOrDefault();string media;
                                if(blip==null||!drawingRelations.TryGetValue((string)blip.Attribute(R+"embed")??"",out media))throw new InvalidDataException("Unsupported external/missing drawing image.");
                                hash=PlacementSourceIo.Hash(Entry(zip,media));
                            }
                            else hash=PlacementSourceIo.Hash(shape.ToString(SaveOptions.DisableFormatting));
                            result.Add(new WorkbookSourceObject(name,"drawing:"+drawingPart+"#"+id,hash,text,kind));
                        }
                    }
                    if(result.Count>20000)throw new InvalidDataException("Workbook source object budget.");
                }
            }
            if(result.GroupBy(o=>o.Sheet+"|"+o.Locator).Any(g=>g.Count()>1))throw new InvalidDataException("Ambiguous workbook source locator.");
            return PlacementLists.Copy(result);
        }
        private static string Text(XElement item)=>item==null?null:string.Concat(item.Descendants(S+"t").Where(t=>!t.Ancestors(S+"rPh").Any()).Select(t=>t.Value));
        private static byte[] Entry(ZipArchive zip,string path)
        {
            var entry=zip.GetEntry(path);if(entry==null||entry.Length>32*1024*1024)throw new InvalidDataException("Workbook entry missing/over budget.");
            using(var stream=entry.Open())using(var memory=new MemoryStream())
            {
                var buffer=new byte[16384];int count;
                while((count=stream.Read(buffer,0,buffer.Length))>0)
                {
                    if(memory.Length+count>32*1024*1024)throw new InvalidDataException("Workbook expanded entry budget.");
                    memory.Write(buffer,0,count);
                }
                return memory.ToArray();
            }
        }
        private static XDocument Xml(ZipArchive zip,string path)
        {
            using(var memory=new MemoryStream(Entry(zip,path)))using(var reader=XmlReader.Create(memory,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024}))return XDocument.Load(reader);
        }
        private static Dictionary<string,string> Relations(ZipArchive zip,string owner)
        {
            int slash=owner.LastIndexOf('/');string path=owner.Substring(0,slash+1)+"_rels/"+owner.Substring(slash+1)+".rels";
            var result=new Dictionary<string,string>();if(zip.GetEntry(path)==null)return result;
            foreach(var e in Xml(zip,path).Root.Elements())
            {
                if((string)e.Attribute("TargetMode")=="External")continue;
                string target=(string)e.Attribute("Target"),id=(string)e.Attribute("Id");
                if(target==null||id==null||target.Contains("\\")||target.Contains(":"))throw new InvalidDataException("Invalid package relationship.");
                var segments=new List<string>();foreach(string p in (target.StartsWith("/")?target.Substring(1):owner.Substring(0,slash+1)+target).Split('/'))
                {if(p=="..") {if(segments.Count==0)throw new InvalidDataException("Package path escapes root.");segments.RemoveAt(segments.Count-1);}else if(p!="."&&p!="")segments.Add(p);}
                result.Add(id,string.Join("/",segments));
            }
            return result;
        }
    }
}
