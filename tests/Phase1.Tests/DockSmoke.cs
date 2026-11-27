using FloatingDock;
using Personalization;
using System.Diagnostics;

namespace Phase1.Tests;

internal static partial class DockSmoke
{
    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => Descendants(c).Prepend(c));

    internal static void Run(string dir)
    {
        var catalog = new ThemeCatalog(dir);
        var profiles = new ProfileStore(dir);
        using (var dock = new DockForm(false, catalog, profiles))
        {
            dock.Show();
            var buttons = Descendants(dock).OfType<Button>().OrderBy(b => b.TabIndex).ToArray();
            Program.Check(buttons.Select(b => b.Text).SequenceEqual(new[] { "Bloc de notas","Calculadora","Explorador","Cerrar" }), "Dock buttons");
            Program.Check(dock.CancelButton == buttons[^1], "Escape");
            var previousIcon = buttons[0].Image!.Width;
            var previousWidth = buttons[0].MinimumSize.Width;
            var previousGap = buttons[0].Margin.Horizontal;
            var previousColor = buttons[0].BackColor;
            Select(dock, "minimal-light", accept: false);
            Program.Check(!Directory.EnumerateFileSystemEntries(dir).Any() && buttons[0].Image!.Width == previousIcon, "Cancel wrote or applied theme");
            Select(dock, "minimal-light", accept: true);
            Program.Check(profiles.Load().ThemeId == "minimal-light", "Save selection");
            Program.Check(buttons[0].MinimumSize.Width < previousWidth && buttons[0].Margin.Horizontal < previousGap &&
                buttons[0].Image!.Width < previousIcon, "Live dimensions");
            Program.Check(SystemInformation.HighContrast || buttons[0].BackColor != previousColor, "Live colors");
            buttons[^1].PerformClick();
            Program.Check(!dock.Visible, "Close");
        }
        using var reopened = new DockForm(false, catalog, new ProfileStore(dir));
        reopened.Show();
        var first = Descendants(reopened).OfType<Button>().First();
        Program.Check(first.Image!.Width == reopened.LogicalToDeviceUnits(catalog.Load("minimal-light").Layout.IconSize), "Restored on startup");
        Select(reopened, ThemeCatalog.DefaultId, accept: true);
        Program.Check(profiles.Load() == Profile.Default(), "Restore default");
        reopened.Close();
    }

    private static void Select(DockForm dock, string id, bool accept)
        => Edit(dock, accept, dialog => SelectTheme(dialog, id));

    private static void SelectTheme(ThemeSettings dialog, string id)
    {
        var choices = Descendants(dialog).OfType<ComboBox>().Single(c => c.AccessibleName == "Tema activo");
        choices.SelectedItem = choices.Items.Cast<Theme>().Single(t => t.Id == id);
    }

    private static void Edit(DockForm dock, bool accept, Action<ThemeSettings> change)
    {
        dock.BeginInvoke(() =>
        {
            var dialog = dock.OwnedForms.OfType<ThemeSettings>().Single();
            change(dialog);
            (accept ? dialog.AcceptButton : dialog.CancelButton)!.PerformClick();
        });
        dock.ContextMenuStrip!.Items[0].PerformClick();
        Program.Check(dock.OwnedForms.Length == 0, "Settings dialog still open");
    }

    internal static void Profiles(string dir)
    {
        var store = new ProfileStore(dir);
        var catalog = new ThemeCatalog(dir);
        using (var dock = new DockForm(false, catalog, store))
        {
            dock.Show();
            Edit(dock, false, dialog =>
            {
                Program.Check(!Descendants(dialog).OfType<Button>().Single(b => b.Text == "&Eliminar…").Enabled, "Default deletable");
                NameProfile(dialog, "&Crear…", "Descartado");
                SelectTheme(dialog, "minimal-light");
            });
            Program.Check(!Directory.EnumerateFileSystemEntries(dir).Any(), "Cancel created a catalog");
            Edit(dock, true, dialog =>
            {
                NameProfile(dialog, "&Crear…", "Casa");
                NameProfile(dialog, "&Renombrar…", "Trabajo");
                SelectTheme(dialog, "minimal-light");
                foreach (var box in Descendants(dialog).OfType<CheckBox>().Where(c => c.Text != "Calculadora"))
                    box.Checked = false;
            });
            CheckCalculatorOnly(dock);
            Program.Check(store.LoadActive().Name == "Trabajo", "Renamed profile not active");
            dock.Close();
        }
        using var reopened = new DockForm(false, catalog, store);
        reopened.Show();
        CheckCalculatorOnly(reopened);
        void SwitchToDefault(ThemeSettings dialog)
        {
            var choices = Descendants(dialog).OfType<ComboBox>().Single(c => c.AccessibleName == "Perfil");
            choices.SelectedItem = choices.Items.Cast<Profile>().Single(p => p.Id == Profile.DefaultId);
        }
        Edit(reopened, false, SwitchToDefault);
        CheckCalculatorOnly(reopened);
        Program.Check(store.LoadActive().Name == "Trabajo", "Cancel switched active profile");
        Edit(reopened, true, SwitchToDefault);
        Program.Check(Descendants(reopened).OfType<Button>().Count(b => b.Visible) == 4 && store.LoadActive() == Profile.Default(),
            "Switch did not restore default launchers");
        reopened.Close();
    }

    private static void CheckCalculatorOnly(DockForm dock)
    {
        var visible = Descendants(dock).OfType<Button>().Where(b => b.Visible).ToArray();
        Program.Check(visible.Length == 2 && visible.Any(b => b.Text == "Cerrar") &&
            visible.Any(b => b.Text == "" && b.AccessibleName == "Calculadora" && b.Image != null),
            "Wrong launchers, captions or accessible names");
    }

    internal static void Icons(string dir)
    {
        var store = new ProfileStore(dir);
        var catalog = new ThemeCatalog(dir);
        string path = Path.Combine(dir, "test.png");
        File.WriteAllBytes(path, Program.SamplePng(Color.Magenta));
        static int Calculator(DockForm dock) => Program.Center(Descendants(dock).OfType<Button>()
            .Single(b => b.AccessibleName == "Calculadora").Image!);
        void Import(ThemeSettings dialog)
        {
            var apps = Descendants(dialog).OfType<ComboBox>().Single(c => c.AccessibleName == "Aplicación del icono");
            apps.SelectedItem = "Calculadora";
            dialog.ImportIcon(path);
            Program.Check(Program.Center(Descendants(dialog).OfType<PictureBox>().Single().Image!) == Color.Magenta.ToArgb(),
                "Import did not update preview");
        }
        int original;
        using (var dock = new DockForm(false, catalog, store))
        {
            dock.Show();
            original = Calculator(dock);
            Edit(dock, false, Import);
            Program.Check(Calculator(dock) == original && !Directory.Exists(Path.Combine(dir, "TaskbarStudio")), "Cancel wrote icon");
            Edit(dock, true, dialog => { NameProfile(dialog, "&Crear…", "Gaming"); Import(dialog); });
            Program.Check(Calculator(dock) == Color.Magenta.ToArgb(), "Icon did not change live");
            File.Delete(path);
            dock.Close();
        }
        VerifyInFreshProcess(dir);
        using var reopened = new DockForm(false, catalog, new ProfileStore(dir));
        reopened.Show();
        Program.Check(Calculator(reopened) == Color.Magenta.ToArgb(), "Lost icon after restart");
        void SelectProfile(ThemeSettings dialog, string name)
        {
            var profiles = Descendants(dialog).OfType<ComboBox>().Single(c => c.AccessibleName == "Perfil");
            profiles.SelectedItem = profiles.Items.Cast<Profile>().Single(p => p.Name == name);
        }
        Edit(reopened, true, dialog => SelectProfile(dialog, "Predeterminado"));
        Program.Check(Calculator(reopened) == original, "Default profile inherited custom icon");
        Edit(reopened, true, dialog => SelectProfile(dialog, "Gaming"));
        Program.Check(Calculator(reopened) == Color.Magenta.ToArgb(), "Switch lost custom icon");
        Edit(reopened, true, dialog =>
        {
            Descendants(dialog).OfType<ComboBox>().Single(c => c.AccessibleName == "Aplicación del icono").SelectedItem = "Calculadora";
            Descendants(dialog).OfType<Button>().Single(b => b.Text == "Res&taurar icono").PerformClick();
        });
        Program.Check(Calculator(reopened) == original && store.LoadActive().Icons.Calculator == null, "Restore failed");
        reopened.Close();
    }

    private static void VerifyInFreshProcess(string dir)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--verify-reopened-icon");
        start.ArgumentList.Add(dir);
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(10000))
        {
            process.Kill();
            process.WaitForExit();
            throw new TimeoutException("The reopened dock did not finish its check.");
        }
        Program.Check(process.ExitCode == 0, "Dock did not restore its icon in a fresh process");
    }

    internal static void VerifyReopenedIcon(string dir)
    {
        using var dock = new DockForm(false, new ThemeCatalog(dir), new ProfileStore(dir));
        dock.Show();
        var button = Descendants(dock).OfType<Button>().Single(b => b.AccessibleName == "Calculadora");
        Program.Check(Program.Center(button.Image!) == Color.Magenta.ToArgb(), "Wrong icon after process restart");
        dock.Close();
    }

    private static void NameProfile(ThemeSettings dialog, string buttonText, string name)
    {
        dialog.BeginInvoke(() =>
        {
            var prompt = dialog.OwnedForms.Single();
            Descendants(prompt).OfType<TextBox>().Single().Text = name;
            prompt.AcceptButton!.PerformClick();
        });
        Descendants(dialog).OfType<Button>().Single(b => b.Text == buttonText).PerformClick();
    }
}
