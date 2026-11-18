// Kadai CAD contracts. All lengths crossing the CAD boundary are in meters.
using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.Kadai
{
    internal sealed class KadaiPanelSource
    {
        public string DocumentPath;
        public string Configuration;
        public object SelectedFaceTransient; // do not persist COM RCW across sessions
        public string PersistentReferenceEncoded;
        public ModelDoc2 DocumentTransient;
        public ModelDoc2 PanelPart;
        public string PanelConfiguration;
        public Component2 PanelComponent;
        public double[] ComponentPlacement;
        public int DocumentStamp, PanelStamp;
    }

    internal sealed class KadaiProfile3D
    {
        public string RibId;
        public KadaiRibDirection Direction;
        // Model coordinates of the source Part. Assembly placement is applied by the renderer.
        public readonly List<double[]> PointsWorldMeters = new List<double[]>();
        public bool Closed;
        public string ValidationMessage;
        public double ThicknessMeters;
        public double[] ExtrusionDirection;
        public double[] AlongDirection;
        public double[] HeightDirection;
    }

    internal sealed class KadaiPartResult
    {
        public string RibId;
        public string PartFile;
        public string DrawingFile;
        public string DxfFile;
        public string Status; // OK, WARN, FAIL
        public string Details;
    }

    internal interface IKadaiCadBackend
    {
        KadaiPanelSource CaptureSource(ISldWorks app, ModelDoc2 document);
        List<KadaiProfile3D> CalculateProfiles(KadaiPlan plan, KadaiPanelSource panel);
        void ShowTemporaryPreview(ModelDoc2 document, KadaiPlan plan, List<KadaiProfile3D> ribs);
        void ClearTemporaryPreview(ModelDoc2 document);
        List<KadaiPartResult> ApplyToNewWorkingCopy(ModelDoc2 document, KadaiPlan plan, List<KadaiProfile3D> ribs);
        void GenerateManufacturingFiles(List<KadaiPartResult> parts);
    }

    internal sealed class KadaiUnimplementedBackend : IKadaiCadBackend
    {
        private static NotSupportedException NotReady()
        {
            return new NotSupportedException("Kadai 3D engine has not been validated on native SolidWorks 2024 parts.");
        }
        public KadaiPanelSource CaptureSource(ISldWorks a, ModelDoc2 d) { throw NotReady(); }
        public List<KadaiProfile3D> CalculateProfiles(KadaiPlan p, KadaiPanelSource s) { throw NotReady(); }
        public void ShowTemporaryPreview(ModelDoc2 d, KadaiPlan p, List<KadaiProfile3D> r) { throw NotReady(); }
        public void ClearTemporaryPreview(ModelDoc2 d) { }
        public List<KadaiPartResult> ApplyToNewWorkingCopy(ModelDoc2 d, KadaiPlan p, List<KadaiProfile3D> r) { throw NotReady(); }
        public void GenerateManufacturingFiles(List<KadaiPartResult> p) { throw NotReady(); }
    }
}
