using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;

namespace ADDIN.UI
{
    internal sealed class BendMinimumCheckForm : Form
    {
        private readonly ISldWorks app;
        private readonly Action<string> publishReport;
        private readonly BendToolCatalogProvider provider=new BendToolCatalogProvider();
        private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        private readonly System.Windows.Forms.Timer scanTimer=new System.Windows.Forms.Timer{Interval=1};
        private readonly System.Windows.Forms.Timer sourceTimer=new System.Windows.Forms.Timer{Interval=5000};
        private readonly Label modelLabel=new Label(),sourceLabel=new Label(),statusLabel=new Label();
        private readonly ComboBox material=new ComboBox(),filter=new ComboBox();
        private readonly ModernButton scan=new ModernButton(),stop=new ModernButton(),locate=new ModernButton();
        private readonly DataGridView grid=new DataGridView();
        private readonly TextBox details=new TextBox();
        private readonly MinimumSectionPreview preview=new MinimumSectionPreview();
        private BendMinimumScanSession session;
        private BendToolCatalogSnapshot source;
        private bool readingSource,scanning,closing;
        private string sourceError;
        private int generation;

        internal BendMinimumCheckForm(ISldWorks application,Action<string> report)
        {
            app=application;publishReport=report;
            Name="BendMinimumCheckForm";Text="CHECK BẺ TỐI THIỂU — Part 3D";
            AutoScaleMode=AutoScaleMode.Dpi;Font=TaiTheme.Body;BackColor=TaiTheme.Page;
            ClientSize=new Size(1000,660);MinimumSize=new Size(740,560);StartPosition=FormStartPosition.CenterScreen;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(12),BackColor=TaiTheme.Page};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,27));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,47));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,170));
            Controls.Add(layout);
            SetupLabel(modelLabel,TaiTheme.Text,true);modelLabel.Text="Part đang mở • configuration hiện tại";layout.Controls.Add(modelLabel,0,0);
            SetupLabel(sourceLabel,TaiTheme.TextMuted,false);sourceLabel.Text="Excel dao chấn: đang đọc đường dẫn trong Settings…";layout.Controls.Add(sourceLabel,0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,Padding=new Padding(0,4,0,0)};
            actions.Controls.Add(new Label{Text="Vật liệu:",AutoSize=true,Margin=new Padding(0,7,6,0),ForeColor=TaiTheme.Text});
            material.Name="minimumMaterial";material.DropDownStyle=ComboBoxStyle.DropDownList;material.Width=170;
            material.Items.AddRange(new object[]{"Từ model","AL","CU","BS","RBS","SUS","ST"});material.SelectedIndex=0;
            actions.Controls.Add(material);
            Button(scan,"Quét lại",ModernButtonKind.Primary,TaiIcon.Refresh,96);scan.Name="scanMinimumBends";scan.Click+=(s,e)=>StartScan();actions.Controls.Add(scan);
            Button(stop,"Dừng",ModernButtonKind.Standard,TaiIcon.Stop,74);stop.Name="stopMinimumScan";stop.Enabled=false;stop.Click+=(s,e)=>StopScan("Đã dừng; kết quả chưa bao phủ toàn bộ Part.");actions.Controls.Add(stop);
            Button(locate,"Tới vị trí",ModernButtonKind.Standard,TaiIcon.BendFlat,103);locate.Name="locateMinimumBend";locate.Enabled=false;locate.Click+=(s,e)=>Locate();actions.Controls.Add(locate);
            filter.Name="minimumResultFilter";filter.DropDownStyle=ComboBoxStyle.DropDownList;filter.Width=153;
            filter.Items.AddRange(new object[]{"Tất cả","Không đạt","Cần xác minh","OK"});filter.SelectedIndex=0;actions.Controls.Add(filter);layout.Controls.Add(actions,0,2);
            SetupLabel(statusLabel,TaiTheme.TextMuted,false);statusLabel.AutoEllipsis=false;
            statusLabel.Text="90° • Kích thước phủ bì • L: A • Z: B • C/U: C\r\nCạnh đã gán bào: OK. Chấn thường: nhóm vật liệu từ thư viện, giới hạn từ Excel.";layout.Controls.Add(statusLabel,0,3);
            ConfigureGrid();layout.Controls.Add(grid,0,4);
            var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,8,0,0)};
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));
            details.Name="minimumBendDetails";details.Multiline=true;details.ReadOnly=true;details.Dock=DockStyle.Fill;details.ScrollBars=ScrollBars.Vertical;
            details.BackColor=TaiTheme.Surface;details.ForeColor=TaiTheme.Text;details.BorderStyle=BorderStyle.FixedSingle;
            details.Text="Chọn một dòng để xem mốc đo A/B/C và lý do kết luận.";
            preview.Dock=DockStyle.Fill;preview.Margin=new Padding(8,0,0,0);bottom.Controls.Add(details,0,0);bottom.Controls.Add(preview,1,0);layout.Controls.Add(bottom,0,5);
            scanTimer.Tick+=(s,e)=>ScanStep();sourceTimer.Tick+=(s,e)=>RefreshSource(false);
            material.SelectedIndexChanged+=(s,e)=>Reevaluate();filter.SelectedIndexChanged+=(s,e)=>Populate();
            Shown+=(s,e)=>{sourceTimer.Start();StartScan();};
        }
        private static void SetupLabel(Label label,Color color,bool strong)
        {label.Dock=DockStyle.Fill;label.TextAlign=ContentAlignment.MiddleLeft;label.AutoEllipsis=true;label.ForeColor=color;label.Font=strong?TaiTheme.BodyStrong:TaiTheme.Small;label.Margin=Padding.Empty;}
        private static void Button(ModernButton button,string caption,ModernButtonKind kind,TaiIcon icon,int width)
        {button.Text=caption;button.Kind=kind;button.Icon=icon;button.Size=new Size(width,30);button.Margin=new Padding(6,0,0,0);}
        private void ConfigureGrid()
        {
            grid.Name="minimumBendResults";grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;
            grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.MultiSelect=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.None;grid.BackgroundColor=TaiTheme.Surface;grid.BorderStyle=BorderStyle.FixedSingle;
            grid.RowTemplate.Height=32;grid.ColumnHeadersHeight=36;grid.EnableHeadersVisualStyles=false;
            grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(245, 247, 250);grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(30, 41, 59);
            grid.ColumnHeadersDefaultCellStyle.Font=TaiTheme.SmallStrong;
            grid.DefaultCellStyle.Font=TaiTheme.Small;grid.DefaultCellStyle.SelectionBackColor=TaiTheme.AccentSoft;grid.DefaultCellStyle.SelectionForeColor=TaiTheme.Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(249, 250, 252);
            string[] names={"Kết quả","Body / vị trí","Dạng","Vật liệu","t","A","B","C","A min","B min","C min","Mặt cắt"};
            int[] widths={125,155,48,82,53,57,57,57,62,62,62,68};
            for(int i=0;i<names.Length;i++)grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=names[i],Name="minimumColumn"+i,Width=widths[i],SortMode=DataGridViewColumnSortMode.NotSortable});
            grid.SelectionChanged+=(s,e)=>ShowDetails();
        }
        private string SelectedMaterial=>material.SelectedIndex<=0?null:material.SelectedItem.ToString();
        private void StartScan()
        {
            if(closing||readingSource||scanning)return;
            var model=app?.ActiveDoc as ModelDoc2;
            if(model==null||model.GetType()!=(int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART){SetStatus("Mở Part 3D ở trạng thái đã chấn rồi chạy lệnh.",true);return;}
            if(session!=null&&!session.IsCurrent()){material.SelectedIndex=0;session.Dispose();session=null;}
            scanTimer.Stop();grid.Rows.Clear();preview.Section=null;details.Clear();scan.Enabled=false;
            modelLabel.Text=model.GetTitle()+" • "+model.ConfigurationManager.ActiveConfiguration.Name;
            generation++;RefreshSource(true);
        }
        private void RefreshSource(bool startGeometry)
        {
            if(closing||readingSource||scanning)return;
            if(!startGeometry&&session!=null&&session.Complete&&!session.IsCurrent())InvalidateModel();
            readingSource=true;int version=generation;CancellationToken token=lifetime.Token;
            Task.Run(()=>provider.ReadForCheck(token),token).ContinueWith(read=>{
                // Observe unexpected faults even if the window has already closed.
                var failure=read.IsFaulted?read.Exception.GetBaseException():null;
                if(closing||IsDisposed)return;
                try{BeginInvoke(new Action(()=>{
                    readingSource=false;
                    if(closing||version!=generation)return;
                    if(read.IsCanceled){scan.Enabled=true;return;}
                    if(failure!=null){SourceReadFailed(failure.Message);return;}
                    var outcome=read.Result;
                    if(outcome.Snapshot==null){SourceReadFailed(outcome.Error);return;}
                    var current=outcome.Snapshot;bool changed=sourceError!=null||source==null||source.SourceSha256!=current.SourceSha256||source.SourcePath!=current.SourcePath;
                    source=current;sourceError=null;sourceLabel.Text="Excel: "+source.SourcePath;
                    if(startGeometry)BeginGeometry();else if(changed)Reevaluate();
                }));}catch(InvalidOperationException){}
            },TaskScheduler.Default);
        }
        private void SourceReadFailed(string message)
        {
            sourceError=message;sourceLabel.Text="Excel dao chấn: chưa có nguồn dữ liệu hợp lệ";
            scan.Enabled=true;InvalidateSource();
            if(grid.CurrentRow==null)details.Text="Chưa kiểm tra được kích thước tối thiểu.\r\n"+message;
        }
        private void BeginGeometry()
        {
            try {
                session?.Dispose();session=new BendMinimumScanSession(app,app.ActiveDoc as ModelDoc2);
                modelLabel.Text=session.Title+" • "+session.Configuration;
                scanning=true;scan.Enabled=false;stop.Enabled=true;material.Enabled=false;locate.Enabled=false;
                SetStatus("Đang nhận dạng và đo mặt cắt trên toàn bộ solid body…",false);scanTimer.Start();
            }catch(Exception ex){scan.Enabled=true;scanning=false;SetStatus(ex.Message,true);}
        }
        private void ScanStep()
        {
            if(closing||!scanning||session==null)return;
            try{
                session.Step();SetStatus(session.Progress,false);
                if(!session.Complete)return;
                scanTimer.Stop();scanning=false;scan.Enabled=true;stop.Enabled=false;material.Enabled=true;
                Reevaluate();
            }catch(Exception ex){StopScan(ex.Message);}
        }
        private void StopScan(string message)
        {
            scanTimer.Stop();scanning=false;stop.Enabled=false;scan.Enabled=true;material.Enabled=true;
            // An interrupted scan must never be displayed as a whole-Part pass.
            if(session!=null)foreach(var row in session.Rows){row.Status=BendMinimumStatus.InsufficientData;row.Reason=message;}
            Populate();SetStatus(message,true);
        }
        private void Reevaluate()
        {
            if(session==null||source==null||scanning)return;
            if(!session.IsCurrent()){InvalidateModel();return;}
            if(!session.Complete){SetStatus("Lần quét chưa hoàn tất. Bấm Quét lại để kiểm tra toàn Part.",true);return;}
            if(sourceError!=null){InvalidateSource();return;}
            session.Evaluate(source,SelectedMaterial);Populate();SetStatus(Summary(),false);publishReport?.Invoke(ReportText());
        }
        private void InvalidateSource()
        {
            bool current=session!=null&&session.Complete&&session.IsCurrent();
            if(session!=null)foreach(var row in session.Rows){
                if(current&&row.Groove?.State==BendMinimumGrooveState.Grooved)row.Evaluate(null,SelectedMaterial);
                else{row.Status=BendMinimumStatus.InsufficientData;row.Reason="Nguồn Excel chưa hợp lệ: "+sourceError;row.Evaluation=null;foreach(var sample in row.Samples)sample.Checks.Clear();}}
            Populate();SetStatus("Chấn thường chưa có kết quả hợp lệ. Excel: "+sourceError,true);
            publishReport?.Invoke("CHECK BẺ TỐI THIỂU\r\n"+modelLabel.Text+"\r\nNguồn Excel chưa hợp lệ: "+sourceError);
        }
        private void InvalidateModel()
        {
            string message="Model/configuration đã thay đổi. Bấm Quét lại; kết quả cũ không còn hiệu lực.";
            if(session!=null)foreach(var row in session.Rows){row.Status=BendMinimumStatus.InsufficientData;row.Reason=message;row.Evaluation=null;foreach(var sample in row.Samples)sample.Checks.Clear();}
            Populate();SetStatus(message,true);locate.Enabled=false;
            publishReport?.Invoke("CHECK BẺ TỐI THIỂU\r\n"+modelLabel.Text+"\r\n"+message);
        }
        private string Summary()
        {
            if(session==null)return "";
            return session.Rows.Count+" vị trí • "+session.Rows.Count(r=>r.Status==BendMinimumStatus.MeetsMinimum)+" đạt tối thiểu • "
                +session.Rows.Count(r=>r.Status==BendMinimumStatus.GroovedAccepted)+" cạnh bào OK • "
                +session.Rows.Count(r=>r.Status==BendMinimumStatus.BelowMinimum)+" không đạt\r\n"
                +session.Rows.Count(r=>r.Status==BendMinimumStatus.InsufficientData||r.Status==BendMinimumStatus.Conditional)
                +" chưa đủ dữ liệu • Chấn 90°, kích thước tối thiểu.";
        }
        private void Populate()
        {
            if(grid.IsDisposed)return;
            var selected=grid.CurrentRow?.Tag as BendMinimumScanRow;
            grid.Rows.Clear();if(session==null)return;
            foreach(var row in session.Rows){
                if(filter.SelectedIndex==1&&row.Status!=BendMinimumStatus.BelowMinimum||filter.SelectedIndex==2&&(IsOk(row.Status)||row.Status==BendMinimumStatus.BelowMinimum)||filter.SelectedIndex==3&&!IsOk(row.Status))continue;
                var s=row.Representative?.Section;var rule=row.Evaluation?.Rule;
                int index=grid.Rows.Add(StatusText(row.Status),row.BodyName+" / "+row.Name,s?.Shape??"?",SelectedMaterial??row.MaterialCode??row.MaterialName,
                    Number(row.Representative?.ThicknessMm),s?.Dimension=="A"?Number(s.A):"—",s?.Dimension=="B"?Number(s.B):"—",s?.Dimension=="C"?Number(s.C):"—",
                    s?.Dimension=="A"?Number(rule?.MinimumA):"—",s?.Dimension=="B"?Number(rule?.MinimumB):"—",s?.Dimension=="C"?Number(rule?.MinimumC):"—",row.Samples.Count);
                var item=grid.Rows[index];item.Tag=row;item.Cells[0].Style.ForeColor=row.Status==BendMinimumStatus.BelowMinimum?TaiTheme.Danger:IsOk(row.Status)?TaiTheme.Success:TaiTheme.TextMuted;
                item.Cells[0].ToolTipText=row.Reason;if(ReferenceEquals(row,selected))grid.CurrentCell=item.Cells[0];
            }
            ShowDetails();
        }
        private void ShowDetails()
        {
            var row=grid.CurrentRow?.Tag as BendMinimumScanRow;
            locate.Enabled=!scanning&&row?.Site!=null&&session!=null&&session.IsCurrent();
            if(row==null){preview.Section=null;details.Text="Chọn một dòng để xem mốc đo và lý do kết luận.";return;}
            preview.Section=row.Representative?.Section;
            details.Text=row.BodyName+" / "+row.Name+" — "+StatusText(row.Status)+"\r\n"+row.Reason
                +"\r\nVật liệu model: "+(string.IsNullOrEmpty(row.MaterialName)?"chưa có":row.MaterialName)
                +"; nhóm dùng check: "+(SelectedMaterial??row.MaterialCode??"chưa chọn")
                +(row.MaterialInfo==null?"":"\r\nThư viện vật liệu: "+(row.MaterialInfo.DatabasePath??row.MaterialInfo.DatabaseName)+" • "+row.MaterialInfo.Category+"; "+row.MaterialInfo.Note)
                +(row.Groove==null?"":"\r\nFeature: "+row.Groove.FeatureName+"; "+(row.Groove.State==BendMinimumGrooveState.Grooved?"cạnh bào "+row.Groove.Kind:row.Groove.State==BendMinimumGrooveState.Unknown?row.Groove.Note:"chấn thường"))
                +"\r\nĐã đọc "+row.Samples.Count+" mặt cắt. "+row.CoverageNote
                +(row.Representative==null?"":"\r\nDòng đang hiển thị: s="+Number(row.Representative.PositionMm)+" mm từ đầu đường chấn; các số cùng một mặt cắt.")
                +(row.Representative?.Section==null?"":"\r\nCụm đang hiển thị: "+row.Representative.Section.Context)
                +(row.Representative==null?"":string.Concat(row.Representative.Checks.Select(c=>"\r\n"+c.Section.Context+": "+c.Section.Dimension+"="+Number(c.Section.DimensionMm)
                    +" mm — "+StatusText(c.Result.Status))))
                +(row.Evaluation?.Rule==null?"":"\r\nGiới hạn: "+row.Evaluation.Rule.Id)
                +"\r\nA/B/C là kích thước phủ bì. Kiểm riêng từng kích thước: L so cột A; Z so cột B; C so cột C.";
        }
        private void Locate()
        {
            var row=grid.CurrentRow?.Tag as BendMinimumScanRow;
            if(row?.Site==null||session==null||!session.IsCurrent()){SetStatus("Model đã thay đổi; hãy Quét lại trước khi chọn vị trí.",true);return;}
            try {
                session.Model.ClearSelection2(true);bool selected=false;
                foreach(var face in row.Site.Faces)selected|=((IEntity)face).Select4(selected,null);
                if(!selected)SetStatus("Không chọn được mặt chấn; hãy Quét lại.",true);
            }catch(Exception ex){SetStatus("Không chọn được vị trí: "+ex.Message,true);}
        }
        private string ReportText()=>"CHECK BẺ TỐI THIỂU\r\n"+modelLabel.Text+"\r\n"+Summary()+"\r\nExcel: "+source?.SourcePath;
        private void SetStatus(string message,bool error){statusLabel.Text=message;statusLabel.ForeColor=error?TaiTheme.Danger:TaiTheme.TextMuted;}
        private static string Number(double? value)=>value.HasValue?value.Value.ToString("0.###",CultureInfo.InvariantCulture):"—";
        private static bool IsOk(BendMinimumStatus status)=>status==BendMinimumStatus.MeetsMinimum||status==BendMinimumStatus.GroovedAccepted;
        private static string StatusText(BendMinimumStatus status)=>status==BendMinimumStatus.GroovedAccepted?"OK (cạnh bào)":status==BendMinimumStatus.MeetsMinimum?"Đạt tối thiểu":status==BendMinimumStatus.BelowMinimum?"Không đạt":status==BendMinimumStatus.Conditional?"Cần xác minh":"Chưa đủ dữ liệu";
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!closing){closing=true;generation++;scanTimer.Stop();sourceTimer.Stop();scanTimer.Dispose();sourceTimer.Dispose();lifetime.Cancel();session?.Dispose();}
            base.Dispose(disposing);
        }
    }

    internal sealed class MinimumSectionPreview : Control
    {
        private BendMinimumSection section;
        internal BendMinimumSection Section{get=>section;set{section=value;Invalidate();}}
        internal MinimumSectionPreview(){DoubleBuffered=true;BackColor=TaiTheme.Surface;Name="minimumSectionPreview";}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var border=new Pen(TaiTheme.Border))e.Graphics.DrawRectangle(border,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
            if(section==null){TextRenderer.DrawText(e.Graphics,"Mặt cắt thực tế\r\nA/B/C phủ bì",TaiTheme.Small,ClientRectangle,TaiTheme.TextFaint,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);return;}
            var points=section.Boundary.SelectMany(c=>new[]{c.A,c.B,c.IsArc?c.Center:c.A}).ToList();
            double minX=points.Min(p=>p.X),maxX=points.Max(p=>p.X),minY=points.Min(p=>p.Y),maxY=points.Max(p=>p.Y);
            double scale=Math.Min(Math.Max(1,Width-85)/(maxX-minX+1e-12),Math.Max(1,Height-70)/(maxY-minY+1e-12));
            Func<SectionProfilePlanner.Point,PointF> project=p=>new PointF((float)(40+(p.X-minX)*scale),(float)(Height-35-(p.Y-minY)*scale));
            foreach(var curve in section.Boundary){
                bool local=section.LocalBoundary==null||section.LocalBoundary.Contains(curve);
                using(var outline=new Pen(local?TaiTheme.Accent:TaiTheme.TextFaint,local?1.8f:1f)){
                if(!curve.IsArc)e.Graphics.DrawLine(outline,project(curve.A),project(curve.B));
                else{double start=Math.Atan2(curve.A.Y-curve.Center.Y,curve.A.X-curve.Center.X),radius=(curve.A-curve.Center).Length;var line=new PointF[33];
                    for(int i=0;i<line.Length;i++){double angle=start+curve.SweepAngleRadians*i/(line.Length-1);line[i]=project(new SectionProfilePlanner.Point(curve.Center.X+radius*Math.Cos(angle),curve.Center.Y+radius*Math.Sin(angle)));}e.Graphics.DrawLines(outline,line);
                }
                }
            }
            using(var pen=new Pen(TaiTheme.Accent,1))foreach(var d in section.Datums){
                PointF a=project(d.Start),b=project(d.End);double length=Math.Sqrt((b.X-a.X)*(b.X-a.X)+(b.Y-a.Y)*(b.Y-a.Y));
                if(length<1)continue;float nx=(float)(-(b.Y-a.Y)/length*14),ny=(float)((b.X-a.X)/length*14);
                var da=new PointF(a.X+nx,a.Y+ny);var db=new PointF(b.X+nx,b.Y+ny);e.Graphics.DrawLine(pen,a,da);e.Graphics.DrawLine(pen,b,db);e.Graphics.DrawLine(pen,da,db);
                TextRenderer.DrawText(e.Graphics,d.Name+" "+d.ValueMm.ToString("0.##",CultureInfo.InvariantCulture),TaiTheme.Small,new Point((int)((da.X+db.X)/2),(int)((da.Y+db.Y)/2)),TaiTheme.Accent);
            }
            TextRenderer.DrawText(e.Graphics,section.Shape+" • mm phủ bì",TaiTheme.Small,new Point(8,5),TaiTheme.TextMuted);
        }
    }
}
