// CHECK HOLE CG2.5 - REAL CAD POSITIVE CONTROLS + TEMPORARY 3D EVIDENCE PREVIEW.
// C# 7.3, .NET Framework 4.8, SOLIDWORKS 2024.
// DIAGNOSTIC ONLY. NO production NG, CSV, stack/pattern/marker or model edits.
// Positive control: a hole already recognized by the production reader in the
// current Assembly. This is NOT proof the reader finds every hole or slot.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        private const int Cg25MaxControls = 12;
        private const int Cg25MaxControlsPerOccurrence = 2;
        private const int Cg25ControlBudgetMs = 8000;
        private const double Cg25ControlHalfWindowM = 0.010; // 10 mm, diagnostics only
        private const double Cg25MarkerSideM = 0.0020; // 2.0 mm temporary visual cube

        // FIX2: one registered view, immutable Assembly-space points.
        // No temporary Body2/Component2/ModelDoc COM objects are retained.
        private ModelView cg25GlView;
        private double[] cg25GlEntry;
        private double[] cg25GlExit;
        private volatile bool cg25GlEnabled;
        private int cg25GlFrames;
        private int cg25GlCandidateId;

        private sealed class Cg25ControlResult
        {
            public string Occurrence;
            public string State;
            public string Reason;
            public int InnerCircle;
            public int InnerComplex;
            public int Outer;
            public int Faces;
            public int Edges;
        }

        // Run AFTER CG2.4. Does not change report rows or production state.
        private void Cg25RunRealCadControls(AssemblyDoc assembly,
            List<HoleAxis> holes, CheckHoleCg2Report report)
        {
            if (assembly == null || holes == null || report == null) return;
            Stopwatch timer = Stopwatch.StartNew();
            int attempted = 0, detected = 0, noInner = 0, unverified = 0;
            var parts = new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (object item in ToObjectArray(assembly.GetComponents(false)))
                {
                    Component2 component = item as Component2;
                    if (!IsUsableComponent(component)) continue;
                    bool subassembly;
                    if (!Cg2IsResolvedPartOccurrence(component, out subassembly)) continue;
                    string name = SafeComponentName(component);
                    if (!string.IsNullOrWhiteSpace(name) && !parts.ContainsKey(name))
                        parts.Add(name, component);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CG25][ERROR] Component index " + ex.GetType().Name);
                return;
            }
            // Deliberately include recognized holes from the target occurrences
            // of the 16 BREP-positive candidates, then other recognized holes.
            var prioritizedTargets = new HashSet<string>(
                report.ExpectedHoleCandidates.Where(r => r != null &&
                    r.MaterialEvidence == "BREP_SOLID_ON_AXIS")
                    .Select(r => r.TargetOccurrence ?? ""),
                StringComparer.OrdinalIgnoreCase);
            var seenPerOccurrence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var contexts = new Dictionary<string, Cg2TargetBodyContext>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<HoleAxis> ordered = holes.Where(h => h != null &&
                    !string.IsNullOrWhiteSpace(h.ComponentOccurrence) &&
                    Cg23Finite3(h.Center) && Cg23Unit(h.Direction) != null)
                .OrderBy(h => prioritizedTargets.Contains(h.ComponentOccurrence) ? 0 : 1)
                .ThenBy(h => h.ComponentOccurrence, StringComparer.OrdinalIgnoreCase);
            foreach (HoleAxis h in ordered)
            {
                if (attempted >= Cg25MaxControls || timer.ElapsedMilliseconds >= Cg25ControlBudgetMs)
                    break;
                ThrowIfCanceled();
                string key = h.ComponentOccurrence;
                int prior;
                seenPerOccurrence.TryGetValue(key, out prior);
                if (prior >= Cg25MaxControlsPerOccurrence) continue;
                Component2 component;
                if (!parts.TryGetValue(key, out component)) continue;
                seenPerOccurrence[key] = prior + 1;
                attempted++;
                var result = new Cg25ControlResult { Occurrence = key,
                    State = "UNVERIFIED", Reason = "No completed topology scan" };
                try
                {
                    Cg2TargetBodyContext ctx;
                    if (!contexts.TryGetValue(key, out ctx))
                    {
                        ctx = Cg2LoadTargetBodyContext(component);
                        contexts.Add(key, ctx);
                    }
                    if (ctx == null || !string.IsNullOrEmpty(ctx.Failure) ||
                        ctx.Bodies == null || ctx.FromWorldToPart == null)
                    {
                        result.Reason = ctx == null ? "No BREP context" :
                            (ctx.Failure ?? "Unresolved context");
                    }
                    else
                    {
                        double[] unit = Cg23Unit(h.Direction);
                        double[] startWorld = Cg23Sub(h.Center,
                            Cg23Scale(unit, Cg25ControlHalfWindowM));
                        double[] endWorld = Cg23Add(h.Center,
                            Cg23Scale(unit, Cg25ControlHalfWindowM));
                        double[] entry = TransformPoint(startWorld, ctx.FromWorldToPart);
                        double[] exit = TransformPoint(endWorld, ctx.FromWorldToPart);
                        if (!Cg23Finite3(entry) || !Cg23Finite3(exit))
                        {
                            result.Reason = "Invalid local control coordinates";
                        }
                        else
                        {
                            Cg24ScanResult scan = Cg24ScanBodyLoops(ctx.Bodies,
                                entry, exit, timer);
                            result.InnerCircle = scan.InnerCircle;
                            result.InnerComplex = scan.InnerComplex;
                            result.Outer = scan.Outer;
                            result.Faces = scan.Faces;
                            result.Edges = scan.EdgeSamples;
                            if (scan.State != null &&
                                scan.State.StartsWith("UNVERIFIED", StringComparison.Ordinal))
                            {
                                result.State = "UNVERIFIED_PARTIAL";
                                result.Reason = scan.State;
                            }
                            else if (scan.InnerCircle + scan.InnerComplex > 0)
                            {
                                result.State = "POSITIVE_INNER_BOUNDARY_FOUND";
                                result.Reason = "Production-detected hole; near inner BREP loop.";
                            }
                            else
                            {
                                result.State = "POSITIVE_CONTROL_INNER_BOUNDARY_NOT_FOUND";
                                result.Reason = "Known hole but CG2.4 found no inner loop within its window; " +
                                    "test is inconclusive, NOT a missing-hole verdict.";
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result.State = "UNVERIFIED_COM_ERROR";
                    result.Reason = ex.GetType().Name;
                }
                if (result.State == "POSITIVE_INNER_BOUNDARY_FOUND") detected++;
                else if (result.State == "POSITIVE_CONTROL_INNER_BOUNDARY_NOT_FOUND") noInner++;
                else unverified++;
                Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "[CG25][POSITIVE] Occurrence={0} State={1} " +
                    "InnerCircular={2} InnerComplex={3} Outer={4} " +
                    "Faces={5} Edges={6} Reason={7}",
                    result.Occurrence, result.State, result.InnerCircle,
                    result.InnerComplex, result.Outer, result.Faces,
                    result.Edges, result.Reason));
            }
            Debug.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "[CG25][SUMMARY] KnownHoleControls={0} InnerFound={1} " +
                "InnerNotFound={2} Unverified={3} ElapsedMs={4} " +
                "ProductionNgChanged=NO",
                attempted, detected, noInner, unverified, timer.ElapsedMilliseconds));
            // A positive control cannot establish a true missing-hole or mate
            // requirement. Slot/Cut-only geometry needs a known CAD fixture.
        }

        // This is called ONLY when the user clicks 'Xem Entry/Exit 3D' in CG2.
        // The model must be the one used for the current report.
        // CG2.5 FIX1: Display temp bodies in the target COMPONENT context.
        // The BREP coordinates are in assembly space, while Display3(Component2)
        // expects the temporary body's coordinates in that component's part space.
        // CG2.5 FIX2 - OpenGL overlay on the TOP LEVEL assembly ModelView.
        // No temporary BREP bodies, no Feature, no Sketch, no model changes.
        // All coordinates here are WORLD/ASSEMBLY coordinates in meters.
        private string Cg25ShowCandidatePreview(int candidateId)
        {
            System.Diagnostics.Debug.WriteLine("[CG25][FIX2_START] ID=" + candidateId);
            try
            {
                if (running || lastReport == null || lastReport.PartRelations == null)
                    return Cg25PreviewFailure(candidateId, "NO_REPORT", "Chưa có báo cáo CG2 hợp lệ.");

                ModelDoc2 model = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
                if (model == null || reportModel == null)
                    return Cg25PreviewFailure(candidateId, "NO_ACTIVE_MODEL", "Không có Assembly đang hoạt động.");
                string activePath = model.GetPathName() ?? "";
                string reportPath = reportModel.GetPathName() ?? "";
                if (!string.Equals(activePath, reportPath, StringComparison.OrdinalIgnoreCase) ||
                    (activePath.Length == 0 &&
                     !string.Equals(model.GetTitle(), reportModel.GetTitle(),
                         StringComparison.OrdinalIgnoreCase)))
                    return Cg25PreviewFailure(candidateId, "STALE_REPORT",
                        "Assembly hiện tại không khớp báo cáo. Chạy Check Hole lại.");
                if (!(model is AssemblyDoc))
                    return Cg25PreviewFailure(candidateId, "NOT_ASSEMBLY", "Chỉ dùng Preview trên Assembly.");

                CheckHoleCg2ExpectedHoleRow row = lastReport.PartRelations.ExpectedHoleCandidates
                    .FirstOrDefault(x => x != null && x.Id == candidateId);
                if (row == null)
                    return Cg25PreviewFailure(candidateId, "NO_CANDIDATE", "Không tìm thấy Candidate.");
                if (row.MaterialEvidence != "BREP_SOLID_ON_AXIS" ||
                    !Cg23Finite3(row.Cg23EntryWorldM) || !Cg23Finite3(row.Cg23ExitWorldM))
                    return Cg25PreviewFailure(candidateId, "NO_ENTRY_EXIT",
                        "Candidate chưa có Entry/Exit hợp lệ.");

                // Important: remove any previous render-event connection BEFORE reattaching.
                Cg25ClearCandidatePreview();

                // An OpenGL BufferSwapNotify view uses the TOP LEVEL model's axes.
                // Do NOT transform the points to target Component2 local axes.
                ModelView view = model.IActiveView;
                if (view == null)
                    return Cg25PreviewFailure(candidateId, "NO_VIEW", "Không tìm thấy viewport 3D.");

                cg25GlEntry = (double[])row.Cg23EntryWorldM.Clone();
                cg25GlExit = (double[])row.Cg23ExitWorldM.Clone();
                cg25GlView = view;
                cg25GlFrames = 0;
                cg25GlCandidateId = candidateId;
                cg25GlEnabled = false;

                // Event callbacks must not call SOLIDWORKS COM methods; they only
                // use world-space coordinates and OpenGL with an existing context.
                view.BufferSwapNotify += Cg25GlBufferSwap;
                view.DestroyNotify2 += Cg25GlViewDestroy;
                cg25GlEnabled = true;

                System.Diagnostics.Debug.WriteLine("[CG25][FIX2_ATTACHED] ID=" + candidateId +
                    " EntryWorldMm=" + Cg25PointMm(cg25GlEntry) +
                    " ExitWorldMm=" + Cg25PointMm(cg25GlExit));
                // Request a normal graphics repaint, NOT ForceRebuild or model edit.
                model.GraphicsRedraw2();
                int frameCount = System.Threading.Interlocked.CompareExchange(ref cg25GlFrames, 0, 0);
                System.Diagnostics.Debug.WriteLine("[CG25][FIX2_RENDER_CHECK] ID=" + candidateId +
                    " RenderCallbacks=" + frameCount + " (0 means not yet rendered/unsupported graphics mode)");
                return frameCount > 0
                    ? "OpenGL Preview đã vẽ " + frameCount + " frame. Xanh=Entry, Cam=Exit. " +
                        "Xoay/zoom để kiểm tra vị trí."
                    : "Đã gắn Preview OpenGL, nhưng chưa nhận BufferSwapNotify. " +
                        "Hãy chuyển qua Assembly và xoay/zoom; nếu vẫn không thấy, gửi log FIX2.";
            }
            catch (Exception ex)
            {
                Cg25ClearCandidatePreview();
                System.Diagnostics.Debug.WriteLine("[CG25][FIX2_ERROR] ID=" + candidateId + " " + ex);
                return "Preview FIX2 lỗi: " + ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static string Cg25PreviewFailure(int id, string code, string message)
        {
            System.Diagnostics.Debug.WriteLine("[CG25][FIX2_FAIL] ID=" + id + " Code=" + code);
            return message + " [" + code + "]";
        }

        private static string Cg25PointMm(double[] xyz)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "({0:F3},{1:F3},{2:F3})", xyz[0] * 1000,
                xyz[1] * 1000, xyz[2] * 1000);
        }

        // Gently detach managed event handlers while a viewport is alive.
        // Never call Hide or Marshal.FinalReleaseComObject on SOLIDWORKS COM objects.
        private void Cg25ClearCandidatePreview()
        {
            cg25GlEnabled = false;
            ModelView previous = cg25GlView;
            cg25GlView = null;
            cg25GlEntry = null;
            cg25GlExit = null;
            cg25GlCandidateId = 0;
            if (previous != null)
            {
                try { previous.BufferSwapNotify -= Cg25GlBufferSwap; }
                catch (Exception e) { Debug.WriteLine("[CG25][FIX2_DETACH_BUFFER] " + e.GetType().Name); }
                try { previous.DestroyNotify2 -= Cg25GlViewDestroy; }
                catch (Exception e) { Debug.WriteLine("[CG25][FIX2_DETACH_DESTROY] " + e.GetType().Name); }
            }
            Debug.WriteLine("[CG25][FIX2_CLEAR] OverlayDetached=True");
        }

        // Called before the view is destroyed or hidden. Never access COM here.
        // The view itself is about to disappear; do NOT unsubscribe while SOLIDWORKS
        // is tearing it down. Managed references are dropped; the old event source dies.
        private int Cg25GlViewDestroy(int destroyType)
        {
            cg25GlEnabled = false;
            cg25GlView = null;
            cg25GlEntry = null;
            cg25GlExit = null;
            cg25GlCandidateId = 0;
            Debug.WriteLine("[CG25][FIX2_VIEW_DESTROY] DestroyType=" + destroyType);
            return 0;
        }

        private int Cg25GlBufferSwap()
        {
            if (!cg25GlEnabled) return 0;
            double[] entry = cg25GlEntry;
            double[] exit = cg25GlExit;
            if (entry == null || exit == null) return 0;

            // Only draw when SOLIDWORKS actually has an OpenGL rendering context.
            // Some Enhanced Graphics / DirectX modes may not deliver this callback.
            if (Cg25WglGetCurrentContext() == IntPtr.Zero) return 0;
            bool pushed = false;
            bool begun = false;
            try
            {
                Cg25GlPushAttrib(0x00002000u | 0x00000100u | 0x00000004u | 0x00000001u);
                pushed = true;
                Cg25GlDisable(0x0B71u); // GL_DEPTH_TEST: show markers above solid faces
                Cg25GlDisable(0x0B50u); // GL_LIGHTING
                Cg25GlDisable(0x0B44u); // GL_CULL_FACE
                Cg25GlLineWidth(4.0f);
                Cg25GlBegin(0x0001u); // GL_LINES
                begun = true;

                // Green marker is larger than orange, so two points 2 mm apart
                // remain distinguishable even in a face-normal view.
                Cg25GlColor3f(0.2f, 1.0f, 0.2f);
                Cg25GlCross(entry, 0.0070);
                Cg25GlColor3f(1.0f, 0.35f, 0.05f);
                Cg25GlCross(exit, 0.0040);
                // Connecting line, white, along the observed material span.
                Cg25GlColor3f(1.0f, 1.0f, 1.0f);
                Cg25GlVertex3d(entry[0], entry[1], entry[2]);
                Cg25GlVertex3d(exit[0], exit[1], exit[2]);
                Cg25GlEnd();
                begun = false;
                int rendered = System.Threading.Interlocked.Increment(ref cg25GlFrames);
                if (rendered == 1)
                    Debug.WriteLine("[CG25][FIX2_FIRST_FRAME] ID=" + cg25GlCandidateId);
            }
            catch (Exception ex)
            {
                cg25GlEnabled = false;
                Debug.WriteLine("[CG25][FIX2_RENDER_ERROR] " + ex.GetType().Name);
            }
            finally
            {
                if (begun) try { Cg25GlEnd(); } catch { }
                if (pushed) try { Cg25GlPopAttrib(); } catch { }
            }
            return 0;
        }

        private static void Cg25GlCross(double[] p, double r)
        {
            // World-space 3D cross with a fixed size in meters.
            // Called inside a matching glBegin/glEnd pair ONLY.
            Cg25GlVertex3d(p[0] - r, p[1], p[2]);
            Cg25GlVertex3d(p[0] + r, p[1], p[2]);
            Cg25GlVertex3d(p[0], p[1] - r, p[2]);
            Cg25GlVertex3d(p[0], p[1] + r, p[2]);
            Cg25GlVertex3d(p[0], p[1], p[2] - r);
            Cg25GlVertex3d(p[0], p[1], p[2] + r);
        }

        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "wglGetCurrentContext")]
        private static extern IntPtr Cg25WglGetCurrentContext();
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glPushAttrib")]
        private static extern void Cg25GlPushAttrib(uint mask);
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glPopAttrib")]
        private static extern void Cg25GlPopAttrib();
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glDisable")]
        private static extern void Cg25GlDisable(uint cap);
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glLineWidth")]
        private static extern void Cg25GlLineWidth(float width);
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glColor3f")]
        private static extern void Cg25GlColor3f(float r, float g, float b);
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glBegin")]
        private static extern void Cg25GlBegin(uint mode);
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glEnd")]
        private static extern void Cg25GlEnd();
        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glVertex3d")]
        private static extern void Cg25GlVertex3d(double x, double y, double z);
    }
}
