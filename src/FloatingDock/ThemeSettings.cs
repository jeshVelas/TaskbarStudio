using Personalization;

namespace FloatingDock;

internal sealed class ThemeSettings : Form
{
    private readonly ComboBox choices = new() { DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 240, AccessibleName = "Tema activo", DisplayMember = nameof(Theme.Name) };
    private readonly ComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 240, AccessibleName = "Perfil", DisplayMember = nameof(Profile.Name) };
    private readonly CheckBox labels = new() { Text = "Mostrar &nombres en el dock", AutoSize = true };
    private readonly CheckBox notepad = new() { Text = "Bloc de notas", AutoSize = true };
    private readonly CheckBox calculator = new() { Text = "Calculadora", AutoSize = true };
    private readonly CheckBox explorer = new() { Text = "Explorador", AutoSize = true };
    private readonly Button remove = new() { Text = "&Eliminar…", AutoSize = true };
    private readonly ProfileDraft draft;
    private readonly IconRenderer iconRenderer;
    private readonly ComboBox iconApp = new() { DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 116, AccessibleName = "Aplicación del icono" };
    private readonly PictureBox preview = new() { Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom,
        AccessibleName = "Vista previa del icono", AccessibleRole = AccessibleRole.Graphic };
    private readonly Button restoreIcon = new() { Text = "Res&taurar icono", AutoSize = true };
    private DockIcon SelectedApp => (DockIcon)iconApp.SelectedIndex;
    private bool refreshingProfiles;

    internal ThemeSettings(IReadOnlyList<Theme> available, ProfileDraft draft, IconResources resources, Action save)
    {
        this.draft = draft;
        iconRenderer = new IconRenderer(resources);
        Text = "Configuración del dock";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var content = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12), Dock = DockStyle.Fill };
        content.Controls.Add(new Label { Text = "&Perfil", AutoSize = true });
        content.Controls.Add(profiles);
        var manage = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var create = new Button { Text = "&Crear…", AutoSize = true };
        var rename = new Button { Text = "&Renombrar…", AutoSize = true };
        manage.Controls.AddRange([create, rename, remove]);
        content.Controls.Add(manage);
        content.Controls.Add(new Label { Text = "&Tema", AutoSize = true });
        var repeatedNames = available.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        choices.FormattingEnabled = true;
        choices.Format += (_, e) =>
        {
            if (e.ListItem is Theme item && repeatedNames.Contains(item.Name))
                e.Value = $"{item.Name} ({item.Id})";
        };
        choices.Items.AddRange(available.ToArray());
        content.Controls.Add(choices);
        content.Controls.Add(labels);
        var shortcuts = new GroupBox { Text = "Accesos directos", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        checks.Controls.AddRange([notepad, calculator, explorer]);
        shortcuts.Controls.Add(checks);
        content.Controls.Add(shortcuts);
        foreach (var check in new[] { notepad, calculator, explorer })
            check.CheckedChanged += (_, _) => UpdateShortcutChoices();

        var iconGroup = new GroupBox { Text = "Icono de la aplicación", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var iconRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        var chooseIcon = new Button { Text = "&Seleccionar…", AutoSize = true, AccessibleName = "Seleccionar icono" };
        iconApp.Items.AddRange(["Bloc de notas", "Calculadora", "Explorador"]);
        iconApp.SelectedIndex = 0;
        iconRow.Controls.AddRange([iconApp, preview, chooseIcon, restoreIcon]);
        iconGroup.Controls.Add(iconRow);
        content.Controls.Add(iconGroup);
        iconApp.SelectedIndexChanged += (_, _) => UpdatePreview();
        choices.SelectedIndexChanged += (_, _) => UpdatePreview();
        restoreIcon.Click += (_, _) => { draft.SetIcon(SelectedApp, null); UpdatePreview(); };
        chooseIcon.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Title = "Seleccionar icono local",
                Filter = "Imágenes PNG e ICO (*.png;*.ico)|*.png;*.ico", DereferenceLinks = false, RestoreDirectory = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try { ImportIcon(picker.FileName); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            { MessageBox.Show(this, ex.Message, "No se pudo importar el icono"); }
        };
        RefreshProfiles();
        profiles.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshingProfiles && profiles.SelectedItem != null) SwitchProfile();
        };
        create.Click += (_, _) => EditName(createNew: true);
        rename.Click += (_, _) => EditName(createNew: false);
        remove.Click += (_, _) =>
        {
            if (MessageBox.Show(this, $"¿Eliminar el perfil «{draft.Selected.Name}» al aplicar?", "Eliminar perfil",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            draft.DeleteSelected();
            RefreshProfiles();
        };
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var apply = new Button { Text = "Aplicar", AutoSize = true };
        apply.Click += (_, _) =>
        {
            try
            {
                CaptureOptions();
                save();
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                MessageBox.Show(this, ex.Message, "No se pudieron guardar los perfiles");
            }
        };
        var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([apply, cancel]);
        content.Controls.Add(actions);
        Controls.Add(content);
        AcceptButton = apply;
        CancelButton = cancel;
        Shown += (_, _) => profiles.Focus();
    }

    private void CaptureOptions() => draft.Configure(((Theme)choices.SelectedItem!).Id, labels.Checked,
        new DockShortcuts(notepad.Checked, calculator.Checked, explorer.Checked));

    private void SwitchProfile()
    {
        CaptureOptions();
        draft.Select(((Profile)profiles.SelectedItem!).Id);
        ShowOptions();
    }

    private void RefreshProfiles()
    {
        refreshingProfiles = true;
        profiles.Items.Clear();
        profiles.Items.AddRange(draft.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        profiles.SelectedItem = draft.Selected;
        refreshingProfiles = false;
        ShowOptions();
    }

    private void ShowOptions()
    {
        var p = draft.Selected;
        choices.SelectedItem = choices.Items.Cast<Theme>().FirstOrDefault(t => t.Id == p.ThemeId) ?? ThemeCatalog.Default;
        labels.Checked = p.ShowLabels;
        notepad.Checked = p.Shortcuts.Notepad;
        calculator.Checked = p.Shortcuts.Calculator;
        explorer.Checked = p.Shortcuts.Explorer;
        remove.Enabled = p.Id != Profile.DefaultId;
        UpdatePreview();
    }

    internal void ImportIcon(string path)
    {
        var pixels = IconImage.Import(path);
        draft.SetIcon(SelectedApp, pixels);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (choices.SelectedItem is not Theme selectedTheme) return;
        var previous = preview.Image;
        preview.Image = iconRenderer.Render(SelectedApp, draft.Selected, selectedTheme, LogicalToDeviceUnits(40), draft);
        previous?.Dispose();
        restoreIcon.Enabled = draft.Selected.Icons.Get(SelectedApp) != null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            preview.Image?.Dispose();
            preview.Image = null;
        }
        base.Dispose(disposing);
    }

    // Keep at least one launcher reachable, including during keyboard editing.
    private void UpdateShortcutChoices()
    {
        notepad.Enabled = !notepad.Checked || calculator.Checked || explorer.Checked;
        calculator.Enabled = !calculator.Checked || notepad.Checked || explorer.Checked;
        explorer.Enabled = !explorer.Checked || notepad.Checked || calculator.Checked;
    }

    private void EditName(bool createNew)
    {
        using var prompt = new Form { Text = createNew ? "Crear perfil" : "Renombrar perfil",
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent };
        var body = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(12) };
        var input = new TextBox { AccessibleName = "Nombre del perfil", Width = 240, MaxLength = 64,
            Text = createNew ? "" : draft.Selected.Name };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var ok = new Button { Text = "Aceptar", AutoSize = true };
        var cancel = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            try
            {
                CaptureOptions();
                if (createNew) draft.Create(input.Text.Trim());
                else draft.Rename(input.Text.Trim());
                prompt.DialogResult = DialogResult.OK;
            }
            catch (InvalidDataException ex) { MessageBox.Show(prompt, ex.Message, prompt.Text); input.Focus(); }
        };
        buttons.Controls.AddRange([ok, cancel]);
        body.Controls.AddRange([new Label { Text = "&Nombre", AutoSize = true }, input, buttons]);
        prompt.Controls.Add(body);
        prompt.AcceptButton = ok;
        prompt.CancelButton = cancel;
        prompt.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        if (prompt.ShowDialog(this) == DialogResult.OK) RefreshProfiles();
    }
}
