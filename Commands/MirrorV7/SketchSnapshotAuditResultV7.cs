namespace ADDIN.Commands.MirrorV7
{
    /// <summary>Aggregate outcome of the Phase 5A read-only sketch snapshot audit.</summary>
    public sealed class SketchSnapshotAuditResultV7
    {
        /// <summary>Graph nodes whose V7 role is Sketch (ProfileFeature / 3DProfileFeature).</summary>
        public int SketchNodes { get; set; }

        public int Captured { get; set; }

        /// <summary>Fatal capture failures: the sketch could not be read and was not suppressed.</summary>
        public int CaptureFailed { get; set; }

        /// <summary>
        /// Tolerated capture failures on suppressed sketch features. SolidWorks may legitimately
        /// refuse the sketch object of a suppressed feature; these are logged loudly but do not
        /// fail the audit unless TreatSuppressedCaptureFailureAsFatal is set.
        /// </summary>
        public int CaptureFailedOnSuppressed { get; set; }

        public int CapturedSuppressed { get; set; }
        public int Captured3D { get; set; }

        public int TotalPoints { get; set; }
        public int TotalSegments { get; set; }
        public int TotalConstructionSegments { get; set; }
        public int TotalSlots { get; set; }

        public int TotalRelations { get; set; }
        public int TotalDanglingRelations { get; set; }
        public int TotalOverDefiningRelations { get; set; }
        public int TotalExternalRelations { get; set; }
        public int TotalLockedRelations { get; set; }
        public int TotalBrokenRelations { get; set; }

        public int TotalDimensions { get; set; }
        public int TotalDanglingDimensions { get; set; }

        public int EntitiesWithIds { get; set; }
        public int EntitiesWithoutIds { get; set; }
        public int EntityPersistCaptured { get; set; }
        public int EntityPersistUnavailable { get; set; }
        public int DimensionPersistCaptured { get; set; }
        public int DimensionPersistUnavailable { get; set; }

        public int TransformAvailable { get; set; }
        public int PointsWithModelCoords { get; set; }
        public int PointsWithoutModelCoords { get; set; }

        /// <summary>Sketches whose two independent captures matched exactly.</summary>
        public int Stable { get; set; }

        /// <summary>Sketches whose two independent captures disagreed structurally.</summary>
        public int Unstable { get; set; }

        /// <summary>Diagnostic only: persist-ref bytes differed between captures.</summary>
        public int PersistRefUnstable { get; set; }

        public int FatalIntegrityErrors { get; set; }
        public int IntegrityErrors { get { return FatalIntegrityErrors; } set { FatalIntegrityErrors = value; } }
        public int ApiQuirks { get; set; }
        public int GeometryUnstable { get { return Unstable; } set { Unstable = value; } }
        public int IntegrityWarnings { get; set; }

        public bool Success
        {
            get
            {
                return CaptureFailed == 0 &&
                       FatalIntegrityErrors == 0 &&
                       GeometryUnstable == 0 &&
                       Captured + CaptureFailedOnSuppressed == SketchNodes;
            }
        }
    }
}
