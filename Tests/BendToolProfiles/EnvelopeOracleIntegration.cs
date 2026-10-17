using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;

internal static partial class ToolProfileTests
{
    private static int EnvelopeOracleIntegration(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            temporary=Path.Combine(output,"CONTROLLED-SYNTHETIC-ONLY");Directory.CreateDirectory(temporary);
            var cases=new List<object>();var random=new Random(140093);var counts=new Dictionary<string,int>();
            Action<string,SheetEnvelopeCapture,object,double,double,double,double,double,double> add=(name,sheet,reference,x0,x1,y0,y1,z0,z1)=> {
                var result=VolumeQuery(sheet,Material(Rect("ORACLE_TOOL",x0,y0,x1-x0,y1-y0)),z0,z1);
                string status=result.Status.ToString();if(!counts.ContainsKey(status))counts[status]=0;counts[status]++;
                cases.Add(new {Name=name,Reference=reference,Tool=new {X0=x0,X1=x1,Y0=y0,Y1=y1,Z0=z0,Z1=z1},Status=status,
                    result.CertifiedClearanceLowerMm,result.Witness,result.Reasons,Policy=result.PolicyVersion});
            };
            var xs=new[]{0.0,2,8,10};var ys=new[]{0.0,2,8,10};var zs=new[]{-5.0,-2,2,5};
            foreach(string kind in new[]{"POCKET","THROUGH_HOLE","NOTCH"})
            {
                Func<int,int,int,bool> occupied=(x,y,z)=>kind=="POCKET"?!(x==1&&y==1&&z==1):kind=="THROUGH_HOLE"?x!=1||y!=1:!(x>0&&y==1&&z==1);
                var model=PolycellModel(xs,ys,zs,occupied);var sheet=CompileNative(model);Check(sheet.Certificate.ContinuousCoverageProven,"Oracle source has exact native-style shell coverage");
                var boxes=(from x in Enumerable.Range(0,3) from y in Enumerable.Range(0,3) from z in Enumerable.Range(0,3) where occupied(x,y,z)
                    select new {X0=xs[x],X1=xs[x+1],Y0=ys[y],Y1=ys[y+1],Z0=zs[z],Z1=zs[z+1]}).ToArray();
                var reference=new {Kind="ExactUnionOfBoxes",Boxes=boxes};
                for(int i=0;i<100;i++)
                {
                    double x=random.NextDouble()*14-2,y=random.NextDouble()*14-2,z=random.NextDouble()*12-6,size=.05+random.NextDouble()*.7;
                    add(kind+"-"+i,sheet,reference,x,x+size,y,y+size,z,z+size);
                }
                add(kind+"-CONTROLLED-VOID",sheet,reference,4,5,4,5,-.5,.5);
                add(kind+"-CONTROLLED-SOLID",sheet,reference,.5,1,.5,1,-.5,.5);
            }
            var bore=Volume(new CrossAxisBoreBox(new AxialInterval(-4,4),-4,4,-4,4,0,0,2));
            var boreReference=new {Kind="ExactBoxMinusXCylinder",Box=new {X0=-4,X1=4,Y0=-4,Y1=4,Z0=-4,Z1=4},Radius=2,CenterY=0,CenterZ=0};
            for(int i=0;i<120;i++)
            {
                double y=random.NextDouble()*7-3.5,z=random.NextDouble()*7-3.5,size=.01+random.NextDouble()*.05;
                add("BORE-"+i,bore,boreReference,-.2,.2,y,y+size,z,z+size);
            }
            File.WriteAllText(Path.Combine(output,"SYNTHETIC-axial-oracle.json"),Json.Serialize(new {FixtureKind="CONTROLLED_SYNTHETIC_NO_COMPANY_DATA",Counts=counts,Cases=cases}));
            Console.WriteLine("AXIAL ORACLE CAPTURE: "+Json.Serialize(counts)+"; cases="+cases.Count);return 0;
        }
        catch(Exception ex){Console.WriteLine("AXIAL ORACLE FAIL: "+ex);return 1;}
    }
}
