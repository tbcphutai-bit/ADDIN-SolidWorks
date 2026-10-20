using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed partial class AutoVCutSectionCommand
    {
        private sealed class PartSectionMember
        {
            internal string Name;
            internal bool Vertical;
            internal AssemblySectionLayout Annotations;
            internal List<PartSectionMarkLayout> Marks;
        }

        private static bool SamePartSectionScope(string path, string config,
            string otherPath, string otherConfig)
        {
            return !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(otherPath) &&
                string.Equals(path, otherPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(config ?? "", otherConfig ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static SolidWorks.Interop.sldworks.View GetPartSectionBase(
            SolidWorks.Interop.sldworks.View view)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int depth = 0; view != null && depth < 16; depth++)
            {
                if (!visited.Add(view.GetName2())) return null;
                if (view.GetSection() == null) return view;
                view = view.GetBaseView() as SolidWorks.Interop.sldworks.View;
            }
            return null;
        }

        private bool SamePartSectionBase(SolidWorks.Interop.sldworks.View candidate,
            SolidWorks.Interop.sldworks.View parent)
        {
            var root = GetPartSectionBase(candidate);
            var model = root == null ? null : root.ReferencedDocument as ModelDoc2;
            var expected = parent.ReferencedDocument as ModelDoc2;
            if (model == null || expected == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
                return false;
            string path = expected.GetPathName();
            if (!string.IsNullOrWhiteSpace(path))
                return SamePartSectionScope(path, parent.ReferencedConfiguration,
                    model.GetPathName(), root.ReferencedConfiguration);
            return app.IsSame(model, expected) == (int)swObjectEquality.swObjectSame &&
                string.Equals(parent.ReferencedConfiguration, root.ReferencedConfiguration,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static double[] GetPartProfileBounds(SolidWorks.Interop.sldworks.View view)
        {
            var profile = GetDisplayedSegments(view);
            return profile.Count == 0 ? view.GetOutline() as double[] : new[] {
                profile.Min(s => Math.Min(s.X1, s.X2)), profile.Min(s => Math.Min(s.Y1, s.Y2)),
                profile.Max(s => Math.Max(s.X1, s.X2)), profile.Max(s => Math.Max(s.Y1, s.Y2)) };
        }

        private static List<double> GetPartBreakProtectedPoints(
            List<ProjectedSegmentInfo> profile, bool vertical)
        {
            var points = new List<double>();
            foreach (var segment in profile)
            {
                double across = vertical ? Math.Abs(segment.X2 - segment.X1) : Math.Abs(segment.Y2 - segment.Y1);
                if (segment.Length <= 1e-9 || across <= .00001 || across / segment.Length <= .05) continue;
                double a = vertical ? segment.Y1 : segment.X1, b = vertical ? segment.Y2 : segment.X2;
                double min = Math.Min(a, b), max = Math.Max(a, b);
                int samples = Math.Max(1, (int)Math.Ceiling((max - min) / .012));
                for (int i = 0; i <= samples; i++) points.Add(min + (max - min) * i / samples);
            }
            return points;
        }

        private bool TryGetPartUnitSize(SolidWorks.Interop.sldworks.View parent,
            out double width, out double height)
        {
            width = height = 0;
            var model = parent.ReferencedDocument as ModelDoc2;
            var part = model as PartDoc;
            if (part != null && string.Equals(model.ConfigurationManager.ActiveConfiguration.Name,
                parent.ReferencedConfiguration, StringComparison.OrdinalIgnoreCase))
            {
                double[] box = part.GetPartBox(true) as double[];
                if (box != null && box.Length >= 6)
                {
                    var sizes = new[] { Math.Abs(box[3] - box[0]), Math.Abs(box[4] - box[1]),
                        Math.Abs(box[5] - box[2]) }.OrderByDescending(x => x).ToArray();
                    width = sizes[0]; height = sizes[1];
                }
            }
            if (!IsPositiveAssemblySize(width) || !IsPositiveAssemblySize(height))
            {
                double[] box = GetPartProfileBounds(parent);
                if (box == null || box.Length < 4 || parent.ScaleDecimal <= 0) return false;
                width = Math.Max(box[2] - box[0], box[3] - box[1]) / parent.ScaleDecimal;
                height = Math.Min(box[2] - box[0], box[3] - box[1]) / parent.ScaleDecimal;
            }
            return IsPositiveAssemblySize(width) && IsPositiveAssemblySize(height);
        }

        // All cap calculations use unbroken geometry. A protected jog in one
        // section limits the entire group's scale, never just that section.
        private static double GetPartCommonScaleCap(SolidWorks.Interop.sldworks.View view,
            bool vertical, double chosen, double targetWidth, double targetHeight)
        {
            var profile = GetDisplayedSegments(view);
            double[] box = GetPartProfileBounds(view);
            if (box == null || box.Length < 4 || view.ScaleDecimal <= 0) return chosen;
            double ratio = chosen / view.ScaleDecimal;
            double width = (box[2] - box[0]) * ratio, height = (box[3] - box[1]) * ratio;
            const double margin = .012;
            double cap = chosen;
            double cross = vertical ? width : height, crossTarget = vertical ? targetWidth : targetHeight;
            if (cross + margin > crossTarget)
                cap = Math.Min(cap, chosen * Math.Max(.002, crossTarget - margin) / Math.Max(cross, 1e-9));
            double length = vertical ? height : width, target = vertical ? targetHeight : targetWidth;
            if (length + margin > target)
            {
                var scaled = profile.Select(s => new ProjectedSegmentInfo {
                    X1 = s.X1 * ratio, Y1 = s.Y1 * ratio, X2 = s.X2 * ratio, Y2 = s.Y2 * ratio }).ToList();
                double first, second;
                double min = (vertical ? box[1] : box[0]) * ratio - margin / 2;
                double max = min + length + margin;
                if (profile.Count == 0 || !TryPlanSectionBreak(min, max, target,
                    GetPartBreakProtectedPoints(scaled, vertical), out first, out second))
                    cap = Math.Min(cap, chosen * Math.Max(.002, target - margin) / Math.Max(length, 1e-9));
            }
            return cap;
        }

        private SolidWorks.Interop.sldworks.View SynchronizePartSectionLayout(
            ModelDoc2 drawing, DrawingDoc drawingDoc, SolidWorks.Interop.sldworks.View section,
            SolidWorks.Interop.sldworks.View parent, bool verticalCut, List<VCutBendInfo> relevant,
            bool hasUra, bool omoteVisible, out bool omoteVerified, out int blocks,
            out List<string> failedMarks, out string notice)
        {
            blocks = 0; omoteVerified = false; failedMarks = new List<string>(); notice = "";
            string newName = section.GetName2();
            Sheet sheet = drawingDoc.GetCurrentSheet() as Sheet;
            double sheetWidth = 0, sheetHeight = 0, unitWidth, unitHeight;
            if (sheet == null || !TryGetPartUnitSize(parent, out unitWidth, out unitHeight))
                throw new InvalidOperationException("Không đọc được kích thước chi tiết để tính tỉ lệ chung.");
            sheet.GetSize(ref sheetWidth, ref sheetHeight);
            var members = new List<PartSectionMember>();
            for (var view = drawingDoc.GetFirstView() as SolidWorks.Interop.sldworks.View;
                view != null; view = view.GetNextView() as SolidWorks.Interop.sldworks.View)
            {
                if (view.GetSection() == null || !SamePartSectionBase(view, parent)) continue;
                // Unbreak/scale/rebreak may replace the temporary cut body.
                // Remove our generated symbol before that topology changes;
                // it is recreated on the final visible skin below.
                RemoveOwnedOmoteMarks(drawing, view);
                members.Add(new PartSectionMember { Name = view.GetName2(),
                    Annotations = view.GetName2() == newName ? null : CaptureAssemblySectionLayout(view) });
            }
            if (!members.Any(m => m.Name == newName))
                throw new InvalidOperationException("Không xác định được nhóm mặt cắt của chi tiết.");
            members = members.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
            foreach (var member in members)
            {
                var view = UnbreakAssemblySectionView(drawing, drawingDoc, FindSectionView(drawingDoc, member.Name));
                double[] box = GetPartProfileBounds(view);
                member.Vertical = member.Name == newName ? verticalCut : box != null && box[3] - box[1] > box[2] - box[0];
                member.Marks = CapturePartSectionMarks(view);
            }
            double chosen = PartSectionLayoutPlanner.CalculatePreferredScale(unitWidth, unitHeight, sheetWidth, sheetHeight);
            if (!IsPositiveAssemblySize(chosen)) throw new InvalidOperationException("Không tính được tỉ lệ chung.");
            for (int capPass = 0; capPass < 3; capPass++)
            {
                double cap = chosen;
                foreach (var member in members)
                {
                    double targetWidth, targetHeight;
                    PartSectionLayoutPlanner.GetTargets(sheetWidth, sheetHeight, member.Vertical, out targetWidth, out targetHeight);
                    cap = Math.Min(cap, GetPartCommonScaleCap(FindSectionView(drawingDoc, member.Name),
                        member.Vertical, chosen, targetWidth, targetHeight));
                }
                double reduced = PartSectionLayoutPlanner.RoundScale(cap, true);
                if (Math.Abs(reduced - chosen) < 1e-9) break;
                chosen = reduced;
            }

            bool inserted = false, placed = false;
            var notes = new List<string>();
            for (int pass = 0; pass < 5 && !placed; pass++)
            {
                foreach (var member in members)
                {
                    var view = UnbreakAssemblySectionView(drawing, drawingDoc, FindSectionView(drawingDoc, member.Name));
                    view.UseParentScale = false; view.UseSheetScale = 0; view.ScaleDecimal = chosen;
                }
                drawing.EditRebuild3();
                foreach (var member in members)
                {
                    var view = FindSectionView(drawingDoc, member.Name);
                    string restored = RestorePartSectionMarks(view, member.Marks);
                    if (!string.IsNullOrWhiteSpace(restored)) notes.Add(restored);
                }
                section = FindSectionView(drawingDoc, newName);
                omoteVerified = hasUra && EnsureOmoteDisplaySide(drawing, section, section.GetSection() as DrSection, omoteVisible);
                if (!inserted)
                {
                    foreach (var bend in relevant)
                    {
                        bend.ProjectedCenterX = bend.ProjectedCenterY = bend.ProjectedCenterZ = null;
                        LogBendGeometry(bend, section);
                        if (InsertVCutBlock(drawing, drawingDoc, section, bend)) blocks++;
                        else failedMarks.Add(bend.FeatureName + " " + bend.MatchedDefinition.Mark);
                    }
                    inserted = true;
                    drawing.EditRebuild3();
                    members.First(m => m.Name == newName).Marks = CapturePartSectionMarks(section);
                }
                var sizes = new List<PartSectionLayoutSize>();
                double fit = 1;
                foreach (var member in members)
                {
                    double targetWidth, targetHeight;
                    PartSectionLayoutPlanner.GetTargets(sheetWidth, sheetHeight, member.Vertical, out targetWidth, out targetHeight);
                    string ignored;
                    var view = AutoBreakSectionView(drawing, drawingDoc, FindSectionView(drawingDoc, member.Name),
                        member.Vertical, member.Name == newName ? relevant : new List<VCutBendInfo>(),
                        member.Vertical ? targetHeight : targetWidth, out ignored, true);
                    double[] box = view.GetOutline() as double[];
                    if (box == null || box.Length < 4) throw new InvalidOperationException("Không đo được giới hạn mặt cắt.");
                    sizes.Add(new PartSectionLayoutSize { Name = member.Name, Vertical = member.Vertical,
                        Width = box[2] - box[0], Height = box[3] - box[1] });
                    fit = Math.Min(fit, Math.Min(targetWidth / (box[2] - box[0]), targetHeight / (box[3] - box[1])));
                }
                var occupied = new List<double[]>();
                for (var view = drawingDoc.GetFirstView() as SolidWorks.Interop.sldworks.View;
                    view != null; view = view.GetNextView() as SolidWorks.Interop.sldworks.View)
                    if (!members.Any(m => m.Name == view.GetName2()) && view.ReferencedDocument != null)
                    {
                        double[] box = view.GetOutline() as double[];
                        if (box != null && box.Length >= 4) occupied.Add(box);
                    }
                Dictionary<string, double[]> centers = null;
                placed = fit >= .98 && PartSectionLayoutPlanner.TryArrange(sizes, parent.GetOutline() as double[],
                    sheetWidth, sheetHeight, occupied, out centers);
                if (placed)
                {
                    foreach (var member in members)
                    {
                        var view = FindSectionView(drawingDoc, member.Name);
                        if (member.Annotations != null) RestoreAssemblySectionLayout(view, member.Annotations);
                        double[] box = view.GetOutline() as double[], position = view.Position as double[];
                        double[] center = centers[member.Name];
                        view.RemoveAlignment();
                        view.Position = new[] { position[0] + center[0] - (box[0] + box[2]) / 2,
                            position[1] + center[1] - (box[1] + box[3]) / 2 };
                    }
                    drawing.EditRebuild3();
                }
                else
                {
                    double next = PartSectionLayoutPlanner.RoundScale(chosen * Math.Min(.86, fit * .97), true);
                    if (!IsPositiveAssemblySize(next) || next >= chosen - 1e-9) break;
                    chosen = next;
                }
            }
            section = FindSectionView(drawingDoc, newName);
            foreach (var member in members)
            {
                if (Math.Abs(FindSectionView(drawingDoc, member.Name).ScaleDecimal - section.ScaleDecimal) > 1e-9)
                    throw new InvalidOperationException("Chưa đồng bộ đủ tỉ lệ các mặt cắt; hãy kiểm tra bản vẽ.");
                string markNotice;
                if (!UpdatePartOmoteSurfaceMark(drawing, drawingDoc,
                    FindSectionView(drawingDoc, member.Name), out markNotice))
                    notes.Add(member.Name + ": " + markNotice);
            }
            drawing.EditRebuild3();
            Log("PART SECTION LAYOUT shared=" + FormatSectionScale(section.ScaleDecimal) +
                " unitMm=(" + Mm(unitWidth) + "," + Mm(unitHeight) + ") sections=" + members.Count +
                " placed=" + placed + " sheet=" + sheet.GetName());
            if (!placed) notes.Add("Chưa tìm được bố trí không chồng lấn; hãy kiểm tra khoảng trống trên tờ.");
            notice = "Đã đồng bộ " + members.Count + " mặt cắt cùng chi tiết/configuration: " +
                FormatSectionScale(section.ScaleDecimal) + (notes.Count == 0 ? "" : "\n" + string.Join("\n", notes.Distinct()));
            return section;
        }
    }
}
