using System.Collections.Concurrent;
using WindowTracking;

namespace Phase1.Tests;

internal static partial class Program
{
    private static void TestWindowTracker()
    {
        Test("Window tracker dispatches live changes, coalesces duplicates, and stops cleanly", _ =>
        {
            var platform = new FakeWindowPlatform();
            var source = new FakeWindowEventSource();
            using var tracker = new WindowTracker(new WindowEnumerator(platform, []), source);
            var changes = new ConcurrentQueue<WindowChange>();
            tracker.Changed += (_, change) => changes.Enqueue(change);
            tracker.Start();
            Check(source.Started.Wait(TimeSpan.FromSeconds(2)), "Hook source did not start");
            platform.Set([Candidate(0x101, "Editor", false)], active: 0);
            source.Emit(new WindowSignal(WindowSignalKind.Create, 0x101));
            WaitUntil(() => tracker.Snapshot.Count == 1, "Live create was not consumed");
            WaitUntil(() => changes.Count == 1, "Live change was not published");
            source.Emit(new WindowSignal(WindowSignalKind.NameChange, 0x101));
            Thread.Sleep(100);
            Check(changes.Count == 1, "Duplicate signal published unchanged state");

            platform.Set([Candidate(0x101, "Editor - saved", false)], active: 0);
            source.Emit(new WindowSignal(WindowSignalKind.NameChange, 0x101));
            WaitUntil(() => changes.Count == 2, "Title update missing");
            Check(changes.Last().Updated.Single().Current.Title == "Editor - saved", "Wrong updated title");

            platform.Set([Candidate(0x101, "Editor - saved", true)], active: 0x101);
            source.Emit(new WindowSignal(WindowSignalKind.Foreground, 0x101));
            WaitUntil(() => changes.Count == 3, "Active-state update missing");
            Check(tracker.Snapshot.Single().IsActive, "Active state missing");

            platform.Set([], active: 0);
            source.Emit(new WindowSignal(WindowSignalKind.Destroy, 0x101));
            WaitUntil(() => tracker.Snapshot.Count == 0, "Destroy not processed");
            Check(changes.Last().Removed.Single().Id == (nint)0x101, "Removed window missing");

            // Even if event payloads are stale or out of order, reconciliation reflects current OS state.
            platform.Set([Candidate(0x202, "Current", true)], active: 0x202);
            source.Emit(new WindowSignal(WindowSignalKind.Show, 0x101));
            WaitUntil(() => tracker.Snapshot.SingleOrDefault()?.Id == (nint)0x202, "Reconciliation failed");
            int beforeDispose = changes.Count;
            tracker.Dispose();
            Check(source.Disposed, "Event source was not disposed");
            source.EmitAfterDispose(new WindowSignal(WindowSignalKind.Destroy, 0x202));
            Thread.Sleep(100);
            Check(changes.Count == beforeDispose && !tracker.IsRunning, "Notifications or worker remained after Dispose");
        });
        Test("FloatingDock exclusion uses executable identity, not a similar third-party title", _ =>
        {
            var dockPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TaskbarStudio", "FloatingDock.exe"));
            var platform = new FakeWindowPlatform();
            platform.Set([
                Candidate(0x301, "Taskbar Studio · Dock", false) with { ProcessId = 3001, ProcessName = "FloatingDock", ExecutablePath = dockPath },
                Candidate(0x302, "TaskbarStudio - Antigravity IDE - FloatingDock.exe", false) with { ProcessId = 3002, ProcessName = "Antigravity IDE", ExecutablePath = @"C:\Apps\Antigravity.exe" }
            ], active: 0);
            var enumerator = new WindowEnumerator(platform, [], dockPath);
            var snapshot = enumerator.Enumerate();
            Check(snapshot.Count == 1 && snapshot[0].Id == (nint)0x302,
                "Did not exclude product executable or excluded a third party by its title");
        });
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(10);
        Check(condition(), message);
    }

    private static WindowCandidate Candidate(nint id, string title, bool active) =>
        new(id, id, title, 1234, "test", true, false, false, false, false, false,
            @"C:\Apps\Test.exe");

    private sealed class FakeWindowPlatform : IWindowPlatform
    {
        private WindowCandidate[] windows = [];
        private nint active;
        public nint GetForegroundWindow() => active;
        public IEnumerable<WindowCandidate> EnumerateCandidates() => Volatile.Read(ref windows);
        internal void Set(WindowCandidate[] value, nint active)
        {
            Volatile.Write(ref windows, value);
            this.active = active;
        }
    }

    private sealed class FakeWindowEventSource : IWindowEventSource
    {
        private Action<WindowSignal>? callback;
        private readonly ManualResetEventSlim quit = new();
        public ManualResetEventSlim Started { get; } = new();
        public bool Disposed { get; private set; }

        public void Run(Action<WindowSignal> onSignal, WaitHandle stop)
        {
            callback = onSignal;
            Started.Set();
            onSignal(new WindowSignal(WindowSignalKind.Reconcile, 0));
            WaitHandle.WaitAny([quit.WaitHandle, stop]);
        }

        public void RequestReconcile() => callback?.Invoke(new WindowSignal(WindowSignalKind.Reconcile, 0));
        public void Emit(WindowSignal signal) => callback?.Invoke(signal);
        public void EmitAfterDispose(WindowSignal signal) => callback?.Invoke(signal);
        public void Dispose()
        {
            Disposed = true;
            callback = null;
            quit.Set();
        }
    }
}
