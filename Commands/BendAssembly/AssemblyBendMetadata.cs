using System;
using System.Collections.Generic;
using ADDIN.Commands.BendPlacement;

namespace ADDIN.Commands.BendAssembly
{
    // Captured presentation facts from the existing Check Min evaluator on
    // STA. No Min math, COM, native delegate, or inferred dimension here.
    internal sealed class AssemblyBendMetadata
    {
        public int FeatureId { get; }
        public string PersistentId { get; }
        public string Name { get; }
        public double? AngleDeg { get; }
        public double? RadiusMm { get; }
        public string MinimumStatus { get; }
        public string MinimumSummary { get; }
        public string MinimumDetails { get; }
        public IReadOnlyList<AssemblyBendMinimumSide> Sides { get; }
        internal AssemblyBendMetadata(int feature, string persistentId, string name, double? angle, double? radius,
            string status, string summary, string details, IEnumerable<AssemblyBendMinimumSide> sides)
        { FeatureId = feature; PersistentId = persistentId; Name = name; AngleDeg = angle; RadiusMm = radius;
            MinimumStatus = status; MinimumSummary = summary; MinimumDetails = details; Sides = PlacementLists.Copy(sides); }
    }
    internal sealed class AssemblyBendMinimumSide
    {
        public string Shape { get; }
        public string Dimension { get; }
        public double ActualMm { get; }
        public double? MinimumMm { get; }
        internal AssemblyBendMinimumSide(string shape, string dimension, double actual, double? minimum)
        { Shape = shape; Dimension = dimension; ActualMm = actual; MinimumMm = minimum; }
    }
}
