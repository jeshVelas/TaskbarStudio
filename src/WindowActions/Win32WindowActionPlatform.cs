using System.Runtime.InteropServices;
using System.Text;
using WindowTracking;

namespace WindowActions;

/// <summary>Documented, non-injecting Win32 calls used by WindowActionService.</summary>
internal sealed class Win32WindowActionPlatform : IWindowActionPlatform
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint GaRoot = 2;
    private const int DwmwaCloaked = 14;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes { public nint Sid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenMandatoryLabel { public SidAndAttributes Label; }

    [DllImport("user32.dll", EntryPoint = "IsWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowNative(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll", EntryPoint = "IsIconic", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconicNative(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true, SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true, SetLastError = true)]
    private static extern int GetWindowLong32(nint hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetWindowTextW(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "ShowWindowAsync", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint hwnd, int command);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowNative(nint hwnd);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint process, uint access, out nint token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int informationClass, nint information,
        int informationLength, out int returnLength);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern nint GetSidSubAuthorityCount(nint sid);

    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern nint GetSidSubAuthority(nint sid, uint index);

    public bool IsWindow(nint id) => IsWindowNative(id);
    public bool IsIconic(nint id) => IsIconicNative(id);

    public WindowCandidate? Inspect(nint id)
    {
        if (!IsWindowNative(id)) return null;
        uint threadId = GetWindowThreadProcessId(id, out uint rawProcessId);
        if (threadId == 0 || rawProcessId == 0 || rawProcessId > int.MaxValue) return null;

        int cloakedResult = DwmGetWindowAttribute(id, DwmwaCloaked, out int cloakedValue, sizeof(int));
        // Fail closed if DWM cannot confirm the target's visibility state.
        if (cloakedResult != 0) return null;
        long extendedStyle = GetExtendedStyle(id).ToInt64();
        var title = new StringBuilder(1024);
        _ = GetWindowTextW(id, title, title.Capacity);
        int processId = (int)rawProcessId;
        return new WindowCandidate(id, GetAncestor(id, GaRoot), title.ToString(), processId, null,
            IsWindowVisible(id), IsIconic(id), (extendedStyle & WsExToolWindow) != 0,
            (extendedStyle & WsExAppWindow) != 0, cloakedValue != 0,
            processId == Environment.ProcessId, null, threadId);
    }

    public bool IsSameOrLowerIntegrity(int processId)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return false;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out nint targetToken)) return false;
            try
            {
                int? target = ReadIntegrityRid(targetToken);
                if (target is null || !OpenProcessToken(GetCurrentProcess(), TokenQuery, out nint currentToken)) return false;
                try
                {
                    int? current = ReadIntegrityRid(currentToken);
                    return current is not null && target.Value <= current.Value;
                }
                finally { _ = CloseHandle(currentToken); }
            }
            finally { _ = CloseHandle(targetToken); }
        }
        finally { _ = CloseHandle(process); }
    }

    public void RestoreAsync(nint id) => _ = ShowWindowAsync(id, SwRestore);
    public void MinimizeAsync(nint id) => _ = ShowWindowAsync(id, SwMinimize);
    public bool SetForegroundWindow(nint id) => SetForegroundWindowNative(id);

    private static int? ReadIntegrityRid(nint token)
    {
        _ = GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out int required);
        if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || required is <= 0 or > 65536) return null;
        nint buffer = Marshal.AllocHGlobal(required);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, required, out _)) return null;
            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            if (label.Label.Sid == 0) return null;
            nint countPointer = GetSidSubAuthorityCount(label.Label.Sid);
            if (countPointer == 0) return null;
            byte count = Marshal.ReadByte(countPointer);
            if (count == 0) return null;
            nint rid = GetSidSubAuthority(label.Label.Sid, (uint)(count - 1));
            return rid == 0 ? null : Marshal.ReadInt32(rid);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static nint GetExtendedStyle(nint hwnd) => IntPtr.Size == 8
        ? GetWindowLongPtr64(hwnd, -20)
        : new nint(GetWindowLong32(hwnd, -20));
}
