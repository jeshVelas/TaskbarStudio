using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using Personalization;

namespace FloatingDock;

/// <summary>Loads a window-provided icon off the UI thread and owns only copied HICONs.</summary>
internal static class WindowIconProvider
{
    private const uint WmGetIcon = 0x007F;
    private const uint IconSmall = 0;
    private const uint IconBig = 1;
    private const uint IconSmall2 = 2;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;
    private const int GclpHicon = -14;
    private const int GclpHiconSm = -34;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wParam, nint lParam,
        uint flags, uint timeout, out nint result);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
    private static extern nint GetClassLongPtr64(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW", SetLastError = true)]
    private static extern uint GetClassLong32(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    internal static Bitmap? TryLoad(nint hwnd)
    {
        if (hwnd == 0) return null;
        try
        {
            nint source = 0;
            foreach (nuint kind in new nuint[] { IconSmall2, IconSmall, IconBig })
            {
                if (SendMessageTimeout(hwnd, WmGetIcon, kind, 0, SmtoBlock | SmtoAbortIfHung, 150, out var result) != 0 && result != 0)
                {
                    source = result;
                    break;
                }
            }
            if (source == 0) source = GetClassIcon(hwnd, GclpHiconSm);
            if (source == 0) source = GetClassIcon(hwnd, GclpHicon);
            if (source == 0) return null;

            nint copy = CopyIcon(source);
            if (copy == 0) return null;
            try
            {
                using var icon = Icon.FromHandle(copy);
                return icon.ToBitmap();
            }
            finally { _ = DestroyIcon(copy); }
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    internal static Bitmap CreateFallback(int size, Theme theme, bool minimized)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        using var fill = new SolidBrush(ColorTranslator.FromHtml(theme.Colors.Button));
        using var edge = new Pen(ColorTranslator.FromHtml(theme.Colors.Text), Math.Max(1f, size / 16f));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        float inset = size * 0.13f;
        var bounds = new RectangleF(inset, inset, size - inset * 2, size - inset * 2);
        graphics.FillRectangle(fill, bounds);
        graphics.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        graphics.DrawLine(edge, bounds.X, bounds.Y + bounds.Height * 0.27f,
            bounds.Right, bounds.Y + bounds.Height * 0.27f);
        if (minimized)
            graphics.DrawLine(edge, bounds.X + bounds.Width * 0.25f, bounds.Bottom - bounds.Height * 0.18f,
                bounds.X + bounds.Width * 0.75f, bounds.Bottom - bounds.Height * 0.18f);
        return bitmap;
    }

    private static nint GetClassIcon(nint hwnd, int index) => IntPtr.Size == 8
        ? GetClassLongPtr64(hwnd, index)
        : unchecked((nint)(int)GetClassLong32(hwnd, index));
}
