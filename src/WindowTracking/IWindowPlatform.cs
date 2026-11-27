namespace WindowTracking;

internal interface IWindowPlatform
{
    nint GetForegroundWindow();
    IEnumerable<WindowCandidate> EnumerateCandidates();
}
