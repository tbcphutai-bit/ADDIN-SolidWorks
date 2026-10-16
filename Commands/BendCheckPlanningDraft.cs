using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace ADDIN.Commands
{
    // Read-only draft associated with the existing Check chan/Check Min session.
    // No order is declared feasible until tool geometry and intermediate-state
    // collision checks have been implemented and validated.
    internal sealed class BendCheckPlanningDraft
    {
        internal const string Unverified = "CHUA_XAC_MINH";
        public string Status = Unverified;
        public readonly List<BendPlanningBend> Bends = new List<BendPlanningBend>();
        public readonly List<BendPlanningOrder> CandidateOrders = new List<BendPlanningOrder>();
        public int SourceRowCount;
        public bool Exhaustive;
        public string Reason = "";
        public string DiagnosticError;
        public string Scope = "Check Min bend inventory only; no tooling, motion or collision evaluation.";

        internal static BendCheckPlanningDraft Create(IList<VCutPropertySetupCommand.BendRow> source)
        {
            var result = new BendCheckPlanningDraft();
            if (source == null || source.Count == 0)
            {
                result.Reason = "No recognized bend rows available."; return result;
            }
            result.SourceRowCount = source.Count;
            var errors = new HashSet<string>(StringComparer.Ordinal);
            var featureIds = new HashSet<int>();
            var persistentIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                var row = source[i];
                if (row == null)
                {
                    errors.Add("Inventory contains a null row; candidate orders suppressed.");
                    continue;
                }
                if (row.FeatureId <= 0)
                    errors.Add("Invalid native feature ID; candidate orders suppressed.");
                if (!featureIds.Add(row.FeatureId))
                    errors.Add("Duplicate feature IDs; candidate orders suppressed.");
                if (!string.IsNullOrEmpty(row.PersistentId) && !persistentIds.Add(row.PersistentId))
                    errors.Add("Duplicate persistent identities; candidate orders suppressed.");
                if (row.MinimumContext != null && !row.MinimumContext.Current)
                    errors.Add("Inventory belongs to a stale Min context; rescan before enumerating.");
                var item = new BendPlanningBend
                {
                    Id = "B" + (i + 1) + "-F" + row.FeatureId,
                    FeatureId = row.FeatureId,
                    FeatureName = row.FeatureName ?? "",
                    HasPersistentId = !string.IsNullOrEmpty(row.PersistentId),
                    AngleDeg = row.BendAngleDeg,
                    RadiusMm = row.BendRadiusMm,
                    ThicknessMm = row.ModelThicknessMm,
                    Mark = row.Kind ?? "",
                    CheckMinStatus = Unverified
                };
                // The diagnostic serializer must never emit NaN/Infinity (invalid
                // JSON), nor turn an unknown API measurement into a zero value.
                item.AngleDeg = Finite(item.AngleDeg, "angle", item);
                item.RadiusMm = Finite(item.RadiusMm, "radius", item);
                item.ThicknessMm = Finite(item.ThicknessMm, "thickness", item);
                try
                {
                    if (row.MinimumContext != null)
                        item.CheckMinStatus = row.MinimumContext.Evaluate(row).Status.ToString();
                }
                catch (Exception ex) { Warn(item, ex.Message); }
                result.Bends.Add(item);
            }
            if (errors.Count > 0)
            {
                result.Reason = string.Join(" ", errors.OrderBy(e => e, StringComparer.Ordinal));
                return result;
            }
            if (result.Bends.Count > 8)
            {
                result.Reason = "More than eight bends: enumeration deferred until pruning is implemented.";
                return result;
            }
            var used = new bool[result.Bends.Count];
            Enumerate(result.Bends.Select(b => b.Id).ToArray(), used, new List<string>(),
                result.CandidateOrders, 24);
            long factorial = 1;
            for (int i = 2; i <= result.Bends.Count; i++) factorial *= i;
            result.Exhaustive = result.CandidateOrders.Count == factorial;
            result.Reason = result.Exhaustive
                ? "All order permutations listed, but none checked."
                : "Truncated at 24 orders; none checked.";
            return result;
        }

        private static double? Finite(double? value, string name, BendPlanningBend item)
        {
            if (!value.HasValue || !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)) return value;
            Warn(item, "Non-finite " + name + " reported as null.");
            return null;
        }

        private static void Warn(BendPlanningBend item, string message)
        {
            item.Warning = string.IsNullOrEmpty(item.Warning) ? message : item.Warning + " " + message;
        }

        private static void Enumerate(string[] ids, bool[] used, List<string> current,
            List<BendPlanningOrder> output, int limit)
        {
            if (output.Count >= limit) return;
            if (current.Count == ids.Length)
            {
                output.Add(new BendPlanningOrder
                {
                    Index = output.Count + 1,
                    BendIds = current.ToArray(),
                    Status = Unverified
                });
                return;
            }
            for (int i = 0; i < ids.Length && output.Count < limit; i++)
            {
                if (used[i]) continue;
                used[i] = true; current.Add(ids[i]);
                Enumerate(ids, used, current, output, limit);
                current.RemoveAt(current.Count - 1); used[i] = false;
            }
        }

        internal string ToJson()
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return serializer.Serialize(new
            {
                Status, SourceRowCount, BendCount = Bends.Count, Bends,
                CandidateOrderCount = CandidateOrders.Count,
                Exhaustive, Reason, DiagnosticError, Scope, CandidateOrders
            });
        }
    }

    internal sealed class BendPlanningBend
    {
        public string Id;
        public int FeatureId;
        public string FeatureName;
        public bool HasPersistentId;
        public double? AngleDeg;
        public double? RadiusMm;
        public double? ThicknessMm;
        public string Mark;
        public string CheckMinStatus;
        public string Warning;
    }

    internal sealed class BendPlanningOrder
    {
        public int Index;
        public string[] BendIds;
        public string Status;
    }
}
