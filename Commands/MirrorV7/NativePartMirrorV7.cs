using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class NativePartMirrorResultV7
    {
        public string OutputPath { get; internal set; }
        public string WorkingPath { get; internal set; }
        public bool GeometryVerified { get; internal set; }
        public bool ReopenVerified { get; internal set; }
        public bool SourceUnchanged { get; internal set; }
        public bool IndependentVerified { get; internal set; }
        public bool FeatureTreeVerified { get; internal set; }
        public bool FeatureMappingVerified { get; internal set; }
        public bool FlatPatternApplicable { get; internal set; }
        public bool FlatPatternVerified { get; internal set; }
        // Type coverage is NOT proof that constraints and feature semantics were preserved.
        public string FeatureTransferReport { get; internal set; }
        public string MappingPath { get; internal set; }
    }

    /// <summary>
    /// Native opposite-hand Part creation, isolated from the experimental sketch executor.
    /// Never modifies the source; never silently falls back to a dumb imported body.
    /// Only the active configuration is certified. Assembly insertion is intentionally absent.
    /// </summary>
    public static class NativePartMirrorV7
    {
        public const string Version = "V7-NATIVE-PART-3";

        public static NativePartMirrorResultV7 Execute(ISldWorks app, ModelDoc2 source,
            CanonicalPartMirrorPlaneV7 plane, string outputPath)
        {
            if (app == null) throw new ArgumentNullException("app");
            var baseline = MirrorSourceBaselineV7.Capture(source);
            outputPath = Path.GetFullPath(outputPath);
            if (!string.Equals(Path.GetExtension(outputPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outputPath, baseline.SourcePath, StringComparison.OrdinalIgnoreCase) || File.Exists(outputPath))
                throw new InvalidOperationException("Chon ten .SLDPRT moi; khong ghi de Part goc hoac file da ton tai.");
            if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
                throw new DirectoryNotFoundException(Path.GetDirectoryName(outputPath));

            var result = new NativePartMirrorResultV7 { OutputPath = outputPath };
            ModelDoc2 working = null, mirrored = null;
            string stage = "PREFLIGHT";
            Log("START version=" + Version + " source=" + baseline.SourcePath + " plane=" + plane +
                " configuration=" + baseline.ConfigurationName + " configurationScope=ACTIVE_ONLY");
            try
            {
                var sourceGraph = FeatureTreeScannerV7.Scan(source);
                CheckFeatureErrors(sourceGraph);
                var reflection = PartReflectionTransformV7.CreateCanonical(plane);
                var expected = ReflectSolids(app, source, reflection);
                stage = "WORKING_COPY";
                var copy = MirrorWorkingCopyV7.Create(app, baseline);
                working = copy.Document;
                result.WorkingPath = copy.WorkingPath;
                Activate(app, working);
                Feature mirrorPlane = FindPlane(app, working, reflection.Normal);
                working.ClearSelection2(true);
                if (!mirrorPlane.Select2(false, 0)) throw new InvalidOperationException("Cannot select mirror plane.");
                bool sheetMetal = sourceGraph.Nodes.Any(n => n.Role == MirrorV7FeatureRole.SheetMetal);
                int options = (int)(swMirrorPartOptions_e.swMirrorPartOptions_ImportSolids |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportSurfaces |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportAxes |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportPlanes |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportAbsorbedSketchs |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportUnabsorbedSketchs |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportCustomProperties |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportCoordinateSystem |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportModelDimensions |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportHoleWizardData |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportCosmeticThreads |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportCutListProperties |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportBodyMaterial |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportPartMaterial);
                if (sheetMetal) options |= (int)(swMirrorPartOptions_e.swMirrorPartOptions_ImportSMInfo |
                    swMirrorPartOptions_e.swMirrorPartOptions_ImportIndProps);
                stage = "MIRROR_PART2";
                Log("API MirrorPart2 breakLink=True options=" + options + " sheetMetal=" + sheetMetal);
                Feature created = ((PartDoc)working).MirrorPart2(true, options, out mirrored);
                // On tested SOLIDWORKS versions MirrorPart2 can validly return a null Feature
                // while ResultPart contains the complete opposite-hand Part. ResultPart is the
                // authoritative return value; the created document is verified below.
                Log("API_RESULT returnedFeature=" + (created != null) + " resultPart=" + (mirrored != null));
                if (mirrored == null || ReferenceEquals(mirrored, source) || ReferenceEquals(mirrored, working))
                    throw new InvalidOperationException("MirrorPart2 did not return a separate Part document.");
                Activate(app, mirrored);
                stage = "VERIFY_CREATED";
                Verify(mirrored, expected, sourceGraph, result);
                result.GeometryVerified = true;
                baseline.AssertUnchanged(source);
                // Save to a private staging path, reopen and verify before publishing anything.
                string candidate = Path.Combine(Path.GetDirectoryName(copy.WorkingPath), "candidate.SLDPRT");
                stage = "SAVE_STAGING";
                mirrored.ClearSelection2(true);
                int errors = 0, warnings = 0;
                if (!mirrored.Extension.SaveAs(candidate, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings) || errors != 0)
                    throw new InvalidOperationException("Save failed errors=" + errors + " warnings=" + warnings);
                Log("SAVE_STAGING errors=" + errors + " warnings=" + warnings + " path=" + candidate);
                app.CloseDoc(mirrored.GetTitle()); mirrored = null;
                // Close the disposable input too: the result must no longer depend on it.
                app.CloseDoc(working.GetTitle()); working = null;
                stage = "REOPEN";
                mirrored = app.OpenDoc6(candidate, (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
                if (mirrored == null || errors != 0) throw new InvalidOperationException("Reopen failed errors=" + errors);
                Activate(app, mirrored);
                Verify(mirrored, expected, sourceGraph, result);
                result.ReopenVerified = true;
                stage = "MAP_OUTPUT";
                string candidateMap = WriteAndVerifyMapping(source, mirrored, candidate);
                result.FeatureMappingVerified = true;
                result.FlatPatternApplicable = VerifyFlatPatternCanRebuild(mirrored);
                result.FlatPatternVerified = true;
                app.CloseDoc(mirrored.GetTitle()); mirrored = null;
                baseline.AssertUnchanged(source);
                result.SourceUnchanged = true;
                stage = "PUBLISH";
                File.Copy(candidate, outputPath, false);
                result.MappingPath = outputPath + ".MirrorMap.xml";
                try { File.Copy(candidateMap, result.MappingPath, false); }
                catch
                {
                    try { File.Delete(outputPath); } catch { }
                    throw;
                }
                Log("GEOMETRY_PASS output=" + outputPath + " reopen=True sourceUnchanged=True" +
                    " independent=True mapping=" + result.MappingPath + " " + result.FeatureTransferReport);
                return result;
            }
            catch (Exception ex)
            {
                Log("FAIL stage=" + stage + " working=" + result.WorkingPath + " error=" + ex);
                throw;
            }
            finally
            {
                // Only close documents created by this invocation; leave diagnostics on disk.
                if (mirrored != null) { try { app.CloseDoc(mirrored.GetTitle()); } catch (Exception ex) { Log("CLEANUP " + ex.Message); } }
                if (working != null) { try { app.CloseDoc(working.GetTitle()); } catch (Exception ex) { Log("CLEANUP " + ex.Message); } }
                baseline.AssertUnchanged(source);
                Activate(app, source);
            }
        }

        private static void Verify(ModelDoc2 model, List<Body2> expected, MirrorV7ModelGraph sourceGraph,
            NativePartMirrorResultV7 result)
        {
            if (!model.EditRebuild3()) throw new InvalidOperationException("Output rebuild failed.");
            var graph = FeatureTreeScannerV7.Scan(model);
            CheckFeatureErrors(graph);
            object dependencies = model.Extension.GetDependencies(true, false, false, false, false);
            var depArray = dependencies as Array;
            if (depArray != null && depArray.Length != 0)
                throw new InvalidOperationException("Output still has file dependencies; independent Part not verified.");
            result.IndependentVerified = true;
            var actual = Solids(model);
            if (!BipartiteEquivalenceMatcherV7.Match(expected, actual, SameSolid).HasPerfectMatching)
                throw new InvalidOperationException("Reflected body set does not match output (bidirectional Boolean check).");
            var missing = new List<string>();
            foreach (var group in sourceGraph.Nodes.Where(n => n.Role != MirrorV7FeatureRole.System &&
                n.Role != MirrorV7FeatureRole.Reference && !n.IsSuppressed).GroupBy(n => n.TypeName))
            {
                int count = graph.Nodes.Count(n => n.TypeName == group.Key && !n.IsSuppressed);
                Log("FEATURE_TRANSFER type=" + group.Key + " source=" + group.Count() + " output=" + count);
                if (count < group.Count()) missing.Add(group.Key + ":" + (group.Count() - count));
            }
            result.FeatureTransferReport = "missingFeatureTypes=" + (missing.Count == 0 ? "NONE" : string.Join(",", missing));
            result.FeatureTreeVerified = missing.Count == 0;
            Log("VERIFY geometry=True dependencies=None " + result.FeatureTransferReport +
                " featureTreeVerified=" + result.FeatureTreeVerified);
        }

        private static List<Body2> Solids(ModelDoc2 model)
        {
            var part = (PartDoc)model;
            var sheets = part.GetBodies2((int)swBodyType_e.swSheetBody, false) as object[];
            if (sheets != null && sheets.Length != 0)
                throw new InvalidOperationException("Surface bodies require a surface verifier; no geometry-only success claimed.");
            var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length == 0) throw new InvalidOperationException("Part has no solid bodies.");
            return bodies.Cast<Body2>().ToList();
        }

        private static List<Body2> ReflectSolids(ISldWorks app, ModelDoc2 model, PartReflectionTransformV7 reflection)
        {
            var math = (MathUtility)app.GetMathUtility();
            var data = new double[16];
            Array.Copy(reflection.GetLinearMatrix3x3(), data, 9); data[12] = 1;
            var transform = ReflectionApiTransformV7.Create(math, data);
            var result = new List<Body2>();
            foreach (var body in Solids(model))
            {
                var copy = body.Copy2(false) as Body2;
                if (copy == null || !copy.ApplyTransform(transform)) throw new InvalidOperationException("Temporary body reflection failed.");
                double[] before = Mass(body), after = Mass(copy);
                if (!VerticesMatchReflection(body, copy, reflection, 1e-8))
                    throw new InvalidOperationException("Temporary body vertices did not reflect about requested plane.");
                if (Math.Abs(before[3] - after[3]) > Math.Max(1e-15, Math.Abs(before[3]) * 1e-4))
                    throw new InvalidOperationException("Reflection changed volume beyond temporary-body tolerance.");
                result.Add(copy);
            }
            return result;
        }

        private static double[] Mass(Body2 body)
        {
            var mass = body.GetMassProperties(1) as double[];
            if (mass == null || mass.Length < 5 || mass.Any(x => double.IsNaN(x) || double.IsInfinity(x)) || mass[3] <= 0)
                throw new InvalidOperationException("Invalid solid mass properties.");
            return mass;
        }
        private static double VolumeTolerance(double volume) { return Math.Max(1e-15, Math.Abs(volume) * 1e-6); }
        private static bool SameSolid(Body2 expected, Body2 actual)
        {
            double[] left = Mass(expected), right = Mass(actual);
            double tolerance = Math.Max(VolumeTolerance(left[3]), Math.Abs(left[3]) * 1e-4);
            if (Math.Abs(left[3] - right[3]) > tolerance) return false;
            for (int i = 0; i < 3; i++) if (Math.Abs(left[i] - right[i]) > 1e-5) return false;
            if (!SameVertexSet(expected, actual, 1e-7)) return false;
            var a = BodyOperationsHelper.BooleanCutStrict(expected, actual, "V7_EXPECTED_MINUS_OUTPUT");
            var b = BodyOperationsHelper.BooleanCutStrict(actual, expected, "V7_OUTPUT_MINUS_EXPECTED");
            return a.Success && b.Success && a.Bodies.Sum(x => Mass(x)[3]) <= tolerance && b.Bodies.Sum(x => Mass(x)[3]) <= tolerance;
        }

        private static bool VerticesMatchReflection(Body2 source, Body2 reflected,
            PartReflectionTransformV7 reflection, double tolerance)
        {
            object[] sourceObjects = source.GetVertices() as object[];
            object[] reflectedObjects = reflected.GetVertices() as object[];
            var expected = sourceObjects == null ? new List<double[]>() : sourceObjects.Cast<Vertex>()
                .Select(v => reflection.ReflectPoint(v.GetPoint() as double[])).ToList();
            var actual = reflectedObjects == null ? new List<double[]>() : reflectedObjects.Cast<Vertex>()
                .Select(v => v.GetPoint() as double[]).ToList();
            return PointSetsMatch(expected, actual, tolerance);
        }
        private static bool SameVertexSet(Body2 left, Body2 right, double tolerance)
        {
            object[] leftObjects = left.GetVertices() as object[];
            object[] rightObjects = right.GetVertices() as object[];
            var a = leftObjects == null ? new List<double[]>() : leftObjects.Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
            var b = rightObjects == null ? new List<double[]>() : rightObjects.Cast<Vertex>().Select(v => v.GetPoint() as double[]).ToList();
            return PointSetsMatch(a, b, tolerance);
        }
        private static bool PointSetsMatch(List<double[]> left, List<double[]> right, double tolerance)
        {
            if (left.Count == 0 || left.Count != right.Count) return false;
            return BipartiteEquivalenceMatcherV7.Match(left, right, (a, b) =>
            {
                if (a == null || b == null || a.Length < 3 || b.Length < 3) return false;
                for (int i = 0; i < 3; i++) if (double.IsNaN(b[i]) || double.IsInfinity(b[i]) || Math.Abs(a[i] - b[i]) > tolerance) return false;
                return true;
            }).HasPerfectMatching;
        }

        private static Feature FindPlane(ISldWorks app, ModelDoc2 model, double[] normal)
        {
            var math = (MathUtility)app.GetMathUtility();
            for (Feature f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                if (f.GetTypeName2() != "RefPlane") continue;
                var plane = f.GetSpecificFeature2() as RefPlane;
                if (plane == null) continue;
                var origin = (MathPoint)math.CreatePoint(new double[3]);
                var direction = (MathVector)math.CreateVector(new double[] { 0, 0, 1 });
                var p = (double[])((MathPoint)origin.MultiplyTransform(plane.Transform)).ArrayData;
                var n = (double[])((MathVector)direction.MultiplyTransform(plane.Transform)).ArrayData;
                double length = Math.Sqrt(n.Sum(x => x * x));
                double dot = n[0] * normal[0] + n[1] * normal[1] + n[2] * normal[2];
                double distance = p[0] * normal[0] + p[1] * normal[1] + p[2] * normal[2];
                if (length > 0 && Math.Abs(Math.Abs(dot / length) - 1) < 1e-10 && Math.Abs(distance) < 1e-9) return f;
            }
            throw new InvalidOperationException("Cannot find requested origin plane by geometry.");
        }
        private static void CheckFeatureErrors(MirrorV7ModelGraph graph)
        {
            foreach (var node in graph.Nodes)
            {
                if (node.IsSuppressed || node.Role == MirrorV7FeatureRole.System) continue;
                bool warning;
                int code = node.Feature.GetErrorCode2(out warning);
                if (code != 0 && !warning) throw new InvalidOperationException("Feature error " + node.Name + " code=" + code);
                if (code != 0) Log("FEATURE_WARNING name=" + node.Name + " code=" + code);
            }
        }

        private static string WriteAndVerifyMapping(ModelDoc2 source, ModelDoc2 target, string outputPath)
        {
            var sourceGraph = FeatureTreeScannerV7.Scan(source);
            var targetGraph = FeatureTreeScannerV7.Scan(target);
            List<MirrorV7FeatureNode> sourceNodes = SemanticNodes(sourceGraph);
            List<MirrorV7FeatureNode> targetNodes = SemanticNodes(targetGraph);
            var targetGroups = targetNodes.GroupBy(Key)
                .ToDictionary(g => g.Key, g => g.OrderBy(n => n.TreeOrder).ToList(), StringComparer.Ordinal);
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            var root = new XElement("MirrorPartMap", new XAttribute("version", Version),
                new XAttribute("source", source.GetPathName()), new XAttribute("target", outputPath),
                new XAttribute("bidirectional", true),
                new XAttribute("scope", "AuthoredSemanticFeaturesExcludingSystemDescendants"));
            int mapped = 0;
            foreach (MirrorV7FeatureNode sourceNode in sourceNodes.OrderBy(n => n.TreeOrder))
            {
                string key = Key(sourceNode); int index;
                if (!used.TryGetValue(key, out index)) index = 0;
                List<MirrorV7FeatureNode> candidates;
                if (!targetGroups.TryGetValue(key, out candidates) || index >= candidates.Count)
                    throw new InvalidOperationException("Cannot map source feature: " + sourceNode.Name + " [" + sourceNode.TypeName + "]");
                MirrorV7FeatureNode targetNode = candidates[index]; used[key] = index + 1;
                var sourceRef = PersistentReferenceServiceV7.Capture(source, sourceNode.Feature, sourceNode.Name, "SourceFeature");
                var targetRef = PersistentReferenceServiceV7.Capture(target, targetNode.Feature, targetNode.Name, "TargetFeature");
                if (sourceRef == null || targetRef == null)
                    throw new InvalidOperationException("Persistent feature identity unavailable for mapping: " + sourceNode.Name);
                int sourceState, targetState;
                if (!(PersistentReferenceServiceV7.Resolve(source, sourceRef, out sourceState) is Feature) ||
                    !(PersistentReferenceServiceV7.Resolve(target, targetRef, out targetState) is Feature))
                    throw new InvalidOperationException("Mapping reference did not round-trip: " + sourceNode.Name);
                root.Add(new XElement("Feature", new XAttribute("sourceOrder", sourceNode.TreeOrder),
                    new XAttribute("targetOrder", targetNode.TreeOrder),
                    new XAttribute("sourceDepth", sourceNode.Depth),
                    new XAttribute("targetDepth", targetNode.Depth),
                    new XAttribute("mappingBasis", "TypeOccurrenceInTreeOrder"),
                    new XAttribute("sourceName", sourceNode.Name), new XAttribute("targetName", targetNode.Name),
                    new XAttribute("type", sourceNode.TypeName),
                    new XElement("SourcePersistRef", Convert.ToBase64String(sourceRef.Data)),
                    new XElement("TargetPersistRef", Convert.ToBase64String(targetRef.Data))));
                mapped++;
            }
            string mappingPath = outputPath + ".MirrorMap.xml";
            var document = new XDocument(root);
            document.Save(mappingPath);
            XDocument.Load(mappingPath);
            Log("FEATURE_MAP mapped=" + mapped + " path=" + mappingPath + " result=PASS");
            return mappingPath;
        }

        private static bool IsSemantic(MirrorV7FeatureNode node)
        {
            return node != null && node.Role != MirrorV7FeatureRole.System && node.Role != MirrorV7FeatureRole.Reference;
        }
        private static List<MirrorV7FeatureNode> SemanticNodes(MirrorV7ModelGraph graph)
        {
            var ordered = graph.Nodes.OrderBy(n => n.TreeOrder).ToList();
            var result = new List<MirrorV7FeatureNode>();
            for (int i = 0; i < ordered.Count; i++)
            {
                MirrorV7FeatureNode node = ordered[i];
                if (!IsSemantic(node)) continue;
                int ancestorDepth = node.Depth - 1;
                bool generatedBySystem = false;
                for (int j = i - 1; j >= 0 && ancestorDepth >= 0; j--)
                {
                    MirrorV7FeatureNode possibleAncestor = ordered[j];
                    if (possibleAncestor.Depth != ancestorDepth) continue;
                    if (possibleAncestor.Role == MirrorV7FeatureRole.System)
                    {
                        generatedBySystem = true;
                        break;
                    }
                    ancestorDepth--;
                }
                if (generatedBySystem)
                {
                    Log("FEATURE_MAP_SKIP_SYSTEM_DESCENDANT name=" + node.Name +
                        " type=" + node.TypeName + " depth=" + node.Depth);
                    continue;
                }
                result.Add(node);
            }
            return result;
        }
        private static string Key(MirrorV7FeatureNode node)
        {
            // MirrorPart2 preserves semantic feature order, but SOLIDWORKS can renumber
            // localized names and can re-parent absorbed sketches after save/reopen. Thus
            // neither display name nor tree depth is a stable cross-document identity.
            // Map the Nth occurrence of each feature type; persist both depths as audit data.
            return node.TypeName ?? "";
        }
        private static bool VerifyFlatPatternCanRebuild(ModelDoc2 target)
        {
            var flat = FeatureTreeScannerV7.Scan(target).Nodes.FirstOrDefault(n =>
                string.Equals(n.TypeName, "FlatPattern", StringComparison.OrdinalIgnoreCase));
            if (flat == null)
            {
                Log("FLAT_PATTERN applicable=False result=PASS");
                return false;
            }
            bool originallySuppressed = flat.IsSuppressed;
            try
            {
                if (originallySuppressed && !flat.Feature.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null))
                    throw new InvalidOperationException("Cannot unsuppress FlatPattern for editability verification.");
                if (!target.EditRebuild3()) throw new InvalidOperationException("FlatPattern rebuild failed.");
                bool warning; int error = flat.Feature.GetErrorCode2(out warning);
                if (error != 0 && !warning) throw new InvalidOperationException("FlatPattern error=" + error);
                Log("FLAT_PATTERN feature=" + flat.Name + " rebuild=True error=" + error + " result=PASS");
            }
            finally
            {
                if (originallySuppressed)
                {
                    flat.Feature.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                        (int)swInConfigurationOpts_e.swThisConfiguration, null);
                    target.EditRebuild3();
                }
            }
            return true;
        }
        private static void Activate(ISldWorks app, ModelDoc2 model)
        {
            int error = 0;
            var active = app.ActivateDoc3(model.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as ModelDoc2;
            if (active == null || error != 0 || active.GetPathName() != model.GetPathName())
                throw new InvalidOperationException("Cannot activate expected document. error=" + error);
        }
        private static void Log(string text) { MirrorV7Diagnostics.Log("[NATIVE_PART] " + text); }
    }
}
