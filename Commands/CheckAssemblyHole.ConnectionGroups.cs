// CG1 — Read-only diagnostics over the existing CHECK HOLE stacks.
// .NET Framework 4.8, C# 7.3. Does not modify HoleAxis, HoleStack,
// tolerance thresholds, matching or OK/NG verdicts.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands
{
    internal sealed class CheckHoleConnectionGroupRow
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public string Components { get; set; }
        public int HoleCount { get; set; }
        public string EndCandidates { get; set; }
        public string MasterDecision { get; set; }
        public string MatchSource { get; set; }
        public double MaxOffsetMm { get; set; }
        public double MaxAngleDeg { get; set; }
        public string Note { get; set; }
    }

    internal sealed class CheckHoleConnectionCoverage
    {
        public int LogicalHoleCount;
        public int GroupCount;
        public int PairedHoleCount;
        public int UnmatchedHoleCount;
        public int NgGroupsWithoutMaster;
        public int ComponentOccurrencesWithHoles;
        public string CoverageStatus;
        public readonly List<CheckHoleConnectionGroupRow> Groups =
            new List<CheckHoleConnectionGroupRow>();
    }

    public sealed partial class CheckAssemblyHole
    {
        /// <summary>
        /// CG1: automated grouping of *existing matched hole stacks*.
        /// A stack is geometric evidence, NOT proof of fastening intent.
        /// Do not use this output to create missing-hole NG or to select an
        /// authoritative master without further design/assembly evidence.
        /// </summary>
        private static CheckHoleConnectionCoverage BuildConnectionCoverage(
            List<HoleAxis> holes, List<HoleStackResult> results)
        {
            if (holes == null) throw new ArgumentNullException("holes");
            if (results == null) throw new ArgumentNullException("results");

            var coverage = new CheckHoleConnectionCoverage();
            var paired = new HashSet<HoleAxis>();
            coverage.LogicalHoleCount = holes.Count;
            coverage.ComponentOccurrencesWithHoles = holes
                .Where(h => h != null)
                .Select(h => h.ComponentOccurrence ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();

            foreach (HoleStackResult result in results)
            {
                if (result == null || result.Stack == null || result.Reference == null)
                    continue;

                List<HoleAxis> members = result.Stack.Holes
                    .Where(h => h != null).Distinct().ToList();
                if (members.Count < 2) continue;
                foreach (HoleAxis member in members) paired.Add(member);

                // The axis orientation is canonical, not a known assembly approach.
                // These are merely two geometric endpoints, NOT a master/host.
                double[] axis = result.Reference.Direction;
                var ordered = members
                    .OrderBy(h => Dot(h.Center, axis))
                    .ThenBy(h => h.ComponentOccurrence ?? "", StringComparer.OrdinalIgnoreCase)
                    .ToList();
                double firstProjection = Dot(ordered[0].Center, axis);
                double lastProjection = Dot(ordered[ordered.Count - 1].Center, axis);
                string endCandidates;
                if (lastProjection - firstProjection <= 0.00005) // 0.05 mm: display only
                {
                    endCandidates = "Khong phan biet duoc hai dau truc";
                }
                else
                {
                    endCandidates = (ordered[0].ComponentOccurrence ?? "?") + " | "
                        + (ordered[ordered.Count - 1].ComponentOccurrence ?? "?");
                }

                var row = new CheckHoleConnectionGroupRow
                {
                    Id = coverage.Groups.Count + 1,
                    Status = result.IsNg ? "NG" : "OK",
                    Components = string.Join(" / ", members
                        .Select(h => h.ComponentOccurrence ?? "?")
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
                    HoleCount = members.Count,
                    EndCandidates = endCandidates,
                    MasterDecision = result.IsNg
                        ? "CHUA_XAC_DINH_MASTER"
                        : "KHONG_CAN_MASTER_DE_CHECK_DONG_TAM",
                    MatchSource = result.Stack.MatchSource.ToString(),
                    MaxOffsetMm = result.MaxOffsetM * 1000.0,
                    MaxAngleDeg = result.MaxAngleDeg,
                    Note = result.IsNg
                        ? "NG hinh hoc da co; chua quy loi cho Part nao"
                        : "Cum hinh hoc OK; khong suy ra y dinh lap ghep"
                };
                coverage.Groups.Add(row);
                if (result.IsNg) coverage.NgGroupsWithoutMaster++;
            }

            coverage.GroupCount = coverage.Groups.Count;
            coverage.PairedHoleCount = paired.Count;
            coverage.UnmatchedHoleCount = holes.Count(h => h != null && !paired.Contains(h));
            coverage.CoverageStatus = holes.Count == 0
                ? "KHONG_DOC_DUOC_LO_HOAC_MODEL_KHONG_CO_LO"
                : coverage.UnmatchedHoleCount > 0
                    ? "CHUA_DAY_DU: CO_LO_CHUA_GHEP"
                    : "DA_GHEP_LO_NHAN_DIEN; CHUA_KIEM_TRA_PART_KHONG_CO_LO";
            return coverage;
        }
    }
}
