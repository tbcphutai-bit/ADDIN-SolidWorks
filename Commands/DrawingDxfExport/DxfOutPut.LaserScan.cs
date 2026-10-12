// Adapted from user-supplied DXF.zip; see SOURCE.md.
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Quét cạnh MARK và miệng lỗ côn/bậc trên một view MÀ KHÔNG vẽ gì (cơ chế xuất DXF
    /// 06/10/2026, chép từ ITool DxfExportService.LaserMark.cs / StepHoles.cs). Dùng bởi
    /// <see cref="BuildSemanticPlan"/>; đường vẽ marker cũ (AssignCutExtrudeLaserMarkLayer) còn
    /// trong DxfOutPut.cs nhưng không còn được gọi.
    /// </summary>
    internal partial class DxfOutPut
    {
        /// <summary>Kết quả quét cạnh MARK trên một view (chưa vẽ gì).</summary>
        internal sealed class MarkEdgeScan
        {
            public List<VisibleEdgeReference> Marked { get; } = new List<VisibleEdgeReference>();
            public List<VisibleEdgeReference> Unmarked { get; } = new List<VisibleEdgeReference>();
            public HashSet<string> VisibleMarkFeatureNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> RecognizedDescriptions { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public int RecoveredEdgeCount { get; set; }
            public int OutlineDropped { get; set; }
        }

        /// <summary>
        /// Cạnh nhìn thấy nào của view thuộc feature MARK (nhận qua chủ sở hữu mặt,
        /// nhận lại bằng hình học, bỏ cạnh biên ngoài và lỗ thường bị MARK cắt lại).
        /// Tách khỏi bước vẽ marker để đường «đặt layer bằng hình học» dùng chung.
        /// </summary>
        internal static MarkEdgeScan ScanMarkEdges(IView view, LaserMarkFeatureRecognizer recognizer, string logPrefix,
            List<VisibleEdgeReference> visibleEdges = null)
        {
            var scan = new MarkEdgeScan();
            if (view == null) return scan;
            try
            {
                List<VisibleEdgeReference> visibleEntityList = visibleEdges ?? CollectVisibleEdges(view);

                // A model IEdge is not a drawing entity.  It can be selected
                // in a view, but SolidWorks has no Layer setter on IEdge and
                // SelectedEdgeProperties(string) only *names* an edge (it
                // never changes its drawing layer).  Keep the marked edges
                // and mirror their 2D geometry into the view sketch instead;
                // ISketchSegment.Layer is the supported per-entity API and
                // survives the temporary Drawing -> DXF export.
                List<VisibleEdgeReference> markedEdges = scan.Marked;
                // A marked Cut-Extrude can expose more than one model edge
                // (for example the two sides of a groove).  Every visible
                // edge belonging to that MARK feature must be mirrored to
                // LASER_MARK.  Cut-Extrude features without MARK are never
                // included and therefore remain on CUT.
                HashSet<string> markedEdgeIds =
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (VisibleEdgeReference visibleEntity in visibleEntityList)
                {
                    IEdge edge = visibleEntity?.Edge;
                    if (edge == null) continue;

                    try
                    {
                        string componentKey = "PART";
                        try
                        {
                            componentKey = visibleEntity.Component == null
                                ? "PART"
                                : (visibleEntity.Component.Name2 ??
                                   visibleEntity.Component.Name ?? "ASSEMBLY");
                        }
                        catch { }

                        if (!recognizer.TryResolve(
                                edge,
                                out LaserMarkFeatureRecognizer.Resolution mark))
                        {
                            scan.Unmarked.Add(visibleEntity);
                            continue;
                        }

                        // Both the owning pattern and its MARK seed count as
                        // visible for the hidden-mark report below.
                        scan.VisibleMarkFeatureNames.Add(mark.SourceName);
                        scan.VisibleMarkFeatureNames.Add(mark.MarkedName);

                        int solidWorksEdgeId = edge.GetID();
                        string edgeId = solidWorksEdgeId > 0
                            ? componentKey + "|ID|" + solidWorksEdgeId
                            // Some Drawing views expose temporary/projected
                            // edges with GetID()==0. Do not collapse all such
                            // edges into a single candidate.
                            : componentKey + "|RCW|" +
                              RuntimeHelpers.GetHashCode(edge);
                        // The Part fallback query can return the same edge as
                        // the component query. Do not create duplicate marker
                        // geometry for that edge.
                        if (markedEdgeIds.Add(edgeId))
                        {
                            DescribeMarkFaces(edge, recognizer, mark.SourceName, visibleEntity);
                            markedEdges.Add(visibleEntity);
                            string description = mark.Description;
                            scan.RecognizedDescriptions[description] =
                                scan.RecognizedDescriptions.TryGetValue(
                                    description,
                                    out int count)
                                    ? count + 1
                                    : 1;
                        }
                    }
                    catch (COMException ex)
                    {
                        DxfTrace.Debug("Cannot inspect MARK feature edge for " +
                            "LASER_MARK: " + ex.Message);
                    }
                }

                // A MARK feature whose edges were not attributed to it through
                // the face owner (flattened faces can report another owner)
                // is matched again by GEOMETRY: its own face edges against
                // the still-unmarked visible edges of the view.
                Lap("resolve");
                scan.RecoveredEdgeCount = RecoverMarkEdgesByGeometry(
                    view,
                    recognizer,
                    scan.Unmarked,
                    markedEdges,
                    scan.VisibleMarkFeatureNames,
                    scan.RecognizedDescriptions,
                    logPrefix);

                // Safety net: an edge of the part OUTLINE (outer loop of a
                // sheet face) is never engraved, or the part would not be cut
                // free. Seen on a lofted rolled sheet whose outline edges were
                // attributed to a MARK feature.
                Lap("recover");
                int outlineDropped = DropOutlineEdges(markedEdges, recognizer, logPrefix);
                Lap("outline");

                // An ordinary (rivet) hole cut AGAIN by a MARK feature in the
                // flat configuration is not a mark: keep it on CUT.
                var shownSources = new HashSet<string>(markedEdges.Select(e => e.MarkSource ?? ""), StringComparer.OrdinalIgnoreCase);
                if (DropOrdinaryHolesRecutByMark(view, markedEdges, recognizer, logPrefix) > 0)
                {
                    // A MARK feature left without any edge shows nothing of its own: on the flat
                    // pattern SOLIDWORKS handed it ordinary holes and gave its own hole to another
                    // feature (4119-41: four Ø4.2 holes for the MARK feature, its Ø6 hole elsewhere).
                    // It is hidden like a MARK feature that never had an edge: look for its hole
                    // again; the hidden-mark report warns when it is not found.
                    shownSources.ExceptWith(markedEdges.Select(e => e.MarkSource ?? ""));
                    shownSources.Remove("");
                    if (shownSources.Count > 0)
                    {
                        foreach (string source in shownSources)
                        {
                            Feature lost = null;
                            try { lost = (view.ReferencedDocument as PartDoc)?.FeatureByName(source) as Feature; } catch (COMException) { }
                            if (lost != null && recognizer.TryResolve(lost, out LaserMarkFeatureRecognizer.Resolution lostMark))
                                scan.RecognizedDescriptions.Remove(lostMark.Description);
                        }
                        scan.VisibleMarkFeatureNames.ExceptWith(shownSources);
                        ErrorEvents.LogDebug(logPrefix + "[LASER_MARK][RECUT-ORDINARY] feature MARK không còn cạnh nào sau khi bỏ lỗ thường: " +
                                             string.Join(", ", shownSources) + " → tìm lại lỗ của nó trên hình trải.");
                        scan.RecoveredEdgeCount += RecoverMarkEdgesByGeometry(
                            view, recognizer, scan.Unmarked, markedEdges,
                            scan.VisibleMarkFeatureNames, scan.RecognizedDescriptions, logPrefix);
                    }
                }
                Lap("recut");
                if (outlineDropped > 0)
                {
                    ErrorEvents.LogError(
                        logPrefix + "CẢNH BÁO LASER_MARK: " + outlineDropped +
                        " cạnh biên ngoài của chi tiết bị nhận nhầm là MARK — " +
                        "đã giữ ở layer CUT (biên ngoài luôn phải cắt).");
                }
                scan.OutlineDropped = outlineDropped;
            }
            catch (COMException ex)
            {
                ErrorEvents.LogError(logPrefix + "LỖI LASER_MARK: không quét được cạnh feature MARK: " + ex.Message);
            }
            return scan;
        }

        /// <summary>Miệng lỗ côn/bậc nhìn thấy trên view (lỗ xuyên phải cũng nhìn thấy). Không vẽ gì.</summary>
        internal static List<StepHoleMouth> FindStepHoleMouths(IView view, string logPrefix,
            List<VisibleEdgeReference> visibleEdges = null)
        {
            var mouths = new List<StepHoleMouth>();
            try
            {
                List<VisibleEdgeReference> visible = visibleEdges ?? CollectVisibleEdges(view);
                if (visible.Count == 0) return mouths;
                var recognizer = new LaserMarkFeatureRecognizer();

                // Visible full circles: the through bore must be on the view.
                var circles = new List<KeyValuePair<VisibleEdgeReference, double[]>>();
                foreach (VisibleEdgeReference v in visible)
                {
                    double[] circle = CircleOf(v?.Edge);
                    if (circle != null) circles.Add(new KeyValuePair<VisibleEdgeReference, double[]>(v, circle));
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (KeyValuePair<VisibleEdgeReference, double[]> candidate in circles)
                {
                    // A usable mouth already requires a visible, smaller,
                    // coaxial bore. Reject ordinary holes before the expensive
                    // COM walk over every loop on their sheet face.
                    if (!circles.Any(other => !ReferenceEquals(other.Key, candidate.Key) &&
                        ReferenceEquals(other.Key.Component, candidate.Key.Component) &&
                        IsPossibleStepBore(candidate.Value, other.Value))) continue;
                    StepHoleMouth mouth;
                    if (!TryGetStepHoleMouth(candidate.Key, candidate.Value, out mouth)) continue;
                    // A MARK feature's own rim is handled by the LASER_MARK pass.
                    if (recognizer.TryResolve(candidate.Key.Edge, out _)) continue;

                    bool boreVisible = circles.Any(other =>
                        !ReferenceEquals(other.Key, candidate.Key) &&
                        ReferenceEquals(other.Key.Component, candidate.Key.Component) &&
                        Math.Abs(other.Value[6] - mouth.InnerRadius) <= StepHoleToleranceMeters &&
                        RadialOffset(other.Value, mouth.Centre, mouth.Axis) <= StepHoleToleranceMeters);
                    if (!boreVisible) continue;

                    string key = (candidate.Key.Component == null ? "P" : "C" + candidate.Key.Component.GetHashCode()) + "|" +
                                 string.Join(",", mouth.Centre.Select(x => Math.Round(x * 1e5).ToString(CultureInfo.InvariantCulture))) +
                                 "|" + Math.Round(mouth.OuterRadius * 1e5).ToString(CultureInfo.InvariantCulture);
                    if (!seen.Add(key)) continue;
                    mouths.Add(mouth);
                }
            }
            catch (Exception ex)
            {
                ErrorEvents.LogDebug(logPrefix + "[STEP_HOLE] lỗi khi dò lỗ côn/bậc: " + ex.Message);
                return mouths;
            }
            return mouths;
        }

        internal static bool IsPossibleStepBore(double[] mouth, double[] bore)
        {
            // Do not subtract the tolerance from the outer radius: a visible
            // bore can be up to that tolerance larger than the model's bore.
            return bore[6] < mouth[6] &&
                RadialOffset(bore, mouth, UnitAxis(mouth)) <= StepHoleToleranceMeters;
        }
    }
}
