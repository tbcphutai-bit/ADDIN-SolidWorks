using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static int ProcessCompanyIntegration(string[] args)
    {
        try
        {
            if(args.Length!=4)return 2;Directory.CreateDirectory(args[3]);
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {PlacementConfigurationPath=args[1],DirectoryPath=args[2]});
            var library=provider.Read(CancellationToken.None);
            Check(library.Error==null&&library.Tools.Count==18&&library.PlacementSources.Tools.Count==18,"Actual library read");
            Check(library.Tools.Count(t=>t.Validation.Region.IsValidRegion)==17,"Source-open profile not closed");
            Check(library.PlacementSources.Tools.All(t=>!t.PhysicalPairVerified),"No production process/seating approval fabricated");
            var runs=new List<object>();
            foreach(int bends in new[]{3,6})
            {
                var model=CollisionModel(Rect("SOFTWARE_WORKPIECE",0,0,1,1),bends);
                var result=ProcessBatchWorker.RunSynchronously(new ProcessBatchRequest(model,library,observedFinalGeometry:BatchEnvelopes(model)));
                Check(result.Candidates.Count==18*bends,"Every real tool remains in every bend inventory");
                Check(result.Candidates.All(r=>r.ProcessResult.ToString()=="UNKNOWN"&&r.UnknownPhases.Count==5),"All missing actual process phases stay UNKNOWN");
                Check(result.Candidates.All(r=>r.PairSegments.Count==10&&r.ManufacturingStatus=="CHUA_XAC_MINH"&&r.WinnerToolId==null),"Independent pairs and no winner");
                Check(result.KernelQueries==0,"Missing source motion cannot be replaced with a fake numeric query");
                runs.Add(new {Bends=bends,Rows=result.Candidates.Count,result.WorkerMilliseconds,result.KernelQueries,
                    Tools=CompanyToolIds.All.Select(id=>new {Id=id,Rows=result.Candidates.Count(r=>r.ToolId==id),
                        Punch="UNKNOWN",Holder="UNKNOWN",UnknownPhases=5,Status="CHUA_XAC_MINH"}).ToArray()});
            }
            File.WriteAllText(Path.Combine(args[3],"PRIVATE-process-company.json"),Json.Serialize(new {Passed=true,Assertions=assertions,
                Workpiece="CONTROLLED_SYNTHETIC; native Part/Assembly tested by separate STA harness",Runs=runs,
                Profiles=CompanyToolIds.All.Select(id=>new {Id=id,PunchRegion=library.Tools.Single(t=>t.Id==id).Validation.Region.Status.ToString(),
                    HolderReferenceIdentified=library.PlacementSources.Tools.Single(t=>t.Id==id).MappingIdentified,
                    PhysicalPairVerified=library.PlacementSources.Tools.Single(t=>t.Id==id).PhysicalPairVerified}).ToArray()}));
            Console.WriteLine("PROCESS COMPANY PASS; "+assertions+" assertions; actual 18 tools; 54/108 rows UNKNOWN, no inferred process.");return 0;
        }
        catch(Exception ex) {Console.WriteLine("PROCESS COMPANY FAIL: "+ex);return 1;}
    }
}
