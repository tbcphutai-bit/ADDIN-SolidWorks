using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class BendPlacementConfiguration
    {
        public int Version { get; set; }
        public string MasterPath { get; set; }
        public string MasterManifestPath { get; set; }
        public string MasterDatumEvidencePath { get; set; }
        public string LimitWorkbookPath { get; set; }
        public string LimitManifestPath { get; set; }
        public string HolderEvidencePath { get; set; }
        public string PairSeatingEvidencePath { get; set; }
        public string ReviewAuthoritiesPath { get; set; }
        public string ReviewWorkspacePath { get; set; }
    }
    internal static class BendPlacementSources
    {
        // Called only by the existing managed library worker. This adapter has
        // no model/control/COM dependency and writes no settings or CAD.
        internal static PlacementSourceSnapshot Read(string configurationPath,CancellationToken token,string relativeRoot=null)
        {
            if(string.IsNullOrWhiteSpace(configurationPath))return PlacementSourceSnapshot.Missing("PLACEMENT_SOURCE_NOT_CONFIGURED");
            try
            {
                configurationPath=PlacementSourceIo.Resolve(relativeRoot??Directory.GetCurrentDirectory(),configurationPath);
                byte[] bytes=PlacementSourceIo.Read(configurationPath,65536,token);var c=PlacementSourceIo.Json<BendPlacementConfiguration>(bytes);
                if(c==null||c.Version!=1)throw new InvalidDataException("Invalid placement source version.");
                string root=Path.GetDirectoryName(Path.GetFullPath(configurationPath));
                var master=BendMasterAssemblyReader.Read(PlacementSourceIo.Resolve(root,c.MasterPath),PlacementSourceIo.Resolve(root,c.MasterManifestPath),PlacementSourceIo.Resolve(root,c.MasterDatumEvidencePath),token,
                    PlacementSourceIo.Resolve(root,c.HolderEvidencePath),PlacementSourceIo.Resolve(root,c.PairSeatingEvidencePath),PlacementSourceIo.Resolve(root,c.ReviewAuthoritiesPath),PlacementSourceIo.Resolve(root,c.ReviewWorkspacePath));
                var limits=BendLimitWorkbookReader.Read(PlacementSourceIo.Resolve(root,c.LimitWorkbookPath),PlacementSourceIo.Resolve(root,c.LimitManifestPath),token);
                return new PlacementSourceSnapshot(PlacementSourceIo.Hash(PlacementSourceIo.Hash(bytes)+master.Fingerprint+limits.Fingerprint),master.Tools,limits.Rules,master.Reasons.Concat(limits.Reasons));
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex){return PlacementSourceSnapshot.Missing("INVALID_PLACEMENT_CONFIGURATION:"+ex.GetType().Name);}
        }
    }
}
