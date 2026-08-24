using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    public sealed class RoundHolePreviewForm : Form
    {
        private static readonly List<RoundHolePreviewForm> OpenForms =
            new List<RoundHolePreviewForm>();

        private List<PreviewItem> items;
        private readonly ListBox listParts;
        private readonly PreviewCanvas canvas;
        private readonly Label lblInfo;

        private RoundHolePreviewForm(List<PreviewItem> previewItems)
        {
            items = previewItems ?? new List<PreviewItem>();
            Text = "CHECK ROUND - 2D PREVIEW";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 500);
            Size = new Size(1080, 700);
            ShowInTaskbar = true;
            MinimizeBox = true;
            Font = new Font("Meiryo UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 128);
            BackColor = Color.White;

            Panel leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 196,
                Padding = new Padding(10, 8, 10, 10),
                BackColor = Color.FromArgb(247, 249, 252)
            };
            Controls.Add(leftPanel);

            Label title = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                Text = "CHI TIET CAN KIEM TRA",
                Font = new Font("Meiryo UI", 10F, FontStyle.Bold, GraphicsUnit.Point, 128),
                ForeColor = Color.FromArgb(35, 74, 120),
                TextAlign = ContentAlignment.MiddleLeft
            };
            leftPanel.Controls.Add(title);

            listParts = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(32, 55, 78),
                ItemHeight = 24
            };
            listParts.SelectedIndexChanged += ListParts_SelectedIndexChanged;
            leftPanel.Controls.Add(listParts);
            listParts.BringToFront();

            Panel bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                Padding = new Padding(10, 6, 10, 6),
                BackColor = Color.FromArgb(247, 249, 252)
            };
            Controls.Add(bottomPanel);

            Button btnClose = CreateButton("DONG", 78);
            btnClose.Dock = DockStyle.Right;
            btnClose.Click += delegate { MinimizeToTaskbar(); };
            bottomPanel.Controls.Add(btnClose);

            Button btnFit = CreateButton("FIT", 68);
            btnFit.Dock = DockStyle.Right;
            btnFit.Click += delegate { canvas.Fit(); };
            bottomPanel.Controls.Add(btnFit);

            Button btnZoomOut = CreateButton("-", 42);
            btnZoomOut.Dock = DockStyle.Right;
            btnZoomOut.Click += delegate { canvas.ChangeZoom(0.8f); };
            bottomPanel.Controls.Add(btnZoomOut);

            Button btnZoomIn = CreateButton("+", 42);
            btnZoomIn.Dock = DockStyle.Right;
            btnZoomIn.Click += delegate { canvas.ChangeZoom(1.25f); };
            bottomPanel.Controls.Add(btnZoomIn);

            lblInfo = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(55, 66, 78)
            };
            bottomPanel.Controls.Add(lblInfo);
            lblInfo.BringToFront();

            canvas = new PreviewCanvas
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            Controls.Add(canvas);
            canvas.BringToFront();

            foreach (PreviewItem item in items)
                listParts.Items.Add(item);
            if (listParts.Items.Count > 0)
                listParts.SelectedIndex = 0;
        }

        private void MinimizeToTaskbar()
        {
            try
            {
                ShowInTaskbar = true;
                WindowState = FormWindowState.Minimized;
            }
            catch { }
        }

        private void LoadPreviewItems(List<PreviewItem> previewItems)
        {
            items = previewItems ?? new List<PreviewItem>();
            listParts.BeginUpdate();
            try
            {
                listParts.Items.Clear();
                foreach (PreviewItem item in items)
                    listParts.Items.Add(item);
                listParts.SelectedIndex = listParts.Items.Count > 0 ? 0 : -1;
            }
            finally
            {
                listParts.EndUpdate();
            }

            if (listParts.Items.Count == 0)
            {
                canvas.Item = null;
                lblInfo.Text = "";
            }
        }

        public static int ShowPreview(List<RoundHoleRowResult> results)
        {
            List<PreviewItem> previewItems = BuildItems(results);
            if (previewItems.Count == 0)
                return 0;

            OpenForms.RemoveAll(openForm => openForm == null || openForm.IsDisposed);
            if (OpenForms.Count > 0)
            {
                RoundHolePreviewForm existing = OpenForms[OpenForms.Count - 1];
                existing.LoadPreviewItems(previewItems);
                existing.RestoreAndActivate();
                return previewItems.Count;
            }

            RoundHolePreviewForm form = new RoundHolePreviewForm(previewItems);
            OpenForms.Add(form);
            form.FormClosed += delegate
            {
                OpenForms.Remove(form);
                form.Dispose();
            };
            form.Show();
            form.RestoreAndActivate();
            return previewItems.Count;
        }

        public static void BringLatestToFront()
        {
            if (OpenForms.Count == 0)
                return;
            RoundHolePreviewForm form = OpenForms[OpenForms.Count - 1];
            if (form == null || form.IsDisposed)
                return;
            form.RestoreAndActivate();
        }

        private void RestoreAndActivate()
        {
            try
            {
                if (!Visible)
                    Show();
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;
                TopMost = true;
                Activate();
                BringToFront();
                TopMost = false;
            }
            catch { }
        }

        private static List<PreviewItem> BuildItems(List<RoundHoleRowResult> results)
        {
            List<PreviewItem> items = new List<PreviewItem>();
            Dictionary<RoundHolePreviewData, PreviewItem> byData =
                new Dictionary<RoundHolePreviewData, PreviewItem>();
            if (results == null)
                return items;

            foreach (RoundHoleRowResult row in results)
            {
                if (row == null || row.PreviewData == null
                    || (row.Status != "NG" && row.Status != "CHECK"))
                {
                    continue;
                }

                PreviewItem item;
                if (!byData.TryGetValue(row.PreviewData, out item))
                {
                    item = new PreviewItem
                    {
                        Data = row.PreviewData,
                        BuhinNo = row.BuhinNo ?? "",
                        PartPath = row.PartPath ?? ""
                    };
                    byData.Add(row.PreviewData, item);
                    items.Add(item);
                }
                item.Rows.Add(row);
            }

            foreach (PreviewItem item in items)
            {
                int marker = 0;
                foreach (RoundHoleRowResult row in item.Rows.OrderBy(r => r.HoleNumber))
                {
                    marker++;
                    row.MarkerId = "NG-" + marker;
                    RoundHolePreviewPath path = item.Data.Paths.FirstOrDefault(
                        p => p.HoleNumber == row.HoleNumber);
                    if (path != null)
                    {
                        path.Status = row.Status;
                        path.MarkerId = row.MarkerId;
                    }
                }
            }
            return items;
        }

        private void ListParts_SelectedIndexChanged(object sender, EventArgs e)
        {
            PreviewItem item = listParts.SelectedItem as PreviewItem;
            canvas.Item = item;
            if (item == null)
            {
                lblInfo.Text = "";
                return;
            }

            int ng = item.Rows.Count(row => row.Status == "NG");
            int check = item.Rows.Count(row => row.Status == "CHECK");
            lblInfo.Text = "Buhin No.: " + item.BuhinNo
                + "    NG: " + ng + "    CHECK: " + check
                + "    " + (string.IsNullOrWhiteSpace(item.Data.ProjectionSource)
                    ? "Chieu: mat phang Part"
                    : item.Data.ProjectionSource)
                + "    Mouse wheel: zoom";
        }

        private static Button CreateButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(35, 74, 120),
                Font = new Font("Meiryo UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 128),
                Margin = new Padding(4)
            };
        }

        private sealed class PreviewItem
        {
            public string BuhinNo;
            public string PartPath;
            public RoundHolePreviewData Data;
            public readonly List<RoundHoleRowResult> Rows = new List<RoundHoleRowResult>();

            public override string ToString()
            {
                int ng = Rows.Count(row => row.Status == "NG");
                int check = Rows.Count(row => row.Status == "CHECK");
                return (BuhinNo.Length == 0 ? "(khong co so)" : BuhinNo)
                    + "   NG " + ng + " / CHECK " + check;
            }
        }

        private sealed class PreviewCanvas : Control
        {
            private PreviewItem item;
            private float zoom = 1.0f;

            public PreviewCanvas()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                    true);
                TabStop = true;
                MouseWheel += PreviewCanvas_MouseWheel;
                MouseEnter += delegate { Focus(); };
            }

            public PreviewItem Item
            {
                get { return item; }
                set
                {
                    item = value;
                    zoom = 1.0f;
                    Invalidate();
                }
            }

            public void Fit()
            {
                zoom = 1.0f;
                Invalidate();
            }

            public void ChangeZoom(float factor)
            {
                zoom = Math.Max(0.25f, Math.Min(8.0f, zoom * factor));
                Invalidate();
            }

            private void PreviewCanvas_MouseWheel(object sender, MouseEventArgs e)
            {
                ChangeZoom(e.Delta > 0 ? 1.15f : 0.87f);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Color.White);
                if (item == null || item.Data == null
                    || (item.Data.Paths.Count == 0 && item.Data.DrawingPaths.Count == 0))
                {
                    DrawCenteredText(e.Graphics, "Khong co du lieu preview 2D.");
                    return;
                }

                // Component Drawing da bien doi day du loop Flat-Pattern sang toa do
                // Drawing View. Dung loop day du de preview vi visible edge co the thieu.
                bool useCompleteComponentPaths = item.Data.Paths.Count > 0
                    && !string.IsNullOrWhiteSpace(item.Data.ProjectionSource)
                    && item.Data.ProjectionSource.StartsWith(
                        "Component Drawing:",
                        StringComparison.OrdinalIgnoreCase);
                List<RoundHolePreviewPath> displayPaths = useCompleteComponentPaths
                    ? item.Data.Paths
                    : (item.Data.DrawingPaths.Count > 0
                        ? item.Data.DrawingPaths
                        : item.Data.Paths);
                RectangleF modelBounds = GetModelBounds(displayPaths);
                if (modelBounds.Width <= 1e-12f || modelBounds.Height <= 1e-12f)
                {
                    DrawCenteredText(e.Graphics, "Bien dang khong hop le de hien thi.");
                    return;
                }

                const float sideMargin = 56F;
                const float topMarkerLane = 54F;
                const float bottomMargin = 42F;
                float availableWidth = Math.Max(1F, ClientSize.Width - sideMargin * 2F);
                float availableHeight = Math.Max(
                    1F,
                    ClientSize.Height - topMarkerLane - bottomMargin);
                float baseScale = Math.Min(
                    availableWidth / modelBounds.Width,
                    availableHeight / modelBounds.Height);
                float scale = baseScale * zoom;
                float drawingWidth = modelBounds.Width * scale;
                float drawingHeight = modelBounds.Height * scale;
                float originX = (ClientSize.Width - drawingWidth) / 2F;
                float originY = topMarkerLane
                    + (availableHeight - drawingHeight) / 2F;

                using (Pen outerPen = new Pen(Color.FromArgb(30, 30, 30), 2.2F))
                using (Pen normalPen = new Pen(Color.FromArgb(45, 55, 62), 1.45F))
                using (Pen errorPen = new Pen(Color.FromArgb(220, 35, 35), 2.6F))
                using (Pen leaderPen = new Pen(Color.FromArgb(220, 35, 35), 1.15F))
                using (Brush bodyBrush = new SolidBrush(Color.FromArgb(232, 247, 249)))
                using (Brush holeBrush = new SolidBrush(Color.White))
                using (Brush markerBrush = new SolidBrush(Color.Red))
                using (Brush markerBackBrush = new SolidBrush(Color.FromArgb(255, 247, 247)))
                using (Font markerFont = new Font("Meiryo UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point, 128))
                {
                    List<ScreenPath> screenPaths = new List<ScreenPath>();
                    foreach (RoundHolePreviewPath path in displayPaths)
                    {
                        if (path == null || path.Points.Count < 2)
                            continue;
                        PointF[] points = path.Points
                            .Select(point => ToScreen(
                                point,
                                modelBounds,
                                originX,
                                originY,
                                drawingHeight,
                                scale))
                            .ToArray();
                        screenPaths.Add(new ScreenPath { Source = path, Points = points });
                    }

                    foreach (ScreenPath screenPath in screenPaths.Where(p => p.Source.IsOuter))
                    {
                        using (GraphicsPath fillPath = CreateClosedGraphicsPath(screenPath.Points))
                        {
                            if (fillPath != null)
                                e.Graphics.FillPath(bodyBrush, fillPath);
                        }
                    }

                    foreach (ScreenPath screenPath in screenPaths)
                    {
                        RoundHolePreviewPath path = screenPath.Source;
                        PointF[] points = screenPath.Points;
                        bool abnormal = path.Status == "NG" || path.Status == "CHECK";
                        Pen pen = abnormal ? errorPen : (path.IsOuter ? outerPen : normalPen);
                        if (!path.IsOuter)
                        {
                            using (GraphicsPath holePath = CreateClosedGraphicsPath(points))
                            {
                                if (holePath != null)
                                    e.Graphics.FillPath(holeBrush, holePath);
                            }
                        }
                        e.Graphics.DrawLines(pen, points);
                    }

                    List<MarkerLayout> markers = item.Data.Paths
                        .Where(p => p != null && p.Points.Count >= 2
                            && (p.Status == "NG" || p.Status == "CHECK")
                            && !string.IsNullOrWhiteSpace(p.MarkerId))
                        .Select(p =>
                        {
                            PointF[] markerPoints = p.Points
                                .Select(point => ToScreen(
                                    point,
                                    modelBounds,
                                    originX,
                                    originY,
                                    drawingHeight,
                                    scale))
                                .ToArray();
                            return new MarkerLayout
                            {
                                Id = p.MarkerId,
                                Center = GetCenter(markerPoints),
                                Bounds = GetScreenBounds(markerPoints)
                            };
                        })
                        .OrderBy(p => p.Center.X)
                        .ToList();

                    if (markers.Count > 0)
                    {
                        float laneLeft = Math.Max(12F, originX);
                        float laneRight = Math.Min(ClientSize.Width - 12F, originX + drawingWidth);
                        float spacing = markers.Count == 1
                            ? 0F
                            : (laneRight - laneLeft) / (markers.Count - 1);
                        float labelY = Math.Max(8F, originY - 34F);
                        for (int i = 0; i < markers.Count; i++)
                        {
                            MarkerLayout marker = markers[i];
                            float anchorX = markers.Count == 1
                                ? marker.Center.X
                                : laneLeft + spacing * i;
                            SizeF textSize = e.Graphics.MeasureString(marker.Id, markerFont);
                            RectangleF labelRect = new RectangleF(
                                anchorX - textSize.Width / 2F - 5F,
                                labelY,
                                textSize.Width + 10F,
                                textSize.Height + 4F);
                            PointF target = new PointF(
                                marker.Center.X,
                                Math.Max(marker.Bounds.Top, originY));
                            PointF elbow = new PointF(anchorX, labelRect.Bottom + 5F);
                            e.Graphics.DrawLine(leaderPen, target, elbow);
                            e.Graphics.FillEllipse(
                                markerBrush,
                                target.X - 3F,
                                target.Y - 3F,
                                6F,
                                6F);
                            e.Graphics.FillRectangle(markerBackBrush, labelRect);
                            e.Graphics.DrawRectangle(
                                leaderPen,
                                labelRect.X,
                                labelRect.Y,
                                labelRect.Width,
                                labelRect.Height);
                            e.Graphics.DrawString(
                                marker.Id,
                                markerFont,
                                markerBrush,
                                labelRect.X + 5F,
                                labelRect.Y + 2F);
                        }
                    }
                }
            }

            private sealed class ScreenPath
            {
                public RoundHolePreviewPath Source;
                public PointF[] Points;
            }

            private sealed class MarkerLayout
            {
                public string Id;
                public PointF Center;
                public RectangleF Bounds;
            }

            private static GraphicsPath CreateClosedGraphicsPath(PointF[] points)
            {
                if (points == null || points.Length < 3)
                    return null;
                PointF first = points[0];
                PointF last = points[points.Length - 1];
                float dx = first.X - last.X;
                float dy = first.Y - last.Y;
                if (dx * dx + dy * dy > 4F)
                    return null;
                GraphicsPath path = new GraphicsPath();
                path.AddLines(points);
                path.CloseFigure();
                return path;
            }

            private static RectangleF GetScreenBounds(PointF[] points)
            {
                if (points == null || points.Length == 0)
                    return RectangleF.Empty;
                float minX = points[0].X;
                float minY = points[0].Y;
                float maxX = points[0].X;
                float maxY = points[0].Y;
                foreach (PointF point in points)
                {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                }
                return RectangleF.FromLTRB(minX, minY, maxX, maxY);
            }

            private void DrawCenteredText(Graphics graphics, string text)
            {
                using (Brush brush = new SolidBrush(Color.Gray))
                {
                    SizeF size = graphics.MeasureString(text, Font);
                    graphics.DrawString(
                        text,
                        Font,
                        brush,
                        (ClientSize.Width - size.Width) / 2F,
                        (ClientSize.Height - size.Height) / 2F);
                }
            }

            private static RectangleF GetModelBounds(List<RoundHolePreviewPath> paths)
            {
                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;
                foreach (RoundHolePreviewPath path in paths)
                {
                    if (path == null)
                        continue;
                    foreach (RoundHolePreviewPoint point in path.Points)
                    {
                        minX = Math.Min(minX, point.X);
                        minY = Math.Min(minY, point.Y);
                        maxX = Math.Max(maxX, point.X);
                        maxY = Math.Max(maxY, point.Y);
                    }
                }
                if (minX == double.MaxValue)
                    return RectangleF.Empty;
                return new RectangleF(
                    (float)minX,
                    (float)minY,
                    (float)(maxX - minX),
                    (float)(maxY - minY));
            }

            private static PointF ToScreen(
                RoundHolePreviewPoint point,
                RectangleF bounds,
                float originX,
                float originY,
                float drawingHeight,
                float scale)
            {
                float x = originX + ((float)point.X - bounds.Left) * scale;
                float y = originY + drawingHeight - ((float)point.Y - bounds.Top) * scale;
                return new PointF(x, y);
            }

            private static PointF GetCenter(PointF[] points)
            {
                float x = 0F;
                float y = 0F;
                foreach (PointF point in points)
                {
                    x += point.X;
                    y += point.Y;
                }
                return points.Length == 0
                    ? PointF.Empty
                    : new PointF(x / points.Length, y / points.Length);
            }
        }
    }
}
