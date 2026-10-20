// ChatGPT-authored CG2.1 fixture-specific regression gate.
// Standalone runner companion only; DO NOT add to ADDIN.csproj.
// Reads local fixture manifest (never uploaded to GitHub).
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ADDIN.Tests
{
    internal static partial class Cg2LiveCadRunner
    {
        private const string Cg21SiteManifestName = "CG2_1_EXPECTED_SITES.tsv";

        private static void AssertCg21Sites(string sandboxRoot, string fixtureName, Metrics metrics)
        {
            string manifest = Path.Combine(sandboxRoot, Cg21SiteManifestName);
            if (!File.Exists(manifest))
            {
                Console.WriteLine("  CG2_1_EXACT_SITE_ASSERT=NOT_TESTED (no local manifest)");
                return;
            }

            int matchedExpectations = 0;
            int lineNumber = 0;
            foreach (string sourceLine in File.ReadAllLines(manifest))
            {
                lineNumber++;
                string line = sourceLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] fields = line.Split('\t');
                if (fields.Length != 9)
                    throw new InvalidDataException("CG2.1 site manifest expects 9 tab-separated columns at line " + lineNumber);
                if (!string.Equals(fields[0], fixtureName, StringComparison.OrdinalIgnoreCase))
                    continue;

                matchedExpectations++;
                string source = fields[1].Trim();
                string target = fields[2].Trim();
                string hole = fields[3].Trim();
                string expectedEvidence = fields[4].Trim();
                if (source.Length == 0 || target.Length == 0 || hole.Length == 0)
                    throw new InvalidDataException("Empty occurrence or hole label in site manifest at line " + lineNumber);

                double minSpan, maxSpan;
                int minHits, minContainers;
                if (!double.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out minSpan) ||
                    !double.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture, out maxSpan) ||
                    !int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out minHits) ||
                    !int.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out minContainers) ||
                    double.IsNaN(minSpan) || double.IsNaN(maxSpan) ||
                    double.IsInfinity(minSpan) || double.IsInfinity(maxSpan) ||
                    minSpan < 0 || maxSpan < minSpan || minHits < 0 || minContainers < 0)
                    throw new InvalidDataException("Invalid numeric constraints in site manifest at line " + lineNumber);

                if (metrics.Cg2SubassemblyContainers < minContainers)
                    throw new InvalidOperationException("CG2.1 nested Subassembly coverage missing at line " + lineNumber);

                var matches = metrics.Cg2ExpectedCandidates.Where(c =>
                    string.Equals(c.SourceOccurrence, source, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.TargetOccurrence, target, StringComparison.OrdinalIgnoreCase) &&
                    (hole == "*" || string.Equals(c.SourceHoleLabel, hole, StringComparison.Ordinal))).ToList();

                if (expectedEvidence == "SUPPRESSED")
                {
                    // Candidate absence is NOT proof of counterpart-hole correctness.
                    if (matches.Count != 0)
                        throw new InvalidOperationException("CG2.1 expected candidate absence at line " + lineNumber);
                    Console.WriteLine("  SITE_ABSENCE_OBSERVED line=" + lineNumber + " (not missing-hole proof)");
                    continue;
                }

                if (matches.Count != 1)
                    throw new InvalidOperationException("CG2.1 expected EXACTLY ONE candidate at line " +
                        lineNumber + "; observed " + matches.Count);
                CandidateDetail c = matches[0];
                if (expectedEvidence != "CHECK_ONLY" &&
                    !string.Equals(c.MaterialEvidence, expectedEvidence, StringComparison.Ordinal))
                    throw new InvalidOperationException("CG2.1 evidence mismatch at manifest line " + lineNumber);
                if (c.MaterialSpanMm < minSpan || c.MaterialSpanMm > maxSpan ||
                    c.MaterialHitCount < minHits)
                    throw new InvalidOperationException("CG2.1 material span/hit count mismatch at line " + lineNumber);

                Console.WriteLine("  CG2_1_EXACT_SITE_ASSERT=PASS line=" + lineNumber +
                    " candidateId=" + c.Id + " material=" + c.MaterialEvidence +
                    " spanMm=" + c.MaterialSpanMm.ToString("F3", CultureInfo.InvariantCulture));
            }

            if (matchedExpectations == 0)
                Console.WriteLine("  CG2_1_EXACT_SITE_ASSERT=NOT_TESTED (no expectations for fixture " + fixtureName + ")");
            else
                Console.WriteLine("  CG2_1_EXACT_SITE_ASSERTIONS=" + matchedExpectations +
                    " VERIFIED (individual PASS/absence observations above)");
        }
    }
}
