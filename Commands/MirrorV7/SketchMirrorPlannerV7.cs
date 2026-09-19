using System;
using System.Globalization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    public static class SketchMirrorPlannerV7
    {
        private const double PlaneToleranceMetres = 1e-8;

        public static bool TrySelectCanonicalPlane(IWin32Window owner, out CanonicalPartMirrorPlaneV7 plane)
        {
            using (Form dialog = new Form())
            using (ComboBox choices = new ComboBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                dialog.Text = "Mirror V7 - Chọn mặt phẳng local của Part";
                dialog.Width = 430; dialog.Height = 155; dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent; dialog.MinimizeBox = false; dialog.MaximizeBox = false;
                Label label = new Label { Left = 14, Top = 14, Width = 390, Text = "Chọn canonical plane trong hệ tọa độ LOCAL của Part:" };
                choices.Left = 14; choices.Top = 42; choices.Width = 390; choices.DropDownStyle = ComboBoxStyle.DropDownList;
                choices.Items.Add("YZ (normal local X)"); choices.Items.Add("XZ (normal local Y)"); choices.Items.Add("XY (normal local Z)"); choices.SelectedIndex = 0;
                ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.Left = 230; ok.Top = 78; ok.Width = 80;
                cancel.Text = "Hủy"; cancel.DialogResult = DialogResult.Cancel; cancel.Left = 324; cancel.Top = 78; cancel.Width = 80;
                dialog.Controls.Add(label); dialog.Controls.Add(choices); dialog.Controls.Add(ok); dialog.Controls.Add(cancel); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                DialogResult result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
                if (result != DialogResult.OK) { plane = CanonicalPartMirrorPlaneV7.YZ; return false; }
                plane = (CanonicalPartMirrorPlaneV7)choices.SelectedIndex;
                return true;
            }
        }

        public static SketchMirrorPlanV7 Build(MirrorV7Context copyContext, CanonicalPartMirrorPlaneV7 plane)
        {
            if (copyContext == null || copyContext.PartDoc == null || copyContext.Graph == null || copyContext.SwApp == null)
                throw new InvalidOperationException("PHASE6B requires a complete working-copy context.");
            PartReflectionTransformV7 reflection = PartReflectionTransformV7.CreateCanonical(plane);
            IMathUtility math = copyContext.SwApp.GetMathUtility() as IMathUtility;
            if (math == null) throw new InvalidOperationException("MathUtility is unavailable.");
            SketchMirrorPlanV7 plan = new SketchMirrorPlanV7 { Plane = plane };
            foreach (MirrorV7FeatureNode node in copyContext.Graph.Nodes)
            {
                if (node == null || node.Role != MirrorV7FeatureRole.Sketch) continue;
                SketchMirrorPlanItemV7 item = new SketchMirrorPlanItemV7 { FeatureName = node.Name, TreeOrder = node.TreeOrder, IsSuppressed = node.IsSuppressed };
                plan.Items.Add(item);
                SketchSnapshotV7 snapshot;
                try { snapshot = SketchSnapshotServiceV7.Capture(copyContext, node); }
                catch (Exception ex) { item.Blockers.Add("SnapshotException"); item.RequiredHandlers.Add("SnapshotRecovery"); MirrorV7Diagnostics.Log("[PHASE6B][SKETCH_ERROR] name=\"" + node.Name + "\" api=Capture message=" + ex.Message); continue; }
                if (snapshot == null || !snapshot.CaptureSucceeded) { item.Blockers.Add("SnapshotUnavailable"); item.RequiredHandlers.Add("SnapshotRecovery"); MirrorV7Diagnostics.Log("[PHASE6B][SKETCH_ERROR] name=\"" + node.Name + "\" api=Capture reason=" + (snapshot == null ? "null" : snapshot.CaptureFailure)); continue; }
                SketchOwnershipEvidenceV7 ownership = SketchOwnershipResolverV7.Resolve(node, snapshot);
                foreach (string ownershipBlocker in SketchOwnershipGuardV7.GetBlockers(snapshot, ownership)) if (!item.Blockers.Contains(ownershipBlocker)) item.Blockers.Add(ownershipBlocker);
                item.OwnershipUnresolved = ownership == SketchOwnershipEvidenceV7.Unresolved;
                item.Is3D = snapshot.Is3D; item.IsDerived = snapshot.IsDerived; item.IsShared = snapshot.IsShared; item.SlotCount = snapshot.Slots.Count; item.ExternalRelationCount = snapshot.RelationCountExternal; item.DimensionCount = snapshot.Dimensions.Count; item.SegmentCount = snapshot.SegmentCount; item.RelationCount = snapshot.Constraints.Count;
                if (item.IsSuppressed) item.RequiredHandlers.Add("SuppressionAndConfigurationStrategy");
                if (item.IsDerived || item.IsShared) item.RequiredHandlers.Add("DerivedOrSharedSketchDependencyStrategy");
                if (item.ExternalRelationCount > 0) item.RequiredHandlers.Add("ExternalReferenceRemapping");
                if (item.SlotCount > 0) item.RequiredHandlers.Add("SlotGeometryAndDirectionHandler");
                if (item.DimensionCount > 0 || snapshot.Constraints.Count > 0) item.RequiredHandlers.Add("ConstraintPreservingSolverStrategy");
                foreach (SketchDimensionSnapshotV7 dimension in snapshot.Dimensions) if (dimension.PersistRefBytes == 0) item.MissingDimensionReferences++;
                foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
                {
                    if (entity.IsBendLine) item.HasBendLine = true;
                    if (entity.Kind == SketchEntityKindV7.Segment && entity.SegmentType != 0) item.HasNonLineGeometry = true;
                    if (entity.Kind != SketchEntityKindV7.Point) continue;
                    item.PointCount++; if (!entity.HasModelCoords) item.MissingModelCoordinates++;
                }
                foreach (SketchConstraintSnapshotV7 relation in snapshot.Constraints) if (relation.RelationType == 17 || relation.RelationType == 70 || relation.RelationType == 71 || relation.RelationType == 72 || relation.RelationType == 74) item.HasFixedEntity = true;
                if (item.HasBendLine) item.RequiredHandlers.Add("ResolveGeneratedSketchOwnerBeforeMutation");
                if (item.MissingDimensionReferences > 0) item.RequiredHandlers.Add("ExactDimensionIdentityStrategy");
                if (item.MissingModelCoordinates > 0) item.Blockers.Add("MissingModelCoordinates");
                if (item.IsDerived || item.IsShared || node.ParentFeatureNames.Count > 0 || node.ChildFeatureNames.Count > 0 || item.OwnershipUnresolved)
                { item.RequiredHandlers.Add("ResolveOwnerAndDependencyStrategy"); MirrorV7Diagnostics.Log("[PHASE6B][OWNERSHIP] name=\"" + node.Name + "\" ownership=" + ownership + " parents=[" + string.Join(",", node.ParentFeatureNames.ToArray()) + "] children=[" + string.Join(",", node.ChildFeatureNames.ToArray()) + "]"); }
                if (item.Is3D) { item.RequiredHandlers.Add("ThreeDimensionalConstraintStrategy"); continue; }
                Sketch sketch = null; try { sketch = node.Feature == null ? null : node.Feature.GetSpecificFeature2() as Sketch; } catch (Exception ex) { item.Blockers.Add("SketchApiReadFailed"); item.RequiredHandlers.Add("SketchSupportResolution"); MirrorV7Diagnostics.Log("[PHASE6B][SKETCH_ERROR] name=\"" + node.Name + "\" api=GetSpecificFeature2 message=" + ex.Message); continue; }
                MathTransform modelToSketch = null; try { modelToSketch = sketch == null ? null : sketch.ModelToSketchTransform as MathTransform; } catch (Exception ex) { item.Blockers.Add("ModelToSketchTransformUnavailable"); MirrorV7Diagnostics.Log("[PHASE6B][SKETCH_ERROR] name=\"" + node.Name + "\" api=ModelToSketchTransform message=" + ex.Message); continue; }
                if (modelToSketch == null) { item.Blockers.Add("ModelToSketchTransformUnavailable"); item.RequiredHandlers.Add("SketchSupportResolution"); continue; }
                if (item.PointCount == 0) { item.Blockers.Add("NoCapturedPointsForPlaneTest"); item.RequiredHandlers.Add("SketchGeometryCapture"); continue; }
                bool allChecked = true;
                foreach (SketchEntitySnapshotV7 entity in snapshot.Entities)
                {
                    if (entity.Kind != SketchEntityKindV7.Point) continue;
                    if (!entity.HasModelCoords) { allChecked = false; continue; }
                    try
                    {
                        double[] reflected = reflection.ReflectPoint(new[] { entity.ModelX, entity.ModelY, entity.ModelZ });
                        MathPoint targetModel = math.CreatePoint(reflected) as MathPoint;
                        MathPoint targetSketch = targetModel == null ? null : targetModel.MultiplyTransform(modelToSketch) as MathPoint;
                        double[] coordinates = targetSketch == null ? null : targetSketch.ArrayData as double[];
                        if (coordinates == null || coordinates.Length < 3 || !Finite(coordinates[0]) || !Finite(coordinates[1]) || !Finite(coordinates[2])) { allChecked = false; item.Blockers.Add("TargetCoordinateConversionFailed"); continue; }
                        item.MaximumTargetPlaneDistance = Math.Max(item.MaximumTargetPlaneDistance, Math.Abs(coordinates[2]));
                    }
                    catch (Exception ex) { allChecked = false; item.Blockers.Add("TargetCoordinateConversionFailed"); MirrorV7Diagnostics.Log("[PHASE6B][SKETCH_ERROR] name=\"" + node.Name + "\" api=PointTransform message=" + ex.Message); }
                }
                item.PlaneTestCompleted = allChecked;
                if (!item.PlaneTestCompleted) item.RequiredHandlers.Add("CompletePointCoordinateMapping");
                else if (item.MaximumTargetPlaneDistance > PlaneToleranceMetres) item.RequiredHandlers.Add("ReflectSketchSupportPlaneOrReference");
                if (item.HasNonLineGeometry) item.Blockers.Add("NonLineGeometry");
                if (item.HasFixedEntity) item.Blockers.Add("FixedOrLockedEntity");
                if (item.RelationCount > 0) item.Blockers.Add("RelationsPresent");
                if (item.DimensionCount > 0) item.Blockers.Add("DimensionsPresent");
                item.RequiredHandlers.Add("VerifiedSketchMutationHandler");
            }
            Log(plan); return plan;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Log(SketchMirrorPlanV7 plan)
        {
            foreach (SketchMirrorPlanItemV7 item in plan.Items) MirrorV7Diagnostics.Log("[PHASE6B][SKETCH_PLAN] name=\"" + item.FeatureName + "\" plane=" + plan.Plane + " is3D=" + item.Is3D + " suppressed=" + item.IsSuppressed + " points=" + item.PointCount + " segments=" + item.SegmentCount + " relations=" + item.RelationCount + " missingCoordinates=" + item.MissingModelCoordinates + " slots=" + item.SlotCount + " externalRelations=" + item.ExternalRelationCount + " dimensions=" + item.DimensionCount + " missingDimensionReferences=" + item.MissingDimensionReferences + " ownershipUnresolved=" + item.OwnershipUnresolved + " planeTestCompleted=" + item.PlaneTestCompleted + " maxTargetPlaneDistanceSI=" + item.MaximumTargetPlaneDistance.ToString("G17", CultureInfo.InvariantCulture) + " blockers=[" + string.Join(",", item.Blockers.ToArray()) + "] requiredHandlers=[" + string.Join(",", item.RequiredHandlers.ToArray()) + "] mutationReady=False");
            MirrorV7Diagnostics.Log("[PHASE6B][SUMMARY] sketches=" + plan.Items.Count + " plane=" + plan.Plane + " planeToleranceSI=" + PlaneToleranceMetres.ToString("G17", CultureInfo.InvariantCulture) + " mutationEnabled=False");
        }
    }
}
