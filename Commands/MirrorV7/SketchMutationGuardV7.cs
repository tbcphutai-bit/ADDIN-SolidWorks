using System;

namespace ADDIN.Commands.MirrorV7
{
    /// <summary>
    /// Hard architectural gate for Mirror V7.
    /// The V7 core must never destroy sketch design intent globally.
    /// </summary>
    public static class SketchMutationGuardV7
    {
        public static void RejectDestructiveMutation(
            bool deletesAllRelations,
            bool deletesAllDimensions)
        {
            if (deletesAllRelations)
                throw new InvalidOperationException(
                    "V7 forbids DeleteAllRelations().");

            if (deletesAllDimensions)
                throw new InvalidOperationException(
                    "V7 forbids global deletion of sketch dimensions.");
        }

        /// <summary>
        /// Phase 5A is a read-only snapshot/integrity phase. Sketch geometry mutation stays
        /// blocked until the snapshot and integrity model have passed on real SolidWorks.
        /// </summary>
        public static void RejectSketchPointMutation(string callerPhase)
        {
            throw new InvalidOperationException(
                "V7 sketch point mutation is not enabled. Caller phase=" +
                (callerPhase ?? "<unknown>") +
                ". Phase 5A captures sketches read-only; mutation requires a later, explicitly approved subphase.");
        }

        /// <summary>
        /// Rejects index-based sketch entity identity, which the V6 legacy path used as a
        /// fallback and which the V7 architecture does not accept as primary identity.
        /// </summary>
        public static void RejectIndexOnlyIdentity(bool hasPersistentReference, bool hasEntityIds)
        {
            if (hasPersistentReference || hasEntityIds) return;
            throw new InvalidOperationException(
                "V7 forbids index-only sketch entity identity. " +
                "Neither a persistent reference nor entity IDs were available.");
        }
    }
}
