// Narrow live acceptance: two existing documents, actual product command/Run button.
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
        private int liveCase,liveStep;
        private ModelDoc2 liveModel;
        private object liveController;
        private bool liveSelected;
        private readonly List<object> liveResults=new List<object>();
        private void LiveRequire(bool condition,string message)
        { if(!condition)throw new InvalidOperationException(message); }
        private void LiveOpenForm()
        {
            var ui=Field(target,"uiControl") as Control;
            var button=Field(ui,"btnCheckBendMinimum") as Control;
            LiveRequire(button!=null,"CHECK_CHAN_COMMAND_MISSING");
            typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{EventArgs.Empty});
            window=Field(ui,"bendCheckWindow") as Form;liveController=Field(ui,"bendCheckController");
            LiveRequire(window!=null&&!window.IsDisposed&&liveController!=null,"LIVE_WINDOW_OR_CONTROLLER_MISSING");
        }
        private void LiveClick(string name)
        {
            var button=window.Controls.Find(name,true).Single() as Button;
            LiveRequire(button!=null&&button.Enabled,"LIVE_BUTTON_NOT_ENABLED:"+name);
            button.PerformClick();
        }
        private void LiveValidate(string run)
        {
            var snapshot=Value(liveController,"displayed");
            string message=window.Controls.Find("lblProgressTitle",true).Single().Text;
            LiveRequire(snapshot!=null&&Equals(Value(snapshot,"Current"),true),"NO_CURRENT_RESULT:"+message);
            var parts=List(Field(window,"allParts"));
            LiveRequire(parts.Length>0,"NO_PARTS_DISPLAYED:"+message);
            var backend=Value(liveController,"backend");var session=Value(backend,"session");var library=Value(session,"ToolLibrary");
            var tools=List(Value(library,"Tools"));int data=tools.Count(t=>Equals(Value(Value(t,"Validation"),"PunchGeometryReadComplete"),true));
            int matched=List(Value(Value(library,"PlacementSources"),"Tools")).Count(p=>Equals(Value(Value(p,"CadPair"),"Matched"),true));
            LiveRequire(tools.Length==18&&data==18&&matched==18,"LIVE_TOOL_SOURCE_NOT_READ: "+Value(library,"Error"));
            var facts=new List<object>();int bends=0,candidates=0;
            foreach(var part in parts)
            {
                var raw=Value(part,"RawPartResult");var metadata=List(Value(raw,"BendMetadata"));
                foreach(var bend in List(Value(part,"Bends")))
                {
                    int feature=Convert.ToInt32(Value(bend,"FeatureId"));
                    var row=metadata.Single(m=>Convert.ToInt32(Value(m,"FeatureId"))==feature);
                    LiveRequire(!string.IsNullOrWhiteSpace(Convert.ToString(Value(row,"PersistentId"))),"MISSING_NATIVE_PERSISTENT_ID");
                    var candidatesForBend=List(Value(bend,"ToolCandidates"));
                    LiveRequire(candidatesForBend.Length==18&&candidatesForBend.Select(t=>Value(t,"ToolId")).Distinct().Count()==18,"18_UNIQUE_CANDIDATES_REQUIRED");
                    LiveRequire(candidatesForBend.All(t=>Convert.ToString(Value(t,"PunchStatus"))=="UNKNOWN"&&Convert.ToString(Value(t,"HolderStatus"))=="UNKNOWN"),"MISSING_SETUP_MUST_REMAIN_UNKNOWN");
                    bends++;candidates+=candidatesForBend.Length;
                }
                if(liveCase==0)
                {
                    LiveRequire(metadata.Select(m=>Convert.ToInt32(Value(m,"FeatureId"))).OrderBy(x=>x).SequenceEqual(new[]{53,59}),"XL02_FEATURE_IDS_CHANGED");
                    LiveRequire(metadata.All(m=>Convert.ToString(Value(m,"MinimumStatus"))=="MeetsMinimum"),"XL02_CHECK_MIN_CHANGED");
                }
                facts.Add(new {Part=Value(part,"PartName"),Quantity=Value(part,"Quantity"),FeatureIds=metadata.Select(m=>Value(m,"FeatureId")).ToArray(),
                    CheckMin=metadata.Select(m=>Value(m,"MinimumStatus")).ToArray(),Reason=Value(part,"Reason")});
            }
            LiveRequire(bends>0&&candidates==18*bends,"NO_NATIVE_BEND_RESULTS");
            liveResults.Add(new {Case=liveCase==0?"XL-02":"ASM-SMALL",Run=run,Selected=liveSelected,Parts=parts.Length,Bends=bends,Candidates=candidates,
                ToolCount=tools.Length,DataCount=data,MatchedPairs=matched,NumericQueries=Value(snapshot,"KernelQueries"),ElapsedMs=clock.Elapsed.TotalMilliseconds,Message=message,Facts=facts});
            state["LiveResults"]=liveResults;Write("LIVE_DEBUG_RUNNING");
        }
        private void LiveDebugTick()
        {
            if(clock.ElapsedMilliseconds>60000)throw new TimeoutException("Bounded live diagnostic exceeded 60s for this document");
            if(liveStep==0)
            {
                clock.Restart();int errors=0,warnings=0;
                string path=liveCase==0?fixture:System.Environment.GetEnvironmentVariable("ADDIN_LIVE_SMALL_ASSEMBLY");
                LiveRequire(Path.IsPathRooted(path)&&File.Exists(path),"EXPLICIT_EXISTING_FIXTURE_REQUIRED");
                liveModel=app.OpenDoc6(path,liveCase==0?1:2,3,"",ref errors,ref warnings) as ModelDoc2;
                LiveRequire(liveModel!=null,"OPEN_EXISTING_READONLY_DOCUMENT_FAILED:"+errors);
                app.ActivateDoc3(liveModel.GetTitle(),false,0,ref errors);liveModel.ClearSelection2(true);
                if(liveCase==1)
                {
                    // Explicit fixture preparation, only on the private COPY.
                    // Never save/rebuild a company original to make a test pass.
                    string copyRoot=System.Environment.GetEnvironmentVariable("ADDIN_LIVE_ASSEMBLY_COPY_ROOT");
                    LiveRequire(!string.IsNullOrWhiteSpace(copyRoot)&&Path.GetFullPath(path).StartsWith(Path.GetFullPath(copyRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"REBUILD_REQUIRES_PRIVATE_FIXTURE_COPY");
                    var edit=((AssemblyDoc)liveModel).GetEditTarget() as ModelDoc2;
                    state["AssemblyPreflightBeforePreparation"]=new {NeedsRebuild=liveModel.Extension.NeedsRebuild2,
                        Sketch=liveModel.GetActiveSketch2()!=null,TargetNeedsRebuild=edit?.Extension.NeedsRebuild2,
                        EditTargetIsRoot=edit==null?true:app.IsSame(edit,liveModel)==(int)SolidWorks.Interop.swconst.swObjectEquality.swObjectSame};
                    if(liveModel.Extension.NeedsRebuild2!=0)liveModel.ForceRebuild3(false);
                    state["AssemblyPreflightAfterPreparation"]=new {NeedsRebuild=liveModel.Extension.NeedsRebuild2,Sketch=liveModel.GetActiveSketch2()!=null};
                    Write("LIVE_DEBUG_RUNNING");
                }
                LiveOpenForm();LiveClick("btnRunBatch");liveStep=1;Write("LIVE_DEBUG_RUNNING");
            }
            else if(Equals(Value(liveController,"Running"),true)){timer.Start();return;}
            else if(liveStep==1)
            {
                LiveValidate("Cold");
                if(liveCase==0){LiveClick("btnRefresh");liveStep=2;}
                else
                {
                    window.Close();LiveRequire(window.IsDisposed,"IDLE_CLOSE_FAILED");
                    var members=((AssemblyDoc)liveModel).GetComponents(false) as object[]??new object[0];
                    var selected=members.OfType<Component2>().FirstOrDefault(c=>!c.IsSuppressed()&&c.GetModelDoc2() is ModelDoc2&&((ModelDoc2)c.GetModelDoc2()).GetType()==1);
                    LiveRequire(selected!=null&&selected.Select4(false,null,false),"SELECT_EXISTING_COMPONENT_FAILED");
                    liveSelected=true;LiveOpenForm();LiveClick("btnRunBatch");liveStep=4;
                }
            }
            else if(liveStep==2)
            {
                LiveValidate("Refresh");window.Close();LiveRequire(window.IsDisposed,"PART_IDLE_CLOSE_FAILED");
                LiveOpenForm();LiveClick("btnRunBatch");LiveRequire(Equals(Value(liveController,"Running"),true),"CLOSE_PROBE_NOT_RUNNING");
                window.Close();liveStep=3;
            }
            else if(liveStep==3)
            {
                if(!window.IsDisposed){timer.Start();return;}
                LiveRequire(Equals(Value(liveController,"backendDisposed"),true),"PART_CANCEL_CLOSE_BACKEND_NOT_DISPOSED");
                state["CancelClose"]="PASS";app.CloseDoc(liveModel.GetTitle());liveModel=null;liveCase=1;liveStep=0;
            }
            else if(liveStep==4)
            {
                LiveRequire(Convert.ToString(Value(window,"Scope"))=="SelectedComponents","SELECTED_SCOPE_NOT_USED");
                LiveValidate("Selected");window.Close();LiveRequire(window.IsDisposed,"ASSEMBLY_CLOSE_FAILED");
                app.CloseDoc(liveModel.GetTitle());state["FunctionalResult"]="PASS";state["LiveResults"]=liveResults;timer.Stop();Write("COMMAND_COMPLETED");return;
            }
            timer.Start();
        }
    }
}
