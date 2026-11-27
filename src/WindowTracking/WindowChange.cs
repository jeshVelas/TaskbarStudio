namespace WindowTracking;

public sealed record WindowChange(
    IReadOnlyList<TrackedWindow> Added,
    IReadOnlyList<TrackedWindow> Removed,
    IReadOnlyList<WindowUpdate> Updated);

public sealed record WindowUpdate(TrackedWindow Previous, TrackedWindow Current);
