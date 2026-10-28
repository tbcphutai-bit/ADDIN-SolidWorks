// CHECK CHẤN UX V4 — UI-only partial class.
// Target: existing UI/BendAssemblyBatchForm.cs on PR #37, .NET Framework 4.8, C# 7.3, WinForms.
// No SolidWorks COM calls. No geometry/collision calculations. Does not change backend event contracts.
// Install via the exact small insertion points in INSTRUCTIONS/PATCH_POINTS.md.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using SolidWorks.Interop.sldworks;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendTools;
using ADDIN.Commands.BendPlacement;
using System.Threading;
using System.Threading.Tasks;

namespace ADDIN.UI
{
    internal partial class BendAssemblyBatchForm
    {
        private TabControl uxV4Tabs;
        private TabPage uxV45ToolDetailsTab;
        private Label uxV45Decision, uxV45Reason, uxV45Max, uxV45Collision, uxV45ToolData;
        private Button uxV45RecoverSource;
        private TabPage uxV4MainTab, uxV4PlanningTab, uxV4EvidenceTab, uxV4ExcelTab, uxV4TechnicalTab;
        private DataGridView uxV4Parts, uxV4Tools;
        private FlowLayoutPanel uxV4Bends;
        private Label uxV4Status, uxV4PartTitle, uxV4BendTitle, uxV4ToolTitle,
                      uxV4ToolReason, uxV4Plan, uxV4Excel, uxV4PartCounts, uxV4Minimum;
        private UxV4PartPreview uxV4Preview;
        private bool uxV4Binding;
        private ISldWorks uxV4NativeHost;
        // Maps a CURRENT source snapshot to a saved .SLDPRT path supplied by
        // the same-session backend. SourceKey itself is a hash, NOT a path.
        private Func<BatchPartViewModel, string> uxV4SavedPartPathResolver;
        private int uxV4ImageGeneration;
        private readonly List<Button> uxV4BendButtons = new List<Button>();
        private readonly Color uxV4Text = Color.FromArgb(30, 41, 59);
        private readonly Color uxV4Muted = Color.FromArgb(100, 116, 139);
        private readonly Color uxV4Page = Color.FromArgb(247, 249, 252);
        private readonly Color uxV4Border = Color.FromArgb(224, 231, 239);
        private readonly Color uxV4Accent = Color.FromArgb(36, 97, 185);

        // The original detailed controls are *kept*, not destroyed. They are reparented
        // to the Technical tab so the already-tested native UI binding stays intact.
        private void InstallUxV4()
        {
            if (uxV4Tabs != null) return;
            pnlRoot.SuspendLayout();
            try
            {
                uxV4Tabs = new TabControl
                {
                    Name = "checkChanUxV4Tabs", Dock = DockStyle.Fill,
                    Font = new Font("Segoe UI", 9.5f), Padding = new Point(14, 7)
                };
                uxV4MainTab = PageV4("Kết quả");
                uxV45ToolDetailsTab = PageV4("Bộ dao");
                uxV4PlanningTab = PageV4("Thứ tự chấn");
                uxV4EvidenceTab = PageV4("Hồ sơ dao");
                uxV4ExcelTab = PageV4("Giới hạn Excel");
                uxV4TechnicalTab = PageV4("Chi tiết kỹ thuật");

                // Relocate existing working controls, retaining the same instances.
                if (sequenceView.Parent != null) sequenceView.Parent.Controls.Remove(sequenceView);
                if (toolEvidence.Parent != null) toolEvidence.Parent.Controls.Remove(toolEvidence);
                uxV4PlanningTab.Controls.Add(sequenceView);
                uxV4EvidenceTab.Controls.Add(toolEvidence);
                sequenceView.Dock = DockStyle.Fill;
                toolEvidence.Dock = DockStyle.Fill;

                pnlRoot.Controls.Remove(splitMain);
                splitMain.Dock = DockStyle.Fill;
                uxV4TechnicalTab.Controls.Add(splitMain);
                pnlRoot.Controls.Remove(pnlOverview);
                pnlOverview.Visible = false;
                if (pnlRoot.RowStyles.Count > 2)
                {
                    pnlRoot.RowStyles[2].SizeType = SizeType.Absolute;
                    pnlRoot.RowStyles[2].Height = 0;
                }
                if (pnlRoot.RowStyles.Count > 3)
                {
                    pnlRoot.RowStyles[3].SizeType = SizeType.Percent;
                    pnlRoot.RowStyles[3].Height = 100f;
                }
                pnlRoot.Controls.Add(uxV4Tabs, 0, 3);

                BuildUxV4Main();
                InstallUxV45ResultFirst();
                InstallUxV43();  // V4.3: genuine final-Part 2D section, DXF unplaced.
                // Keep illustrative test data out of day-to-day production UI.
                btnToggleMockup.Visible = false;
                BuildUxV4Excel();
                BuildUxV4PlanningCaption();
                uxV4Tabs.TabPages.AddRange(new[] {
                    uxV4MainTab, uxV45ToolDetailsTab, uxV4PlanningTab, uxV4EvidenceTab,
                    uxV4ExcelTab, uxV4TechnicalTab
                });
                uxV4Tabs.SelectedTab = uxV4MainTab;
                UxV4SyncStatus("Chọn phạm vi rồi bấm Bắt đầu kiểm tra.");
                this.Disposed += (s, e) => {
                    uxV4ImageGeneration++;
                    uxV4NativeHost = null; // borrowed RCW: NEVER ReleaseComObject
                    uxV4SavedPartPathResolver = null;
                    if (uxV4Preview != null) uxV4Preview.ClearImage();
                };
            }
            finally { pnlRoot.ResumeLayout(true); }
        }


        // Result-first presentation. Reuses live data grid and event handlers without changing checks.
        private void InstallUxV45ResultFirst()
        {
            if (uxV45Decision != null) return;
            Control card = uxV4Tools?.Parent?.Parent;
            var columns = card?.Parent as TableLayoutPanel;
            if (card == null || columns == null || columns.ColumnStyles.Count != 3)
                throw new InvalidOperationException("CHECK_CHAN_UI_ANCHOR_MISSING");
            columns.SuspendLayout();
            try
            {
                columns.Controls.Remove(card);
                uxV45ToolDetailsTab.Controls.Add(card);
                card.Dock = DockStyle.Fill;
                columns.ColumnStyles[0].Width = 22;
                columns.ColumnStyles[1].Width = 51;
                columns.ColumnStyles[2].Width = 27;

                var result = UxV4Table(1, 10);
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
                result.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                result.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
                result.Controls.Add(UxV4Label("KẾT QUẢ", 10f, true, uxV4Text, 32), 0, 0);
                uxV45Decision = UxV4Label("CHƯA CÓ KẾT QUẢ", 13.5f, true, uxV4Text, 74);
                result.Controls.Add(uxV45Decision, 0, 1);
                uxV45Reason = UxV4Label("Chọn Part và nếp chấn.", 9f, false, uxV4Muted, 62);
                result.Controls.Add(uxV45Reason, 0, 2);
                if (uxV4Minimum.Parent != null)
                    uxV4Minimum.Parent.Controls.Remove(uxV4Minimum);
                result.Controls.Add(uxV4Minimum, 0, 3);
                uxV45Max = UxV4Label("Check MAX: chưa đối chiếu", 9f, false, uxV4Text, 40);
                result.Controls.Add(uxV45Max, 0, 4);
                uxV45Collision = UxV4Label("Va chạm: chưa kiểm tra", 9f, false, uxV4Text, 48);
                result.Controls.Add(uxV45Collision, 0, 5);
                uxV45ToolData = UxV4Label("Hình học dao: chưa có", 8.5f, false, uxV4Muted, 38);
                result.Controls.Add(uxV45ToolData, 0, 6);
                uxV45RecoverSource = UxV4Action("Chọn lại nguồn dao DXF");
                uxV45RecoverSource.Dock = DockStyle.Fill;
                uxV45RecoverSource.Visible = false;
                uxV45RecoverSource.Click += async (sender, args) => await UxV45RecoverSourceAsync();
                result.Controls.Add(uxV45RecoverSource, 0, 7);
                var openDetails = UxV4Action("Xem kết quả từng dao");
                openDetails.Dock = DockStyle.Fill;
                openDetails.Click += (sender, args) => uxV4Tabs.SelectedTab = uxV45ToolDetailsTab;
                result.Controls.Add(openDetails, 0, 9);
                columns.Controls.Add(UxV4Card("uxV45Results", result), 2, 0);

                // Decorative explanations stay in their detail tabs instead of crowding results.
                if (uxV4Preview?.Parent is TableLayoutPanel view && view.RowStyles.Count >= 5)
                {
                    view.RowStyles[3].Height = 58;
                    view.RowStyles[4].Height = 0;
                }
                uxV4Excel.Visible = false;
                if (uxV4Parts?.Parent is TableLayoutPanel parts && parts.RowStyles.Count >= 3)
                    parts.RowStyles[2].Height = 0;
            }
            finally { columns.ResumeLayout(true); }
            UxV45UpdateOutcome();
        }

        // UI projection only; no native I/O, source reload, or numeric rerun.
        private void UxV45UpdateOutcome()
        {
            if(uxV45Decision==null||uxV45Decision.IsDisposed)return;
            var result=BatchCheckOutcome.Project(selectedPart,selectedBend,selectedTool,isRunning,isMockupMode);
            uxV45Decision.Text=result.Caption;
            uxV45Reason.Text=result.Reason;
            uxV45Decision.ForeColor=result.State==BatchOutcomeState.MIN_NG||result.State==BatchOutcomeState.EXCEL_LIMIT_FAIL||
                result.State==BatchOutcomeState.CAD_COLLISION_CONFIRMED?Color.FromArgb(170,48,48):
                result.State==BatchOutcomeState.RUNNING||result.State==BatchOutcomeState.NOT_RUN?uxV4Text:Color.FromArgb(141,98,40);
            bool current=!isRunning&&!isMockupMode&&selectedPart?.RawPartResult?.Current==true;
            uxV45ToolData.Text=current?"Punch "+selectedPart.LoadedPunchCount+"/"+selectedPart.ToolCatalogCount+
                " · ghép CAD "+selectedPart.MatchedCadPairs+" · gá duyệt "+selectedPart.VerifiedMountingPairs:"Hình học dao: chưa có kết quả hiện hành";
            toolTips.SetToolTip(uxV45ToolData,current?"Mã khai báo: "+selectedPart.ToolCatalogCount+"\nPunch đã đọc: "+selectedPart.LoadedPunchCount+
                "\nCặp CAD đối chiếu: "+selectedPart.MatchedCadPairs+"\nCặp có đường bao: "+selectedPart.ReadyGeometryPairs+
                "\nProfile đủ điều kiện hình học: "+selectedPart.CollisionProfileReadyPairs+"\nGá vật lý duyệt: "+selectedPart.VerifiedMountingPairs:
                "Chưa có số liệu hiện hành. Profile đủ điều kiện không thay cho pose, chiều dài hữu dụng và kiểm tra số.");
            uxV45Max.Text=current?BatchCheckOutcome.ExcelText(selectedTool?.ExcelCase):"Check MAX: chưa kiểm tra";
            uxV45Collision.Text=!current?"Va chạm: chưa kiểm tra":result.State==BatchOutcomeState.CAD_COLLISION_CONFIRMED?
                "Va chạm: có bằng chứng số · "+result.Scope:result.State==BatchOutcomeState.PARTIAL_CLEAR?
                "Va chạm: mới đạt một phần · "+result.Scope:"Va chạm: chưa xác minh toàn bộ quá trình";
            UxV45ShowSourceRecovery(current&&!string.IsNullOrWhiteSpace(selectedPart.ToolSourceError));
            toolTips.SetToolTip(uxV45Reason,"Phạm vi: "+result.Scope+"\nNguồn: "+result.Fingerprint);
            toolTips.SetToolTip(uxV45Max,selectedTool?.ExcelCase?.Evidence??"Chưa có dữ liệu Excel gắn với nếp/dao hiện hành.");
        }

        private void UxV45ShowSourceRecovery(bool visible)
        {
            if(uxV45RecoverSource==null||uxV45RecoverSource.IsDisposed)return;
            uxV45RecoverSource.Visible=visible;
            var grid=uxV45RecoverSource.Parent as TableLayoutPanel;
            if(grid!=null&&grid.RowStyles.Count>7)grid.RowStyles[7].Height=visible?43:0;
        }

        private Task UxV45RecoverSourceAsync()
        {
            if(isRunning||IsDisposed||uxV45RecoverSource==null||!uxV45RecoverSource.Enabled)return Task.CompletedTask;
            uxV45RecoverSource.Enabled=false;
            btnRunBatch.Enabled=false;
            var task=ConfigureToolSourcesAsync(this,()=>uxV45ToolData.Text="Đã lưu nguồn; bấm Bắt đầu kiểm tra để đọc snapshot mới.",()=>!isRunning&&!IsDisposed);
            task.ContinueWith(t=>{
                var observed=t.Exception;
                if(IsDisposed||!IsHandleCreated)return;
                try { BeginInvoke(new Action(()=>{if(!IsDisposed&&!uxV45RecoverSource.IsDisposed){uxV45RecoverSource.Enabled=!isRunning;btnRunBatch.Enabled=!isRunning;}})); }
                catch(InvalidOperationException) { }
            },TaskScheduler.Default);
            return task;
        }

        // One opt-in workflow shared by Check Chấn and existing Settings.
        // Every No preserves the existing field. The only write is a backed-up
        // settings JSON after a validated preview and an explicit final Yes.
        internal static Task ConfigureToolSourcesAsync(Control owner,Action saved=null,Func<bool> canProceed=null)
        {
            if(owner==null||owner.IsDisposed||!owner.IsHandleCreated)return Task.CompletedTask;
            if(owner.InvokeRequired)throw new InvalidOperationException("SOURCE_SELECTION_REQUIRES_UI_STA");
            var overrides=ToolProfileSourceSettings.OverrideNames.Where(n=> System.Environment.GetEnvironmentVariable(n)!=null).ToArray();
            if(overrides.Length>0)
            {
                MessageBox.Show(owner,"Biến môi trường đang ưu tiên hơn cấu hình cục bộ:\n"+string.Join("\n",overrides)+
                    "\nHãy kiểm tra và gỡ override nếu muốn lưu nguồn cục bộ. Công cụ không tự sửa environment.",
                    "Nguồn Check Chấn",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                return Task.CompletedTask;
            }
            var settings=new ToolProfileSourceSettings();ToolProfileSourceConfiguration current;
            try { current=settings.Load(); }
            catch(Exception ex)
            {
                if(MessageBox.Show(owner,"Không đọc được cấu hình: "+ex.GetType().Name+" · "+ex.Message+
                    "\nChọn lại nguồn? JSON cũ được sao lưu khi bạn duyệt lưu.","Nguồn Check Chấn",MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return Task.CompletedTask;
                current=new ToolProfileSourceConfiguration();
            }
            var proposed=new ToolProfileSourceConfiguration {Version=1,DirectoryPath=current.DirectoryPath,
                ManifestPath=current.ManifestPath,DatumEvidencePath=current.DatumEvidencePath,
                PlacementConfigurationPath=current.PlacementConfigurationPath,UseMasterPairsAsLibrary=current.UseMasterPairsAsLibrary};
            var mode=MessageBox.Show(owner,"Chế độ hiện tại: "+(proposed.UseMasterPairsAsLibrary?"18 cặp cố định trong Master CAD":"thư mục DXF / manifest")+
                "\nCó đổi sang chế độ còn lại không? No giữ nguyên.","Chế độ nguồn",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
            if(mode==DialogResult.Cancel)return Task.CompletedTask;
            if(mode==DialogResult.Yes)proposed.UseMasterPairsAsLibrary=!proposed.UseMasterPairsAsLibrary;
            if(!proposed.UseMasterPairsAsLibrary)
            {
                bool required=!Directory.Exists(proposed.DirectoryPath);
                var change=required?DialogResult.Yes:MessageBox.Show(owner,"Đổi thư mục DXF? No giữ: "+proposed.DirectoryPath,
                    "Thư mục DXF",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
                if(change==DialogResult.Cancel)return Task.CompletedTask;
                if(change==DialogResult.Yes)using(var folder=new FolderBrowserDialog {Description="Thư mục DXF gốc (chỉ đọc)",SelectedPath=required?"":proposed.DirectoryPath})
                {if(folder.ShowDialog(owner)!=DialogResult.OK)return Task.CompletedTask;proposed.DirectoryPath=folder.SelectedPath;}
            }
            Func<string,string,bool,string> choose=(caption,existing,required)=>{
                var answer=required?DialogResult.Yes:MessageBox.Show(owner,"Đổi "+caption+"? No giữ nguyên:\n"+(existing??"Chưa cấu hình"),
                    "Nguồn Check Chấn",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);
                if(answer==DialogResult.Cancel)throw new OperationCanceledException();
                if(answer!=DialogResult.Yes)return existing;
                using(var file=new OpenFileDialog {Title=caption,Filter="JSON (*.json)|*.json",CheckFileExists=true,Multiselect=false})
                {if(file.ShowDialog(owner)!=DialogResult.OK)throw new OperationCanceledException();return file.FileName;}
            };
            try
            {
                if(!proposed.UseMasterPairsAsLibrary)
                {
                    proposed.ManifestPath=choose("manifest Punch–Holder",proposed.ManifestPath,false);
                    proposed.DatumEvidencePath=choose("hồ sơ mốc dao",proposed.DatumEvidencePath,false);
                }
                proposed.PlacementConfigurationPath=choose("cấu hình Master / gá / Excel",proposed.PlacementConfigurationPath,
                    proposed.UseMasterPairsAsLibrary&&string.IsNullOrWhiteSpace(proposed.PlacementConfigurationPath));
            }
            catch(OperationCanceledException){return Task.CompletedTask;}
            var completed=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lifetime=new CancellationTokenSource();var token=lifetime.Token;
            var provider=new ToolProfileLibraryProvider(()=>proposed);
            var lifecycle=new object();Task worker=null;int finished=0;
            Action finish=null;EventHandler closed=null;
            closed=(s,e)=>{
                Task wait;
                lock(lifecycle){if(finished!=0)return;lifetime.Cancel();wait=worker??Task.CompletedTask;}
                wait.ContinueWith(t=>{var observed=t.Exception;finish();},TaskScheduler.Default);
            };
            owner.Disposed+=closed;
            finish=()=>{
                lock(lifecycle){if(finished!=0)return;finished=1;}
                if(!owner.IsDisposed)owner.Disposed-=closed;
                // Called only after a worker completes; no native/UI resources
                // or live COM references are owned by this provider.
                provider.Dispose();lifetime.Dispose();completed.TrySetResult(true);
            };
            Action<Action> publish=action=>{
                if(Volatile.Read(ref finished)!=0||owner.IsDisposed||!owner.IsHandleCreated){finish();return;}
                try {owner.BeginInvoke(new Action(()=>{if(Volatile.Read(ref finished)!=0||owner.IsDisposed||canProceed?.Invoke()==false){finish();return;}action();}));}
                catch(InvalidOperationException){finish();}
            };
            var reading=Task.Run(()=>provider.Read(token),token);worker=reading;
            reading.ContinueWith(read=>{
                var observed=read.Exception;
                publish(()=>{
                    if(read.IsCanceled){finish();return;}
                    if(read.IsFaulted)
                    {
                        MessageBox.Show(owner,read.Exception.GetBaseException().Message,"Chưa lưu nguồn",MessageBoxButtons.OK,MessageBoxIcon.Warning);finish();return;
                    }
                    var preview=read.Result;
                    string problem=preview.SelectionProblem(proposed);
                    if(problem!=null){MessageBox.Show(owner,problem,"Nguồn chưa hợp lệ",MessageBoxButtons.OK,MessageBoxIcon.Warning);finish();return;}
                    string summary="Chế độ: "+(proposed.UseMasterPairsAsLibrary?"18 cặp cố định":"DXF / manifest")+
                        "\nSettings: "+ToolProfileSourceSettings.DefaultSettingsPath+
                        "\nThư mục: "+proposed.DirectoryPath+"\nManifest: "+proposed.ManifestPath+
                        "\nPlacement: "+proposed.PlacementConfigurationPath+
                        "\nKhai báo: "+preview.Tools.Count+" · Punch đọc: "+preview.Tools.Count(t=>t.Validation.PunchGeometryReadComplete)+
                        " · cặp: "+preview.PlacementSources.Tools.Count(t=>t.CadPair?.Matched==true)+
                        " · gá xác minh: "+preview.PlacementSources.Tools.Count(t=>t.ClampVerified)+
                        "\nSHA nguồn hiệu lực: "+preview.EffectiveSources?.Fingerprint+
                        "\n\nThiếu mapping Excel / mốc gá / quá trình vẫn UNKNOWN. KIK hở được giữ nguyên."+
                        "\nViệc lưu sẽ làm hết hiệu lực kết quả đang mở ở nguồn cũ.\nLưu cấu hình cục bộ này (có sao lưu)?";
                    if(MessageBox.Show(owner,summary,"Xem nguồn trước khi lưu",MessageBoxButtons.YesNo,MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2)!=DialogResult.Yes){finish();return;}
                    if(token.IsCancellationRequested){finish();return;}
                    Task saving;
                    lock(lifecycle)
                    {
                        if(finished!=0||token.IsCancellationRequested)return;
                        saving=Task.Run(()=>settings.SaveApprovedSelection(proposed,preview,token),token);worker=saving;
                    }
                    saving.ContinueWith(save=>{
                        var saveError=save.Exception;
                        publish(()=>{
                        try
                        {
                            if(save.IsCanceled)return;
                            if(save.IsFaulted){MessageBox.Show(owner,save.Exception.GetBaseException().Message,"Chưa lưu nguồn",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}
                            saved?.Invoke();
                            MessageBox.Show(owner,"Đã lưu nguồn. Bấm Bắt đầu kiểm tra để đọc dữ liệu hiện hành. Chưa xác minh sản xuất.",
                                "Nguồn Check Chấn",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        }
                        finally {finish();}
                        });
                    },TaskScheduler.Default);
                });
            },TaskScheduler.Default);
            return completed.Task;
        }

        private TabPage PageV4(string caption)
        {
            return new TabPage(caption) { BackColor = uxV4Page, Padding = new Padding(8), UseVisualStyleBackColor = false };
        }

        private static Label UxV4Label(string text, float size, bool bold, Color color, int height)
        {
            return new Label {
                Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color, TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill, AutoEllipsis = true,
                Height = height, Margin = new Padding(3)
            };
        }

        private static TableLayoutPanel UxV4Table(int columns, int rows)
        {
            var panel = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows,
                BackColor = Color.White, Margin = Padding.Empty, Padding = new Padding(0)
            };
            return panel;
        }

        private Panel UxV4Card(string name, Control content)
        {
            var card = new Panel {
                Name = name, BackColor = Color.White, Dock = DockStyle.Fill,
                Margin = new Padding(5), Padding = new Padding(8)
            };
            card.Paint += (s, e) => {
                var r = card.ClientRectangle;
                if (r.Width > 1 && r.Height > 1)
                using (var pen = new Pen(uxV4Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            };
            content.Dock = DockStyle.Fill;
            card.Controls.Add(content);
            return card;
        }

        private static DataGridView UxV4Grid(string name, params string[] columns)
        {
            var grid = new DataGridView {
                Name = name, Dock = DockStyle.Fill, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None, ReadOnly = true, MultiSelect = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false, ColumnHeadersHeight = 34,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 34 }, GridColor = Color.FromArgb(236, 240, 245),
                Font = new Font("Segoe UI", 9f),
                DefaultCellStyle = {
                    SelectionBackColor = Color.FromArgb(229, 239, 253),
                    SelectionForeColor = Color.FromArgb(23, 50, 86),
                    BackColor = Color.White, ForeColor = Color.FromArgb(30, 41, 59),
                    Padding = new Padding(4, 0, 4, 0)
                },
                ColumnHeadersDefaultCellStyle = {
                    BackColor = Color.FromArgb(242, 246, 250),
                    ForeColor = Color.FromArgb(71, 85, 105),
                    Font = new Font("Segoe UI", 8.75f, FontStyle.Bold)
                }
            };
            foreach (string caption in columns)
            {
                int i = grid.Columns.Add("c" + grid.Columns.Count, caption);
                grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            return grid;
        }

        private void BuildUxV4Main()
        {
            var root = UxV4Table(1, 3); root.BackColor = uxV4Page;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            uxV4MainTab.Controls.Add(root);

            uxV4PartCounts = UxV4Label("Chưa có kết quả từ CAD", 9.1f, false, uxV4Muted, 35);
            uxV4PartCounts.Padding = new Padding(8, 0, 8, 0);
            root.Controls.Add(uxV4PartCounts, 0, 0);

            var columns = UxV4Table(3, 1); columns.BackColor = uxV4Page;
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(columns, 0, 1);

            // LEFT: selected part inventory
            var parts = UxV4Table(1, 3);
            parts.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            parts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            parts.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            parts.Controls.Add(UxV4Label("1  CHỌN CHI TIẾT", 10f, true, uxV4Text, 40), 0, 0);
            uxV4Parts = UxV4Grid("uxV4Parts", "Chi tiết", "Nếp", "SL");
            uxV4Parts.Columns[0].FillWeight = 66;
            uxV4Parts.Columns[1].FillWeight = 17;
            uxV4Parts.Columns[2].FillWeight = 17;
            parts.Controls.Add(uxV4Parts, 0, 1);
            var partNote = UxV4Label("Theo Part, cấu hình và nhóm occurrence.", 8.2f, false, uxV4Muted, 34);
            parts.Controls.Add(partNote, 0, 2);
            columns.Controls.Add(UxV4Card("uxV4PartsCard", parts), 0, 0);

            // CENTER: actual 3D must come from source CAD, never a fabricated contour.
            var view = UxV4Table(1, 5);
            view.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            view.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            view.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            view.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            view.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            uxV4PartTitle = UxV4Label("2  XEM PART VÀ NẾP CHẤN", 10f, true, uxV4Text, 38);
            view.Controls.Add(uxV4PartTitle, 0, 0);
            uxV4Preview = new UxV4PartPreview { Dock = DockStyle.Fill };
            view.Controls.Add(uxV4Preview, 0, 1);
            uxV4BendTitle = UxV4Label("Chọn Part để xem các nếp", 9f, true, uxV4Text, 36);
            view.Controls.Add(uxV4BendTitle, 0, 2);
            uxV4Bends = new FlowLayoutPanel {
                Name = "uxV4BendButtons", Dock = DockStyle.Fill, AutoScroll = true,
                BackColor = Color.White, Padding = new Padding(6, 4, 6, 3),
                WrapContents = true, FlowDirection = FlowDirection.LeftToRight
            };
            view.Controls.Add(uxV4Bends, 0, 3);
            uxV4Excel = UxV4Label("Excel MAX: chọn nếp và xem Giới hạn Excel; chưa gán họ khuôn vào mã dao.",
                8.25f, false, uxV4Muted, 35);
            view.Controls.Add(uxV4Excel, 0, 4);
            columns.Controls.Add(UxV4Card("uxV4ViewCard", view), 1, 0);

            // RIGHT: concise 18 candidates; no fake tool recommendation.
            var tools = UxV4Table(1, 8);
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            tools.Controls.Add(UxV4Label("3  KIỂM TRA 18 BỘ DAO", 10f, true, uxV4Text, 38), 0, 0);
            uxV4ToolTitle = UxV4Label("Chưa chọn nếp", 9.2f, true, uxV4Accent, 42);
            tools.Controls.Add(uxV4ToolTitle, 0, 1);
            uxV4Minimum = UxV4Label("Check Min: Chưa có dữ liệu của nếp đang chọn.", 8.6f, false, uxV4Muted, 30);
            tools.Controls.Add(uxV4Minimum, 0, 2);
            uxV4Tools = UxV4Grid("uxV4ToolCandidates", "Bộ dao", "Hình học", "Quá trình");
            uxV4Tools.Columns[0].FillWeight = 37;
            uxV4Tools.Columns[1].FillWeight = 34;
            uxV4Tools.Columns[2].FillWeight = 29;
            uxV4Tools.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) uxV4Tabs.SelectedTab = uxV4TechnicalTab; };
            tools.Controls.Add(uxV4Tools, 0, 3);
            tools.Controls.Add(UxV4Label("GIẢI THÍCH KẾT QUẢ", 8.8f, true, uxV4Muted, 34), 0, 4);
            uxV4ToolReason = UxV4Label("Chưa có bộ dao đang chọn.", 8.6f, false, uxV4Muted, 56);
            uxV4ToolReason.MaximumSize = new Size(0, 0);
            tools.Controls.Add(uxV4ToolReason, 0, 5);
            uxV4Plan = UxV4Label("Thứ tự chấn: Chưa có phương án được xác minh.", 8.6f, false, uxV4Muted, 54);
            tools.Controls.Add(uxV4Plan, 0, 6);
            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var planButton = UxV4Action("Xem thứ tự chấn");
            planButton.Click += (s, e) => uxV4Tabs.SelectedTab = uxV4PlanningTab;
            var toolButton = UxV4Action("Hồ sơ dao");
            toolButton.Click += (s, e) => uxV4Tabs.SelectedTab = uxV4EvidenceTab;
            buttonRow.Controls.Add(planButton); buttonRow.Controls.Add(toolButton);
            tools.Controls.Add(buttonRow, 0, 7);
            columns.Controls.Add(UxV4Card("uxV4ToolsCard", tools), 2, 0);

            uxV4Status = UxV4Label("Chọn phạm vi rồi bấm Bắt đầu kiểm tra.", 9f, false, uxV4Text, 37);
            uxV4Status.BackColor = Color.White;
            uxV4Status.Padding = new Padding(10, 0, 10, 0);
            root.Controls.Add(uxV4Status, 0, 2);

            uxV4Parts.CurrentCellChanged += (s, e) => { if (!uxV4Binding) UxV4SelectPart(); };
            uxV4Tools.CurrentCellChanged += (s, e) => { if (!uxV4Binding) UxV4SelectTool(); };
        }

        private Button UxV4Action(string caption)
        {
            return new Button {
                Text = caption, AutoSize = true, Height = 30,
                Padding = new Padding(10, 1, 10, 1), FlatStyle = FlatStyle.Flat,
                BackColor = Color.White, ForeColor = uxV4Accent,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = new Padding(3, 4, 3, 4)
            };
        }

        private void BuildUxV4PlanningCaption()
        {
            var note = new Label {
                Text = "Thứ tự chấn và lựa chọn bộ dao phải theo bằng chứng phôi, gá và chuyển động. " +
                    "Chưa có chứng cứ không đồng nghĩa đã chọn được thứ tự ít thay dao nhất.",
                Dock = DockStyle.Top, Height = 43, AutoEllipsis = true,
                Padding = new Padding(10, 4, 10, 4), BackColor = Color.White,
                ForeColor = uxV4Muted, Font = new Font("Segoe UI", 9f)
            };
            uxV4PlanningTab.Controls.Add(note);
            note.BringToFront();
        }

        private void BuildUxV4Excel()
        {
            var container = UxV4Table(1, 3); container.BackColor = uxV4Page;
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            container.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            uxV4ExcelTab.Controls.Add(container);
            container.Controls.Add(UxV4Label(
                "NGUỒN EXCEL GỐC — 深曲げ限界(4).xlsx\r\n" +
                "Các con số dưới đây là chiều dài hỗ trợ theo HỌ KHUÔN; không phải Lmax của cánh hay kết luận không va chạm.",
                9.4f, true, uxV4Text, 70), 0, 0);
            var grid = UxV4Grid("uxV4ExcelFamilies", "Họ khuôn", "Hệ", "Chiều dài tham chiếu", "Ô gốc");
            grid.Columns[0].FillWeight = 36; grid.Columns[1].FillWeight = 15;
            grid.Columns[2].FillWeight = 25; grid.Columns[3].FillWeight = 24;
            foreach (var row in BendExcelReferenceV4.KnownFamilies)
            {
                var i = grid.Rows.Add(row.Family, row.System, row.ReferenceLengthMm.ToString("0") + " mm", row.CellRange);
                grid.Rows[i].Tag = row;
            }
            container.Controls.Add(grid, 0, 1);
            container.Controls.Add(UxV4Label(
                "Hình W/L trong Excel thể hiện vùng giới hạn phụ thuộc mặt cắt và tư thế gá. " +
                "Không suy ra tự động hình ghép Punch–Holder–Part từ ảnh. " +
                "Chưa có mapping được duyệt từng mã dao → họ khuôn/đường bao giới hạn, không phát sinh PASS/CLEAR/RECOMMENDED. " +
                "Trạng thái sản xuất: CHƯA XÁC MINH.", 9f, false, uxV4Muted, 86), 0, 2);
        }

        // Called only at stable points: never call each native Pump iteration.
        private void UxV4RefreshParts()
        {
            if (uxV4Parts == null || IsDisposed) return;
            uxV4Binding = true;
            uxV4Parts.SuspendLayout();
            try
            {
                var keep = selectedPart?.SelectionKey ?? preservePart;
                uxV4Parts.Rows.Clear();
                foreach (var part in displayedParts)
                {
                    int i = uxV4Parts.Rows.Add(part.PartName, part.NativeInventoryKnown?(object)part.BendCount:"? / "+part.BendCount, part.Quantity);
                    var row = uxV4Parts.Rows[i];
                    row.Tag = part;
                    row.Cells[0].ToolTipText = (part.Configuration ?? "") + "\n" + (part.Reason ?? "");
                    if (part.Status == CollisionStatus.COLLISION)
                        row.Cells[0].Style.ForeColor = Color.FromArgb(168, 53, 48);
                }
                var match = uxV4Parts.Rows.Cast<DataGridViewRow>().FirstOrDefault(
                    r => string.Equals((r.Tag as BatchPartViewModel)?.SelectionKey, keep, StringComparison.Ordinal));
                if (match == null && uxV4Parts.Rows.Count > 0) match = uxV4Parts.Rows[0];
                if (match != null)
                {
                    uxV4Parts.CurrentCell = match.Cells[0];
                    match.Selected = true;
                }
                else uxV4Parts.ClearSelection();
                uxV4PartCounts.Text = allParts.Count == 0 ? "Chưa có kết quả" :
                    allParts.Count + " Part · " + allParts.Sum(p => p.BendCount) + " nếp chấn";
            }
            finally { uxV4Parts.ResumeLayout(); uxV4Binding = false; }
            if (selectedPart == null && uxV4Parts.CurrentRow?.Tag is BatchPartViewModel emptyPart &&
                (emptyPart.Bends == null || emptyPart.Bends.Count == 0)) selectedPart = emptyPart;
            UxV4RefreshSelection();
        }

        private void UxV4SelectPart()
        {
            var row = uxV4Parts?.CurrentRow;
            var part = row?.Tag as BatchPartViewModel;
            if (part == null || !allParts.Contains(part)) return;
            selectedPart = part;
            preservePart = part.SelectionKey;
            // Reuse the original paginated grid so diagnostics and selections stay synchronized.
            int index = displayedParts.FindIndex(p => ReferenceEquals(p, part));
            if (index >= 0 && index / PartsPerPage != partPage)
            {
                partPage = index / PartsPerPage;
                PopulatePartsGrid();
            }
            var oldRow = dgvParts.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => ReferenceEquals(r.Tag, part));
            if (oldRow != null)
            {
                dgvParts.CurrentCell = oldRow.Cells[0];
                oldRow.Selected = true;
            }
            selectedPart = part;
            PartSelected?.Invoke(this, part);
            RenderPartDetail(part);  // reuse legacy binder, Check Min, selection and tool evidence
            if (part.Bends == null || part.Bends.Count == 0) selectedPart = part;
            UxV4RefreshSelection();
            UxV4RequestSavedFilePreview(part);
        }

        private void UxV4SelectBend(BatchBendViewModel bend)
        {
            if (uxV4Binding || bend == null || selectedPart == null) return;
            SelectBend(bend); // highlights REAL FeatureId in SOLIDWORKS via existing event
            UxV4RefreshSelection();
        }

        private void UxV4SelectTool()
        {
            var tool = uxV4Tools?.CurrentRow?.Tag as BatchToolCandidateViewModel;
            if (tool == null || selectedBend == null || selectedPart == null) return;
            selectedTool = tool;
            preserveTool = tool.ToolId;
            var nativeRow = dgvTools.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => ReferenceEquals(r.Tag, tool));
            if (nativeRow != null) { dgvTools.CurrentCell = nativeRow.Cells[0]; nativeRow.Selected = true; }
            RenderToolCallout(tool); // reuse genuine evidence editor, no recalculation
            UxV4RefreshToolStatus();
        }

        private void UxV4RefreshSelection()
        {
            if (uxV4Parts == null || uxV4Binding || IsDisposed) return;
            uxV4Binding = true;
            try
            {
                uxV4Bends.SuspendLayout();
                uxV4Bends.Controls.Clear();
                foreach (var previous in uxV4BendButtons) previous.Dispose();
                uxV4BendButtons.Clear();
                if (selectedPart == null || !allParts.Contains(selectedPart))
                {
                    uxV4PartTitle.Text = "Chọn chi tiết";
                    uxV4BendTitle.Text = "Chọn Part để xem các nếp";
                    uxV4Preview.SetPart(null, null, false);
                    UxV43ClearSection();
                    uxV4Tools.Rows.Clear();
                    uxV4ToolTitle.Text = "Chưa chọn nếp";
                    uxV4Minimum.Text = "Check Min: Chưa có dữ liệu của nếp đang chọn.";
                    uxV4ToolReason.Text = "Chưa có bộ dao đang chọn.";
                    uxV4Plan.Text = "Thứ tự chấn: Chưa có phương án được xác minh.";
                    UxV45UpdateOutcome();
                    return;
                }
                uxV4PartTitle.Text = selectedPart.PartName;
                uxV4BendTitle.Text = "Chọn nếp chấn (" + selectedPart.Bends.Count + ")";
                uxV4Preview.SetPart(selectedPart.PartName, selectedPart.SelectionKey,
                    selectedPart.RawPartResult?.Current == true && !isMockupMode);
                foreach (var bend in selectedPart.Bends)
                {
                    var local = bend;
                    var button = new Button {
                        Text = bend.BendIndex > 0 ? "B" + bend.BendIndex : bend.BendName,
                        AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat,
                        BackColor = ReferenceEquals(bend, selectedBend) ? Color.FromArgb(223, 236, 252) : Color.White,
                        ForeColor = ReferenceEquals(bend, selectedBend) ? uxV4Accent : uxV4Text,
                        Font = new Font("Segoe UI", 8.5f, ReferenceEquals(bend, selectedBend) ? FontStyle.Bold : FontStyle.Regular),
                        Padding = new Padding(7, 1, 7, 1), Margin = new Padding(2, 2, 2, 2), Tag = bend
                    };
                    button.Click += (s, e) => UxV4SelectBend(local);
                    toolTips.SetToolTip(button, "Nếp thật · FeatureId " + bend.FeatureId + " · " + bend.AngleDisplay + " · " + bend.RadiusDisplay);
                    uxV4BendButtons.Add(button);
                    uxV4Bends.Controls.Add(button);
                }
                UxV4RebindTools();
                var planning = selectedPart.RawPartResult?.Planning;
                if (planning != null && planning.Current && planning.PreferredCheckedSequence != null)
                {
                    var seq = planning.PreferredCheckedSequence;
                    uxV4Plan.Text = "Có chuỗi được xét: " + seq.ToolId + " · " + seq.Steps.Count +
                        " bước. Mở 'Thứ tự chấn' để xem bằng chứng. Chưa xác minh SX.";
                }
                else
                    uxV4Plan.Text = "Chưa có chuỗi chấn đầy đủ được xác minh; không tự xếp thứ tự hay tuyên bố ít thay dao.";
            }
            finally { uxV4Bends.ResumeLayout(true); uxV4Binding = false; }
            UxV4RefreshToolStatus();
        }

        private void UxV4RebindTools()
        {
            uxV4Tools.Rows.Clear();
            uxV4ToolTitle.Text = selectedBend == null ? "Chưa chọn nếp" :
                selectedBend.BendName + " · " + selectedBend.AngleDisplay + " · " + selectedBend.RadiusDisplay;
            var meta = selectedPart?.RawPartResult?.BendMetadata?
                .FirstOrDefault(m => m.FeatureId == selectedBend?.FeatureId);
            uxV4Minimum.Text = meta == null ? "Check Min: Chưa có dữ liệu của nếp đang chọn." :
                "Check Min: " + (meta.MinimumStatus == "MeetsMinimum" ? "Đạt Min" :
                meta.MinimumStatus == "BelowMinimum" ? "Không đạt Min" :
                meta.MinimumStatus == "GroovedAccepted" ? "Cạnh bào" : "Chưa xác minh");
            if (selectedBend?.ToolCandidates == null) return;
            foreach (var tool in selectedBend.ToolCandidates)
            {
                int i = uxV4Tools.Rows.Add(tool.ToolId, tool.GeometricStatusDisplay, tool.MotionDisplay);
                var row = uxV4Tools.Rows[i];
                row.Tag = tool;
                row.Cells[0].ToolTipText = tool.CombinedReason;
                if (tool.GeometricStatus == CollisionStatus.COLLISION || tool.ProcessStatus == CollisionStatus.COLLISION)
                    row.Cells[1].Style.ForeColor = Color.FromArgb(190, 45, 45);
                else if (tool.GeometricStatus == CollisionStatus.UNKNOWN || tool.ProcessStatus == CollisionStatus.UNKNOWN)
                    row.Cells[1].Style.ForeColor = uxV4Muted;
            }
            var found = uxV4Tools.Rows.Cast<DataGridViewRow>().FirstOrDefault(
                r => ReferenceEquals(r.Tag, selectedTool));
            if (found == null && uxV4Tools.Rows.Count > 0) found = uxV4Tools.Rows[0];
            if (found != null) { uxV4Tools.CurrentCell = found.Cells[0]; found.Selected = true; }
        }

        private void UxV4RefreshToolStatus()
        {
            UxV43UpdateSection();
            UxV45UpdateOutcome();
            if (uxV4ToolReason == null) return;
            var tool = selectedTool;
            if (tool == null)
            {
                uxV4ToolReason.Text = "Chưa có bộ dao đang chọn.";
                return;
            }
            string reason = BendCheckDisplayText.Reasons(tool.CombinedReason, false);
            uxV4ToolReason.Text = "Đang xem " + tool.ToolId + " (không phải dao được khuyên dùng) · " + (string.IsNullOrWhiteSpace(reason) ?
                "Cần xem bằng chứng gá và chuyển động." : reason);
            uxV4Excel.Text=BatchCheckOutcome.ExcelText(tool.ExcelCase);
            toolTips.SetToolTip(uxV4ToolReason,tool.CombinedReason+"\nExcel: "+tool.ExcelCase?.Evidence);
        }

        private void UxV4SyncStatus(string message)
        {
            if (uxV4Status == null || IsDisposed) return;
            uxV4Status.Text = string.IsNullOrWhiteSpace(message) ? "Chưa có thông tin trạng thái." : message;
            uxV4Status.ForeColor = uxV4Text;
        }

        private void UxV4SyncRunning(bool running)
        {
            if (uxV4Parts == null || IsDisposed) return;
            uxV4Parts.Enabled = !running;
            uxV4Tools.Enabled = !running;
            uxV4Bends.Enabled = !running;
            if (running) UxV43ClearSection();
            UxV45UpdateOutcome();
            if (!running && selectedPart != null)
            {
                UxV4RequestSavedFilePreview(selectedPart);
                // Rebind the genuine final-Part section when RunAsync completes.
                // The previous V4.3 bridge cleared it during Run and could leave
                // the selected B1 blank until the user clicked the bend again.
                UxV43UpdateSection();
            }
        }

        // Both launchers use the same controller. It installs a borrowed host and
        // read-only path resolver before initiating any Run. Do not release RCWs.
        internal void UxV4SetSolidWorksApp(ISldWorks existingHost)
        {
            uxV4NativeHost = existingHost;
        }
        internal void UxV4SetSavedPartPathResolver(Func<BatchPartViewModel, string> resolver)
        {
            uxV4SavedPartPathResolver = resolver;
        }

        // On-demand *saved-file* thumbnail. NOT the active unsaved assembly-cut state,
        // NOT a CNC tool/Part collision overlay, and NOT an approved manufacturing setup.
        // SOLIDWORKS GetPreviewBitmapFile runs only on the owning WinForms/COM STA.
        // This method never saves/modifies the Part or Assembly itself.
        private void UxV4RequestSavedFilePreview(BatchPartViewModel part)
        {
            int ticket = ++uxV4ImageGeneration;
            if (part == null || uxV4NativeHost == null || isRunning || isMockupMode ||
                part.RawPartResult?.Current != true || !IsHandleCreated) return;
            string key = part.SelectionKey;
            string path;
            try
            {
                // Do not parse identity hashes, and do not search CAD folders
                // by PartName (duplicates/configuration mismatch risk).
                path = uxV4SavedPartPathResolver == null ? null : uxV4SavedPartPathResolver(part);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("[CHECK CHAN PREVIEW] Path resolution: " +
                    ex.GetType().Name + " - " + ex.Message);
                return;
            }
            string cfg = part.Configuration;
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path) || string.IsNullOrWhiteSpace(cfg)) return;
            BeginInvoke((Action)(() => {
                if (ticket != uxV4ImageGeneration || IsDisposed || isRunning || isMockupMode ||
                    selectedPart?.RawPartResult?.Current != true ||
                    !string.Equals(selectedPart.SelectionKey, key, StringComparison.Ordinal)) return;
                string temp = Path.Combine(Path.GetTempPath(), "check-chan-part-preview-" + Guid.NewGuid().ToString("N") + ".bmp");
                try
                {
                    if (!uxV4NativeHost.GetPreviewBitmapFile(path, cfg, temp) || !File.Exists(temp)) return;
                    // Load and clone so the temporary file can be removed promptly.
                    using (var bitmap = Image.FromFile(temp))
                        uxV4Preview.SetImage(new Bitmap(bitmap));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine("[CHECK CHAN PREVIEW] Saved thumbnail unavailable: " + ex.GetType().Name);
                    // Preview failure must NOT invalidate check data or cause another native run.
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }));
        }

        // Optional *read-only* CAD bitmap entry point. Not called unless a separate,
        // independently reviewed source adapter supplies real, same-session Part imagery.
        // Never pass an artist's 2D approximation or a cached image from another configuration.
        internal void UxV4SupplyVerifiedPartImage(string selectionKey, Image cadImage)
        {
            if (InvokeRequired)
                throw new InvalidOperationException("UI bitmap binding must happen on WinForms STA thread.");
            if (uxV4Preview == null || selectedPart?.RawPartResult?.Current != true || isMockupMode ||
                !string.Equals(selectedPart.SelectionKey, selectionKey, StringComparison.Ordinal)) return;
            uxV4Preview.SetImage(cadImage == null ? null : (Image)cadImage.Clone());
        }

        private sealed class UxV4PartPreview : Control
        {
            private Image image;
            private string name, identity;
            private bool sourceCurrent;
            internal UxV4PartPreview()
            {
                DoubleBuffered = true; BackColor = Color.FromArgb(248, 250, 253);
                Font = new Font("Segoe UI", 9f);
            }
            internal void ClearImage()
            {
                var previous = image; image = null; previous?.Dispose(); Invalidate();
            }
            internal void SetPart(string partName, string sourceIdentity, bool current)
            {
                if (!string.Equals(identity, sourceIdentity, StringComparison.Ordinal) || !current) ClearImage();
                identity = sourceIdentity; name = partName; sourceCurrent = current; Invalidate();
            }
            internal void SetImage(Image partImage)
            {
                ClearImage(); image = partImage; Invalidate();
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = ClientRectangle;
                if (r.Width < 30 || r.Height < 30) return;
                using (var pen = new Pen(Color.FromArgb(216, 225, 236)))
                    g.DrawRectangle(pen, 8, 8, r.Width - 17, r.Height - 17);
                if (image != null && sourceCurrent)
                {
                    float k = Math.Min((r.Width - 38f) / image.Width, (r.Height - 63f) / image.Height);
                    float w = image.Width * k, h = image.Height * k;
                    g.DrawImage(image, (r.Width - w) / 2f, (r.Height - h) / 2f - 10f, w, h);
                    using (var brush = new SolidBrush(Color.FromArgb(60, 75, 94)))
                        g.DrawString("Ảnh lưu theo file/cấu hình — có thể cũ nếu Part chưa Save; KHÔNG phải gá dao", Font, brush, 17, r.Height - 30);
                    return;
                }
                using (var titleFont = new Font("Segoe UI", 11f, FontStyle.Bold))
                using (var minor = new SolidBrush(Color.FromArgb(96, 112, 131)))
                using (var major = new SolidBrush(Color.FromArgb(35, 57, 82)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString(name ?? "Chưa chọn Part", titleFont, major,
                        new RectangleF(30, r.Height * .30f, r.Width - 60, 33), sf);
                    g.DrawString(sourceCurrent ?
                        "Xem Part 3D trong SOLIDWORKS; chọn nếp bên dưới để làm sáng Feature." :
                        "Chưa có ảnh CAD hợp lệ của Part hiện tại.",
                        Font, minor, new RectangleF(25, r.Height * .49f, r.Width - 50, 58), sf);
                    g.DrawString("Không vẽ biên dạng giả và không suy ra tư thế ghép dao.",
                        Font, minor, new RectangleF(25, r.Height - 46, r.Width - 50, 30), sf);
                }
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) { var previous = image; image = null; previous?.Dispose(); }
                base.Dispose(disposing);
            }
        }
    }
}
