using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;

internal static partial class ToolProfileTests
{
    private static string ResultFingerprintRead(AssemblyBatchSnapshot snapshot)
    {
        var read = snapshot.ReadSnapshot();
        if (read == null) throw new InvalidOperationException("Snapshot invalidated while copying results");
        var rows = read.Parts.SelectMany(p => p.Occurrences.Select(o => o.Id + ":" + string.Join("|", p.StaticCandidates.Select(c =>
            c.ToolId + ":" + c.FeatureId + ":" + StaticPairText(c.PunchResult) + ":" + StaticPairText(c.HolderResult))) + "/" +
            string.Join("|", p.ProcessCandidates.Select(c => c.ToolId + ":" + c.FeatureId + ":" + string.Join(";", c.PairSegments.Select(q =>
                q.Pair + ":" + q.Phase + ":" + q.Status + ":" + string.Join(",", q.Reasons) + ":" + (q.Witness == null ? "-" :
                    ADDIN.Commands.BendPlacement.ToolEvidenceCanonical.Number(q.Witness.Parameter) + ":" +
                    ADDIN.Commands.BendPlacement.ToolEvidenceCanonical.Point(q.Witness.Geometry.PointMm) + ":" +
                    ADDIN.Commands.BendPlacement.ToolEvidenceCanonical.Number(q.Witness.Geometry.AxialMm))))))))
            .OrderBy(x => x, StringComparer.Ordinal);
        string fingerprint = string.Join("\n", rows);
        if (!read.Current) throw new InvalidOperationException("Snapshot invalidated before fingerprint publication");
        return fingerprint;
    }
    private static string StaticPairText(AssemblyStaticPairRead p) => p.Status + ":" + string.Join(",", p.Reasons) + ":" + p.CertifiedClearanceLowerMm + ":" +
        (p.Witness == null ? "-" : ADDIN.Commands.BendPlacement.ToolEvidenceCanonical.Point(p.Witness.PointMm) + ":" +
            ADDIN.Commands.BendPlacement.ToolEvidenceCanonical.Number(p.Witness.AxialMm));

    private static void AssemblyReadTests()
    {
        Test("assembly read: full audits bracket copy; strict getters and other threads remain strict", () =>
        {
            int audits = 0, cheap = 0; string invalid = null;
            var guard = new AssemblyResultReadGuard(() => { Interlocked.Increment(ref audits); return invalid; },
                () => { Interlocked.Increment(ref cheap); return null; });
            double ms;
            var copy = guard.Copy(() =>
            {
                for (int i = 0; i < 100; i++) Check(guard.Check() == null, "Shared checks still run");
                Task.Run(() => Check(guard.Check() == null, "A different thread cannot inherit read scope")).GetAwaiter().GetResult();
                return new object();
            }, out ms);
            Check(copy != null && audits == 3 && cheap == 100 && ms >= 0, "Exactly two full boundary audits plus strict other-thread audit");
            guard.Check(); Check(audits == 4, "Outside scope full freshness remains mandatory");
            var discarded = guard.Copy(() => { invalid = "STALE_UNRELATED_PLAN"; return new object(); }, out ms);
            Check(discarded == null, "A final unrelated-plan/review change discards all copied CLEARs");
            invalid = null;
            try { guard.Copy<object>(() => throw new Exception("copy"), out ms); } catch (Exception) { }
            int before = audits; guard.Check(); Check(audits == before + 1, "Failed copy never leaves ambient bypass");
        });
        Test("assembly read: detached statuses/reasons/witnesses equal strict held getters cold and warm", () =>
        {
            var model = CollisionModel(Rect("READ_SOURCE", 0, 0, 1, 1), 3);
            var source = LocalSource("READ_SOURCE", model, new[] { Occurrence(0, 0) }, plans: AssemblyPlans(model, full: true));
            using (var coordinator = new AssemblyBatchCoordinator())
            {
                var request = AssemblyRequest(new[] { source });
                var cold = coordinator.RunAsync(request).GetAwaiter().GetResult();
                Check(ResultFingerprintRead(cold) == ResultFingerprint(cold.Parts), "Strict and bounded-read fields identical");
                var warm = coordinator.RunAsync(request).GetAwaiter().GetResult();
                Check(ResultFingerprintRead(warm) == ResultFingerprint(warm.Parts), "Restored cache proof fields identical");
                var held = warm.ReadSnapshot(); Check(held != null && held.Current, "Fresh bounded read");
                source.Plans.Last().Invalidate();
                Check(!held.Current && warm.ReadSnapshot() == null && !warm.Current, "Other plan invalidates read and strict paths");
                Check(warm.Parts.SelectMany(p => p.ProcessCandidates).All(c => c.ProcessResult == ADDIN.Commands.BendCollision.CollisionStatus.UNKNOWN),
                    "No strict proof remains after invalidation");
            }
        });
    }
}
