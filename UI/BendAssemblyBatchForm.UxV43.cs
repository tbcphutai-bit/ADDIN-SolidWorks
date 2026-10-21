// CHECK CHAN V4.4 - direct update of existing UX V4.3 bridge; Excel-first UX.
// No SolidWorks COM calls, no CAD save, no mutation of check/planning/collision data.
// Unregistered source DXF stays in memory but is NOT overlaid on Part.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendTools;

namespace ADDIN.UI
{
    internal partial class BendAssemblyBatchForm
    {
        private TabControl uxV43PreviewTabs;
        private BendSection2DCanvas uxV43Section;
        // Reference selection is for viewing provenance only, NOT proof that
        // an Excel case applies to this ToolId and this bend.
        private ComboBox uxV44ExcelCase;
        private Label uxV44EvidenceLabel;
        private string uxV43SelectionKey;
        private int uxV43FeatureId;
        private string uxV43ToolId;

        private void InstallUxV43()
        {
            if (uxV43PreviewTabs != null) return;
            if (uxV4Preview == null || !(uxV4Preview.Parent is TableLayoutPanel))
                throw new InvalidOperationException("UX43_PREVIEW_PARENT_UNEXPECTED");
            var parent = (TableLayoutPanel)uxV4Preview.Parent;
            TableLayoutPanelCellPosition cell = parent.GetPositionFromControl(uxV4Preview);
            parent.SuspendLayout();
            try
            {
                uxV43PreviewTabs = new TabControl {
                    Name = "uxV43PreviewModes", Dock = DockStyle.Fill,
                    Font = new System.Drawing.Font("Segoe UI", 9f),
                    Margin = System.Windows.Forms.Padding.Empty
                };
                var sectionPage = new TabPage("Excel trước · Mặt cắt 2D") { Padding = System.Windows.Forms.Padding.Empty };
                var thumbnailPage = new TabPage("Ảnh Part đã lưu") { Padding = System.Windows.Forms.Padding.Empty };
                var sectionLayout = new TableLayoutPanel {
                    Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                    Padding = new Padding(0), Margin = Padding.Empty
                };
                sectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
                sectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var excelBar = new TableLayoutPanel {
                    Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                    BackColor = System.Drawing.Color.FromArgb(245, 248, 252)
                };
                excelBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
                excelBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                uxV44ExcelCase = new ComboBox {
                    Name = "checkChanExcelReferenceOnlyV44", Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Margin = new Padding(5, 9, 5, 8)
                };
                uxV44ExcelCase.Items.Add("Tự dò Excel (chưa xác minh)");
                foreach (var item in BendExcelReferenceV4.KnownFamilies)
                    uxV44ExcelCase.Items.Add(item.Family + " · " +
                        item.ReferenceLengthMm.ToString("0") + " mm");
                uxV44ExcelCase.SelectedIndex = 0;
                uxV44EvidenceLabel = new Label {
                    Dock = DockStyle.Fill, AutoEllipsis = true,
                    ForeColor = System.Drawing.Color.FromArgb(132, 92, 35),
                    TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                    Text = "Trường hợp Excel: chưa có mapping đã duyệt → UNKNOWN"
                };
                excelBar.Controls.Add(uxV44ExcelCase, 0, 0);
                excelBar.Controls.Add(uxV44EvidenceLabel, 1, 0);
                uxV43Section = new BendSection2DCanvas();
                sectionLayout.Controls.Add(excelBar, 0, 0);
                sectionLayout.Controls.Add(uxV43Section, 0, 1);
                sectionPage.Controls.Add(sectionLayout);
                uxV44ExcelCase.SelectedIndexChanged += (sender, ev) => UxV43UpdateSection();
                parent.Controls.Remove(uxV4Preview);
                uxV4Preview.Dock = DockStyle.Fill;
                thumbnailPage.Controls.Add(uxV4Preview);
                uxV43PreviewTabs.TabPages.Add(sectionPage);
                uxV43PreviewTabs.TabPages.Add(thumbnailPage);
                parent.Controls.Add(uxV43PreviewTabs, cell.Column, cell.Row);
                uxV43PreviewTabs.SelectedTab = sectionPage;
            }
            finally { parent.ResumeLayout(true); }
        }

        private void UxV43ClearSection()
        {
            uxV43SelectionKey = null;
            uxV43FeatureId = 0;
            uxV43ToolId = null;
            if (uxV43Section != null && !uxV43Section.IsDisposed)
                uxV43Section.SetScene(null);
            if (uxV44EvidenceLabel != null && !uxV44EvidenceLabel.IsDisposed)
                uxV44EvidenceLabel.Text = "EXCEL TRƯỚC · Đang chờ dữ liệu hợp lệ";
        }

        // Invoked ONLY by existing V4.2 selection UI; no new native operations.
        // The final folded Part cross-section is NOT a pre-bend/mid-bend state.
        private void UxV43UpdateSection()
        {
            if (uxV43Section == null || uxV43Section.IsDisposed) return;
            if (isRunning || isMockupMode || selectedPart?.RawPartResult?.Current != true ||
                selectedBend == null || selectedPart.Bends == null ||
                !selectedPart.Bends.Contains(selectedBend))
            { UxV43ClearSection(); return; }
            var part = selectedPart;
            var bend = selectedBend;
            var tool = selectedTool != null && selectedTool.FeatureId == bend.FeatureId &&
                bend.ToolCandidates != null && bend.ToolCandidates.Contains(selectedTool) ? selectedTool : null;
            var data = new BendSection2DScene {
                PartTitle = (part.PartName ?? "Part") + " / " + (bend.BendName ?? "Bend") + " · FeatureId " + bend.FeatureId,
                ToolTitle = tool?.ToolId ?? "Chưa chọn bộ dao",
                SourceKey = part.SelectionKey,
                FeatureId = bend.FeatureId,
                ToolId = tool?.ToolId,
                ExcelCaseCaption = "Tự dò Excel: chưa có điều kiện/mapping đã duyệt",
                ExcelCaseSource = BendExcelReferenceV4.WorkbookName,
                DecisionText = "CHƯA XÁC MINH · Chưa thể xác nhận trường hợp Excel áp dụng"
            };
            // A user-selected family here is a REFERENCE VIEW, not an automatic
            // tool match, not a scored fit and not a production decision.
            int choice = uxV44ExcelCase == null ? 0 : uxV44ExcelCase.SelectedIndex;
            if (choice > 0 && choice <= BendExcelReferenceV4.KnownFamilies.Count)
            {
                var family = BendExcelReferenceV4.KnownFamilies[choice - 1];
                data.ExcelCaseCaption = "THAM KHẢO: " + family.Family +
                    " · " + family.ReferenceLengthMm.ToString("0") +
                    " mm · " + family.CellRange;
                data.ExcelCaseSource = BendExcelReferenceV4.WorkbookName +
                    " · " + family.CellRange;
            }
            if (uxV44EvidenceLabel != null && !uxV44EvidenceLabel.IsDisposed)
                uxV44EvidenceLabel.Text = "Chưa duyệt Excel → ToolId/W-L · CAD fallback chưa có chứng cứ";
            try
            {
                // These envelopes are from THIS snapshot, not a file-name lookup.
                // Fast precheck can carry UnknownCoverage; the model's read-only
                // final-part Sections may still provide a supported section.
                var envelopes = (part.RawPartResult.ReviewEnvelopes ?? new SheetEnvelopeCapture[0])
                    .Where(e => e != null && e.Current && e.Binding != null &&
                         e.Binding.FeatureId == bend.FeatureId && e.Binding.Model != null &&
                         e.Binding.Model.Lease.Current).ToArray();
                if (envelopes.Length == 1)
                {
                    var env = envelopes[0];
                    var model = env.Binding.Model;
                    var sections = model.Sections.Where(s => s != null && s.FeatureId == bend.FeatureId).ToArray();
                    if (sections.Length == 1 &&
                        sections[0].Coverage == SheetCoverage.CompleteUniformExtrusion &&
                        sections[0].Boundary != null && sections[0].Boundary.Count > 0)
                    {
                        data.Part = BendSection2DScene.CopyCurves(sections[0].Boundary);
                        data.PartEvidence = "Mặt cắt hình học cuối · " + sections[0].Frame.FrameId;
                    }
                    else
                    {
                        // A finite observed slice may be drawn, but NEVER
                        // claimed to prove continuous coverage or manufacturing.
                        var observed = env.ObservedSlices.FirstOrDefault(s => s != null &&
                            s.Material != null && s.Material.Valid && s.Material.Contours != null &&
                            s.Material.Contours.Any(c => c?.Boundary?.Count > 0));
                        if (observed != null)
                        {
                            data.Part = BendSection2DScene.CopyCurves(observed.Material.Contours
                                .Where(c => c != null).SelectMany(c => c.Boundary));
                            data.PartEvidence = "Lát cắt đo được tại z = " + observed.AxialMm.ToString("0.###") +
                                " mm; không chứng nhận toàn bộ chiều dài.";
                        }
                        else data.PartEvidence = "Không có mặt cắt hợp lệ tại nếp này. " +
                            "Model không hỗ trợ tiết diện uniform hoặc thiếu lát cắt native.";
                    }
                }
                else data.PartEvidence = envelopes.Length == 0 ?
                    "Snapshot này chưa có nguồn mặt cắt native cho FeatureId đã chọn." :
                    "Nhiều nguồn mặt cắt trùng FeatureId: không vẽ để tránh chọn sai Part.";
            }
            catch (Exception ex)
            {
                data.Part = new System.Drawing.PointF[0][];
                data.PartEvidence = "Không đọc được mặt cắt: " + ex.GetType().Name;
                System.Diagnostics.Trace.WriteLine("[CHECK CHAN SECTION 2D] " + ex);
            }

            if (tool != null)
            {
                try
                {
                    var cad = tool.CadAssembly;
                    // This is reference source DXF. The single canvas deliberately
                    // refuses to overlay it until a reviewed placement transform
                    // and a trustworthy contact witness are provided. No fake red.
                    if (cad?.Punch?.Curves != null)
                        data.Punch = BendSection2DScene.CopyCurves(cad.Punch.Curves);
                    if (cad?.Holder?.Curves != null)
                        data.Holder = BendSection2DScene.CopyCurves(cad.Holder.Curves);
                    data.ToolEvidence = "Chưa đăng ký tọa độ gá đã duyệt. " +
                        "Không được coi cạnh gần dao là va chạm.";
                }
                catch (Exception ex)
                {
                    data.Punch = new System.Drawing.PointF[0][];
                    data.Holder = new System.Drawing.PointF[0][];
                    data.ToolEvidence = "Lỗi đọc đường bao dao: " + ex.GetType().Name;
                    System.Diagnostics.Trace.WriteLine("[CHECK CHAN TOOL SECTION 2D] " + ex);
                }
            }
            uxV43SelectionKey = data.SourceKey;
            uxV43FeatureId = data.FeatureId;
            uxV43ToolId = data.ToolId;
            uxV43Section.SetScene(data);
        }
    }
}
