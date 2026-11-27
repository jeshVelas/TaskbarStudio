namespace WindowTracking;

internal enum WindowSignalKind { Reconcile, Foreground, Create, Destroy, Show, Hide, NameChange, MinimizeStart, MinimizeEnd }

internal readonly record struct WindowSignal(WindowSignalKind Kind, nint Window);

internal interface IWindowEventSource : IDisposable
{
    void Run(Action<WindowSignal> onSignal, WaitHandle stop);
    void RequestReconcile();
}
