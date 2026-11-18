using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using static ADDIN.Commands.CircleFaceTubeLayout.TubeLayoutSolver;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    internal sealed class SwFaceLayout
    {
        private readonly Face2 face;
        private readonly Surface surface;
        private readonly Modeler modeler;
        private readonly Body2 ownerBody;
        private readonly double faceSense;
        private readonly Action<string> log;
        private readonly ExternalBodyContext externalContext;
        private readonly Func<Point3, Point3> toDocument;
        public readonly BoundaryRails Rails;

        private enum SheetCheck { Clear, Intersecting, Inconclusive }

        public SwFaceLayout(Face2 face, Modeler modeler, Action<string> log,
            ExternalBodyContext externalContext = null, Func<Point3, Point3> toDocument = null)
        {
            this.face = face;
            this.modeler = modeler;
            this.log = log;
            this.externalContext = externalContext;
            this.toDocument = toDocument ?? (p => p);
            surface = (Surface)face.GetSurface();
            ownerBody = face.IGetBody();
            if (surface == null || ownerBody == null)
                throw new LayoutException("The selected face has no supported surface or body.");
            faceSense = face.FaceInSurfaceSense() ? -1 : 1;
            log(F("FACE id={0} surface_type={1} face_vs_surface_sign={2} body_type={3}",
                face.GetFaceId(), surface.Identity(), faceSense, ownerBody.GetType()));

            var loops = (face.GetLoops() as object[] ?? new object[0]).OfType<Loop2>().ToArray();
            foreach (Loop2 loop in loops) log(F("LOOP outer={0} coedges={1}", loop.IsOuter(), loop.GetCoEdgeCount()));
            var outer = loops.Where(l => l.IsOuter()).ToArray();
            if (outer.Length != 1)
                throw new LayoutException("Select a face with one identifiable exterior loop.");

            int count = outer[0].GetCoEdgeCount();
            var pieces = new List<BoundaryPiece>(count);
            CoEdge coedge = outer[0].IGetFirstCoEdge();
            for (int i = 0; i < count; i++)
            {
                if (coedge == null) throw new LayoutException("The exterior loop has a missing coedge.");
                pieces.Add(ReadPiece(coedge, i + 1));
                coedge = coedge.IGetNext();
            }
            Rails = BoundaryRails.Find(pieces, log);
            log(F("RAILS small_mm={0:F6} large_mm={1:F6} small_pieces={2} large_pieces={3}",
                Rails.Small.Length / Mm, Rails.Large.Length / Mm, Rails.Small.PieceCount, Rails.Large.PieceCount));
        }

        private sealed class RailEvaluatorSet
        {
            public readonly Func<double, RailStation> SmallInside, LargeInside;
            public readonly Func<double, RailStation> SmallExtended, LargeExtended;
            public readonly Func<double, RailStation> SmallCacheInside, LargeCacheInside;
            public readonly Func<double, RailStation> SmallCacheExtended, LargeCacheExtended;

            public RailEvaluatorSet(SwFaceLayout owner, double diameter, double sign)
            {
                SmallInside = s => owner.Station(owner.Rails.Small.At(s), sign);
                LargeInside = s => owner.Station(owner.Rails.Large.At(owner.Rails.Large.Length - s), sign);
                SmallExtended = owner.ExtendRail(SmallInside, owner.Rails.Small.Length, diameter / 2, "SMALL");
                LargeExtended = owner.ExtendRail(LargeInside, owner.Rails.Large.Length, diameter / 2, "LARGE");
                SmallCacheInside = owner.CacheRail(SmallInside, owner.Rails.Small.Length, diameter, "small");
                LargeCacheInside = owner.CacheRail(LargeInside, owner.Rails.Large.Length, diameter, "large");
                SmallCacheExtended = s => (s < 0 || s > owner.Rails.Small.Length) ? SmallExtended(s) : SmallCacheInside(s);
                LargeCacheExtended = s => (s < 0 || s > owner.Rails.Large.Length) ? LargeExtended(s) : LargeCacheInside(s);
            }
        }

        public TubeLayout Analyze(int? count, double diameter, double margin, bool flip,
            SmallEndSpacingMode smallMode = SmallEndSpacingMode.TangentContact,
            bool allowSymmetricOverhang = false, double maxOverhang = 0,
            double minimumPairClearance = 0, bool checkExternalContext = true)
        {
            double sign = faceSense * (flip ? -1 : 1);
            if (!(minimumPairClearance >= 0) || double.IsNaN(minimumPairClearance) || double.IsInfinity(minimumPairClearance))
                throw new LayoutException("Minimum pair clearance must be non-negative.");
            if (minimumPairClearance > 0 && smallMode != SmallEndSpacingMode.EqualPitch)
                throw new LayoutException("Minimum pair clearance is only supported in EqualPitch mode.");

            // V3 is strictly opt-in and applies only to Custom + EqualPitch.
            if (!allowSymmetricOverhang)
            {
                var rails = new RailEvaluatorSet(this, diameter, sign);
                return AnalyzeOne(count, diameter, margin, flip, smallMode, false, sign, rails,
                    minimumPairClearance, checkExternalContext, skipSheetOnGeometryFail: false);
            }
            if (!count.HasValue || smallMode != SmallEndSpacingMode.EqualPitch)
                throw new LayoutException("Smart Overhang requires Custom count and EqualPitch.");
            if (!(margin >= 0) || !(diameter > 0) || !(maxOverhang >= 0) ||
                maxOverhang > diameter || double.IsNaN(maxOverhang) || double.IsInfinity(maxOverhang))
                throw new LayoutException("Max cap overhang must be between 0 and tube diameter.");

            log(F("SMART_OVERHANG enabled=True count={0} baseline_margin_mm={1:F3} max_cap_overhang_mm={2:F3} min_pair_gap_mm={3:F3} check_context={4}",
                count.Value, margin / Mm, maxOverhang / Mm, minimumPairClearance / Mm, checkExternalContext));
            var evalSet = new RailEvaluatorSet(this, diameter, sign);
            TubeLayout baseline = null, last = null, bestAccepted = null;
            string baselineFailure = null;
            bool baselineAborted = false;
            try
            {
                baseline = AnalyzeOne(count, diameter, margin, flip, smallMode, false, sign, evalSet,
                    minimumPairClearance, checkExternalContext, skipSheetOnGeometryFail: true,
                    stopContextOnFirstClash: true, out baselineAborted);
                baseline.SmartOverhangEnabled = true;
                baseline.EffectiveCapMargin = margin;
                if (baseline.Issue == null)
                {
                    log("SMART_OVERHANG kept_baseline=true overhang_mm=0");
                    return baseline;
                }
                baselineFailure = baseline.Issue;
                log("SMART_OVERHANG baseline_not_feasible reason=" + baselineFailure);
            }
            catch (LayoutException ex)
            {
                baselineFailure = ex.Message;
                log("SMART_OVERHANG baseline_rejected reason=" + baselineFailure);
            }

            int attempts = 0;
            // Coarse scan across 0.5 mm increments followed by bounded bisection
            // refinement (~0.06 mm) on the first passing bracket; never skip checks.
            const double step = 0.5 * Mm;
            int steps = (int)Math.Ceiling(maxOverhang / step);
            double lastFailedH = 0;
            double? firstPassH = null;
            bool lastAborted = false;

            TubeLayout RunTrial(double h, string tag)
            {
                attempts++;
                log(F("{0} index={1} target_cap_mm={2:F4}", tag, attempts, -h / Mm));
                bool aborted;
                TubeLayout candidate = AnalyzeOne(count, diameter, -h, flip, smallMode, true, sign, evalSet,
                    minimumPairClearance, checkExternalContext, skipSheetOnGeometryFail: true,
                    stopContextOnFirstClash: true, out aborted);
                candidate.SmartOverhangEnabled = true;
                candidate.OverhangSearchAttempts = attempts;
                candidate.AppliedOverhang = h;
                candidate.EffectiveCapMargin = -h;
                double smallLast = candidate.SmallStations[count.Value - 1];
                double largeLast = candidate.FirstLargeStation + (count.Value - 1) * candidate.LargePitch;
                candidate.MaximumCenterExtension = Math.Max(0,
                    Math.Max(Math.Max(-candidate.SmallStations[0], smallLast - Rails.Small.Length),
                        Math.Max(-candidate.FirstLargeStation, largeLast - Rails.Large.Length)));
                candidate.CenterExtrapolated = candidate.MaximumCenterExtension > 1e-8;
                if (candidate.CenterExtrapolated)
                    log(F("OVERHANG_EXTRAPOLATED center_ext_mm={0:F6}", candidate.MaximumCenterExtension / Mm));
                log(F("SMART_OVERHANG_RESULT index={0} cap_mm={1:F3} issue={2} min_gap_mm={3:F6} sheet_clashes={4} uncertain={5} context_clashes={6}",
                    attempts, -h / Mm, candidate.Issue ?? "OK", candidate.MinimumGap / Mm,
                    candidate.SheetClashCount, candidate.SheetInconclusiveCount, candidate.ContextClashCount));
                last = candidate;
                lastAborted = aborted;
                return candidate;
            }

            for (int i = 0; i <= steps; i++)
            {
                double h = Math.Min(maxOverhang, i * step);
                if (i > 0 && h <= (i - 1) * step) continue;
                if (i == 0 && margin == 0 && baseline != null && baseline.InterferingPairCount > 0)
                {
                    lastFailedH = 0;
                    continue;
                }
                try
                {
                    TubeLayout candidate = RunTrial(h, "SMART_OVERHANG_TRIAL");
                    if (candidate.Issue == null)
                    {
                        firstPassH = h;
                        bestAccepted = candidate;
                        break;
                    }
                    lastFailedH = h;
                }
                catch (LayoutException ex)
                {
                    lastFailedH = h;
                    log(F("SMART_OVERHANG_REJECT index={0} cap_mm={1:F3} reason={2}",
                        attempts, -h / Mm, ex.Message));
                }
            }

            if (bestAccepted != null)
            {
                if (firstPassH.HasValue && firstPassH.Value - lastFailedH > 0.08 * Mm)
                {
                    double lo = lastFailedH, hi = firstPassH.Value;
                    for (int refine = 0; refine < 3; refine++)
                    {
                        double mid = Math.Round((lo + hi) * 0.5 / (0.05 * Mm)) * (0.05 * Mm);
                        if (mid <= lo + 1e-7 || mid >= hi - 1e-7) break;
                        try
                        {
                            TubeLayout refined = RunTrial(mid, "SMART_OVERHANG_REFINE");
                            if (refined.Issue == null)
                            {
                                hi = mid;
                                bestAccepted = refined;
                                bestAccepted.OverhangSearchAttempts = attempts;
                            }
                            else
                            {
                                lo = mid;
                            }
                        }
                        catch (LayoutException ex)
                        {
                            lo = mid;
                            log(F("SMART_OVERHANG_REJECT index={0} cap_mm={1:F3} reason={2}",
                                attempts, -mid / Mm, ex.Message));
                        }
                    }
                }
                log(F("SMART_OVERHANG_ACCEPT cap_overhang_mm={0:F3} trials={1}", bestAccepted.AppliedOverhang / Mm, attempts));
                return bestAccepted;
            }

            if (baseline != null && baseline.InterferingPairCount == 0 && baseline.SheetClashCount == 0 &&
                baseline.ContextClashCount > 0 && (last == null || last.ContextClashCount > 0))
            {
                if (baselineAborted) CompleteFailureDiagnostics(baseline, diameter, sign, checkExternalContext);
                AddIssue(baseline, F("Smart Overhang found no verified layout up to {0:F2} mm.", maxOverhang / Mm));
                return baseline;
            }
            if (last != null)
            {
                if (lastAborted) CompleteFailureDiagnostics(last, diameter, sign, checkExternalContext);
                AddIssue(last, F("Smart Overhang found no verified layout up to {0:F2} mm (baseline: {1}).",
                    maxOverhang / Mm, baselineFailure ?? "unavailable"));
                return last;
            }
            if (baseline != null)
            {
                if (baselineAborted) CompleteFailureDiagnostics(baseline, diameter, sign, checkExternalContext);
                AddIssue(baseline, F("Smart Overhang found no verified layout up to {0:F2} mm.",
                    maxOverhang / Mm));
                return baseline;
            }
            throw new LayoutException(F("No verified Smart Overhang solution within {0:F2} mm. Baseline: {1}",
                maxOverhang / Mm, baselineFailure ?? "unknown"));
        }

        private TubeLayout AnalyzeOne(int? count, double diameter, double margin, bool flip,
            SmallEndSpacingMode smallMode, bool overhangCandidate, double sign, RailEvaluatorSet evalSet,
            double minimumPairClearance, bool checkExternalContext, bool skipSheetOnGeometryFail)
        {
            bool unused;
            return AnalyzeOne(count, diameter, margin, flip, smallMode, overhangCandidate, sign, evalSet,
                minimumPairClearance, checkExternalContext, skipSheetOnGeometryFail,
                stopContextOnFirstClash: false, out unused);
        }

        private TubeLayout AnalyzeOne(int? count, double diameter, double margin, bool flip,
            SmallEndSpacingMode smallMode, bool overhangCandidate, double sign, RailEvaluatorSet evalSet,
            double minimumPairClearance, bool checkExternalContext, bool skipSheetOnGeometryFail,
            bool stopContextOnFirstClash, out bool diagnosticsAborted)
        {
            diagnosticsAborted = false;
            if (!count.HasValue && smallMode == SmallEndSpacingMode.EqualPitch)
                throw new LayoutException("Equal-pitch small end requires Custom count.");
            log(F("SETTINGS flip={0} selected_normal_sign={1} large_edge_margin_mm={2:F6} count={3} small_mode={4} min_pair_gap_mm={5:F3} check_context={6}",
                flip, sign, margin / Mm, count?.ToString() ?? "auto", smallMode, minimumPairClearance / Mm, checkExternalContext));
            Func<double, RailStation> Small = overhangCandidate ? evalSet.SmallExtended : evalSet.SmallInside;
            Func<double, RailStation> Large = overhangCandidate ? evalSet.LargeExtended : evalSet.LargeInside;
            Func<double, RailStation> smallCache = overhangCandidate ? evalSet.SmallCacheExtended : evalSet.SmallCacheInside;
            Func<double, RailStation> largeCache = overhangCandidate ? evalSet.LargeCacheExtended : evalSet.LargeCacheInside;

            // Shared outward lift keeps offsets coherent. Re-solve contact after each
            // lift. A bounded search deliberately rejects faces that require more.
            double lift = 0.01 * Mm;
            TubeLayout layout = null;
            int clashes = 0;
            int uncertain = 0;
            int startAxis = 0;
            bool flippedSolidClash = false;
            for (int pass = 0; pass < 9 && lift <= 2 * diameter; pass++)
            {
                if (count.HasValue)
                {
                    layout = Solve(smallCache, Rails.Small.Length, largeCache, Rails.Large.Length,
                        count.Value, diameter, margin, lift, log, verify: false,
                        smallMode: smallMode, allowOverhang: overhangCandidate, minimumClearance: minimumPairClearance);
                    VerifyExact(layout, Small, Large, Rails.Large.Length, diameter, margin, log);
                    if (skipSheetOnGeometryFail && layout.Issue != null)
                    {
                        log(F("OVERHANG_GEOMETRY_REJECT lift_mm={0:F6} reason={1}", lift / Mm, layout.Issue));
                        return layout;
                    }
                }
                else
                {
                    int limit = MaximumCount;
                    while (true)
                    {
                        layout = SolveAutomatic(smallCache, Rails.Small.Length, largeCache, Rails.Large.Length,
                            diameter, margin, lift, log, limit);
                        VerifyExact(layout, Small, Large, Rails.Large.Length, diameter, margin, log);
                        if (layout.Issue == null) break;
                        log(F("AUTO_COUNT exact_rejected={0} reason={1}", layout.Axes.Length, layout.Issue));
                        limit = layout.Axes.Length - 1;
                    }
                }
                clashes = 0;
                uncertain = 0;
                diagnosticsAborted = false;
                double[] estimates = new double[layout.Axes.Length];
                var checks = new SheetCheck[layout.Axes.Length];
                layout.MinimumSheetEstimate = double.PositiveInfinity;
                if (!skipSheetOnGeometryFail)
                {
                    for (int i = 0; i < layout.Axes.Length; i++)
                    {
                        estimates[i] = EstimateFaceClearance(layout.Axes[i], diameter / 2, sign);
                        layout.MinimumSheetEstimate = Math.Min(layout.MinimumSheetEstimate, estimates[i]);
                    }
                }
                for (int k = 0; k < layout.Axes.Length; k++)
                {
                    int i = (startAxis + k) % layout.Axes.Length;
                    SheetCheck check = CheckSheet(layout.Axes[i], diameter / 2, i + 1);
                    checks[i] = check;
                    if (check == SheetCheck.Intersecting) clashes++;
                    if (check == SheetCheck.Inconclusive) uncertain++;
                    if (!skipSheetOnGeometryFail)
                    {
                        log(F("SHEET axis={0} sampled_min_mm={1:F6} brep={2}", i + 1, estimates[i] / Mm, check));
                    }
                    else if (clashes > 0 || uncertain > 0)
                    {
                        double singleEst = EstimateFaceClearance(layout.Axes[i], diameter / 2, sign);
                        layout.MinimumSheetEstimate = singleEst;
                        log(F("SHEET axis={0} sampled_min_mm={1:F6} brep={2}", i + 1, singleEst / Mm, check));
                        startAxis = i;
                        diagnosticsAborted = true;
                        log(F("SHEET_PASS_ABORT pass={0} axis={1} status={2}", pass + 1, i + 1, check));
                        break;
                    }
                }
                if (skipSheetOnGeometryFail && clashes == 0 && uncertain == 0)
                {
                    for (int i = 0; i < layout.Axes.Length; i++)
                    {
                        estimates[i] = EstimateFaceClearance(layout.Axes[i], diameter / 2, sign);
                        layout.MinimumSheetEstimate = Math.Min(layout.MinimumSheetEstimate, estimates[i]);
                        log(F("SHEET axis={0} sampled_min_mm={1:F6} brep={2}", i + 1, estimates[i] / Mm, checks[i]));
                    }
                }
                layout.SheetClashCount = clashes;
                layout.SheetInconclusiveCount = uncertain;
                log(F("LIFT pass={0} offset_mm={1:F6} clashes={2} signed_sample_min_mm={3:F6} inconclusive={4}",
                    pass + 1, lift / Mm, clashes, layout.MinimumSheetEstimate / Mm, uncertain));
                // B-rep is authoritative for extrapolated caps; sampled signed
                // face-distance can be misleading beyond the face's trimmed edge.
                bool sampledOk = overhangCandidate || layout.MinimumSheetEstimate >= -0.02 * Mm;
                if (clashes == 0 && uncertain == 0 && sampledOk)
                {
                    ApplyContextCheck(layout, diameter / 2, checkExternalContext, stopContextOnFirstClash, out bool contextAborted);
                    diagnosticsAborted |= contextAborted;
                    return layout;
                }
                if (flip && ownerBody.GetType() == (int)swBodyType_e.swSolidBody && clashes > 0)
                {
                    // On a solid, the opposite normal points into material. A
                    // flipped solution is allowed only if the body check is clear.
                    flippedSolidClash = true;
                    log("FLIPPED_SOLID: selected side may be inside the sheet material.");
                }
                lift += Math.Max(Math.Max(0.05 * Mm, lift * 0.5), -layout.MinimumSheetEstimate + 0.02 * Mm);
            }
            if (overhangCandidate && layout != null && layout.Issue != null)
                return layout; // Failed geometry proof: no BREP pass can authorize Create.
            string sheetIssue = uncertain > 0
                ? "SolidWorks could not verify all tube-to-sheet checks within the allowed lift."
                : flippedSolidClash
                    ? "The solver could not clear the sheet while staying close to the selected side (the flipped direction points into solid material)."
                    : "The solver could not clear the sheet while staying close to the selected side.";
            AddIssue(layout, sheetIssue);
            return layout;
        }

        private void ApplyContextCheck(TubeLayout layout, double radius, bool checkExternalContext,
            bool stopOnFirstClash, out bool aborted)
        {
            aborted = false;
            if (layout == null || externalContext == null)
            {
                if (layout != null)
                {
                    layout.ContextCheckPerformed = true;
                    layout.ContextBodyCount = 0;
                }
                return;
            }
            layout.ContextBodyCount = externalContext.TotalBodyCount;
            if (!checkExternalContext)
            {
                layout.ContextCheckPerformed = false;
                layout.ContextClashCount = 0;
                layout.ContextInconclusiveCount = externalContext.UnreadableCount;
                log(F("CONTEXT_CHECK skipped=true unchecked_bodies={0} unreadable={1}",
                    externalContext.TotalBodyCount, externalContext.UnreadableCount));
                return;
            }

            ContextCheckResult ctx = externalContext.Check(layout.Axes, radius, toDocument, stopOnFirstClash, log, out aborted);
            layout.ContextCheckPerformed = true;
            layout.ContextBodyCount = ctx.ContextBodyCount;
            layout.ContextClashCount = ctx.CollidingAxisCount;
            layout.ContextInconclusiveCount = ctx.InconclusiveCount;
            layout.ContextClashSummary = ctx.Summary;
            if (ctx.CollidingAxisCount > 0)
                AddIssue(layout, F("3D tubes collide with {0} other assembly/part body check(s) ({1}).",
                    ctx.ClashHitCount, ctx.Summary));
            else if (ctx.InconclusiveCount > 0)
                AddIssue(layout, F("SolidWorks could not verify {0} other assembly/part component check(s) ({1}).",
                    ctx.InconclusiveCount, ctx.Summary));
        }

        private void CompleteFailureDiagnostics(TubeLayout layout, double diameter, double sign, bool checkExternalContext)
        {
            if (layout == null || double.IsNaN(layout.MinimumSheetEstimate)) return;
            int clashes = 0, uncertain = 0;
            for (int i = 0; i < layout.Axes.Length; i++)
            {
                SheetCheck check = CheckSheet(layout.Axes[i], diameter / 2, i + 1);
                if (check == SheetCheck.Intersecting) clashes++;
                if (check == SheetCheck.Inconclusive) uncertain++;
            }
            layout.SheetClashCount = clashes;
            layout.SheetInconclusiveCount = uncertain;
            if (clashes == 0 && uncertain == 0 && externalContext != null && checkExternalContext)
            {
                bool unused;
                ContextCheckResult ctx = externalContext.Check(layout.Axes, diameter / 2, toDocument, false, log, out unused);
                layout.ContextClashCount = ctx.CollidingAxisCount;
                layout.ContextInconclusiveCount = ctx.InconclusiveCount;
                layout.ContextClashSummary = ctx.Summary;
            }
        }

        private Func<double, RailStation> ExtendRail(Func<double, RailStation> interior,
            double length, double maximumCenterExtension, string name)
        {
            double epsilon = Math.Min(0.01 * Mm, length / 100);
            RailStation start = interior(0), end = interior(length);
            Point3 startTangent = (interior(epsilon).Point - start.Point).Unit();
            Point3 endTangent = (end.Point - interior(length - epsilon).Point).Unit();
            return s =>
            {
                if (s >= 0 && s <= length) return interior(s);
                if (s < -maximumCenterExtension - 1e-9 ||
                    s > length + maximumCenterExtension + 1e-9)
                    throw new LayoutException("Overhang station is outside the bounded rail extension.");
                log(F("EXTRAPOLATE_RAIL end={0} station_mm={1:F6} extension_mm={2:F6}",
                    name, s / Mm, (s < 0 ? -s : s - length) / Mm));
                return s < 0
                    ? new RailStation(start.Point + startTangent * s, start.Normal)
                    : new RailStation(end.Point + endTangent * (s - length), end.Normal);
            };
        }

        private Func<double, RailStation> CacheRail(Func<double, RailStation> exact, double length, double diameter, string name)
        {
            // The solver runs entirely on managed data. Only the chosen
            // stations are later evaluated against the exact CAD curves.
            int count = Math.Max(2, (int)Math.Ceiling(length / Math.Min(0.004, diameter / 3)));
            if (count > 5000) throw new LayoutException("The end rail is too long for this tube diameter.");
            var samples = new RailStation[count + 1];
            for (int i = 0; i <= count; i++) samples[i] = exact(length * i / count);
            log(F("RAIL_CACHE end={0} samples={1} step_mm={2:F6}", name, samples.Length, length / count / Mm));
            return station =>
            {
                if (station < 0 || station > length)
                    return exact(station); // Do not clamp a V3 extrapolated station.
                double fraction = Math.Max(0, Math.Min(count, station / length * count));
                int i = Math.Min(count - 1, (int)fraction);
                double t = fraction - i;
                var a = samples[i];
                var b = samples[i + 1];
                return new RailStation(a.Point + (b.Point - a.Point) * t,
                    (a.Normal + (b.Normal - a.Normal) * t).Unit());
            };
        }

        private RailStation Station(Point3 point, double sign, double[] closest = null)
        {
            // XYZ evaluation can fail at a trimmed boundary even when its
            // parent surface is regular at the corresponding UV parameters.
            var xyz = surface.EvaluateAtPoint(point.X, point.Y, point.Z) as double[];
            Point3 normal;
            if (xyz != null && xyz.Length == 11 && TryReadNormal(xyz, 0, out normal))
                return new RailStation(point, normal * sign);

            closest = closest ?? face.GetClosestPointOn(point.X, point.Y, point.Z) as double[];
            if (closest != null && closest.Length == 5 &&
                (Point3.Read(closest) - point).Length <= 0.02 * Mm)
            {
                // Zero derivatives returns [point XYZ, surface normal XYZ].
                var uv = surface.Evaluate(closest[3], closest[4], 0, 0) as double[];
                if (uv != null && uv.Length == 6 &&
                    (Point3.Read(uv) - point).Length <= 0.02 * Mm &&
                    TryReadNormal(uv, 3, out normal))
                {
                    log(F("NORMAL_UV_FALLBACK point_m={0} u={1:R} v={2:R} normal={3}",
                        point, closest[3], closest[4], normal * sign));
                    return new RailStation(point, normal * sign);
                }
            }

            log(F("NORMAL_FAILED point_m={0} xyz_count={1} closest_count={2}",
                point, xyz?.Length ?? 0, closest?.Length ?? 0));
            throw new LayoutException("SolidWorks could not evaluate the selected face normal at a layout point.");
        }

        private static bool TryReadNormal(double[] values, int offset, out Point3 normal)
        {
            normal = new Point3(values[offset], values[offset + 1], values[offset + 2]);
            double length = normal.Length;
            if (!(length > 1e-12) || double.IsInfinity(length)) return false;
            normal = normal * (1 / length);
            return true;
        }

        private double EstimateFaceClearance(TubeAxis axis, double radius, double sign)
        {
            int divisions = Math.Max(16, Math.Min(128, (int)Math.Ceiling(axis.Length / radius)));
            double minimum = double.PositiveInfinity;
            for (int i = 0; i <= divisions; i++)
            {
                Point3 p = axis.A + axis.Direction * (axis.Length * i / divisions);
                var closest = face.GetClosestPointOn(p.X, p.Y, p.Z) as double[];
                Point3 q = Point3.Read(closest);
                Point3 n = Station(q, sign, closest).Normal;
                double along = n.Dot(axis.Direction);
                double radialExtent = radius * Math.Sqrt(Math.Max(0, 1 - along * along));
                minimum = Math.Min(minimum, (p - q).Dot(n) - radialExtent);
            }
            return minimum;
        }

        private SheetCheck CheckSheet(TubeAxis axis, double radius, int index)
        {
            Body2 target = null, tool = null;
            Array result = null;
            try
            {
                // Operations2 consumes its inputs. Only pass bodies owned by this
                // call, never the Part's actual body or a displayed preview body.
                target = ownerBody.GetType() == (int)swBodyType_e.swSolidBody
                    ? ownerBody.ICopy()
                    : modeler.CreateSheetFromFaces(new[] { face });
                var a = axis.A;
                var d = axis.Direction;
                tool = modeler.CreateBodyFromCyl(new[] { a.X, a.Y, a.Z, d.X, d.Y, d.Z, radius, axis.Length }) as Body2;
                if (target == null || tool == null)
                    throw new LayoutException("SolidWorks could not create temporary interference bodies.");
                if (HasFaults(target) || HasFaults(tool))
                    throw new LayoutException("The temporary sheet or tube body has geometric faults.");

                int error;
                result = target.Operations2((int)swBodyOperationType_e.SWBODYINTERSECT, tool, out error) as Array;
                log(F("BREP axis={0} result_count={1} error={2}", index, result?.Length ?? 0, error));
                if (result != null && result.Length > 0) return SheetCheck.Intersecting;
                switch ((swBodyOperationError_e)error)
                {
                    case swBodyOperationError_e.swBodyOperationNoError:
                    case swBodyOperationError_e.swBodyOperationDisjointBodies:
                    case swBodyOperationError_e.swBodyOperationNoIntersect:
                        return SheetCheck.Clear;
                    case swBodyOperationError_e.swBodyOperationUnknownError:
                    case swBodyOperationError_e.swBodyOperationNonManifold:
                    case swBodyOperationError_e.swBodyOperationPartialCoincidence:
                    case swBodyOperationError_e.swBodyOperationBooleanFail:
                        // Near tangency can make the Boolean indeterminate. Retry at
                        // the next lift without treating this attempt as separated.
                        return SheetCheck.Inconclusive;
                    default:
                        throw new LayoutException("Sheet interference check was inconclusive (SolidWorks error " + error + ").");
                }
            }
            finally
            {
                if (result != null)
                    foreach (object body in result) ReleaseTemporary(body);
                ReleaseTemporary(tool);
                ReleaseTemporary(target);
            }
        }

        internal static bool HasFaults(Body2 body)
        {
            FaultEntity faults = body.Check3;
            try { return faults != null && faults.Count > 0; }
            finally { ReleaseTemporary(faults); }
        }

        internal static void ReleaseTemporary(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        private BoundaryPiece ReadPiece(CoEdge coedge, int index)
        {
            Edge edge = coedge.IGetEdge();
            Curve curve = (Curve)edge.GetCurve();
            CurveParamData range = edge.GetCurveParams3();
            bool same = coedge.GetSense();
            Point3 start = Point3.Read(same == range.Sense ? range.StartPoint : range.EndPoint);
            Point3 end = Point3.Read(same == range.Sense ? range.EndPoint : range.StartPoint);
            double a = range.UMinValue, b = range.UMaxValue;
            const double joinTolerance = 0.00002;
            bool TryDomain(Func<double, int, object> eval, double u0, double u1, out bool increasing, string tag)
            {
                Point3 low = Point3.Read(eval(u0, 0)), high = Point3.Read(eval(u1, 0));
                increasing = (low - start).Length + (high - end).Length < (high - start).Length + (low - end).Length;
                log(F("{0} coedge={1} sense={2}/{3} raw_u0={4:R} raw_u1={5:R} tried_u0={6:R} tried_u1={7:R} expected_start_m={8} expected_end_m={9} eval_low_m={10} eval_high_m={11}",
                    tag, index, range.Sense, same, a, b, u0, u1, start, end, low, high));
                return ((increasing ? low : high) - start).Length <= joinTolerance &&
                    ((increasing ? high : low) - end).Length <= joinTolerance && u1 > u0;
            }

            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            bool forward;
            bool useEdgeSpace = false;
            Func<double, int, object> evaluate = (u, d) => curve.Evaluate2(u, d);
            if (!TryDomain(evaluate, lo, hi, out forward, "CURVE_DOMAIN"))
            {
                // Some edge ranges are reported in reversed parameter space.
                // Accept a domain only when its evaluated endpoints agree.
                double opposite = -lo;
                lo = -hi;
                hi = opposite;
                if (!TryDomain(evaluate, lo, hi, out forward, "CURVE_DOMAIN"))
                {
                    // Edge.GetCurveParams3() bounds can belong to Edge parameter space
                    // rather than the underlying untrimmed Curve (e.g. CIRCLE_TYPE/LINE_TYPE).
                    lo = Math.Min(a, b);
                    hi = Math.Max(a, b);
                    useEdgeSpace = true;
                    evaluate = (u, d) => edge.Evaluate2(u, d);
                    if (!TryDomain(evaluate, lo, hi, out forward, "EDGE_DOMAIN_FALLBACK"))
                        throw new LayoutException("Coedge " + index + " does not match its evaluated curve endpoints.");
                }
            }

            const int intervals = 32;
            var distances = new double[intervals + 1];
            for (int i = 1; i <= intervals; i++)
            {
                double uPrev = lo + (hi - lo) * (i - 1) / intervals;
                double uCurr = lo + (hi - lo) * i / intervals;
                distances[i] = useEdgeSpace
                    ? distances[i - 1] + EdgeSegmentLength(edge, uPrev, uCurr, 4)
                    : curve.GetLength3(lo, uCurr);
                if (!(distances[i] > distances[i - 1]))
                    throw new LayoutException("Coedge arc length is not monotone.");
            }
            double length = distances[intervals];
            Point3 t0 = Point3.Read(evaluate(forward ? lo : hi, 1), 3) * (forward ? 1 : -1);
            Point3 t1 = Point3.Read(evaluate(forward ? hi : lo, 1), 3) * (forward ? 1 : -1);
            log(F("COEDGE index={0} type={1} curve_sense={2} coedge_sense={3} forward={4} edge_space={5} u0={6:R} u1={7:R} start_m={8} end_m={9} length_mm={10:F6}",
                index, range.CurveType, range.Sense, same, forward, useEdgeSpace, lo, hi, start, end, length / Mm));

            Point3 At(double station)
            {
                double target = Math.Max(0, Math.Min(length, forward ? station : length - station));
                int pos = Array.BinarySearch(distances, target);
                if (pos >= 0) return Point3.Read(evaluate(lo + (hi - lo) * pos / intervals, 0));
                pos = ~pos;
                if (pos <= 0) return Point3.Read(evaluate(lo, 0));
                if (pos > intervals) return Point3.Read(evaluate(hi, 0));
                double min = lo + (hi - lo) * (pos - 1) / intervals, max = lo + (hi - lo) * pos / intervals;
                double minLength = distances[pos - 1], maxLength = distances[pos];
                for (int i = 0; i < 56; i++)
                {
                    double fraction = (target - minLength) / (maxLength - minLength);
                    double step = i == 0 ? fraction : ((i & 3) == 3 ? 0.5 : Math.Max(0.1, Math.Min(0.9, fraction)));
                    double u = min + (max - min) * step;
                    double l = useEdgeSpace
                        ? minLength + EdgeSegmentLength(edge, min, u, 2)
                        : curve.GetLength3(lo, u);
                    if (Math.Abs(l - target) <= 1e-8) return Point3.Read(evaluate(u, 0));
                    if (l < target) { min = u; minLength = l; } else { max = u; maxLength = l; }
                }
                throw new LayoutException("Arc-length inversion did not converge.");
            }

            return new BoundaryPiece(length, start, end, t0, t1, station =>
            {
                Point3 point = At(station);
                // Curve is acquired from the topology. Keep the owning coedge
                // and edge proxies rooted for the lifetime of the evaluator.
                GC.KeepAlive(edge);
                GC.KeepAlive(coedge);
                return point;
            });
        }

        private static double EdgeSegmentLength(Edge edge, double u0, double u1, int panels)
        {
            if (u1 <= u0) return 0;
            double[] x = { -0.906179845938664, -0.538469310105683, 0.0, 0.538469310105683, 0.906179845938664 };
            double[] w = { 0.236926885056189, 0.478628670499366, 0.568888888888889, 0.478628670499366, 0.236926885056189 };
            double total = 0;
            double h = (u1 - u0) / panels;
            for (int p = 0; p < panels; p++)
            {
                double a = u0 + p * h, mid = a + 0.5 * h, half = 0.5 * h;
                for (int k = 0; k < 5; k++)
                {
                    var d = Point3.Read(edge.Evaluate2(mid + half * x[k], 1), 3);
                    total += w[k] * half * d.Length;
                }
            }
            return total;
        }
    }

    internal sealed class ContextCheckResult
    {
        public int ContextBodyCount;
        public int CollidingAxisCount;
        public int ClashHitCount;
        public int InconclusiveCount;
        public string Summary;
    }

    internal sealed class ExternalBodyContext : IDisposable
    {
        private struct Entry
        {
            public readonly string Name;
            public readonly Body2 Body;
            public readonly double[] Box;
            public Entry(string name, Body2 body, double[] box) { Name = name; Body = body; Box = box; }
        }

        private readonly Modeler modeler;
        private readonly List<Entry> entries;
        private readonly string unreadableSummary;
        public int TotalBodyCount => entries.Count;
        public readonly int UnreadableCount;

        private ExternalBodyContext(Modeler modeler, List<Entry> entries, int unreadableCount, string unreadableSummary)
        {
            this.modeler = modeler;
            this.entries = entries;
            UnreadableCount = unreadableCount;
            this.unreadableSummary = unreadableSummary;
        }

        public static ExternalBodyContext Create(ISldWorks app, ModelDoc2 model, Component2 selectedComponent,
            Face2 partFace, Modeler modeler, Action<string> log)
        {
            var entries = new List<Entry>();
            var unreadableNames = new List<string>();
            int unreadable = 0;
            Body2 ownerPartBody = partFace?.IGetBody();

            try
            {
                bool solidOwner = ownerPartBody != null && ownerPartBody.GetType() == (int)swBodyType_e.swSolidBody;
                if (model != null && model.GetType() == (int)swDocumentTypes_e.swDocPART)
                {
                    var bodies = ((PartDoc)model).GetBodies2((int)swBodyType_e.swAllBodies, true) as object[] ?? new object[0];
                    int index = 0;
                    foreach (Body2 b in bodies.OfType<Body2>())
                    {
                        index++;
                        if (ownerPartBody != null && app.IsSame(b, ownerPartBody) == (int)swObjectEquality.swObjectSame) continue;
                        if (solidOwner && b.GetType() == (int)swBodyType_e.swSheetBody)
                        {
                            // A solid Part's own visible surface bodies (e.g. extended construction guides)
                            // are modeling helpers rather than separate solid obstacles.
                            log?.Invoke("CONTEXT_SURFACE_EXCLUDED name=" + (b.Name ?? "") + " reason=construction_surface_in_solid_part");
                            continue;
                        }
                        Body2 copy = b.ICopy();
                        if (copy == null)
                        {
                            unreadable++;
                            unreadableNames.Add(string.IsNullOrEmpty(b.Name) ? "PartBody#" + index : b.Name);
                            continue;
                        }
                        entries.Add(new Entry(string.IsNullOrEmpty(b.Name) ? "PartBody#" + index : b.Name, copy, copy.GetBodyBox() as double[]));
                    }
                }
                else if (model is AssemblyDoc assembly)
                {
                    string ownerBodyName = ownerPartBody?.Name ?? "";
                    int selectedPartSolidCount = 1;
                    if (selectedComponent?.GetModelDoc2() is PartDoc selectedPart)
                    {
                        var partSolids = selectedPart.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                        selectedPartSolidCount = partSolids?.Length ?? 1;
                    }

                    var comps = (assembly.GetComponents(false) as object[] ?? new object[0]).OfType<Component2>();
                    foreach (Component2 comp in comps)
                    {
                        if (!IsVisibleAndUnsuppressed(comp)) continue;
                        int supp = comp.GetSuppression2();
                        if (supp == (int)swComponentSuppressionState_e.swComponentLightweight ||
                            supp == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                        {
                            unreadable++;
                            if (unreadableNames.Count < 4) unreadableNames.Add(comp.Name2 + " (lightweight)");
                            log?.Invoke("CONTEXT_COMPONENT_UNREADABLE name=" + comp.Name2 + " reason=lightweight");
                            continue;
                        }

                        var compDoc = comp.GetModelDoc2() as ModelDoc2;
                        if (compDoc != null && compDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY) continue;
                        if (compDoc == null)
                        {
                            unreadable++;
                            if (unreadableNames.Count < 4) unreadableNames.Add(comp.Name2 + " (unresolved)");
                            log?.Invoke("CONTEXT_COMPONENT_UNREADABLE name=" + comp.Name2 + " reason=unresolved");
                            continue;
                        }

                        var bodies = comp.GetBodies2((int)swBodyType_e.swAllBodies) as object[];
                        if (bodies == null) continue;
                        bool isSelectedComp = selectedComponent != null &&
                            app.IsSame(comp, selectedComponent) == (int)swObjectEquality.swObjectSame;
                        MathTransform xf = comp.Transform2;
                        double[] xfData = xf?.ArrayData as double[];
                        bool validXf = xf == null || (xfData != null && xfData.Length == 16 && Math.Abs(Math.Abs(xfData[12]) - 1) <= 1e-9);
                        if (!validXf)
                        {
                            unreadable++;
                            if (unreadableNames.Count < 4) unreadableNames.Add(comp.Name2 + " (non-rigid transform)");
                            log?.Invoke("CONTEXT_COMPONENT_UNREADABLE name=" + comp.Name2 + " reason=non_rigid_transform");
                            continue;
                        }

                        foreach (Body2 ob in bodies.OfType<Body2>())
                        {
                            if (isSelectedComp)
                            {
                                if (solidOwner && ob.GetType() == (int)swBodyType_e.swSheetBody)
                                {
                                    log?.Invoke("CONTEXT_SURFACE_EXCLUDED name=" + (ob.Name ?? "") + " comp=" + comp.Name2);
                                    continue;
                                }
                                if (selectedPartSolidCount <= 1 || string.Equals(ob.Name, ownerBodyName, StringComparison.Ordinal))
                                    continue;
                            }
                            Body2 copy = (Body2)ob.Copy();
                            if (copy == null)
                            {
                                unreadable++;
                                if (unreadableNames.Count < 4) unreadableNames.Add(comp.Name2);
                                continue;
                            }
                            if (xf != null) copy.ApplyTransform(xf);
                            entries.Add(new Entry(comp.Name2, copy, copy.GetBodyBox() as double[]));
                        }
                    }
                }
            }
            catch
            {
                foreach (Entry e in entries) SwFaceLayout.ReleaseTemporary(e.Body);
                throw;
            }

            string summary = unreadableNames.Count > 0 ? string.Join(", ", unreadableNames) : null;
            log?.Invoke(F("CONTEXT_INIT bodies={0} unreadable={1}", entries.Count, unreadable));
            return new ExternalBodyContext(modeler, entries, unreadable, summary);
        }

        public ContextCheckResult Check(TubeAxis[] localAxes, double radius, Func<Point3, Point3> toDocument,
            bool stopOnFirstClash, Action<string> log, out bool aborted)
        {
            aborted = false;
            var res = new ContextCheckResult
            {
                ContextBodyCount = entries.Count,
                InconclusiveCount = UnreadableCount,
                Summary = unreadableSummary
            };
            if (entries.Count == 0 || localAxes == null || localAxes.Length == 0)
                return res;

            var hitNames = new List<string>();
            for (int i = 0; i < localAxes.Length; i++)
            {
                Point3 a = toDocument(localAxes[i].A), b = toDocument(localAxes[i].B);
                var docAxis = new TubeAxis(a, b);
                double[] abox = AxisBox(docAxis, radius + 0.0005);
                bool anyOverlap = false;
                for (int k = 0; k < entries.Count; k++)
                {
                    if (BoxesOverlap(abox, entries[k].Box))
                    {
                        anyOverlap = true;
                        break;
                    }
                }
                if (!anyOverlap) continue;

                Body2 cyl = null;
                bool axisHit = false;
                try
                {
                    Point3 d = docAxis.Direction;
                    cyl = modeler.CreateBodyFromCyl(new[] { a.X, a.Y, a.Z, d.X, d.Y, d.Z, radius, docAxis.Length }) as Body2;
                    if (cyl == null || SwFaceLayout.HasFaults(cyl))
                    {
                        res.InconclusiveCount++;
                        log?.Invoke(F("ASSEMBLY_BREP_INCONCLUSIVE axis={0} reason=cylinder_fault", i + 1));
                        continue;
                    }

                    for (int k = 0; k < entries.Count; k++)
                    {
                        Entry entry = entries[k];
                        if (!BoxesOverlap(abox, entry.Box)) continue;
                        Body2 cylCopy = null, targetCopy = null;
                        Array hits = null;
                        try
                        {
                            cylCopy = (Body2)cyl.Copy();
                            targetCopy = (Body2)entry.Body.Copy();
                            if (cylCopy == null || targetCopy == null)
                            {
                                res.InconclusiveCount++;
                                continue;
                            }
                            int error;
                            hits = targetCopy.Operations2((int)swBodyOperationType_e.SWBODYINTERSECT, cylCopy, out error) as Array;
                            if (hits != null && hits.Length > 0)
                            {
                                if (!axisHit)
                                {
                                    axisHit = true;
                                    res.CollidingAxisCount++;
                                }
                                res.ClashHitCount++;
                                log?.Invoke(F("ASSEMBLY_BREP axis={0} comp={1} pieces={2} error={3}",
                                    i + 1, entry.Name, hits.Length, error));
                                if (hitNames.Count < 4)
                                {
                                    string tag = F("axis {0} vs {1}", i + 1, entry.Name);
                                    if (!hitNames.Contains(tag)) hitNames.Add(tag);
                                }
                                if (stopOnFirstClash)
                                {
                                    aborted = true;
                                    res.Summary = string.Join("; ", hitNames);
                                    return res;
                                }
                            }
                            else
                            {
                                switch ((swBodyOperationError_e)error)
                                {
                                    case swBodyOperationError_e.swBodyOperationNoError:
                                    case swBodyOperationError_e.swBodyOperationDisjointBodies:
                                    case swBodyOperationError_e.swBodyOperationNoIntersect:
                                        break;
                                    default:
                                        res.InconclusiveCount++;
                                        log?.Invoke(F("ASSEMBLY_BREP_INCONCLUSIVE axis={0} comp={1} error={2}",
                                            i + 1, entry.Name, error));
                                        break;
                                }
                            }
                        }
                        finally
                        {
                            if (hits != null)
                                foreach (object item in hits) SwFaceLayout.ReleaseTemporary(item);
                            SwFaceLayout.ReleaseTemporary(cylCopy);
                            SwFaceLayout.ReleaseTemporary(targetCopy);
                        }
                    }
                }
                finally
                {
                    SwFaceLayout.ReleaseTemporary(cyl);
                }
            }

            if (hitNames.Count > 0) res.Summary = string.Join("; ", hitNames);
            return res;
        }

        public void Dispose()
        {
            foreach (Entry entry in entries) SwFaceLayout.ReleaseTemporary(entry.Body);
            entries.Clear();
        }

        private static bool IsVisibleAndUnsuppressed(Component2 comp)
        {
            for (Component2 cur = comp; cur != null; cur = cur.GetParent())
            {
                if (cur.IsSuppressed()) return false;
                if (cur.Visible != (int)swComponentVisibilityState_e.swComponentVisible) return false;
            }
            return true;
        }

        private static double[] AxisBox(TubeAxis ax, double pad) => new[]
        {
            Math.Min(ax.A.X, ax.B.X) - pad, Math.Min(ax.A.Y, ax.B.Y) - pad, Math.Min(ax.A.Z, ax.B.Z) - pad,
            Math.Max(ax.A.X, ax.B.X) + pad, Math.Max(ax.A.Y, ax.B.Y) + pad, Math.Max(ax.A.Z, ax.B.Z) + pad
        };

        private static bool BoxesOverlap(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != 6 || b.Length != 6) return true;
            return a[0] <= b[3] && a[3] >= b[0] &&
                   a[1] <= b[4] && a[4] >= b[1] &&
                   a[2] <= b[5] && a[5] >= b[2];
        }
    }
}
