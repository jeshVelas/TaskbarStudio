using System.Diagnostics;
using Personalization;
using WindowTracking;

namespace FloatingDock;

// A source supplies stable identity, presentation data and an activation command.
// The surface never interprets an executable path or a window handle.
internal interface IBarItem
{
    string Id { get; }
    string Label { get; }
    bool IsVisible(Profile profile);
    Bitmap CreateIcon(IconRenderer renderer, Profile profile, Theme theme, int size);
    bool CanActivate => true;
    void Activate();
}

internal sealed record LauncherItem(string Id, string Label, DockIcon Icon, string Target) : IBarItem
{
    internal static IReadOnlyList<IBarItem> BuiltIns { get; } = [
        new LauncherItem("notepad", "Bloc de notas", DockIcon.Notepad, Path.Combine(Environment.SystemDirectory, "notepad.exe")),
        new LauncherItem("calculator", "Calculadora", DockIcon.Calculator, Path.Combine(Environment.SystemDirectory, "calc.exe")),
        new LauncherItem("explorer", "Explorador", DockIcon.Explorer,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
    ];

    public bool IsVisible(Profile profile) => Icon switch
    {
        DockIcon.Notepad => profile.Shortcuts.Notepad,
        DockIcon.Calculator => profile.Shortcuts.Calculator,
        DockIcon.Explorer => profile.Shortcuts.Explorer,
        _ => false
    };

    public Bitmap CreateIcon(IconRenderer renderer, Profile profile, Theme theme, int size)
        => renderer.Render(Icon, profile, theme, size);

    public void Activate() => Process.Start(new ProcessStartInfo(Target) { UseShellExecute = true })?.Dispose();
}

// The visual item delegates activation; Win32 stays in WindowActionService.
internal sealed class WindowBarItem : IBarItem, IDisposable
{
    private Bitmap? applicationIcon;
    private readonly Action<TrackedWindow> activate;

    internal WindowBarItem(TrackedWindow window, Action<TrackedWindow> activate)
    {
        Window = window;
        this.activate = activate ?? throw new ArgumentNullException(nameof(activate));
    }
    internal TrackedWindow Window { get; private set; }
    public string Id => $"window:{Window.Id.ToInt64():X}:{Window.ProcessId}";
    public string Label => string.IsNullOrWhiteSpace(Window.Title) ? Window.ProcessName ?? "Ventana" : Window.Title;
    public bool CanActivate => true;
    public bool IsVisible(Profile profile) => true;

    public Bitmap CreateIcon(IconRenderer renderer, Profile profile, Theme theme, int size)
    {
        if (applicationIcon is not null) return new Bitmap(applicationIcon, size, size);
        return WindowIconProvider.CreateFallback(size, theme, Window.IsMinimized);
    }

    internal bool Update(TrackedWindow window)
    {
        bool changed = Window != window;
        Window = window;
        return changed;
    }

    internal void SetIcon(Bitmap? icon)
    {
        applicationIcon?.Dispose();
        applicationIcon = icon;
    }

    public void Activate() => activate(Window);
    public void Dispose() => applicationIcon?.Dispose();
}
