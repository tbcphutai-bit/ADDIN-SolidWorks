using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    // An option reader is deliberately NOT a replay handler. Keep these capabilities separate.
    public static class FeatureDefinitionCaptureV7
    {
        private static readonly Type[] DefinitionInterfaces = {
            typeof(IExtrudeFeatureData2), typeof(IChamferFeatureData2),
            typeof(ILinearPatternFeatureData), typeof(ICircularPatternFeatureData),
            typeof(ICurveDrivenPatternFeatureData), typeof(ILoftFeatureData),
            typeof(ISurfaceOffsetFeatureData), typeof(IEdgeFlangeFeatureData)
        };

        public static XElement Capture(ModelDoc2 copy, Feature feature)
        {
            var record = new XElement("Definition", new XAttribute("captureOnly", true),
                new XAttribute("replayImplemented", false));
            object definition = feature.GetDefinition();
            Type contract = definition == null ? null : DefinitionInterfaces.FirstOrDefault(t => t.IsInstanceOfType(definition));
            if (contract == null)
            {
                record.Add(new XElement("Blocker", "DefinitionReaderNotImplemented"));
                return record;
            }
            record.Add(new XAttribute("interface", contract.Name));
            MethodInfo access = contract.GetMethod("AccessSelections");
            MethodInfo release = contract.GetMethod("ReleaseSelectionAccess");
            bool acquired = false;
            try
            {
                acquired = (bool)access.Invoke(definition, new object[] { copy, null });
                if (!acquired) throw new InvalidOperationException("AccessSelections returned false.");
                // Read public properties through the *known interop interface*, never dynamic COM dispatch.
                // Missing or failing members remain explicit; they are never replaced by default values.
                foreach (PropertyInfo property in contract.GetProperties())
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                    Read(record, property.Name, () => property.GetValue(definition, null), copy);
                }
                CaptureMethodOptions(record, definition, copy);
                record.Add(new XElement("Blocker", "FeatureReplayAndSemanticVerificationNotImplemented"));
            }
            catch (Exception ex)
            {
                record.Add(new XElement("CaptureError", Error(ex)));
            }
            finally
            {
                // AccessSelections rolls back the model. A release failure invalidates the run.
                if (acquired) release.Invoke(definition, null);
            }
            return record;
        }

        private static void CaptureMethodOptions(XElement record, object definition, ModelDoc2 copy)
        {
            var extrude = definition as IExtrudeFeatureData2;
            if (extrude != null)
            {
                Read(record, "IsBoss", () => extrude.IsBossFeature(), copy);
                Read(record, "IsThin", () => extrude.IsThinFeature(), copy);
                Read(record, "IsBase", () => extrude.IsBaseExtrude(), copy);
                foreach (bool direction in new[] { true, false })
                {
                    bool d = direction;
                    string prefix = d ? "D1." : "D2.";
                    Read(record, prefix + "EndCondition", () => extrude.GetEndCondition(d), copy);
                    Read(record, prefix + "Depth", () => extrude.GetDepth(d), copy);
                    Read(record, prefix + "WallThickness", () => extrude.GetWallThickness(d), copy);
                    Read(record, prefix + "DraftEnabled", () => extrude.GetDraftWhileExtruding(d), copy);
                    Read(record, prefix + "DraftOutward", () => extrude.GetDraftOutward(d), copy);
                    Read(record, prefix + "DraftAngle", () => extrude.GetDraftAngle(d), copy);
                    Read(record, prefix + "ReverseOffset", () => extrude.GetReverseOffset(d), copy);
                    Read(record, prefix + "TranslateSurface", () => extrude.GetTranslateSurface(d), copy);
                    Read(record, prefix + "EndReference", () => {
                        int kind; object entity = extrude.GetEndConditionReference(d, out kind);
                        return new object[] { kind, entity };
                    }, copy);
                }
                Read(record, "FromEntity", () => {
                    object entity; int kind; extrude.GetFromEntity(out entity, out kind);
                    return new object[] { kind, entity };
                }, copy);
                Read(record, "DirectionReferences", () => {
                    object first, second; int firstKind, secondKind;
                    int status = extrude.GetDirectionReference(out first, out firstKind, out second, out secondKind);
                    return new object[] { status, firstKind, first, secondKind, second };
                }, copy);
            }
            var chamfer = definition as IChamferFeatureData2;
            if (chamfer != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    int index = i;
                    Read(record, "EdgeDistance[" + i + "]", () => chamfer.GetEdgeChamferDistance(index), copy);
                }
                for (int i = 0; i < 3; i++)
                {
                    int index = i;
                    Read(record, "VertexDistance[" + i + "]", () => chamfer.GetVertexChamferDistance(index), copy);
                }
                var edges = chamfer.Edges as object[];
                if (edges != null) for (int i = 0; i < edges.Length; i++)
                {
                    object edge = edges[i];
                    Read(record, "EdgeFlipped[" + i + "]", () => chamfer.GetIsFlipped(edge), copy);
                }
            }
            var loft = definition as ILoftFeatureData;
            if (loft != null)
            {
                Read(record, "IsBoss", () => loft.IsBossFeature(), copy);
                Read(record, "IsThin", () => loft.IsThinFeature(), copy);
                Read(record, "WallThickness1", () => loft.GetWallThickness(true), copy);
                Read(record, "WallThickness2", () => loft.GetWallThickness(false), copy);
                Read(record, "GuideCurveTypes", () => loft.GetGuideCurvesType(), copy);
                short count = loft.GetGuideCurvesCount();
                for (short i = 0; i < count; i++)
                {
                    short index = i;
                    Read(record, "GuideTangency[" + i + "]", () => loft.GetGuideTangencyType(index), copy);
                }
                record.Add(new XElement("SemanticRequirement",
                    "Preserve ordered Profiles, GuideCurves and PickPoints; transform connector coordinates " +
                    "and tangent vectors in their documented frames. Twisted loft needs connector validation, not only profile matching."));
            }
            if (definition is ISurfaceOffsetFeatureData)
                record.Add(new XElement("SemanticRequirement",
                    "Map every selected surface face and verify oriented normals before choosing Flip; " +
                    "distance alone cannot define the mirrored side. Surface verification cannot use solid volume."));
            var linear = definition as ILinearPatternFeatureData;
            if (linear != null)
            {
                Read(record, "Direction2Specified", () => linear.IsDirection2Specified(), copy);
                if (linear.InstancesToVary)
                    record.Add(new XElement("Blocker", "VariablePatternInstanceOptionsReaderNotImplemented"));
            }
            var circular = definition as ICircularPatternFeatureData;
            if (circular != null && circular.InstancesToVary)
                record.Add(new XElement("Blocker", "VariablePatternInstanceOptionsReaderNotImplemented"));
        }

        private static void Read(XElement parent, string name, Func<object> getter, ModelDoc2 model)
        {
            var option = new XElement("Option", new XAttribute("name", name));
            parent.Add(option);
            try { option.Add(Encode(getter(), model)); }
            catch (Exception ex) { option.Add(new XElement("CaptureError", Error(ex))); }
        }

        private static XElement Encode(object value, ModelDoc2 model)
        {
            if (value == null) return new XElement("Null");
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal)
                return new XElement("Value", new XAttribute("type", type.FullName),
                    Convert.ToString(value, CultureInfo.InvariantCulture));
            var array = value as Array;
            if (array != null)
            {
                var result = new XElement("Array", new XAttribute("rank", array.Rank),
                    new XAttribute("length", array.Length));
                int i = 0;
                foreach (object item in array) result.Add(new XElement("Item", new XAttribute("index", i++), Encode(item, model)));
                return result;
            }
            var point = value as MathPoint;
            if (point != null) return new XElement("MathPoint", Encode(point.ArrayData, model));
            var vector = value as MathVector;
            if (vector != null) return new XElement("MathVector", Encode(vector.ArrayData, model));
            byte[] id = model.Extension.GetPersistReference3(value) as byte[];
            if (id == null || id.Length == 0)
                return new XElement("UnresolvedReference", new XAttribute("runtimeType", type.FullName));
            return new XElement("Reference", new XAttribute("scope", "WORKING_COPY_ONLY"),
                new XAttribute("kind", EntityKind(value)), Convert.ToBase64String(id));
        }

        internal static string EntityKind(object entity)
        {
            if (entity is Feature) return "Feature";
            if (entity is Face2) return "Face";
            if (entity is Edge) return "Edge";
            if (entity is Vertex) return "Vertex";
            if (entity is Body2) return "Body";
            if (entity is Sketch) return "Sketch";
            if (entity is SketchSegment) return "SketchSegment";
            if (entity is SketchPoint) return "SketchPoint";
            return "Other";
        }
        private static string Error(Exception exception)
        {
            return (exception.InnerException ?? exception).ToString();
        }
    }
}
