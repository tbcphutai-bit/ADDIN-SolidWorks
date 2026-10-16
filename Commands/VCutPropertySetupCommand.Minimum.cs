using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    internal sealed partial class VCutPropertySetupCommand
    {
        internal bool IsMinimumDocumentActive(ModelDoc2 part)
        {
            var active = app.ActiveDoc as ModelDoc2;
            return active != null && (ReferenceEquals(active, part) || app.IsSame(active, part) == (int)SolidWorks.Interop.swconst.swObjectEquality.swObjectSame);
        }
        private void LoadMinimum(List<BendRow> rows, ModelDoc2 part, string config)
        {
            var timer = Stopwatch.StartNew();
            int stamp = part.GetUpdateStamp();
            BendMinimumContext context;
            try
            {
                foreach (var row in rows)
                    try { var id = part.Extension.GetPersistReference3(row.Feature) as byte[]; row.PersistentId = id == null ? "" : Convert.ToBase64String(id); }
                    catch { row.PersistentId = ""; }
                var bodies = BendPanelGeometryReader.Read(app, part, config, rows);
                var geometry = BendMinimumFeatureGraph.Build(bodies, rows);
                context = new BendMinimumContext(geometry,
                    () => IsLoadedPart(app.ActiveDoc as ModelDoc2) && part.GetUpdateStamp() == stamp,
                    timer.Elapsed.TotalSeconds, bodies.Sum(b => b.Faces.Count), bodies.Sum(b => b.Edges.Count));
            }
            catch (Exception ex)
            {
                // Minimum recognition must not disable existing groove assignment.
                context = new BendMinimumContext(new Dictionary<int, BendMinimumFeatureGeometry>(),
                    () => IsLoadedPart(app.ActiveDoc as ModelDoc2) && part.GetUpdateStamp() == stamp,
                    timer.Elapsed.TotalSeconds, 0, 0) { CaptureError = "Không đọc được hình học tối thiểu: " + ex.Message };
            }
            context.LiveFactory = () => new BendMinimumLiveMonitor(this, part);
            context.InspectionFactory = () => new BendMinimumInspection(app, part, context);
            foreach (var row in rows) row.MinimumContext = context;
            context.CheckCurrent();
        }

        internal bool SelectMinimum(BendRow row)
        {
            if (row?.MinimumContext == null || !row.MinimumContext.CheckCurrent()) return false;
            var check = row.MinimumContext.Evaluate(row);
            BendMinimumFeatureGeometry geometry;
            if (check.Datum == null || !row.MinimumContext.Geometry.TryGetValue(row.FeatureId, out geometry)) return SelectFeature(row);
            try
            {
                loadedPart.ClearSelection2(true);
                bool selected = false;
                foreach (int id in check.Datum.Faces.Distinct())
                {
                    Face2 face;
                    if (geometry.Body.NativeFaces.TryGetValue(id, out face)) selected |= ((Entity)face).Select4(true, null);
                }
                loadedPart.GraphicsRedraw2();
                return selected;
            }
            catch { return false; }
        }
    }
}
