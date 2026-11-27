namespace WindowTracking;

/// <summary>Window properties required by the pure eligibility policy.</summary>
public sealed record WindowCandidate(
    nint Id,
    nint RootId,
    string Title,
    int ProcessId,
    string? ProcessName,
    bool IsVisible,
    bool IsMinimized,
    bool IsToolWindow,
    bool HasAppWindowStyle,
    bool IsCloaked,
    bool IsOwnProcess,
    string? ExecutablePath = null,
    uint ThreadId = 0);

/// <summary>Filters top-level windows suitable for a future taskbar surface.</summary>
public static class WindowEligibility
{
    public static bool IsEligible(WindowCandidate window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.Id != 0 &&
            window.RootId == window.Id &&
            window.ProcessId > 0 &&
            !window.IsOwnProcess &&
            !window.IsCloaked &&
            window.IsVisible &&
            !window.IsToolWindow &&
            (window.HasAppWindowStyle || window.Title.Length > 0);
    }
}
