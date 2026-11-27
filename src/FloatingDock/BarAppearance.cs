using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace FloatingDock;

internal static class BarAppearance
{
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, in int value, int size);

    internal static void RequestRoundCorners(nint hwnd)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            const int windowCornerPreference = 33;
            int round = 2;
            // A compositor that declines the hint retains a fully usable rectangular window.
            _ = DwmSetWindowAttribute(hwnd, windowCornerPreference, in round, sizeof(int));
        }
    }

    internal static Color Mix(Color surface, Color ink, int percent)
    {
        int Channel(int a, int b) => (a * (100 - percent) + b * percent) / 100;
        return Color.FromArgb(Channel(surface.R, ink.R), Channel(surface.G, ink.G), Channel(surface.B, ink.B));
    }
}

internal sealed class BarButton : Button
{
    private GraphicsPath? outline;
    private GraphicsPath? corners;
    private SolidBrush? backdrop;
    private Pen? edge;
    private Pen? focus;
    private Size geometry;
    private int dpi;
    private bool activeWindow;
    private bool minimizedWindow;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsActiveWindow
    {
        get => activeWindow;
        set { if (activeWindow == value) return; activeWindow = value; Invalidate(); }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsMinimizedWindow
    {
        get => minimizedWindow;
        set { if (minimizedWindow == value) return; minimizedWindow = value; Invalidate(); }
    }

    internal void SetOutline(Color background, Color border, Color ink)
    {
        backdrop ??= new SolidBrush(background);
        edge ??= new Pen(border);
        focus ??= new Pen(ink);
        backdrop.Color = background;
        edge.Color = border;
        focus.Color = ink;
        focus.Width = Math.Max(1, LogicalToDeviceUnits(2));
        Invalidate();
    }

    // Keep native Button input, labels, icons, hover, press and accessibility.
    // Only the tile edge is painted here. Paths change with size or monitor DPI.
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 4 || Height < 4) return;
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (FlatStyle == FlatStyle.Flat && backdrop != null)
        {
            if (geometry != ClientSize || dpi != DeviceDpi)
            {
                geometry = ClientSize;
                dpi = DeviceDpi;
                outline?.Dispose();
                corners?.Dispose();
                outline = new GraphicsPath();
                var rect = new RectangleF(1, 1, Width - 2, Height - 2);
                float radius = Math.Min(LogicalToDeviceUnits(9), Math.Min(rect.Width, rect.Height) / 2);
                outline.AddRoundedRectangle(rect, new SizeF(radius, radius));
                corners = new GraphicsPath(FillMode.Alternate);
                corners.AddRectangle(ClientRectangle);
                corners.AddPath(outline, false);
            }
            e.Graphics.FillPath(backdrop, corners!);
            e.Graphics.DrawPath(Focused && ShowFocusCues ? focus! : edge!, outline!);
        }
        if (activeWindow)
        {
            using var marker = new SolidBrush(SystemColors.Highlight);
            int diameter = Math.Max(4, LogicalToDeviceUnits(6));
            e.Graphics.FillEllipse(marker, Width - diameter - LogicalToDeviceUnits(5),
                LogicalToDeviceUnits(5), diameter, diameter);
        }
        if (minimizedWindow)
        {
            using var marker = new Pen(SystemInformation.HighContrast ? SystemColors.GrayText : BarAppearance.Mix(BackColor, ForeColor, 60),
                Math.Max(1, LogicalToDeviceUnits(2)));
            int inset = LogicalToDeviceUnits(6);
            int y = Height - LogicalToDeviceUnits(5);
            e.Graphics.DrawLine(marker, inset, y, inset + LogicalToDeviceUnits(8), y);
        }
        e.Graphics.SmoothingMode = smoothing;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            outline?.Dispose();
            corners?.Dispose();
            backdrop?.Dispose();
            edge?.Dispose();
            focus?.Dispose();
        }
        base.Dispose(disposing);
    }
}
