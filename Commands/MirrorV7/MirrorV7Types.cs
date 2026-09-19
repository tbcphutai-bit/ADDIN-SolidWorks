using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    public enum MirrorV7FeatureRole { Unknown = 0, System, Reference, Sketch, Curve, SurfaceFeature, BodyFeature, SheetMetal, Pattern }
    public enum MirrorV7ReplayStatus { NotProcessed = 0, ExactReplay, RecoveredEquivalent, SemanticChanged, Unsupported, Failed }
    public sealed class MirrorV7FeatureNode
    {
        public int TreeOrder { get; set; }
        public int Depth { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }
        public Feature Feature { get; set; }
        public PersistReferenceV7 PersistentReference { get; set; }
        public bool IsSuppressed { get; set; }
        public MirrorV7FeatureRole Role { get; set; }
        public List<string> ParentFeatureNames { get; private set; }
        public List<string> ChildFeatureNames { get; private set; }
        public MirrorV7FeatureNode() { ParentFeatureNames = new List<string>(); ChildFeatureNames = new List<string>(); }
        public override string ToString() { return string.Format("#{0} depth={1} name={2} type={3} role={4} suppressed={5}", TreeOrder, Depth, Name ?? "<null>", TypeName ?? "<null>", Role, IsSuppressed); }
    }
    public sealed class MirrorV7ModelGraph
    {
        private readonly List<MirrorV7FeatureNode> nodes = new List<MirrorV7FeatureNode>();
        private readonly Dictionary<string, MirrorV7FeatureNode> byName = new Dictionary<string, MirrorV7FeatureNode>(StringComparer.OrdinalIgnoreCase);
        public IList<MirrorV7FeatureNode> Nodes { get { return nodes.AsReadOnly(); } }
        public int Count { get { return nodes.Count; } }
        public void Add(MirrorV7FeatureNode node) { if (node == null) return; nodes.Add(node); if (!string.IsNullOrWhiteSpace(node.Name) && !byName.ContainsKey(node.Name)) byName.Add(node.Name, node); }
        public MirrorV7FeatureNode FindByName(string name) { MirrorV7FeatureNode node; return string.IsNullOrWhiteSpace(name) || !byName.TryGetValue(name, out node) ? null : node; }
    }
    public sealed class MirrorV7FeatureResult
    {
        public string FeatureName { get; set; }
        public string FeatureType { get; set; }
        public MirrorV7ReplayStatus Status { get; set; }
        public string Message { get; set; }
        public bool Success { get { return Status == MirrorV7ReplayStatus.ExactReplay || Status == MirrorV7ReplayStatus.RecoveredEquivalent; } }
        public static MirrorV7FeatureResult CreateNotProcessed(string featureName, string featureType) { return new MirrorV7FeatureResult { FeatureName = featureName, FeatureType = featureType, Status = MirrorV7ReplayStatus.NotProcessed, Message = "Feature has not been processed." }; }
    }
}
