using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    // Explicit local-only mode. Never bundles CAD or copies source files. Full
    // analytic evidence is written only to the caller's private output path.
    private static int CompanyIntegration(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[1]), output=Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
            var before=Directory.GetFiles(root,"*.dxf",SearchOption.AllDirectories).ToDictionary(p=>p,p=>ToolProfileLibraryProvider.Hash(File.ReadAllBytes(p)),StringComparer.OrdinalIgnoreCase);
            bool useManifest=args.Length>4 && args[3]=="--manifest";
            var provider=Provider(root,useManifest ? args[4] : null); var snapshot=provider.Read(CancellationToken.None);
            Check(snapshot.Tools.Count==18 && snapshot.Tools.All(t=>t.Validation.SourceFilesExist),"18 existing company files");
            Check(snapshot.Tools.All(t=>t.Validation.PunchGeometryReadComplete),"18 analytic punch profiles read completely");
            var kik=snapshot.Tools.Single(t=>t.Id=="KIK-7181328001"); Check(!kik.Validation.PunchClosed,"Original KIK open profile retained");
            Check(kik.Parts.Single(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).Entities.Single().SourceClosed==false,"KIK source closed flag remains false");
            Check(snapshot.Tools.Count(t=>t.Validation.PunchClosed)==17,"17 other source profiles remain closed");
            Check(snapshot.Tools.All(t=>!t.Validation.ClampVerified && !t.Validation.ToolTipOriginVerified && !t.Validation.CoordinateFrameVerified && t.Validation.Status=="CHUA_XAC_MINH"),"No guessed tool frame or clamp fit");
            Check(snapshot.Tools.All(t=>t.Parts.All(p=>p.Role!=ToolGeometryRole.VerifiedHolderGeometry)),"No reference promoted to verified mounting CAD");
            Check(snapshot.Tools.All(t=>t.Parts.Where(p=>p.Role==ToolGeometryRole.CompanyPunchProfile).SelectMany(p=>p.Entities).All(e=>e.Type=="LWPOLYLINE" && e.Handle.Length>0 && e.Curves.Count>0)),"Punch source metadata retained");
            File.WriteAllText(Path.Combine(output,"PRIVATE-company-snapshot.json"),Json.Serialize(snapshot),new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output,"summary.json"),snapshot.ToDebugJson(),new UTF8Encoding(false));
            int firstExtra=useManifest ? 5 : 3;
            for (int i=firstExtra;i<args.Length;i++)
            {
                string path=Path.GetFullPath(args[i]); byte[] bytes=File.ReadAllBytes(path); string hash=ToolProfileLibraryProvider.Hash(bytes);
                var doc=ToolDxfReader.Read(bytes,new ToolProfileProvenance { Path=path,Sha256=hash,ByteCount=bytes.Length },CancellationToken.None);
                Check(doc.Source.Error==null,"Optional source parsed: "+Path.GetFileName(path)+": "+doc.Source.Error);
                Check(doc.Entities.Any(e=>e.Blocks.Count>0 && e.Curves.Count>0),"Optional INSERT geometry retained");
                File.WriteAllText(Path.Combine(output,"PRIVATE-document-"+(i-firstExtra)+".json"),Json.Serialize(new {doc.Source,doc.Entities}),new UTF8Encoding(false));
                Check(ToolProfileLibraryProvider.Hash(File.ReadAllBytes(path))==hash,"Optional CAD unchanged");
            }
            foreach (var pair in before) Check(ToolProfileLibraryProvider.Hash(File.ReadAllBytes(pair.Key))==pair.Value,"Original source unchanged");
            Console.WriteLine("COMPANY PASS: 18/18 readable; 17 closed, KIK open; reference tools="+snapshot.Tools.Count(t=>t.Parts.Any(p=>p.Role==ToolGeometryRole.HolderClampReference))+"; datum/frame/clamp unverified; "+assertions+" assertions.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("COMPANY FAIL: "+ex.Message); return 1; }
    }
}
