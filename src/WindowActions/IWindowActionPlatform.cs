using WindowTracking;

namespace WindowActions;

internal interface IWindowActionPlatform
{
    bool IsWindow(nint id);
    WindowCandidate? Inspect(nint id);
    bool IsSameOrLowerIntegrity(int processId);
    bool IsIconic(nint id);
    void RestoreAsync(nint id);
    void MinimizeAsync(nint id);
    bool SetForegroundWindow(nint id);
}
