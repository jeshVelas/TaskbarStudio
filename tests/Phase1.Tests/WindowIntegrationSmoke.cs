using FloatingDock;
using Personalization;
using WindowTracking;
using WindowActions;

namespace Phase1.Tests;

internal static class WindowIntegrationSmoke
{
    internal static void Run(string dir)
    {
        var platform = new FakePlatform();
        var source = new FakeSource();
        var actions = new FakeActions();
        using var tracker = new WindowTracker(new WindowEnumerator(platform, [Environment.ProcessId]), source);
        using var dock = new DockForm(false, new ThemeCatalog(dir), new ProfileStore(dir), windowTracker: tracker, windowActions: actions);
        dock.Show();
        WaitFor(() => source.Started.IsSet && tracker.IsRunning, "Tracker did not start with the dock");

        platform.Set([Candidate(0x501, "Document - Notepad", 501, false)], 0);
        source.Emit(new WindowSignal(WindowSignalKind.Create, (nint)0x501));
        WaitFor(() => WindowButtons(dock).Length == 1, "New window was not added");
        var note = WindowButtons(dock).Single();
        Program.Check(note.Image is not null && note.AccessibleName!.Contains("Document - Notepad"), "Window presentation/icon fallback");
        Program.Check(!note.IsActiveWindow && note.AccessibleDescription!.Contains("Document - Notepad"), "Window started with wrong state");
        Program.Check(note.ContextMenuStrip!.Items.Cast<ToolStripItem>().Select(item => item.Text)
            .SequenceEqual(["Activar", "Minimizar", "Restaurar"]), "Window context menu entries");
        note.PerformClick();
        WaitFor(() => actions.Activated.Count == 1, "Left click did not request activation");
        Program.Check(actions.Activated.Single().Id == (nint)0x501, "Activation targeted a different window");
        Pump(30);
        note.ContextMenuStrip.Items.Cast<ToolStripItem>().Single(item => item.Text == "Minimizar").PerformClick();
        WaitFor(() => actions.Minimized.Count == 1, "Context menu did not request minimization");
        Pump(30);
        note.ContextMenuStrip.Items.Cast<ToolStripItem>().Single(item => item.Text == "Restaurar").PerformClick();
        WaitFor(() => actions.Restored.Count == 1, "Context menu did not request restoration");

        source.Emit(new WindowSignal(WindowSignalKind.Create, (nint)0x501));
        Pump(150);
        Program.Check(WindowButtons(dock).Length == 1, "Duplicate event created another tile");

        platform.Set([Candidate(0x501, "Document - Notepad", 501, false)], 0x501);
        source.Emit(new WindowSignal(WindowSignalKind.Foreground, (nint)0x501));
        WaitFor(() => WindowButtons(dock).Single().IsActiveWindow, "Active marker did not update");

        platform.Set([Candidate(0x501, "Document - Notepad", 501, true)], 0x501);
        source.Emit(new WindowSignal(WindowSignalKind.MinimizeStart, (nint)0x501));
        WaitFor(() => WindowButtons(dock).Single().IsMinimizedWindow &&
            WindowButtons(dock).Single().AccessibleDescription!.Contains("minimizada"), "Minimized marker did not update");

        platform.Set([Candidate(0x501, "Document - Notepad (edited)", 501, false)], 0);
        source.Emit(new WindowSignal(WindowSignalKind.NameChange, (nint)0x501));
        WaitFor(() => WindowButtons(dock).Single().AccessibleName!.Contains("edited"), "Window title did not update");

        platform.Set([Candidate(0x501, "Document - Notepad (edited)", 501, false),
            Candidate(0x502, "Taskbar Studio · Dock", Environment.ProcessId, false)], 0);
        source.Emit(new WindowSignal(WindowSignalKind.Show, (nint)0x502));
        Pump(150);
        Program.Check(WindowButtons(dock).Length == 1, "Own-process window was included");

        var manyWindows = Enumerable.Range(0, 16)
            .Select(index => Candidate((nint)(0x510 + index), $"Test application {index}", 510 + index, false))
            .Prepend(Candidate(0x501, "Document - Notepad (edited)", 501, false)).ToArray();
        platform.Set(manyWindows, 0);
        source.Emit(new WindowSignal(WindowSignalKind.Show, (nint)0x510));
        WaitFor(() => WindowButtons(dock).Length == 17, "Additional windows were not reconciled");
        int widthLimit = Math.Min(Screen.FromControl(dock).WorkingArea.Width, 760 * dock.DeviceDpi / 96);
        Program.Check(dock.Width <= widthLimit, "Dock exceeded its bounded width with many windows");

        platform.Set([], 0);
        source.Emit(new WindowSignal(WindowSignalKind.Destroy, (nint)0x501));
        WaitFor(() => WindowButtons(dock).Length == 0, "Closed windows remained after reconciliation");
        // Queue another state change and close immediately to exercise pending UI dispatch cleanup.
        platform.Set([Candidate(0x503, "Pending close", 503, false)], 0);
        source.Emit(new WindowSignal(WindowSignalKind.Create, (nint)0x503));
        dock.Close();
        Program.Check(source.Disposed && !tracker.IsRunning, "Dock did not stop its tracker");
    }

    private static BarButton[] WindowButtons(Control dock) => Descendants(dock).OfType<BarButton>()
        .Where(button => button.Name.StartsWith("window_", StringComparison.Ordinal)).ToArray();

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => Descendants(child).Prepend(child));

    private static void WaitFor(Func<bool> condition, string message)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(3)) Pump(10);
        Program.Check(condition(), message);
    }

    private static void Pump(int milliseconds)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(5); } while (timer.ElapsedMilliseconds < milliseconds);
    }

    private static WindowCandidate Candidate(nint id, string title, int pid, bool minimized) =>
        new(id, id, title, pid, "test-app", true, minimized, false, false, false, false,
            $@"C:\Apps\Test-{pid}.exe", (uint)pid);

    private sealed class FakeActions : IWindowActionService
    {
        internal List<TrackedWindow> Activated { get; } = [];
        internal List<TrackedWindow> Minimized { get; } = [];
        internal List<TrackedWindow> Restored { get; } = [];
        public WindowActionResult Activate(TrackedWindow window) { Activated.Add(window); return new(WindowActionStatus.Accepted); }
        public WindowActionResult Minimize(TrackedWindow window) { Minimized.Add(window); return new(WindowActionStatus.Accepted); }
        public WindowActionResult Restore(TrackedWindow window) { Restored.Add(window); return new(WindowActionStatus.Accepted); }
    }

    private sealed class FakePlatform : IWindowPlatform
    {
        private readonly object gate = new();
        private WindowCandidate[] candidates = [];
        private nint foreground;
        public nint GetForegroundWindow() { lock (gate) return foreground; }
        public IEnumerable<WindowCandidate> EnumerateCandidates() { lock (gate) return candidates.ToArray(); }
        internal void Set(WindowCandidate[] value, nint active)
        {
            lock (gate) { candidates = value; foreground = active; }
        }
    }

    private sealed class FakeSource : IWindowEventSource
    {
        private readonly ManualResetEventSlim stopSource = new();
        private Action<WindowSignal>? callback;
        internal ManualResetEventSlim Started { get; } = new();
        internal bool Disposed { get; private set; }

        public void Run(Action<WindowSignal> onSignal, WaitHandle stop)
        {
            callback = onSignal;
            Started.Set();
            onSignal(new WindowSignal(WindowSignalKind.Reconcile, 0));
            WaitHandle.WaitAny([stop, stopSource.WaitHandle]);
        }

        internal void Emit(WindowSignal signal) => callback?.Invoke(signal);
        public void RequestReconcile() => callback?.Invoke(new WindowSignal(WindowSignalKind.Reconcile, 0));
        public void Dispose()
        {
            Disposed = true;
            callback = null;
            stopSource.Set();
        }
    }
}
