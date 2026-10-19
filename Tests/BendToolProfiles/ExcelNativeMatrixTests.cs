using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using NativeMatrix;

internal static class ExcelNativeMatrixTests
{
    private static int tests,assertions;
    private static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    private static void Check(bool yes,string reason){assertions++;if(!yes)throw new Exception(reason);}
    private static void Reject(Action read,string reason)
    {try{read();}catch(InvalidDataException ex){Check(ex.Message.StartsWith(reason,StringComparison.Ordinal),"Expected "+reason+", got "+ex.Message);return;}throw new Exception("Accepted invalid "+reason);}
    private static void Test(string name,Action action){action();tests++;Console.WriteLine("PASS "+name);}
    private static MatrixSection Section()=>new MatrixSection{CaseId="TEST-U",Evidence="Controlled generator test only",InsideRadiusMm=2,SketchVerticesMm=new[]{new[]{0.0,50},new[]{0.0,0},new[]{80.0,0},new[]{80.0,50}}};
    private static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--validate")
        {try{var input=MatrixInput.Read(args[1]);Console.WriteLine("VALID "+input.Specification.Purpose+" "+input.Cases.Length+" cases");return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
        string folder=Path.Combine(Path.GetTempPath(),"ADDIN-MatrixInput-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            Test("RFC4180 preserves quoted comma, escaped quote, embedded newline and BOM",()=>{
                var r=MatrixInput.ReadCsv("\uFEFFId,Note\r\nA,\"a,b\"\r\nB,\"c\"\"d\nend\"\r\n");
                Check(r.Count==3&&r[1][1]=="a,b"&&r[2][1]=="c\"d\nend","Quoted values");
                Check(MatrixInput.ReadCsv("a,b\n1,").Last()[1]=="","Trailing empty cell preserved");
            });
            Test("Malformed quotes fail without partial records",()=>{
                Reject(()=>MatrixInput.ReadCsv("a,b\n\"unfinished"),"CSV_UNCLOSED_QUOTE");
                Reject(()=>MatrixInput.ReadCsv("a,b\n\"ok\"x,1"),"CSV_TEXT_AFTER_QUOTE");
                Reject(()=>MatrixInput.ReadCsv("a,b\nab\"c,1"),"CSV_UNEXPECTED_QUOTE");
            });
            Test("Safe fixture names reject traversal and reserved Windows paths",()=>{
                foreach(var id in new[]{"..","A/B","A\\B","CON","lpt9","", "A.SLDPRT"})Check(!MatrixInput.SafeId(id),"Rejected "+id);
                Check(MatrixInput.SafeId("CASE-001_U"),"Safe ID");
            });
            Test("Cold/warm JSON comparison ignores property order but preserves all numeric and witness facts",()=>{
                Check(MatrixInput.SameJsonFacts("{\"Status\":\"UNKNOWN\",\"Witness\":null,\"Tolerance\":0.001}","{\"Tolerance\":0.001,\"Witness\":null,\"Status\":\"UNKNOWN\"}"),"Object ordering is not a geometry change");
                Check(!MatrixInput.SameJsonFacts("{\"Status\":\"UNKNOWN\"}","{\"Status\":\"CLEAR\"}"),"Changed status detected");
                Check(!MatrixInput.SameJsonFacts("{\"Error\":0.001}","{\"Error\":0.0010000001}"),"No numeric tolerance relaxation");
                Check(!MatrixInput.SameJsonFacts("{\"Witness\":null}","{\"Witness\":{\"X\":1}}"),"Changed witness detected");
                Check(!MatrixInput.SameJsonFacts("[1,2]","[2,1]"),"Array order preserved");
            });
            string csv=Path.Combine(folder,"private.csv"),db=Path.Combine(folder,"private.sldmat"),sidecar=Path.Combine(folder,"private.json");
            File.WriteAllText(db,"Controlled test placeholder for existence only; never passed to SOLIDWORKS");
            const string header="Id,Shape,Length,Reference,W,L,t,Material\n";
            const string data="TEST-U,U,4210,4200,80,50,3,ST\n";
            var spec=new MatrixSpecification{Version=1,Purpose="GeneratorValidation",InterpretationEvidence="Controlled tests, no company acceptance",
                Sources=new List<MatrixSource>{new MatrixSource{Role="Csv",Path=csv}},Sections=new List<MatrixSection>{Section()},
                Materials=new List<MatrixMaterial>{new MatrixMaterial{Group="ST",Name="Fixture",Database=db}},
                Columns=new Dictionary<string,string>{{"CaseId","Id"},{"Shape","Shape"},{"AxialLengthMm","Length"},{"ReferenceAxialLengthMm","Reference"},{"W","W"},{"L","L"},{"ThicknessMm","t"},{"MaterialGroup","Material"}}};
            Action<string> save=text=>{File.WriteAllText(csv,text);spec.Sources[0].Sha256=MatrixInput.Hash(csv);File.WriteAllText(sidecar,Json.Serialize(spec));};
            Test("Explicit column mapping keeps axial length separate from W/L",()=>{
                save(header+data);var i=MatrixInput.Read(sidecar);var c=i.Cases.Single();
                Check(c.AxialLengthMm==4210&&c.W==80&&c.L==50&&c.ThicknessMm==3&&c.ExpectedBends==2,"Distinct physical dimensions");
                Check(c.LengthReferenceRelation=="ABOVE_REFERENCE_NOT_A_COLLISION_VERDICT","No capacity inference");
                c.AxialLengthMm=4200;Check(c.LengthReferenceRelation=="AT_REFERENCE","Equality");c.AxialLengthMm=4190;Check(c.LengthReferenceRelation=="BELOW_REFERENCE","Below");
            });
            Test("Source content change with same length invalidates evidence",()=>{
                save(header+data);var i=MatrixInput.Read(sidecar);var stamp=File.GetLastWriteTimeUtc(csv);File.WriteAllText(csv,(header+data).Replace("4210","4211"));File.SetLastWriteTimeUtc(csv,stamp);
                Reject(i.CheckSourcesCurrent,"STALE_SOURCE:Csv");
            });
            Test("Changed sidecar invalidates already captured interpretation",()=>{
                save(header+data);var i=MatrixInput.Read(sidecar);File.AppendAllText(sidecar," ");Reject(i.CheckSourcesCurrent,"STALE_SECTION_INTERPRETATION");
            });
            Test("Case-insensitive Windows fixture collisions and uppercase source hashes",()=>{
                save(header+data);spec.Sources[0].Sha256=spec.Sources[0].Sha256.ToUpperInvariant();File.WriteAllText(sidecar,Json.Serialize(spec));
                Check(MatrixInput.Read(sidecar).Cases.Length==1,"SHA256 case is not a different revision");
                spec.Sections.Add(new MatrixSection{CaseId="test-u"});save(header+data);Reject(()=>MatrixInput.Read(sidecar),"DUPLICATE_SECTION_CASE");spec.Sections.RemoveAt(1);
                save(header+data+data.Replace("TEST-U","test-u"));Reject(()=>MatrixInput.Read(sidecar),"SECTION_INTERPRETATION_MISSING");
            });
            Test("Company acceptance cannot run without both workbook revisions or all 33 rows",()=>{
                spec.Purpose="CompanyExcel33";save(header+data);Reject(()=>MatrixInput.Read(sidecar),"BOTH_EXCEL_SOURCES_REQUIRED");
                spec.Sources.Add(new MatrixSource{Role="ExcelExamples",Path=db,Sha256=MatrixInput.Hash(db)});
                spec.Sources.Add(new MatrixSource{Role="ExcelMatrix",Path=db,Sha256=MatrixInput.Hash(db)});
                save(header+data);Reject(()=>MatrixInput.Read(sidecar),"COMPANY_MATRIX_REQUIRES_33_CASES");
                spec.Purpose="GeneratorValidation";spec.Sources.RemoveRange(1,2);
            });
            Test("No default for missing, nonfinite, negative or ambiguous dimensions",()=>{
                foreach(string bad in new[]{"", "NaN","Infinity","-3","0","3mm"}){save(header+data.Replace(",3,ST",","+bad+",ST"));Reject(()=>MatrixInput.Read(sidecar),"INVALID_MM_VALUE:ThicknessMm");}
                save(header.Replace("Id,Shape","Id,Id")+data);Reject(()=>MatrixInput.Read(sidecar),"DUPLICATE_OR_EMPTY_CSV_HEADER");
                spec.Columns["W"]="Length";save(header+data);Reject(()=>MatrixInput.Read(sidecar),"AMBIGUOUS_CSV_COLUMN_MAPPING");spec.Columns["W"]="W";
            });
            Test("Each CSV row needs its own evidenced section interpretation",()=>{
                spec.Sections.Clear();save(header+data);Reject(()=>MatrixInput.Read(sidecar),"SECTION_INTERPRETATION_MISSING");spec.Sections.Add(Section());
                save(header+data+data);Reject(()=>MatrixInput.Read(sidecar),"DUPLICATE_CASE_ID");
                spec.Sections[0].Evidence=null;save(header+data);Reject(()=>MatrixInput.Read(sidecar),"SECTION_EVIDENCE_REQUIRED");spec.Sections[0]=Section();
            });
            Test("U/Z are distinguished by actual signed turns",()=>{
                save(header+data.Replace(",U,",",Z,"));Reject(()=>MatrixInput.Read(sidecar),"Z_TURNS_MISMATCH");
                var section=spec.Sections[0];section.SketchVerticesMm[3][1]=-50;save(header+data.Replace(",U,",",Z,"));Check(MatrixInput.Read(sidecar).Cases[0].Shape=="Z","Opposite turns");
                save(header+data);Reject(()=>MatrixInput.Read(sidecar),"U_TURNS_MISMATCH");spec.Sections[0]=Section();
            });
            Test("THREE requires four actual segments, rejects degenerate and self-intersecting intent",()=>{
                save(header+data.Replace(",U,",",THREE,"));Reject(()=>MatrixInput.Read(sidecar),"EXPLICIT_SECTION_VERTICES_REQUIRED");
                spec.Sections[0].SketchVerticesMm=new[]{new[]{0.0,0},new[]{80.0,0},new[]{80.0,50},new[]{0.0,50},new[]{0.0,-10}};
                save(header+data.Replace(",U,",",THREE,"));Reject(()=>MatrixInput.Read(sidecar),"SELF_INTERSECTING_SECTION");
                spec.Sections[0]=Section();spec.Sections[0].SketchVerticesMm[1]=new[]{0.0,50};save(header+data);Reject(()=>MatrixInput.Read(sidecar),"ORTHOGONAL_NONDEGENERATE_SECTION_REQUIRED");
            });
            Console.WriteLine("RESULT "+tests+" passed; 0 failed; "+assertions+" assertions");return 0;
        }
        catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
        finally{Directory.Delete(folder,true);}
    }
}
