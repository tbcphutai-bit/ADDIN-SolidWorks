using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7
{
    public static class SingleSketchMutationServiceV7
    {
        private sealed class Baseline
        {
            public int PointCount, SegmentCount, RelationCount, DimensionCount;
            public List<string> SegmentSignatures = new List<string>();
            public List<string> RelationSignatures = new List<string>();
            public List<string> DimensionSignatures = new List<string>();
        }

        public static SketchSelectionResultV7 SelectEligibleSketch(IWin32Window owner, MirrorV7Context context)
        {
            if (context == null || context.SketchMirrorPlan == null || context.WorkingGraph == null) return SketchSelectionResultV7.NoEligible();
            List<SketchMirrorPlanItemV7> eligible = new List<SketchMirrorPlanItemV7>();
            foreach (SketchMirrorPlanItemV7 item in context.SketchMirrorPlan.Items)
            {
                List<string> reasons = new List<string>();
                SketchOwnershipEvidenceV7 ownership = SketchOwnershipEvidenceV7.Unresolved;
                if (item.Is3D) reasons.Add("Is3D"); if (item.IsSuppressed) reasons.Add("Suppressed"); if (item.IsDerived) reasons.Add("Derived"); if (item.IsShared) reasons.Add("Shared"); if (item.OwnershipUnresolved) reasons.Add("OwnershipUnresolved"); if (item.PointCount == 0) reasons.Add("NoPoints"); if (item.MissingModelCoordinates > 0) reasons.Add("MissingModelCoordinates"); if (item.SlotCount > 0) reasons.Add("Slot"); if (item.ExternalRelationCount > 0) reasons.Add("ExternalRelations"); if (item.DimensionCount > 0) reasons.Add("Dimensions"); if (item.RelationCount > 0) reasons.Add("Relations"); if (item.HasNonLineGeometry) reasons.Add("NonLineGeometry"); if (item.HasFixedEntity) reasons.Add("FixedEntity"); if (!item.PlaneTestCompleted) reasons.Add("PlaneTestIncomplete"); if (item.MaximumTargetPlaneDistance > SketchMutationMathV7.PositionToleranceMetres) reasons.Add("TargetOffPlane"); if (item.Blockers.Count > 0) reasons.AddRange(item.Blockers);
                MirrorV7FeatureNode evidenceNode = null; foreach (MirrorV7FeatureNode candidate in context.WorkingGraph.Nodes) if (candidate != null && candidate.Role == MirrorV7FeatureRole.Sketch && string.Equals(candidate.Name, item.FeatureName, StringComparison.Ordinal)) { evidenceNode = candidate; break; }
                if (evidenceNode == null) reasons.Add("SketchNodeUnavailable");
                else { try { MirrorV7Context workingContext = new MirrorV7Context { SwApp = context.SwApp, PartDoc = context.WorkingDocument, Graph = context.WorkingGraph, AuditOnly = true }; SketchSnapshotV7 snapshot = SketchSnapshotServiceV7.Capture(workingContext, evidenceNode); ownership = SketchOwnershipResolverV7.Resolve(evidenceNode, snapshot); if (ownership == SketchOwnershipEvidenceV7.VerifiedUserInput) reasons.RemoveAll(r => r == "OwnershipUnresolved"); foreach (string blocker in SketchOwnershipGuardV7.GetBlockers(snapshot, ownership)) if (!reasons.Contains(blocker)) reasons.Add(blocker); } catch (Exception ex) { reasons.Add("OwnershipSnapshotException"); MirrorV7Diagnostics.Log("[PHASE6C1][ELIGIBILITY_ERROR] name=\"" + item.FeatureName + "\" message=" + ex.Message); } }
                bool isEligible = reasons.Count == 0; MirrorV7Diagnostics.Log("[PHASE6C1][ELIGIBILITY] name=\"" + item.FeatureName + "\" eligible=" + isEligible + " ownership=" + ownership + " reasons=[" + string.Join(",", reasons.ToArray()) + "]"); if (isEligible) eligible.Add(item);
            }
            if (eligible.Count == 0) return SketchSelectionResultV7.NoEligible();
            using (Form dialog = new Form())
            using (ListBox list = new ListBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                dialog.Text = "Mirror V7 - Chọn sketch đủ điều kiện"; dialog.Width = 560; dialog.Height = 300; dialog.FormBorderStyle = FormBorderStyle.FixedDialog; dialog.StartPosition = FormStartPosition.CenterParent; dialog.MinimizeBox = false; dialog.MaximizeBox = false;
                Label label = new Label { Left = 12, Top = 10, Width = 520, Text = "Chỉ thử nghiệm một sketch line 2D không ràng buộc:" };
                list.Left = 12; list.Top = 36; list.Width = 520; list.Height = 170;
                foreach (SketchMirrorPlanItemV7 item in eligible) list.Items.Add(item.TreeOrder + ": " + item.FeatureName + " (points=" + item.PointCount + ")");
                list.SelectedIndex = -1; ok.Text = "Thực hiện"; ok.DialogResult = DialogResult.OK; ok.Left = 350; ok.Top = 220; ok.Width = 85; cancel.Text = "Hủy"; cancel.DialogResult = DialogResult.Cancel; cancel.Left = 447; cancel.Top = 220; cancel.Width = 85;
                dialog.Controls.Add(label); dialog.Controls.Add(list); dialog.Controls.Add(ok); dialog.Controls.Add(cancel); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                if ((owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != DialogResult.OK || list.SelectedIndex < 0) return SketchSelectionResultV7.Cancelled();
                string name = eligible[list.SelectedIndex].FeatureName;
                foreach (MirrorV7FeatureNode node in context.WorkingGraph.Nodes) if (node != null && string.Equals(node.Name, name, StringComparison.Ordinal) && node.Role == MirrorV7FeatureRole.Sketch) return SketchSelectionResultV7.Selected(node);
                return SketchSelectionResultV7.NoEligible();
            }
        }

        public static SingleSketchMutationResultV7 Execute(MirrorV7Context context, MirrorV7FeatureNode selectedNode, CanonicalPartMirrorPlaneV7 plane)
        {
            SingleSketchMutationResultV7 result = new SingleSketchMutationResultV7();
            if (context == null || context.WorkingDocument == null || context.SourceBaseline == null || selectedNode == null) { result.Errors.Add("Mutation session is incomplete."); return result; }
            ModelDoc2 working = context.WorkingDocument; ModelDoc2 source = context.PartDoc;
            bool enteredByRun = false; bool partial = false;
            try
            {
                if (SingleSketchTargetBuilderV7.SameComObject(working, source)) throw new InvalidOperationException("Working document equals source document.");
                Configuration config = working.ConfigurationManager.ActiveConfiguration; if (config == null || !string.Equals(config.Name, context.SourceBaseline.ConfigurationName, StringComparison.Ordinal)) throw new InvalidOperationException("Working-copy configuration mismatch.");
                Feature feature = selectedNode.Feature; Sketch sketch = feature == null ? null : feature.GetSpecificFeature2() as Sketch; if (sketch == null) throw new InvalidOperationException("Selected node is not a sketch.");
                MirrorV7Context workingContext = new MirrorV7Context { SwApp = context.SwApp, PartDoc = working, Graph = context.WorkingGraph, AuditOnly = true };
                SketchSnapshotV7 ownershipSnapshot = SketchSnapshotServiceV7.Capture(workingContext, selectedNode);
                SketchOwnershipGuardV7.AssertAllowed(ownershipSnapshot, SketchOwnershipResolverV7.Resolve(selectedNode, ownershipSnapshot));
                Baseline baseline = CaptureBaseline(sketch, feature);
                if (baseline.RelationCount != 0 || baseline.DimensionCount != 0 || baseline.SegmentCount == 0) throw new InvalidOperationException("Eligibility changed before mutation.");
                IMathUtility math = context.SwApp.GetMathUtility() as IMathUtility; PartReflectionTransformV7 reflection = PartReflectionTransformV7.CreateCanonical(plane);
                List<SketchPointTargetV7> targets = SingleSketchTargetBuilderV7.Capture(working, sketch, math, reflection, selectedNode.Name);
                result.PointCount = targets.Count; int moving = 0; foreach (SketchPointTargetV7 target in targets) if (SketchMutationMathV7.Distance(target.BeforeSketch, target.TargetSketch) > SketchMutationMathV7.PositionToleranceMetres) moving++; result.MovingPointCount = moving;
                if (moving == 0) throw new InvalidOperationException("No point changes significantly; result is inconclusive.");
                PersistReferenceV7 featureReference = PersistentReferenceServiceV7.Capture(working, feature, selectedNode.Name, "PHASE6C1_SKETCH"); if (featureReference == null) throw new InvalidOperationException("Sketch feature persistent reference unavailable.");
                int state; Feature resolvedFeature = PersistentReferenceServiceV7.Resolve(working, featureReference, out state) as Feature; if (resolvedFeature == null || !SingleSketchTargetBuilderV7.SameComObject(resolvedFeature, feature)) throw new InvalidOperationException("Selected sketch feature identity could not be verified.");
                working.ClearSelection2(true); if (!resolvedFeature.Select2(false, 0)) throw new InvalidOperationException("Cannot select selected sketch feature.");
                if (working.SketchManager.ActiveSketch != null) throw new InvalidOperationException("Another sketch is already being edited.");
                working.EditSketch(); enteredByRun = true;
                if (working.SketchManager.ActiveSketch == null || !SingleSketchTargetBuilderV7.SameComObject(working.SketchManager.ActiveSketch, sketch)) throw new InvalidOperationException("Active sketch identity mismatch.");
                partial = true; UnconstrainedLineSketchReflectionV7.ApplyToActiveSketch(working, sketch, targets); working.SketchManager.InsertSketch(true); enteredByRun = false;
                Sketch refreshed = ResolveSketch(working, featureReference); result.MaximumErrorMetres = SingleSketchMutationVerifierV7.VerifyPoints(working, refreshed, math, targets); VerifyInvariants(refreshed, feature, baseline); result.PostEditVerified = true;
                bool rebuilt = working.EditRebuild3(); if (!rebuilt) throw new InvalidOperationException("EditRebuild3 returned false.");
                refreshed = ResolveSketch(working, featureReference); result.MaximumErrorMetres = Math.Max(result.MaximumErrorMetres, SingleSketchMutationVerifierV7.VerifyPoints(working, refreshed, math, targets)); VerifyInvariants(refreshed, feature, baseline); result.PostRebuildVerified = true; result.SketchMutationVerified = true;
                context.SourceBaseline.AssertUnchanged(source); result.SourceUnchanged = true;
                MirrorV7Diagnostics.Log("[PHASE6C1][PASS] sketch=\"" + selectedNode.Name + "\" points=" + result.PointCount + " movingPoints=" + result.MovingPointCount + " maxErrorSI=" + result.MaximumErrorMetres + " fullPartMirrorVerified=False"); return result;
            }
            catch (Exception ex) { partial = true; result.PartialMutation = partial; result.Errors.Add(ex.Message); MirrorV7Diagnostics.LogException("PHASE6C1", ex); try { context.SourceBaseline.AssertUnchanged(source); result.SourceUnchanged = true; } catch (Exception safety) { result.Errors.Add("Source safety: " + safety.Message); MirrorV7Diagnostics.LogException("PHASE6C1_SOURCE_SAFETY", safety); } return result; }
            finally { if (enteredByRun) { try { context.WorkingDocument.SketchManager.InsertSketch(true); } catch (Exception ex) { MirrorV7Diagnostics.Log("[PHASE6C1][EXIT_EDIT_ERROR] " + ex.Message); } } }
        }

        private static Sketch ResolveSketch(ModelDoc2 working, PersistReferenceV7 reference) { int state; Feature feature = PersistentReferenceServiceV7.Resolve(working, reference, out state) as Feature; if (feature == null) throw new InvalidOperationException("Sketch feature reference lost. state=" + state); Sketch sketch = feature.GetSpecificFeature2() as Sketch; if (sketch == null) throw new InvalidOperationException("Resolved feature is no longer a sketch."); return sketch; }
        private static Baseline CaptureBaseline(Sketch sketch, Feature feature) {
            Baseline b = new Baseline(); object[] segments = sketch.GetSketchSegments() as object[]; object[] points = sketch.GetSketchPoints2() as object[];
            b.SegmentCount = segments == null ? 0 : segments.Length; b.PointCount = points == null ? 0 : points.Length;
            b.RelationCount = sketch.RelationManager == null ? 0 : sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swAll);
            object current = feature == null ? null : feature.GetFirstDisplayDimension(); int guard = 0;
            while (current != null && guard++ < 100000) {
                DisplayDimension display = current as DisplayDimension; if (display == null) throw new InvalidOperationException("Display dimension enumeration returned an unexpected object.");
                Dimension dimension = display.GetDimension2(0); b.DimensionCount++; b.DimensionSignatures.Add(dimension == null ? "<null>" : (dimension.FullName ?? dimension.Name ?? "") + ":" + dimension.SystemValue.ToString("G17"));
                current = feature.GetNextDisplayDimension(current);
            }
            if (segments != null) foreach (object raw in segments) { ISketchSegment s = raw as ISketchSegment; if (s != null) { ISketchLine line = raw as ISketchLine; b.SegmentSignatures.Add(s.GetType() + ":" + s.ConstructionGeometry + ":" + s.GetLength().ToString("G17") + ":" + (line == null ? "<non-line>" : PointIdentity(line.GetStartPoint2() as SketchPoint) + ":" + PointIdentity(line.GetEndPoint2() as SketchPoint))); } }
            return b;
        }
        private static string PointIdentity(SketchPoint point) { if (point == null) return "<null>"; try { int[] ids = point.GetID() as int[]; if (ids != null && ids.Length >= 2) return ids[0] + ":" + ids[1]; } catch { } return "<unidentified>"; }
        private static void VerifyInvariants(Sketch sketch, Feature feature, Baseline baseline) { Baseline after = CaptureBaseline(sketch, feature); if (after.PointCount != baseline.PointCount || after.SegmentCount != baseline.SegmentCount || after.RelationCount != baseline.RelationCount || after.DimensionCount != baseline.DimensionCount) throw new InvalidOperationException("Sketch topology/constraint invariant changed."); for (int i = 0; i < baseline.SegmentSignatures.Count; i++) if (i >= after.SegmentSignatures.Count || baseline.SegmentSignatures[i] != after.SegmentSignatures[i]) throw new InvalidOperationException("Line/connectivity/construction invariant changed."); for (int i = 0; i < baseline.DimensionSignatures.Count; i++) if (i >= after.DimensionSignatures.Count || baseline.DimensionSignatures[i] != after.DimensionSignatures[i]) throw new InvalidOperationException("Dimension invariant changed."); }
    }
}
