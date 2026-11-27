using System.Diagnostics;
using WindowActions;
using WindowTracking;

namespace Phase1.Tests;

internal static class WindowActionLiveSmoke
{
    internal static void Run(string directory)
    {
        string title = $"Taskbar Studio action test {Guid.NewGuid():N}";
        using var target = StartTarget(title);
        using var tracker = new WindowTracker();
        tracker.Start();
        var actions = new WindowActionService();
        try
        {
            var tracked = WaitFor(tracker, target.Id, title, minimized: null);
            var activation = actions.Activate(tracked);
            Program.Check(activation.Status is WindowActionStatus.Accepted or WindowActionStatus.ForegroundDenied,
                $"Unexpected activation result: {activation.Status}.");

            var minimize = actions.Minimize(tracked);
            Program.Check(minimize.Status == WindowActionStatus.Accepted, "Minimize request was not accepted.");
            var minimized = WaitFor(tracker, target.Id, title, minimized: true);

            var restore = actions.Restore(minimized);
            Program.Check(restore.Status == WindowActionStatus.Accepted, "Restore request was not accepted.");
            var restored = WaitFor(tracker, target.Id, title, minimized: false);

            Program.Check(actions.Minimize(restored).Status == WindowActionStatus.Accepted, "Second minimize request failed.");
            minimized = WaitFor(tracker, target.Id, title, minimized: true);
            var activateMinimized = actions.Activate(minimized);
            Program.Check(activateMinimized.Status is WindowActionStatus.Accepted or WindowActionStatus.ForegroundDenied,
                "Activation of minimized window returned an unexpected result.");
            _ = WaitFor(tracker, target.Id, title, minimized: false);

            Program.Check(target.CloseMainWindow(), "Could not close the test window normally.");
            if (!target.WaitForExit(3000)) throw new TimeoutException("Test window did not close within three seconds.");
            var stale = actions.Activate(restored);
            Program.Check(stale.Status is WindowActionStatus.WindowUnavailable or WindowActionStatus.WindowChanged,
                "A closed window handle was accepted.");
        }
        finally
        {
            if (!target.HasExited)
            {
                _ = target.CloseMainWindow();
                if (!target.WaitForExit(2000))
                {
                    target.Kill(entireProcessTree: false);
                    target.WaitForExit(2000);
                }
            }
        }
    }

    private static Process StartTarget(string title)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Test executable path unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--window-action-target");
        start.ArgumentList.Add(title);
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated test window.");
    }

    private static TrackedWindow WaitFor(WindowTracker tracker, int processId, string title, bool? minimized)
    {
        tracker.Reconcile();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(4))
        {
            var match = tracker.Snapshot.FirstOrDefault(window => window.ProcessId == processId && window.Title == title);
            if (match is not null && (minimized is null || match.IsMinimized == minimized)) return match;
            Thread.Sleep(20);
        }
        throw new TimeoutException("The expected test window state was not observed.");
    }
}
