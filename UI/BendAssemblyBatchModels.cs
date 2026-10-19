using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

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
        public string StatusDisplay => Status == CollisionStatus.COLLISION ? "COLLISION*" :
                                       Status == CollisionStatus.CLEAR ? "CLEAR*" : "UNKNOWN";
        public string Reason { get; set; }
        public string SourceKey { get; set; }
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
        public double AngleDeg { get; set; }
        public double RadiusMm { get; set; }
        public int FeatureId { get; set; }
        public CollisionStatus Status { get; set; }
        public string StatusDisplay => Status == CollisionStatus.COLLISION ? "COLLISION*" :
                                       Status == CollisionStatus.CLEAR ? "CLEAR*" : "UNKNOWN";
        public List<BatchToolCandidateViewModel> ToolCandidates { get; set; } = new List<BatchToolCandidateViewModel>();

        public override string ToString() => $"{BendName} ({AngleDeg:0.#}° R{RadiusMm:0.##}) - {StatusDisplay}";
    }

    /// <summary>
    /// UI-level view model for one of the 18 company tool profiles evaluated against a bend.
    /// </summary>
    internal sealed class BatchToolCandidateViewModel
    {
        public string ToolId { get; set; }
        public CollisionStatus PunchStatus { get; set; }
        public string PunchStatusDisplay => PunchStatus == CollisionStatus.COLLISION ? "COLLISION" :
                                            PunchStatus == CollisionStatus.CLEAR ? "CLEAR" : "UNKNOWN";

        public CollisionStatus HolderStatus { get; set; }
        public string HolderStatusDisplay => HolderStatus == CollisionStatus.COLLISION ? "COLLISION" :
                                             HolderStatus == CollisionStatus.CLEAR ? "CLEAR" : "Chưa xác minh";

        public CollisionStatus MotionPreBend { get; set; }
        public CollisionStatus MotionDuringBend { get; set; }
        public CollisionStatus MotionPostBend { get; set; }

        public CollisionStatus GeometricStatus { get; set; }
        public string GeometricStatusDisplay => GeometricStatus == CollisionStatus.COLLISION ? "COLLISION*" :
                                                GeometricStatus == CollisionStatus.CLEAR ? "CLEAR*" : "UNKNOWN";

        public string ManufacturingStatus => "CHƯA XÁC MINH";
        public double? PunchClearanceMm { get; set; }
        public double? HolderClearanceMm { get; set; }
        public string PunchReason { get; set; }
        public string HolderReason { get; set; }
        public string CombinedReason { get; set; }

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
        public int ActiveWorkers { get; set; }
        public long GeometryCacheHits { get; set; }
        public long StaticCertificateHits { get; set; }
        public long ProcessCertificateHits { get; set; }
        public double MemoryMb { get; set; }
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
            ActiveWorkers = 1
        };
    }

    /// <summary>
    /// Converter to transform Phase 4A.1 backend AssemblyBatchSnapshot into UI view models.
    /// </summary>
    internal static class BatchViewModelConverter
    {
        public static BatchSummaryViewModel FromSnapshot(AssemblyBatchSnapshot snapshot)
        {
            if (snapshot == null) return BatchSummaryViewModel.CreateEmpty();

            int clearCount = snapshot.Parts.Count(p => p.GeometricStatus == CollisionStatus.CLEAR);
            int collCount = snapshot.Parts.Count(p => p.GeometricStatus == CollisionStatus.COLLISION);
            int unkCount = snapshot.Parts.Count(p => p.GeometricStatus == CollisionStatus.UNKNOWN);
            int checkedParts = clearCount + collCount + unkCount;
            int totalParts = Math.Max(1, snapshot.Parts.Count);
            int progress = (int)Math.Round((double)checkedParts / totalParts * 100.0);

            var diag = new List<string>();
            if (snapshot.LightweightOccurrences > 0)
                diag.Add($"Phát hiện {snapshot.LightweightOccurrences} component ở trạng thái Lightweight. Cần Resolve để kiểm tra đầy đủ.");
            if (snapshot.UnsupportedOccurrences > 0)
                diag.Add($"Bỏ qua {snapshot.UnsupportedOccurrences} component không hỗ trợ (Non-SheetMetal, Unloaded, v.v.).");
            if (snapshot.UnresolvedBranches > 0)
                diag.Add($"Có {snapshot.UnresolvedBranches} nhánh subassembly chưa xác minh được danh sách chi tiết con.");
            if (!string.IsNullOrWhiteSpace(snapshot.InvalidReason))
                diag.Add($"Trạng thái kiểm tra: {snapshot.InvalidReason}");

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
                ElapsedSeconds = Math.Round(snapshot.WorkerMilliseconds / 1000.0, 1),
                ActiveWorkers = snapshot.PeakWorkers > 0 ? snapshot.PeakWorkers : 2,
                GeometryCacheHits = snapshot.GeometryHits,
                StaticCertificateHits = snapshot.StaticHits,
                ProcessCertificateHits = snapshot.ProcessHits,
                MemoryMb = Math.Round(snapshot.PeakManagedBytes / (1024.0 * 1024.0), 1),
                Diagnostics = diag
            };
        }

        public static List<BatchPartViewModel> PartsFromSnapshot(AssemblyBatchSnapshot snapshot)
        {
            var result = new List<BatchPartViewModel>();
            if (snapshot?.Parts == null) return result;

            foreach (var part in snapshot.Parts)
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
                    Status = part.GeometricStatus,
                    Reason = part.Reason ?? (part.GeometricStatus == CollisionStatus.CLEAR ? "Tất cả nếp CLEAR hình học" : "Chưa đủ bằng chứng mapping và vị trí gá Holder."),
                    SourceKey = part.SourceKey,
                    RawPartResult = part
                };

                foreach (var occ in occs)
                {
                    string badge = "Bình thường";
                    if (occ.Kind == AssemblyOccurrenceKind.Lightweight) badge = "Lightweight";
                    else if (occ.Kind == AssemblyOccurrenceKind.Suppressed) badge = "Suppressed";
                    else if (occ.Kind != AssemblyOccurrenceKind.SheetMetal) badge = occ.Kind.ToString();
                    else if (occ.Mirrored) badge = "Mirrored";
                    else if (occ.Flexible) badge = "Flexible";
                    else if (occ.Modified) badge = "Modified-in-Assem";

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

                // Populate bends and 18 tools
                for (int b = 1; b <= Math.Max(1, part.BendCount); b++)
                {
                    int bendIdx = b;
                    var bendVm = new BatchBendViewModel
                    {
                        BendIndex = bendIdx,
                        BendName = $"Bend {bendIdx:D2}",
                        AngleDeg = 90.0,
                        RadiusMm = 1.0,
                        FeatureId = bendIdx,
                        Status = part.GeometricStatus
                    };

                    foreach (string toolId in CompanyToolIds.All)
                    {
                        var staticCand = part.StaticCandidates?.FirstOrDefault(c => c.ToolId == toolId && c.FeatureId == bendIdx);
                        var procCand = part.ProcessCandidates?.FirstOrDefault(c => c.ToolId == toolId && c.FeatureId == bendIdx);

                        CollisionStatus punch = staticCand != null ? staticCand.PunchResult.Status : CollisionStatus.UNKNOWN;
                        CollisionStatus holder = staticCand != null ? staticCand.HolderResult.Status : CollisionStatus.UNKNOWN;
                        CollisionStatus geom = procCand != null ? procCand.ProcessResult :
                                              staticCand != null ? staticCand.CombinedResult : CollisionStatus.UNKNOWN;

                        bendVm.ToolCandidates.Add(new BatchToolCandidateViewModel
                        {
                            ToolId = toolId,
                            PunchStatus = punch,
                            HolderStatus = holder,
                            MotionPreBend = procCand != null ? procCand.PunchResult : CollisionStatus.UNKNOWN,
                            MotionDuringBend = procCand != null ? procCand.ProcessResult : CollisionStatus.UNKNOWN,
                            MotionPostBend = procCand != null ? procCand.HolderResult : CollisionStatus.UNKNOWN,
                            GeometricStatus = geom,
                            PunchReason = staticCand?.PunchResult?.Reasons?.FirstOrDefault() ?? "chưa đủ bằng chứng mapping",
                            HolderReason = staticCand?.HolderResult?.Reasons?.FirstOrDefault() ?? "vị trí gá Holder chưa xác minh",
                            CombinedReason = part.Reason ?? "chưa đủ bằng chứng mapping và vị trí gá Holder."
                        });
                    }

                    partVm.Bends.Add(bendVm);
                }

                result.Add(partVm);
            }

            return result;
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
