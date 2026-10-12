// Adapted from user-supplied DXF.zip; see SOURCE.md.
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Phần SolidWorks của nhật ký <see cref="DxfTrace"/> (Flatten→DXF, View2DXF): mở phiên,
    /// ghi môi trường SolidWorks, giải nghĩa mã SaveAs/OpenDoc, mô tả view. Bản sao của
    /// ITool src\ITool.SwCore\DXF\DxfExportService.Common.cs (BeginTrace…).
    /// </summary>
    internal partial class DxfOutPut
    {
        /// <summary>
        /// Mở nhật ký cho một lượt xuất, ghi môi trường SolidWorks và chép ErrorEvents
        /// (errorLog.txt / RenkeiDebug.log) vào cùng file.
        /// </summary>
        internal static IDisposable BeginTrace(ISldWorks app, string feature, string title)
        {
            bool outer = !DxfTrace.IsActive;
            IDisposable session = DxfTrace.BeginSession(feature, title);
            if (!outer) return session;
            ErrorEvents.Mirror = DxfTrace.FromErrorEvents;
            TraceSolidWorksEnvironment(app);
            return new TraceScope(session);
        }

        private sealed class TraceScope : IDisposable
        {
            private IDisposable _session;
            public TraceScope(IDisposable session) { _session = session; }

            public void Dispose()
            {
                IDisposable session = _session;
                _session = null;
                if (session == null) return;
                ErrorEvents.Mirror = null;
                session.Dispose();
            }
        }

        internal static void TraceSolidWorksEnvironment(ISldWorks app)
        {
            if (app == null)
            {
                DxfTrace.Warn("SolidWorks: không có đối tượng ISldWorks.");
                return;
            }
            DxfTrace.Info("SolidWorks:");
            TraceValue("revision", () => app.RevisionNumber());
            TraceValue("ngôn ngữ", () => app.GetCurrentLanguage());
            TraceValue("license", () => app.GetCurrentLicenseType());
            TraceValue("hiện cửa sổ", () => app.Visible);
            TraceValue("số tài liệu đang mở", () => app.GetDocumentCount());
            TraceValue("tài liệu đang kích hoạt", () => (app.ActiveDoc as IModelDoc2)?.GetPathName());
            DxfTrace.Info("Cài đặt DXF toàn cục của SolidWorks (lúc bắt đầu):");
            TraceValue("swDxfVersion", () => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfVersion));
            TraceValue("swDxfOutputFonts", () => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfOutputFonts));
            TraceValue("swDxfOutputLineStyles", () => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfOutputLineStyles));
            TraceValue("swDxfOutputNoScale", () => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfOutputNoScale));
            TraceValue("swDxfMultiSheetOption", () => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfMultiSheetOption));
            TraceValue("swDxfMapping", () => app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDxfMapping));
            TraceValue("swDxfMappingFiles", () => app.GetUserPreferenceStringListValue((int)swUserPreferenceStringListValue_e.swDxfMappingFiles));
            TraceValue("swDxfMappingFileIndex", () => app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfMappingFileIndex));
            TraceValue("swDxfUseSolidworksLayers", () => app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDxfUseSolidworksLayers));
            TraceValue("swDxfExportSplinesAsSplines", () => app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDxfExportSplinesAsSplines));
            TraceValue("swDXFHighQualityExport", () => app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDXFHighQualityExport));
            TraceValue("swDxfEndPointMerge", () => app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDxfEndPointMerge));
            TraceValue("swDXFExportHiddenLayersOn", () => app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDXFExportHiddenLayersOn));
        }

        private static void TraceValue(string key, Func<object> read)
        {
            try { DxfTrace.Value(key, read()); }
            catch (Exception ex) { DxfTrace.Value(key, "<không đọc được: " + ex.GetType().Name + " " + ex.Message + ">"); }
        }

        /// <summary>Mã lỗi/cảnh báo SaveAs của SolidWorks kèm tên bit.</summary>
        internal static string DescribeSaveResult(int errors, int warnings) =>
            "errors=" + DxfTrace.DescribeFlags(typeof(swFileSaveError_e), errors) +
            ", warnings=" + DxfTrace.DescribeFlags(typeof(swFileSaveWarning_e), warnings);

        /// <summary>Mã lỗi/cảnh báo OpenDoc của SolidWorks kèm tên bit.</summary>
        internal static string DescribeLoadResult(int errors, int warnings) =>
            "errors=" + DxfTrace.DescribeFlags(typeof(swFileLoadError_e), errors) +
            ", warnings=" + DxfTrace.DescribeFlags(typeof(swFileLoadWarning_e), warnings);

        /// <summary>Tóm tắt một view để nhật ký: tên, loại, tỉ lệ, cấu hình, model, khung.</summary>
        internal static string DescribeView(IView view)
        {
            if (view == null) return "(view null)";
            var parts = new List<string>();
            try { parts.Add("'" + view.GetName2() + "'"); } catch (Exception ex) { parts.Add("tên?" + ex.Message); }
            try { parts.Add("type=" + (swDrawingViewTypes_e)view.Type); } catch { }
            try { parts.Add("flat=" + view.IsFlatPatternView()); } catch { }
            try { parts.Add("scale=" + view.ScaleDecimal.ToString("0.####", CultureInfo.InvariantCulture)); } catch { }
            try { parts.Add("visible=" + view.GetVisible()); } catch { }
            try { parts.Add("config='" + view.ReferencedConfiguration + "'"); } catch { }
            try { parts.Add("model=" + view.GetReferencedModelName()); } catch { }
            try
            {
                if (view.GetOutline() is double[] o && o.Length >= 4)
                    parts.Add("outline(mm)=[" + string.Join(", ", o.Take(4).Select(v => (v * 1000).ToString("0.#", CultureInfo.InvariantCulture))) + "]");
            }
            catch { }
            try { parts.Add("display=" + (swDisplayMode_e)view.GetDisplayMode2()); } catch { }
            return string.Join(" ", parts);
        }

        /// <summary>Bước dọn trên bản sao: lỗi không dừng việc xuất nhưng phải có trong nhật ký.</summary>
        private static void TracedStep(string name, Action action)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                action();
                DxfTrace.Debug("  " + name + ": xong " + clock.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex)
            {
                DxfTrace.Warn("  " + name + ": LỖI sau " + clock.ElapsedMilliseconds + " ms (bỏ qua, xuất tiếp) — " + DxfTrace.Describe(ex));
            }
        }
    }
}
