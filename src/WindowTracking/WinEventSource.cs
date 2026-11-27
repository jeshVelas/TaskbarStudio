using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WindowTracking;

/// <summary>Out-of-context WinEvent hooks run on a dedicated message-loop thread.</summary>
internal sealed class WinEventSource : IWindowEventSource
{
    private const uint WineventOutOfContext = 0;
    private const uint WineventSkipOwnProcess = 2;
    private const uint WmQuit = 0x0012;
    private const uint WmApp = 0x8000;
    private const uint WmSignal = WmApp + 71;
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectDestroy = 0x8001;
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectNameChange = 0x800C;
    private const int ObjidWindow = 0;
    private const int ChildIdSelf = 0;
    private readonly object gate = new();
    private readonly HashSet<nint> hooks = [];
    private uint threadId;
    private bool disposed;
    private WinEventCallback? callback;
    private Action<WindowSignal>? receiver;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventCallback(nint hook, uint eventType, nint hwnd,
        int objectId, int childId, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetMessageW(out NativeMessage message, nint hwnd, uint min, uint max);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool PeekMessageW(out NativeMessage message, nint hwnd, uint min, uint max, uint remove);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool PostThreadMessageW(uint thread, uint message, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();

    public void Run(Action<WindowSignal> onSignal, WaitHandle stop)
    {
        ArgumentNullException.ThrowIfNull(onSignal);
        ArgumentNullException.ThrowIfNull(stop);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            receiver = onSignal;
            callback = OnWinEvent;
            threadId = GetCurrentThreadId();
            // Create the thread message queue before the owner can request stop.
            PeekMessageW(out _, 0, 0, 0, 0);
        }

        try
        {
            InstallHooks();
            onSignal(new WindowSignal(WindowSignalKind.Reconcile, 0));
            while (!stop.WaitOne(0))
            {
                int result = GetMessageW(out var message, 0, 0, 0);
                if (result <= 0 || message.Message == WmQuit) break;
                if (message.Message == WmSignal)
                    onSignal(new WindowSignal((WindowSignalKind)(int)message.WParam, message.LParam));
            }
        }
        finally
        {
            UnhookAll();
            lock (gate)
            {
                receiver = null;
                callback = null;
                threadId = 0;
            }
        }
    }

    private void InstallHooks()
    {
        var callback = this.callback ?? throw new InvalidOperationException("Callback no inicializado.");
        (uint First, uint Last)[] ranges = [
            (EventSystemForeground, EventSystemForeground),
            (EventSystemMinimizeStart, EventSystemMinimizeEnd),
            (EventObjectCreate, EventObjectHide),
            (EventObjectNameChange, EventObjectNameChange)
        ];
        foreach (var (first, last) in ranges)
        {
            nint hook = SetWinEventHook(first, last, 0, callback, 0, 0,
                WineventOutOfContext | WineventSkipOwnProcess);
            if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo registrar WinEvent.");
            lock (gate) hooks.Add(hook);
        }
    }

    private void OnWinEvent(nint hook, uint eventType, nint hwnd, int objectId,
        int childId, uint eventThread, uint eventTime)
    {
        if (hwnd == 0 || objectId != ObjidWindow || childId != ChildIdSelf) return;
        WindowSignalKind? kind = eventType switch
        {
            EventSystemForeground => WindowSignalKind.Foreground,
            EventSystemMinimizeStart => WindowSignalKind.MinimizeStart,
            EventSystemMinimizeEnd => WindowSignalKind.MinimizeEnd,
            EventObjectCreate => WindowSignalKind.Create,
            EventObjectDestroy => WindowSignalKind.Destroy,
            EventObjectShow => WindowSignalKind.Show,
            EventObjectHide => WindowSignalKind.Hide,
            EventObjectNameChange => WindowSignalKind.NameChange,
            _ => null
        };
        if (kind is WindowSignalKind signal)
        {
            uint thread;
            lock (gate) thread = threadId;
            if (thread != 0) PostThreadMessageW(thread, WmSignal, (nuint)signal, hwnd);
        }
    }

    public void Dispose()
    {
        uint thread;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            thread = threadId;
            if (thread != 0) PostThreadMessageW(thread, WmQuit, 0, 0);
        }
        if (thread == 0) UnhookAll();
    }

    public void RequestReconcile()
    {
        lock (gate)
            if (threadId != 0 && !disposed)
                PostThreadMessageW(threadId, WmSignal, (nuint)WindowSignalKind.Reconcile, 0);
    }

    private void UnhookAll()
    {
        nint[] current;
        lock (gate)
        {
            current = hooks.ToArray();
            hooks.Clear();
        }
        foreach (nint hook in current) UnhookWinEvent(hook);
    }
}
