using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class NativeAssemblyBatchValidation
{
    private static Feature Plane(ModelDoc2 doc)
    {
        for(var f=(Feature)doc.FirstFeature();f!=null;f=(Feature)f.GetNextFeature())if(f.GetTypeName2()=="RefPlane")return f;
        throw new Exception("No native datum plane in fixture template");
    }
    private static ModelDoc2 Block(ISldWorks app,string template,string path,int bodies)
    {
        var doc=app.NewDocument(template,0,0,0) as ModelDoc2;Check(doc!=null,"New block fixture");
        for(int i=0;i<bodies;i++)
        {
            doc.ClearSelection2(true);Check(Plane(doc).Select2(false,0),"Select native datum plane");doc.SketchManager.InsertSketch(true);
            var sketch=(Sketch)doc.GetActiveSketch2();Check(sketch!=null,"Create block sketch");
            doc.SketchManager.CreateCornerRectangle(i*.04,0,0,i*.04+.02,.02,0);doc.SketchManager.InsertSketch(true);
            doc.ClearSelection2(true);Check(SketchFeature(app,doc,sketch).Select2(false,0),"Select block profile");
            var feature=doc.FeatureManager.FeatureExtrusion3(true,false,false,0,0,.01,0,false,false,false,false,0,0,false,false,false,false,false,false,true,0,0,false);
            Check(feature!=null,"Create actual separate block body");
        }
        doc.ForceRebuild3(false);SaveAs(doc,path);Check((((PartDoc)doc).GetBodies2(0,false) as object[]).Length==bodies,"Native block body count");return doc;
    }
    private static Feature SketchFeature(ISldWorks app,ModelDoc2 doc,Sketch sketch)
    {
        for(var feature=(Feature)doc.FirstFeature();feature!=null;feature=(Feature)feature.GetNextFeature())
            if(feature.GetSpecificFeature2() is Sketch candidate&&app.IsSame(candidate,sketch)==1)return feature;
        throw new Exception("Created sketch feature not found");
    }
    private static IEnumerable<Feature> SketchFeatures(ModelDoc2 doc)
    {
        for(var f=(Feature)doc.FirstFeature();f!=null;f=(Feature)f.GetNextFeature())if(f.GetSpecificFeature2() is Sketch)yield return f;
    }
    private static object Inventory(ISldWorks app,ModelDoc2 root,List<IDisposable> disposable)
    {Activate(app,root);root.ClearSelection2(true);root.ForceRebuild3(false);var session=Session(app,root);disposable.Add((IDisposable)session);Call(session,"BuildInventory",false);return session;}
    private static void EdgeFixtures(ISldWorks app,ModelDoc2 main,string assemblyTemplate,string output,List<IDisposable> disposable,Dictionary<string,object> report,object minimumSource=null)
    {
        string directory=Path.Combine(output,"edge-fixtures-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string partTemplate=System.Environment.GetEnvironmentVariable("ADDIN_NATIVE_PART_TEMPLATE")??Path.Combine(Path.GetDirectoryName(assemblyTemplate),"部品.PRTDOT");
        var cube=Block(app,partTemplate,Path.Combine(directory,"BLOCK.SLDPRT"),1);var multi=Block(app,partTemplate,Path.Combine(directory,"MULTI.SLDPRT"),2);
        var edgeRoot=app.NewDocument(assemblyTemplate,0,0,0) as ModelDoc2;Check(edgeRoot!=null,"Create native edge Assembly");var a=(AssemblyDoc)edgeRoot;
        var nonSheet=a.AddComponent5(cube.GetPathName(),0,"",true,"Default",.3,0,0);var multiBody=a.AddComponent5(multi.GetPathName(),0,"",true,"Default",.4,0,0);
        string sheetPath=Path.Combine(output,"GENERATED-02.SLDPRT");
        var first=a.AddComponent5(sheetPath,0,"",true,"Default",0,0,0);var second=a.AddComponent5(Path.Combine(output,"GENERATED-03.SLDPRT"),0,"",true,"Default",.2,0,0);
        // MakeVirtual operates on the source definition, so give this case its
        // own source instead of unintentionally converting both mirror inputs.
        string virtualPath=Path.Combine(directory,"VIRTUAL-INPUT.SLDPRT");File.Copy(sheetPath,virtualPath,false);Open(app,virtualPath);Activate(app,edgeRoot);
        var virtualPart=a.AddComponent5(virtualPath,0,"",true,"Default",.5,0,0);Check(virtualPart!=null&&virtualPart.MakeVirtual2(false),"Actual virtual Part fixture");
        a.EditAssembly();edgeRoot.ForceRebuild3(false);SaveAs(edgeRoot,Path.Combine(directory,"EDGE.SLDASM"));
        var inventory=Inventory(app,edgeRoot,disposable);var initial=Items(Get(inventory,"InventoryOccurrences"));
        Check(initial.Count(o=>Get(o,"Kind").ToString()=="NonSheetMetal")==1,"Actual non-sheet-metal is skipped");
        Check(initial.Count(o=>Convert.ToString(Get(o,"Reason"))=="MULTIBODY_INSTANCE_UNSUPPORTED")==1,"Actual multibody does not reuse one-body result");
        Check(initial.Count(o=>(bool)Get(o,"Virtual"))==1&&initial.Where(o=>(bool)Get(o,"Virtual")).All(o=>!(bool)Get(o,"GeometryShareable")),"Virtual source isolated");
        // Actual native mirror feature, not an annotation or a fabricated DTO.
        a.EditAssembly();edgeRoot.ClearSelection2(true);
        Plane(edgeRoot).Select2(false,1);
        var mirrorSelect=(SelectData)((SelectionMgr)edgeRoot.SelectionManager).CreateSelectData();mirrorSelect.Mark=2;
        Check(first.Select4(true,mirrorSelect,false)&&second.Select4(true,mirrorSelect,false),"Preselect native mirror seeds");
        object selectedPlane=((SelectionMgr)edgeRoot.SelectionManager).GetSelectedObject6(1,1);Check(selectedPlane!=null,"Native mirror plane selection");
        var mirrorData=(MirrorComponentFeatureData)edgeRoot.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmMirrorComponent);
        mirrorData.MirrorPlane=selectedPlane;
        mirrorData.OppositeHandComponents=new object[]{first,second};mirrorData.CreateDerivedConfigurations=false;
        mirrorData.MirroredComponentFilenames=new string[]{"MIRROR-A.SLDPRT","MIRROR-B.SLDPRT"};
        mirrorData.NameModifierType=(int)swMirrorComponentNameModifier_e.swMirrorComponentName_Custom;
        mirrorData.PlaceFilesInOneFolder=true;mirrorData.MirrorComponentsFolderLocation=directory;
        mirrorData.ReplaceFileLocations=new string[]{directory,directory};
        mirrorData.MirrorTransferOptions=(int)swMirrorPartOptions_e.swMirrorPartOptions_ImportSolids;
        mirrorData.PreserveZAxis=true;mirrorData.ForceUseSeedConfiguration=true;
        var mirrorFeature=edgeRoot.FeatureManager.CreateFeature(mirrorData);mirrorData.ReleaseSelectionAccess();edgeRoot.ForceRebuild3(false);
        inventory=Inventory(app,edgeRoot,disposable);var mirrored=Items(Get(inventory,"InventoryOccurrences")).Where(o=>(bool)Get(o,"Mirrored")).ToArray();
        if(mirrorFeature!=null)
            Check(mirrored.Length==2&&mirrored.All(o=>!(bool)Get(o,"GeometryShareable")&&Get(o,"Kind").ToString()=="Unsupported"),"Actual mirrored sources cannot inherit source certification");
        else
            report["OppositeHandMirrorFeature"]="NOT_RUN: native CreateFeature returned null; no mirrored flag fabricated";
        // Independent real native reflected-transform case. Read back the
        // determinant; a rejected transform is a failing test, never a fake tag.
        var reflected=new List<Component2>();
        for(int i=0;i<2;i++)
        {
            var c=a.AddComponent5(sheetPath,0,"",true,"Default",.6+i*.1,0,0);Check(c!=null,"Create native reflection probe");
            c.Transform2=(MathTransform)((MathUtility)app.GetMathUtility()).CreateTransform(new double[]{-1,0,0,0,1,0,0,0,1,.6+i*.1,0,0,1,0,0,0});
            var t=(double[])c.GetTotalTransform(false).ArrayData;
            double determinant=t[0]*(t[4]*t[8]-t[5]*t[7])-t[1]*(t[3]*t[8]-t[5]*t[6])+t[2]*(t[3]*t[7]-t[4]*t[6]);
            Check(determinant<-.999999,"SOLIDWORKS retained an actual reflected transform");reflected.Add(c);
        }
        var normalProbe=a.AddComponent5(sheetPath,0,"",true,"Default",.85,0,0);
        Check(normalProbe!=null,"Add an ordinary same-source instance next to native reflected instances");
        var normalSibling=a.AddComponent5(sheetPath,0,"",true,"Default",.95,0,0);
        Check(normalSibling!=null,"Add second normal same-source sibling");
        inventory=Inventory(app,edgeRoot,disposable);
        Check(Items(Get(inventory,"InventoryOccurrences")).Count(o=>Convert.ToString(Get(o,"Reason"))=="MIRRORED_OR_UNVERIFIED_INSTANCE_TRANSFORM")>=2,"Native reflections cannot inherit rigid-source certification");
        // Cut exactly one Assembly instance, with propagation to source disabled.
        object info;var body=((object[])first.GetBodies3(0,out info)).OfType<Body2>().Single();
        var face=(body.GetFaces() as object[]).OfType<Face2>().OrderByDescending(f=>f.GetArea()).First();var box=(double[])face.GetBox();
        var closest=(double[])face.GetClosestPointOn((box[0]+box[3])/2,(box[1]+box[4])/2,(box[2]+box[5])/2);
        var math=(MathUtility)app.GetMathUtility();var point=(MathPoint)math.CreatePoint(new[]{closest[0],closest[1],closest[2]});point=(MathPoint)point.MultiplyTransform(first.GetTotalTransform(false));
        a.EditAssembly();Activate(app,edgeRoot);
        var target=a.GetEditTarget() as ModelDoc2;Check(target==null||app.IsSame(target,edgeRoot)==1,"Cut builder is in Assembly edit context");
        var sketchIds=new HashSet<int>(SketchFeatures(edgeRoot).Select(f=>f.GetID()));
        var assemblyFace=first.GetCorrespondingEntity(face) as Entity;
        edgeRoot.ClearSelection2(true);Check(assemblyFace!=null&&assemblyFace.Select4(false,null),"Select actual assembly member face for cut plane");edgeRoot.SketchManager.InsertSketch(true);var sketch=(Sketch)edgeRoot.GetActiveSketch2();
        Check(sketch!=null,"Assembly cut sketch creation");
        point=(MathPoint)point.MultiplyTransform(sketch.ModelToSketchTransform);var xy=(double[])point.ArrayData;
        var projected=(MathPoint)((MathPoint)math.CreatePoint(new[]{xy[0],xy[1],0.0})).MultiplyTransform((MathTransform)sketch.ModelToSketchTransform.Inverse());
        var planePoint=(double[])projected.ArrayData;
        // SketchManager coordinates are in model space; the chosen assembly
        // template need not use the Part template's first-plane orientation.
        bool oldAddToDb=edgeRoot.SketchManager.AddToDB;edgeRoot.SketchManager.AddToDB=true;
        SketchSegment circle;try{circle=edgeRoot.SketchManager.CreateCircleByRadius(planePoint[0],planePoint[1],planePoint[2],.001);}finally{edgeRoot.SketchManager.AddToDB=oldAddToDb;}
        Check(circle!=null,"Actual cut circle created");edgeRoot.SketchManager.InsertSketch(true);edgeRoot.ClearSelection2(true);
        var addedSketch=SketchFeatures(edgeRoot).Where(f=>!sketchIds.Contains(f.GetID())).ToArray();
        if(addedSketch.Length!=1)
        {
            var featureList=new List<object>();for(var f=(Feature)edgeRoot.FirstFeature();f!=null;f=(Feature)f.GetNextFeature())featureList.Add(new {f.Name,Kind=f.GetTypeName2(),Specific=f.GetSpecificFeature2()?.GetType().FullName});
            Console.WriteLine("CUT_SKETCH_DIAGNOSTIC "+Json.Serialize(new {Sketches=addedSketch.Length,ActiveSketch=edgeRoot.GetActiveSketch2()!=null,FeatureCount=edgeRoot.GetFeatureCount(),Features=featureList}));
        }
        Check(addedSketch.Length==1,"One newly added native assembly sketch");addedSketch[0].Select2(false,0);
        var selection=(SelectData)((SelectionMgr)edgeRoot.SelectionManager).CreateSelectData();selection.Mark=1;Check(first.Select4(true,selection,false),"Set one-component Assembly cut scope");
        var cut=edgeRoot.FeatureManager.FeatureCut4(false,false,false,1,1,.1,.1,false,false,false,false,0,0,false,false,false,false,false,true,false,true,false,false,0,0,false,false);
        Check(cut!=null,"Actual Assembly-only cut exists");edgeRoot.ForceRebuild3(false);
        Console.WriteLine("CUT_SCOPE "+a.GetFeatureScopeCount(cut));
        if(a.GetFeatureScopeCount(cut)==0)
        {edgeRoot.ClearSelection2(true);cut.Select2(false,0);Check(a.AddToFeatureScope(first.Name2),"Explicit one-member cut scope");a.UpdateFeatureScope();edgeRoot.ForceRebuild3(false);}
        inventory=Inventory(app,edgeRoot,disposable);var cutOccurrences=Items(Get(inventory,"InventoryOccurrences")).Where(o=>(bool)Get(o,"Modified")).ToArray();
        bool cutWarning;int cutError=cut.GetErrorCode2(out cutWarning);
        Console.WriteLine("CUT_NATIVE_RESULT "+Json.Serialize(new {Count=cutOccurrences.Length,Error=cutError,Warning=cutWarning,
            Occurrences=Items(Get(inventory,"InventoryOccurrences")).Select(o=>new{Kind=Get(o,"Kind").ToString(),Modified=Get(o,"Modified"),Reason=Get(o,"Reason")})}));
        Check(cutOccurrences.Length==1&&cutOccurrences.All(o=>!(bool)Get(o,"GeometryShareable")&&Convert.ToString(Get(o,"Reason"))=="ASSEMBLY_MODIFIED_BODY_REQUIRES_INSTANCE_GEOMETRY"),"One real assembly-cut body isolated");
        // One file/config Min item now intentionally contains both an ordinary
        // occurrence and reflected, unsupported siblings.
        var mixed=Items(Get(inventory,"Items")).Where(item=>Items(Get(item,"AssemblyOccurrences"))
            .Any(o=>string.Equals(Convert.ToString(Get(o,"DisplayPath")),normalProbe.Name2,StringComparison.Ordinal))).ToArray();
        Check(mixed.Length==1,"Normal probe resolves to one shared Min file/config group");
        var members=Items(Get(mixed[0],"AssemblyOccurrences"));
        Check(members.Any(o=>Get(o,"Kind").ToString()=="SheetMetal"&&Get(o,"Reason")==null)&&
            members.Count(o=>Convert.ToString(Get(o,"Reason"))=="MIRRORED_OR_UNVERIFIED_INSTANCE_TRANSFORM")>=2&&
            Get(mixed[0],"AssemblyBatchReason")==null,
            "Native mixed source cannot let reflected members poison an eligible Min group");
        if(minimumSource!=null)
        {
            double wall,sta;
            var batch=Run(inventory,minimumSource,false,Options(2),out wall,out sta);
            var parts=Items(Get(batch,"Parts"));
            var ordinary=parts.Where(part=>Items(Get(part,"Occurrences"))
                .Any(o=>string.Equals(Convert.ToString(Get(o,"DisplayPath")),normalProbe.Name2,StringComparison.Ordinal))).ToArray();
            Check(ordinary.Length==1&&Get(ordinary[0],"Reason")==null&&
                (int)Get(ordinary[0],"BendCount")>0&&Items(Get(ordinary[0],"StaticCandidates")).Length>0,
                "Native eligible instance still captures bends and gets 18-tool results");
            var reflectedParts=parts.Where(part=>Convert.ToString(Get(part,"Reason"))=="MIRRORED_OR_UNVERIFIED_INSTANCE_TRANSFORM").ToArray();
            Check(reflectedParts.Length>=2&&reflectedParts.All(part=>(int)Get(part,"Quantity")==1&&
                Items(Get(part,"StaticCandidates")).Length>0),
                "Native reflected instances are individually UNKNOWN, never inherited source certificates");
            report["MixedOccurrenceIsolation"]=new {Normal=ordinary.Length,Blocked=reflectedParts.Length,
                ExactReason="MIRRORED_OR_UNVERIFIED_INSTANCE_TRANSFORM"};
            ExerciseMixedOccurrenceScopes(app,edgeRoot,assemblyTemplate,directory,sheetPath,normalProbe,normalSibling,first,
                reflected[0],minimumSource,disposable,report);
        }
        report["NativeEdgeFixtures"]=new {NonSheetMetal=1,Multibody=1,Virtual=1,OppositeHandMirrorFeature=mirrorFeature!=null,Mirrored=mirrored.Length,ReflectedTransforms=reflected.Count,AssemblyCut=cutOccurrences.Length};
        Activate(app,main);
    }
}
