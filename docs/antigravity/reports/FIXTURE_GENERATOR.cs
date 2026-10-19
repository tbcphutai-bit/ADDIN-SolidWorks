// CG1 Review #6 & #7: Standalone fixture generator and metadata reproducibility script.
// C# 7.3 / .NET Framework 4.8 / SolidWorks API.
// Generates self-contained, isolated test assemblies for Check Hole CG1:
// 1. Fixture_OK.SLDASM: 2-part clean joint (57 holes, 24 grouped, 33 unmatched, 12 stacks, 0 NG).
// 2. Fixture_NG_Offset.SLDASM: 2-part offset joint with MateGroup suppressed and +0.30 mm translation in X
//    (57 holes, 24 grouped, 33 unmatched, 12 stacks, 12 NG).
// 3. Fixture_Unmatched.SLDASM: 1-part model with fr1 suppressed (32 holes, 0 stacks, 32 unmatched).
// 4. Fixture_MultiLayer_ABC.SLDASM: 3-part joint (82 holes, 48 grouped, 34 unmatched, 24 stacks).
// 5. Fixture_OldMarker.SLDASM: Disposable model with CHECK-HOLE-NG-STALE-MARKER feature for negative tests.

using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Tests
{
    public static class FixtureGenerator
    {
        public static void GenerateAll(ISldWorks app, string fixtureDir)
        {
            string baseAssy = Path.Combine(fixtureDir, "1618-02-bh-A10.SLDASM");
            int errors = 0, warnings = 0;

            // 1. Fixture_OK: Base assembly with only component "1" and "fr1" active
            Console.WriteLine("Generating Fixture_OK.SLDASM...");
            ModelDoc2 doc = app.OpenDoc6(baseAssy, (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            AssemblyDoc assy = (AssemblyDoc)doc;

            foreach (object o in (object[])assy.GetComponents(false))
            {
                Component2 c = (Component2)o;
                string n = c.Name2.ToLowerInvariant();
                if (!n.Contains("-1-") && !n.Contains("-fr1-"))
                {
                    c.SetSuppression2((int)swComponentSuppressionState_e.swComponentSuppressed);
                }
            }
            doc.EditRebuild3();
            string okPath = Path.Combine(fixtureDir, "Fixture_OK.SLDASM");
            doc.Extension.SaveAs(okPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            app.CloseDoc(doc.GetTitle());
            Console.WriteLine("Saved: " + okPath);

            // 2. Fixture_NG_Offset: Suppress all mates in MateGroup, then apply +0.30mm translation in X
            Console.WriteLine("Generating Fixture_NG_Offset.SLDASM...");
            doc = app.OpenDoc6(okPath, (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            assy = (AssemblyDoc)doc;

            Feature feat = (Feature)doc.FirstFeature();
            while (feat != null)
            {
                string typeName = feat.GetTypeName2();
                if (typeName == "MateGroup")
                {
                    feat.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                        (int)swInConfigurationOpts_e.swAllConfiguration, null);
                    Feature sub = (Feature)feat.GetFirstSubFeature();
                    while (sub != null)
                    {
                        sub.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                            (int)swInConfigurationOpts_e.swAllConfiguration, null);
                        sub = (Feature)sub.GetNextSubFeature();
                    }
                }
                feat = (Feature)feat.GetNextFeature();
            }

            foreach (object o in (object[])assy.GetComponents(false))
            {
                Component2 c = (Component2)o;
                string n = c.Name2.ToLowerInvariant();
                if (n.Contains("fr1"))
                {
                    MathTransform t = c.Transform2;
                    double[] data = (double[])t.ArrayData;
                    data[9] += 0.00030; // +0.30 mm in X (transverse to Y-axis holes)
                    MathUtility mathUtil = (MathUtility)app.GetMathUtility();
                    c.Transform2 = (MathTransform)mathUtil.CreateTransform(data);
                    Console.WriteLine("Component " + c.Name2 + " offset by +0.30mm in X (new X=" + (data[9] * 1000).ToString("F3") + "mm)");
                }
            }
            doc.EditRebuild3();
            string ngPath = Path.Combine(fixtureDir, "Fixture_NG_Offset.SLDASM");
            doc.Extension.SaveAs(ngPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            app.CloseDoc(doc.GetTitle());
            Console.WriteLine("Saved: " + ngPath);

            // 3. Fixture_Unmatched: Component "fr1" suppressed as well -> only component "1" remains
            Console.WriteLine("Generating Fixture_Unmatched.SLDASM...");
            doc = app.OpenDoc6(okPath, (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            assy = (AssemblyDoc)doc;
            foreach (object o in (object[])assy.GetComponents(false))
            {
                Component2 c = (Component2)o;
                if (c.Name2.ToLowerInvariant().Contains("fr1"))
                {
                    c.SetSuppression2((int)swComponentSuppressionState_e.swComponentSuppressed);
                }
            }
            doc.EditRebuild3();
            string unPath = Path.Combine(fixtureDir, "Fixture_Unmatched.SLDASM");
            doc.Extension.SaveAs(unPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            app.CloseDoc(doc.GetTitle());
            Console.WriteLine("Saved: " + unPath);

            // 4. Fixture_MultiLayer_ABC: Keep components "1", "fr1", "fr3" unsuppressed
            Console.WriteLine("Generating Fixture_MultiLayer_ABC.SLDASM...");
            doc = app.OpenDoc6(baseAssy, (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            assy = (AssemblyDoc)doc;
            foreach (object o in (object[])assy.GetComponents(false))
            {
                Component2 c = (Component2)o;
                string n = c.Name2.ToLowerInvariant();
                if (!n.Contains("-1-") && !n.Contains("-fr1-") && !n.Contains("-fr3-"))
                {
                    c.SetSuppression2((int)swComponentSuppressionState_e.swComponentSuppressed);
                }
            }
            doc.EditRebuild3();
            string multiPath = Path.Combine(fixtureDir, "Fixture_MultiLayer_ABC.SLDASM");
            doc.Extension.SaveAs(multiPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            app.CloseDoc(doc.GetTitle());
            Console.WriteLine("Saved: " + multiPath);

            // 5. Fixture_OldMarker: Add stale CHECK-HOLE-NG feature for negative test
            Console.WriteLine("Generating Fixture_OldMarker.SLDASM...");
            doc = app.OpenDoc6(okPath, (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            doc.SketchManager.Insert3DSketch(true);
            doc.SketchManager.CreatePoint(0, 0, 0);
            doc.SketchManager.Insert3DSketch(true);
            Feature markerFeat = (Feature)doc.Extension.GetLastFeatureAdded();
            if (markerFeat != null) markerFeat.Name = "CHECK-HOLE-NG-STALE-MARKER";
            string markerPath = Path.Combine(fixtureDir, "Fixture_OldMarker.SLDASM");
            doc.Extension.SaveAs(markerPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            app.CloseDoc(doc.GetTitle());
            Console.WriteLine("Saved: " + markerPath);

            Console.WriteLine("All 5 standalone test fixtures generated successfully.");
        }
    }
}
