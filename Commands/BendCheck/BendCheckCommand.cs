using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.BendCheck
{
    // Bend Check backend V0: read-only inventory and unverified order candidates.
    // IMPORTANT: this module never asserts manufacturing feasibility. Collision/clearance
    // and press/tool verification must be implemented before any result can say PASS.
    internal sealed class BendCheckCommand
    {
        private static readonly string[] CompanyToolIds =
        {
            "amd-202662", "amd-204662", "amd-std-003", "amd-std-0048",
            "amd-std-0049", "amd-std-004", "amd-std-008", "amd-std-045",
            "amd-std-047", "amd-std-103", "amd-std-109", "amd-std-117",
            "amd-std-200", "amd-std-202", "amd-std-210", "amd-std-452",
            "amd-std-453", "kik-7181328001"
        };

        internal string AnalyzeToJson(ISldWorks app)
        {
            BendCheckReport report = Analyze(app);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Serialize(report);
        }

        internal BendCheckReport Analyze(ISldWorks app)
        {
            var report = new BendCheckReport
            {
                Phase = "read-only-discovery-v0",
                Status = "UNVERIFIED",
                Tools = ReadToolInventory()
            };
            if (app == null)
            {
                report.Issues.Add("SOLIDWORKS application is not available.");
                return report;
            }

            ModelDoc2 model;
            try { model = app.ActiveDoc as ModelDoc2; }
            catch (Exception ex)
            {
                report.Issues.Add("Cannot access the active document: " + ex.Message);
                return report;
            }
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                report.Issues.Add("Open a native SOLIDWORKS sheet-metal Part (.SLDPRT).");
                return report;
            }
            report.DocumentPath = model.GetPathName() ?? "";
            try
            {
                Configuration configuration = model.ConfigurationManager.ActiveConfiguration;
                report.Configuration = configuration == null ? "" : configuration.Name;
                if (model.Extension.NeedsRebuild2 != 0)
                    report.Issues.Add("Model needs rebuild. Data may be stale; this command will not rebuild it.");
                if (model.GetSaveFlag())
                    report.Issues.Add("Document has unsaved changes. Results describe current in-memory geometry.");
                PartDoc part = model as PartDoc;
                if (part == null)
                {
                    report.Issues.Add("Active Part cannot be accessed.");
                    return report;
                }
                Array bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as Array;
                report.SolidBodyCount = bodies == null ? 0 : bodies.Length;
                if (report.SolidBodyCount != 1)
                {
                    report.Issues.Add("Only single-solid-body sheet-metal Parts are supported.");
                    return report;
                }

                int guard = 0;
                for (Feature root = model.FirstFeature() as Feature;
                    root != null && ++guard < 20000;
                    root = root.GetNextFeature() as Feature)
                {
                    ScanFeature(root, report, 0);
                }
                if (guard >= 20000) report.Issues.Add("Feature-tree guard reached.");
                if (!report.HasSheetMetalFeature)
                {
                    report.Issues.Add("No native SheetMetal feature found in the active configuration.");
                    return report;
                }
                if (!report.HasFlatPatternFeature)
                    report.Issues.Add("No FlatPattern feature found; native sheet-metal scope is not confirmed.");
                if (report.Bends.Count == 0)
                    report.Issues.Add("No active OneBend features found. This is not proof that the part is flat.");

                // These are permutations to be CHECKED, never bend sequences declared feasible.
                // We deliberately do not infer missing angles, lengths, press capacity or
                // interference from feature names, tool labels, or catalog pictures.
                report.SequenceCandidates = SequenceCandidates.Generate(
                    report.Bends.Select(b => b.BendId).ToList(), 24);
                if (report.SequenceCandidates.Count > 0)
                    report.Issues.Add("All sequence candidates are UNVERIFIED: 2D transition/collision checks not implemented.");
                report.Issues.Add("Tool DXF files are listed as an inventory only; no punch/die/clamp fit has been verified.");
            }
            catch (Exception ex)
            {
                report.Issues.Add("Recognition interrupted: " + ex.Message);
            }
            return report;
        }

        private static List<BendCheckTool> ReadToolInventory()
        {
            string directory = Environment.GetEnvironmentVariable("ADDIN_BEND_TOOL_DXF_DIR");
            bool available = !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
            return CompanyToolIds.Select(id => new BendCheckTool
            {
                ToolId = id,
                DxfFileName = id + "_DIM.dxf",
                FilePresent = available && File.Exists(Path.Combine(directory, id + "_DIM.dxf")),
                GeometryVerified = false,
                ClampVerified = false
            }).ToList();
        }

        private static void ScanFeature(Feature feature, BendCheckReport report, int depth)
        {
            if (feature == null || depth > 40)
            {
                if (depth > 40) report.Issues.Add("Feature depth limit reached.");
                return;
            }
            string type = "";
            try { type = feature.GetTypeName2() ?? ""; }
            catch (Exception ex)
            {
                report.Issues.Add("Feature type unavailable: " + ex.Message);
            }

            if (string.Equals(type, "SheetMetal", StringComparison.OrdinalIgnoreCase))
                report.HasSheetMetalFeature = true;
            if (string.Equals(type, "FlatPattern", StringComparison.OrdinalIgnoreCase))
                report.HasFlatPatternFeature = true;

            if (string.Equals(type, "OneBend", StringComparison.OrdinalIgnoreCase))
            {
                bool suppressed;
                if (!TrySuppressed(feature, out suppressed))
                    report.Issues.Add("Cannot resolve suppression state for: " + feature.Name);
                else if (!suppressed)
                    ReadBend(feature, report);
            }

            int guard = 0;
            try
            {
                for (Feature sub = feature.GetFirstSubFeature() as Feature;
                    sub != null && ++guard < 20000;
                    sub = sub.GetNextSubFeature() as Feature)
                    ScanFeature(sub, report, depth + 1);
            }
            catch (Exception ex)
            {
                report.Issues.Add("Subfeature read failed for " + feature.Name + ": " + ex.Message);
            }
            if (guard >= 20000) report.Issues.Add("Subfeature guard reached at " + feature.Name);
        }

        private static bool TrySuppressed(Feature feature, out bool suppressed)
        {
            suppressed = false;
            try
            {
                Array flags = feature.IsSuppressed2(
                    (int)swInConfigurationOpts_e.swThisConfiguration, null) as Array;
                if (flags == null || flags.Length != 1) return false;
                suppressed = Convert.ToBoolean(flags.GetValue(flags.GetLowerBound(0)));
                return true;
            }
            catch { return false; }
        }

        private static void ReadBend(Feature feature, BendCheckReport report)
        {
            var bend = new BendCheckBend
            {
                BendId = "B" + (report.Bends.Count + 1),
                FeatureName = feature.Name ?? "",
                FeatureType = "OneBend",
                Status = "UNVERIFIED"
            };
            try
            {
                IOneBendFeatureData definition = feature.GetDefinition() as IOneBendFeatureData;
                if (definition == null)
                {
                    bend.Warning = "No OneBend feature data.";
                }
                else
                {
                    double angleRad = definition.BendAngle;
                    double radiusM = definition.BendRadius;
                    if (Finite(angleRad) && Math.Abs(angleRad) > 1e-8)
                        bend.AngleDeg = Math.Abs(angleRad) * 180.0 / Math.PI;
                    if (Finite(radiusM) && radiusM >= 0)
                        bend.InsideRadiusMm = radiusM * 1000.0;
                    Array segments = definition.FlatPatternSketchSegments2 as Array;
                    if (segments != null && segments.Length == 1)
                    {
                        SketchSegment line = segments.GetValue(segments.GetLowerBound(0)) as SketchSegment;
                        if (line != null && line.IsBendLine())
                        {
                            double lengthM = line.GetLength();
                            if (Finite(lengthM) && lengthM > 0)
                                bend.FlatBendLineLengthMm = lengthM * 1000.0;
                        }
                    }
                    if (!bend.AngleDeg.HasValue || !bend.InsideRadiusMm.HasValue)
                        bend.Warning = "Bend angle/radius unavailable; no values inferred.";
                }
            }
            catch (Exception ex) { bend.Warning = "Unable to read bend data: " + ex.Message; }
            report.Bends.Add(bend);
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal static class SequenceCandidates
    {
        internal static List<BendOrderCandidate> Generate(List<string> bends, int limit)
        {
            var output = new List<BendOrderCandidate>();
            if (bends == null || bends.Count == 0 || bends.Count > 8 || limit <= 0)
                return output;
            var used = new bool[bends.Count];
            var current = new List<string>();
            Search(bends, used, current, output, limit);
            return output;
        }

        private static void Search(List<string> bends, bool[] used, List<string> current,
            List<BendOrderCandidate> result, int limit)
        {
            if (result.Count >= limit) return;
            if (current.Count == bends.Count)
            {
                result.Add(new BendOrderCandidate
                {
                    BendIds = current.ToArray(),
                    Status = "NOT_EVALUATED"
                });
                return;
            }
            for (int i = 0; i < bends.Count && result.Count < limit; ++i)
            {
                if (used[i]) continue;
                used[i] = true;
                current.Add(bends[i]);
                Search(bends, used, current, result, limit);
                current.RemoveAt(current.Count - 1);
                used[i] = false;
            }
        }
    }

    internal sealed class BendCheckReport
    {
        public string Phase { get; set; }
        public string Status { get; set; }
        public string DocumentPath { get; set; }
        public string Configuration { get; set; }
        public int SolidBodyCount { get; set; }
        public bool HasSheetMetalFeature { get; set; }
        public bool HasFlatPatternFeature { get; set; }
        public List<BendCheckBend> Bends { get; set; } = new List<BendCheckBend>();
        public List<BendCheckTool> Tools { get; set; } = new List<BendCheckTool>();
        public List<BendOrderCandidate> SequenceCandidates { get; set; } = new List<BendOrderCandidate>();
        public List<string> Issues { get; set; } = new List<string>();
    }

    internal sealed class BendCheckBend
    {
        public string BendId { get; set; }
        public string FeatureName { get; set; }
        public string FeatureType { get; set; }
        public double? AngleDeg { get; set; }
        public double? InsideRadiusMm { get; set; }
        public double? FlatBendLineLengthMm { get; set; }
        public string Status { get; set; }
        public string Warning { get; set; }
    }

    internal sealed class BendCheckTool
    {
        public string ToolId { get; set; }
        public string DxfFileName { get; set; }
        public bool FilePresent { get; set; }
        public bool GeometryVerified { get; set; }
        public bool ClampVerified { get; set; }
    }

    internal sealed class BendOrderCandidate
    {
        public string[] BendIds { get; set; }
        public string Status { get; set; }
    }
}
