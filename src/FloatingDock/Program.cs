using Personalization;
using WindowTracking;
using WindowActions;

namespace FloatingDock;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Application.Run(new DockForm(args.Contains("--load-test"), new ThemeCatalog(appData), new ProfileStore(appData)));
    }
}

internal sealed class DockForm : Form
{
    private readonly List<(IBarItem Item, BarButton Button)> launchers = [];
    private readonly Dictionary<WindowIdentity, WindowEntry> windows = [];
    private readonly FlowLayoutPanel row;
    private readonly BarButton close;
    private readonly Panel windowSeparator = new() { Visible = false, TabStop = false };
    private readonly Label windowHeader = new() { Text = "Ventanas abiertas", Visible = false,
        AutoSize = true, AccessibleName = "Ventanas abiertas" };
    private readonly Label grip = new() { Text = "⠿", TextAlign = ContentAlignment.MiddleCenter,
        Cursor = Cursors.SizeAll, AccessibleName = "Mover barra", AccessibleRole = AccessibleRole.Grip };
    private readonly ContextMenuStrip settingsMenu = new();
    private readonly ThemeCatalog themes;
    private readonly ProfileStore profiles;
    private readonly IconRenderer icons;
    private readonly WindowTracker windowTracker;
    private readonly IWindowActionService windowActions;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<WindowIdentity, byte> actionsInFlight = new();
    private readonly CancellationTokenSource iconLoads = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Bitmap, byte> pendingWindowIcons = new();
    private readonly object iconGate = new();
    private int renderedSize;
    private readonly ToolTip toolTips = new();
    private Profile profile;
    private Theme theme;
    private bool highContrast;
    private bool repositionPending;
    private bool manuallyPositioned;
    private Point? dragOffset;
    private volatile bool stopping;
    private int activeIconLoads;

    private readonly record struct WindowIdentity(nint Id, int ProcessId);
    private sealed record WindowEntry(WindowBarItem Item, BarButton Button, ContextMenuStrip Menu);

    public DockForm(bool loadTest, ThemeCatalog themes, ProfileStore profiles, IReadOnlyList<IBarItem>? items = null,
        WindowTracker? windowTracker = null, IWindowActionService? windowActions = null)
    {
        this.themes = themes;
        this.profiles = profiles;
        this.windowTracker = windowTracker ?? new WindowTracker();
        this.windowActions = windowActions ?? new WindowActionService();
        icons = new IconRenderer(profiles.Icons);
        profile = profiles.LoadActive();
        theme = themes.Resolve(profile);
        Text = "Taskbar Studio · Dock";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        DoubleBuffered = true;
        row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        Controls.Add(row);
        row.Controls.Add(grip);
        toolTips.SetToolTip(grip, "Arrastre para mover la barra · Alt + flechas");
        grip.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            dragOffset = PointToClient(grip.PointToScreen(e.Location));
            grip.Capture = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (dragOffset is not Point offset || !grip.Capture) return;
            var cursor = grip.PointToScreen(e.Location);
            MoveBar(cursor - (Size)offset, Screen.FromPoint(cursor).WorkingArea);
        };
        grip.MouseUp += (_, _) => grip.Capture = false;
        grip.MouseCaptureChanged += (_, _) => { if (!grip.Capture) dragOffset = null; };
        items ??= LauncherItem.BuiltIns;
        int count = loadTest && items.Count > 0 ? 20 : items.Count;
        for (int index = 0; index < count; index++)
        {
            var item = items[index % items.Count];
            var button = new BarButton { Name = item.Id, TabIndex = index,
                TextImageRelation = TextImageRelation.ImageAboveText, AccessibleName = item.Label };
            button.Click += (_, _) => ActivateItem(item);
            toolTips.SetToolTip(button, item.Label);
            launchers.Add((item, button));
            row.Controls.Add(button);
            if (loadTest && (index + 1) % 5 == 0) row.SetFlowBreak(button, true);
        }
        windowSeparator.Width = LogicalToDeviceUnits(1);
        windowSeparator.Height = LogicalToDeviceUnits(28);
        windowSeparator.Margin = new Padding(LogicalToDeviceUnits(4), LogicalToDeviceUnits(8), LogicalToDeviceUnits(4), LogicalToDeviceUnits(8));
        row.Controls.Add(windowSeparator);
        windowHeader.Margin = new Padding(LogicalToDeviceUnits(4), LogicalToDeviceUnits(4), LogicalToDeviceUnits(4), 0);
        row.Controls.Add(windowHeader);
        close = new BarButton { Text = "Cerrar", AccessibleName = "Cerrar", TabIndex = count };
        close.Click += (_, _) => Close();
        row.Controls.Add(close);
        toolTips.SetToolTip(close, "Cerrar la barra (Esc)");
        AcceptButton = null;
        CancelButton = close;
        var settings = settingsMenu.Items.Add("Configurar perfiles y tema…", null, (_, _) => ConfigureTheme());
        ((ToolStripMenuItem)settings).ShortcutKeyDisplayString = "Ctrl+,";
        settingsMenu.Items.Add("Centrar abajo", null, (_, _) => { manuallyPositioned = false; PositionDock(); });
        settingsMenu.Items.Add("Cerrar", null, (_, _) => Close());
        ContextMenuStrip = settingsMenu;
        Load += (_, _) => ApplyTheme();
        this.windowTracker.Changed += OnTrackedWindowsChanged;
        this.windowTracker.TrackingError += OnTrackingError;
        FormClosing += (_, _) => StopWindowTracking();
        ActiveControl = launchers.Where(entry => entry.Item.IsVisible(profile)).Select(entry => entry.Button).FirstOrDefault() ?? close;
        Shown += (_, _) => { PositionDock(); this.windowTracker.Start(); };
        DpiChanged += (_, _) => BeginInvoke(ApplyTheme);
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BarAppearance.RequestRoundCorners(Handle);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Oemcomma)) { ConfigureTheme(); return true; }
        if ((keyData & Keys.Modifiers) == Keys.Alt)
        {
            int step = LogicalToDeviceUnits(12);
            var delta = (keyData & Keys.KeyCode) switch {
                Keys.Left => new Size(-step, 0), Keys.Right => new Size(step, 0),
                Keys.Up => new Size(0, -step), Keys.Down => new Size(0, step), _ => Size.Empty
            };
            if (!delta.IsEmpty)
            {
                MoveBar(Location + delta, Screen.FromControl(this).WorkingArea);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ConfigureTheme()
    {
        try
        {
            var draft = profiles.BeginEdit();
            using var dialog = new ThemeSettings(themes.Available(), draft, profiles.Icons, () =>
            {
                var selectedTheme = themes.Load(draft.Selected.ThemeId);
                profiles.Apply(draft);
                profile = draft.Selected;
                theme = selectedTheme;
                icons.Clear();
                renderedSize = 0;
            });
            if (dialog.ShowDialog(this) == DialogResult.OK) ApplyTheme();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            MessageBox.Show(this, ex.Message, "No se pudo abrir la configuración");
        }
    }

    private void ApplyTheme()
    {
        if (Disposing || IsDisposed) return;
        highContrast = SystemInformation.HighContrast;
        var palette = theme.Colors;
        var layout = theme.Layout;
        SuspendLayout();
        row.SuspendLayout();
        BackColor = highContrast ? SystemColors.Control : ColorTranslator.FromHtml(palette.Background);
        row.Padding = new Padding(LogicalToDeviceUnits(layout.Padding));
        int gap = LogicalToDeviceUnits(layout.Gap);
        var margin = new Padding(gap / 2, gap / 2, gap - gap / 2, gap - gap / 2);
        int iconSize = LogicalToDeviceUnits(layout.IconSize);
        int height = iconSize + LogicalToDeviceUnits(12) + (profile.ShowLabels ? Font.Height : 0);
        var surface = highContrast ? SystemColors.Control : ColorTranslator.FromHtml(palette.Button);
        var ink = highContrast ? SystemColors.ControlText : ColorTranslator.FromHtml(palette.Text);
        grip.Size = new Size(LogicalToDeviceUnits(18), height);
        grip.Margin = margin;
        grip.ForeColor = highContrast ? ink : BarAppearance.Mix(BackColor, ink, 70);
        foreach (var (item, button) in launchers)
        {
            button.Visible = item.IsVisible(profile);
            button.Text = profile.ShowLabels ? item.Label : string.Empty;
            int width = profile.ShowLabels ? Math.Max(LogicalToDeviceUnits(layout.ButtonWidth),
                TextRenderer.MeasureText(item.Label, Font).Width + LogicalToDeviceUnits(8)) : iconSize + LogicalToDeviceUnits(16);
            button.MinimumSize = new Size(profile.ShowLabels ? LogicalToDeviceUnits(layout.ButtonWidth) : width, 0);
            button.Size = new Size(width, height);
            Style(button, surface);
        }
        windowHeader.ForeColor = ink;
        windowSeparator.BackColor = highContrast ? SystemColors.ControlText : ColorTranslator.FromHtml(palette.Border);
        foreach (var entry in windows.Values) UpdateWindowEntry(entry, surface, ink, margin, height, iconSize, layout.ButtonWidth, refreshIcon: false);
        close.Size = new Size(TextRenderer.MeasureText(close.Text, Font).Width + LogicalToDeviceUnits(12), height);
        Style(close, BackColor);
        RefreshIcons();
        row.ResumeLayout(false);
        ResumeLayout(false);
        FitToArea(Screen.FromControl(this).WorkingArea);
        PositionDock();

        void Style(BarButton button, Color background)
        {
            button.FlatStyle = highContrast ? FlatStyle.Standard : FlatStyle.Flat;
            button.BackColor = background;
            button.ForeColor = ink;
            button.UseVisualStyleBackColor = highContrast;
            button.FlatAppearance.BorderSize = 0;
            var active = windows.Values.Any(entry => entry.Button == button && entry.Item.Window.IsActive);
            var border = active ? SystemColors.Highlight : button == close ? background : BarAppearance.Mix(background, ColorTranslator.FromHtml(palette.Border), 24);
            button.SetOutline(BackColor, border, ink);
            button.IsActiveWindow = active;
            button.IsMinimizedWindow = windows.Values.Any(entry => entry.Button == button && entry.Item.Window.IsMinimized);
            button.FlatAppearance.MouseOverBackColor = BarAppearance.Mix(background, ink, 10);
            button.FlatAppearance.MouseDownBackColor = BarAppearance.Mix(background, ink, 22);
            button.Margin = margin;
        }
    }

    private void ActivateItem(IBarItem item)
    {
        try { item.Activate(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "No se pudo abrir el acceso");
        }
    }

    private void RefreshIcons()
    {
        int size = LogicalToDeviceUnits(theme.Layout.IconSize);
        if (renderedSize == size) return;
        foreach (var (item, button) in launchers)
        {
            var previous = button.Image;
            button.Image = item.CreateIcon(icons, profile, theme, size);
            previous?.Dispose();
        }
        var layout = theme.Layout;
        int logicalHeight = size + LogicalToDeviceUnits(12) + (profile.ShowLabels ? Font.Height : 0);
        int gap = LogicalToDeviceUnits(layout.Gap);
        var margin = new Padding(gap / 2, gap / 2, gap - gap / 2, gap - gap / 2);
        var surface = highContrast ? SystemColors.Control : ColorTranslator.FromHtml(theme.Colors.Button);
        var ink = highContrast ? SystemColors.ControlText : ColorTranslator.FromHtml(theme.Colors.Text);
        foreach (var entry in windows.Values)
            UpdateWindowEntry(entry, surface, ink, margin, logicalHeight, size, layout.ButtonWidth, refreshIcon: true);
        renderedSize = size;
    }

    private void OnTrackedWindowsChanged(object? sender, WindowChange change)
    {
        if (stopping || IsDisposed || Disposing || !IsHandleCreated) return;
        try { BeginInvoke(() => ApplyWindowChange(change)); }
        catch (InvalidOperationException) { }
    }

    private void OnTrackingError(object? sender, Exception error)
    {
        // Avoid retaining or logging user window titles. The tracker continues to reconcile later.
        System.Diagnostics.Debug.WriteLine($"Window tracking error: {error.GetType().Name}");
    }

    private void ApplyWindowChange(WindowChange change)
    {
        if (stopping || IsDisposed || Disposing) return;
        foreach (var removed in change.Removed) RemoveWindow(removed);
        foreach (var updated in change.Updated) AddOrUpdateWindow(updated.Current);
        foreach (var added in change.Added) AddOrUpdateWindow(added);
        bool hasWindows = windows.Count != 0;
        windowSeparator.Visible = hasWindows;
        windowHeader.Visible = hasWindows;
        UpdateTabOrder();
        FitToArea(Screen.FromControl(this).WorkingArea);
        PositionDock();
    }

    private void AddOrUpdateWindow(TrackedWindow window)
    {
        var key = new WindowIdentity(window.Id, window.ProcessId);
        if (windows.TryGetValue(key, out var existing))
        {
            existing.Item.Update(window);
            UpdateWindowButton(existing);
            return;
        }

        var item = new WindowBarItem(window, target => QueueWindowAction(target, this.windowActions.Activate));
        var button = new BarButton { Name = $"window_{window.Id.ToInt64():X}_{window.ProcessId}",
            TabIndex = row.Controls.Count, TextImageRelation = TextImageRelation.ImageAboveText,
            AccessibleRole = AccessibleRole.PushButton };
        var menu = new ContextMenuStrip();
        var activate = (ToolStripMenuItem)menu.Items.Add("Activar");
        var minimize = (ToolStripMenuItem)menu.Items.Add("Minimizar");
        var restore = (ToolStripMenuItem)menu.Items.Add("Restaurar");
        var entry = new WindowEntry(item, button, menu);
        activate.Click += (_, _) => QueueWindowAction(entry.Item.Window, this.windowActions.Activate);
        minimize.Click += (_, _) => QueueWindowAction(entry.Item.Window, this.windowActions.Minimize);
        restore.Click += (_, _) => QueueWindowAction(entry.Item.Window, this.windowActions.Restore);
        menu.Opening += (_, e) =>
        {
            if (stopping) { e.Cancel = true; return; }
            minimize.Visible = !entry.Item.Window.IsMinimized;
            restore.Visible = entry.Item.Window.IsMinimized;
            bool busy = actionsInFlight.ContainsKey(new WindowIdentity(entry.Item.Window.Id, entry.Item.Window.ProcessId));
            activate.Enabled = minimize.Enabled = restore.Enabled = !busy;
        };
        button.ContextMenuStrip = menu;
        button.AccessibleName = $"Ventana: {item.Label}";
        button.Click += (_, _) => { if (item.CanActivate) ActivateItem(item); };
        windows.Add(key, entry);
        row.Controls.Add(button);
        UpdateWindowButton(entry);
        LoadWindowIcon(key, entry);
    }

    private void RemoveWindow(TrackedWindow window)
    {
        var key = new WindowIdentity(window.Id, window.ProcessId);
        if (!windows.Remove(key, out var entry)) return;
        entry.Button.Image?.Dispose();
        entry.Button.Image = null;
        entry.Item.Dispose();
        entry.Button.ContextMenuStrip = null;
        entry.Menu.Dispose();
        row.Controls.Remove(entry.Button);
        entry.Button.Dispose();
    }

    private void UpdateWindowButton(WindowEntry entry)
    {
        var item = entry.Item;
        var window = item.Window;
        var state = window.IsMinimized ? " (minimizada)" : window.IsActive ? " (activa)" : string.Empty;
        string title = item.Label;
        entry.Button.AccessibleName = $"Ventana: {title}";
        entry.Button.AccessibleDescription = $"{title}{state}. Solo visualización.";
        toolTips.SetToolTip(entry.Button, $"{title}{state}");
        entry.Button.Text = profile.ShowLabels ? Ellipsize(title + (window.IsMinimized ? " · minimizada" : string.Empty), LogicalToDeviceUnits(180)) : string.Empty;
        entry.Button.IsActiveWindow = window.IsActive;
        entry.Button.IsMinimizedWindow = window.IsMinimized;
        var palette = theme.Colors;
        var surface = highContrast ? SystemColors.Control : ColorTranslator.FromHtml(palette.Button);
        var ink = highContrast ? SystemColors.ControlText : ColorTranslator.FromHtml(palette.Text);
        int gap = LogicalToDeviceUnits(theme.Layout.Gap);
        int iconSize = LogicalToDeviceUnits(theme.Layout.IconSize);
        int height = iconSize + LogicalToDeviceUnits(12) + (profile.ShowLabels ? Font.Height : 0);
        var margin = new Padding(gap / 2, gap / 2, gap - gap / 2, gap - gap / 2);
        UpdateWindowEntry(entry, surface, ink, margin, height, iconSize, theme.Layout.ButtonWidth, refreshIcon: entry.Button.Image is null || entry.Button.Image.Width != iconSize);
    }

    private void UpdateWindowEntry(WindowEntry entry, Color surface, Color ink, Padding margin, int height,
        int iconSize, int buttonWidth, bool refreshIcon)
    {
        var window = entry.Item.Window;
        var button = entry.Button;
        string title = entry.Item.Label;
        button.Visible = true;
        button.Text = profile.ShowLabels ? Ellipsize(title + (window.IsMinimized ? " · minimizada" : string.Empty), LogicalToDeviceUnits(180)) : string.Empty;
        int width = profile.ShowLabels ? Math.Max(LogicalToDeviceUnits(buttonWidth),
            TextRenderer.MeasureText(button.Text, Font, new Size(LogicalToDeviceUnits(180), int.MaxValue), TextFormatFlags.EndEllipsis).Width + LogicalToDeviceUnits(8))
            : iconSize + LogicalToDeviceUnits(16);
        button.MinimumSize = new Size(profile.ShowLabels ? LogicalToDeviceUnits(buttonWidth) : width, 0);
        button.Size = new Size(width, height);
        button.Margin = margin;
        button.FlatStyle = highContrast ? FlatStyle.Standard : FlatStyle.Flat;
        button.BackColor = surface;
        button.ForeColor = window.IsMinimized && !highContrast ? BarAppearance.Mix(surface, ink, 62) : ink;
        button.UseVisualStyleBackColor = highContrast;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = BarAppearance.Mix(surface, ink, 10);
        button.FlatAppearance.MouseDownBackColor = BarAppearance.Mix(surface, ink, 22);
        var border = window.IsActive ? SystemColors.Highlight : BarAppearance.Mix(surface, ColorTranslator.FromHtml(theme.Colors.Border), 24);
        button.SetOutline(BackColor, border, ink);
        button.IsActiveWindow = window.IsActive;
        button.IsMinimizedWindow = window.IsMinimized;
        button.AccessibleName = $"Ventana: {title}";
        button.AccessibleDescription = $"{title}{(window.IsMinimized ? " (minimizada)" : window.IsActive ? " (activa)" : string.Empty)}. Solo visualización.";
        toolTips.SetToolTip(button, $"{title}{(window.IsMinimized ? " (minimizada)" : window.IsActive ? " (activa)" : string.Empty)}");
        if (refreshIcon)
        {
            var previous = button.Image;
            button.Image = entry.Item.CreateIcon(icons, profile, theme, iconSize);
            previous?.Dispose();
        }
    }

    private void LoadWindowIcon(WindowIdentity key, WindowEntry entry)
    {
        if (Interlocked.Increment(ref activeIconLoads) > 2)
        {
            Interlocked.Decrement(ref activeIconLoads);
            return;
        }
        nint handle = entry.Item.Window.Id;
        var token = iconLoads.Token;
        _ = Task.Run(() => WindowIconProvider.TryLoad(handle), token).ContinueWith(task =>
        {
            Interlocked.Decrement(ref activeIconLoads);
            if (task.Status != TaskStatus.RanToCompletion || task.Result is not Bitmap icon) return;
            lock (iconGate)
            {
                if (stopping || token.IsCancellationRequested) { icon.Dispose(); return; }
                pendingWindowIcons.TryAdd(icon, 0);
            }
            try
            {
                BeginInvoke(() =>
                {
                    if (!pendingWindowIcons.TryRemove(icon, out _)) return;
                    if (stopping || IsDisposed || Disposing || !windows.TryGetValue(key, out var current) || current != entry)
                    {
                        icon.Dispose();
                        return;
                    }
                    entry.Item.SetIcon(icon);
                    entry.Button.Image?.Dispose();
                    entry.Button.Image = null;
                    UpdateWindowButton(entry);
                    FitToArea(Screen.FromControl(this).WorkingArea);
                    PositionDock();
                });
            }
            catch (InvalidOperationException)
            {
                if (pendingWindowIcons.TryRemove(icon, out _)) icon.Dispose();
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void QueueWindowAction(TrackedWindow target, Func<TrackedWindow, WindowActionResult> action)
    {
        var key = new WindowIdentity(target.Id, target.ProcessId);
        if (stopping || !actionsInFlight.TryAdd(key, 0)) return;
        _ = Task.Run(() =>
        {
            try { return action(target); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or
                DllNotFoundException or EntryPointNotFoundException or System.Runtime.InteropServices.ExternalException)
            { return new WindowActionResult(WindowActionStatus.Failed); }
        }).ContinueWith(task =>
        {
            actionsInFlight.TryRemove(key, out _);
            if (task.Status != TaskStatus.RanToCompletion || stopping || !IsHandleCreated) return;
            try
            {
                BeginInvoke(() =>
                {
                    if (stopping || IsDisposed || Disposing) return;
                    if (task.Result.Status is WindowActionStatus.WindowUnavailable or WindowActionStatus.WindowChanged or WindowActionStatus.NotEligible)
                        windowTracker.Reconcile();
                    if ((task.Result.Status is WindowActionStatus.ForegroundDenied or WindowActionStatus.Failed) &&
                        windows.TryGetValue(key, out var current))
                    {
                        string message = task.Result.Status == WindowActionStatus.ForegroundDenied
                            ? "Windows no permitió cambiar el foco." : "No se pudo realizar la acción sobre esta ventana.";
                        toolTips.Show(message, current.Button, 2400);
                    }
                });
            }
            catch (InvalidOperationException) { }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private string Ellipsize(string value, int width)
    {
        if (TextRenderer.MeasureText(value, Font).Width <= width) return value;
        const string ellipsis = "…";
        int low = 0, high = value.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (TextRenderer.MeasureText(value[..mid] + ellipsis, Font).Width <= width) low = mid;
            else high = mid - 1;
        }
        return value[..low] + ellipsis;
    }

    private void UpdateTabOrder()
    {
        for (int index = 0; index < row.Controls.Count; index++) row.Controls[index].TabIndex = index;
    }

    private void StopWindowTracking()
    {
        lock (iconGate)
        {
            if (stopping) return;
            stopping = true;
            foreach (var icon in pendingWindowIcons.Keys)
                if (pendingWindowIcons.TryRemove(icon, out _)) icon.Dispose();
        }
        windowTracker.Changed -= OnTrackedWindowsChanged;
        windowTracker.TrackingError -= OnTrackingError;
        iconLoads.Cancel();
        windowTracker.Dispose();
    }

    private void PositionDock()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        Location = manuallyPositioned ? DockPlacement.Constrain(area, Size, Location) :
            DockPlacement.BottomCenter(area, Size, LogicalToDeviceUnits(theme.Layout.BottomMargin));
    }

    private void FitToArea(Rectangle area)
    {
        int widthLimit = Math.Min(area.Width, LogicalToDeviceUnits(760));
        int heightLimit = Math.Min(area.Height, LogicalToDeviceUnits(260));
        var preferred = row.GetPreferredSize(new Size(widthLimit, 0));
        ClientSize = new Size(Math.Min(widthLimit, preferred.Width), Math.Min(heightLimit, preferred.Height));
        row.PerformLayout();
    }

    private void MoveBar(Point requested, Rectangle area)
    {
        manuallyPositioned = true;
        if (area.Size != Screen.FromControl(this).WorkingArea.Size) FitToArea(area);
        Location = DockPlacement.Constrain(area, Size, requested);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is 0x0015 or 0x001A && IsHandleCreated && Visible &&
            highContrast != SystemInformation.HighContrast) ApplyTheme();
        if (message.Msg is 0x007E or 0x001A) QueuePosition(); // Display or desktop settings changed.
    }

    private void QueuePosition()
    {
        if (repositionPending || !IsHandleCreated || Disposing) return;
        repositionPending = true;
        BeginInvoke(() =>
        {
            repositionPending = false;
            if (IsDisposed || Disposing) return;
            FitToArea(Screen.FromControl(this).WorkingArea);
            PositionDock();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWindowTracking();
            iconLoads.Dispose();
            settingsMenu.Dispose();
            toolTips.Dispose();
            foreach (var (_, button) in launchers)
            {
                button.Image?.Dispose();
                button.Image = null;
            }
            foreach (var entry in windows.Values)
            {
                entry.Button.Image?.Dispose();
                entry.Button.Image = null;
                entry.Button.ContextMenuStrip = null;
                entry.Menu.Dispose();
                entry.Item.Dispose();
            }
            windows.Clear();
        }
        base.Dispose(disposing);
    }
}
