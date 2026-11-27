using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowTracking;

/// <summary>Documented Win32 window queries, isolated from eligibility policy.</summary>
internal sealed class Win32WindowPlatform : IWindowPlatform
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint GaRoot = 2;
    private const int DwmwaCloaked = 14;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private delegate bool EnumWindowsCallback(nint hwnd, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLong32(nint hwnd, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", ExactSpelling = true)]
    private static extern nint GetForegroundWindowNative();

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    public nint GetForegroundWindow() => GetForegroundWindowNative();

    public IEnumerable<WindowCandidate> EnumerateCandidates()
    {
        var windows = new List<WindowCandidate>();
        EnumWindowsCallback callback = (hwnd, _) =>
        {
            try
            {
                windows.Add(ReadCandidate(hwnd));
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
            return true;
        };

        if (!EnumWindows(callback, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudieron enumerar las ventanas.");
        GC.KeepAlive(callback);
        return windows;
    }

    private static WindowCandidate ReadCandidate(nint hwnd)
    {
        uint threadId = GetWindowThreadProcessId(hwnd, out uint rawProcessId);
        if (threadId == 0 || rawProcessId == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        int capacity = 256;
        var title = new StringBuilder(capacity);
        int length;
        do
        {
            length = GetWindowTextW(hwnd, title, title.Capacity);
            if (length < title.Capacity - 1 || title.Capacity >= 4096) break;
            title.EnsureCapacity(Math.Min(title.Capacity * 2, 4096));
        } while (true);

        long extendedStyle = GetExtendedStyle(hwnd).ToInt64();
        bool cloaked = DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloakedValue, sizeof(int)) == 0 &&
            cloakedValue != 0;
        int processId = checked((int)rawProcessId);
        var (processName, executablePath) = TryGetProcessIdentity(processId);

        return new WindowCandidate(hwnd, GetAncestor(hwnd, GaRoot), title.ToString(), processId,
            processName, IsWindowVisible(hwnd), IsIconic(hwnd),
            (extendedStyle & WsExToolWindow) != 0, (extendedStyle & WsExAppWindow) != 0,
            cloaked, false, executablePath, threadId);
    }

    private static nint GetExtendedStyle(nint hwnd)
    {
        if (IntPtr.Size == 8) return GetWindowLongPtr64(hwnd, GwlExStyle);
        return new nint(GetWindowLong32(hwnd, GwlExStyle));
    }

    // QueryFullProcessImageNameW avoids constructing System.Diagnostics.Process for a
    // process that may exit between EnumWindows and metadata lookup.
    private static (string? Name, string? Path) TryGetProcessIdentity(int processId)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return (null, null);
        try
        {
            var path = new StringBuilder(1024);
            uint capacity = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref capacity)) return (null, null);
            string fullPath = path.ToString();
            return (Path.GetFileNameWithoutExtension(fullPath), fullPath);
        }
        catch (ArgumentException) { return (null, null); }
        finally { _ = CloseHandle(process); }
    }
}
