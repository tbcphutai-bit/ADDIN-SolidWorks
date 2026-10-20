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
            RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.DisplayedCells};
        private readonly TextBox editor=new TextBox {Name="unsignedToolEvidence",Dock=DockStyle.Fill,Multiline=true,ScrollBars=ScrollBars.Both,WordWrap=false,MaxLength=4*1024*1024};
        private readonly TextBox detail=new TextBox {Dock=DockStyle.Bottom,Height=54,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        private readonly ToolReferenceCanvas canvas=new ToolReferenceCanvas {Dock=DockStyle.Fill};
        private HolderMaterialReviewDraft draft;
        private ToolEvidenceReviewProposal proposal;
        private SheetEnvelopeCapture nativeSheet;
        private string importRoot;
        private bool binding;
        private readonly CancellationTokenSource preparationLifetime=new CancellationTokenSource();
        internal HolderMaterialReviewDraft Draft => draft;
        internal string ProposalText {get=>editor.Text;set=>editor.Text=value;}
        internal BendToolEvidenceControl()
        {
            Dock=DockStyle.Fill;Name="bendToolEvidence";
            var tabs=new TabControl {Dock=DockStyle.Fill};var geometry=new TabPage("CAD / vai trò");var input=new TabPage("Hồ sơ gá / JSON");
            var split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Vertical};
            split.Panel1.Controls.Add(canvas);split.Panel2.Controls.Add(entities);geometry.Controls.Add(split);input.Controls.Add(editor);
            var zoomBar=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=false};
            foreach(string mode in new[]{"Holder","Punch","All","Selected"})
            {string fit=mode;var b=new Button {Text=mode,AutoSize=true};b.Click+=(s,e)=>canvas.Fit(fit);zoomBar.Controls.Add(b);}
            split.Panel1.Controls.Add(zoomBar);
            foreach(string text in new[]{"Handle / key","Loại","Layer","Hình học","Sai số mm"})
            {int i=entities.Columns.Add("e"+entities.Columns.Count,text);entities.Columns[i].ReadOnly=true;}
            entities.Columns.Add(new DataGridViewComboBoxColumn {Name="role",HeaderText="Vai trò đề xuất",DataSource=new[]{"NeedsReview","Solid","Hole","Island","Decorative","Centerline","Reference","Annotation"}});
            entities.Columns.Add("contour","Contour ID");entities.Columns.Add("parent","Parent ID");
            entities.Columns[0].AutoSizeMode=DataGridViewAutoSizeColumnMode.None;entities.Columns[0].Width=76;
            entities.DataError+=(s,e)=>{e.ThrowException=false;detail.Text="Vai trò không hợp lệ.";};
            entities.SelectionChanged+=(s,e)=> {canvas.SelectedKey=entities.CurrentRow?.Tag as string;canvas.Invalidate();};
            entities.CellToolTipTextNeeded+=(s,e)=> {if(e.RowIndex>=0&&e.ColumnIndex>=0)e.ToolTipText=e.ColumnIndex==0?
                Convert.ToString(entities.Rows[e.RowIndex].Tag):Convert.ToString(entities[e.ColumnIndex,e.RowIndex].Value);};
            tabs.TabPages.AddRange(new[]{geometry,input});
            var actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,AutoSize=true,WrapContents=true};
            AddButton(actions,"applyHolderRoles","Áp dụng vai trò",()=>ApplyRoles());
            AddButton(actions,"loadToolProposal","Nhập hồ sơ",()=>LoadProposal());
            AddButton(actions,"auditToolEvidence","Kiểm tra chứng cứ",()=>AuditProposal());
            AddAsyncButton(actions,"exportToolProposal","Xuất chưa ký",ExportProposalAsync);
            AddButton(actions,"exportProcessTemplate","Xuất quy trình trống",()=>ExportProcessTemplate());
            Controls.Add(tabs);Controls.Add(detail);Controls.Add(actions);Controls.Add(status);
            Disposed+=(s,e)=>{preparationLifetime.Cancel();preparationLifetime.Dispose();};
        }
        private void AddButton(Control parent,string name,string text,Action action)
        {
            var b=new Button {Name=name,Text=text,AutoSize=true};parent.Controls.Add(b);
            b.Click+=(s,e)=>{try {action();}catch(Exception ex) when(ex is InvalidOperationException||ex is ArgumentException||ex is IOException||ex is UnauthorizedAccessException)
                {detail.Text="UNKNOWN · "+ex.Message;}};
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
                {if(!IsDisposed)detail.Text="UNKNOWN · "+ex.Message;}
                finally{if(!button.IsDisposed)button.Enabled=true;}
            };
        }
        internal void ShowTool(ToolAssemblyProfile source,SheetEnvelopeCapture sheet,Func<bool> current)
        {
            // No proposal is ever committed on selection; a reviewer explicitly
            // exports. A changed source/snapshot discards the editable proposal.
            draft=null;proposal=null;entities.Rows.Clear();editor.Clear();detail.Clear();canvas.ShowSource(null,null);importRoot=null;
            nativeSheet=sheet;
            if(source?.CadPair?.Matched!=true||current?.Invoke()!=true){status.Text="UNKNOWN · Chọn dao từ kết quả còn hiệu lực.";return;}
            draft=HolderMaterialReviewDraftBuilder.Create(source,CancellationToken.None,current);
            proposal=ToolEvidenceReviewWorkspace.Create(draft,sheet);editor.Text=ToolEvidenceReviewWorkspace.Serialize(proposal);
            binding=true;
            try {foreach(var row in draft.Rows) {int i=entities.Rows.Add(row.Handle,row.SourceType,row.Layer,(row.GeometrySupported?"Supported":row.UnsupportedReason)+
                    (row.ReviewedRole==null?"":" · reviewed "+row.ReviewedRole),
                    row.BoundaryApproximationErrorMm.ToString("R"),"NeedsReview","","");entities.Rows[i].Tag=row.EntityKey;}}
            finally {binding=false;}
            canvas.ShowSource(source,draft);
            status.Text=source.Id+" · "+draft.Status+" · "+draft.Rows.Count+" entities · "+draft.ConnectedComponents+" components\n"+
                "CAD reference / WCS trình bày · CHƯA XÁC MINH · "+source.CadPair.PunchInsert+" / "+source.CadPair.HolderInsert;
            detail.Text=string.Join(Environment.NewLine,draft.MissingReasons)+Environment.NewLine+"Độ dài 4200/6000 trong Excel không được tự điền vào chiều dài dao.";
        }
        internal void ApplyRoles()
        {
            if(binding||draft==null)throw new InvalidOperationException("MISSING_CURRENT_TOOL");
            entities.EndEdit();var p=ToolEvidenceReviewWorkspace.Read(editor.Text,draft);
            var contours=new List<HolderContourDefinition>();var excluded=new List<HolderExcludedEntity>();
            foreach(DataGridViewRow row in entities.Rows)
            {
                string key=(string)row.Tag,role=Convert.ToString(row.Cells[5].Value),id=Convert.ToString(row.Cells[6].Value),parent=Convert.ToString(row.Cells[7].Value);
                parent=string.IsNullOrWhiteSpace(parent)?null:parent;
                if(new[]{"Solid","Hole","Island"}.Contains(role))
                {
                    if(string.IsNullOrWhiteSpace(id))throw new InvalidOperationException("CONTOUR_ID_REQUIRED");
                    var c=contours.SingleOrDefault(v=>v.Id==id);
                    if(c==null){c=new HolderContourDefinition {Id=id,ParentId=parent,MaterialKind=role,EntityKeys=new string[0]};contours.Add(c);}
                    if(c.MaterialKind!=role||c.ParentId!=parent)throw new InvalidOperationException("INCONSISTENT_CONTOUR_ROLE_OR_PARENT");
                    c.EntityKeys=c.EntityKeys.Concat(new[]{key}).ToArray();
                }
                else if(role!="NeedsReview")excluded.Add(new HolderExcludedEntity {EntityKey=key,Role=role});
                else throw new InvalidOperationException("INCOMPLETE_ENTITY_ROLES");
            }
            p.Material=HolderMaterialReviewDraftBuilder.PrepareUnsigned(draft,contours,excluded,p.Material?.CombinationRule);
            proposal=ToolEvidenceReviewWorkspace.PrepareUnsigned(p,draft);editor.Text=ToolEvidenceReviewWorkspace.Serialize(proposal);
            detail.Text="Đã ghi đề xuất chưa ký. Chưa phải material đã duyệt; collision vẫn theo bộ xác minh hiện có.";
            canvas.Proposed=proposal.Material;canvas.Invalidate();
        }
        private void LoadProposal()
        {
            using(var dialog=new OpenFileDialog {Filter="Private review JSON|*.json"})
                if(dialog.ShowDialog(this)==DialogResult.OK)
                {
                    if(new FileInfo(dialog.FileName).Length>4*1024*1024)throw new InvalidDataException("REVIEW_PROPOSAL_SIZE_LIMIT");
                    proposal=ToolEvidenceReviewWorkspace.Read(File.ReadAllText(dialog.FileName),draft);
                    importRoot=Path.GetDirectoryName(dialog.FileName);editor.Text=ToolEvidenceReviewWorkspace.Serialize(proposal);
                    canvas.Proposed=proposal.Material;canvas.Invalidate();detail.Text="Đã nhập; chưa cấp quyền gá hoặc thay đổi nguồn Check Chấn. Bấm Kiểm tra chứng cứ.";
                }
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
            detail.Text=audit.Status+" · material "+audit.MaterialReady+" · tọa độ "+audit.CoordinatesReady+" · physical "+audit.PhysicalPairVerified+"\r\n"+
                string.Join("\r\n",audit.Reasons)+"\r\n"+string.Join("\r\n",audit.Payloads.Select(v=>v.Key+" payload: "+v.Value));
        }
        private async Task ExportProposalAsync()
        {
            var selected=draft;var sheet=nativeSheet;
            string inputText=editor.Text;
            var prepared=ToolEvidenceReviewWorkspace.PrepareUnsigned(ToolEvidenceReviewWorkspace.Read(inputText,selected),selected);
            if(sheet!=null)
            {
                // Read the exact final-envelope digest, not the fast-precheck
                // placeholder used when no setup exists. Worker receives ONLY
                // managed model DTOs; freshness is checked again on the UI STA.
                var model=sheet.Binding.Model;int feature=sheet.Binding.FeatureId;var token=preparationLifetime.Token;
                detail.Text="Đang chuẩn bị hình học cuối và binding native…";
                var final=await Task.Run(()=>PrismaticEnvelopeCompiler.Read(model,model.Bends.Single(b=>b.FeatureId==feature),token),token);
                if(IsDisposed||!ReferenceEquals(selected,draft)||editor.Text!=inputText||!selected.Current||!final.Current)throw new OperationCanceledException();
                foreach(var record in prepared.Axial.Where(r=>r.FeatureId==feature))record.EnvelopeGeometrySha256=final.GeometrySha256;
            }
            proposal=ToolEvidenceReviewWorkspace.PrepareUnsigned(prepared,selected);
            using(var dialog=new SaveFileDialog {Filter="Private review JSON|*.json",FileName=draft.ToolId+"-UNSIGNED.json"})
                if(dialog.ShowDialog(this)==DialogResult.OK)
                {
                    if(!ReferenceEquals(selected,draft))throw new OperationCanceledException();
                    var final=ToolEvidenceReviewWorkspace.PrepareUnsigned(proposal,selected);
                    File.WriteAllText(dialog.FileName,ToolEvidenceReviewWorkspace.Serialize(final));editor.Text=ToolEvidenceReviewWorkspace.Serialize(final);
                    detail.Text="Đã xuất hồ sơ chưa ký với binding hình học cuối. CAD và cấu hình production không đổi.";
                }
        }
        private void ExportProcessTemplate()
        {
            var package=ToolEvidenceReviewWorkspace.CreateUnsignedProcessTemplate(draft,nativeSheet);
            using(var dialog=new SaveFileDialog {Filter="Private process JSON|*.json",FileName="PROCESS-UNSIGNED.json"})
                if(dialog.ShowDialog(this)==DialogResult.OK){File.WriteAllText(dialog.FileName,ToolEvidenceReviewWorkspace.SerializeProcessTemplate(package));
                    detail.Text="Đã xuất đúng native FeatureId / PersistentId. Cần bổ sung phôi từng trạng thái, motion, setup và chứng cứ độc lập; chưa có đồ thị quy trình.";}
        }
    }
    internal sealed class ToolReferenceCanvas : Control
    {
        private ToolAssemblyProfile source;private HolderMaterialReviewDraft draft;
        private string fitMode="Holder";private double zoom=1;private PointF pan;private Point? dragging;
        internal string SelectedKey {get;set;}
        internal HolderMaterialDefinition Proposed {get;set;}
        internal ToolReferenceCanvas(){DoubleBuffered=true;BackColor=Color.White;ResizeRedraw=true;TabStop=true;}
        internal void Fit(string mode){fitMode=mode;zoom=1;pan=PointF.Empty;Invalidate();}
        internal void ShowSource(ToolAssemblyProfile source,HolderMaterialReviewDraft draft){this.source=source;this.draft=draft;SelectedKey=null;Proposed=null;Fit("Holder");}
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);double old=zoom;zoom=Math.Max(.05,Math.Min(100,zoom*Math.Pow(1.2,e.Delta/120.0)));
            pan=new PointF((float)(e.X-(e.X-pan.X)*zoom/old),(float)(e.Y-(e.Y-pan.Y)*zoom/old));Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(e.Button==MouseButtons.Right){dragging=e.Location;Capture=true;}}
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
            base.OnPaint(e);if(source==null||draft==null)return;
            var holder=draft.Rows.SelectMany(r=>r.Geometry.NominalCurves.Select(c=>new {Curve=c,Key=r.EntityKey})).ToArray();
            var punch=source.Punch.Curves.Select(c=>new {Curve=c,Key=(string)null});
            var paths=punch.Concat(holder).Select(c=>new {Points=DisplayPoints(c.Curve),c.Key}).ToArray();
            var fit=paths.Where(p=>fitMode=="All"||fitMode=="Punch"&&p.Key==null||fitMode=="Holder"&&p.Key!=null||fitMode=="Selected"&&p.Key==SelectedKey).ToArray();
            if(fit.Length==0)fit=paths;var all=fit.SelectMany(p=>p.Points).ToArray();if(all.Length==0)return;
            double minX=all.Min(p=>p.X),minY=all.Min(p=>p.Y),maxX=all.Max(p=>p.X),maxY=all.Max(p=>p.Y);
            double scale=zoom*Math.Min(Math.Max(1,Width-40)/Math.Max(1e-6,maxX-minX),Math.Max(1,Height-55)/Math.Max(1e-6,maxY-minY));
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            foreach(var path in paths)
            {
                Color color=path.Key==null?Color.SlateGray:Color.DarkOrange;
                // Proposed colors are never green/PASS. Reviewed material and
                // simulation readiness remain text in the evidence panel.
                if(Proposed?.Excluded?.Any(x=>x.EntityKey==path.Key)==true)color=Color.LightSlateGray;
                bool selected=path.Key!=null&&path.Key==SelectedKey;
                using(var pen=new Pen(selected?Color.Blue:color,selected?3:1.2f))
                    for(int i=1;i<path.Points.Length;i++)DrawClipped(e.Graphics,pen,pan.X+20+(path.Points[i-1].X-minX)*scale,
                        pan.Y+Height-20-(path.Points[i-1].Y-minY)*scale,pan.X+20+(path.Points[i].X-minX)*scale,pan.Y+Height-20-(path.Points[i].Y-minY)*scale);
            }
            e.Graphics.DrawString(draft.Current?"CAD reference · WCS · chưa xác minh gá":"SOURCE STALE · UNKNOWN",Font,Brushes.Black,5,3);
        }
    }
}
