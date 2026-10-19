using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

namespace ADDIN.UI
{
    // Bounded navigation: 18 tools -> one tool's retained sequences -> one
    // sequence's native steps -> one step's phase pairs. Never an all-rows dump.
    internal sealed class BendSequencePlanningControl : UserControl
    {
        private readonly DataGridView tools = Grid("sequenceTools"), sequences = Grid("sequenceOptions"), timeline = Grid("sequenceTimeline"), phases = Grid("sequencePhases");
        private readonly Label summary = new Label { Name = "sequenceSummary", Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly TextBox evidence = new TextBox { Name = "sequenceEvidence", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
        private readonly ComboBox filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        private readonly CheckBox pin = new CheckBox { AutoSize = true, Text = "Giữ dao / thứ tự đang xem" };
        private readonly ProcessStatePreview preview = new ProcessStatePreview { Dock = DockStyle.Fill, MinimumSize = new Size(240, 110) };
        private PartPlanningResult result;
        private string pinnedTool, pinnedSequence;
        private string missingReason;
        private string baseEvidence;
        private bool binding;
        internal event EventHandler<int> FeatureSelected;
        internal BendSequencePlanningControl()
        {
            Name = "BendSequencePlanningControl"; Dock = DockStyle.Fill; Font = new Font("Segoe UI", 9F); BackColor = Color.White; AutoScroll = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, MinimumSize = new Size(640, 440), ColumnCount = 2, RowCount = 4, Padding = new Padding(6) };
            AutoScrollMinSize = root.MinimumSize;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
            filter.Items.AddRange(new object[] { "Tất cả 18 dao", "Có chứng minh trong phạm vi", "Cần xác minh", "Bị loại trong điều kiện đã xét" }); filter.SelectedIndex = 0;
            bar.Controls.Add(filter); bar.Controls.Add(pin); root.Controls.Add(bar, 0, 0);
            root.Controls.Add(summary, 1, 0); root.Controls.Add(tools, 0, 1); root.Controls.Add(sequences, 1, 1);
            root.Controls.Add(timeline, 0, 2); root.Controls.Add(phases, 1, 2); root.Controls.Add(evidence, 0, 3); root.Controls.Add(preview, 1, 3);
            Controls.Add(root);
            Columns(tools, "ToolId", "Phạm vi", "Phương án", "Bằng chứng");
            Columns(sequences, "Thứ tự FeatureId", "Process", "Phạm vi / lý do");
            Columns(timeline, "Bước", "FeatureId", "Punch", "Holder");
            Columns(phases, "Phase", "Cặp", "Kết quả", "Witness / lý do");
            filter.SelectedIndexChanged += (s, e) => BindTools();
            // WinForms SelectionChanged precedes CurrentCell replacement.
            // Read CurrentRow only after CurrentCellChanged, or the old order
            // is rendered again when selecting a different sequence.
            tools.CurrentCellChanged += (s, e) => { if (!binding) SelectTool(); };
            sequences.CurrentCellChanged += (s, e) => { if (!binding) SelectSequence(); };
            timeline.CurrentCellChanged += (s, e) => { if (!binding) SelectStep(); };
            SetResult(null, false);
        }
        private static DataGridView Grid(string name) => new DataGridView { Name = name, Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, ShowCellToolTips = true };
        private static void Columns(DataGridView grid, params string[] names)
        {
            foreach (string name in names) { int i = grid.Columns.Add("c" + grid.Columns.Count, name); grid.Columns[i].MinimumWidth = 70; }
            grid.CellToolTipTextNeeded += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0) e.ToolTipText = Convert.ToString(grid[e.ColumnIndex, e.RowIndex].Value); };
        }
        private static T Selected<T>(DataGridView grid) where T : class => grid.CurrentRow?.Tag as T;
        internal void SetResult(PartPlanningResult value, bool sample, string reason = null)
        {
            result = value; missingReason = reason;
            WithDisplay(() => {
                summary.Text = sample ? "PREVIEW · dữ liệu mẫu" : value == null ? "Chưa có dữ liệu quá trình" :
                    (value.EvidenceLevel==SequenceEvidenceLevel.SimulationOnly?"SIMULATION":value.EvidenceLevel==SequenceEvidenceLevel.ReviewedSourceGraph?"REVIEWED":"UNKNOWN") +
                    $" · {value.CommonToolIds.Count}/18 · {value.ElapsedMilliseconds:0} ms · CHƯA XÁC MINH";
                BindToolsCore();
            });
        }
        private static string CoverageLabel(SequenceSearchCoverage coverage) => coverage == SequenceSearchCoverage.ExhaustiveDeclaredSpace ? "Đủ phạm vi nguồn" :
            coverage == SequenceSearchCoverage.Bounded ? "Có giới hạn" : coverage == SequenceSearchCoverage.Interrupted ? "Đã dừng / cũ" : "Thiếu chứng cứ";
        private void WithDisplay(Action render)
        {
            var source = result;
            if (source == null) { render(); return; }
            if (!source.ReadDisplay(render))
            {
                result = null; missingReason = source.InvalidReason ?? "SOURCE_CHANGED_DURING_DISPLAY";
                summary.Text = "STALE · cần Quét lại"; BindToolsCore();
            }
        }
        private void BindTools() => WithDisplay(BindToolsCore);
        private void BindToolsCore()
        {
            binding = true; tools.Rows.Clear(); sequences.Rows.Clear(); timeline.Rows.Clear(); phases.Rows.Clear(); preview.SetStep(null);
            evidence.Text = (missingReason == null ? "" : missingReason + "\r\n") + "Chưa có process-state graph, kinematics và bằng chứng gá dao. Không suy ra thứ tự từ Part đã chấn.";
            try
            {
                foreach (string id in CompanyToolIds.All)
                {
                    var item = result?.Tools.FirstOrDefault(t => t.ToolId == id);
                    if (filter.SelectedIndex > 0 && item != null && (int)item.Decision != filter.SelectedIndex - 1) continue;
                    var reason = result?.Current == false ? result.InvalidReason : item == null ? missingReason ?? "MISSING_PROCESS_STATE_GRAPH_AND_KINEMATIC_SOURCE" : string.Join("; ", item.Reasons);
                    string decision = item?.Decision == SequenceToolDecision.ProvenInCheckedScope ? "Có chứng minh*" :
                        item?.Decision == SequenceToolDecision.RejectedInDeclaredConditions ? "Bị loại*" : "Cần xác minh";
                    int i = tools.Rows.Add(id, item == null ? "Thiếu chứng cứ" : CoverageLabel(item.Coverage), item?.Sequences.Count ?? 0, decision);
                    tools.Rows[i].Tag = item; foreach (DataGridViewCell cell in tools.Rows[i].Cells) cell.ToolTipText = reason;
                }
                if (tools.Rows.Count > 0)
                {
                    var row = pin.Checked ? tools.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => Convert.ToString(r.Cells[0].Value) == pinnedTool) : null;
                    if(row==null&&!pin.Checked&&result?.PreferredCheckedSequence!=null)
                        row=tools.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>Convert.ToString(r.Cells[0].Value)==result.PreferredCheckedSequence.ToolId);
                    row = row ?? tools.Rows[0]; tools.CurrentCell = row.Cells[0]; row.Selected = true;
                }
            }
            finally { binding = false; }
            SelectTool();
        }
        private void SelectTool() => WithDisplay(SelectToolCore);
        private void SelectToolCore()
        {
            var tool = Selected<SequenceToolResult>(tools); pinnedTool = Convert.ToString(tools.CurrentRow?.Cells[0].Value);
            binding = true; sequences.Rows.Clear(); timeline.Rows.Clear(); phases.Rows.Clear(); preview.SetStep(null);
            try
            {
                if (tool == null || result?.Current != true) return;
                foreach (var sequence in tool.Sequences)
                {
                    string ids = string.Join(" → ", sequence.Steps.Select(s => s.FeatureId));
                    int i = sequences.Rows.Add(ids.Length == 0 ? "Chưa có bước" : ids, sequence.Status, sequence.Reason);
                    sequences.Rows[i].Tag = sequence;
                }
                baseEvidence = $"{tool.ToolId}\r\n{tool.Coverage}\r\n{result.EvidenceLevel}\r\n" +
                    "Sản xuất: CHUA_XAC_MINH\r\n" + string.Join("\r\n", tool.Reasons) + "\r\nRevision: " + result.Revision +
                    "\r\nKhông tìm thấy trong search giới hạn không chứng minh Part không chấn được.";
                evidence.Text = baseEvidence;
                if (sequences.Rows.Count > 0)
                {
                    var row = pin.Checked ? sequences.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => Convert.ToString(r.Cells[0].Value) == pinnedSequence) : null;
                    if(row==null&&!pin.Checked&&result.PreferredCheckedSequence?.ToolId==tool.ToolId)
                        row=sequences.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>ReferenceEquals(r.Tag,result.PreferredCheckedSequence));
                    row = row ?? sequences.Rows[0]; sequences.CurrentCell = row.Cells[0];
                }
            }
            finally { binding = false; }
            SelectSequence();
        }
        private void SelectSequence() => WithDisplay(SelectSequenceCore);
        private void SelectSequenceCore()
        {
            var sequence = Selected<BendSequenceResult>(sequences); pinnedSequence = Convert.ToString(sequences.CurrentRow?.Cells[0].Value);
            binding = true; timeline.Rows.Clear(); phases.Rows.Clear(); preview.SetStep(null);
            try
            {
                if (result?.Current != true || sequence == null) return;
                foreach (var step in sequence.Steps)
                { int i = timeline.Rows.Add(timeline.Rows.Count + 1, step.FeatureId, step.Punch, step.Holder); timeline.Rows[i].Tag = step; }
                if (timeline.Rows.Count > 0) timeline.CurrentCell = timeline.Rows[0].Cells[0];
            }
            finally { binding = false; }
            SelectStep();
        }
        private void SelectStep() => WithDisplay(SelectStepCore);
        private void SelectStepCore()
        {
            phases.Rows.Clear(); var step = Selected<SequenceStepResult>(timeline);
            if (result?.Current != true || step == null) { preview.SetStep(null); return; }
            foreach (var pair in step.Pairs)
            {
                string reason = pair.Witness == null ? string.Join("; ", pair.Reasons) : $"u={pair.Witness.Parameter:0.######} · {pair.Witness.PartId} · margin>{pair.Witness.Geometry.CrossSectionInteriorMarginLowerMm:0.######} mm";
                phases.Rows.Add(pair.Phase, pair.Pair == CollisionPairKind.WorkpiecePunch ? "Punch" : "Holder", pair.Status, reason);
            }
            evidence.Text = baseEvidence + "\r\nBước: " + step.FeatureId + " · " + step.Transition.BeforeStateId + " → " + step.Transition.AfterStateId;
            preview.SetStep(step, () => result?.Current == true);
            FeatureSelected?.Invoke(this, step.FeatureId);
        }
        private sealed class ProcessStatePreview : Control
        {
            private SequenceStepResult step;
            private Func<bool> current;
            internal ProcessStatePreview() { DoubleBuffered = true; }
            internal void SetStep(SequenceStepResult value, Func<bool> guard = null) { step = value; current = guard; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e); e.Graphics.Clear(Color.FromArgb(248, 250, 252));
                e.Graphics.DrawString("Trước / giữa / sau · lát 2D minh họa", Font, Brushes.DimGray, 4, 2);
                if (step == null || current?.Invoke() == false || step.Transition.Plan.InvalidatedReason != null) return;
                var segment = step.Transition.Plan.Segments.FirstOrDefault(s => s.Phase == BendProcessPhase.DuringBend);
                if (segment == null) return;
                for (int state = 0; state < 3; state++)
                {
                    double u = state * .5; var curves = segment.Parts.Where(p => p.Source.Domain != null).SelectMany(p => {
                        var material = p.Source.Slice(p.Source.Domain.MidMm).Material;
                        return material == null ? new ToolCurve[0] : material.Contours.SelectMany(c => c.Boundary).Select(p.Motion.At(u).Apply).ToArray();
                    }).ToArray();
                    var points = curves.SelectMany(c => c.Kind == "ARC" ? Enumerable.Range(0, 33).Select(i => CollisionGeometry.Point(c, i / 32.0)) : new[] { c.Start, c.End }).ToArray();
                    if (points.Length == 0) continue;
                    double minX = points.Min(p => p.X), minY = points.Min(p => p.Y), dx = Math.Max(.1, points.Max(p => p.X) - minX), dy = Math.Max(.1, points.Max(p => p.Y) - minY);
                    double width = ClientSize.Width / 3.0, scale = Math.Min((width - 12) / dx, (ClientSize.Height - 42) / dy);
                    Func<ToolPoint, PointF> map = p => new PointF((float)(state * width + 6 + (p.X - minX) * scale), (float)(ClientSize.Height - 18 - (p.Y - minY) * scale));
                    using (var pen = new Pen(Color.SteelBlue, 1.4f)) foreach (var curve in curves)
                    {
                        var line = (curve.Kind == "ARC" ? Enumerable.Range(0, 33).Select(i => CollisionGeometry.Point(curve, i / 32.0)) : new[] { curve.Start, curve.End }).Select(map).ToArray();
                        e.Graphics.DrawLines(pen, line);
                    }
                    e.Graphics.DrawString("u=" + u.ToString("0.0"), Font, Brushes.DimGray, (float)(state * width + 6), ClientSize.Height - 16);
                }
            }
        }
    }
}
