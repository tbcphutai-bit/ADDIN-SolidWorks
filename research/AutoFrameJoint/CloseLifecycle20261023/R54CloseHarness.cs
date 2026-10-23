using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Web.Script.Serialization;
using ADDIN.UI.AutoFrameJoint;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Standalone managed contract harness. Compile together with exactly one UI source.
// Usage: R54CloseHarness.exe <results.json>; define OLD_REPRO for the old UI source.
// No Show, window, dispatcher, message pump, SOLIDWORKS process or COM activation.
internal static class R54CloseHarness
{
    private static readonly List<object> Results = new List<object>();
    private static int failures;
    private static int forbiddenInterfaceCalls;

    private sealed class ForbiddenComponentProxy : RealProxy
    {
        internal ForbiddenComponentProxy() : base(typeof(Component2)) { }

        public override IMessage Invoke(IMessage message)
        {
            forbiddenInterfaceCalls++;
            IMethodCallMessage call = (IMethodCallMessage)message;
            return new ReturnMessage(new InvalidOperationException(
                "FORBIDDEN_INTERFACE_CALL: " + call.MethodName), call);
        }
    }

    private sealed class Fixture
    {
        internal readonly AutoFrameJointSelectionPage Page;
        internal int ApplyCalls, CleanupCalls, ClosedCalls;
        internal Component2 A, B;
        internal readonly List<string> Events = new List<string>();

        internal Fixture()
        {
            Page = new AutoFrameJointSelectionPage(null,
                (a, b) => { throw new InvalidOperationException("Unexpected Analyze"); },
                (a, b) => { throw new InvalidOperationException("Unexpected ShowBodies"); },
                (a, b, candidate) => { throw new InvalidOperationException("Unexpected Preview"); },
                () => { CleanupCalls++; Events.Add("cleanup"); },
                p => { Assert(Object.ReferenceEquals(Page, p), "Wrong closed page"); ClosedCalls++; Events.Add("closed"); },
                (a, b) =>
                {
                    ApplyCalls++;
                    Events.Add("apply");
                    Assert(Object.ReferenceEquals(A, a) && Object.ReferenceEquals(B, b), "Wrong Apply snapshot");
                    Assert(Get<bool>(Page, "closing") && !Get<bool>(Page, "shown"), "Apply before page teardown");
                    Assert(Get<object>(Page, "page") == null, "Apply retained native page");
                    return "MANAGED_CALLBACK_ONLY";
                });
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static FieldInfo PrivateField(string name)
    {
        FieldInfo field = typeof(AutoFrameJointSelectionPage).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing private field: " + name);
        return field;
    }

    private static void Set(AutoFrameJointSelectionPage page, string name, object value)
    {
        PrivateField(name).SetValue(page, value);
    }

    private static T Get<T>(AutoFrameJointSelectionPage page, string name)
    {
        return (T)PrivateField(name).GetValue(page);
    }

    private static void Run(string name, Action test)
    {
        int callsBefore = forbiddenInterfaceCalls;
        try
        {
            test();
            Assert(forbiddenInterfaceCalls == callsBefore, "Native interface proxy was invoked");
            Results.Add(new { test = name, status = "PASS_MANAGED_CONTRACT", interface_calls = forbiddenInterfaceCalls - callsBefore });
            Console.WriteLine(name + " PASS_MANAGED_CONTRACT");
        }
        catch (Exception ex)
        {
            failures++;
            Results.Add(new { test = name, status = "FAIL", error_type = ex.GetType().Name,
                error = ex.Message, interface_calls = forbiddenInterfaceCalls - callsBefore });
            Console.WriteLine(name + " FAIL " + ex.Message);
        }
    }

#if OLD_REPRO
    private static void RunOldReproduction()
    {
        Run("OLD_A_ONLY_OK_REPRODUCES_HRESULT_1_EXCEPTION", () =>
        {
            Fixture f = new Fixture();
            // No fake components and no R54 fields: this is the logged stale-revision trigger.
            Set(f.Page, "selectionRevision", 2);
            Set(f.Page, "committedSelectionRevision", 0);
            COMException observed = null;
            try { f.Page.OnClose((int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay); }
            catch (COMException ex) { observed = ex; }
            Assert(observed != null, "Old OnClose did not reproduce the COMException");
            Assert(observed.HResult == 1, "Old exception HRESULT differs from 1");
            Assert(observed.Message == "Auto Frame Joint is finishing the current Preview", "Wrong old exception");
            Assert(f.ApplyCalls == 0 && f.CleanupCalls == 0 && f.ClosedCalls == 0, "Old repro invoked delegates");
            Results.Add(new { evidence = "OLD_THROW_OBSERVED", hresult = observed.HResult, message = observed.Message });
        });
    }
#else
    private static Component2 FakeComponent()
    {
        return (Component2)new ForbiddenComponentProxy().GetTransparentProxy();
    }

    private static Fixture Prepared(bool withA, bool withB, bool validated, int revision, int committed)
    {
        Fixture f = new Fixture();
        f.A = withA ? FakeComponent() : null;
        f.B = withB ? FakeComponent() : null;
        Set(f.Page, "committedA", f.A);
        Set(f.Page, "committedB", f.B);
        Set(f.Page, "selectionRevision", revision);
        Set(f.Page, "committedSelectionRevision", committed);
        Set(f.Page, "observedSelectionRevision", revision);
        Set(f.Page, "currentPairValidated", validated);
        Set(f.Page, "pageEpoch", 17);
        Set(f.Page, "shown", true);
        Set(f.Page, "nativeActivated", true);
        Set(f.Page, "finishRequested", true);
        Set(f.Page, "finishQueued", true);
        Set(f.Page, "requestAutoAnalysis", true);
        Set(f.Page, "requestFocusB", true);
        return f;
    }

    private static Fixture ValidPair()
    {
        return Prepared(true, true, true, 4, 4);
    }

    private static void CheckClose(Fixture f, int reason, bool eligible, string skip)
    {
        int before = forbiddenInterfaceCalls;
        f.Page.OnClose(reason);
        Assert(forbiddenInterfaceCalls == before, "OnClose read a component interface");
        Assert(Get<bool>(f.Page, "closing"), "OnClose did not mark closing");
        Assert(!Get<bool>(f.Page, "nativeActivated"), "OnClose retained activation");
        Assert(Get<int>(f.Page, "pageEpoch") == 18, "OnClose did not invalidate epoch exactly once");
        Assert(!Get<bool>(f.Page, "finishRequested") && !Get<bool>(f.Page, "finishQueued"), "Finish state was retained");
        Assert(!Get<bool>(f.Page, "requestAutoAnalysis") && !Get<bool>(f.Page, "requestFocusB"), "Queued intent was retained");
        Assert(Get<bool>(f.Page, "applyOnCloseReady") == eligible, "Wrong Apply eligibility");
        Assert(Get<string>(f.Page, "applyCloseSkipReason") == skip, "Wrong Apply skip reason");
        Assert(f.ApplyCalls == 0 && f.CleanupCalls == 0 && f.ClosedCalls == 0, "Delegate ran inside OnClose");
        Assert(eligible ? Object.ReferenceEquals(Get<object>(f.Page, "applyA"), f.A) : Get<object>(f.Page, "applyA") == null,
            "Wrong captured A");
        Assert(eligible ? Object.ReferenceEquals(Get<object>(f.Page, "applyB"), f.B) : Get<object>(f.Page, "applyB") == null,
            "Wrong captured B");
    }

    private static void CheckAfterClose(Fixture f, bool eligible)
    {
        int before = forbiddenInterfaceCalls;
        f.Page.AfterClose();
        Assert(forbiddenInterfaceCalls == before, "AfterClose read a component interface");
        Assert(f.ApplyCalls == (eligible ? 1 : 0), "Wrong Apply count after AfterClose");
        Assert(f.CleanupCalls == 1 && f.ClosedCalls == 1, "Cleanup/closed callback did not run once");
        Assert(Get<bool>(f.Page, "closing") && !Get<bool>(f.Page, "shown"), "AfterClose left page open");
        Assert(Get<int>(f.Page, "pageEpoch") == 19, "AfterClose did not invalidate epoch");
        Assert(Get<bool>(f.Page, "closeNotified"), "AfterClose did not mark notification");
        Assert(!Get<bool>(f.Page, "applyOnCloseReady"), "Apply intent survived AfterClose");
        Assert(Get<object>(f.Page, "applyA") == null && Get<object>(f.Page, "applyB") == null, "Apply snapshot was retained");
        Assert(Get<object>(f.Page, "committedA") == null && Get<object>(f.Page, "committedB") == null, "Selection snapshot was retained");
        string order = String.Join(",", f.Events.ToArray());
        Assert(order == (eligible ? "apply,cleanup,closed" : "cleanup,closed"), "Wrong callback order: " + order);
        f.Page.AfterClose();
        Assert(f.ApplyCalls == (eligible ? 1 : 0) && f.CleanupCalls == 1 && f.ClosedCalls == 1, "Duplicate AfterClose repeated callbacks");
        Assert(Get<int>(f.Page, "pageEpoch") == 19, "Duplicate AfterClose changed epoch");
    }

    private static void Case(string name, Fixture f, int reason, bool eligible, string skip)
    {
        Run(name, () => { CheckClose(f, reason, eligible, skip); CheckAfterClose(f, eligible); });
    }

    private static void RunNewContracts()
    {
        int okay = (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay;
        int cancel = (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Cancel;
        Case("EMPTY_OK_SKIPS_APPLY_AND_CLEANS", Prepared(false, false, false, 0, 0), okay, false, "INCOMPLETE_OR_INVALID_PAIR");
        Case("A_ONLY_REVISION_2_COMMITTED_0_OK_NO_THROW", Prepared(true, false, false, 2, 0), okay, false, "INCOMPLETE_OR_INVALID_PAIR");
        Case("STALE_COMPLETE_PAIR_OK_SKIPS_APPLY", Prepared(true, true, true, 5, 4), okay, false, "SELECTION_CHANGED");
        Case("INVALID_COMPLETE_PAIR_OK_SKIPS_APPLY", Prepared(true, true, false, 4, 4), okay, false, "INCOMPLETE_OR_INVALID_PAIR");
        foreach (string busyField in new[] { "handlingAnalyze", "handlingShowBodies", "handlingCutPreview" })
        {
            Fixture f = ValidPair();
            Set(f.Page, busyField, true);
            Case("BUSY_" + busyField + "_OK_SKIPS_APPLY", f, okay, false, "BACKEND_BUSY_OR_QUEUED");
        }
        Fixture queued = ValidPair();
        Set(queued.Page, "backendActionQueued", true);
        Case("QUEUED_BACKEND_OK_SKIPS_APPLY", queued, okay, false, "BACKEND_BUSY_OR_QUEUED");
        Case("CURRENT_VALID_OK_APPLIES_ONLY_IN_AFTER_CLOSE", ValidPair(), okay, true, "NONE");
        Case("CANCEL_WITH_VALID_PAIR_SKIPS_APPLY", ValidPair(), cancel, false, "CANCEL_OR_OTHER_CLOSE");
        Run("DUPLICATE_CLOSE_AND_AFTER_CLOSE_APPLY_AT_MOST_ONCE", () =>
        {
            Fixture f = ValidPair();
            CheckClose(f, okay, true, "NONE");
            f.Page.OnClose(cancel);
            Assert(Get<int>(f.Page, "closeReason") == okay, "Duplicate close replaced original intent");
            Assert(Get<int>(f.Page, "pageEpoch") == 18 && Get<bool>(f.Page, "applyOnCloseReady"), "Duplicate close changed captured state");
            CheckAfterClose(f, true);
            f.Page.OnClose(okay);
            f.Page.AfterClose();
            Assert(f.ApplyCalls == 1 && f.CleanupCalls == 1 && f.ClosedCalls == 1, "Late duplicate repeated lifecycle callbacks");
            Assert(Get<int>(f.Page, "pageEpoch") == 19, "Late duplicate changed epoch");
        });
    }
#endif

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: R54CloseHarness.exe <results.json>");
            return 2;
        }
#if OLD_REPRO
        string mode = "OLD_REPRO";
        RunOldReproduction();
#else
        string mode = "R54_CONTRACTS";
        try { RunNewContracts(); }
        catch (Exception ex)
        {
            failures++;
            Results.Add(new { test = "HARNESS_SETUP", status = "FAIL", error_type = ex.GetType().Name, error = ex.Message });
        }
#endif
        if (forbiddenInterfaceCalls != 0) failures++;
        object output = new
        {
            status = failures == 0 ? "PASS_MANAGED_CONTRACT" : "FAIL",
            mode = mode,
            failure_count = failures,
            real_com_calls = 0,
            forbidden_interface_proxy_calls = forbiddenInterfaceCalls,
            native_windows_created = 0,
            installed_addin_test = "NOT_RUN",
            real_solidworks_api = "NOT_RUN",
            scope = "Reflection-supplied managed state; actual OnClose and AfterClose methods; no native lifecycle integration",
            tests = Results
        };
        string directory = Path.GetDirectoryName(args[0]);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(args[0], new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.Serialize(output));
        return failures == 0 ? 0 : 1;
    }
}
