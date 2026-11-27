using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FloatingDock;
using Personalization;

namespace Phase1.Tests;

internal static partial class DockSmoke
{
    [DllImport("user32.dll")] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint hwnd, int message, nint wParam, nint lParam);

    // Feed the WinForms keyboard pipeline. Modifier state is local to this test's input thread.
    private static bool Key(Control target, Keys chord)
    {
        var original = new byte[256];
        Program.Check(GetKeyboardState(original), "Cannot read keyboard state");
        var state = (byte[])original.Clone();
        state[(int)Keys.ControlKey] = chord.HasFlag(Keys.Control) ? (byte)128 : (byte)0;
        state[(int)Keys.ShiftKey] = chord.HasFlag(Keys.Shift) ? (byte)128 : (byte)0;
        state[(int)Keys.Menu] = chord.HasFlag(Keys.Alt) ? (byte)128 : (byte)0;
        try
        {
            Program.Check(SetKeyboardState(state), "Cannot set thread keyboard state");
            var msg = Message.Create(target.Handle, chord.HasFlag(Keys.Alt) ? 0x104 : 0x100, (int)(chord & Keys.KeyCode), 0);
            return target.PreProcessMessage(ref msg);
        }
        finally { SetKeyboardState(original); }
    }

    internal static void Surface(string dir)
    {
        string desktop = DesktopProbe.Snapshot();
        var profiles = new ProfileStore(dir);
        using var dock = new DockForm(false, new ThemeCatalog(dir), profiles);
        dock.Show();
        Application.DoEvents();
        var area = Screen.FromControl(dock).WorkingArea;
        Program.Check(dock.FormBorderStyle == FormBorderStyle.None && !dock.TopMost &&
            area.Contains(dock.Bounds), "Frame, z-order or work-area bounds");
        int WidthFor(int count, bool labels = true)
        {
            Edit(dock, true, dialog =>
            {
                var boxes = Descendants(dialog).OfType<CheckBox>().ToArray();
                boxes.Single(c => c.Text == "Calculadora").Checked = count >= 2;
                boxes.Single(c => c.Text == "Explorador").Checked = count >= 3;
                boxes.Single(c => c.Text.Contains("&nombres")).Checked = labels;
            });
            var visible = Descendants(dock).OfType<Button>().Where(b => b.Visible).OrderBy(b => b.Left).ToArray();
            Program.Check(visible.Length == count + 1 && visible.All(b => dock.Bounds.Contains(b.RectangleToScreen(b.ClientRectangle))),
                "Visible controls clipped or missing");
            Program.Check(visible.All(b => b.Top == visible[0].Top && b.Height == visible[0].Height) &&
                visible.Zip(visible.Skip(1)).All(pair => pair.First.Right <= pair.Second.Left), "Misaligned controls");
            return dock.Width;
        }
        int three = WidthFor(3), two = WidthFor(2), one = WidthFor(1), iconOnly = WidthFor(1, false);
        Console.WriteLine($"DPI={dock.DeviceDpi} widths(px) 3={three}, 2={two}, 1={one}, icon-only={iconOnly}");
        Program.Check(three > two && two > one && one > iconOnly, "Width did not follow visible content");
        WidthFor(3);
        var buttons = Descendants(dock).OfType<Button>().OrderBy(b => b.TabIndex).ToArray();
        dock.Activate();
        buttons[0].Focus();
        Program.Check(Key(buttons[0], Keys.Tab) && buttons[1].Focused, "Tab order");
        Program.Check(Key(buttons[1], Keys.Shift | Keys.Tab) && buttons[0].Focused, "Reverse tab order");
        dock.BeginInvoke(() =>
        {
            var settings = dock.OwnedForms.OfType<ThemeSettings>().Single();
            Program.Check(Key(settings.ActiveControl!, Keys.Escape), "Escape did not cancel settings");
        });
        Program.Check(Key(buttons[0], Keys.Control | Keys.Oemcomma) && dock.Visible, "Ctrl+, routing");
        var start = dock.Location;
        Program.Check(Key(buttons[0], Keys.Alt | Keys.Up) && dock.Top < start.Y, "Keyboard movement");
        var grip = Descendants(dock).Single(c => c.AccessibleName == "Mover barra");
        var before = dock.Location;
        SendMessageW(grip.Handle, 0x201, 1, (5 << 16) | 5); // WM_LBUTTONDOWN
        SendMessageW(grip.Handle, 0x200, 1, (5 << 16) | 45); // WM_MOUSEMOVE
        SendMessageW(grip.Handle, 0x202, 0, (5 << 16) | 5);
        Program.Check(dock.Left == before.X + 40 && !grip.Capture && area.Contains(dock.Bounds), "Grip drag");
        var moved = dock.Location;
        WidthFor(2);
        Program.Check(dock.Location == moved, "Apply reset manual placement");
        dock.ContextMenuStrip!.Items[1].PerformClick();
        Program.Check(dock.Location == DockPlacement.BottomCenter(area, dock.Size, dock.LogicalToDeviceUnits(10)), "Recenter");
        Program.Check(DesktopProbe.Snapshot() == desktop, "Bar changed Windows taskbar or work area");
        Program.Check(Key(buttons[0], Keys.Escape) && !dock.Visible, "Escape did not close bar");
        using var reopened = new DockForm(false, new ThemeCatalog(dir), profiles);
        reopened.Show();
        reopened.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i => i.Text == "Cerrar").PerformClick();
        Program.Check(!reopened.Visible, "Context menu did not close bar");
        Program.Check(DesktopProbe.Snapshot() == desktop, "Desktop changed on close");
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, nint parameter);
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out int processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool PostMessageW(nint window, int message, nint wParam, nint lParam);

    private static List<nint> ApplicationWindows(string id, bool visibleOnly)
    {
        var found = new List<nint>();
        EnumWindows((hwnd, _) =>
        {
            if (visibleOnly && !IsWindowVisible(hwnd)) return true;
            var cls = new StringBuilder(128);
            GetClassName(hwnd, cls, cls.Capacity);
            var name = cls.ToString();
            if (id == "explorer" && name == "CabinetWClass" || id == "notepad" && name == "Notepad")
                found.Add(hwnd);
            else if (id == "calculator")
            {
                GetWindowThreadProcessId(hwnd, out int pid);
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (process.ProcessName.Equals("CalculatorApp", StringComparison.OrdinalIgnoreCase) &&
                        name is "WinUIDesktopWin32WindowClass" or "ApplicationFrameWindow" or "Windows.UI.Core.CoreWindow")
                        found.Add(hwnd);
                }
                catch (ArgumentException) { } // The observed window exited.
            }
            return true;
        }, 0);
        return found;
    }

    // Opt-in interactive check. Only newly observed app windows are asked to close.
    internal static void RealLaunch(string dir)
    {
        using var dock = new DockForm(false, new ThemeCatalog(dir), new ProfileStore(dir));
        dock.Show();
        foreach (var item in LauncherItem.BuiltIns)
        {
            var before = ApplicationWindows(item.Id, visibleOnly: false).ToHashSet();
            var button = Descendants(dock).OfType<Button>().Single(b => b.Name == item.Id);
            dock.Activate();
            button.Focus();
            Program.Check(Key(button, Keys.Enter), "Enter did not activate " + item.Label);
            var timer = Stopwatch.StartNew();
            nint opened = 0;
            while (opened == 0 && timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                Application.DoEvents();
                var windows = ApplicationWindows(item.Id, visibleOnly: true);
                opened = windows.FirstOrDefault(w => !before.Contains(w) || w == GetForegroundWindow());
                if (opened == 0) Thread.Sleep(100);
            }
            Program.Check(opened != 0, "Windows did not present " + item.Label);
            if (!before.Contains(opened))
            {
                Program.Check(PostMessageW(opened, 0x10, 0, 0), "Close request failed for " + item.Label);
                timer.Restart();
                while (IsWindowVisible(opened) && timer.Elapsed < TimeSpan.FromSeconds(4))
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }
                Program.Check(!IsWindowVisible(opened), "New test window remained open for " + item.Label);
            }
            Console.WriteLine("PASS " + item.Label);
        }
        dock.Close();
    }
}
