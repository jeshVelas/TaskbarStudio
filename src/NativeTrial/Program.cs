namespace NativeTrial;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskbarStudio", "NativeTrial");
        try
        {
            Directory.CreateDirectory(directory);
            using var instance = new FileStream(Path.Combine(directory, "session.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Application.Run(new TrialForm(directory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("No se pudo abrir la sesión de prueba. Compruebe si ya está abierta.\n" + ex.Message,
                "Taskbar Studio");
        }
    }
}

internal sealed class TrialForm : Form
{
    private readonly ShellTaskbar shell;
    private readonly Trial trial;
    private readonly Label state = new() { AutoSize = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(480, 0) };
    private readonly FlowLayoutPanel panel = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(14), Dock = DockStyle.Fill };
    private readonly Button enable, disable, restore, keep;
    private bool liveTrial;

    public TrialForm(string directory)
    {
        Text = "Taskbar Studio · Prueba de ocultación";
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MaximizeBox = false;
        shell = new ShellTaskbar(() => Handle);
        trial = new Trial(shell, directory);
        Controls.Add(panel);
        panel.Controls.Add(state);
        panel.Controls.Add(status);
        Add("Consultar estado", () => Inspect());
        enable = Add("Activar ocultación", () => Apply(true));
        disable = Add("Desactivar ocultación", () => Apply(false));
        restore = Add("Restaurar estado anterior", () => { trial.Restore(); liveTrial = false; Inspect(); });
        keep = Add("Conservar estado actual", () => { trial.Keep(); liveTrial = false; Inspect(); });
        Add("Cerrar", Close);
        Shown += (_, _) => Inspect();
        Activated += (_, _) => { if (Visible) Inspect(); };
        FormClosing += (_, _) =>
        {
            if (!liveTrial) return;
            try { trial.Restore(); }
            catch (Exception ex) { MessageBox.Show("Recuperación pendiente al volver a abrir.\n" + ex.Message, Text); }
        };
    }

    private Button Add(string label, Action action)
    {
        var button = new Button { Text = label, AutoSize = true };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { liveTrial = false; Inspect(); status.Text = ex.Message; }
        };
        panel.Controls.Add(button);
        return button;
    }

    private void Apply(bool target)
    {
        trial.Apply(target);
        liveTrial = trial.Original.HasValue;
        Inspect();
    }

    private void Inspect()
    {
        enable.Enabled = disable.Enabled = restore.Enabled = keep.Enabled = false;
        state.Text = "Ocultación automática · sin confirmar";
        try
        {
            bool? original = trial.Original;
            keep.Enabled = original.HasValue;
            var (current, canVerify) = shell.Inspect();
            state.Text = "Ocultación automática · " + (current ? "activada" : "desactivada");
            if (!canVerify)
            {
                liveTrial = false;
                status.Text = ShellTaskbar.PositionLimitation +
                    (original.HasValue ? " Cerrar mantiene la recuperación pendiente." : "");
                return;
            }
            if (original.HasValue && current == original) liveTrial = false;
            status.Text = original.HasValue
                ? $"Estado anterior: {(original.Value ? "activada" : "desactivada")}. " +
                    (liveTrial ? "Cerrar restaurará la prueba." : "Elija restaurar o conservar. Cerrar deja la decisión pendiente.")
                : "Elija una operación para probar y verificar.";
            enable.Enabled = !original.HasValue && !current;
            disable.Enabled = !original.HasValue && current;
            restore.Enabled = original.HasValue;
        }
        catch (InvalidDataException ex) { status.Text = ex.Message; keep.Enabled = true; }
        catch (Exception ex) { status.Text = ex.Message; }
    }
}
