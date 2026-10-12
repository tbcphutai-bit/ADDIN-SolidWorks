using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal sealed class BomDxfPart
    {
        public string ComponentPath, DrawingPath, ConfigurationName, PartNumber, PartName, UnitName, PMaterial;
        public string PartType = "Sheet Metal";
        public int PartQty;
        public double Thickness;
        // true: SPLINE, false: POLYLINE, null: whatever the machine is set to.
        public bool? ExportSplinesAsSplines;
    }

    internal sealed class BomDxfExportOptions
    {
        public string TargetFolder;
        // The run's report and Check DXF output go to <ReportFolder>\CHECK_DXF_OUTPUT\<run>, so the DXF folder
        // holds DXF files only. Empty: inside TargetFolder, as before.
        public string ReportFolder;
        public bool IncludeBendLines = true;
        public bool IncludeLaserMarks = true;
        public bool RecognizeFeatureMarks = true;
        public bool KeepDrawingMarkSketches = true;
        public bool CheckWhileExporting = true;
        public DxfManufacturingLayerSettings Layers = DxfManufacturingLayerSettings.CreateDefault();
    }

    internal sealed class BomDxfFileResult
    {
        public int BomRow;
        public string ItemNumber, PartNumber, ComponentPath, DrawingPath, Configuration, OutputPath, SheetName, ViewName;
        public string Status = "ERROR", Message = "", Phase = "";
        public readonly List<string> Notices = new List<string>();
        public int WarningCount;
        public string CheckStatus = "SKIP", CheckMessage = "", CheckOutputPath;
        public int CheckIssueCount;
        public double ExportMilliseconds, CheckMilliseconds;
        public readonly List<string> CheckIssues = new List<string>();
    }

    // This context exists only during one synchronous BOM command. It never
    // replaces the user's SOLIDWORKS instance or changes another tool's settings.
    internal static class DxfExportContext
    {
        [ThreadStatic] internal static ISldWorks App;
        [ThreadStatic] internal static DxfManufacturingLayerSettings LayerSettings;
        [ThreadStatic] internal static BomDxfFileResult CurrentFile;
        [ThreadStatic] internal static Action<string> PhaseChanged;
        [ThreadStatic] internal static Func<bool> CancellationRequested;

        internal static void ReportPhase(string name)
        {
            string text;
            switch (name) {
                case "read view identity": text = "Đọc hướng view Flat Pattern"; break;
                case "open drawing copy": text = "Mở bản sao Drawing"; break;
                case "keep flat-pattern view on copy": text = "Giữ riêng view Flat Pattern"; break;
                case "read model semantics": text = "Đọc nét cắt / uốn / vạch dấu của view"; break;
                case "disable model colors": text = "Tắt màu model trên bản sao"; break;
                case "prepare copy for raw export": text = "Bỏ khung tên / kích thước / chú thích"; break;
                case "save raw DXF": text = "Lưu hình học Drawing view sang DXF"; break;
                case "close drawing copy": text = "Đóng bản sao Drawing"; break;
                case "place semantic layers": text = "Đặt layer CUT / BEND / LASER_MARK"; break;
                case "finalize DXF layers": text = "Chuẩn hóa layer DXF"; break;
                case "confirm exported Drawing View": text = "Đối chiếu hướng DXF với Drawing view"; break;
                case "view identity gate (log only)": text = "Đối chiếu hướng DXF với Drawing view"; break;
                case "read MARK sketches on source Drawing View": text = "Đọc sketch MARK trong view Drawing"; break;
                case "publish output file": text = "Hoàn tất file DXF"; break;
                default: text = name; break;
            }
            if (CurrentFile != null) CurrentFile.Phase = text;
            PhaseChanged?.Invoke(text);
            if (CancellationRequested?.Invoke() == true) throw new OperationCanceledException("Đã dừng xuất DXF.");
        }

        internal static IDisposable Enter(ISldWorks application, DxfManufacturingLayerSettings layers)
        {
            if (App != null) throw new InvalidOperationException("Một lệnh xuất DXF khác đang chạy.");
            App = application;
            LayerSettings = DxfManufacturingLayerSettings.Normalize(layers);
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { CurrentFile = null; LayerSettings = null; App = null; PhaseChanged = null; CancellationRequested = null; ErrorEvents.Mirror = null; }
        }
    }

    internal sealed class DxfDrawingColorScope : IDisposable
    {
        private readonly ISldWorks app;
        private readonly bool original;
        private bool disposed;
        internal DxfDrawingColorScope(ISldWorks application, IModelDoc2 drawingCopy)
        {
            app = application;
            int preference = (int)swUserPreferenceToggle_e.swUseModelColorInDrawings;
            original = app.GetUserPreferenceToggle(preference);
            try {
                app.SetUserPreferenceToggle(preference, false);
                if (app.GetUserPreferenceToggle(preference)) throw new InvalidOperationException("Không tắt được Use model color in drawings.");
                // Also clear any document override on the disposable Drawing copy.
                drawingCopy.SetUserPreferenceToggle(preference, false);
                DxfTrace.Info("Use model color in drawings = OFF trước khi SaveAs DXF; layer override của component = 0.");
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swUseModelColorInDrawings, original);
        }
    }

    internal static class ErrorEvents
    {
        [ThreadStatic] internal static Action<string, string> Mirror;
        internal static void LogDebug(string message) { Trace.WriteLine(message); Mirror?.Invoke("DEBUG", message); }
        internal static void LogError(string message)
        {
            Trace.WriteLine(message);
            Mirror?.Invoke("ERROR", message);
            var file = DxfExportContext.CurrentFile;
            if (file != null) file.Notices.Add(message ?? "");
        }
    }

    internal static class DxfExportNotices
    {
        internal static void SetPartNumber(string number)
        { if (DxfExportContext.CurrentFile != null) DxfExportContext.CurrentFile.PartNumber = number; }
        internal static void SetOutputPath(string path)
        { if (DxfExportContext.CurrentFile != null) DxfExportContext.CurrentFile.OutputPath = path; }
        internal static void Info(string category, string message) { Add(category, message, false); }
        internal static void Warning(string category, string message) { Add(category, message, true); }
        private static void Add(string category, string message, bool warning)
        {
            DxfTrace.Notice(warning ? "Warning" : "Info", category, message);
            var file = DxfExportContext.CurrentFile;
            if (file == null) return;
            if (warning) file.WarningCount++;
            file.Notices.Add(category + ": " + message);
        }
    }

    internal static class DxfPropertyNames
    {
        internal const string PropOrderNumber = "手配番号", PropPartNumber = "部品番号";
        internal const string PropQTY = "数量", PropUnitName = "ユニット名";
        internal const string PropThickness = "板厚", PropMaterial = "材質";
    }

    internal sealed class DxfModelAccess
    {
        private readonly ModelDoc2 model;
        internal DxfModelAccess(ModelDoc2 document) { model = document; }
        internal string GetCustomProperty(string name, string configuration = "", bool resolved = true)
        {
            if (model == null) return "";
            CustomPropertyManager manager = null;
            try {
                manager = model.Extension.CustomPropertyManager[configuration ?? ""];
                manager.Get6(name, false, out string raw, out string value, out bool wasResolved, out bool linked);
                return (resolved && !string.IsNullOrWhiteSpace(value) ? value : raw) ?? "";
            }
            finally {
                if (manager != null && System.Runtime.InteropServices.Marshal.IsComObject(manager))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(manager);
            }
        }
        internal static ModelDoc2 OpenFile(string path)
        {
            var app = DxfExportContext.App;
            ModelDoc2 open = app.GetOpenDocumentByName(path) as ModelDoc2;
            if (open != null) return open;
            int type = string.Equals(Path.GetExtension(path), ".SLDDRW", StringComparison.OrdinalIgnoreCase)
                ? (int)swDocumentTypes_e.swDocDRAWING : (int)swDocumentTypes_e.swDocPART;
            int errors = 0, warnings = 0;
            return app.OpenDoc6(path, type, (int)(swOpenDocOptions_e.swOpenDocOptions_Silent |
                swOpenDocOptions_e.swOpenDocOptions_ReadOnly), "", ref errors, ref warnings) as ModelDoc2;
        }
        internal static void CloseFile(ModelDoc2 document)
        { if (document != null) DxfExportContext.App.CloseDoc(document.GetTitle()); }
        internal static void UnitSetup(IModelDoc2 document)
        {
            document.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
        }
        internal static string GetBoundingBoxSketchName(IModelDoc2 document)
        {
            for (var feature = document?.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature) {
                string type = feature.GetTypeName2() ?? "";
                if (type.IndexOf("BoundingBox", StringComparison.OrdinalIgnoreCase) < 0 && type.IndexOf("BBox", StringComparison.OrdinalIgnoreCase) < 0) continue;
                for (var child = feature.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature)
                    if (child.GetTypeName2() == "3DProfileFeature" || child.GetTypeName2() == "ProfileFeature") return child.Name;
                return feature.Name;
            }
            return "";
        }
    }
}
