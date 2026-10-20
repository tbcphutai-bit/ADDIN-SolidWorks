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
