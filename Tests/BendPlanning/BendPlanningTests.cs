using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands;
using Row = ADDIN.Commands.VCutPropertySetupCommand.BendRow;

internal static class BendPlanningTests
{
    private static int passed, failed, assertions;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static void Test(string name, Action test)
    { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
    private static List<Row> Rows(int count)
    {
        return Enumerable.Range(1, count).Select(i => new Row { FeatureId = 10 + i, FeatureName = "Bend " + i, PersistentId = "pid-" + i,
            BendAngleDeg = 90, BendRadiusMm = 1.5, ModelThicknessMm = 2.3, Kind = "",
            MinimumContext = new BendMinimumContext { Read = r => new BendMinimumRowResult { Status = BendMinimumStatus.MeetsMinimum } } }).ToList();
    }
    private static void Valid(BendCheckPlanningDraft draft)
    {
        var root = (Dictionary<string, object>)Json.DeserializeObject(draft.ToJson());
        Check(root["Status"].ToString() == "CHUA_XAC_MINH", "Draft status must remain unverified");
        Check(Convert.ToInt32(root["BendCount"]) == draft.Bends.Count, "JSON bend count");
        Check(Convert.ToInt32(root["SourceRowCount"]) >= draft.Bends.Count, "Source count retains incomplete inventory evidence");
        Check(Convert.ToInt32(root["CandidateOrderCount"]) == draft.CandidateOrders.Count, "JSON order count");
        var ids = draft.Bends.Select(b => b.Id).ToArray();
        Check(ids.Distinct().Count() == ids.Length, "Unique draft bend IDs even for invalid inventory");
        Check(draft.CandidateOrders.Select(o => string.Join(",", o.BendIds)).Distinct().Count() == draft.CandidateOrders.Count, "Unique orders");
        for (int i = 0; i < draft.CandidateOrders.Count; i++)
        {
            var order = draft.CandidateOrders[i];
            Check(order.Index == i + 1, "Sequential order indices");
            Check(order.Status == "CHUA_XAC_MINH", "A Min pass cannot become a manufacturing pass");
            Check(order.BendIds.OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x)), "Every candidate uses all bends exactly once");
        }
    }
    private static void Suppressed(BendCheckPlanningDraft draft, string reason)
    { Valid(draft); Check(draft.CandidateOrders.Count == 0 && !draft.Exhaustive, "Unreliable inventory cannot enumerate"); Check(draft.Reason.IndexOf(reason, StringComparison.OrdinalIgnoreCase) >= 0, "Specific reason: " + draft.Reason); }
    private static int Main()
    {
        Test("null inventory", () => Suppressed(BendCheckPlanningDraft.Create(null), "No recognized"));
        Test("empty inventory", () => Suppressed(BendCheckPlanningDraft.Create(Rows(0)), "No recognized"));
        for (int count = 1; count <= 9; count++)
        {
            int n = count;
            Test(n + " bends: bounded deterministic permutations", () => {
                var rows = Rows(n); var draft = BendCheckPlanningDraft.Create(rows); Valid(draft);
                int expected = n == 1 ? 1 : n == 2 ? 2 : n == 3 ? 6 : n <= 8 ? 24 : 0;
                Check(draft.Bends.Count == n && draft.CandidateOrders.Count == expected, "Expected candidate count");
                Check(draft.Exhaustive == (n <= 4), "Exhaustive means enumeration only");
                Check(draft.ToJson() == BendCheckPlanningDraft.Create(rows).ToJson(), "Deterministic JSON");
                if (n == 2) Check(draft.CandidateOrders[0].BendIds.Reverse().SequenceEqual(draft.CandidateOrders[1].BendIds), "Both reverse orders");
                if (n > 8) Check(draft.Reason.Contains("eight"), "V0 size limit is explicit");
            });
        }
        Test("source fields, Unicode and JSON escaping", () => {
            var rows = Rows(1); rows[0].FeatureName = "ベース\\ベンド\"\r\nTấm"; rows[0].Kind = "V溝1";
            rows[0].MinimumContext.Read = r => new BendMinimumRowResult { Status = BendMinimumStatus.GroovedAccepted };
            var draft = BendCheckPlanningDraft.Create(rows); Valid(draft); var bend = draft.Bends.Single();
            Check(bend.FeatureId == rows[0].FeatureId && bend.FeatureName == rows[0].FeatureName && bend.Mark == rows[0].Kind, "Metadata preserved");
            Check(bend.AngleDeg == 90 && bend.RadiusMm == 1.5 && bend.ThicknessMm == 2.3 && bend.HasPersistentId, "Numbers and persistence preserved");
            Check(bend.CheckMinStatus == "GroovedAccepted", "Existing groove result preserved");
            var decoded = (Dictionary<string, object>)((object[])((Dictionary<string, object>)Json.DeserializeObject(draft.ToJson()))["Bends"])[0];
            Check(decoded["FeatureName"].ToString() == rows[0].FeatureName, "Escaping round trip");
        });
        Test("Min NG stays separate from planning status", () => {
            var rows = Rows(2); rows[0].MinimumContext.Read = r => new BendMinimumRowResult { Status = BendMinimumStatus.BelowMinimum };
            var draft = BendCheckPlanningDraft.Create(rows); Valid(draft); Check(draft.Bends[0].CheckMinStatus == "BelowMinimum", "Keep Min NG");
        });
        Test("snapshot independent of mutable rows", () => {
            var rows = Rows(2); var draft = BendCheckPlanningDraft.Create(rows); string before = draft.ToJson();
            rows[0].FeatureName = "renamed"; rows[0].Kind = "C溝"; rows[0].BendAngleDeg = 20; rows.Clear();
            Check(draft.ToJson() == before, "No live row references"); draft.CandidateOrders[0].BendIds[0] = "local mutation";
            Check(draft.CandidateOrders[1].BendIds.All(id => id != "local mutation"), "Orders have independent arrays");
        });
        Test("duplicate native feature ID", () => { var rows = Rows(2); rows[1].FeatureId = rows[0].FeatureId; Suppressed(BendCheckPlanningDraft.Create(rows), "Duplicate feature"); });
        Test("null row is explicit, no partial order", () => { var rows = Rows(3); rows[1] = null; Suppressed(BendCheckPlanningDraft.Create(rows), "null"); });
        Test("invalid native feature ID", () => { var rows = Rows(1); rows[0].FeatureId = 0; Suppressed(BendCheckPlanningDraft.Create(rows), "feature ID"); });
        Test("duplicate persistent identity", () => { var rows = Rows(2); rows[1].PersistentId = rows[0].PersistentId; Suppressed(BendCheckPlanningDraft.Create(rows), "persistent"); });
        Test("optional persistent identity", () => { var rows = Rows(2); rows[0].PersistentId = null; var draft = BendCheckPlanningDraft.Create(rows); Valid(draft); Check(!draft.Bends[0].HasPersistentId && draft.CandidateOrders.Count == 2, "Optional persistence does not lose an identified native row"); });
        Test("missing context and metadata", () => {
            var rows = Rows(1); rows[0].MinimumContext = null; rows[0].FeatureName = rows[0].Kind = null;
            rows[0].BendAngleDeg = rows[0].BendRadiusMm = rows[0].ModelThicknessMm = null;
            var draft = BendCheckPlanningDraft.Create(rows); Valid(draft); Check(draft.Bends[0].CheckMinStatus == "CHUA_XAC_MINH", "Unknown Min remains unknown");
        });
        Test("Min evaluation exception does not escape", () => {
            var rows = Rows(1); rows[0].MinimumContext.Read = r => { throw new InvalidOperationException("test evaluator failure"); };
            var draft = BendCheckPlanningDraft.Create(rows); Valid(draft); Check(draft.Bends[0].Warning.Contains("test evaluator failure"), "Diagnostic retained");
        });
        Test("stale shared context suppresses candidates", () => { var rows = Rows(2); rows[0].MinimumContext.Current = false; Suppressed(BendCheckPlanningDraft.Create(rows), "stale"); });
        Test("non-finite API numbers produce strict JSON null", () => {
            var rows = Rows(1); rows[0].BendAngleDeg = double.NaN; rows[0].BendRadiusMm = double.PositiveInfinity; rows[0].ModelThicknessMm = double.NegativeInfinity;
            var draft = BendCheckPlanningDraft.Create(rows); Valid(draft); var bend = draft.Bends[0];
            Check(bend.AngleDeg == null && bend.RadiusMm == null && bend.ThicknessMm == null, "Replace non-finite measurements with null");
            Check(!string.IsNullOrEmpty(bend.Warning), "Bad metadata is explicit");
            Check(!draft.ToJson().Contains(":NaN") && !draft.ToJson().Contains(":Infinity") && !draft.ToJson().Contains(":-Infinity"), "Strict JSON numbers");
        });
        Test("invariant numeric JSON under Vietnamese culture", () => {
            var prior = Thread.CurrentThread.CurrentCulture;
            try { Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN"); var draft = BendCheckPlanningDraft.Create(Rows(1)); Valid(draft); Check(draft.ToJson().Contains("2.3"), "JSON decimal point"); }
            finally { Thread.CurrentThread.CurrentCulture = prior; }
        });
        Console.WriteLine("RESULT " + passed + " passed; " + failed + " failed; " + assertions + " assertions (no SOLIDWORKS API).");
        return failed == 0 ? 0 : 1;
    }
}
