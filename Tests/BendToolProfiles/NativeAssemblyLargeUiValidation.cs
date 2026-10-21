using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

internal static partial class NativeAssemblyBatchValidation
{
    private static void ExerciseLargeNativeUi(ISldWorks app,ModelDoc2 root,Dictionary<string,object> report,string output)
    {
        Activate(app,root);root.ClearSelection2(true);
        using(var owner=(Form)Activator.CreateInstance(product.GetType("ADDIN.UI.BendCheckForm",true),All,null,new object[]{app,root,null},null))
        {
            owner.Show();LegacyBatchButton(owner).PerformClick();
            var form=(Form)Get(owner,"batchForm");var controller=Get(owner,"batchController");
            // Reproduce a late Shown/timer restart after the launcher stopped
            // the legacy timer. Both existing scan entries must refuse COM.
            var legacyTimer=(System.Windows.Forms.Timer)Get(owner,"timer");legacyTimer.Start();
            Call(owner,"RequestScan");Call(owner,"Tick");
            Check(!legacyTimer.Enabled&&!(bool)Get(owner,"capturing"),"Late legacy timer cannot take native capture ownership from Batch");
            report["LegacyLateShownOwnershipGuard"]="PASS";
            foreach(string stage in new[]{"cold","warm"})
            {
                UiClick(form,"btnRunBatch");UiSpin(()=>!(bool)Get(controller,"Running"));
                var result=Get(controller,"displayed");
                Check(result!=null&&(bool)Get(result,"Current")&&(int)Get(result,"LeafOccurrences")==120&&
                    (int)Get(result,"UniqueSourceConfigurations")==25,"Actual 120/25 Live Form completes: "+UiControl(form,"lblProgressTitle").Text);
                UiCheckBindings(form,result);report["Ui120-"+stage]=UiTimings(controller);
                if(stage=="warm")Check((int)Get(result,"Captures")==0&&(int)Get(result,"CaptureHits")==25,"Live UI warm hits actual 25 native captures");
                using(var bitmap=new Bitmap(form.Width,form.Height))
                {form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(output,"PRIVATE-live-120-"+stage+".png"));}
                Console.WriteLine("LIVE_UI_120 "+stage+" "+Json.Serialize(report["Ui120-"+stage]));
            }
            form.Close();
            Check(legacyTimer.Enabled,"Closing Batch resumes the original Min freshness timer");
            Call(owner,"RequestScan");
            Check(legacyTimer.Enabled&&(bool)Get(owner,"pendingStart"),"Original Min scan entry resumes after Batch closes");
            report["LegacyMinResumesAfterBatch"]="PASS";
            owner.Close();
        }
    }
}
