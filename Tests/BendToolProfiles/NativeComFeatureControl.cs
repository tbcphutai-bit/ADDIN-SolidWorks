// Diagnostic controls only. The original IndependentBendIds and its assertions
// execute first, unchanged. No production lifetime workaround is installed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CheckChan.NativeAcceptance
{
    public sealed partial class AcceptanceAddin
    {
        private string comControl;
        private bool ProductAssemblyLoaded => AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="ADDIN");
        private void PrepareComControl()
        {
            comControl=RepresentativeAcceptanceInput.ComControlMode(System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_COM_CONTROL"));
            if(comControl==null)return;
            if(representativeCases==null||CloseProbeOnly||!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ADDIN_ACCEPTANCE_PASSED_XL02_AUDIT")))
                throw new InvalidDataException("COM control requires its own explicit scope, not an acceptance resume");
            representativeIndex=comControl=="A"?2:1;
            state["ComControl"]=comControl;state["MaxOwnedControls"]=2;
            state["Scope"]=comControl=="A"?"XL19_WITHOUT_PRODUCT_DIAGNOSTIC_BRIDGE_ONLY":"XL17_EXISTING_UI_CLOSE_THEN_XL19";
        }
        private void CompleteComControl(RepresentativeCase item)
        {
            // Do not prime the typed interface before the original GetFeature
            // cast: that would change the RCW cache in the negative control.
            state["ComControlPhase"]="UNCHANGED_GETFEATURE_AUDIT_PASSED";state["Assertions"]=representativeAssertions;Write("COM_CONTROL_RUNNING");
            var ids=new HashSet<int>();int cylinders=0,roundtrips=0;
            foreach(Body2 body in ((PartDoc)representativeModel).GetBodies2(0,false) as object[]??new object[0])
            foreach(Face2 face in body.GetFaces() as object[]??new object[0])
            {
                if(!((Surface)face.GetSurface()).IsCylinder())continue;
                cylinders++;
                state["ComControlPhase"]="BEFORE_IGETFEATURE";state["CylinderOrdinal"]=cylinders;Write("COM_CONTROL_RUNNING");
                Feature typed=face.IGetFeature();Assert(typed!=null,"IGETFEATURE_OWNER_REQUIRED");
                var original=(Feature)face.GetFeature();
                Assert(app.IsSame(typed,original)==(int)swObjectEquality.swObjectSame,"GETFEATURE_IGETFEATURE_NATIVE_IDENTITY_CHANGED");
                Assert(typed.GetID()==original.GetID(),"GETFEATURE_IGETFEATURE_FEATURE_ID_CHANGED");
                var bytes=(byte[])representativeModel.Extension.GetPersistReference3(typed);int error;
                object resolved=representativeModel.Extension.GetObjectByPersistReference3(bytes,out error);
                Assert(bytes.Length>0&&resolved!=null&&error==0&&app.IsSame(resolved,typed)==(int)swObjectEquality.swObjectSame,"TYPED_FEATURE_PERSISTENT_ROUNDTRIP_REQUIRED");roundtrips++;
                var definition=typed.GetDefinition();
                if(definition is IOneBendFeatureData||definition is ISketchedBendFeatureData||definition is IEdgeFlangeFeatureData)ids.Add(typed.GetID());
            }
            Assert(ids.OrderBy(id=>id).SequenceEqual(item.FeatureIds),"TYPED_FACE_OWNER_NATIVE_FEATURE_IDS_CHANGED");
            Assert(cylinders>0&&roundtrips==cylinders,"ALL_CYLINDERS_MUST_BE_COMPARED");
            foreach(var source in representativeStates)Assert(ModelState(source.Key)==source.Value,"ORIGINAL_DOCUMENT_STATE_CHANGED");
            representativeCases.CheckSources();
            if(comControl=="A")Assert(!ProductAssemblyLoaded,"CONTROL_A_PRODUCT_MUST_REMAIN_ABSENT");
            state["TypedApiComparison"]=new{Result="PASS",Cylinders=cylinders,PersistentRoundtrips=roundtrips,FeatureIds=ids.OrderBy(id=>id).ToArray(),Order="original GetFeature audit first; typed IGetFeature comparison second"};
            representativeResults[representativeIndex]["Execution"]="PASS";
            representativeResults[representativeIndex]["TotalMilliseconds"]=clock.Elapsed.TotalMilliseconds;
            app.CloseDoc(representativeModel.GetTitle());representativeModel=null;
            state["Assertions"]=representativeAssertions;state["ProductAssemblyLoaded"]=ProductAssemblyLoaded;
            state["FunctionalResult"]="PASS";state["ComControlPhase"]="COMPARISON_COMPLETED";Write("COMMAND_COMPLETED");
        }
    }
}
