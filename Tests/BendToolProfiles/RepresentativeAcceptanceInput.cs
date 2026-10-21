using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace CheckChan.NativeAcceptance
{
    // Private manifest only: no fixture creation, CAD coordinates or company
    // source paths are embedded in the test binary or public repository.
    internal sealed class RepresentativeAcceptanceInput
    {
        public RepresentativeCase[] Cases { get; set; }
        public Dictionary<string,string> ProtectedSha256 { get; set; }
        internal static string Hash(string path)
        { return NativeMatrix.MatrixInput.Hash(path); }
        internal static string ComControlMode(string value)
        {
            if(string.IsNullOrEmpty(value))return null;
            if(value!="A"&&value!="B")throw new InvalidDataException("Only explicit COM controls A or B are supported");
            return value;
        }
        internal static int ComControlTests()
        {
            int n=0;Action<bool> check=ok=>{n++;if(!ok)throw new InvalidDataException("COM control guard regression");};
            check(ComControlMode(null)==null);check(ComControlMode("")==null);check(ComControlMode("A")=="A");check(ComControlMode("B")=="B");
            foreach(string input in new[]{"C","a"," "}){bool rejected=false;try{ComControlMode(input);}catch(InvalidDataException){rejected=true;}check(rejected);}
            Console.WriteLine("PASS COM control guards: "+n+" assertions; no native call");return 0;
        }
        internal static RepresentativeAcceptanceInput Read(string path)
        {
            var value=new JavaScriptSerializer().Deserialize<RepresentativeAcceptanceInput>(File.ReadAllText(path));
            value.Validate();return value;
        }
        internal void Validate()
        {
            if(Cases==null||Cases.Length!=5||!Cases.Select(c=>c.CaseId).SequenceEqual(new[]{"XL-02","XL-17","XL-19","PART-3","ASM-SMALL"})||
                Cases.Take(4).Any(c=>c.DocumentType!=1)||Cases[4].DocumentType!=2||
                !Cases.Select(c=>c.ExpectedBends).SequenceEqual(new[]{2,2,2,3,9})||
                ProtectedSha256==null||Cases.Any(c=>!System.IO.Path.IsPathRooted(c.Path)||!ProtectedSha256.ContainsKey(c.Path)))
                throw new InvalidDataException("Exactly five existing representative fixtures required");
            if(Cases.Take(4).Any(c=>c.FeatureIds==null||c.FeatureIds.Length!=c.ExpectedBends||c.FeatureIds.Distinct().Count()!=c.ExpectedBends||
                c.MinimumStatuses==null||c.MinimumStatuses.Length!=c.ExpectedBends||c.OuterExtentsMm==null||c.OuterExtentsMm.Length!=3))
                throw new InvalidDataException("Explicit baseline native identities, Min and dimensions required");
            CheckSources();
        }
        internal void CheckSources()
        {
            foreach(var source in ProtectedSha256)
                if(!System.IO.Path.IsPathRooted(source.Key)||!File.Exists(source.Key)||!string.Equals(Hash(source.Key),source.Value,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Representative input missing or stale: "+source.Key);
        }
        // Test selection only, never manufacturing evidence. Resume the four
        // remaining fixtures only after the exact product DLL completed XL-02.
        internal static int StartIndexAfterXl02(string proofPath,string productDll)
        {
            if(string.IsNullOrWhiteSpace(proofPath))return 0;
            if(!System.IO.Path.IsPathRooted(proofPath)||!File.Exists(proofPath))throw new InvalidDataException("Explicit existing XL-02 gate required");
            var proof=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(proofPath)) as Dictionary<string,object>;
            object result,function,shutdown,state;
            if(!proof.TryGetValue("Result",out result)||!Equals(result,"PASS")||!proof.TryGetValue("FunctionalResult",out function)||!Equals(function,"PASS")||
                !proof.TryGetValue("ShutdownResult",out shutdown)||!Equals(shutdown,"PASS")||!proof.TryGetValue("InprocessResult",out state))
                throw new InvalidDataException("XL-02 lifecycle has not passed");
            var data=state as Dictionary<string,object>;object sha,cases;
            if(data==null||!data.TryGetValue("DllSha256",out sha)||!Equals(sha,Hash(productDll))||!data.TryGetValue("RepresentativeCases",out cases))
                throw new InvalidDataException("XL-02 gate does not match this product DLL");
            var rows=cases as object[];var first=rows?.FirstOrDefault() as Dictionary<string,object>;object id;
            if(first==null||!first.TryGetValue("CaseId",out id)||!Equals(id,"XL-02")||
                new[]{"Execution","IdleClose","CloseDuringRun","OpenClose","Cancel"}.Any(key=>!first.ContainsKey(key)||!Equals(first[key],"PASS")))
                throw new InvalidDataException("XL-02 close/cancel gate is incomplete");
            return 1;
        }
        internal static int GateTests(string proofPath,string productDll)
        {
            int assertions=0;Action<bool> check=ok=>{assertions++;if(!ok)throw new Exception("Prior XL-02 gate regression");};
            string copy=System.IO.Path.GetTempFileName();var json=new JavaScriptSerializer();string baseline=File.ReadAllText(proofPath);
            try
            {
                check(StartIndexAfterXl02(null,productDll)==0);check(StartIndexAfterXl02(proofPath,productDll)==1);
                Action<Action<Dictionary<string,object>>> reject=change=>{
                    var value=(Dictionary<string,object>)json.DeserializeObject(baseline);change(value);File.WriteAllText(copy,json.Serialize(value));
                    bool failed=false;try{StartIndexAfterXl02(copy,productDll);}catch(InvalidDataException){failed=true;}check(failed);
                };
                reject(p=>p["Result"]="FAIL");reject(p=>p["ShutdownResult"]="FAIL");
                reject(p=>((Dictionary<string,object>)p["InprocessResult"])["DllSha256"]=new string('0',64));
                reject(p=>((Dictionary<string,object>)((object[])((Dictionary<string,object>)p["InprocessResult"])["RepresentativeCases"])[0]).Remove("IdleClose"));
                reject(p=>((Dictionary<string,object>)((object[])((Dictionary<string,object>)p["InprocessResult"])["RepresentativeCases"])[0])["CaseId"]="XL-17");
                Console.WriteLine("PASS prior XL-02 gate: "+assertions+" assertions; no native/CAD call");return 0;
            }
            catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
            finally{File.Delete(copy);}
        }
        internal static int Tests()
        {
            int checks=0;Action<bool> check=ok=>{checks++;if(!ok)throw new Exception("Representative manifest regression");};
            string path=System.IO.Path.GetTempFileName();
            try
            {
                // This is parser input, never opened as CAD or counted as a native test.
                File.WriteAllText(path,"private input guard");var ids=new[]{"XL-02","XL-17","XL-19","PART-3","ASM-SMALL"};
                var input=new RepresentativeAcceptanceInput {Cases=ids.Select((id,i)=>new RepresentativeCase {CaseId=id,Path=path,DocumentType=i==4?2:1,ExpectedBends=i==4?9:i==3?3:2,
                    FeatureIds=Enumerable.Range(1,i==3?3:2).ToArray(),MinimumStatuses=Enumerable.Repeat("MeetsMinimum",i==3?3:2).ToArray(),OuterExtentsMm=new[]{1d,2d,3d}}).ToArray(),
                    ProtectedSha256=new Dictionary<string,string>{{path,Hash(path)}}};
                input.Validate();check(true);
                Action<Action> reject=change=>{var original=new JavaScriptSerializer().Serialize(input);change();bool caught=false;try{input.Validate();}catch(InvalidDataException){caught=true;}check(caught);input=new JavaScriptSerializer().Deserialize<RepresentativeAcceptanceInput>(original);};
                reject(()=>input.Cases=input.Cases.Take(4).ToArray());
                reject(()=>input.Cases[1].CaseId="XL-02");
                reject(()=>input.Cases[3].FeatureIds=new[]{1,1,2});
                reject(()=>input.Cases[0].Path="relative.input");
                reject(()=>input.ProtectedSha256[path]=new string('0',64));
                File.AppendAllText(path," changed");bool stale=false;try{input.CheckSources();}catch(InvalidDataException){stale=true;}check(stale);
                check(NativeMatrix.MatrixInput.SameJsonFacts("{\"Reason\":\"UNKNOWN\",\"Min\":12.5}","{\"Min\":12.5,\"Reason\":\"UNKNOWN\"}"));
                check(!NativeMatrix.MatrixInput.SameJsonFacts("{\"Min\":12.5}","{\"Min\":12.500000000000002}"));
                check(!NativeMatrix.MatrixInput.SameJsonFacts("{\"Reason\":\"UNKNOWN\"}","{\"Reason\":\"CLEAR\"}"));
                check(!NativeMatrix.MatrixInput.SameJsonFacts("[1,2]","[2,1]"));
                Console.WriteLine("PASS representative input guards: "+checks+" assertions; no CAD generated or opened");return 0;
            }
            catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
            finally{File.Delete(path);}
        }
    }
    internal sealed class RepresentativeCase
    {
        public string CaseId { get; set; }
        public string Path { get; set; }
        public int DocumentType { get; set; }
        public int ExpectedBends { get; set; }
        public int[] FeatureIds { get; set; }
        public string[] MinimumStatuses { get; set; }
        public double[] OuterExtentsMm { get; set; }
    }
}
