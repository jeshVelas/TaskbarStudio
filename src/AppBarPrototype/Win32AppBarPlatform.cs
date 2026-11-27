using System.Drawing;
using System.Runtime.InteropServices;

namespace AppBarPrototype;

internal sealed class Win32AppBarPlatform : IAppBarPlatform
{
    internal const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_WINDOWPOSCHANGED = 9;
    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public int rcLeft, rcTop, rcRight, rcBottom;
        public nint lParam;
    }

    [DllImport("shell32.dll", ExactSpelling = true)] private static extern nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveWindow(nint hWnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    private static APPBARDATA Data(nint hwnd) => new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
    private static void SetRect(ref APPBARDATA d, Rectangle r) { d.rcLeft = r.Left; d.rcTop = r.Top; d.rcRight = r.Right; d.rcBottom = r.Bottom; }
    private static Rectangle GetRect(APPBARDATA d) => Rectangle.FromLTRB(d.rcLeft, d.rcTop, d.rcRight, d.rcBottom);
    public bool Register(nint hwnd, uint callbackMessage) { var d = Data(hwnd); d.uCallbackMessage = callbackMessage; return SHAppBarMessage(ABM_NEW, ref d) != 0; }
    public bool Query(nint hwnd, AppBarEdge edge, ref Rectangle rectangle) { var d = Data(hwnd); d.uEdge = (uint)edge; SetRect(ref d, rectangle); var ok = SHAppBarMessage(ABM_QUERYPOS, ref d) != 0; rectangle = GetRect(d); return ok; }
    public bool SetPosition(nint hwnd, AppBarEdge edge, ref Rectangle rectangle) { var d = Data(hwnd); d.uEdge = (uint)edge; SetRect(ref d, rectangle); var ok = SHAppBarMessage(ABM_SETPOS, ref d) != 0; rectangle = GetRect(d); return ok; }
    public bool Move(nint hwnd, Rectangle rectangle) => MoveWindow(hwnd, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, true);
    public bool Remove(nint hwnd) { var d = Data(hwnd); return SHAppBarMessage(ABM_REMOVE, ref d) != 0; }
    public void PositionChanged(nint hwnd) { var d = Data(hwnd); SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref d); }

    internal static bool ReadTaskbar(nint ownerHwnd, out Rectangle bounds, out uint state)
    {
        var p = Data(ownerHwnd);
        if (SHAppBarMessage(5, ref p) == 0) { bounds = Rectangle.Empty; state = 0; return false; }
        bounds = GetRect(p);
        p = Data(ownerHwnd); state = (uint)SHAppBarMessage(4, ref p); return true;
    }
    internal static Rectangle ReadWorkArea()
    {
        var r = new RECT();
        if (!SystemParametersInfo(0x0030, 0, ref r, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint param, ref RECT data, uint update);
}
