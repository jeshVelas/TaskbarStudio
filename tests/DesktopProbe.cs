using System;
using System.Runtime.InteropServices;

// Read-only queries for the Windows PowerShell desktop tests.
public static class DesktopProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr Window;
        public uint Callback, Edge;
        public Rect Bounds;
        public IntPtr Parameter;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out Rect value, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern uint GetGuiResources(IntPtr process, uint flags);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    public static string Snapshot()
    {
        // UI Automation can initialize DPI awareness during the first cycle.
        IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // PerMonitorV2
        try
        {
            Rect area;
            if (!SystemParametersInfoW(0x30, 0, out area, 0)) // SPI_GETWORKAREA, primary display
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var data = new AppBarData();
            data.Size = (uint)Marshal.SizeOf(typeof(AppBarData));
            if (SHAppBarMessage(5, ref data) == UIntPtr.Zero) // ABM_GETTASKBARPOS
                throw new InvalidOperationException("Windows taskbar unavailable.");
            ulong flags = SHAppBarMessage(4, ref data).ToUInt64(); // ABM_GETSTATE
            return string.Format("area={0},{1},{2},{3} bar={4},{5},{6},{7} flags=0x{8:X} autohide={9}",
                area.Left, area.Top, area.Right, area.Bottom, data.Bounds.Left, data.Bounds.Top,
                data.Bounds.Right, data.Bounds.Bottom, flags, (flags & 1) != 0);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
}
