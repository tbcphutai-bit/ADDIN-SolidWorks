using System;
using System.IO;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.DxfChecking
{
    internal sealed class DrawingComparisonSource
    {
        public string DrawingPath, DxfPath, ModelPath, Configuration, Sheet, View, AddinVersion;
        public string DrawingSha256, DxfSha256;
        public DateTime DrawingWrittenUtc, DxfWrittenUtc;
        public bool DrawingWasUnsaved, RebuildRequested, RebuildReturned;
        public bool ModelConfigurationNeedsRebuildBefore, ModelConfigurationNeedsRebuildAfter;
        public int RebuildStateBefore, RebuildStateAfter, ModelRebuildStateAfter, UpdateStamp;
        public double ViewScale;

        internal static DrawingComparisonSource Capture(ModelDoc2 drawing, IView view, string dxfPath, string sheet)
        {
            var source = new DrawingComparisonSource {
                DrawingPath = drawing.GetPathName(), DxfPath = Path.GetFullPath(dxfPath),
                ModelPath = view.GetReferencedModelName(), Configuration = view.ReferencedConfiguration,
                Sheet = sheet, View = view.GetName2(), ViewScale = view.ScaleDecimal,
                DrawingWasUnsaved = drawing.GetSaveFlag(),
                AddinVersion = typeof(DrawingComparisonSource).Assembly.ManifestModule.ModuleVersionId.ToString()
            };
            source.DrawingSha256 = Hash(source.DrawingPath);
            source.DxfSha256 = Hash(source.DxfPath);
            source.DrawingWrittenUtc = string.IsNullOrEmpty(source.DrawingPath) ? DateTime.MinValue : File.GetLastWriteTimeUtc(source.DrawingPath);
            source.DxfWrittenUtc = File.GetLastWriteTimeUtc(source.DxfPath);
            return source;
        }
        internal void VerifySavedFiles()
        {
            if (DrawingSha256 != Hash(DrawingPath) || DxfSha256 != Hash(DxfPath))
                throw new InvalidDataException("File Drawing/DXF đã đổi trong lúc kiểm tra. Hãy chạy lại.");
        }
        private static string Hash(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete)) using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }
    }

    internal static class DrawingViewPreparation
    {
        internal static void Prepare(ModelDoc2 drawing, IView view, DrawingComparisonSource source)
        {
            var model = view.ReferencedDocument as ModelDoc2;
            if (model == null) throw new InvalidOperationException("Flat View chưa tải được model tham chiếu; không dùng hình học cache để kết luận.");
            var configuration = model.GetConfigurationByName(view.ReferencedConfiguration) as IConfiguration;
            if (configuration == null) throw new InvalidOperationException("Không tìm thấy configuration tham chiếu của Flat View.");
            source.RebuildStateBefore = drawing.Extension.NeedsRebuild2;
            source.ModelConfigurationNeedsRebuildBefore = configuration.NeedsRebuild;
            source.RebuildRequested = source.RebuildStateBefore != 0 || model.Extension.NeedsRebuild2 != 0 || source.ModelConfigurationNeedsRebuildBefore;
            if (source.ModelConfigurationNeedsRebuildBefore) model.Extension.EditRebuildAll();
            if (source.RebuildRequested) source.RebuildReturned = drawing.ForceRebuild3(false);
            source.RebuildStateAfter = drawing.Extension.NeedsRebuild2;
            source.ModelRebuildStateAfter = model.Extension.NeedsRebuild2;
            source.ModelConfigurationNeedsRebuildAfter = configuration.NeedsRebuild;
            // The bool alone does not prove a stale or valid view. Inspect the state
            // after a requested rebuild, then refresh this view's display geometry.
            // A Drawing can expose a model-wide rebuild flag even when its
            // referenced flat configuration is fully rebuilt. Gate on that
            // exact configuration; retain the global flag in diagnostics.
            if (source.RebuildStateAfter != 0 || source.ModelConfigurationNeedsRebuildAfter)
                throw new InvalidOperationException("Drawing/model vẫn cần rebuild; chưa xác nhận được hình học mới.");
            ADDIN.Commands.FlatPatternDrawingDisplay.RemoveShading(view);
            view.UpdateViewDisplayGeometry();
            source.UpdateStamp = drawing.GetUpdateStamp();
        }
    }
}
