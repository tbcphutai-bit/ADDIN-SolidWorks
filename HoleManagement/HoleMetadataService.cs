using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwAttribute = SolidWorks.Interop.sldworks.Attribute;

namespace ADDIN.HoleManagement
{
    internal sealed class HoleMetadata
    {
        public string FamilyId;
        public string Label;
        public double? DiameterMm;
        public string Role;
    }

    internal static class HoleMetadataService
    {
        private const string DefinitionName = "ADDIN_HOLE_FAMILY_V1";
        private static AttributeDef definition;

        public static void Initialize(ISldWorks app)
        {
            if (definition != null || app == null) return;
            definition = app.DefineAttribute(DefinitionName) as AttributeDef;
            if (definition == null) throw new InvalidOperationException("Khong tao duoc Hole AttributeDef.");
            // DefineAttribute can attach to a definition already registered in this session.
            definition.AddParameter("ManagedBy", (int)swParamType_e.swParamTypeString, 0, 0);
            definition.AddParameter("FamilyId", (int)swParamType_e.swParamTypeString, 0, 0);
            definition.AddParameter("Label", (int)swParamType_e.swParamTypeString, 0, 0);
            definition.AddParameter("DiameterMm", (int)swParamType_e.swParamTypeString, 0, 0);
            definition.AddParameter("Role", (int)swParamType_e.swParamTypeString, 0, 0);
            definition.Register();
        }

        public static HoleMetadata Read(ModelDoc2 model, Feature feature)
        {
            if (definition == null || model == null || feature == null) return null;
            try
            {
                SwAttribute attribute = Find(model, feature);
                if (attribute == null || Get(attribute, "ManagedBy") != "ADDIN") return null;
                double diameter;
                string text = Get(attribute, "DiameterMm");
                return new HoleMetadata
                {
                    FamilyId = Get(attribute, "FamilyId"),
                    Label = Get(attribute, "Label"),
                    DiameterMm = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out diameter)
                        ? (double?)diameter : null,
                    Role = Get(attribute, "Role")
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HOLE FAMILY] Read attribute failed: " + ex.Message);
                return null;
            }
        }

        public static void Write(ModelDoc2 model, Feature feature, HoleMetadata value)
        {
            if (definition == null) throw new InvalidOperationException("Hole AttributeDef chua duoc khoi tao.");
            SwAttribute attribute = Find(model, feature);
            if (attribute == null)
                attribute = definition.CreateInstance5(model, feature,
                    "ADDIN_HOLE_" + Guid.NewGuid().ToString("N"), 1,
                    (int)swInConfigurationOpts_e.swThisConfiguration);
            if (attribute == null) throw new InvalidOperationException("Khong gan duoc attribute vao feature.");
            Set(attribute, "ManagedBy", "ADDIN");
            Set(attribute, "FamilyId", value.FamilyId);
            Set(attribute, "Label", value.Label);
            Set(attribute, "DiameterMm", value.DiameterMm.HasValue
                ? value.DiameterMm.Value.ToString("0.###", CultureInfo.InvariantCulture) : "");
            Set(attribute, "Role", value.Role);
        }

        private static SwAttribute Find(ModelDoc2 model, Feature feature)
        {
            Entity entity = feature as Entity;
            if (entity != null)
            {
                SwAttribute found = entity.FindAttribute(definition, 0) as SwAttribute;
                if (found != null) return found;
            }

            // Some interop wrappers do not expose IEntity on Feature. Attribute features
            // are still discoverable by their owner entity.
            long target = ComIdentity(feature);
            for (Feature candidate = model.FirstFeature() as Feature; candidate != null;
                candidate = candidate.GetNextFeature() as Feature)
            {
                try
                {
                    if (!string.Equals(candidate.GetTypeName2(), "Attribute", StringComparison.OrdinalIgnoreCase))
                        continue;
                    SwAttribute found = candidate.GetSpecificFeature2() as SwAttribute;
                    object owner = found == null ? null : found.GetEntity();
                    if (owner != null && ComIdentity(owner) == target) return found;
                }
                catch (Exception ex) { Debug.WriteLine("[HOLE FAMILY] Attribute lookup skipped: " + ex.Message); }
            }
            return null;
        }

        private static long ComIdentity(object value)
        {
            IntPtr pointer = Marshal.GetIUnknownForObject(value);
            try { return pointer.ToInt64(); }
            finally { Marshal.Release(pointer); }
        }

        private static string Get(SwAttribute attribute, string name)
        {
            Parameter parameter = attribute.GetParameter(name) as Parameter;
            return parameter == null ? "" : parameter.GetStringValue() ?? "";
        }

        private static void Set(SwAttribute attribute, string name, string value)
        {
            Parameter parameter = attribute.GetParameter(name) as Parameter;
            if (parameter == null || !parameter.SetStringValue(value ?? ""))
                throw new InvalidOperationException("Khong ghi duoc hole attribute " + name);
        }
    }
}
