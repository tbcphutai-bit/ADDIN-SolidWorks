// CHECK CHẤN — safe planner for pre-VERIFIED native state transitions.
// C# 7.3 / .NET Framework 4.8, no SolidWorks COM dependency.
// This module does NOT certify whether a transition is physically possible.
// NO caller may turn UNKNOWN or Excel illustrative curves into Certified=true.
using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.UI
{
    internal sealed class BendVerifiedTransition
    {
        internal string BeforeStateId, AfterStateId;
        internal int FeatureId;
        internal string ToolId, VerifiedSetupId; // same ToolId in a different setup can still require re-installation
        internal string SourceRevision, EvidenceReference;
        internal int Reorientations;
        internal bool GeometryVerified, SetupVerified, ProcessVerified, SourceCurrent;
        internal bool IsUsable => GeometryVerified && SetupVerified && ProcessVerified && SourceCurrent &&
            !string.IsNullOrWhiteSpace(EvidenceReference) && !string.IsNullOrWhiteSpace(BeforeStateId) &&
            !string.IsNullOrWhiteSpace(AfterStateId) && !string.IsNullOrWhiteSpace(ToolId) &&
            !string.IsNullOrWhiteSpace(VerifiedSetupId) && Reorientations >= 0;
    }

    internal sealed class BendVerifiedPlanResult
    {
        internal IReadOnlyList<BendVerifiedTransition> Steps;
        internal int SetupChanges, ToolChanges, DistinctTools, Reorientations;
        internal string SourceRevision;
        internal string Explanation => "Phương án chỉ nằm trong đồ thị trạng thái đã được backend chứng minh; " +
            "không thay thế chứng nhận gá và điều kiện sản xuất.";
    }

    // Searches all evidenced prefixes and minimizes (1) install/setup changes,
    // (2) tool swaps, (3) unique tools, (4) reorientations, (5) steps.
    // Never fabricates process states.
    internal static class BendVerifiedToolChangePlanner
    {
        private sealed class Node
        {
            internal string State;
            internal ulong BendMask, ToolMask;
            internal int LastTool;
            internal string LastSetup;
            internal int SetupChanges, Switches, Reorientations, Steps;
            internal Node Prev;
            internal BendVerifiedTransition Via;
            internal long Serial;
            internal int DistinctTools => CountBits(ToolMask);
            internal string Key => State.Length + ":" + State + ":" + BendMask + ":" + ToolMask +
                ":" + LastTool + ":" + (LastSetup == null ? "0:" : LastSetup.Length + ":" + LastSetup);
        }
        private sealed class NodeOrder : IComparer<Node>
        {
            public int Compare(Node a, Node b)
            {
                int c = a.SetupChanges.CompareTo(b.SetupChanges); if (c != 0) return c;
                c = a.Switches.CompareTo(b.Switches); if (c != 0) return c;
                c = a.DistinctTools.CompareTo(b.DistinctTools); if (c != 0) return c;
                c = a.Reorientations.CompareTo(b.Reorientations); if (c != 0) return c;
                c = a.Steps.CompareTo(b.Steps); if (c != 0) return c;
                return a.Serial.CompareTo(b.Serial);
            }
        }
        private static int CountBits(ulong v)
        {
            int result = 0; while (v != 0) { result += (int)(v & 1); v >>= 1; } return result;
        }
        private static bool Better(Node a, Node b) => new NodeOrder().Compare(a, b) < 0;

        internal static bool TryChoose(
            string initialStateId,
            IEnumerable<string> certifiedTerminalStates,
            IEnumerable<int> expectedFeatureIds,
            IEnumerable<BendVerifiedTransition> transitions,
            string requiredSourceRevision,
            out BendVerifiedPlanResult plan,
            out string reason,
            int maxStates = 35000)
        {
            plan = null;
            reason = "Chưa đủ bằng chứng tạo đồ thị trạng thái phôi và dao.";
            if (string.IsNullOrWhiteSpace(initialStateId) || string.IsNullOrWhiteSpace(requiredSourceRevision) ||
                certifiedTerminalStates == null || expectedFeatureIds == null || transitions == null) return false;
            var featureIds = expectedFeatureIds.Distinct().OrderBy(x => x).ToArray();
            var terminals = new HashSet<string>(certifiedTerminalStates.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
            if (featureIds.Length == 0 || featureIds.Length > 20 || terminals.Count == 0) return false;
            if (maxStates < 100 || maxStates > 100000) { reason = "Giới hạn tìm kiếm không hợp lệ."; return false; }
            var edges = transitions.Where(e => e != null && e.IsUsable &&
                string.Equals(e.SourceRevision, requiredSourceRevision, StringComparison.Ordinal) &&
                featureIds.Contains(e.FeatureId)).ToList();
            if (edges.Count == 0) { reason = "Không có chuyển trạng thái nào đã xác minh đầy đủ."; return false; }
            var tools = edges.Select(e => e.ToolId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            if (tools.Length > 18) { reason = "Số dao lớn hơn thư viện nguồn; cần kiểm tra dữ liệu."; return false; }
            var toolIndex = tools.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index, StringComparer.Ordinal);
            var featureIndex = featureIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);
            var adjacency = edges.GroupBy(e => e.BeforeStateId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            var open = new SortedSet<Node>(new NodeOrder());
            var best = new Dictionary<string, Node>(StringComparer.Ordinal);
            long serial = 0;
            var start = new Node { State = initialStateId, LastTool = -1, Serial = ++serial };
            best[start.Key] = start; open.Add(start);
            ulong completedMask = (1UL << featureIds.Length) - 1;
            while (open.Count != 0)
            {
                var current = open.Min; open.Remove(current);
                Node accepted;
                if (!best.TryGetValue(current.Key, out accepted) || !ReferenceEquals(current, accepted)) continue;
                if (terminals.Contains(current.State) && current.BendMask == completedMask)
                {
                    var steps = new List<BendVerifiedTransition>();
                    for (var node = current; node.Via != null; node = node.Prev) steps.Add(node.Via);
                    steps.Reverse();
                    plan = new BendVerifiedPlanResult {
                        Steps = steps.AsReadOnly(), SetupChanges = current.SetupChanges,
                        ToolChanges = current.Switches,
                        DistinctTools = current.DistinctTools, Reorientations = current.Reorientations,
                        SourceRevision = requiredSourceRevision
                    };
                    reason = "Tìm được phương án trên đồ thị có chứng cứ.";
                    return true;
                }
                BendVerifiedTransition[] outgoing;
                if (!adjacency.TryGetValue(current.State, out outgoing)) continue;
                foreach (var edge in outgoing)
                {
                    ulong bit = 1UL << featureIndex[edge.FeatureId];
                    if ((current.BendMask & bit) != 0) continue; // each real bend exactly once
                    int idx = toolIndex[edge.ToolId];
                    ulong tBit = 1UL << idx;
                    var next = new Node {
                        State = edge.AfterStateId,
                        BendMask = current.BendMask | bit,
                        ToolMask = current.ToolMask | tBit,
                        LastTool = idx,
                        LastSetup = edge.VerifiedSetupId,
                        SetupChanges = current.SetupChanges + (current.LastSetup == null ||
                            string.Equals(current.LastSetup, edge.VerifiedSetupId, StringComparison.Ordinal) ? 0 : 1),
                        Switches = current.Switches + (current.LastTool < 0 || current.LastTool == idx ? 0 : 1),
                        Reorientations = current.Reorientations + edge.Reorientations,
                        Steps = current.Steps + 1,
                        Via = edge, Prev = current, Serial = ++serial
                    };
                    Node old;
                    if (best.TryGetValue(next.Key, out old) && !Better(next, old)) continue;
                    best[next.Key] = next; open.Add(next);
                    if (best.Count > maxStates) {
                        reason = "Đã đạt ngân sách tìm kiếm; kết quả UNKNOWN, không chọn phương án phỏng đoán.";
                        return false;
                    }
                }
            }
            reason = "Không có đường đi đầy đủ trong dữ liệu chuyển trạng thái ĐÃ XÁC MINH; " +
                     "không kết luận Part không thể chấn.";
            return false;
        }
    }
}
