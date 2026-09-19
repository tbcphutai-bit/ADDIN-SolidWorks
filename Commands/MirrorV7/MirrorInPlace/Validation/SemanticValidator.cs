using System;
using System.Linq;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public static class SemanticValidatorV7
    {
        public static void AssertPublishable(FeatureReplayJournalV7 journal)
        {
            if (journal == null) throw new ArgumentNullException("journal");
            MirrorV7FeatureResult blocker = journal.Entries.FirstOrDefault(x => !x.Success);
            if (blocker != null)
                throw new InvalidOperationException("Output publication blocked at feature '" +
                    blocker.FeatureName + "' (" + blocker.FeatureType + "): " + blocker.Status + ". " + blocker.Message);
        }
    }
}
