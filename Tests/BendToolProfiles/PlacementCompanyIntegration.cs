using System;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    // Caller supplies local paths. No company selection keys, geometry or
    // coordinate-bearing evidence is embedded in this test executable.
    private static int WorkbookObjectInventory(string[] args)
    {
        try
        {
            var objects=BendLimitWorkbookReader.ReadObjects(PlacementSourceIo.Read(args[1],32*1024*1024,CancellationToken.None),CancellationToken.None);
            File.WriteAllText(args[2],Json.Serialize(objects));
            Console.WriteLine("Workbook source objects written to caller-selected PRIVATE output: "+objects.Count);
            return 0;
        }
        catch(Exception ex){Console.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}
    }
    private static int PlacementCompanyIntegration(string[] args)
    {
        try
        {
            string config=Path.GetFullPath(args[1]),directory=Path.GetFullPath(args[2]),output=Path.GetFullPath(args[3]);
            Directory.CreateDirectory(output);
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=directory,PlacementConfigurationPath=config});
            var library=provider.Read(CancellationToken.None);var source=library.PlacementSources;
            Check(library.Error==null,"Company library can be read");
            Check(library.Tools.Count==18&&source.Tools.Count==18,"18 DIM and 18 Master slots");
            Check(library.Tools.All(t=>t.Validation.PunchGeometryReadComplete),"18 dimensioned sources read");
            Check(source.Tools.All(t=>t.IdentityMatched&&t.Punch!=null&&t.Punch.Supported),"18 company Master punch selections identified explicitly");
            Check(source.Tools.All(t=>!t.ClampVerified&&!t.PhysicalPairVerified),"No illustrative/physical promotion");
            Check(source.Tools.All(t=>t.MasterDatums.Status==DatumEvidenceStatus.Missing),"Actual source has no verified datums");
            Check(source.LimitRules.Count>0&&source.LimitRules.All(r=>r.ReferenceOnly&&!r.FamilyMappingVerified),"Workbook references remain unverified applicability");
            // Only the SHEET here is a synthetic harness fixture. All tool
            // geometry above is the supplied local company data, never a fake.
            var model=Model(Extrusion(Square().Curves),3);var draft=BendPlacementBuilder.Build(model,library);
            Check(draft.Candidates.Count==54&&draft.Candidates.All(c=>!c.ReadyForCollisionCheck&&c.Status=="CHUA_XAC_MINH"),"All 18 candidates for each of 3 fixture bends, no ready verdict");
            Check(draft.WinnerToolId==null&&draft.Candidates.All(c=>c.PunchPlacement.ToWorkFrame==null),"No guessed placement or tool winner");
            var rows=source.Tools.Select(t=>new {t.Id,t.IdentityMatched,t.MappingIdentified,
                PunchRead=t.Punch?.Supported==true,PunchClosed=t.Punch?.ObservedClosed==true,PunchValidRegion=t.Punch?.Region.IsValidRegion==true,
                HolderSelected=t.Holder!=null,HolderSupported=t.Holder?.Supported==true,HolderValidRegion=t.Holder?.Region.IsValidRegion==true,
                UnsupportedHolderTypes=t.Holder?.UnsupportedTypes,DatumStatus=t.MasterDatums.Status.ToString(),t.ClampVerified,t.PhysicalPairVerified,
                Reasons=draft.Candidates.Where(c=>c.ToolId==t.Id).SelectMany(c=>c.Reasons).Distinct().OrderBy(r=>r).ToArray()}).ToArray();
            var report=new {Passed=true,Assertions=assertions,SheetFixture="SYNTHETIC_ONLY",SourceReasons=source.Reasons,Tools=rows,
                LimitRules=source.LimitRules.Select(r=>new {r.Id,r.Kind,r.FamilyId,r.Sheet,r.Locator,ValueRead=r.ValueMm.HasValue,ToolCount=r.ToolIds.Count,r.FamilyMappingVerified,r.Reasons}),
                Counts=new {PunchRead=rows.Count(t=>t.PunchRead),PunchClosed=rows.Count(t=>t.PunchClosed),PunchRegions=rows.Count(t=>t.PunchValidRegion),
                    HolderSelected=rows.Count(t=>t.HolderSelected),HolderSupported=rows.Count(t=>t.HolderSupported),HolderRegions=rows.Count(t=>t.HolderValidRegion),
                    VerifiedDatum=0,VerifiedClamp=0,Candidates=draft.Candidates.Count,Ready=0}};
            File.WriteAllText(Path.Combine(output,"PRIVATE-company-placement.json"),Json.Serialize(report));
            Console.WriteLine("Company placement pipeline PASS; assertions="+assertions+"; "+Json.Serialize(report.Counts));
            return 0;
        }
        catch(Exception ex)
        {
            Console.WriteLine("Company placement pipeline FAIL: "+ex.Message);
            return 1;
        }
    }
}
