using WindowTracking;

int durationSeconds = args.Length == 0 ? 120 :
    int.TryParse(args[0], out int parsed) && parsed is >= 10 and <= 600
        ? parsed : throw new ArgumentException("Duración válida: 10 a 600 segundos.");
Console.WriteLine($"WindowTracking diagnostics. Ctrl+C or timeout ({durationSeconds}s) closes the tracker.");
Console.WriteLine("Snapshot lists eligible windows. Events show only effective additions, removals and updates.");
using var tracker = new WindowTracker();
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
using var initialized = new ManualResetEventSlim();

tracker.Changed += (_, change) =>
{
    foreach (var window in change.Added) Console.WriteLine($"+ {Describe(window)}");
    foreach (var window in change.Removed) Console.WriteLine($"- {Describe(window)}");
    foreach (var update in change.Updated)
    {
        if (update.Previous.IsActive != update.Current.IsActive)
            Console.WriteLine($"focus {(update.Current.IsActive ? "on" : "off")}: {Describe(update.Current)}");
        if (update.Previous.IsMinimized != update.Current.IsMinimized)
            Console.WriteLine($"{(update.Current.IsMinimized ? "minimized" : "restored")}: {Describe(update.Current)}");
        if (update.Previous.Title != update.Current.Title)
            Console.WriteLine($"title: {Describe(update.Current)}");
    }
};
tracker.TrackingError += (_, error) => Console.Error.WriteLine($"Tracking error: {error.Message}");
tracker.Started += (_, _) => initialized.Set();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

tracker.Start();
if (!initialized.Wait(TimeSpan.FromSeconds(5))) Console.Error.WriteLine("Initial enumeration did not complete in five seconds.");
Console.WriteLine("Initial eligible windows:");
foreach (var window in tracker.Snapshot) Console.WriteLine($"= {Describe(window)}");
Console.WriteLine("Open, focus, minimize, restore and close test windows. No window actions are sent by this tool.");
try { await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token); }
catch (OperationCanceledException) { }
finally { tracker.Dispose(); }
Console.WriteLine("Hooks released. Diagnostic ended.");

static string Describe(TrackedWindow window)
{
    // Titles are displayed only in this console session and are never written to disk.
    return $"{window.Id} pid={window.ProcessId} {window.ProcessName ?? "?"} {(window.IsMinimized ? "minimized" : "visible")}";
}
