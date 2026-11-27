using WindowTracking;
using System.Runtime.InteropServices;

namespace WindowActions;

public enum WindowActionStatus
{
    Accepted,
    AlreadyInRequestedState,
    WindowUnavailable,
    WindowChanged,
    NotEligible,
    ForegroundDenied,
    Failed
}

public readonly record struct WindowActionResult(WindowActionStatus Status)
{
    public bool WasAccepted => Status is WindowActionStatus.Accepted or WindowActionStatus.AlreadyInRequestedState;
}

public interface IWindowActionService
{
    WindowActionResult Activate(TrackedWindow window);
    WindowActionResult Minimize(TrackedWindow window);
    WindowActionResult Restore(TrackedWindow window);
}

/// <summary>
/// Revalidates each target immediately before issuing a non-blocking window command.
/// HWND, PID and UI-thread identity are checked to reduce the risk from recycled handles.
/// </summary>
public sealed class WindowActionService : IWindowActionService
{
    private readonly IWindowActionPlatform platform;

    public WindowActionService() : this(new Win32WindowActionPlatform()) { }

    internal WindowActionService(IWindowActionPlatform platform)
        => this.platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public WindowActionResult Activate(TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            var validation = Validate(window);
            if (validation.Status != WindowActionStatus.Accepted) return validation;
            if (platform.IsIconic(window.Id))
            {
                platform.RestoreAsync(window.Id);
                // The HWND may have closed or been recycled between restoration and activation.
                validation = Validate(window);
                if (validation.Status != WindowActionStatus.Accepted) return validation;
            }
            return platform.SetForegroundWindow(window.Id)
                ? new(WindowActionStatus.Accepted)
                : new(WindowActionStatus.ForegroundDenied);
        }
        catch (Exception ex) when (IsNativeFailure(ex)) { return new(WindowActionStatus.Failed); }
    }

    public WindowActionResult Minimize(TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            var validation = Validate(window);
            if (validation.Status != WindowActionStatus.Accepted) return validation;
            if (platform.IsIconic(window.Id)) return new(WindowActionStatus.AlreadyInRequestedState);
            platform.MinimizeAsync(window.Id);
            return new(WindowActionStatus.Accepted);
        }
        catch (Exception ex) when (IsNativeFailure(ex)) { return new(WindowActionStatus.Failed); }
    }

    public WindowActionResult Restore(TrackedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            var validation = Validate(window);
            if (validation.Status != WindowActionStatus.Accepted) return validation;
            if (!platform.IsIconic(window.Id)) return new(WindowActionStatus.AlreadyInRequestedState);
            platform.RestoreAsync(window.Id);
            return new(WindowActionStatus.Accepted);
        }
        catch (Exception ex) when (IsNativeFailure(ex)) { return new(WindowActionStatus.Failed); }
    }

    private WindowActionResult Validate(TrackedWindow expected)
    {
        if (expected.Id == 0 || expected.ProcessId <= 0 || expected.ThreadId == 0 || !platform.IsWindow(expected.Id))
            return new(WindowActionStatus.WindowUnavailable);
        var actual = platform.Inspect(expected.Id);
        if (actual is null) return new(WindowActionStatus.WindowUnavailable);
        if (actual.Id != expected.Id || actual.ProcessId != expected.ProcessId || actual.ThreadId != expected.ThreadId)
            return new(WindowActionStatus.WindowChanged);
        if (!WindowEligibility.IsEligible(actual) || actual.IsOwnProcess || !platform.IsSameOrLowerIntegrity(actual.ProcessId))
            return new(WindowActionStatus.NotEligible);
        return new(WindowActionStatus.Accepted);
    }

    private static bool IsNativeFailure(Exception ex) => ex is
        System.ComponentModel.Win32Exception or InvalidOperationException or
        DllNotFoundException or EntryPointNotFoundException or ExternalException;
}
