using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace NativeMatrix
{
    // Test infrastructure only. A reviewed sidecar supplies the cross-section
    // interpretation; W/L alone cannot define a three-bend section. No company
    // values, CAD coordinates, process states or mounting datums are embedded.
    internal sealed class MatrixSource
    {
        public string Role { get; set; }
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }
    internal sealed class MatrixMaterial
    {
        public string Group { get; set; }
        public string Database { get; set; }
        public string Name { get; set; }
    }
    internal sealed class MatrixSection
    {
        public string CaseId { get; set; }
        public string Evidence { get; set; }
        // Native sketch lines in mm, NOT a guessed process trajectory. The
        // resulting physical outside W/L must pass the separate B-rep audit.
        public double[][] SketchVerticesMm { get; set; }
        public double InsideRadiusMm { get; set; }
    }
    internal sealed class MatrixSpecification
    {
        public int Version { get; set; }
        public string Purpose { get; set; } // CompanyExcel33 or GeneratorValidation
        public string InterpretationEvidence { get; set; }
        public List<MatrixSource> Sources { get; set; } = new List<MatrixSource>();
        // Logical column -> exact CSV header. No fuzzy matching or unit guesses.
        public Dictionary<string,string> Columns { get; set; } = new Dictionary<string,string>();
        public List<MatrixMaterial> Materials { get; set; } = new List<MatrixMaterial>();
        public List<MatrixSection> Sections { get; set; } = new List<MatrixSection>();
    }
    internal sealed class MatrixCase
    {
        public string CaseId { get; set; }
        public int CsvRecord { get; set; }
        public string Shape { get; set; }
        public double AxialLengthMm { get; set; }
        public double ReferenceAxialLengthMm { get; set; }
        public double W { get; set; }
        public double L { get; set; }
        public double ThicknessMm { get; set; }
        public string MaterialGroup { get; set; }
        public MatrixMaterial Material { get; set; }
        public MatrixSection Section { get; set; }
        public int ExpectedBends => Shape == "THREE" ? 3 : 2;
        public string LengthReferenceRelation => AxialLengthMm < ReferenceAxialLengthMm ? "BELOW_REFERENCE" :
            AxialLengthMm > ReferenceAxialLengthMm ? "ABOVE_REFERENCE_NOT_A_COLLISION_VERDICT" : "AT_REFERENCE";
    }
    internal sealed class MatrixInput
    {
        internal MatrixSpecification Specification { get; private set; }
        internal MatrixCase[] Cases { get; private set; }
        internal string SidecarPath { get; private set; }
        internal string SidecarSha256 { get; private set; }
        internal const double MeasurementToleranceMm = 0.001;
        internal static string Hash(string path)
        {
            // SOLIDWORKS legitimately holds its generated document open for
            // write. Hash without asking to deny the existing writer access.
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) using(var sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal void CheckSourcesCurrent()
        {
            Require(Hash(SidecarPath)==SidecarSha256,"STALE_SECTION_INTERPRETATION");
            foreach(var source in Specification.Sources)
                Require(File.Exists(source.Path)&&string.Equals(Hash(source.Path),source.Sha256,StringComparison.OrdinalIgnoreCase),"STALE_SOURCE:"+source.Role);
        }
        internal static MatrixInput Read(string sidecar)
        {
            var input=new MatrixInput {SidecarPath=System.IO.Path.GetFullPath(sidecar),SidecarSha256=Hash(sidecar)};
            var spec=new JavaScriptSerializer().Deserialize<MatrixSpecification>(File.ReadAllText(sidecar));
            Require(spec!=null&&spec.Version==1,"MATRIX_VERSION"); input.Specification=spec;
            bool company=spec.Purpose=="CompanyExcel33";
            Require(company||spec.Purpose=="GeneratorValidation","EXPLICIT_TEST_PURPOSE_REQUIRED");
            Require(!string.IsNullOrWhiteSpace(spec.InterpretationEvidence),"SECTION_INTERPRETATION_EVIDENCE_REQUIRED");
            Require(spec.Sources!=null&&spec.Sources.All(s=>s!=null&&!string.IsNullOrWhiteSpace(s.Role)&&System.IO.Path.IsPathRooted(s.Path)&&
                s.Sha256!=null&&s.Sha256.Length==64&&s.Sha256.All(Uri.IsHexDigit)),"SOURCE_PROVENANCE_REQUIRED");
            Require(spec.Sources.Select(s=>s.Role).Distinct(StringComparer.Ordinal).Count()==spec.Sources.Count,"DUPLICATE_SOURCE_ROLE");
            Require(spec.Sources.Count(s=>s.Role=="Csv")==1,"CSV_SOURCE_REQUIRED");
            if(company) Require(spec.Sources.Count(s=>s.Role=="ExcelExamples")==1&&spec.Sources.Count(s=>s.Role=="ExcelMatrix")==1,"BOTH_EXCEL_SOURCES_REQUIRED");
            input.CheckSourcesCurrent();
            var csv=ReadCsv(File.ReadAllText(spec.Sources.Single(s=>s.Role=="Csv").Path,Encoding.UTF8));
            Require(csv.Count>1,"EMPTY_MATRIX"); var headers=csv[0];
            Require(headers.All(h=>!string.IsNullOrWhiteSpace(h))&&headers.Distinct(StringComparer.Ordinal).Count()==headers.Length,"DUPLICATE_OR_EMPTY_CSV_HEADER");
            var fields=new[]{"CaseId","Shape","AxialLengthMm","ReferenceAxialLengthMm","W","L","ThicknessMm","MaterialGroup"};
            Require(spec.Columns!=null&&fields.All(f=>spec.Columns.ContainsKey(f)&&headers.Contains(spec.Columns[f])),"EXPLICIT_CSV_COLUMN_MAPPING_REQUIRED");
            Require(fields.Select(f=>spec.Columns[f]).Distinct().Count()==fields.Length,"AMBIGUOUS_CSV_COLUMN_MAPPING");
            Require(spec.Sections!=null&&spec.Sections.All(s=>s!=null)&&spec.Sections.Select(s=>s.CaseId).Distinct(StringComparer.OrdinalIgnoreCase).Count()==spec.Sections.Count,"DUPLICATE_SECTION_CASE");
            Require(spec.Materials!=null&&spec.Materials.All(m=>m!=null)&&spec.Materials.Select(m=>m.Group).Distinct(StringComparer.Ordinal).Count()==spec.Materials.Count,"DUPLICATE_MATERIAL_GROUP");
            var cases=new List<MatrixCase>();
            for(int i=1;i<csv.Count;i++)
            {
                var row=csv[i];Require(row.Length==headers.Length,"CSV_RECORD_WIDTH:"+(i+1));
                Func<string,string> value=field=>row[Array.IndexOf(headers,spec.Columns[field])].Trim();
                Func<string,double> number=field=>Positive(value(field),field+":"+(i+1));
                string id=value("CaseId"),shape=value("Shape"),group=value("MaterialGroup");
                Require(SafeId(id),"UNSAFE_CASE_ID");Require(new[]{"U","Z","THREE"}.Contains(shape),"UNSUPPORTED_SECTION:"+id);
                Require(new[]{"ST","SUS","AL"}.Contains(group),"UNSUPPORTED_MATERIAL_GROUP:"+id);
                var section=spec.Sections.SingleOrDefault(s=>s.CaseId==id);Require(section!=null,"SECTION_INTERPRETATION_MISSING:"+id);
                var material=spec.Materials.SingleOrDefault(m=>m.Group==group);
                Require(material!=null&&System.IO.Path.IsPathRooted(material.Database)&&File.Exists(material.Database)&&!string.IsNullOrWhiteSpace(material.Name),"MATERIAL_LIBRARY_REQUIRED:"+id);
                var item=new MatrixCase {CaseId=id,CsvRecord=i+1,Shape=shape,MaterialGroup=group,Material=material,Section=section,
                    AxialLengthMm=number("AxialLengthMm"),ReferenceAxialLengthMm=number("ReferenceAxialLengthMm"),
                    W=number("W"),L=number("L"),ThicknessMm=number("ThicknessMm")};
                Require(item.ReferenceAxialLengthMm==4200||item.ReferenceAxialLengthMm==6000,"EXCEL_REFERENCE_LENGTH_REQUIRED:"+id);
                ValidateSection(item); cases.Add(item);
            }
            Require(cases.Select(c=>c.CaseId).Distinct(StringComparer.OrdinalIgnoreCase).Count()==cases.Count,"DUPLICATE_CASE_ID");
            Require(spec.Sections.Count==cases.Count,"ORPHAN_SECTION_INTERPRETATION");
            if(company)Require(cases.Count==33,"COMPANY_MATRIX_REQUIRES_33_CASES");
            input.Cases=cases.ToArray();return input;
        }
        internal static void ValidateSection(MatrixCase item)
        {
            var s=item.Section;Require(!string.IsNullOrWhiteSpace(s.Evidence),"SECTION_EVIDENCE_REQUIRED:"+item.CaseId);
            Require(s.InsideRadiusMm>0&&!double.IsInfinity(s.InsideRadiusMm),"EXPLICIT_INSIDE_RADIUS_REQUIRED");
            var p=s.SketchVerticesMm;Require(p!=null&&p.Length==item.ExpectedBends+2&&p.All(v=>v!=null&&v.Length==2&&v.All(n=>!double.IsNaN(n)&&!double.IsInfinity(n))),"EXPLICIT_SECTION_VERTICES_REQUIRED");
            var turns=new List<int>();
            for(int i=1;i<p.Length;i++)
            {
                double dx=p[i][0]-p[i-1][0],dy=p[i][1]-p[i-1][1];
                Require((Math.Abs(dx)>MeasurementToleranceMm)^(Math.Abs(dy)>MeasurementToleranceMm),"ORTHOGONAL_NONDEGENERATE_SECTION_REQUIRED");
                if(i>1)
                {
                    double ax=p[i-1][0]-p[i-2][0],ay=p[i-1][1]-p[i-2][1];
                    Require(Math.Abs(ax*dx+ay*dy)<1e-8,"RIGHT_ANGLE_REQUIRED"); turns.Add(Math.Sign(ax*dy-ay*dx));
                }
            }
            if(item.Shape=="U")Require(turns[0]==turns[1],"U_TURNS_MISMATCH");
            if(item.Shape=="Z")Require(turns[0]!=turns[1],"Z_TURNS_MISMATCH");
            // Reject crossings/touching nonadjacent segments. Do not repair the
            // sidecar or ask SOLIDWORKS to heal ambiguous profile intent.
            for(int a=0;a<p.Length-1;a++)for(int b=a+2;b<p.Length-1;b++)
            {
                double minX=Math.Max(Math.Min(p[a][0],p[a+1][0]),Math.Min(p[b][0],p[b+1][0]));
                double maxX=Math.Min(Math.Max(p[a][0],p[a+1][0]),Math.Max(p[b][0],p[b+1][0]));
                double minY=Math.Max(Math.Min(p[a][1],p[a+1][1]),Math.Min(p[b][1],p[b+1][1]));
                double maxY=Math.Min(Math.Max(p[a][1],p[a+1][1]),Math.Max(p[b][1],p[b+1][1]));
                Require(minX>maxX+MeasurementToleranceMm||minY>maxY+MeasurementToleranceMm,"SELF_INTERSECTING_SECTION");
            }
        }
        internal static bool SafeId(string id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=80&&id.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_')&&
            !new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(id.ToUpperInvariant());
        private static double Positive(string text,string field)
        {
            double n;Require(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&n>0&&!double.IsInfinity(n),"INVALID_MM_VALUE:"+field);return n;
        }
        internal static void Require(bool condition,string reason){if(!condition)throw new InvalidDataException(reason);}
        // JavaScriptSerializer reflects property order differently after its
        // reflection cache warms. JSON object order is not a result change.
        // Preserve array order and compare EVERY value, including witnesses,
        // error bounds and reason strings. No geometry tolerance is relaxed.
        internal static bool SameJsonFacts(string left,string right)
        {
            var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
            return SameValue(json.DeserializeObject(left),json.DeserializeObject(right));
        }
        private static bool SameValue(object a,object b)
        {
            if(a==null||b==null)return a==null&&b==null;
            var da=a as Dictionary<string,object>;var db=b as Dictionary<string,object>;
            if(da!=null||db!=null)return da!=null&&db!=null&&da.Count==db.Count&&da.All(p=>db.ContainsKey(p.Key)&&SameValue(p.Value,db[p.Key]));
            var aa=a as object[];var ab=b as object[];
            if(aa!=null||ab!=null)return aa!=null&&ab!=null&&aa.Length==ab.Length&&aa.Zip(ab,SameValue).All(equal=>equal);
            bool na=a is int||a is long||a is decimal||a is double,nb=b is int||b is long||b is decimal||b is double;
            if(!na||!nb)return Equals(a,b);
            if(a is double&&b is double)return (double)a==(double)b;
            decimal x,y;
            string ax=a is double?((double)a).ToString("R",CultureInfo.InvariantCulture):Convert.ToString(a,CultureInfo.InvariantCulture);
            string bx=b is double?((double)b).ToString("R",CultureInfo.InvariantCulture):Convert.ToString(b,CultureInfo.InvariantCulture);
            return decimal.TryParse(ax,NumberStyles.Float,CultureInfo.InvariantCulture,out x)&&decimal.TryParse(bx,NumberStyles.Float,CultureInfo.InvariantCulture,out y)&&x==y;
        }
        // Strict RFC4180 comma-delimited UTF-8. Quoted commas/newlines/escaped
        // quotes are preserved; malformed quoting and duplicate headers fail.
        internal static List<string[]> ReadCsv(string text)
        {
            var rows=new List<string[]>();var row=new List<string>();var cell=new StringBuilder();
            bool quoted=false,closed=false,start=true,record=false;
            text=text.TrimStart('\uFEFF');
            for(int i=0;i<text.Length;i++)
            {
                char c=text[i];record=true;
                if(quoted)
                {
                    if(c=='"'){if(i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}else{quoted=false;closed=true;}}
                    else cell.Append(c);continue;
                }
                if(c=='"'){Require(start&&!closed,"CSV_UNEXPECTED_QUOTE");quoted=true;start=false;continue;}
                if(c==','||c=='\r'||c=='\n')
                {
                    row.Add(cell.ToString());cell.Clear();start=true;closed=false;
                    if(c!=','){rows.Add(row.ToArray());row.Clear();record=false;if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;}
                    continue;
                }
                Require(!closed,"CSV_TEXT_AFTER_QUOTE");cell.Append(c);start=false;
            }
            Require(!quoted,"CSV_UNCLOSED_QUOTE");if(record||row.Count>0){row.Add(cell.ToString());rows.Add(row.ToArray());}return rows;
        }
    }
}
