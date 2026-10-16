using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.DxfChecking
{
    // Resolves inputs for DXF comparison. It has no Drawing-check rules, result
    // types, background sessions or references to the Check Drawing command.
    internal sealed class DxfBomDrawingSourceResolver
    {
        // Match only the label in its original text so stripping a mixed-width
        // title does not normalize or otherwise change the actual filename.
        static readonly Regex DxfFileNamePrefix = new Regex(
            @"^\s*[DＤ]\s*[XＸ]\s*[FＦ]\s*(?:[フﾌ]\s*[ァｧ]\s*[イｲ]\s*[ルﾙ]\s*名|[FＦ]\s*[IＩ]\s*[LＬ]\s*[EＥ]\s*[NＮ]\s*[AＡ]\s*[MＭ]\s*[EＥ])\s*[:：=＝]?\s*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        readonly DataGridView grid;
        internal DxfBomDrawingSourceResolver(ISldWorks application, DataGridView bomGrid) { grid = bomGrid; }

        internal string ResolveBomDrawingPath(DataGridViewRow row, ModelDoc2 bomDocument, out string modelPath)
        {
            modelPath = ""; if (row == null) return "";
            var snapshot = row.Tag as BomDrawingReference;
            if (snapshot != null) { modelPath = snapshot.ModelPath ?? ""; return snapshot.DrawingPath ?? ""; }
            string filename = row.Cells.Count > 5 ? Convert.ToString(row.Cells[5].Value ?? "") : "";
            string stem = Path.GetFileNameWithoutExtension(filename);
            var components = row.Tag as object[];
            if (components != null)
            {
                foreach (Component2 component in components.OfType<Component2>())
                {
                    string path = component.GetPathName(); if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    modelPath = path; string drawing = AdjacentDrawing(path); if (drawing != null) return drawing;
                }
            }
            else
            {
                string path = row.Tag as string;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) { modelPath = path; string drawing = AdjacentDrawing(path); if (drawing != null) return drawing; }
            }
            if (!string.IsNullOrWhiteSpace(filename) && Path.IsPathRooted(filename)) { modelPath = filename; string drawing = AdjacentDrawing(filename); if (drawing != null) return drawing; }
            foreach (string directory in SearchDirectories(bomDocument))
            {
                if (string.IsNullOrWhiteSpace(stem)) break;
                if (string.IsNullOrWhiteSpace(modelPath))
                    foreach (string extension in new[] { ".SLDPRT", ".sldprt", ".SLDASM", ".sldasm" })
                    { string path = Path.Combine(directory, stem + extension); if (File.Exists(path)) { modelPath = path; break; } }
                foreach (string extension in new[] { ".SLDDRW", ".slddrw" })
                { string path = Path.Combine(directory, stem + extension); if (File.Exists(path)) return path; }
            }
            return "";
        }
        static string AdjacentDrawing(string path)
        {
            foreach (string extension in new[] { ".SLDDRW", ".slddrw" }) { string drawing = Path.ChangeExtension(path, extension); if (File.Exists(drawing)) return drawing; }
            return null;
        }
        List<string> SearchDirectories(ModelDoc2 bomDocument)
        {
            var directories = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = path => { if (string.IsNullOrWhiteSpace(path)) return; string directory = Path.GetDirectoryName(path); if (Directory.Exists(directory) && seen.Add(directory)) directories.Add(directory); };
            if (bomDocument != null) add(bomDocument.GetPathName());
            if (grid != null) foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var components = row.Tag as object[];
                if (components != null) foreach (Component2 component in components.OfType<Component2>()) add(component.GetPathName());
                else if (row.Tag is string) add((string)row.Tag);
                else if (row.Tag is BomDrawingReference) add(((BomDrawingReference)row.Tag).ModelPath);
            }
            return directories;
        }

        internal sealed class DisplayedNote
        {
            internal string Text, Raw;
            internal double X, Y;
            internal bool HasPosition;
        }
        internal string ReadDisplayedDxfName(ModelDoc2 model)
        {
            var drawing = model as DrawingDoc; if (drawing == null) return "";
            var sheet = drawing.GetCurrentSheet() as Sheet; double width = 0, height = 0;
            if (sheet != null) try { ((ISheet)sheet).GetSize(ref width, ref height); } catch { }
            var notes = new List<DisplayedNote>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<Note> add = note =>
            {
                if (note == null) return; string text = note.GetText() ?? ""; string raw = note.PropertyLinkedText ?? text;
                var annotation = note.GetAnnotation() as Annotation;
                if (annotation != null && annotation.Visible == (int)swAnnotationVisibilityState_e.swAnnotationHidden) return;
                var position = annotation == null ? null : annotation.GetPosition() as double[];
                bool hasPosition = position != null && position.Length >= 2;
                double x = hasPosition ? position[0] : 0, y = hasPosition ? position[1] : 0;
                if (hasPosition && width > 0 && height > 0 && (x < -.0002 || y < -.0002 || x > width + .0002 || y > height + .0002)) return;
                string key = text + "|" + raw + "|" + x.ToString("R", CultureInfo.InvariantCulture) + "|" + y.ToString("R", CultureInfo.InvariantCulture);
                if (seen.Add(key)) notes.Add(new DisplayedNote { Text = text.Trim(), Raw = raw, X = x, Y = y, HasPosition = hasPosition });
            };
            for (IView view = drawing.GetFirstView() as IView; view != null; view = view.GetNextView() as IView)
                for (Note note = view.GetFirstNote() as Note; note != null; note = note.GetNext() as Note) add(note);
            for (Annotation annotation = model.GetFirstAnnotation2() as Annotation; annotation != null; annotation = annotation.GetNext3() as Annotation)
                add(annotation.GetSpecificAnnotation() as Note);
            string fromNotes = ParseDisplayedDxfName(notes);
            if (!string.IsNullOrWhiteSpace(fromNotes)) return fromNotes;
            // Some templates use a named table cell instead of a title-block note.
            // Read only a cell explicitly identified as the DXF filename.
            var namedCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (IView view = drawing.GetFirstView() as IView; view != null; view = view.GetNextView() as IView)
            {
                var tables = view.GetTableAnnotations() as Array; if (tables == null) continue;
                foreach (ITableAnnotation table in tables)
                {
                    var annotation = table.GetAnnotation() as Annotation;
                    var position = annotation == null ? null : annotation.GetPosition() as double[];
                    if (position != null && position.Length >= 2 && width > 0 && height > 0 && (position[0] < -.0002 || position[1] < -.0002 || position[0] > width + .0002 || position[1] > height + .0002)) continue;
                    for (int row = 0; row < table.RowCount; row++) for (int column = 0; column < table.ColumnCount; column++)
                    {
                        string header = table.get_DisplayedText2(row, column, false);
                        if (!IsDxfFileNameLabel(header)) continue;
                        string value = column + 1 < table.ColumnCount ? table.get_DisplayedText2(row, column + 1, false) : "";
                        if ((string.IsNullOrWhiteSpace(value) || IsDxfFileNameLabel(value)) && row + 1 < table.RowCount) value = table.get_DisplayedText2(row + 1, column, false);
                        if (!string.IsNullOrWhiteSpace(value) && !IsDxfFileNameLabel(value)) namedCells.Add(value.Trim());
                    }
                }
            }
            if (namedCells.Count > 1) throw new InvalidDataException("Drawing có nhiều tên DXF khác nhau trong các ô tên file.");
            return namedCells.FirstOrDefault() ?? "";
        }
        internal static string ParseDisplayedDxfName(IEnumerable<DisplayedNote> notes)
        {
            var all = notes.ToList(); var explicitNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in all)
            {
                string normalizedRaw = Normalize(note.Raw ?? note.Text);
                if (normalizedRaw.Contains("DXFファイル名") || normalizedRaw.Contains("DXFFILENAME"))
                {
                    string name = DxfFileNamePrefix.Replace(note.Text ?? "", "", 1).Trim();
                    if (!string.IsNullOrWhiteSpace(name) && !IsDxfFileNameLabel(name)) explicitNames.Add(name);
                }
            }
            if (explicitNames.Count > 1) throw new InvalidDataException("Drawing có nhiều tên DXF khác nhau; cần chọn đúng bản xuất.");
            if (explicitNames.Count == 1) return explicitNames.First();
            // Preserve the existing title template's bottom-left 手配番号 / 部品番号
            // fragments. A random note containing .dxf is not accepted as a source.
            string job = "", part = "";
            foreach (var note in all.Where(n => n.HasPosition && n.Y <= .020 && !IsDxfFileNameLabel(n.Text)))
            {
                string text = (note.Text ?? "").Trim(), raw = note.Raw ?? "";
                if ((raw.Contains("\"手配番号\"") || (note.X >= .030 && note.X <= .050 && !text.Contains("/"))) && string.IsNullOrWhiteSpace(job) && !string.IsNullOrWhiteSpace(text)) job = text;
                if (text.StartsWith("/") || raw.Contains("/ $PRP:\"部品番号\"") || raw.Contains("/$PRP:\"部品番号\"")) part = text;
            }
            if (string.IsNullOrWhiteSpace(part))
            {
                // Keep the title block's same header band and column partition
                // rules for the fallback, without invoking any Drawing checker.
                var header = all.LastOrDefault(n => Normalize(n.Text) == "部品番号" && n.HasPosition);
                var quantity = all.LastOrDefault(n => Normalize(n.Text) == "数量" && n.HasPosition);
                if (header != null && quantity != null && header.X < quantity.X && Math.Abs(header.Y - quantity.Y) <= .010)
                {
                    double headerY = (header.Y + quantity.Y) / 2;
                    var columns = all.Where(n => n.HasPosition && n.X > header.X && n.X < quantity.X && Math.Abs(n.Y - headerY) <= .010 && (Normalize(n.Text) == "W" || Normalize(n.Text) == "L")).ToList();
                    columns.Add(quantity); double next = columns.Min(n => n.X), middle = (header.X + next) / 2;
                    double minimumX = header.X - (middle - header.X);
                    var value = all.Where(n => n != header && n != quantity && !columns.Contains(n) && !IsDxfFileNameLabel(n.Text) && n.HasPosition && headerY - n.Y >= .0005 && headerY - n.Y <= .025 && n.X >= minimumX && n.X <= middle).OrderBy(n => headerY - n.Y).FirstOrDefault();
                    if (value != null && !string.IsNullOrWhiteSpace(value.Text)) part = "/" + value.Text.Trim();
                }
            }
            if (string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(part)) return "";
            string compactJob = Regex.Replace(Normalize(job).Replace('／', '/'), @"(?<=\d)[-－−](?=\d)", "/");
            string compactPart = Regex.Replace(Normalize(part).Replace('／', '/'), @"(?<=\d)[-－−](?=\d)", "/");
            return compactPart.StartsWith("/", StringComparison.Ordinal) && compactJob.EndsWith(compactPart, StringComparison.OrdinalIgnoreCase) ? job.Trim() : (job + " " + part).Trim();
        }
        internal static bool IsDxfFileNameLabel(string text)
        {
            string value = (text ?? "").Trim();
            Match label = DxfFileNamePrefix.Match(value);
            return label.Success && label.Length == value.Length;
        }
        static string Normalize(string text) { return Regex.Replace((text ?? "").Normalize(NormalizationForm.FormKC), @"\s+", "").ToUpperInvariant(); }
    }
}
