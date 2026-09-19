using System;
namespace ADDIN.Commands.MirrorV7
{
    public sealed class PersistReferenceV7
    {
        private readonly byte[] data;
        public string OwnerFeatureName { get; private set; }
        public string Purpose { get; private set; }
        public byte[] Data { get { if (data == null) return null; byte[] copy = new byte[data.Length]; Array.Copy(data, copy, data.Length); return copy; } }
        public int ByteCount { get { return data == null ? 0 : data.Length; } }
        public PersistReferenceV7(string ownerFeatureName, string purpose, byte[] data)
        {
            if (data == null || data.Length == 0) throw new ArgumentException("Persistent reference data is empty.", "data");
            OwnerFeatureName = ownerFeatureName ?? ""; Purpose = purpose ?? ""; this.data = new byte[data.Length]; Array.Copy(data, this.data, data.Length);
        }
    }
}
