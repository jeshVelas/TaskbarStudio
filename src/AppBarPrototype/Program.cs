using System.Drawing;
using System.Runtime.InteropServices;

namespace AppBarPrototype;

internal static class Program
{
    internal static void WriteDiagnostic(string message)
    {
        try { Console.WriteLine(message); } catch (IOException) { /* WinExe may have no attached console. */ }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase) && AttachConsole(unchecked((uint)-1)))
        {
            try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); }
            catch (IOException) { }
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new PrototypeForm(args.Contains("--smoke", StringComparer.OrdinalIgnoreCase)));
    }

    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}

internal sealed class PrototypeForm : Form
{
    private const int CallbackMessage = 0x8000 + 0x53;
    private readonly bool smoke;
    private readonly Label status;
    private readonly Button toggle;
    private readonly AppBarRegistration appBar;
    private bool registered, closing;
    private Rectangle workBefore;
    private Rectangle taskbarBefore;
    private uint stateBefore;

    public PrototypeForm(bool smoke)
    {
        this.smoke = smoke;
        Text = "Taskbar Studio · AppBar Prototype";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = true;
        KeyPreview = true;
        Width = 390; Height = 175;
        var info = new Label { AutoSize = false, Left = 16, Top = 14, Width = 345, Height = 58,
            Text = "Experimento reversible: registra una barra estrecha en el borde izquierdo. No modifica ni oculta la barra de Windows." };
        status = new Label { AutoSize = false, Left = 16, Top = 78, Width = 345, Height = 25, Text = "Sin registrar." };
        toggle = new Button { Left = 16, Top = 110, Width = 160, Height = 30, Text = "Registrar AppBar" };
        var exit = new Button { Left = 190, Top = 110, Width = 160, Height = 30, Text = "Salir (Esc)" };
        toggle.Click += (_, _) => ToggleRegistration(); exit.Click += (_, _) => Close();
        Controls.AddRange([info, status, toggle, exit]);
        appBar = new AppBarRegistration(new Win32AppBarPlatform(), Handle, AppBarEdge.Left, LogicalToDeviceUnits(72));
        Shown += OnShown;
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (!smoke) return;
        workBefore = Win32AppBarPlatform.ReadWorkArea();
        var taskbarAvailableBefore = Win32AppBarPlatform.ReadTaskbar(Handle, out taskbarBefore, out stateBefore);
        if (!RegisterAppBar()) { Console.WriteLine("AppBar registration/position rejected by Windows."); Close(); return; }
        Program.WriteDiagnostic($"Registered=true; work-before={workBefore}; work-during={Win32AppBarPlatform.ReadWorkArea()}; bar-before={(taskbarAvailableBefore ? taskbarBefore.ToString() : "unavailable")}; state={stateBefore}; appbar={appBar.ActualBounds}");
        var timer = new System.Windows.Forms.Timer { Interval = 1800 };
        timer.Tick += (_, _) => { timer.Stop(); var removed = UnregisterAppBar(); var workAfter = Win32AppBarPlatform.ReadWorkArea(); var taskbarAvailableAfter = Win32AppBarPlatform.ReadTaskbar(Handle, out var barAfter, out var stateAfter); Program.WriteDiagnostic($"Removed={removed}; work-after={workAfter}; bar-after={(taskbarAvailableAfter ? barAfter.ToString() : "unavailable")}; state-after={stateAfter}; work-restored={workAfter == workBefore}; original-taskbar-unchanged={taskbarAvailableBefore && taskbarAvailableAfter && barAfter == taskbarBefore && stateAfter == stateBefore}"); timer.Dispose(); if (removed) Close(); };
        timer.Start();
    }

    private bool RegisterAppBar()
    {
        if (registered) return true;
        try
        {
            if (!appBar.Start(CallbackMessage, Screen.FromHandle(Handle).Bounds))
            { status.Text = "Windows rechazó el registro o la geometría."; return false; }
            registered = true; toggle.Text = "Retirar AppBar";
            status.Text = $"Registrada: {appBar.ActualBounds}";
            return true;
        }
        catch (Exception ex)
        {
            registered = appBar.IsRegistered;
            toggle.Text = registered ? "Retirar AppBar" : "Registrar AppBar";
            status.Text = registered ? $"Error; AppBar sigue registrada, reintente la retirada: {ex.Message}" : $"Error al registrar: {ex.Message}";
            return false;
        }
    }

    private bool UnregisterAppBar()
    {
        if (!registered) return true;
        try
        {
            if (!appBar.Stop()) { status.Text = "No se confirmó ABM_REMOVE; reintente antes de cerrar."; return false; }
            registered = false; toggle.Text = "Registrar AppBar"; status.Text = "Retirada; el Shell libera la reserva."; return true;
        }
        catch (Exception ex) { status.Text = $"Error al retirar: {ex.Message}"; return false; }
    }

    private void ToggleRegistration() { if (registered) UnregisterAppBar(); else RegisterAppBar(); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == CallbackMessage)
        {
            switch ((int)m.WParam)
            {
                case 1: // ABN_POSCHANGED
                case 0: // ABN_STATECHANGE
                    if (registered) Reposition();
                    break;
                case 2: // ABN_FULLSCREENAPP; AppBar itself is never topmost.
                    if (m.LParam != 0) status.Text = "Aplicación a pantalla completa detectada; Windows puede cubrir una AppBar flotante.";
                    else status.Text = $"AppBar registrada: {appBar.ActualBounds}";
                    break;
            }
        }
        else if (m.Msg == 0x0047 && registered) // WM_WINDOWPOSCHANGED
            appBar.NotifyWindowPositionChanged();
        else if ((m.Msg == 0x007E || m.Msg == 0x02E0 || m.Msg == 0x001A) && registered) // display/DPI/settings
            BeginInvoke((Action)Reposition);
        base.WndProc(ref m);
    }

    private void Reposition()
    {
        if (!registered || IsDisposed || !IsHandleCreated) return;
        appBar.SetThickness(LogicalToDeviceUnits(72));
        if (appBar.Reposition(Screen.FromHandle(Handle).Bounds)) status.Text = $"Registrada: {appBar.ActualBounds}";
        else status.Text = "Windows no aceptó la nueva geometría; la barra original sigue intacta.";
    }

    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } base.OnKeyDown(e); }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (closing) { base.OnFormClosing(e); return; }
        if (registered && !UnregisterAppBar()) { e.Cancel = true; MessageBox.Show(this, status.Text, "No se pudo retirar AppBar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        else { closing = true; appBar.Dispose(); }
        base.OnFormClosing(e);
    }
}
