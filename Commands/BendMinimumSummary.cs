using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands
{
    internal sealed class BendMinimumSummary
    {
        internal BendMinimumStatus Status;
        internal string Text, Counts;
        internal static BendMinimumSummary Create(IEnumerable<VCutPropertySetupCommand.BendRow> rows)
        {
            var checks = (rows ?? Enumerable.Empty<VCutPropertySetupCommand.BendRow>())
                .Select(r => r.MinimumContext?.Evaluate(r)).ToList();
            int pass = checks.Count(c => c != null && (c.Status == BendMinimumStatus.MeetsMinimum || c.Status == BendMinimumStatus.GroovedAccepted));
            int fail = checks.Count(c => c != null && c.Status == BendMinimumStatus.BelowMinimum);
            int unknown = checks.Count - pass - fail;
            var status = fail > 0 ? BendMinimumStatus.BelowMinimum : unknown > 0 || checks.Count == 0 ? BendMinimumStatus.InsufficientData : BendMinimumStatus.MeetsMinimum;
            return new BendMinimumSummary {
                Status = status,
                Text = status == BendMinimumStatus.MeetsMinimum ? "✓ Đạt tối thiểu" : status == BendMinimumStatus.BelowMinimum ? "✕ Không đạt tối thiểu" : "? Chưa đủ dữ liệu",
                Counts = pass + " đạt · " + fail + " NG · " + unknown + " chưa đủ dữ liệu"
            };
        }
    }
}
