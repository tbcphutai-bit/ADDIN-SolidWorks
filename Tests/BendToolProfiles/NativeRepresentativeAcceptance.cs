using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace CheckChan.NativeAcceptance
{
    public sealed partial class AcceptanceAddin
    {
        private RepresentativeAcceptanceInput representativeCases;
        private readonly List<Dictionary<string,object>> representativeResults=new List<Dictionary<string,object>>();
        private readonly Dictionary<ModelDoc2,string> representativeStates=new Dictionary<ModelDoc2,string>();
        private int representativeIndex,representativeStage,representativeAssertions;
        private ModelDoc2 representativeModel;
        private object representativeController;
        private string representativeColdFacts;
        private readonly Stopwatch representativeRunClock=new Stopwatch();
        private static readonly BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private static object Value(object source,string name)=>source==null?null:Field(source,name)??Property(source,name);
        private static object[] List(object source)=>source==null?new object[0]:((IEnumerable)source).Cast<object>().ToArray();
        private static object Call(object source,string name,params object[] args)=>source.GetType().GetMethod(name,Members).Invoke(source,args);
        private static string Serialize(object value)=>new System.Web.Script.Serialization.JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(value);
        private void Assert(bool condition,string reason){representativeAssertions++;if(!condition)throw new InvalidOperationException(reason);}
        private void LoadRepresentativeCases()
        {
            string manifest=System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_CASES");if(string.IsNullOrWhiteSpace(manifest))return;
            representativeCases=RepresentativeAcceptanceInput.Read(manifest);
            foreach(var c in representativeCases.Cases)representativeResults.Add(new Dictionary<string,object>{{"CaseId",c.CaseId},{"FixturePath",c.Path},{"Execution","NOT_RUN"}});
            state["RepresentativeCases"]=representativeResults;
        }
        private object RepresentativeControl=>Field(target,"uiControl");
        private void OpenRepresentativeWindow()
        {
            var button=(Control)Field(RepresentativeControl,"btnCheckBendMinimum");
            typeof(Control).GetMethod("OnClick",Members).Invoke(button,new object[]{EventArgs.Empty});
            window=Field(RepresentativeControl,"bendCheckWindow") as Form;
            representativeController=Field(RepresentativeControl,"bendCheckController");
            Assert(window!=null&&!window.IsDisposed&&representativeController!=null,"EXISTING_CHECK_CHAN_COMMAND_DID_NOT_OPEN");
            Assert(window.GetType().Assembly==assembly,"EXACT_FINAL_DLL_UI_REQUIRED");
        }
        private void ClickRepresentative(string name)
        {
            var button=(Button)window.Controls.Find(name,true).Single();Assert(button.Enabled,"DISABLED_ACTION:"+name);button.PerformClick();
        }
        private static string ModelState(ModelDoc2 model)=>Serialize(new {Dirty=model.GetSaveFlag(),Stamp=model.GetUpdateStamp(),Configuration=model.ConfigurationManager.ActiveConfiguration.Name});
        private int[] IndependentBendIds(ModelDoc2 model)
        {
            var ids=new HashSet<int>();
            foreach(Body2 body in ((PartDoc)model).GetBodies2(0,false) as object[]??new object[0])
            foreach(Face2 face in body.GetFaces() as object[]??new object[0])
            {
                var surface=(Surface)face.GetSurface();if(!surface.IsCylinder())continue;
                var feature=(Feature)face.GetFeature();var definition=feature.GetDefinition();
                if(!(definition is IOneBendFeatureData)&&!(definition is ISketchedBendFeatureData)&&!(definition is IEdgeFlangeFeatureData))continue;
                ids.Add(feature.GetID());int error;var persistent=(byte[])model.Extension.GetPersistReference3(feature);
                Assert(model.Extension.GetObjectByPersistReference3(persistent,out error)!=null&&error==0,"NATIVE_FEATURE_PERSISTENT_ID_NOT_RESOLVABLE");
            }
            return ids.OrderBy(id=>id).ToArray();
        }
        private void AuditRepresentativePart(RepresentativeCase item)
        {
            var ids=IndependentBendIds(representativeModel);Assert(ids.SequenceEqual(item.FeatureIds),"INDEPENDENT_NATIVE_FEATURE_IDS_CHANGED");
            var bodies=(object[])((PartDoc)representativeModel).GetBodies2(0,false);Assert(bodies.Length==1,"ONE_EXISTING_NATIVE_BODY_REQUIRED");
            var body=(Body2)bodies[0];var extents=new double[3];
            for(int axis=0;axis<3;axis++)
            {
                var d=new double[3];d[axis]=1;double x,y,z;
                Assert(body.GetExtremePoint(d[0],d[1],d[2],out x,out y,out z),"NATIVE_MAX_EXTREME_FAILED");double max=new[]{x,y,z}[axis];
                Assert(body.GetExtremePoint(-d[0],-d[1],-d[2],out x,out y,out z),"NATIVE_MIN_EXTREME_FAILED");extents[axis]=(max-new[]{x,y,z}[axis])*1000;
                Assert(Math.Abs(extents[axis]-item.OuterExtentsMm[axis])<1e-5,"ACTUAL_NATIVE_OUTER_EXTENT_CHANGED");
            }
            var row=representativeResults[representativeIndex];row["NativeFeatureIds"]=ids;row["ActualOuterExtentsMm"]=extents;
        }
        private void StartRepresentativeRun(string button)
        { representativeRunClock.Restart();ClickRepresentative(button); }
        private object ValidateRepresentativeResult(string label,bool selected=false)
        {
            var snapshot=Value(representativeController,"displayed");Assert(snapshot!=null&&Equals(Value(snapshot,"Current"),true),"CURRENT_NATIVE_RESULT_REQUIRED:"+label);
            var parts=List(Field(window,"allParts"));Assert(parts.Length>0,"EMPTY_NATIVE_PART_RESULTS");
            var item=representativeCases.Cases[representativeIndex];var facts=new List<object>();int bends=0;
            var backend=Field(representativeController,"backend");var sessionItems=List(Value(Field(backend,"session"),"Items"));
            foreach(var part in parts)
            {
                var raw=Value(part,"RawPartResult");var metadata=List(Value(raw,"BendMetadata")).OrderBy(m=>Convert.ToInt32(Value(m,"FeatureId"))).ToArray();
                var sources=sessionItems.Where(i=>Path.GetFileNameWithoutExtension(Convert.ToString(Value(i,"Name")))==Path.GetFileNameWithoutExtension(Convert.ToString(Value(part,"PartName")))&&
                    Convert.ToString(Value(i,"Configuration"))==Convert.ToString(Value(part,"Configuration"))).ToArray();
                Assert(sources.Length==1,"UNIQUE_NATIVE_PART_CONFIGURATION_REQUIRED");
                var model=(ModelDoc2)Value(sources[0],"Model");
                Assert(metadata.Select(m=>Convert.ToInt32(Value(m,"FeatureId"))).SequenceEqual(IndependentBendIds(model)),"UI_FEATURE_IDS_DIFFER_FROM_NATIVE_FACE_OWNERS");
                var minimumRows=List(Value(sources[0],"Rows"));
                foreach(var m in metadata)
                {
                    int id=Convert.ToInt32(Value(m,"FeatureId"));var row=minimumRows.Single(r=>Convert.ToInt32(Value(r,"FeatureId"))==id);
                    foreach(string collection in new[]{"StaticCandidates","ProcessCandidates"})
                    {
                        var candidates=List(Value(raw,collection)).Where(c=>Convert.ToInt32(Value(c,"FeatureId"))==id).ToArray();
                        Assert(candidates.Length==18&&candidates.Select(c=>Value(c,"ToolId")).Distinct().Count()==18,"18_ACTUAL_BACKEND_CANDIDATES_REQUIRED:"+collection);
                    }
                    var legacy=Call(Value(row,"MinimumContext"),"Evaluate",row);
                    Assert(Convert.ToString(Value(legacy,"Status"))==Convert.ToString(Value(m,"MinimumStatus"))&&Equals(Value(legacy,"Summary"),Value(m,"MinimumSummary")),"CHECK_MIN_REGRESSION");
                    var expected=List(Value(legacy,"Sides")).Where(s=>Value(s,"Measure")!=null).ToArray();var actual=List(Value(m,"Sides"));
                    Assert(expected.Length==actual.Length,"CHECK_MIN_SIDE_COUNT_CHANGED");
                    for(int side=0;side<expected.Length;side++)Assert(Equals(Value(Value(expected[side],"Measure"),"ValueMm"),Value(actual[side],"ActualMm"))&&
                        Equals(Value(expected[side],"LimitMm"),Value(actual[side],"MinimumMm")),"CHECK_MIN_DIMENSION_OR_LIMIT_CHANGED");
                    int error;Assert(model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(Convert.ToString(Value(m,"PersistentId"))),out error)!=null&&error==0,"UI_PERSISTENT_ID_NOT_NATIVE");
                }
                foreach(var bend in List(Value(part,"Bends")))
                {
                    var tools=List(Value(bend,"ToolCandidates"));bends++;
                    Assert(tools.Length==18&&tools.Select(t=>Value(t,"ToolId")).Distinct().Count()==18,"18_UNIQUE_TOOLS_REQUIRED_PER_BEND");
                    Assert(tools.All(t=>Convert.ToString(Value(t,"PunchStatus"))=="UNKNOWN"&&Convert.ToString(Value(t,"HolderStatus"))=="UNKNOWN"&&Convert.ToString(Value(t,"ProcessStatus"))=="UNKNOWN"),"MISSING_EVIDENCE_MUST_REMAIN_UNKNOWN");
                    Assert(tools.All(t=>Equals(Value(Value(t,"CadAssembly"),"Id"),Value(t,"ToolId"))&&Equals(Value(Value(Value(t,"CadAssembly"),"CadPair"),"Matched"),true)),"FIXED_COMPANY_PAIR_BINDING_REQUIRED");
                }
                if(item.DocumentType==1)
                {
                    Assert(metadata.Select(m=>Convert.ToInt32(Value(m,"FeatureId"))).SequenceEqual(item.FeatureIds),"BASELINE_FEATURE_IDS_CHANGED");
                    Assert(metadata.Select(m=>Convert.ToString(Value(m,"MinimumStatus"))).SequenceEqual(item.MinimumStatuses),"BASELINE_CHECK_MIN_CHANGED");
                }
                Assert(Value(raw,"WinnerToolId")==null&&Convert.ToString(Value(raw,"ManufacturingStatus"))=="CHUA_XAC_MINH","NO_MANUFACTURING_APPROVAL_ALLOWED");
                if(metadata.Length>0)Assert(List(Value(Value(raw,"Planning"),"Tools")).Length==18,"18_PLANNING_CANDIDATES_REQUIRED");
                facts.Add(new {Name=Value(part,"PartName"),Quantity=Value(part,"Quantity"),Min=metadata.Select(m=>new {Id=Value(m,"FeatureId"),Status=Convert.ToString(Value(m,"MinimumStatus")),Sides=Value(m,"Sides")}).ToArray(),
                    Tools=List(Value(part,"Bends")).SelectMany(b=>List(Value(b,"ToolCandidates"))).Select(t=>new {Id=Value(t,"ToolId"),Feature=Value(t,"FeatureId"),Punch=Convert.ToString(Value(t,"PunchStatus")),Holder=Convert.ToString(Value(t,"HolderStatus")),Process=Convert.ToString(Value(t,"ProcessStatus")),Reason=Value(t,"CombinedReason")}).ToArray()});
            }
            Assert(bends==(selected?3:item.ExpectedBends),"UNEXPECTED_BEND_COUNT:"+bends);
            if(selected)Assert(Convert.ToInt32(Value(snapshot,"LeafOccurrences"))==1,"SELECTED_MUST_NOT_BECOME_ENTIRE");
            var record=new {Milliseconds=representativeRunClock.Elapsed.TotalMilliseconds,NativeCaptureMilliseconds=Value(snapshot,"CaptureMilliseconds"),
                Captures=Value(snapshot,"Captures"),CaptureHits=Value(snapshot,"CaptureHits"),NumericQueries=Value(snapshot,"KernelQueries"),
                GeometryHits=Value(snapshot,"GeometryHits"),StaticHits=Value(snapshot,"StaticHits"),ProcessHits=Value(snapshot,"ProcessHits"),Parts=parts.Length,Bends=bends,Facts=facts};
            representativeResults[representativeIndex][label]=record;
            state["Assertions"]=representativeAssertions;Write("REPRESENTATIVE_RUNNING");return facts;
        }
        private void RepresentativeTick()
        {
            if(clock.ElapsedMilliseconds>60000)throw new TimeoutException("Original 60s per-case diagnostic deadline expired");
            var item=representativeCases.Cases[representativeIndex];
            if(representativeStage==0)
            {
                clock.Restart();representativeCases.CheckSources();int error=0,warning=0;
                representativeModel=app.OpenDoc6(item.Path,item.DocumentType,3,"",ref error,ref warning) as ModelDoc2;
                Assert(representativeModel!=null,"READ_ONLY_EXISTING_FIXTURE_OPEN_FAILED:"+error);
                app.ActivateDoc3(representativeModel.GetTitle(),false,0,ref error);
                representativeResults[representativeIndex]["OpenWarning"]=warning;
                representativeStates.Clear();foreach(ModelDoc2 m in (app.GetDocuments() as object[]??new object[0]))representativeStates[m]=ModelState(m);
                if(item.DocumentType==1)AuditRepresentativePart(item);
                OpenRepresentativeWindow();StartRepresentativeRun("btnRunBatch");representativeStage=1;
            }
            else if(Equals(Value(representativeController,"Running"),true)){timer.Start();return;}
            else if(representativeStage==1)
            { representativeColdFacts=Serialize(ValidateRepresentativeResult("Cold"));StartRepresentativeRun("btnRunBatch");representativeStage=2; }
            else if(representativeStage==2)
            {
                Assert(NativeMatrix.MatrixInput.SameJsonFacts(Serialize(ValidateRepresentativeResult("Warm")),representativeColdFacts),"COLD_WARM_FACTS_CHANGED");
                Assert(Convert.ToInt32(Value(Value(representativeController,"displayed"),"Captures"))==0,"WARM_NATIVE_CAPTURE_REPEATED");
                StartRepresentativeRun("btnRefresh");representativeStage=3;
            }
            else if(representativeStage==3)
            {
                Assert(NativeMatrix.MatrixInput.SameJsonFacts(Serialize(ValidateRepresentativeResult("Refresh")),representativeColdFacts),"REFRESH_FEATURE_MIN_OR_UNKNOWN_CHANGED");
                StartRepresentativeRun("btnRunBatch");ClickRepresentative("btnCancel");representativeStage=4;
            }
            else if(representativeStage==4)
            {
                Assert(Value(representativeController,"displayed")==null&&List(Field(window,"allParts")).Length==0,"CANCEL_PUBLISHED_A_RESULT");
                representativeResults[representativeIndex]["Cancel"]="PASS";StartRepresentativeRun("btnRunBatch");representativeStage=5;
            }
            else if(representativeStage==5)
            {
                Assert(NativeMatrix.MatrixInput.SameJsonFacts(Serialize(ValidateRepresentativeResult("AfterCancel")),representativeColdFacts),"RESTART_FACTS_CHANGED");
                if(item.DocumentType==2)
                {
                    var components=((AssemblyDoc)representativeModel).GetComponents(false) as object[]??new object[0];
                    var selected=components.Cast<Component2>().First(c=>c.GetSuppression()!=0&&c.GetModelDoc2() is ModelDoc2 m&&m.GetType()==1&&IndependentBendIds(m).Length==3);
                    representativeModel.ClearSelection2(true);Assert(selected.Select4(false,null,false),"NATIVE_COMPONENT_SELECTION_FAILED");
                    var method=window.GetType().GetMethod("SetScope",Members);method.Invoke(window,new[]{Enum.Parse(method.GetParameters()[0].ParameterType,"SelectedComponents")});
                    StartRepresentativeRun("btnRunBatch");representativeStage=6;
                }
                else {StartRepresentativeRun("btnRunBatch");window.Close();representativeStage=8;}
            }
            else if(representativeStage==6)
            { ValidateRepresentativeResult("Selected",true);representativeModel.ClearSelection2(true);StartRepresentativeRun("btnRefresh");representativeStage=7; }
            else if(representativeStage==7)
            { ValidateRepresentativeResult("SelectedRefresh",true);StartRepresentativeRun("btnRunBatch");window.Close();representativeStage=8; }
            else if(representativeStage==8)
            {
                if(!window.IsDisposed){timer.Start();return;}
                representativeResults[representativeIndex]["CloseDuringRun"]="PASS";
                foreach(var source in representativeStates)Assert(ModelState(source.Key)==source.Value,"ORIGINAL_DOCUMENT_STATE_CHANGED");
                representativeCases.CheckSources();OpenRepresentativeWindow();window.Close();Assert(window.IsDisposed,"REOPEN_CLOSE_FAILED");
                representativeResults[representativeIndex]["OpenClose"]="PASS";representativeResults[representativeIndex]["Execution"]="PASS";
                representativeResults[representativeIndex]["TotalMilliseconds"]=clock.Elapsed.TotalMilliseconds;
                app.CloseDoc(representativeModel.GetTitle());representativeModel=null;representativeIndex++;representativeStage=0;clock.Restart();
                if(representativeIndex==representativeCases.Cases.Length)
                { state["Assertions"]=representativeAssertions;state["FunctionalResult"]="PASS";state["ProtectedSourcesUnchanged"]=true;Write("COMMAND_COMPLETED");return; }
            }
            timer.Start();
        }
    }
}
