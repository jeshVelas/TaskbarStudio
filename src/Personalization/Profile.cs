namespace Personalization;

// Keys refer only to built-in launchers. Executable paths belong to the dock.
public sealed record DockShortcuts(bool Notepad, bool Calculator, bool Explorer)
{
    public static DockShortcuts All { get; } = new(true, true, true);
}

public sealed record Profile(int Version, string Id, string ThemeId)
{
    public const string DefaultId = "default";
    // Additive defaults also read the three-field profiles from iteration 1.
    public string Name { get; init; } = Id == DefaultId ? "Predeterminado" : Id;
    public bool ShowLabels { get; init; } = true;
    public DockShortcuts Shortcuts { get; init; } = DockShortcuts.All;
    public IconReferences Icons { get; init; } = new();
    public static Profile Default(string id = DefaultId) => new(1, id, ThemeCatalog.DefaultId);

    internal static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name != name.Trim() ||
            name.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is
                System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.LineSeparator
                or System.Globalization.UnicodeCategory.ParagraphSeparator))
            throw new InvalidDataException("Use un nombre de 1 a 64 caracteres visibles, sin espacios en los extremos.");
    }

    internal void Validate()
    {
        LocalData.RequireId(Id);
        LocalData.RequireId(ThemeId);
        ValidateName(Name);
        if (Icons is null) throw new InvalidDataException("Referencias de icono no válidas.");
        if (Version != 1 || Shortcuts is null || Shortcuts == new DockShortcuts(false, false, false))
            throw new InvalidDataException("El perfil debe usar la versión 1 e incluir al menos un acceso.");
    }
}
