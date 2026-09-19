using System;
using System.Collections.Generic;

namespace ADDIN.Commands.MirrorV7
{
    public enum SketchOwnershipEvidenceV7 { Unresolved = 0, VerifiedUserInput = 1, VerifiedGenerated = 2, FeatureOwned = 3 }
    public static class SketchOwnershipGuardV7
    {
        public static List<string> GetBlockers(SketchSnapshotV7 snapshot, SketchOwnershipEvidenceV7 ownership)
        {
            List<string> blockers = new List<string>(); if (snapshot == null || !snapshot.CaptureSucceeded) { blockers.Add("SnapshotUnavailable"); return blockers; }
            foreach (SketchEntitySnapshotV7 entity in snapshot.Entities) if (entity.IsBendLine) { blockers.Add("BendLineStrategyNotImplemented"); break; }
            if (ownership == SketchOwnershipEvidenceV7.Unresolved) blockers.Add("OwnershipUnresolved");
            if (ownership == SketchOwnershipEvidenceV7.VerifiedGenerated) blockers.Add("GeneratedSketchStrategyNotImplemented");
            if (ownership == SketchOwnershipEvidenceV7.FeatureOwned) blockers.Add("FeatureOwnedSketchStrategyNotImplemented");
            return blockers;
        }
        public static void AssertAllowed(SketchSnapshotV7 snapshot, SketchOwnershipEvidenceV7 ownership)
        {
            List<string> blockers = GetBlockers(snapshot, ownership); if (blockers.Count > 0) throw new InvalidOperationException("Sketch mutation blocked: " + string.Join(",", blockers.ToArray()));
        }
    }
}
