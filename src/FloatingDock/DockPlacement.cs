using System.Drawing;

namespace FloatingDock;

internal static class DockPlacement
{
    public static Point Constrain(Rectangle area, Size window, Point requested) =>
        new(Math.Clamp(requested.X, area.Left, Math.Max(area.Left, area.Right - window.Width)),
            Math.Clamp(requested.Y, area.Top, Math.Max(area.Top, area.Bottom - window.Height)));

    public static Point BottomCenter(Rectangle area, Size window, int margin) =>
        new(area.Left + Math.Max(0, (area.Width - window.Width) / 2),
            Math.Max(area.Top, area.Bottom - window.Height - margin));
}
