using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class NativeAssemblyBatchValidation
{
    private static void ExerciseMixedOccurrenceScopes(ISldWorks app,ModelDoc2 root,string template,string directory,string sheetPath,
        Component2 normalA,Component2 normalB,Component2 cut,Component2 reflection,object minimum,List<IDisposable> disposable,
        Dictionary<string,object> report)
    {
        var sub=app.NewDocument(template,0,0,0) as ModelDoc2;Check(sub!=null,"Create real flexible subassembly");
        var member=((AssemblyDoc)sub).AddComponent5(sheetPath,0,"",true,"Default",0,0,0);
        Check(member!=null,"Insert same source in flexible test branch");
        string subPath=Path.Combine(directory,"FLEXIBLE.SLDASM");SaveAs(sub,subPath);Activate(app,root);
        var assembly=(AssemblyDoc)root;var flexible=assembly.AddComponent5(subPath,0,"",true,"Default",1.1,0,0);
        Check(flexible!=null,"Insert flexible branch");root.ClearSelection2(true);Check(flexible.Select4(false,null,false),"Preselect flexible branch");
        // Fixture-only mutation. The backend never sets solving/suppression.
        bool configured=assembly.CompConfigProperties5(flexible.GetSuppression2(),(int)swComponentSolvingOption_e.swComponentFlexibleSolving,
            true,false,flexible.ReferencedConfiguration,false,false);
        root.ForceRebuild3(false);
        Check(configured&&flexible.Solving==(int)swComponentSolvingOption_e.swComponentFlexibleSolving,"Native API confirms flexible solving");
        var children=(flexible.GetChildren() as object[]).OfType<Component2>().ToArray();Check(children.Length==1,"One actual flexible descendant");
        // A loaded same-file normal definition can cause SOLIDWORKS to resolve
        // its lightweight twin automatically. Keep this probe a distinct real
        // definition; normal/reflection/cut/flexible still share one source.
        string lightweightPath=Path.Combine(directory,"LIGHTWEIGHT-INPUT.SLDPRT");File.Copy(sheetPath,lightweightPath,false);
        Open(app,lightweightPath);Activate(app,root);
        var lightweight=assembly.AddComponent5(lightweightPath,0,"",true,"Default",1.2,0,0);
        var suppressed=assembly.AddComponent5(sheetPath,0,"",true,"Default",1.3,0,0);
        Check(lightweight!=null&&suppressed!=null,"Insert same-source LW and suppressed probes");root.ForceRebuild3(false);
        Check(suppressed.SetSuppression2(0)==(int)swSuppressionError_e.swSuppressionChangeOk,"Suppress fixture instance");
        // Suppression can rebuild/resolve other fixture members. Set LW last
        // and read back the real state rather than trusting the API return.
        Check(lightweight.SetSuppression2(1)==(int)swSuppressionError_e.swSuppressionChangeOk,"Set fixture instance lightweight");
        int lightweightState=lightweight.GetSuppression2();bool lightweightAvailable=lightweightState==1;
        Console.WriteLine("NATIVE_MIXED_PREP "+Json.Serialize(new {Lightweight=lightweightState,Suppressed=suppressed.GetSuppression2(),Flexible=flexible.Solving}));
        Check(suppressed.GetSuppression2()==0,"Read back actual suppressed state before mixed batch");
        if(!lightweightAvailable)report["LightweightInFlexibleFixture"]="NOT_RUN: requested lightweight but native state was "+lightweightState+"; standalone native LW batch is checked separately";
        root.ClearSelection2(true);
        var sourceModel=(ModelDoc2)normalA.GetModelDoc2();string sourceHash=Sha(sheetPath),sourceState=Json.Serialize(State(sourceModel)),hostState=Json.Serialize(State(root));
        var session=Session(app,root);disposable.Add((IDisposable)session);double wall,sta;
        var all=Run(session,minimum,false,Options(2),out wall,out sta);var allParts=Items(Get(all,"Parts"));
        var inventory=Items(Get(session,"InventoryOccurrences"));
        Func<Component2,object> occurrence=c=>inventory.Single(o=>Convert.ToString(Get(o,"DisplayPath"))==c.Name2);
        string sourceIdentity=Convert.ToString(Get(occurrence(normalA),"SourceIdentity"));
        Check(new[]{normalB,cut,reflection,children[0],suppressed}.All(c=>Convert.ToString(Get(occurrence(c),"SourceIdentity"))==sourceIdentity),
            "Normal/reflection/cut/flexible/suppressed share the same real source/config identity");
        Func<object,string,object> partFor=(batch,id)=>Items(Get(batch,"Parts")).Single(p=>Items(Get(p,"Occurrences")).Any(o=>Convert.ToString(Get(o,"Id"))==id));
        var healthyItem=Items(Get(session,"Items")).Single(i=>Items(Get(i,"AssemblyOccurrences")).Any(o=>Convert.ToString(Get(o,"Id"))==Convert.ToString(Get(occurrence(normalA),"Id"))));
        Check(Get(healthyItem,"AssemblyBatchReason")==null&&Get(healthyItem,"Error")==null&&(int)Get(Get(healthyItem,"Context"),"GeometryReadCount")==1,
            "Native healthy group is actually captured once despite blocked same-source siblings");
        Console.WriteLine("NATIVE_MIXED_CAPTURE "+Json.Serialize(new {Quantity=Get(healthyItem,"Quantity"),Minimum=Get(healthyItem,"MinimumText"),
            Error=Get(healthyItem,"Error"),Rows=Items(Get(healthyItem,"Rows")).Length,Lightweight=lightweight.GetSuppression2()}));
        Check((int)Get(healthyItem,"Quantity")==5&&Items(Get(healthyItem,"Rows")).Length>0&&Convert.ToString(Get(healthyItem,"MinimumText"))!="Chưa xác minh",
            "Check Min keeps normal/reflected/flexible file-config grouping and performs real measurement");
        foreach(var normal in new[]{normalA,normalB})
        {
            var p=partFor(all,Convert.ToString(Get(occurrence(normal),"Id")));int bends=(int)Get(p,"BendCount");
            Check(Get(p,"Reason")==null&&bends>0&&Items(Get(p,"StaticCandidates")).Length==18*bends&&Items(Get(p,"ProcessCandidates")).Length==18*bends,
                "Each normal sibling receives all 18 actual pipeline results per captured bend");
        }
        foreach(var blocked in new[]{cut,reflection,children[0]}.Concat(lightweightAvailable?new[]{lightweight}:new Component2[0]))
        {
            var o=occurrence(blocked);var p=partFor(all,Convert.ToString(Get(o,"Id")));int bends=(int)Get(p,"BendCount");
            Check((int)Get(p,"Quantity")==1&&Convert.ToString(Get(p,"Reason"))==Convert.ToString(Get(o,"Reason")),"Blocked native occurrence retains exact independent reason");
            Check(Items(Get(p,"StaticCandidates")).Length==18*bends&&Items(Get(p,"ProcessCandidates")).Length==18*bends&&
                Items(Get(p,"StaticCandidates")).All(c=>Get(c,"CombinedResult").ToString()=="UNKNOWN")&&
                Items(Get(p,"ProcessCandidates")).All(c=>Get(c,"ProcessResult").ToString()=="UNKNOWN"),"No blocked instance inherits a certificate; known bend slots remain complete");
        }
        Check(inventory.Any(o=>Get(o,"Kind").ToString()=="Suppressed")&&lightweight.GetSuppression2()==lightweightState&&suppressed.GetSuppression2()==0,
            "Mixed backend preserves actual suppressed/lightweight state without resolving");
        string[] ids=allParts.SelectMany(p=>Items(Get(p,"Occurrences")).Select(o=>Convert.ToString(Get(o,"Id")))).ToArray();
        Check(ids.Distinct().Count()==ids.Length,"No duplicated native mixed occurrence output");
        root.ClearSelection2(true);foreach(var c in new[]{normalA,reflection,cut,children[0]})Check(c.Select4(true,null,false),"Select mixed instance");
        var selected=Session(app,root);disposable.Add((IDisposable)selected);var subset=Run(selected,minimum,true,Options(2),out wall,out sta);
        Check((int)Get(subset,"LeafOccurrences")==4&&Items(Get(subset,"Parts")).Sum(p=>(int)Get(p,"Quantity"))==4,"Selected mixed scope keeps exactly four instances");
        var selectedNormal=Items(Get(subset,"Parts")).Single(p=>Items(Get(p,"Occurrences")).Any(o=>Convert.ToString(Get(o,"DisplayPath"))==normalA.Name2));
        Check(Get(selectedNormal,"Reason")==null&&(int)Get(selectedNormal,"BendCount")>0,"Selected normal executes capture while cut/reflection/flexible are blocked");
        foreach(var blocked in new[]{cut,reflection,children[0]})
        {
            var selectedPart=Items(Get(subset,"Parts")).Single(p=>Items(Get(p,"Occurrences")).Any(o=>Convert.ToString(Get(o,"DisplayPath"))==blocked.Name2));
            Check(Convert.ToString(Get(selectedPart,"Reason"))==Convert.ToString(Get(occurrence(blocked),"Reason"))&&
                Get(selectedPart,"GeometricStatus").ToString()=="UNKNOWN","Selected descendant retains the same blocked ancestry/reason as All scope");
        }
        Check(Sha(sheetPath)==sourceHash&&Json.Serialize(State(sourceModel))==sourceState&&Json.Serialize(State(root))==hostState,
            "Mixed all/selected checks preserve original source bytes, dirty/stamp/config and host state");
        report["MixedNativeScopes"]=new {All=true,Selected=true,Normal=2,Reflected=2,AssemblyCut=1,Flexible=1,Lightweight=lightweightAvailable?1:0,Suppressed=1,
            MinimumGroupQuantity=Get(healthyItem,"Quantity"),MinimumGeometryReads=Get(Get(healthyItem,"Context"),"GeometryReadCount"),NumericQueries=Get(all,"KernelQueries"),
            WorkerComQueries=T("BendEnvelope.SheetNativeTopologyCapture").GetProperty("WrongThreadCount",All).GetValue(null,null)};
    }
}
