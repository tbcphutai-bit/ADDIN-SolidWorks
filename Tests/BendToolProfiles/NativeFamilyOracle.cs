using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADDIN.Commands.BendCollision;

internal static partial class ToolProfileTests
{
    private static int NativeFamilyOracle(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);temporary=Path.Combine(output,"SYNTHETIC-ONLY");Directory.CreateDirectory(temporary);
            var cases=new List<object>();var counts=new Dictionary<string,int>();var random=new Random(160123);
            for(int kind=0;kind<2;kind++)
            {
                var model=kind==0?NativeBoreModel():NativeQuarterBendModel();var sheet=CompileNative(model);
                Check(sheet.Certificate.ContinuousCoverageProven,"Native-style analytic B-rep family must be proved before oracle queries");
                for(int i=0;i<240;i++)
                {
                    double x=.5+random.NextDouble()*8,y=kind==0?random.NextDouble()*7-3.5:random.NextDouble()*3.7+.01;
                    double z=kind==0?random.NextDouble()*7-3.5:random.NextDouble()*3.7+.01,size=.005+random.NextDouble()*.05;
                    var result=VolumeQuery(sheet,Material(Rect("QA",x,y,size,size)),z,z+size);string status=result.Status.ToString();
                    if(!counts.ContainsKey(status))counts[status]=0;counts[status]++;
                    cases.Add(new {Name=(kind==0?"NATIVE-BORE-":"NATIVE-BEND-")+i,SourceKind=kind==0?"BoxMinusXCylinder":"QuarterAnnulusX",
                        Tool=new {X0=x,X1=x+size,Y0=y,Y1=y+size,Z0=z,Z1=z+size},Status=status,result.CertifiedClearanceLowerMm,result.Witness,result.Reasons});
                }
            }
            File.WriteAllText(Path.Combine(output,"SYNTHETIC-native-family-oracle.json"),Json.Serialize(new {FixtureKind="EXACT_SYNTHETIC_NATIVE_STYLE_BREP_NOT_COMPANY_DATA",Counts=counts,Cases=cases}));
            Console.WriteLine("NATIVE FAMILY ORACLE: "+Json.Serialize(counts)+"; cases="+cases.Count);return 0;
        }
        catch(Exception ex){Console.WriteLine("NATIVE FAMILY ORACLE FAIL: "+ex);return 1;}
    }
}
