using System.Drawing;

namespace FloatingDock;

internal static class DockPlacement
{
    public static Point BottomCenter(Rectangle area, Size window, int margin) =>
        new(area.Left + Math.Max(0, (area.Width - window.Width) / 2),
            Math.Max(area.Top, area.Bottom - window.Height - margin));
}
