// CG2 Review #3 — dedicated repeatability and traversal diagnostic.
// To compile: include this file alongside CG2_LIVE_CAD_RUNNER.cs.
// C# 7.3 / .NET Framework 4.8. NO production command modifications.
//
// This is a partial helper, not a standalone EXE and has no Main().
// Invocation through existing runner:
//   Cg2LiveCadRunner.exe <CG1.dll> <CG2.dll> <sandbox-root> <fixture.sldasm> --repeatability
//
// Each of four Check Hole runs opens and closes a fresh read-only CAD document
// using the existing safety guards in RunOneIsolated. A full pairwise breakdown
// is written even when byte parity fails, then exit code 1 is returned.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Tests
{
    internal static partial class Cg2LiveCadRunner
    {
        private sealed class RepeatSnapshot
        {
            public string Tag;
            public string CsvPath;
            public string CsvSha256;
            public byte[] CsvBytes;
            public string ComponentOrder;
            public string ExactOrderedRows;
            public string CanonicalizedRows;
            public Metrics ProductionMetrics;
        }

        private static int RunRepeatability(
            ISldWorks app, Type cg1Type, Type cg2Type,
            Assembly cg1Assembly, Assembly cg2Assembly,
            string sandboxRoot, string fixture)
        {
            RequireSandboxPath(sandboxRoot, fixture);
            string folder = Path.Combine(sandboxRoot, "CG2_REVIEW3_REPEATABILITY");
            Directory.CreateDirectory(folder);

            Console.WriteLine("REVIEW3: repeating the same fixture in fresh read-only opens");
            Console.WriteLine("REVIEW3: strict byte mismatch is a FAIL, even when canonicalized rows agree");
            var runs = new List<RepeatSnapshot>();
            try
            {
                runs.Add(CaptureRepeatRun(app, cg1Type, cg1Assembly,
                    sandboxRoot, fixture, folder, "cg1_1", false));
                runs.Add(CaptureRepeatRun(app, cg1Type, cg1Assembly,
                    sandboxRoot, fixture, folder, "cg1_2", false));
                runs.Add(CaptureRepeatRun(app, cg2Type, cg2Assembly,
                    sandboxRoot, fixture, folder, "cg2_1", true));
                runs.Add(CaptureRepeatRun(app, cg2Type, cg2Assembly,
                    sandboxRoot, fixture, folder, "cg2_2", true));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("REVIEW3_INCOMPLETE (FAIL): " + ex);
                return 1;
            }

            var sb = new StringBuilder();
            bool cg1Stable = CompareRepeatPair(runs[0], runs[1], sb);
            bool cg2Stable = CompareRepeatPair(runs[2], runs[3], sb);
            bool crossEqual = CompareRepeatPair(runs[0], runs[2], sb);

            bool productionCountersEqual = true;
            try
            {
                CompareMetrics(runs[0].ProductionMetrics, runs[2].ProductionMetrics, fixture);
            }
            catch (Exception ex)
            {
                productionCountersEqual = false;
                sb.AppendLine("CG1_vs_CG2_COUNTERS_OR_CANONICAL_SIGNATURE=FAIL: " + ex.Message);
            }

            sb.AppendLine("CG1_REPEATABLE_BYTES=" + (cg1Stable ? "YES" : "NO"));
            sb.AppendLine("CG2_REPEATABLE_BYTES=" + (cg2Stable ? "YES" : "NO"));
            sb.AppendLine("CG1_VS_CG2_BYTE_PARITY=" + (crossEqual ? "PASS" : "FAIL"));
            sb.AppendLine("PRODUCTION_CANONICAL_COUNTERS=" +
                          (productionCountersEqual ? "PASS" : "FAIL"));

            string outcome;
            if (!cg1Stable)
                outcome = "BASELINE_NONDETERMINISM_OBSERVED";
            else if (!cg2Stable)
                outcome = "CANDIDATE_NONDETERMINISM_OBSERVED";
            else if (!crossEqual)
                outcome = "REPEATABLE_CROSS_BUILD_DIFFERENCE_REQUIRES_INVESTIGATION";
            else
                outcome = "STRICT_BYTE_PARITY_OBSERVED";
            sb.AppendLine("DIAGNOSTIC_CLASSIFICATION=" + outcome);
            sb.AppendLine("CAUSE_OF_ORDER_CHANGE=NOT_YET_PROVEN_BY_THIS_CLASSIFICATION");
            sb.AppendLine("SKETCH_MARKER_WRITE_PARITY=NOT_TESTED");

            string reportPath = Path.Combine(folder, "repeatability-summary.txt");
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine(sb.ToString());
            Console.WriteLine("REVIEW3_SUMMARY=" + reportPath);

            // Required strict gate: baseline repeatability, candidate repeatability,
            // cross-build byte equality, and existing production counters all pass.
            bool success = cg1Stable && cg2Stable && crossEqual && productionCountersEqual;
            return success ? 0 : 1;
        }

        private static RepeatSnapshot CaptureRepeatRun(
            ISldWorks app, Type checkerType, Assembly binaryAssembly,
            string root, string fixture, string folder, string tag, bool isCandidate)
        {
            // RunOneIsolated checks active CAD document, sandbox references,
            // old NG markers, model feature tree/dirty state, and file SHA;
            // its finally block closes only the newly opened fixture.
            Metrics m = RunOneIsolated(app, checkerType, root, fixture, isCandidate);
            if (m.RawReport == null)
                throw new InvalidOperationException(tag + " did not return a production report");
            if (m.ComponentTraversal == null || m.OrderedRowSignature == null)
                throw new InvalidOperationException(tag + " missing ordered traversal/row trace");

            Type csv = binaryAssembly.GetType("ADDIN.Commands.CheckHoleCsv", true);
            MethodInfo write = csv.GetMethod("Write", BindingFlags.Static | BindingFlags.Public);
            if (write == null) throw new MissingMethodException(csv.FullName, "Write");

            string csvPath = Path.Combine(folder, tag + ".csv");
            write.Invoke(null, new object[] { csvPath, m.RawReport });
            byte[] bytes = File.ReadAllBytes(csvPath);
            if (bytes.Length == 0) throw new InvalidOperationException(tag + " CSV is empty");
            string sha = Sha256(csvPath);
            File.WriteAllText(Path.Combine(folder, tag + ".component-order.txt"),
                m.ComponentTraversal, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, tag + ".raw-ordered-rows.txt"),
                m.OrderedRowSignature, new UTF8Encoding(false));

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "REVIEW3_RUN={0} CSV_BYTES={1} CSV_SHA256={2} ROWS={3} HOLES={4} STACKS={5} NG={6}",
                tag, bytes.Length, sha, m.RowCount, m.LogicalHoles, m.StackCount, m.NgStacks));
            return new RepeatSnapshot
            {
                Tag = tag,
                CsvPath = csvPath,
                CsvSha256 = sha,
                CsvBytes = bytes,
                ComponentOrder = m.ComponentTraversal,
                ExactOrderedRows = m.OrderedRowSignature,
                CanonicalizedRows = m.RowSignature,
                ProductionMetrics = m
            };
        }

        private static bool CompareRepeatPair(
            RepeatSnapshot left, RepeatSnapshot right, StringBuilder trace)
        {
            byte[] a = left.CsvBytes, b = right.CsvBytes;
            bool bytesEqual = a.SequenceEqual(b);
            bool orderEqual = string.Equals(left.ComponentOrder, right.ComponentOrder,
                StringComparison.Ordinal);
            bool rowOrderEqual = string.Equals(left.ExactOrderedRows, right.ExactOrderedRows,
                StringComparison.Ordinal);
            bool canonicalEqual = string.Equals(left.CanonicalizedRows, right.CanonicalizedRows,
                StringComparison.Ordinal);

            int firstDiff = -1;
            if (!bytesEqual)
            {
                int limit = Math.Min(a.Length, b.Length);
                for (int i = 0; i < limit; i++)
                {
                    if (a[i] != b[i]) { firstDiff = i; break; }
                }
                if (firstDiff < 0) firstDiff = limit;
            }

            trace.AppendLine(left.Tag + " vs " + right.Tag);
            trace.AppendLine("  CSV_BYTE_EQUAL=" + bytesEqual + " SHA256=" +
                left.CsvSha256 + " / " + right.CsvSha256 +
                " FIRST_DIFFERENT_BYTE=" + firstDiff);
            trace.AppendLine("  ORDERED_GETCOMPONENTS_EQUAL=" + orderEqual);
            trace.AppendLine("  RAW_ROW_ORDER_AND_EXACT_VALUES_EQUAL=" + rowOrderEqual);
            trace.AppendLine("  CANONICALIZED_ROW_SIGNATURE_EQUAL=" + canonicalEqual);
            WriteFirstDifferentLines(left.ComponentOrder, right.ComponentOrder, trace, "COMPONENT_ORDER");
            WriteFirstDifferentLines(left.ExactOrderedRows, right.ExactOrderedRows, trace, "RAW_ROWS");
            WriteFirstDifferentCsvRecords(left.CsvBytes, right.CsvBytes, trace);

            return bytesEqual;
        }

        private static void WriteFirstDifferentLines(
            string a, string b, StringBuilder output, string label)
        {
            string[] left = (a ?? "").Split('\n');
            string[] right = (b ?? "").Split('\n');
            int count = 0;
            for (int i = 0; i < Math.Max(left.Length, right.Length) && count < 5; i++)
            {
                string x = i < left.Length ? left[i] : "<missing>";
                string y = i < right.Length ? right[i] : "<missing>";
                if (string.Equals(x, y, StringComparison.Ordinal)) continue;
                output.AppendLine("  " + label + "_DIFF index=" + i +
                    " LEFT=" + SafeLog(x) + " RIGHT=" + SafeLog(y));
                count++;
            }
        }

        private static void WriteFirstDifferentCsvRecords(
            byte[] a, byte[] b, StringBuilder output)
        {
            string textA = new UTF8Encoding(true, true).GetString(a);
            string textB = new UTF8Encoding(true, true).GetString(b);
            string[] left = SplitCsvRecords(textA).ToArray();
            string[] right = SplitCsvRecords(textB).ToArray();
            int count = 0;
            for (int i = 0; i < Math.Max(left.Length, right.Length) && count < 5; i++)
            {
                string x = i < left.Length ? left[i] : "<missing>";
                string y = i < right.Length ? right[i] : "<missing>";
                if (string.Equals(x, y, StringComparison.Ordinal)) continue;
                output.AppendLine("  CSV_RECORD_DIFF index=" + i +
                    " LEFT=" + SafeLog(x) + " RIGHT=" + SafeLog(y));
                count++;
            }
        }

        // Splits records outside quoted strings; preserves newlines within
        // quoted CSV fields and doubled quote escapes. For diagnostics only.
        private static IEnumerable<string> SplitCsvRecords(string text)
        {
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append("\"\"");
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                        field.Append(c);
                    }
                    continue;
                }

                if (!quoted && (c == '\r' || c == '\n'))
                {
                    yield return field.ToString();
                    field.Clear();
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                }
                else field.Append(c);
            }
            if (quoted)
                throw new FormatException("Unterminated quoted CSV data in repeatability trace");
            if (field.Length > 0) yield return field.ToString();
        }

        private static string SafeLog(string s)
        {
            if (s == null) return "<null>";
            string clean = s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0");
            return clean.Length <= 400 ? clean : clean.Substring(0, 400) + "...";
        }

        private static string SnapshotComponentTraversal(ModelDoc2 model)
        {
            AssemblyDoc assembly = model as AssemblyDoc;
            if (assembly == null) throw new InvalidOperationException("Expected AssemblyDoc");
            object[] components = assembly.GetComponents(false) as object[];
            if (components == null)
                throw new InvalidOperationException("GetComponents(false) returned null");
            var sb = new StringBuilder();
            for (int i = 0; i < components.Length; i++)
            {
                Component2 c = components[i] as Component2;
                if (c == null) throw new InvalidOperationException("Invalid component in traversal at " + i);
                sb.Append(i.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(c.Name2 ?? "<null>").Append('\n');
            }
            return sb.ToString();
        }

        // No sort. No Ref/Actual swapping. No F6 rounding.
        // This trace exists solely to diagnose changing row order/orientation.
        private static string OrderedRowSignatureExact(object report)
        {
            FieldInfo rowsField = report.GetType().GetField("Rows");
            if (rowsField == null)
                throw new MissingFieldException(report.GetType().FullName, "Rows");
            IEnumerable rows = rowsField.GetValue(report) as IEnumerable;
            if (rows == null) throw new InvalidOperationException("Missing report rows");
            var sb = new StringBuilder();
            foreach (object row in rows)
            {
                Type t = row.GetType();
                foreach (string name in new[]
                {
                    "Id", "Status", "Kind", "ReferenceComponent", "ActualComponent",
                    "OffsetMm", "AngleDeg", "Reason", "ReferenceCenter",
                    "ActualCenter", "ViewAxis", "OrientFromReferenceSide",
                    "ViewRadiusM", "CanMark"
                })
                {
                    PropertyInfo property = t.GetProperty(name);
                    if (property == null)
                        throw new MissingMemberException(t.FullName, name);
                    object value = property.GetValue(row, null);
                    string field;
                    if (value is double[])
                        field = string.Join(";", ((double[])value).Select(
                            d => d.ToString("R", CultureInfo.InvariantCulture)));
                    else if (value is double)
                        field = ((double)value).ToString("R", CultureInfo.InvariantCulture);
                    else field = value == null ? "<null>" : Convert.ToString(
                        value, CultureInfo.InvariantCulture);
                    sb.Append(name).Append(':').Append(field.Length).Append(':')
                        .Append(field).Append('|');
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
