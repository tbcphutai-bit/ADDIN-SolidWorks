using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using ADDIN.HoleManagement;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed class ChangeHoleNameCommand
    {
        private readonly ISldWorks app;

        public ChangeHoleNameCommand(ISldWorks app) { this.app = app; }

        public void Run(string label)
        {
            label = (label ?? "").Trim();
            if (label.Length == 0)
            {
                MessageBox.Show("Hay chon hoac nhap Hole Type truoc.", "CHANGE NAME HOLE");
                return;
            }
            ModelDoc2 model = app == null ? null : app.ActiveDoc as ModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                MessageBox.Show("Hay mo Part va Ctrl-chon feature trong FeatureManager.", "CHANGE NAME HOLE");
                return;
            }

            var selection = model.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) == 0)
            {
                MessageBox.Show("Hay chon it nhat mot feature.", "CHANGE NAME HOLE");
                return;
            }

            var selected = new List<Feature>();
            for (int index = 1; index <= selection.GetSelectedObjectCount2(-1); index++)
            {
                Feature feature = selection.GetSelectedObject6(index, -1) as Feature;
                if (feature != null) selected.Add(feature);
            }
            if (selected.Count == 0)
            {
                MessageBox.Show("Lua chon khong co feature ho tro.", "CHANGE NAME HOLE");
                return;
            }

            try
            {
                HoleMetadataService.Initialize(app);
                var resolver = new HoleFamilyResolver(model);
                var families = new HashSet<HoleFamily>();
                foreach (Feature feature in selected)
                {
                    HoleFamily family = resolver.Find(feature);
                    if (family == null)
                        throw new InvalidOperationException("Khong xac dinh duoc ho lo cho " + feature.Name);
                    families.Add(family);
                }
                var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (Feature feature = model.FirstFeature() as Feature; feature != null;
                    feature = feature.GetNextFeature() as Feature)
                    occupied.Add(feature.Name);

                int changed = 0;
                foreach (HoleFamily family in families)
                {
                    string id = string.IsNullOrWhiteSpace(family.FamilyId)
                        ? Guid.NewGuid().ToString("N") : family.FamilyId;
                    string baseName = family.DiameterMm.HasValue
                        ? "φ" + family.DiameterMm.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + label
                        : label;
                    foreach (Feature feature in family.Features)
                    {
                        HoleMetadataService.Write(model, feature, new HoleMetadata
                        {
                            FamilyId = id,
                            Label = label,
                            DiameterMm = family.DiameterMm,
                            Role = HoleFeatureClassifier.Classify(feature) == HoleFeatureKind.Pattern ? "Pattern" : "Seed"
                        });
                        occupied.Remove(feature.Name);
                        string unique = baseName;
                        for (int suffix = 2; occupied.Contains(unique); suffix++)
                            unique = baseName + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                        feature.Name = unique;
                        occupied.Add(unique);
                        changed++;
                        Debug.WriteLine("[HOLE FAMILY] Renamed=" + unique + ", FamilyId=" + id +
                            ", Diameter=" + family.DiameterMm + ", Label=" + label);
                    }
                }
                MessageBox.Show("Da gan Hole Type cho " + families.Count + " ho lo (" + changed + " feature).",
                    "CHANGE NAME HOLE", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HOLE FAMILY] Change name failed: " + ex);
                MessageBox.Show("Khong the doi ten hole: " + ex.Message, "CHANGE NAME HOLE",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    model.ClearSelection2(true);
                    foreach (Feature feature in selected) feature.Select2(true, 0);
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Restore selection failed: " + ex.Message); }
            }
        }
    }
}
