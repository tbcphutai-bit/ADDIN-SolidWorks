using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendPlacement;

namespace ADDIN.Commands.BendCollision
{
    internal sealed class BendStateCollisionResult
    {
        public BendStateCoverage State { get; }
        public CollisionResult2D PunchResult { get; }
        public CollisionResult2D HolderResult { get; }
        public CollisionStatus CombinedResult => Combine(PunchResult.Status,HolderResult.Status);
        internal BendStateCollisionResult(BendStateCoverage state,CollisionResult2D punch,CollisionResult2D holder)
        { State=state;PunchResult=punch;HolderResult=holder; }
        internal static CollisionStatus Combine(CollisionStatus punch,CollisionStatus holder) =>
            punch==CollisionStatus.COLLISION || holder==CollisionStatus.COLLISION ? CollisionStatus.COLLISION :
            punch==CollisionStatus.CLEAR && holder==CollisionStatus.CLEAR ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
    }
    internal sealed class CandidateCollisionResult
    {
        public string ToolId { get; }
        public int FeatureId { get; }
        public bool SimulationOnly { get; }
        public string ManufacturingStatus => "CHUA_XAC_MINH";
        public bool SequenceVerified => false;
        public IReadOnlyList<BendStateCollisionResult> States { get; }
        public BendStateCollisionResult ObservedState => States.Single(s=>s.State==BendStateCoverage.FinalModelObserved);
        public CollisionResult2D PunchResult => ObservedState.PunchResult;
        public CollisionResult2D HolderResult => ObservedState.HolderResult;
        public CollisionStatus CombinedResult => ObservedState.CombinedResult;
        public CollisionStatus AllRequiredStatesResult => States.Any(s=>s.CombinedResult==CollisionStatus.COLLISION)?CollisionStatus.COLLISION:
            States.All(s=>s.CombinedResult==CollisionStatus.CLEAR)?CollisionStatus.CLEAR:CollisionStatus.UNKNOWN;
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(PunchResult.Reasons.Concat(HolderResult.Reasons));
        internal CandidateCollisionResult(string id,int feature,bool simulation,IEnumerable<BendStateCollisionResult> states)
        { ToolId=id;FeatureId=feature;SimulationOnly=simulation;States=PlacementLists.Copy(states); }
    }
}
