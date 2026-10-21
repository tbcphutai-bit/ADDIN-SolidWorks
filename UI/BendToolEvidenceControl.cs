using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.UI
{
    // A pane in the existing Check Chấn window, not a command. All geometry is
    // managed and read-only. Viewer fit changes pixels, never CAD coordinates.
    internal sealed class BendToolEvidenceControl : UserControl
    {
        private readonly Label status=new Label {Dock=DockStyle.Top,Height=42,AutoEllipsis=true};
        private readonly DataGridView entities=new DataGridView {Name="holderRoleEntities",Dock=DockStyle.Fill,AllowUserToAddRows=false,
            RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=true,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells};
        private readonly TextBox editor=new TextBox {Name="unsignedToolEvidence",Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,MaxLength=4*1024*1024};
        private readonly TextBox detail=new TextBox {Dock=DockStyle.Bottom,Height=54,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        private readonly ToolReferenceCanvas canvas=new ToolReferenceCanvas {Dock=DockStyle.Fill};
        private HolderMaterialReviewDraft draft;
        private ToolEvidenceReviewProposal proposal;
        private SheetEnvelopeCapture nativeSheet;
        private string importRoot;
        private bool binding;
        private bool dirty,gridEdited,textEdited;
        private string activeContext;
        private readonly string recoveryRoot;
        private readonly Dictionary<string,string> savedContexts=new Dictionary<string,string>(StringComparer.Ordinal);
        private Form host;
        private readonly ComboBox groups=new ComboBox {Name="holderConnectedGroups",DropDownStyle=ComboBoxStyle.DropDownList,Width=180};
        internal Func<string,bool> ConfirmAction {get;set;}
        internal string LastRecoveryPath {get;private set;}
        internal bool HasUnsavedChanges=>dirty;
        private readonly CancellationTokenSource preparationLifetime=new CancellationTokenSource();
        private bool preparationDisposed;
        internal HolderMaterialReviewDraft Draft => draft;
        internal string ProposalText {get=>editor.Text;set=>editor.Text=value;}
        internal BendToolEvidenceControl(string privateRecoveryDirectory=null)
        {
            recoveryRoot=privateRecoveryDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ADDIN","BendTools","PrivateReviewDrafts");
            ConfirmAction=message=>MessageBox.Show(this,message,"Xác nhận phân loại chưa xác minh",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;
            Dock=DockStyle.Fill;Name="bendToolEvidence";
            var tabs=new TabControl {Dock=DockStyle.Fill};var geometry=new TabPage("Hình học / Phân loại");var input=new TabPage("Nội dung hồ sơ kỹ thuật");
            var split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Vertical};
            split.Panel1.Controls.Add(canvas);split.Panel2.Controls.Add(entities);geometry.Controls.Add(split);input.Controls.Add(editor);
            var zoomBar=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
            foreach(string mode in new[]{"Holder","Punch","All","Selected"})
            {string fit=mode;var b=new Button {Text=mode=="Holder"?"Đầu kẹp chày":mode=="Punch"?"Chày chấn":mode=="All"?"Toàn bộ":"Đường đang chọn",AutoSize=true};b.Click+=(s,e)=>canvas.Fit(fit);zoomBar.Controls.Add(b);}
            split.Panel1.Controls.Add(zoomBar);
            foreach(string text in new[]{"Mã đường CAD","Loại","Lớp CAD","Hình học","Sai số (mm)"})
            {int i=entities.Columns.Add("e"+entities.Columns.Count,text);entities.Columns[i].ReadOnly=true;}
            var roles=new System.Collections.Generic.Dictionary<string,string> {{"NeedsReview","Chưa phân loại"},{"Solid","Vật liệu"},{"Hole","Lỗ"},{"Island","Vùng vật liệu trong lỗ"},{"Decorative","Minh họa"},{"Centerline","Đường tâm"},{"Reference","Tham khảo"},{"Annotation","Ghi chú"}};
            entities.Columns.Add(new DataGridViewComboBoxColumn {Name="role",HeaderText="Vai trò đề xuất",DisplayMember="Value",ValueMember="Key",DataSource=roles.ToArray()});
            entities.Columns.Add("contour","Mã đường bao");entities.Columns.Add("parent","Đường bao cha");
            entities.Columns[0].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;entities.Columns[0].Width=76;
            entities.DataError+=(s,e)=>{e.ThrowException=false;detail.Text="Vai trò không hợp lệ.";};
            entities.SelectionChanged+=(s,e)=> {canvas.SelectedKey=entities.CurrentRow?.Tag as string;canvas.SelectedKeys=new HashSet<string>(entities.SelectedRows.Cast<DataGridViewRow>().Select(r=>Convert.ToString(r.Tag)),StringComparer.Ordinal);canvas.Invalidate();};
            entities.CellValueChanged+=(s,e)=>{if(!binding){dirty=gridEdited=true;UpdatePreview();}};
            editor.TextChanged+=(s,e)=>{if(!binding)dirty=textEdited=true;};
            canvas.EntityPicked+=(key,extend)=>{if(!extend)entities.ClearSelection();var row=entities.Rows.Cast<DataGridViewRow>().SingleOrDefault(r=>Convert.ToString(r.Tag)==key);if(row!=null){row.Selected=true;entities.FirstDisplayedScrollingRowIndex=row.Index;}};
            entities.CellToolTipTextNeeded+=(s,e)=> {if(e.RowIndex>=0&&e.ColumnIndex>=0)e.ToolTipText=e.ColumnIndex==0?
                Convert.ToString(entities.Rows[e.RowIndex].Tag):Convert.ToString(entities[e.ColumnIndex,e.RowIndex].Value);};
            tabs.TabPages.AddRange(new[]{geometry,input});
            var bulk=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=true};
            bulk.Controls.Add(groups);AddButton(bulk,"selectHolderConnectedGroup","Chọn nhóm liên thông",SelectConnectedGroup);
            var bulkRole=new ComboBox {Name="holderBulkRole",DropDownStyle=ComboBoxStyle.DropDownList,Width=145,DisplayMember="Value",ValueMember="Key",DataSource=new[]{new KeyValuePair<string,string>("","Chọn vai trò…")}.Concat(roles.Where(r=>r.Key!="NeedsReview")).ToArray()};
            var contour=new TextBox {Name="holderBulkContour",Width=100};var parent=new TextBox {Name="holderBulkParent",Width=100};
            bulk.Controls.Add(new Label {Text="Gán cho đường đã chọn:",AutoSize=true});bulk.Controls.Add(bulkRole);
            bulk.Controls.Add(new Label {Text="Mã đường bao",AutoSize=true});bulk.Controls.Add(contour);bulk.Controls.Add(new Label {Text="Mã đường bao cha",AutoSize=true});bulk.Controls.Add(parent);
            AddButton(bulk,"applySelectedHolderRoles","Gán và xác nhận",()=>ApplySelectionRoles(Convert.ToString(bulkRole.SelectedValue),contour.Text,parent.Text));geometry.Controls.Add(bulk);
            var actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,AutoSize=true,WrapContents=true};
            AddButton(actions,"applyHolderRoles","Áp dụng vai trò",()=>ApplyRoles());
            AddButton(actions,"loadToolProposal","Nhập hồ sơ",()=>LoadProposal());
            AddButton(actions,"forkToolProposal","Tạo bản nháp để sửa",ForkImported);
            AddButton(actions,"saveHolderEditorDraft","Giữ bản nháp",()=>TryPreserve());
            AddButton(actions,"auditToolEvidence","Kiểm tra chứng cứ",()=>AuditProposal());
            AddAsyncButton(actions,"exportToolProposal","Xuất chưa ký",ExportProposalAsync);
            AddButton(actions,"exportProcessTemplate","Xuất quy trình trống",()=>ExportProcessTemplate());
            Controls.Add(tabs);Controls.Add(detail);Controls.Add(actions);Controls.Add(status);
        }
        protected override void OnHandleCreated(EventArgs e)
        {base.OnHandleCreated(e);host=FindForm();if(host!=null)host.FormClosing+=ProtectClosing;}
        protected override void OnHandleDestroyed(EventArgs e)
        {if(host!=null)host.FormClosing-=ProtectClosing;host=null;base.OnHandleDestroyed(e);}
        private void ProtectClosing(object sender,FormClosingEventArgs e){if(!e.Cancel&&!TryPreserve())e.Cancel=true;}
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!preparationDisposed)
            {
                if(!IsDisposed)TryPreserve();preparationDisposed=true;
                try{preparationLifetime.Cancel();}finally{preparationLifetime.Dispose();}
            }
            base.Dispose(disposing);
        }
        private void AddButton(Control parent,string name,string text,Action action)
        {
            var b=new Button {Name=name,Text=text,AutoSize=true};parent.Controls.Add(b);
            b.Click+=(s,e)=>{try {action();}catch(Exception ex) when(ex is InvalidOperationException||ex is ArgumentException||ex is IOException||ex is UnauthorizedAccessException)
                {detail.Text="Chưa thực hiện được thao tác · "+BendCheckDisplayText.Reasons(ex.Message)+"\r\nChi tiết kỹ thuật: "+ex.Message;}};
        }
        private void AddAsyncButton(Control parent,string name,string text,Func<Task> action)
        {
            var button=new Button {Name=name,Text=text,AutoSize=true};parent.Controls.Add(button);
            button.Click+=async(s,e)=>
            {
                button.Enabled=false;
                try{await action();}
                catch(OperationCanceledException){if(!IsDisposed)detail.Text="Đã hủy chuẩn bị hồ sơ / nguồn đã đổi.";}
                catch(Exception ex) when(ex is InvalidOperationException||ex is ArgumentException||ex is IOException||ex is UnauthorizedAccessException)
                {if(!IsDisposed)detail.Text="Chưa thực hiện được thao tác · "+BendCheckDisplayText.Reasons(ex.Message)+"\r\nChi tiết kỹ thuật: "+ex.Message;}
                finally{if(!button.IsDisposed)button.Enabled=true;}
            };
        }
        internal void ShowTool(ToolAssemblyProfile source,SheetEnvelopeCapture sheet,Func<bool> current)
        {
            if(!TryPreserve())return;
            var next=source?.CadPair?.Matched==true&&current?.Invoke()==true?HolderMaterialReviewDraftBuilder.Create(source,CancellationToken.None,current):null;
            if(next!=null&&draft?.Current==true&&activeContext==HolderReviewEditorData.ContextKey(next,sheet))return;
            binding=true;
            try {draft=null;proposal=null;activeContext=null;nativeSheet=sheet;entities.Rows.Clear();editor.Clear();detail.Clear();canvas.ShowSource(null,null);importRoot=null;groups.Items.Clear();gridEdited=textEdited=dirty=false;
                if(next==null){status.Text="Chưa đủ dữ liệu · Chọn dao từ kết quả còn hiệu lực. Các nháp đã giữ riêng.";return;}
                draft=next;activeContext=HolderReviewEditorData.ContextKey(draft,sheet);
                proposal=ToolEvidenceReviewWorkspace.Create(draft,sheet);editor.Text=ToolEvidenceReviewWorkspace.Serialize(proposal);
                FillAssignments(HolderReviewEditorData.ReadAssignments(draft,proposal));
                foreach(var keys in draft.Topology.SourceEntityGroups)groups.Items.Add("Nhóm "+(groups.Items.Count+1)+" · "+keys.Count+" đường");
                if(groups.Items.Count>0)groups.SelectedIndex=0;
                string saved;
                if(savedContexts.TryGetValue(activeContext,out saved))
                {
                    var recovery=HolderReviewEditorData.ReadRecovery(File.ReadAllText(saved),draft);
                    if(recovery.NativeContext!=HolderReviewEditorData.NativeContext(sheet))throw new InvalidDataException("STALE_NATIVE_EDITOR_CONTEXT");
                    editor.Text=recovery.Text;importRoot=recovery.ImportRoot;FillAssignments(recovery.Assignments);gridEdited=recovery.GridEdited;textEdited=recovery.TextEdited;
                    try {proposal=ToolEvidenceReviewWorkspace.Read(editor.Text,draft);}catch(Exception ex) when(ex is ArgumentException||ex is InvalidDataException||ex is InvalidOperationException){proposal=null;}
                    detail.Text="Đã khôi phục đúng bản nháp của nguồn/chi tiết/nếp này.";
                }
                SetImportedLock();canvas.ShowSource(source,draft);UpdatePreview();
            } finally {binding=false;}
            status.Text=source.Id+" · "+(draft.Status=="MATERIAL_REVIEWED"?"Đã kiểm tra phân loại vật liệu":"Cần phân loại vật liệu đầu kẹp chày")+" · "+draft.Rows.Count+" đường · "+draft.ConnectedComponents+" nhóm liên thông\n"+
                "CAD tham khảo / tọa độ trình bày · Chưa xác minh sản xuất";
            detail.AppendText(Environment.NewLine+BendCheckDisplayText.Reasons(string.Join(";",draft.MissingReasons))+Environment.NewLine+"Bản nháp nguồn/phiên bản khác được giữ riêng, không tự áp dụng.");
        }
        private List<HolderReviewAssignment> Assignments()=>entities.Rows.Cast<DataGridViewRow>().Select(r=>new HolderReviewAssignment {EntityKey=(string)r.Tag,Role=Convert.ToString(r.Cells[5].Value),ContourId=Convert.ToString(r.Cells[6].Value),ParentId=string.IsNullOrWhiteSpace(Convert.ToString(r.Cells[7].Value))?null:Convert.ToString(r.Cells[7].Value)}).ToList();
        private void FillAssignments(IEnumerable<HolderReviewAssignment> input)
        {
            var map=input.ToDictionary(r=>r.EntityKey,StringComparer.Ordinal);entities.Rows.Clear();
            foreach(var source in draft.Rows)
            {
                var row=map[source.EntityKey];int i=entities.Rows.Add(source.Handle,source.SourceType,source.Layer,source.GeometrySupported?"Đọc được":BendCheckDisplayText.Reasons(source.UnsupportedReason),
                    source.BoundaryApproximationErrorMm.ToString("R"),row.Role,row.ContourId,row.ParentId);entities.Rows[i].Tag=source.EntityKey;
            }
        }
        private void SetImportedLock()
        {
            bool locked=proposal!=null&&(HolderReviewEditorData.HasAttestation(proposal)||ForeignNative(proposal));editor.ReadOnly=locked;
            foreach(int column in new[]{5,6,7})entities.Columns[column].ReadOnly=locked;
        }
        private bool TryPreserve()
        {
            if(!dirty||draft==null||binding||entities.IsDisposed)return true;
            try
            {
                entities.EndEdit();LastRecoveryPath=HolderReviewEditorData.SaveRecovery(recoveryRoot,new HolderReviewRecovery {SourcePin=HolderReviewEditorData.SourcePin(draft),NativeContext=HolderReviewEditorData.NativeContext(nativeSheet),
                    Text=editor.Text,ImportRoot=importRoot,Assignments=Assignments(),GridEdited=gridEdited,TextEdited=textEdited});
                savedContexts[activeContext]=LastRecoveryPath;dirty=false;
                detail.Text="Đã giữ bản nháp riêng, chưa ký và chưa áp dụng: "+LastRecoveryPath;return true;
            }
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException)
            {detail.Text="Chưa lưu được bản nháp; giữ nguyên hồ sơ đang sửa. "+ex.Message;return false;}
        }
        private void RequireEditable()
        {
            if(draft==null||!draft.Current||nativeSheet?.Current==false)throw new InvalidDataException("STALE_OR_MISSING_EDITOR_SOURCE");
            var p=ToolEvidenceReviewWorkspace.Read(editor.Text,draft);
            if(HolderReviewEditorData.HasAttestation(p)||ForeignNative(p))throw new InvalidDataException("FORK_REVIEWED_OR_FOREIGN_NATIVE_INPUT_BEFORE_EDITING");
        }
        private bool ForeignNative(ToolEvidenceReviewProposal p)=>p.Axial?.Any(r=>r==null||nativeSheet==null||r.ModelSnapshotId!=nativeSheet.Binding.ModelSnapshotId||
            r.ModelUpdateStamp!=nativeSheet.Binding.UpdateStamp||r.Configuration!=nativeSheet.Binding.Configuration||r.FeatureId!=nativeSheet.Binding.FeatureId||r.FrameId!=nativeSheet.Binding.FrameId)==true;
        private void UpdatePreview()
        {
            if(draft==null)return;var rows=Assignments();canvas.ProposedRoles=rows.ToDictionary(r=>r.EntityKey,r=>r.Role,StringComparer.Ordinal);
            canvas.SelectedKeys=new HashSet<string>(entities.SelectedRows.Cast<DataGridViewRow>().Select(r=>Convert.ToString(r.Tag)),StringComparer.Ordinal);canvas.Invalidate();
        }
        private void SelectConnectedGroup()
        {
            if(draft==null||groups.SelectedIndex<0)return;
            var keys=new HashSet<string>(draft.Topology.SourceEntityGroups[groups.SelectedIndex],StringComparer.Ordinal);entities.ClearSelection();
            foreach(DataGridViewRow row in entities.Rows)if(keys.Contains(Convert.ToString(row.Tag)))row.Selected=true;
        }
        internal bool ApplySelectionRoles(string role,string contourId,string parentId)
        {
            RequireEditable();var selected=entities.SelectedRows.Cast<DataGridViewRow>().ToArray();
            if(selected.Length==0||!new[]{"Solid","Hole","Island","Decorative","Centerline","Reference","Annotation"}.Contains(role))throw new InvalidDataException("SELECT_ENTITIES_AND_EXPLICIT_ROLE");
            bool material=new[]{"Solid","Hole","Island"}.Contains(role);
            if(material&&string.IsNullOrWhiteSpace(contourId))throw new InvalidDataException("CONTOUR_ID_REQUIRED");
            if(ConfirmAction?.Invoke("Gán vai trò "+role+" cho "+selected.Length+" đường nguyên gốc của "+draft.ToolId+"? Đây chỉ là đề xuất chưa xác minh.")!=true)return false;
            binding=true;try {foreach(var row in selected){row.Cells[5].Value=role;row.Cells[6].Value=material?contourId:null;row.Cells[7].Value=material&&!string.IsNullOrWhiteSpace(parentId)?parentId:null;}}
            finally{binding=false;}dirty=gridEdited=true;UpdatePreview();detail.Text="Đã gán đề xuất cho "+selected.Length+" đường. Chưa xác minh; cần kiểm tra toàn bộ trước khi xuất.";return true;
        }
        private void ForkImported()
        {
            if(draft==null||!draft.Current)throw new InvalidDataException("SOURCE_STALE");
            var original=ToolEvidenceReviewWorkspace.Read(editor.Text,draft);
            if(ConfirmAction?.Invoke("Tạo bản chỉnh sửa chưa ký? Bản gốc và chứng cứ cũ được giữ riêng; mọi chữ ký/xác nhận trong bản chỉnh sửa bị loại bỏ và phải được xét duyệt lại."+
                (ForeignNative(original)?" Hồ sơ thuộc nếp/chi tiết hoặc lần đọc khác: chỉ sao chép phân loại đường của đúng nguồn CAD, không sao chép dữ liệu gá, chiều dài hoặc xác nhận phôi.":""))!=true)return;
            if(!TryPreserve())return;
            if(ForeignNative(original))
            {
                // Explicitly confirmed copy of source-bound roles only. No
                // rebase of seating, axial lengths or prior Part approvals.
                proposal=ToolEvidenceReviewWorkspace.Create(draft,nativeSheet);proposal.Material=HolderReviewEditorData.ForkUnsigned(draft,original).Material;importRoot=null;
            }
            else proposal=HolderReviewEditorData.ForkUnsigned(draft,original);
            binding=true;
            try{editor.Text=ToolEvidenceReviewWorkspace.Serialize(proposal);FillAssignments(HolderReviewEditorData.ReadAssignments(draft,proposal));SetImportedLock();}
            finally{binding=false;}dirty=true;gridEdited=textEdited=false;UpdatePreview();
        }
        internal void ApplyRoles()
        {
            if(binding||draft==null)throw new InvalidOperationException("MISSING_CURRENT_TOOL");
            RequireEditable();entities.EndEdit();var p=ToolEvidenceReviewWorkspace.Read(editor.Text,draft);
            if(textEdited)
            {
                if(ConfirmAction?.Invoke("Nội dung hồ sơ đã được sửa trực tiếp. Xác nhận thay mục vật liệu bằng phân loại từ bảng? Các mục khác được giữ và bản nháp cả hai sẽ được lưu riêng trước khi thay.")!=true)
                    throw new InvalidDataException("EDITOR_CONFLICT_REQUIRES_EXPLICIT_CHOICE");
                if(!TryPreserve())throw new IOException("EDITOR_CONFLICT_RECOVERY_REQUIRED");
            }
            p.Material=HolderReviewEditorData.Material(draft,Assignments(),p.Material?.CombinationRule);
            proposal=ToolEvidenceReviewWorkspace.PrepareUnsigned(p,draft);editor.Text=ToolEvidenceReviewWorkspace.Serialize(proposal);
            gridEdited=textEdited=false;dirty=true;
            detail.Text="Đã ghi đề xuất chưa ký. Chưa phải vật liệu đã duyệt; kiểm tra va chạm vẫn qua bộ xác minh hiện có.";
            canvas.Proposed=proposal.Material;canvas.Invalidate();
        }
        private void LoadProposal()
        {
            using(var dialog=new OpenFileDialog {Filter="Hồ sơ kỹ thuật / nháp riêng (*.json)|*.json"})
                if(dialog.ShowDialog(this)==DialogResult.OK)
                {
                    if(new FileInfo(dialog.FileName).Length>8*1024*1024)throw new InvalidDataException("REVIEW_PROPOSAL_SIZE_LIMIT");
                    ImportProposal(File.ReadAllText(dialog.FileName),Path.GetDirectoryName(dialog.FileName));
                }
        }
        internal void ImportProposal(string text,string evidenceRoot)
        {
            if(draft==null||!draft.Current)throw new InvalidDataException("SOURCE_STALE");
            ToolEvidenceReviewProposal incoming;List<HolderReviewAssignment> rows;bool edited=false,editedText=false;string raw=text;
            var parsed=new System.Web.Script.Serialization.JavaScriptSerializer {MaxJsonLength=8*1024*1024}.DeserializeObject(text) as Dictionary<string,object>;
            if(parsed?.ContainsKey("Format")==true)
            {
                var recovery=HolderReviewEditorData.ReadRecovery(text,draft);
                if(recovery.NativeContext!=HolderReviewEditorData.NativeContext(nativeSheet))throw new InvalidDataException("STALE_NATIVE_EDITOR_RECOVERY_EXPLICIT_MATERIAL_COPY_REQUIRED");
                // A recovery file preserves unfinished text, including invalid
                // JSON. It is never a verified record or runtime input.
                incoming=null;try{incoming=ToolEvidenceReviewWorkspace.Read(recovery.Text,draft);}
                catch(Exception ex) when(ex is ArgumentException||ex is InvalidDataException||ex is InvalidOperationException){}
                rows=recovery.Assignments;raw=recovery.Text;evidenceRoot=recovery.ImportRoot;edited=recovery.GridEdited;editedText=recovery.TextEdited;
            }
            else {incoming=ToolEvidenceReviewWorkspace.Read(text,draft);rows=HolderReviewEditorData.ReadAssignments(draft,incoming);raw=ToolEvidenceReviewWorkspace.Serialize(incoming);}
            // Parse/validate before touching either the old grid or text.
            if(!TryPreserve())return;
            binding=true;
            try {proposal=incoming;importRoot=evidenceRoot;FillAssignments(rows);editor.Text=raw;SetImportedLock();canvas.Proposed=proposal?.Material;gridEdited=edited;textEdited=editedText;dirty=true;UpdatePreview();}
            finally {binding=false;}
            detail.Text=incoming==null?"Đã khôi phục bản nháp chưa hoàn chỉnh và bảng phân loại. Cần sửa nội dung trước khi xuất hồ sơ; bản nháp không phải chứng cứ xác minh.":ForeignNative(incoming)?"Hồ sơ thuộc chi tiết/nếp hoặc lần đọc khác. Chỉ xem; tạo bản nháp mới để sao chép riêng phân loại đường, không dùng lại dữ liệu gá/phôi cũ.":
                HolderReviewEditorData.HasAttestation(incoming)?"Đã nạp đầy đủ vai trò/đường bao. Hồ sơ có trường xác nhận: chỉ xem và kiểm tra qua bộ xác minh; muốn sửa phải tạo bản chưa ký riêng.":
                "Đã nạp đầy đủ vai trò/đường bao vào bảng để sửa. Đây là đề xuất chưa xác minh, không tự áp dụng vào thư viện.";
        }
        internal ToolEvidenceReviewProposal PrepareUnsignedExport()
        {
            RequireEditable();if(gridEdited)ApplyRoles();
            var p=ToolEvidenceReviewWorkspace.Read(editor.Text,draft);
            var rows=HolderReviewEditorData.ReadAssignments(draft,p);
            p.Material=HolderReviewEditorData.Material(draft,rows,p.Material?.CombinationRule);
            if(textEdited)
            {
                binding=true;try {FillAssignments(rows);proposal=p;textEdited=false;SetImportedLock();}finally{binding=false;}UpdatePreview();
            }
            return ToolEvidenceReviewWorkspace.PrepareUnsigned(p,draft);
        }
        internal void AuditProposal()
        {
            var p=ToolEvidenceReviewWorkspace.Read(editor.Text,draft);
            var config=new ToolProfileSourceSettings().Load();string authorities=null;
            if(!string.IsNullOrWhiteSpace(config.PlacementConfigurationPath))
            {
                string path=Path.GetFullPath(config.PlacementConfigurationPath);
                var placement=PlacementSourceIo.Json<BendPlacementConfiguration>(PlacementSourceIo.Read(path,65536,CancellationToken.None));
                authorities=PlacementSourceIo.Resolve(Path.GetDirectoryName(path),placement.ReviewAuthoritiesPath);
            }
            var audit=ToolEvidenceReviewWorkspace.Audit(p,draft,ToolReviewVerifier.Load(authorities,CancellationToken.None),importRoot??Directory.GetCurrentDirectory(),CancellationToken.None);
            detail.Text=(audit.Status=="REVIEWED_INPUT_SCOPE"?"Đã kiểm tra phạm vi hồ sơ":"Chưa đủ dữ liệu")+" · vật liệu "+(audit.MaterialReady?"đã kiểm tra":"chưa đủ")+" · tọa độ "+(audit.CoordinatesReady?"đã kiểm tra":"chưa đủ")+" · gá thực tế "+(audit.PhysicalPairVerified?"đã kiểm tra":"chưa đủ")+"\r\n"+
                string.Join("\r\n",audit.Reasons)+"\r\n"+string.Join("\r\n",audit.Payloads.Select(v=>v.Key+" payload: "+v.Value));
        }
        private async Task ExportProposalAsync()
        {
            var selected=draft;var sheet=nativeSheet;
            var prepared=PrepareUnsignedExport();string inputText=editor.Text;
            if(sheet!=null)
            {
                // Read the exact final-envelope digest, not the fast-precheck
                // placeholder used when no setup exists. Worker receives ONLY
                // managed model DTOs; freshness is checked again on the UI STA.
                var model=sheet.Binding.Model;int feature=sheet.Binding.FeatureId;var token=preparationLifetime.Token;
                detail.Text="Đang chuẩn bị hình học cuối và mã liên kết CAD…";
                var final=await Task.Run(()=>PrismaticEnvelopeCompiler.Read(model,model.Bends.Single(b=>b.FeatureId==feature),token),token);
                if(IsDisposed||!ReferenceEquals(selected,draft)||editor.Text!=inputText||!selected.Current||!final.Current)throw new OperationCanceledException();
                foreach(var record in prepared.Axial.Where(r=>r.FeatureId==feature))record.EnvelopeGeometrySha256=final.GeometrySha256;
            }
            proposal=ToolEvidenceReviewWorkspace.PrepareUnsigned(prepared,selected);
            using(var dialog=new SaveFileDialog {Filter="Hồ sơ kỹ thuật chưa ký (*.json)|*.json",FileName=draft.ToolId+"-UNSIGNED.json"})
                if(dialog.ShowDialog(this)==DialogResult.OK)
                {
                    if(!ReferenceEquals(selected,draft))throw new OperationCanceledException();
                    var final=ToolEvidenceReviewWorkspace.PrepareUnsigned(proposal,selected);
                    File.WriteAllText(dialog.FileName,ToolEvidenceReviewWorkspace.Serialize(final));editor.Text=ToolEvidenceReviewWorkspace.Serialize(final);
                    dirty=gridEdited=textEdited=false;
                    detail.Text="Đã xuất hồ sơ chưa ký với liên kết hình học cuối. CAD và cấu hình sản xuất không đổi.";
                }
        }
        private void ExportProcessTemplate()
        {
            var package=ToolEvidenceReviewWorkspace.CreateUnsignedProcessTemplate(draft,nativeSheet);
            using(var dialog=new SaveFileDialog {Filter="Hồ sơ quy trình riêng chưa ký (*.json)|*.json",FileName="PROCESS-UNSIGNED.json"})
                if(dialog.ShowDialog(this)==DialogResult.OK){File.WriteAllText(dialog.FileName,ToolEvidenceReviewWorkspace.SerializeProcessTemplate(package));
                    detail.Text="Đã xuất đúng native FeatureId / PersistentId. Cần bổ sung phôi từng trạng thái, motion, setup và chứng cứ độc lập; chưa có đồ thị quy trình.";}
        }
    }
    internal sealed class ToolReferenceCanvas : Control
    {
        private ToolAssemblyProfile source;private HolderMaterialReviewDraft draft;
        private string fitMode="Holder";private double zoom=1;private PointF pan;private Point? dragging;
        internal string SelectedKey {get;set;}
        internal HashSet<string> SelectedKeys {get;set;}=new HashSet<string>(StringComparer.Ordinal);
        internal IReadOnlyDictionary<string,string> ProposedRoles {get;set;}
        internal event Action<string,bool> EntityPicked;
        private readonly List<Tuple<string,double,double,double,double>> picks=new List<Tuple<string,double,double,double,double>>();
        internal HolderMaterialDefinition Proposed {get;set;}
        internal ToolReferenceCanvas(){DoubleBuffered=true;BackColor=Color.White;ResizeRedraw=true;TabStop=true;}
        internal void Fit(string mode){fitMode=mode;zoom=1;pan=PointF.Empty;Invalidate();}
        internal void ShowSource(ToolAssemblyProfile source,HolderMaterialReviewDraft draft){this.source=source;this.draft=draft;SelectedKey=null;SelectedKeys.Clear();Proposed=null;ProposedRoles=null;picks.Clear();Fit("Holder");}
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);double old=zoom;zoom=Math.Max(.05,Math.Min(100,zoom*Math.Pow(1.2,e.Delta/120.0)));
            pan=new PointF((float)(e.X-(e.X-pan.X)*zoom/old),(float)(e.Y-(e.Y-pan.Y)*zoom/old));Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);Focus();if(e.Button==MouseButtons.Right){dragging=e.Location;Capture=true;}
            if(e.Button==MouseButtons.Left)
            {
                // Pixel picking in a read-only viewer. This is never used for
                // DXF pairing, topology identity, material or collision proof.
                var hit=picks.Select(p=>new {p.Item1,Distance=PixelDistance(e.X,e.Y,p.Item2,p.Item3,p.Item4,p.Item5)}).Where(p=>p.Distance<=6).OrderBy(p=>p.Distance).FirstOrDefault();
                if(hit!=null)EntityPicked?.Invoke(hit.Item1,(ModifierKeys&(Keys.Control|Keys.Shift))!=0);
            }
        }
        private static double PixelDistance(double x,double y,double ax,double ay,double bx,double by)
        {double dx=bx-ax,dy=by-ay,n=dx*dx+dy*dy,t=n==0?0:Math.Max(0,Math.Min(1,((x-ax)*dx+(y-ay)*dy)/n));return Math.Sqrt(Math.Pow(x-ax-t*dx,2)+Math.Pow(y-ay-t*dy,2));}
        internal static Color ProposalColor(string role)=>role=="Hole"?Color.Purple:role=="Island"?Color.SaddleBrown:role=="Solid"?Color.DarkOrange:Color.LightSlateGray;
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging.HasValue){pan.X+=e.X-dragging.Value.X;pan.Y+=e.Y-dragging.Value.Y;dragging=e.Location;Invalidate();}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=null;Capture=false;}
        private static ToolPoint[] DisplayPoints(ToolCurve c)
        {
            if(c.Kind!="ARC")return new[]{c.Start,c.End};
            int n=Math.Max(2,(int)Math.Ceiling(Math.Abs(c.SweepRadians)*32));double angle=Math.Atan2(c.Start.Y-c.Center.Y,c.Start.X-c.Center.X);
            return Enumerable.Range(0,n+1).Select(i=>new ToolPoint(c.Center.X+c.RadiusMm*Math.Cos(angle+c.SweepRadians*i/n),c.Center.Y+c.RadiusMm*Math.Sin(angle+c.SweepRadians*i/n))).ToArray();
        }
        private void DrawClipped(Graphics graphics,Pen pen,double ax,double ay,double bx,double by)
        {
            // Pixel clipping avoids GDI+ overflow when zooming a very short
            // selected entity while distant drawing references remain present.
            double dx=bx-ax,dy=by-ay,lo=0,hi=1;
            double[] p={-dx,dx,-dy,dy},q={ax,Width-ax,ay,Height-ay};
            for(int i=0;i<4;i++)
            {
                if(p[i]==0){if(q[i]<0)return;continue;}
                double t=q[i]/p[i];if(p[i]<0)lo=Math.Max(lo,t);else hi=Math.Min(hi,t);if(lo>hi)return;
            }
            graphics.DrawLine(pen,(float)(ax+lo*dx),(float)(ay+lo*dy),(float)(ax+hi*dx),(float)(ay+hi*dy));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);picks.Clear();if(source==null||draft==null)return;
            var holder=draft.Rows.SelectMany(r=>r.Geometry.NominalCurves.Select(c=>new {Curve=c,Key=r.EntityKey})).ToArray();
            var punch=source.Punch.Curves.Select(c=>new {Curve=c,Key=(string)null});
            var paths=punch.Concat(holder).Select(c=>new {Points=DisplayPoints(c.Curve),c.Key}).ToArray();
            var fit=paths.Where(p=>fitMode=="All"||fitMode=="Punch"&&p.Key==null||fitMode=="Holder"&&p.Key!=null||fitMode=="Selected"&&(SelectedKeys.Contains(p.Key)||p.Key==SelectedKey)).ToArray();
            if(fit.Length==0)fit=paths;var all=fit.SelectMany(p=>p.Points).ToArray();if(all.Length==0)return;
            double minX=all.Min(p=>p.X),minY=all.Min(p=>p.Y),maxX=all.Max(p=>p.X),maxY=all.Max(p=>p.Y);
            string legend=(draft.Current?"CAD gốc · tọa độ trình bày · chưa xác minh gá":"Nguồn đã đổi · cần quét lại")+
                "\nĐề xuất: cam vật liệu · tím lỗ · nâu vùng trong lỗ\nXám chưa phân loại/tham khảo · xanh dương đang chọn";
            int legendHeight=TextRenderer.MeasureText(legend,Font,new Size(Math.Max(1,Width-10),int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPadding).Height+10;
            double scale=zoom*Math.Min(Math.Max(1,Width-40)/Math.Max(1e-6,maxX-minX),Math.Max(1,Height-legendHeight-20)/Math.Max(1e-6,maxY-minY));
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            foreach(var path in paths)
            {
                Color color=path.Key==null?Color.SlateGray:Color.LightSlateGray;
                // Proposed colors are never green/PASS. Reviewed material and
                // simulation readiness remain text in the evidence panel.
                if(Proposed?.Excluded?.Any(x=>x.EntityKey==path.Key)==true)color=Color.LightSlateGray;
                string role;if(path.Key!=null&&ProposedRoles?.TryGetValue(path.Key,out role)==true)
                    color=ProposalColor(role);
                bool selected=path.Key!=null&&SelectedKeys.Contains(path.Key);
                using(var pen=new Pen(selected?Color.Blue:color,selected?3:1.2f))
                    for(int i=1;i<path.Points.Length;i++)
                    {
                        double ax=pan.X+20+(path.Points[i-1].X-minX)*scale,ay=pan.Y+Height-20-(path.Points[i-1].Y-minY)*scale,bx=pan.X+20+(path.Points[i].X-minX)*scale,by=pan.Y+Height-20-(path.Points[i].Y-minY)*scale;
                        DrawClipped(e.Graphics,pen,ax,ay,bx,by);if(path.Key!=null)picks.Add(Tuple.Create(path.Key,ax,ay,bx,by));
                    }
            }
            TextRenderer.DrawText(e.Graphics,legend,Font,new Rectangle(5,3,Math.Max(1,Width-10),legendHeight),Color.DimGray,TextFormatFlags.WordBreak|TextFormatFlags.NoPadding);
        }
    }
}
