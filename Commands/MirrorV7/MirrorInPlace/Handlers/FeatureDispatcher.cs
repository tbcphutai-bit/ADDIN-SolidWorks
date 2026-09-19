using System;
using System.Collections.Generic;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public interface IFeatureHandlerV7
    {
        string Name { get; }
        bool CanHandle(MirrorV7FeatureNode feature);
        MirrorV7FeatureResult Mutate(MirrorInPlaceExecutionContextV7 context, MirrorV7FeatureNode feature);
    }

    internal abstract class PlannedFeatureHandlerV7 : IFeatureHandlerV7
    {
        public abstract string Name { get; }
        public abstract bool CanHandle(MirrorV7FeatureNode feature);

        public virtual MirrorV7FeatureResult Mutate(MirrorInPlaceExecutionContextV7 context, MirrorV7FeatureNode feature)
        {
            return new MirrorV7FeatureResult
            {
                FeatureName = feature == null ? null : feature.Name,
                FeatureType = feature == null ? null : feature.TypeName,
                Status = MirrorV7ReplayStatus.NotProcessed,
                Message = Name + " selected; handler mutation is not enabled in the preparation milestone."
            };
        }

        protected static string Type(MirrorV7FeatureNode feature)
        {
            return (feature == null ? string.Empty : feature.TypeName ?? string.Empty)
                .Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    internal sealed class BaseAndExtrudeHandlerV7 : PlannedFeatureHandlerV7
    {
        public override string Name { get { return "BaseOrExtrude"; } }
        public override bool CanHandle(MirrorV7FeatureNode feature)
        {
            string t = Type(feature);
            return t.Contains("extrude") || t.Contains("extrusion") || t == "boss" || t == "cut" ||
                   t.Contains("baseflange") || t.Contains("smbaseflange");
        }
    }

    internal sealed class SheetMetalHandlerV7 : PlannedFeatureHandlerV7
    {
        public override string Name { get { return "SheetMetal"; } }
        public override bool CanHandle(MirrorV7FeatureNode feature)
        {
            if (feature != null && feature.Role == MirrorV7FeatureRole.SheetMetal) return true;
            string t = Type(feature);
            return t.Contains("edgeflange") || t.Contains("bend") || t.Contains("jog") ||
                   t.Contains("hem") || t == "flatpattern" || t.Contains("sheetmetal");
        }
    }

    internal sealed class PatternHandlerV7 : PlannedFeatureHandlerV7
    {
        public override string Name { get { return "Pattern"; } }
        public override bool CanHandle(MirrorV7FeatureNode feature)
        {
            return feature != null && feature.Role == MirrorV7FeatureRole.Pattern || Type(feature).Contains("pattern");
        }
    }

    internal sealed class ChiralityHandlerV7 : PlannedFeatureHandlerV7
    {
        public override string Name { get { return "SweepLoftHelixChirality"; } }
        public override bool CanHandle(MirrorV7FeatureNode feature)
        {
            string t = Type(feature);
            return t.Contains("sweep") || t.Contains("loft") || t.Contains("helix") || t.Contains("spiral");
        }
    }

    internal sealed class PassThroughHandlerV7 : PlannedFeatureHandlerV7
    {
        public override string Name { get { return "SystemPassThrough"; } }
        public override bool CanHandle(MirrorV7FeatureNode feature)
        {
            return feature != null && feature.Role == MirrorV7FeatureRole.System;
        }
    }

    public sealed class FeatureDispatcherV7
    {
        private readonly List<IFeatureHandlerV7> handlers = new List<IFeatureHandlerV7>();

        public FeatureDispatcherV7()
        {
            // Specific handlers must remain before generic handlers.
            handlers.Add(new ChiralityHandlerV7());
            handlers.Add(new SheetMetalHandlerV7());
            handlers.Add(new PatternHandlerV7());
            handlers.Add(new BaseAndExtrudeHandlerV7());
            handlers.Add(new PassThroughHandlerV7());
        }

        public IFeatureHandlerV7 Resolve(MirrorV7FeatureNode feature)
        {
            foreach (IFeatureHandlerV7 handler in handlers)
                if (handler.CanHandle(feature)) return handler;
            return null;
        }
    }
}
