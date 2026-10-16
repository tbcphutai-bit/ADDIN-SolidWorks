using System;
using System.Collections.Generic;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal partial class DxfOutPut
    {
        private static List<string> ReadFlatFeatureErrors(ModelDoc2 part)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Action<Feature> visit = null;
            visit = feature =>
            {
                if (feature == null || !seen.Add(feature.Name + ":" + feature.GetTypeName2())) return;
                if (!feature.IsSuppressed())
                {
                    bool warning;
                    int code = feature.GetErrorCode2(out warning);
                    // ICE can wrap Cut; inspect every feature type, including sketch subfeatures.
                    if (code != 0 && !warning) errors.Add(feature.Name + ":" + code);
                }
                for (Feature child = feature.GetFirstSubFeature() as Feature; child != null; child = child.GetNextSubFeature() as Feature)
                    visit(child);
            };
            for (Feature feature = part.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                visit(feature);
            return errors;
        }

        private static IDisposable UpdateFlatPatternDrawing(ModelDoc2 drawing, IView view, DXFPropforExport properties)
        {
            DxfExportContext.ReportPhase("update drawing flat");
            var clock = Stopwatch.StartNew();
            ModelDoc2 part = view?.ReferencedDocument as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART ||
                !DxfSourceIdentity.SamePath(part.GetPathName(), properties.PartPath))
                throw new InvalidOperationException("Flat Pattern không tham chiếu đúng Part của dòng BOM.");
            string flat = view.ReferencedConfiguration ?? "";
            string original = (part.GetActiveConfiguration() as Configuration)?.Name ?? "";
            if (string.IsNullOrWhiteSpace(flat) || string.IsNullOrWhiteSpace(original))
                throw new InvalidOperationException("Không đọc được cấu hình Part / Drawing View.");
            var lease = new FlatConfigurationLease(part, original);
            try
            {
                ShowExactConfiguration(part, flat);
                var before = ReadFlatFeatureErrors(part);
                // The flag alone is insufficient for a configuration rebuilt by another Drawing.
                // Rebuild only this exact configuration, never ForceRebuildAll or activate a Part window.
                var forceClock = Stopwatch.StartNew();
                if (!part.ForceRebuild3(false)) throw new InvalidOperationException("Dựng lại cấu hình trải thất bại: " + flat);
                long forceMs = forceClock.ElapsedMilliseconds;
                var after = ReadFlatFeatureErrors(part);
                if (after.Count > 0) throw new InvalidOperationException("Cấu hình trải còn lỗi: " + string.Join("; ", after));
                if (!drawing.ForceRebuild3(false)) throw new InvalidOperationException("Không cập nhật được Drawing trước khi xuất.");
                drawing.GraphicsRedraw2();
                DxfTrace.Info("[FLAT_UPDATE] config='" + flat + "' forced=True errorsBefore=[" + string.Join("; ", before) +
                    "] errorsAfter=[" + string.Join("; ", after) + "] force=" + forceMs + " ms total=" + clock.ElapsedMilliseconds +
                    " ms; giữ cấu hình này khi đọc view và xuất; khôi phục khi xong.");
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }

        private sealed class FlatConfigurationLease : IDisposable
        {
            private ISldWorks app;
            private readonly string partPath;
            private readonly string original;
            internal FlatConfigurationLease(ModelDoc2 model, string configuration)
            { app = DxfExportContext.App; partPath = model.GetPathName(); original = configuration; }
            public void Dispose()
            {
                var application = app; app = null;
                // Closing the only Drawing can unload a hidden, transient Part. Reacquire an
                // existing document by path; never call a disconnected COM object or reopen it.
                var model = application?.IGetOpenDocumentByName2(partPath) as ModelDoc2;
                if (model != null) ShowExactConfiguration(model, original);
            }
        }

        private static void ShowExactConfiguration(ModelDoc2 part, string name)
        {
            string active = (part.GetActiveConfiguration() as Configuration)?.Name ?? "";
            if (string.Equals(active, name, StringComparison.OrdinalIgnoreCase)) return;
            part.ShowConfiguration2(name);
            active = (part.GetActiveConfiguration() as Configuration)?.Name ?? "";
            if (!string.Equals(active, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Không chuyển được cấu hình Part: " + name);
        }
    }
}
