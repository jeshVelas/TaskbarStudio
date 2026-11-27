using System.Drawing.Drawing2D;
using Personalization;

namespace FloatingDock;

internal static class DockIcons
{
    // Stable artwork keys are independent of button order and launch targets.
    internal static Bitmap Render(DockIcon icon, int size)
    {
        var color = icon switch
        {
            DockIcon.Notepad => Color.MidnightBlue,
            DockIcon.Calculator => Color.DarkGreen,
            DockIcon.Explorer => Color.DarkRed,
            _ => throw new ArgumentOutOfRangeException(nameof(icon))
        };
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        using var fill = new SolidBrush(color);
        using var pen = new Pen(Color.White, 2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 28f, size / 28f);
        g.FillEllipse(fill, 0, 0, 27, 27);
        switch (icon)
        {
            case DockIcon.Notepad:
                g.DrawRectangle(pen, 8, 5, 12, 17);
                g.DrawLine(pen, 11, 11, 17, 11);
                g.DrawLine(pen, 11, 15, 17, 15);
                break;
            case DockIcon.Calculator:
                g.DrawRectangle(pen, 7, 5, 14, 17);
                g.DrawLine(pen, 8, 11, 20, 11);
                g.DrawLine(pen, 14, 13, 14, 20);
                g.DrawLine(pen, 9, 16, 19, 16);
                break;
            case DockIcon.Explorer:
                g.DrawPolygon(pen, new Point[] { new(4, 9), new(11, 9), new(13, 11),
                    new(23, 11), new(21, 20), new(6, 20) });
                break;
        }
        return bitmap;
    }
}
