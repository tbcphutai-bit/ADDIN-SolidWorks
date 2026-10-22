using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendTools;
using ADDIN.Commands.BendPlacement;

namespace ADDIN.UI
{
    /// <summary>
    /// UI-level view model for an assembly member part group (canonical source/configuration).
    /// </summary>
    internal sealed class BatchPartViewModel
    {
        public string PartName { get; set; }
        public string Configuration { get; set; }
        public int Quantity { get; set; }
        public int BendCount { get; set; }
        public CollisionStatus Status { get; set; }
        public CollisionStatus ProcessStatus { get; set; } = CollisionStatus.UNKNOWN;
        public string StatusDisplay => Status == CollisionStatus.COLLISION ? "Có phương án va chạm*" : BendCheckDisplayText.Collision(Status);
        public string ManufacturingStatus => "CHƯA XÁC MINH";
        public string Reason { get; set; }
        // These source-health fields are captured WITH the audited run projection.
        // Catalog IDs are never interchangeable with loaded Punch/Holder geometry.
        public string ToolSourceError { get; set; }
        public string ToolSourceFingerprint { get; set; }
        public int ToolCatalogCount { get; set; }
        public int LoadedPunchCount { get; set; }
        public int ReadyGeometryPairs { get; set; }
        public int VerifiedMountingPairs { get; set; }
        public int ExcelRuleCount { get; set; }
        public bool HasCandidateCollision { get; set; }
        public string SourceKey { get; set; }
        public string SelectionKey => RawPartResult?.OccurrenceSetKey ?? SourceKey;
        public bool IsExpanded { get; set; }
        public List<BatchOccurrenceViewModel> Occurrences { get; set; } = new List<BatchOccurrenceViewModel>();
        public List<BatchBendViewModel> Bends { get; set; } = new List<BatchBendViewModel>();
        public AssemblyPartResult RawPartResult { get; set; }

        public override string ToString() => $"{PartName} ({Configuration}) - Qty: {Quantity}, Bends: {BendCount}, Status: {StatusDisplay}";
    }

    /// <summary>
    /// UI-level view model for an individual occurrence inside the assembly.
    /// </summary>
    internal sealed class BatchOccurrenceViewModel
    {
        public string Id { get; set; }
        public string DisplayPath { get; set; }
        public string Configuration { get; set; }
        public AssemblyOccurrenceKind Kind { get; set; }
        public string StatusBadge { get; set; }
        public string Reason { get; set; }
        public bool IsRigid { get; set; }
        public bool IsMirrored { get; set; }
        public bool IsFlexible { get; set; }
        public bool IsModified { get; set; }
        public AssemblyOccurrence RawOccurrence { get; set; }

        public override string ToString() => $"{DisplayPath} [{Kind}] ({StatusBadge})";
    }

    /// <summary>
    /// UI-level view model for a single bend line on a part.
    /// </summary>
    internal sealed class BatchBendViewModel
    {
        public int BendIndex { get; set; }
        public string BendName { get; set; }
        public double? AngleDeg { get; set; }
        public double? RadiusMm { get; set; }
        public string AngleDisplay => AngleDeg.HasValue ? $"{AngleDeg:0.#}°" : "Chưa có dữ liệu";
        public string RadiusDisplay => RadiusMm.HasValue ? $"R{RadiusMm:0.##} mm" : "Chưa có dữ liệu";
        public int FeatureId { get; set; }
        public CollisionStatus Status { get; set; }
        public string StatusDisplay => BendCheckDisplayText.Collision(Status);
        public List<BatchToolCandidateViewModel> ToolCandidates { get; set; } = new List<BatchToolCandidateViewModel>();

        public override string ToString() => $"{BendName} (Góc: {AngleDisplay}; bán kính: {RadiusDisplay}) - {StatusDisplay}";
    }

    internal sealed class BatchMotionSegmentViewModel
    {
        public BendProcessPhase Phase { get; set; }
        public CollisionPairKind Pair { get; set; }
        public string SegmentId { get; set; }
        public CollisionStatus Status { get; set; }
        public string Reason { get; set; }
        public override string ToString() => $"{Phase} / {SegmentId} / {Pair}: {Status} — {Reason}";
        public ProcessCollisionWitness Witness { get; set; }
    }

    /// <summary>
    /// UI-level view model for one of the 18 company tool profiles evaluated against a bend.
    /// </summary>
    internal sealed class BatchToolCandidateViewModel
    {
        public ToolAssemblyProfile CadAssembly { get; set; }
        public SheetEnvelopeCapture NativeEnvelope { get; set; }
        public bool StaticSimulationOnly { get; set; }
        public bool ProcessSimulationOnly { get; set; }
        public string ToolId { get; set; }
        public int FeatureId { get; set; }
        public CollisionStatus PunchStatus { get; set; }
        public string PunchStatusDisplay => BendCheckDisplayText.Collision(PunchStatus);

        public CollisionStatus HolderStatus { get; set; }
        public string HolderStatusDisplay => BendCheckDisplayText.Collision(HolderStatus);

        public CollisionStatus MotionPreBend { get; set; }
        public CollisionStatus MotionDuringBend { get; set; }
        public CollisionStatus MotionPostBend { get; set; }
        public CollisionStatus ProcessStatus { get; set; } = CollisionStatus.UNKNOWN;
        public List<BatchMotionSegmentViewModel> MotionSegments { get; set; } = new List<BatchMotionSegmentViewModel>();
        public CollisionStatus PhaseStatus(BendProcessPhase phase)
        {
            var rows = MotionSegments.Where(s => s.Phase == phase).ToArray();
            if (rows.Any(s => s.Status == CollisionStatus.COLLISION)) return CollisionStatus.COLLISION;
            var punch = rows.Where(s => s.Pair == CollisionPairKind.WorkpiecePunch).ToArray();
            var holder = rows.Where(s => s.Pair == CollisionPairKind.WorkpieceHolder).ToArray();
            bool complete = punch.Length > 0 && punch.Select(s => s.SegmentId).Distinct().Count() == punch.Length &&
                holder.Length == punch.Length && holder.Select(s => s.SegmentId).Distinct().Count() == holder.Length &&
                punch.All(s => !string.IsNullOrWhiteSpace(s.SegmentId)) &&
                punch.Select(s => s.SegmentId).OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(
                    holder.Select(s => s.SegmentId).OrderBy(s => s, StringComparer.Ordinal));
            return complete && rows.All(s => s.Status == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
        }
        public string MotionDisplay => BendCheckDisplayText.Collision(ProcessStatus);
        public string MotionDetail => MotionSegments.Count == 0 ? "Chưa có dữ liệu quá trình." :
            string.Join(Environment.NewLine, MotionSegments.Select(s => s.ToString()));

        public CollisionStatus GeometricStatus { get; set; }
        public string GeometricStatusDisplay => BendCheckDisplayText.Collision(GeometricStatus);

        public string ManufacturingStatus => "CHƯA XÁC MINH";
        public double? PunchClearanceMm { get; set; }
        public double? HolderClearanceMm { get; set; }
        public string PunchReason { get; set; }
        public string HolderReason { get; set; }
        public string CombinedReason { get; set; }
        public AxialCollisionWitness PunchWitness { get; set; }
        public AxialCollisionWitness HolderWitness { get; set; }
        public string PunchWitnessFrameId { get; set; }
        public string HolderWitnessFrameId { get; set; }
        public string PunchWitnessSourceSha256 { get; set; }
        public string HolderWitnessSourceSha256 { get; set; }

        public override string ToString() => $"{ToolId} - Punch: {PunchStatusDisplay}, Holder: {HolderStatusDisplay}, Geom: {GeometricStatusDisplay}";
    }

    /// <summary>
    /// UI-level aggregated assembly summary metrics.
    /// </summary>
    internal sealed class BatchSummaryViewModel
    {
        public int TotalComponents { get; set; }
        public int UniqueParts { get; set; }
        public int CheckedParts { get; set; }
        public int UnknownParts { get; set; }
        public int ClearParts { get; set; }
        public int CollisionParts { get; set; }
        public int SkippedComponents { get; set; }
        public int ProgressPercent { get; set; }
        public double ElapsedSeconds { get; set; }
        public double NativeCaptureSeconds { get; set; }
        public double WorkerSeconds { get; set; }
        public bool StageTimingsAvailable { get; set; }
        public bool CompletionConfirmed { get; set; }
        public int ActiveWorkers { get; set; }
        public long GeometryCacheHits { get; set; }
        public long StaticCertificateHits { get; set; }
        public long ProcessCertificateHits { get; set; }
        public double MemoryMb { get; set; }
        public long NumericQueries { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();

        public static BatchSummaryViewModel CreateEmpty() => new BatchSummaryViewModel
        {
            TotalComponents = 0,
            UniqueParts = 0,
            CheckedParts = 0,
            UnknownParts = 0,
            ClearParts = 0,
            CollisionParts = 0,
            SkippedComponents = 0,
            ProgressPercent = 0,
            ElapsedSeconds = 0,
            ActiveWorkers = 0
        };
    }

    /// <summary>
    /// Converter to transform Phase 4A.1 backend AssemblyBatchSnapshot into UI view models.
    /// </summary>
    internal sealed class BatchViewModelProjection
    {
        internal BatchSummaryViewModel Summary { get; set; } = BatchSummaryViewModel.CreateEmpty();
        internal List<BatchPartViewModel> Parts { get; set; } = new List<BatchPartViewModel>();
        internal double SnapshotValidationMilliseconds { get; set; }
        internal double SnapshotCopyMilliseconds { get; set; }
        internal double ConversionMilliseconds { get; set; }
        internal bool Current { get; set; }
    }

    internal static class BatchViewModelConverter
    {
        internal static BatchViewModelProjection Convert(AssemblyBatchSnapshot snapshot, double elapsedSeconds = 0)
        {
            var answer = new BatchViewModelProjection();
            var read = snapshot?.ReadSnapshot();
            if (read == null) return answer;
            answer.SnapshotValidationMilliseconds = read.ValidationMilliseconds;
            answer.SnapshotCopyMilliseconds = read.CopyMilliseconds;
            var clock = Stopwatch.StartNew();
            answer.Parts = PartsFromRead(read);
            answer.Summary = SummaryFromRead(read, answer.Parts, elapsedSeconds);
            answer.ConversionMilliseconds = clock.Elapsed.TotalMilliseconds;
            clock.Restart(); answer.Current = read.Current;
            answer.SnapshotValidationMilliseconds += clock.Elapsed.TotalMilliseconds;
            if (!answer.Current)
            { answer.Parts.Clear(); answer.Summary = BatchSummaryViewModel.CreateEmpty(); }
            return answer;
        }
        public static BatchSummaryViewModel FromSnapshot(AssemblyBatchSnapshot snapshot, double elapsedSeconds = 0) =>
            Convert(snapshot, elapsedSeconds).Summary;
        public static List<BatchPartViewModel> PartsFromSnapshot(AssemblyBatchSnapshot snapshot) => Convert(snapshot).Parts;

        private static BatchSummaryViewModel SummaryFromRead(AssemblyBatchReadSnapshot read, List<BatchPartViewModel> parts, double elapsedSeconds)
        {
            var snapshot = read.Source;
            int clearCount = parts.Count(p => p.Status == CollisionStatus.CLEAR);
            int collCount = parts.Count(p => p.Status == CollisionStatus.COLLISION);
            int unkCount = snapshot.Parts.Count - clearCount - collCount;
            int checkedParts = clearCount + collCount + unkCount;
            var reasons = read.Parts.SelectMany(p => p.StaticCandidates.SelectMany(c => c.Reasons)
                .Concat(p.ProcessCandidates.SelectMany(c => c.PairSegments.SelectMany(s => s.Reasons))))
                .Concat(read.Parts.Select(p => p.Reason));
            bool interrupted = reasons.Any(r => r != null && (r.Contains("BUDGET") || r.Contains("CANCELLED") ||
                r.Contains("STALE") || r.Contains("SUPERSEDED"))) || read.Parts.Any(p => !p.Current);
            bool complete = snapshot.InventoryComplete && !interrupted && parts.All(p => p.Bends.Count == p.BendCount);
            int progress = complete ? 100 : checkedParts > 0 ? 99 : 0;

            var diag = new List<string>();
            if (snapshot.LightweightOccurrences > 0)
                diag.Add($"Có {snapshot.LightweightOccurrences} chi tiết chưa nạp đầy đủ. Cần nạp đầy đủ trong SOLIDWORKS để kiểm tra.");
            if (snapshot.UnsupportedOccurrences > 0)
                diag.Add($"Bỏ qua {snapshot.UnsupportedOccurrences} chi tiết chưa hỗ trợ hoặc chưa nạp được.");
            if (snapshot.UnresolvedBranches > 0)
                diag.Add($"Có {snapshot.UnresolvedBranches} nhánh cụm lắp ráp con chưa xác minh được danh sách chi tiết con.");
            if (!complete) diag.Add("Tác vụ chưa hoàn tất hợp lệ; không xác nhận 100%.");
            diag.Add("Hình học và quá trình là kết quả theo từng ứng viên. Một phương án dao va chạm không chứng minh toàn bộ chi tiết không chấn được. Sản xuất: CHƯA XÁC MINH.");

            return new BatchSummaryViewModel
            {
                TotalComponents = snapshot.ComponentOccurrences,
                UniqueParts = snapshot.UniqueSourceConfigurations,
                CheckedParts = checkedParts,
                UnknownParts = unkCount,
                ClearParts = clearCount,
                CollisionParts = collCount,
                SkippedComponents = snapshot.SuppressedOccurrences + snapshot.LightweightOccurrences + snapshot.UnsupportedOccurrences,
                ProgressPercent = progress,
                ElapsedSeconds = elapsedSeconds,
                NativeCaptureSeconds = snapshot.CaptureMilliseconds / 1000.0,
                WorkerSeconds = snapshot.WorkerMilliseconds / 1000.0,
                StageTimingsAvailable = true,
                CompletionConfirmed = complete,
                ActiveWorkers = snapshot.PeakWorkers,
                GeometryCacheHits = snapshot.GeometryHits,
                StaticCertificateHits = snapshot.StaticHits,
                ProcessCertificateHits = snapshot.ProcessHits,
                MemoryMb = Math.Round(snapshot.PeakManagedBytes / (1024.0 * 1024.0), 1),
                NumericQueries = snapshot.KernelQueries,
                Diagnostics = diag
            };
        }

        private static List<BatchPartViewModel> PartsFromRead(AssemblyBatchReadSnapshot read)
        {
            var result = new List<BatchPartViewModel>();

            foreach (var part in read.Parts)
            {
                var occs = part.Occurrences ?? new AssemblyOccurrence[0];
                string firstDisplay = occs.FirstOrDefault()?.DisplayPath ?? part.SourceKey;
                string partName = System.IO.Path.GetFileNameWithoutExtension(firstDisplay);
                if (string.IsNullOrWhiteSpace(partName)) partName = part.SourceKey ?? "UnknownPart";
                string config = occs.FirstOrDefault()?.Configuration ?? "Default";

                var partVm = new BatchPartViewModel
                {
                    PartName = partName,
                    Configuration = config,
                    Quantity = part.Quantity,
                    BendCount = part.BendCount,
                    Status = CollisionStatus.UNKNOWN,
                    Reason = !part.Current ? "SOURCE_INVALIDATED — cần chạy lại." : part.Reason ??
                        "Kết quả theo ứng viên; chưa xác minh sản xuất hoặc chọn dao cho toàn Part.",
                    SourceKey = part.SourceKey,
                    ToolSourceError = read.ToolSourceError,
                    ToolSourceFingerprint = read.ToolSourceFingerprint,
                    ToolCatalogCount = read.CatalogCount,
                    LoadedPunchCount = read.LoadedPunchCount,
                    ReadyGeometryPairs = read.GeometryPairCount,
                    VerifiedMountingPairs = read.VerifiedMountingPairCount,
                    ExcelRuleCount = read.ExcelRuleCount,
                    RawPartResult = part.Source
                };

                foreach (var occ in occs)
                {
                    string badge = "Bình thường";
                    if (occ.Kind == AssemblyOccurrenceKind.Lightweight) badge = "Chưa nạp đầy đủ";
                    else if (occ.Kind == AssemblyOccurrenceKind.Suppressed) badge = "Tạm loại";
                    else if (occ.Kind != AssemblyOccurrenceKind.SheetMetal) badge = occ.Kind.ToString();
                    else if (occ.Mirrored) badge = "Đối xứng";
                    else if (occ.Flexible) badge = "Cụm linh hoạt";
                    else if (occ.Modified) badge = "Đã sửa trong cụm";

                    partVm.Occurrences.Add(new BatchOccurrenceViewModel
                    {
                        Id = occ.Id,
                        DisplayPath = occ.DisplayPath,
                        Configuration = occ.Configuration,
                        Kind = occ.Kind,
                        StatusBadge = badge,
                        Reason = occ.Reason ?? AssemblyOccurrenceEligibility.Reason(occ),
                        IsRigid = occ.RigidInstanceTransform,
                        IsMirrored = occ.Mirrored,
                        IsFlexible = occ.Flexible,
                        IsModified = occ.Modified,
                        RawOccurrence = occ
                    });
                }

                // Native Check Min bend metadata wins over tool candidates. A bend
                // must remain selectable even when there are zero source tools or
                // collision queries. Unknown recognition is NOT a verified zero.
                var nativeIds = part.Source.BendMetadata.Where(m=>m!=null && m.FeatureId>0)
                    .Select(m=>m.FeatureId).Distinct().OrderBy(id=>id).ToArray();
                var candidateIds = part.StaticCandidates.Select(c=>c.FeatureId)
                    .Concat(part.ProcessCandidates.Select(c=>c.FeatureId))
                    .Where(id=>id>0).Distinct().OrderBy(id=>id).ToArray();
                var featureIds = part.BendCount==0 ? new int[0] :
                    nativeIds.Length==part.BendCount ? nativeIds :
                    nativeIds.Concat(candidateIds).Distinct().OrderBy(id=>id).ToArray();
                if (featureIds.Length != part.BendCount)
                    partVm.Reason += " UNRECOGNIZED_BEND_INVENTORY: có " + part.BendCount +
                        " nếp báo cáo nhưng chỉ đối chiếu được " + featureIds.Length + " FeatureId.";
                var staticByFeature = part.StaticCandidates.ToLookup(c => c.FeatureId);
                var processByFeature = part.ProcessCandidates.ToLookup(c => c.FeatureId);
                foreach (int featureId in featureIds)
                {
                    var metadata = part.Source.BendMetadata.Where(b => b.FeatureId == featureId).ToArray();
                    var native = metadata.Length == 1 ? metadata[0] : null;
                    var bendVm = new BatchBendViewModel
                    {
                        BendIndex = partVm.Bends.Count + 1,
                        BendName = native == null ? $"Mã nếp {featureId}" : $"{native.Name} [{featureId}]",
                        FeatureId = featureId,
                        AngleDeg = native?.AngleDeg, RadiusMm = native?.RadiusMm,
                        Status = CollisionStatus.UNKNOWN
                    };

                    var staticByTool = staticByFeature[featureId].ToLookup(c => c.ToolId, StringComparer.Ordinal);
                    var processByTool = processByFeature[featureId].ToLookup(c => c.ToolId, StringComparer.Ordinal);
                    var availableEnvelopes = part.Source.ReviewEnvelopes.Where(e=>e?.Binding?.FeatureId==featureId).Take(2).ToArray();
                    var verifiedEnvelope = availableEnvelopes.Length==1 ? availableEnvelopes[0] : null;
                    foreach (string toolId in CompanyToolIds.All)
                    {
                        var statics = staticByTool[toolId].ToArray();
                        var processes = processByTool[toolId].ToArray();
                        var staticCand = part.Current && statics.Length == 1 ? statics[0] : null;
                        var procCand = part.Current && processes.Length == 1 ? processes[0] : null;

                        CollisionStatus punch = staticCand != null ? staticCand.PunchResult.Status : CollisionStatus.UNKNOWN;
                        CollisionStatus holder = staticCand != null ? staticCand.HolderResult.Status : CollisionStatus.UNKNOWN;
                        CollisionStatus geom = staticCand != null ? staticCand.CombinedResult : CollisionStatus.UNKNOWN;

                        var toolVm = new BatchToolCandidateViewModel
                        {
                            CadAssembly = read.ToolPairs.Where(p=>p!=null && p.Id==toolId).Take(2).Count()==1 ?
                                read.ToolPairs.First(p=>p!=null && p.Id==toolId) : null,
                            NativeEnvelope = verifiedEnvelope,
                            ToolId = toolId,
                            FeatureId = featureId,
                            StaticSimulationOnly = part.SimulationOnly || staticCand?.SimulationOnly==true,
                            ProcessSimulationOnly = part.SimulationOnly || procCand?.SimulationOnly==true,
                            PunchStatus = punch,
                            HolderStatus = holder,
                            GeometricStatus = geom,
                            PunchClearanceMm = staticCand?.PunchResult?.CertifiedClearanceLowerMm,
                            HolderClearanceMm = staticCand?.HolderResult?.CertifiedClearanceLowerMm,
                            PunchWitness = staticCand?.PunchResult?.Witness, HolderWitness = staticCand?.HolderResult?.Witness,
                            PunchWitnessFrameId = staticCand?.PunchResult?.FrameId,
                            HolderWitnessFrameId = staticCand?.HolderResult?.FrameId,
                            PunchWitnessSourceSha256 = staticCand?.PunchResult?.SourceSha256,
                            HolderWitnessSourceSha256 = staticCand?.HolderResult?.SourceSha256,
                            PunchReason = staticCand?.PunchResult?.Reasons?.FirstOrDefault() ?? "chưa đủ bằng chứng mapping",
                            HolderReason = staticCand?.HolderResult?.Reasons?.FirstOrDefault() ?? "vị trí gá Holder chưa xác minh",
                            CombinedReason = !part.Current ? "SOURCE_INVALIDATED" : statics.Length > 1 || processes.Length > 1 ?
                                "AMBIGUOUS_CANDIDATE_BINDING" : string.Join("; ",
                                (staticCand?.Reasons ?? new[] { "MISSING_STATIC_CANDIDATE" })
                                .Concat(procCand?.PairSegments.SelectMany(s => s.Reasons) ?? new[] { "MISSING_PROCESS_CANDIDATE" })
                                .Concat(part.Source.ReadinessDiagnostics)
                                .Concat(part.Reason == null ? new string[0] : new[] { part.Reason }).Distinct()),
                            MotionSegments = procCand?.PairSegments.Select(s => new BatchMotionSegmentViewModel
                            {
                                Phase = s.Phase, Pair = s.Pair, SegmentId = s.SegmentId,
                                Status = s.Status, Reason = string.Join("; ", s.Reasons), Witness = s.Witness
                            }).ToList() ?? new List<BatchMotionSegmentViewModel>()
                        };
                        toolVm.MotionPreBend = toolVm.PhaseStatus(BendProcessPhase.PreBend);
                        toolVm.MotionDuringBend = toolVm.PhaseStatus(BendProcessPhase.DuringBend);
                        toolVm.MotionPostBend = toolVm.PhaseStatus(BendProcessPhase.RetractUnload);
                        var phases = Enum.GetValues(typeof(BendProcessPhase)).Cast<BendProcessPhase>().Select(toolVm.PhaseStatus).ToArray();
                        toolVm.ProcessStatus = phases.Contains(CollisionStatus.COLLISION) ? CollisionStatus.COLLISION :
                            phases.All(s => s == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
                        bendVm.ToolCandidates.Add(toolVm);
                    }

                    // Per-candidate tests are NOT an entire-bend feasibility proof.
                    // Keep these facts inside ToolCandidates and technical details.
                    bendVm.Status = CollisionStatus.UNKNOWN;

                    partVm.Bends.Add(bendVm);
                }

                partVm.HasCandidateCollision = partVm.Bends.SelectMany(b=>b.ToolCandidates)
                    .Any(t=>t.GeometricStatus==CollisionStatus.COLLISION || t.ProcessStatus==CollisionStatus.COLLISION);
                // A failed single tool is not an impossible Part; all candidate
                // CLEAR values are still not one complete verified bend sequence.
                partVm.Status = CollisionStatus.UNKNOWN;
                partVm.ProcessStatus = CollisionStatus.UNKNOWN;
                partVm.Reason += partVm.HasCandidateCollision ?
                    " Có ứng viên va chạm (xem Bộ dao); chưa kết luận khả năng chấn toàn Part." :
                    " Chưa xác minh đầy đủ trình tự, Excel MAX và sản xuất.";

                result.Add(partVm);
            }

            return result;
        }

        private static CollisionStatus Aggregate(IEnumerable<CollisionStatus> values, bool inventoryComplete)
        {
            var statuses = values.ToArray();
            return statuses.Contains(CollisionStatus.COLLISION) ? CollisionStatus.COLLISION :
                inventoryComplete && statuses.Length > 0 && statuses.All(s => s == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
        }

        /// <summary>
        /// Creates realistic mockup data matching the wireframe in media_1792379592993_ec79bc63.png.
        /// </summary>
        public static Tuple<BatchSummaryViewModel, List<BatchPartViewModel>> CreateMockupData()
        {
            var summary = new BatchSummaryViewModel
            {
                TotalComponents = 120,
                UniqueParts = 25,
                CheckedParts = 18,
                UnknownParts = 7,
                ClearParts = 14,
                CollisionParts = 4,
                SkippedComponents = 12,
                ProgressPercent = 72,
                ElapsedSeconds = 4.2,
                ActiveWorkers = 2,
                GeometryCacheHits = 48,
                StaticCertificateHits = 132,
                ProcessCertificateHits = 120,
                MemoryMb = 84.5,
                Diagnostics = new List<string>
                {
                    "Dữ liệu mẫu đang được hiển thị theo bản vẽ thiết kế (UI Concept).",
                    "Đã kiểm tra 18 / 25 Part (72%). 7 Part còn lại đang chờ hoặc chưa đủ bằng chứng.",
                    "Có 12 component bị bỏ qua: 8 chi tiết tiêu chuẩn (Non-SheetMetal) và 4 chi tiết Lightweight.",
                    "Phạm vi kiểm tra: Toàn bộ cụm (Entire Assembly). Tốc độ xử lý: 4.2s với 2 CPU Workers."
                }
            };

            var parts = new List<BatchPartViewModel>();

            // 1. Frame-A (Qty: 24, Bends: 6, Status: UNKNOWN)
            var frameA = new BatchPartViewModel
            {
                PartName = "Frame-A",
                Configuration = "Default",
                Quantity = 24,
                BendCount = 6,
                Status = CollisionStatus.UNKNOWN,
                Reason = "chưa đủ bằng chứng mapping và vị trí gá Holder.",
                SourceKey = "C:\\Projects\\Machine\\Frame-A.SLDPRT"
            };
            for (int i = 1; i <= 24; i++)
            {
                frameA.Occurrences.Add(new BatchOccurrenceViewModel
                {
                    Id = $"FrameA_Occ_{i:D2}",
                    DisplayPath = $"MainAssem/BaseFrame/Frame-A-{i}",
                    Configuration = "Default",
                    Kind = AssemblyOccurrenceKind.SheetMetal,
                    StatusBadge = "Bình thường",
                    IsRigid = true
                });
            }
            PopulateMockupBends(frameA, CollisionStatus.UNKNOWN, "chưa đủ bằng chứng mapping và vị trí gá Holder.");
            parts.Add(frameA);

            // 2. Bracket-B (Qty: 12, Bends: 4, Status: COLLISION*)
            var bracketB = new BatchPartViewModel
            {
                PartName = "Bracket-B",
                Configuration = "Standard",
                Quantity = 12,
                BendCount = 4,
                Status = CollisionStatus.COLLISION,
                Reason = "Va chạm chày với cạnh gập ngược tại nếp 02 (FormingEnd)",
                SourceKey = "C:\\Projects\\Machine\\Bracket-B.SLDPRT"
            };
            for (int i = 1; i <= 12; i++)
            {
                bracketB.Occurrences.Add(new BatchOccurrenceViewModel
                {
                    Id = $"BracketB_Occ_{i:D2}",
                    DisplayPath = $"MainAssem/MotorMount/Bracket-B-{i}",
                    Configuration = "Standard",
                    Kind = AssemblyOccurrenceKind.SheetMetal,
                    StatusBadge = "Bình thường",
                    IsRigid = true
                });
            }
            PopulateMockupBends(bracketB, CollisionStatus.COLLISION, "Va chạm chày với cạnh biên (-0.42mm).");
            parts.Add(bracketB);

            // 3. Panel-C (Qty: 8, Bends: 3, Status: CLEAR*)
            var panelC = new BatchPartViewModel
            {
                PartName = "Panel-C",
                Configuration = "Default",
                Quantity = 8,
                BendCount = 3,
                Status = CollisionStatus.CLEAR,
                Reason = "Tất cả nếp CLEAR trong phạm vi chứng minh hình học.",
                SourceKey = "C:\\Projects\\Machine\\Panel-C.SLDPRT"
            };
            for (int i = 1; i <= 8; i++)
            {
                panelC.Occurrences.Add(new BatchOccurrenceViewModel
                {
                    Id = $"PanelC_Occ_{i:D2}",
                    DisplayPath = $"MainAssem/Cover/Panel-C-{i}",
                    Configuration = "Default",
                    Kind = AssemblyOccurrenceKind.SheetMetal,
                    StatusBadge = "Bình thường",
                    IsRigid = true
                });
            }
            PopulateMockupBends(panelC, CollisionStatus.CLEAR, "Clearance an toàn > 1.8mm trên toàn bộ hành trình.");
            parts.Add(panelC);

            // 4. Stiffener-D (Qty: 16, Bends: 2, Status: CLEAR*)
            var stiffenerD = new BatchPartViewModel
            {
                PartName = "Stiffener-D",
                Configuration = "Short",
                Quantity = 16,
                BendCount = 2,
                Status = CollisionStatus.CLEAR,
                Reason = "CLEAR hình học trên tất cả 18 dao.",
                SourceKey = "C:\\Projects\\Machine\\Stiffener-D.SLDPRT"
            };
            for (int i = 1; i <= 16; i++)
            {
                stiffenerD.Occurrences.Add(new BatchOccurrenceViewModel
                {
                    Id = $"StiffenerD_Occ_{i:D2}",
                    DisplayPath = $"MainAssem/BaseFrame/Stiffener-D-{i}",
                    Configuration = "Short",
                    Kind = AssemblyOccurrenceKind.SheetMetal,
                    StatusBadge = "Bình thường",
                    IsRigid = true
                });
            }
            PopulateMockupBends(stiffenerD, CollisionStatus.CLEAR, "Clearance > 2.5mm.");
            parts.Add(stiffenerD);

            // 5. Door-Sheet (Qty: 2, Bends: 8, Status: UNKNOWN)
            var doorSheet = new BatchPartViewModel
            {
                PartName = "Door-Sheet",
                Configuration = "Left",
                Quantity = 2,
                BendCount = 8,
                Status = CollisionStatus.UNKNOWN,
                Reason = "Cần kiểm tra lại cối kẹp trên (Holder clearance thiếu dữ liệu)",
                SourceKey = "C:\\Projects\\Machine\\Door-Sheet.SLDPRT"
            };
            for (int i = 1; i <= 2; i++)
            {
                doorSheet.Occurrences.Add(new BatchOccurrenceViewModel
                {
                    Id = $"Door_Occ_{i}",
                    DisplayPath = $"MainAssem/DoorAssembly/Door-Sheet-{i}",
                    Configuration = "Left",
                    Kind = AssemblyOccurrenceKind.SheetMetal,
                    StatusBadge = "Bình thường",
                    IsRigid = true
                });
            }
            PopulateMockupBends(doorSheet, CollisionStatus.UNKNOWN, "Thiếu setup cối kẹp.");
            parts.Add(doorSheet);

            return Tuple.Create(summary, parts);
        }

        private static void PopulateMockupBends(BatchPartViewModel part, CollisionStatus defaultStatus, string defaultReason)
        {
            for (int b = 1; b <= part.BendCount; b++)
            {
                var bend = new BatchBendViewModel
                {
                    BendIndex = b,
                    BendName = $"Bend {b:D2}",
                    AngleDeg = b % 2 == 1 ? 90.0 : 45.0,
                    RadiusMm = 1.0,
                    FeatureId = b,
                    Status = (defaultStatus == CollisionStatus.COLLISION && b == 2) ? CollisionStatus.COLLISION :
                             (defaultStatus == CollisionStatus.COLLISION) ? CollisionStatus.CLEAR : defaultStatus
                };

                foreach (string toolId in CompanyToolIds.All)
                {
                    CollisionStatus punch = (bend.Status == CollisionStatus.COLLISION && toolId.Contains("004")) ? CollisionStatus.COLLISION :
                                            (bend.Status == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
                    CollisionStatus holder = (bend.Status == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;
                    CollisionStatus geom = punch == CollisionStatus.COLLISION ? CollisionStatus.COLLISION :
                                          (punch == CollisionStatus.CLEAR && holder == CollisionStatus.CLEAR) ? CollisionStatus.CLEAR : CollisionStatus.UNKNOWN;

                    bend.ToolCandidates.Add(new BatchToolCandidateViewModel
                    {
                        ToolId = toolId,
                        PunchStatus = punch,
                        HolderStatus = holder,
                        MotionPreBend = punch == CollisionStatus.COLLISION ? CollisionStatus.COLLISION : CollisionStatus.CLEAR,
                        MotionDuringBend = geom,
                        MotionPostBend = CollisionStatus.CLEAR,
                        GeometricStatus = geom,
                        PunchClearanceMm = punch == CollisionStatus.COLLISION ? -0.42 : (punch == CollisionStatus.CLEAR ? 2.15 : (double?)null),
                        HolderClearanceMm = holder == CollisionStatus.CLEAR ? 3.4 : (double?)null,
                        PunchReason = punch == CollisionStatus.COLLISION ? "Va chạm cạnh biên chày tại góc R" :
                                      punch == CollisionStatus.CLEAR ? "Clearance > 2mm" : "chưa đủ bằng chứng mapping",
                        HolderReason = holder == CollisionStatus.CLEAR ? "Clearance > 3mm" : "vị trí gá Holder chưa xác minh",
                        CombinedReason = geom == CollisionStatus.COLLISION ? "Va chạm chày với cạnh gập ngược" : defaultReason
                    });
                }

                part.Bends.Add(bend);
            }
        }
    }
}
