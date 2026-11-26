using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace FloatingDock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DockForm(args.Contains("--load-test")));
    }
}

internal sealed class DockForm : Form
{
    // Original geometric artwork, drawn locally at the monitor's DPI.
    private static readonly (string Name, string File, Color Color)[] Shortcuts =
    [
        ("Bloc de notas", "notepad.exe", Color.MidnightBlue),
        ("Calculadora", "calc.exe", Color.DarkGreen),
        ("Explorador", "explorer.exe", Color.DarkRed)
    ];
    private readonly List<Button> launchers = [];
    private bool repositionPending;

    public DockForm(bool loadTest)
    {
        Text = "Taskbar Studio · Dock";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6) };
        Controls.Add(row);
        int count = loadTest ? 20 : Shortcuts.Length;
        row.FlowDirection = FlowDirection.LeftToRight;
        // Small rows keep the workload usable on ordinary laptop screens.
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = loadTest ? 5 : 4 };
        row.Controls.Add(grid);
        for (int index = 0; index < count; index++)
        {
            var shortcut = Shortcuts[index % Shortcuts.Length];
            var button = new Button { Text = shortcut.Name, AutoSize = true, TabIndex = index,
                TextImageRelation = TextImageRelation.ImageAboveText, AccessibleName = shortcut.Name };
            button.Click += (_, _) => Launch(shortcut.File);
            launchers.Add(button);
            grid.Controls.Add(button, index % grid.ColumnCount, index / grid.ColumnCount);
        }
        var close = new Button { Text = "Cerrar", AutoSize = true, TabIndex = count };
        close.Click += (_, _) => Close();
        grid.Controls.Add(close, count % grid.ColumnCount, count / grid.ColumnCount);
        AcceptButton = null;
        CancelButton = close;
        Load += (_, _) => { RefreshIcons(); PositionDock(); };
        Shown += (_, _) => { launchers[0].Select(); PositionDock(); };
        DpiChanged += (_, _) => { RefreshIcons(); QueuePosition(); };
    }

    private void Launch(string file)
    {
        string dir = file == "explorer.exe"
            ? Environment.GetFolderPath(Environment.SpecialFolder.Windows) : Environment.SystemDirectory;
        try { Process.Start(new ProcessStartInfo(Path.Combine(dir, file)) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "No se pudo abrir el acceso");
        }
    }

    private void RefreshIcons()
    {
        for (int i = 0; i < launchers.Count; i++)
        {
            int kind = i % Shortcuts.Length;
            int size = LogicalToDeviceUnits(28);
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            using (var fill = new SolidBrush(Shortcuts[kind].Color))
            using (var pen = new Pen(Color.White, 2))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(size / 28f, size / 28f);
                g.FillEllipse(fill, 0, 0, 27, 27);
                if (kind == 0)
                {
                    g.DrawRectangle(pen, 8, 5, 12, 17);
                    g.DrawLine(pen, 11, 11, 17, 11);
                    g.DrawLine(pen, 11, 15, 17, 15);
                }
                else if (kind == 1)
                {
                    g.DrawRectangle(pen, 7, 5, 14, 17);
                    g.DrawLine(pen, 8, 11, 20, 11);
                    g.DrawLine(pen, 14, 13, 14, 20);
                    g.DrawLine(pen, 9, 16, 19, 16);
                }
                else g.DrawPolygon(pen, new Point[] { new(4, 9), new(11, 9), new(13, 11),
                    new(23, 11), new(21, 20), new(6, 20) });
            }
            var previous = launchers[i].Image;
            launchers[i].Image = bitmap;
            previous?.Dispose();
        }
    }

    private void PositionDock()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        Location = DockPlacement.BottomCenter(area, Size, LogicalToDeviceUnits(10));
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is 0x007E or 0x001A) QueuePosition(); // Display or desktop settings changed.
    }

    private void QueuePosition()
    {
        if (repositionPending || !IsHandleCreated || Disposing) return;
        repositionPending = true;
        BeginInvoke(() => { repositionPending = false; PositionDock(); });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var button in launchers)
            {
                button.Image?.Dispose();
                button.Image = null;
            }
        base.Dispose(disposing);
    }
}
