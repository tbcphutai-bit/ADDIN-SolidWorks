// Kadai CAD contracts. All lengths crossing the CAD boundary are in meters.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

    internal sealed class KadaiBaseDatum
    {
        public KadaiPanelSource Source;
        public int HeightAxis, HeightSign;
        public double PlaneCoordinateMm;
        public double[] BoundsInPanelMeters;
        public double MinPanelHeightMm;
        public string MaterialName, MaterialDatabase;
    }

    internal enum KadaiUraSide { Unset = 0, First = -1, Second = 1 }

    internal sealed class KadaiApplyOptions
    {
        internal readonly string PartTemplate, DrawingTemplate, MaterialName, MaterialDatabase;
        private readonly Dictionary<string, string> partNames;
        private readonly Dictionary<string, KadaiUraSide> uraSides;

        internal KadaiApplyOptions(string partTemplate, string drawingTemplate, string materialName,
            string materialDatabase, Dictionary<string, string> partNames, Dictionary<string, KadaiUraSide> uraSides)
        {
            PartTemplate = partTemplate;
            DrawingTemplate = drawingTemplate;
            MaterialName = materialName;
            MaterialDatabase = materialDatabase;
            this.partNames = new Dictionary<string, string>(partNames, StringComparer.Ordinal);
            this.uraSides = new Dictionary<string, KadaiUraSide>(uraSides, StringComparer.Ordinal);
        }

        internal string PartName(string ribId) { return partNames[ribId]; }
        internal KadaiUraSide UraSide(string ribId) { return uraSides[ribId]; }

        internal void Validate(IEnumerable<KadaiProfile3D> ribs)
        {
            RequireTemplate(PartTemplate, ".prtdot");
            RequireTemplate(DrawingTemplate, ".drwdot");
            if (string.IsNullOrWhiteSpace(MaterialName) || !File.Exists(MaterialDatabase))
                throw new InvalidOperationException("Chọn vật liệu từ thư viện SolidWorks có thể truy cập.");
            var ids = ribs.Select(rib => rib.RibId).ToArray();
            if (ids.Length != partNames.Count || ids.Any(id => !partNames.ContainsKey(id)))
                throw new InvalidOperationException("Danh sách tên Part không khớp với Preview.");
            ValidateNames(ids.Select(PartName));
            if (ids.Length != uraSides.Count || ids.Any(id => !uraSides.TryGetValue(id, out var side) ||
                (side != KadaiUraSide.First && side != KadaiUraSide.Second)))
                throw new InvalidOperationException("Chọn mặt ウラ cho tất cả các thanh trong Preview.");
        }

        internal static void RequireTemplate(string path, string extension)
        {
            if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Không tìm thấy template " + extension + " đã chọn.");
        }

        internal static void ValidateNames(IEnumerable<string> names)
        {
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal) ||
                    Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])\s*(\.|$)", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(name, @"\.(sldprt|slddrw|sldasm|dxf)$", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("Tên Part không hợp lệ: " + name);
                if (!unique.Add(name))
                    throw new InvalidOperationException("Tên Part bị trùng: " + name);
            }
        }

        internal static string[] NumberedNames(string seed, int count)
        {
            seed = seed.Trim();
            Match number = Regex.Match(seed, @"[0-9]+$");
            long first;
            if (!number.Success || !long.TryParse(number.Value, NumberStyles.None, CultureInfo.InvariantCulture, out first)
                || first > long.MaxValue - (count - 1))
                throw new InvalidOperationException("Nhập tên bắt đầu có số ở cuối, ví dụ KDA-t01.");
            string prefix = seed.Substring(0, number.Index);
            return Enumerable.Range(0, count).Select(i =>
                prefix + (first + i).ToString(CultureInfo.InvariantCulture).PadLeft(number.Length, '0')).ToArray();
        }
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
