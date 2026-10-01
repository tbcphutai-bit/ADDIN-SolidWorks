using System;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    // Store one document-owned SOLIDWORKS Attribute per persistent Bend Feature ID.
    // It holds only the groove kind; the numeric values stay in the Part properties.
    internal sealed class VCutFeatureTagStore
    {
        private const string DefinitionName = "ADDIN_VCutBendKind_v1";
        private const string ParameterName = "PropertyName";
        private const string InstancePrefix = "ADDIN_VCut_Bend_";
        private readonly ISldWorks app;
        private readonly ModelDoc2 part;
        private static AttributeDef definition;

        public VCutFeatureTagStore(ISldWorks app, ModelDoc2 part)
        {
            this.app = app;
            this.part = part;
        }

        public string Read(Feature bend)
        {
            if (part == null || bend == null) return "";
            try
            {
                // Bend Feature RCWs are not a reliable place to look for an
                // IEntity attribute after reopening the Part via a Drawing.
                // A document-owned attribute named with the persistent feature ID
                // can be found again from either document context.
                SolidWorks.Interop.sldworks.Attribute stored = FindDocumentAttribute(bend);
                if (stored != null)
                {
                    Parameter parameter = stored.GetParameter(ParameterName) as Parameter;
                    string kind = Normalize(parameter == null ? "" : parameter.GetStringValue());
                    Debug.WriteLine("[VCUT TAG] READ feature=" + bend.Name +
                        " id=" + bend.GetID() + " kind=" + kind + " source=document");
                    return kind;
                }
                // Read the old feature-owned format so existing Part files can be
                // migrated on their next Save in Set Bend line.
                if (!EnsureDefinition()) return "";
                Entity entity = bend as Entity;
                SolidWorks.Interop.sldworks.Attribute legacy = entity == null ? null :
                    entity.FindAttribute(definition, 0) as SolidWorks.Interop.sldworks.Attribute;
                Parameter oldParameter = legacy == null ? null :
                    legacy.GetParameter(ParameterName) as Parameter;
                string oldKind = Normalize(oldParameter == null ? "" : oldParameter.GetStringValue());
                Debug.WriteLine("[VCUT TAG] READ feature=" + bend.Name +
                    " id=" + bend.GetID() + " kind=" + oldKind + " source=legacy");
                return oldKind;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[VCUT TAG] READ ERROR " + bend.Name + " " + ex);
                return "";
            }
        }

        public bool HasDocumentTag(Feature bend)
        {
            try { return FindDocumentAttribute(bend) != null; }
            catch { return false; }
        }

        public bool Write(ModelDoc2 owner, Feature bend, string propertyName, out string error)
        {
            error = "";
            propertyName = Normalize(propertyName);
            if (part == null || owner == null || bend == null || !EnsureDefinition())
            {
                error = "Không tạo được định nghĩa Attribute cho Bend Feature.";
                return false;
            }

            try
            {
                int id = bend.GetID();
                if (id <= 0)
                {
                    error = "Bend Feature không có ID ổn định: " + bend.Name;
                    return false;
                }
                string instanceName = InstancePrefix + id;
                PartDoc ownerPart = owner as PartDoc;
                Feature existing = ownerPart == null ? null :
                    ownerPart.FeatureByName(instanceName) as Feature;
                SolidWorks.Interop.sldworks.Attribute attribute = existing == null ? null :
                    existing.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
                if (existing != null && attribute == null)
                {
                    error = "Tên Attribute trùng với Feature khác: " + instanceName;
                    return false;
                }
                bool created = false;
                if (attribute == null)
                {
                    if (propertyName.Length == 0 && ReadLegacy(bend).Length == 0)
                        return true;
                    attribute = definition.CreateInstance5(owner, null,
                        instanceName, 1,
                        (int)swInConfigurationOpts_e.swAllConfiguration);
                    if (attribute == null)
                    {
                        error = "SOLIDWORKS không tạo được Attribute cho " + bend.Name;
                        return false;
                    }
                    created = true;
                }

                Parameter parameter = attribute.GetParameter(ParameterName) as Parameter;
                if (parameter == null || !parameter.SetStringValue2(propertyName,
                    (int)swInConfigurationOpts_e.swAllConfiguration, ""))
                {
                    error = "Không lưu được loại bào cho " + bend.Name;
                    return false;
                }

                string actual = Normalize(parameter.GetStringValue());
                if (actual != propertyName)
                {
                    error = "Không đọc lại được loại bào trên " + bend.Name;
                    return false;
                }
                if (created) owner.ForceRebuild3(false);
                owner.SetSaveFlag();
                Debug.WriteLine("[VCUT TAG] WRITE feature=" + bend.Name +
                    " id=" + id + " kind=" + propertyName + " instance=" + instanceName);
                return true;
            }
            catch (Exception ex)
            {
                error = bend.Name + ": " + ex.Message;
                return false;
            }
        }

        private SolidWorks.Interop.sldworks.Attribute FindDocumentAttribute(Feature bend)
        {
            if (part == null || bend == null || bend.GetID() <= 0) return null;
            PartDoc ownerPart = part as PartDoc;
            Feature stored = ownerPart == null ? null :
                ownerPart.FeatureByName(InstancePrefix + bend.GetID()) as Feature;
            if (stored == null)
                for (Feature candidate = part.FirstFeature() as Feature;
                    candidate != null; candidate = candidate.GetNextFeature() as Feature)
                    if (string.Equals(candidate.Name, InstancePrefix + bend.GetID(),
                        StringComparison.OrdinalIgnoreCase))
                    { stored = candidate; break; }
            return stored == null ? null :
                stored.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
        }

        private string ReadLegacy(Feature bend)
        {
            Entity entity = bend as Entity;
            SolidWorks.Interop.sldworks.Attribute legacy = entity == null ? null :
                entity.FindAttribute(definition, 0) as SolidWorks.Interop.sldworks.Attribute;
            Parameter parameter = legacy == null ? null : legacy.GetParameter(ParameterName) as Parameter;
            return Normalize(parameter == null ? "" : parameter.GetStringValue());
        }

        private bool EnsureDefinition()
        {
            if (definition != null) return true;
            if (app == null) return false;
            try
            {
                AttributeDef candidate = app.DefineAttribute(DefinitionName) as AttributeDef;
                if (candidate == null) return false;
                // DefineAttribute may return an already registered definition
                // after the add-in is reloaded. Its parameters cannot be added
                // again; only a newly created definition needs registration.
                bool added = candidate.AddParameter(ParameterName,
                    (int)swParamType_e.swParamTypeString, 0, 0);
                if (added && !candidate.Register()) return false;
                definition = candidate;
                return true;
            }
            catch
            {
                definition = null;
                return false;
            }
        }

        public static string Normalize(string value)
        {
            return value == "V溝1" || value == "V溝2" || value == "C溝" ? value : "";
        }
    }
}
