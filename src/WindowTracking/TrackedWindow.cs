namespace WindowTracking;

/// <summary>A point-in-time description of an eligible top-level desktop window.</summary>
public sealed record TrackedWindow(
    nint Id,
    string Title,
    int ProcessId,
    string? ProcessName,
    bool IsMinimized,
    bool IsActive,
    uint ThreadId = 0);
