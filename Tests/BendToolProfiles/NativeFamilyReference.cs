using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment=System.Environment;

internal static partial class NativePlanningValidation
{
    private sealed class NativeProbe
    {
        internal object Envelope,Frame,Material,Enclosing;
        internal double X,Y,Z;
        internal double[] World;
        internal string SliceLocation,EnvelopeLocation;
        internal int Feature;
    }
    private static object Static(string type,string method,params object[] args)=>product.GetType("ADDIN."+type,true).GetMethods(All)
        .Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);
    private static Array Typed(string type,params object[] values)
    {var a=Array.CreateInstance(product.GetType("ADDIN."+type,true),values.Length);for(int i=0;i<values.Length;i++)a.SetValue(values[i],i);return a;}
    private static double[] Vector(object p)=>new[]{Convert.ToDouble(Get(p,"X")),Convert.ToDouble(Get(p,"Y")),Convert.ToDouble(Get(p,"Z"))};
    private static object Point2(double x,double y)=>New("Commands.BendTools.ToolPoint",x,y);
    private static double[] World(object frame,double x,double y,double z)=>Vector(Call(frame,"World",Point2(x,y),z));
    private static string Locate(object material,double x,double y)
    {
        var pair=Enum.Parse(product.GetType("ADDIN.Commands.BendCollision.CollisionPairKind"),"WorkpiecePunch");
        var q=New("Commands.BendCollision.CollisionQuery2D",pair,material,material,"NATIVE_QA",null,null,null);
        var work=New("Commands.BendCollision.CollisionWork",q,CancellationToken.None);
        return Get(Static("Commands.BendCollision.CollisionPointLocation","Locate",material,Point2(x,y),work,1e-5),"Location").ToString();
    }
    private static object NativeProbeCollision(object envelope,double x,double y,double z0,double z1,double size)
    {
        var binding=Get(envelope,"Binding");string frame=Get(binding,"FrameId").ToString();
        var component=Static("Commands.BendEnvelope.CrossAxisBoreBox","Rectangle","CONTROLLED_QA_PROBE",x,x+size,y,y+size,0.0);
        var material=New("Commands.BendCollision.CollisionMaterial2D",frame,Typed("Commands.BendCollision.CollisionComponent2D",component),
            "CONTROLLED_QA_PROBE_NOT_COMPANY_TOOL",true,null,false);
        var interval=New("Commands.BendEnvelope.AxialInterval",z0,z1);
        var tool=Static("Commands.BendEnvelope.AxialToolOccupancy","Controlled",material,Typed("Commands.BendEnvelope.AxialInterval",interval),0.0);
        var pair=Enum.Parse(product.GetType("ADDIN.Commands.BendCollision.CollisionPairKind"),"WorkpiecePunch");
        var query=New("Commands.BendEnvelope.AxialCollisionQuery",envelope,tool,pair,null,null,null);
        return Static("Commands.BendEnvelope.AxialCollisionEngine","Evaluate",query,CancellationToken.None);
    }
    private static object NativeFamilyReference(ModelDoc2 model,object capture,object[] envelopes)
    {
        Check(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA,"Independent native reference stays on STA");
        var nativeBodies=((PartDoc)model).GetBodies2(0,false) as object[]??new object[0];Check(nativeBodies.Length==1,"Independent reference uses the exact single owner body");
        var body=(Body2)nativeBodies[0];int stamp=model.GetUpdateStamp();var probes=new List<NativeProbe>();
        foreach(var envelope in envelopes)
        {
            if(!(bool)Get(Get(envelope,"Certificate"),"ContinuousCoverageProven"))continue;
            var binding=Get(envelope,"Binding");var frame=Get(binding,"WorkFrame");int feature=(int)Get(binding,"FeatureId");
            foreach(var interval in Items(Get(Get(envelope,"Certificate"),"Intervals")))
            {
                double lo=(double)Get(interval,"StartMm"),hi=(double)Get(interval,"EndMm");
                var range=New("Commands.BendEnvelope.AxialInterval",lo+(hi-lo)*.1,hi-(hi-lo)*.1);
                var enclosing=Call(envelope,"Enclose",range);if(enclosing==null)continue;
                foreach(double fraction in new[]{.19,.53,.81})
                {
                    double z=lo+(hi-lo)*fraction;var material=Get(Call(envelope,"Slice",z),"Material");if(material==null)continue;
                    foreach(var edge in Items(Get(material,"Contours")).SelectMany(c=>Items(Get(c,"Boundary"))).Take(48))
                    {
                        var a=Static("Commands.BendCollision.CollisionGeometry","Point",edge,.37);
                        var b=Static("Commands.BendCollision.CollisionGeometry","Point",edge,.371);
                        double ax=(double)Get(a,"X"),ay=(double)Get(a,"Y"),dx=(double)Get(b,"X")-ax,dy=(double)Get(b,"Y")-ay;
                        double length=Math.Sqrt(dx*dx+dy*dy);if(length<1e-12)continue;
                        foreach(int side in new[]{-1,1})
                        {
                            double x=ax-side*dy/length*.05,y=ay+side*dx/length*.05;
                            probes.Add(new NativeProbe {Envelope=envelope,Frame=frame,Feature=feature,X=x,Y=y,Z=z,World=World(frame,x,y,z),
                                Material=material,Enclosing=enclosing,SliceLocation=Locate(material,x,y),EnvelopeLocation=Locate(enclosing,x,y)});
                        }
                    }
                }
            }
        }
        Check(probes.Count>0,"Native reference has source-derived interior and void candidates");Console.WriteLine("Native B-rep reference points: "+probes.Count);
        var witnessPoints=new List<double[]>();var perFeature=new List<object>();
        foreach(var group in probes.GroupBy(p=>p.Feature))
        {
            int collisions=0;
            foreach(var p in group.Where(p=>p.SliceLocation=="Inside").Take(2))
            {
                var result=NativeProbeCollision(p.Envelope,p.X-.001,p.Y-.001,p.Z-.002,p.Z+.002,.002);
                Check(Get(result,"Status").ToString()=="COLLISION","Controlled finite QA probe intersects native slice material");
                var witness=Get(result,"Witness");var point=Get(witness,"PointMm");witnessPoints.Add(World(p.Frame,(double)Get(point,"X"),(double)Get(point,"Y"),(double)Get(witness,"AxialMm")));collisions++;
            }
            var sample=group.First();var axis=Vector(Get(sample.Frame,"X"));double ex,ey,ez;
            Check(body.GetExtremePoint(axis[0],axis[1],axis[2],out ex,out ey,out ez),"Independent native body extreme exists");
            var projected=Call(sample.Frame,"Project",New("Commands.BendPlacement.PlacementVector3",ex*1000,ey*1000,ez*1000));
            var domain=Get(sample.Envelope,"Domain");var clear=NativeProbeCollision(sample.Envelope,(double)Get(projected,"X")+1000,sample.Y,
                (double)Get(domain,"StartMm")-1,(double)Get(domain,"EndMm")+1,1);
            Check(Get(clear,"Status").ToString()=="CLEAR","Whole-source analytic enclosure separates a probe beyond the independently verified native extreme");
            Check(collisions==2,"Each native bend supplies collision witnesses for independent verification");
            perFeature.Add(new {Feature=group.Key,ControlledProbeCollisions=collisions,ControlledProbeClear=1});
        }
        var bounds=new double[6];
        for(int k=0;k<3;k++)foreach(int sign in new[]{-1,1})
        {
            var direction=new double[3];direction[k]=sign;double x,y,z;
            Check(body.GetExtremePoint(direction[0],direction[1],direction[2],out x,out y,out z),"Independent original B-rep bound exists");
            bounds[k+(sign>0?3:0)]=new[]{x,y,z}[k]*1000;
        }
        string path=model.GetPathName();int errors=0,warnings=0;
        Check(model.Extension.SaveAs(Path.Combine(output,"PRIVATE-native-source.step"),0,(int)(swSaveAsOptions_e.swSaveAsOptions_Silent|
            swSaveAsOptions_e.swSaveAsOptions_Copy|swSaveAsOptions_e.swSaveAsOptions_AvoidRebuildOnSave),null,ref errors,ref warnings)&&errors==0,"Export a private STEP copy for a genuinely independent B-rep kernel");
        Check(model.GetPathName()==path&&model.GetUpdateStamp()==stamp,"Independent native reference does not change the Part or its stamp");
        File.WriteAllText(Path.Combine(output,"PRIVATE-native-kernel-reference.json"),Json.Serialize(new {Candidates=probes.Select(p=>new {p.Feature,p.X,p.Y,p.Z,p.World,p.SliceLocation,p.EnvelopeLocation}),
            Witnesses=witnessPoints,NativeBoundsMm=bounds,PerFeature=perFeature}));
        string python=Environment.GetEnvironmentVariable("ADDIN_NATIVE_REFERENCE_PYTHON"),script=Environment.GetEnvironmentVariable("ADDIN_NATIVE_REFERENCE_SCRIPT");
        Check(File.Exists(python)&&File.Exists(script),"Independent native reference Python and script are explicitly configured");
        var start=new ProcessStartInfo(python,"\""+script+"\" \""+output+"\"") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            RedirectStandardOutput=true,RedirectStandardError=true};
        using(var process=Process.Start(start))
        {
            var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            var watch=Stopwatch.StartNew();while(!process.HasExited&&watch.ElapsedMilliseconds<60000)Pump(20);
            if(!process.HasExited){process.Kill();throw new Exception("Independent reference timed out");}
            string result=stdout.GetAwaiter().GetResult(),error=stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output,"PRIVATE-independent-reference-output.txt"),result+"\n"+error);
            Check(process.ExitCode==0,"Independent OpenCascade comparison passes; see private output for any failed geometry assertion");
        }
        var summary=Json.DeserializeObject(File.ReadAllText(Path.Combine(output,"independent-native-step-qa.json")));
        Check((bool)((Dictionary<string,object>)summary)["Passed"],"Independent B-rep report confirms material void interval coverage and witnesses");
        return summary;
    }
}
