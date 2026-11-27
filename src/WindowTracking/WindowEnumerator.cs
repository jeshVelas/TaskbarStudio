namespace WindowTracking;

/// <summary>Reads one snapshot of eligible top-level windows. This class does not retain titles.</summary>
public sealed class WindowEnumerator
{
    private readonly IWindowPlatform platform;
    private readonly HashSet<int> excludedProcesses;
    private readonly string? floatingDockPath;

    public WindowEnumerator() : this(new Win32WindowPlatform(), DefaultExcludedProcesses(), FindFloatingDockPath()) { }

    internal WindowEnumerator(IWindowPlatform platform, IEnumerable<int> excludedProcesses, string? floatingDockPath = null)
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.excludedProcesses = excludedProcesses.ToHashSet();
        this.floatingDockPath = NormalizePath(floatingDockPath);
    }

    public IReadOnlyList<TrackedWindow> Enumerate()
    {
        nint active = platform.GetForegroundWindow();
        var result = new List<TrackedWindow>();
        foreach (var candidate in platform.EnumerateCandidates())
        {
            var current = excludedProcesses.Contains(candidate.ProcessId)
                ? candidate with { IsOwnProcess = true }
                : candidate;
            if (floatingDockPath != null && NormalizePath(current.ExecutablePath) is string path &&
                StringComparer.OrdinalIgnoreCase.Equals(path, floatingDockPath))
                current = current with { IsOwnProcess = true };
            if (!WindowEligibility.IsEligible(current)) continue;

            result.Add(new TrackedWindow(current.Id, current.Title, current.ProcessId,
                current.ProcessName, current.IsMinimized, current.Id == active, current.ThreadId));
        }
        return result;
    }

    private static IEnumerable<int> DefaultExcludedProcesses()
    {
        yield return Environment.ProcessId;
    }

    private static string? FindFloatingDockPath()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("FloatingDock"))
        {
            using (process)
            {
                try { return NormalizePath(process.MainModule?.FileName); }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        return null;
    }

    private static string? NormalizePath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
