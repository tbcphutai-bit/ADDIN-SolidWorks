namespace ADDIN.Commands.MirrorV7
{
    public sealed class PersistentReferenceAuditResultV7
    {
        public int Attempted { get; set; }
        public int Captured { get; set; }
        public int Resolved { get; set; }
        public int UnsupportedOrUnavailable { get; set; }
        public int Mismatched { get; set; }
        public int SystemSkipped { get; set; }
        public bool Success { get { return Captured > 0 && Resolved == Captured && Mismatched == 0; } }
    }
}
