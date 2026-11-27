using System.Globalization;

namespace Personalization;

public sealed class ThemeCatalog(string appData)
{
    public const string DefaultId = "minimal-dark";
    private static readonly Dictionary<string, Theme> BuiltIns = LoadBuiltIns();
    public static Theme Default => BuiltIns[DefaultId];

    public static Theme Parse(ReadOnlySpan<byte> json)
    {
        var theme = LocalData.Parse<Theme>(json);
        LocalData.RequireId(theme.Id);
        if (theme.Version != 1 || theme.Name is not { Length: >= 1 and <= 64 } ||
            string.IsNullOrWhiteSpace(theme.Name) || theme.Name.Any(c =>
                char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format))
            throw new InvalidDataException("Nombre o versión de tema no válidos.");
        var c = theme.Colors;
        if (!new[] { c.Background, c.Button, c.Text, c.Border }.All(s =>
            s is { Length: 7 } && s[0] == '#' && s.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEFabcdef") < 0))
            throw new InvalidDataException("Cada color debe usar el formato #RRGGBB.");
        var layout = theme.Layout;
        if (layout.IconSize is < 16 or > 48 || layout.ButtonWidth is < 56 or > 144 ||
            layout.Padding is < 0 or > 16 || layout.Gap is < 0 or > 16 || layout.BottomMargin is < 0 or > 32)
            throw new InvalidDataException("Dimensiones fuera del intervalo admitido.");
        return theme;
    }

    private static Dictionary<string, Theme> LoadBuiltIns()
    {
        var themes = new Dictionary<string, Theme>(StringComparer.Ordinal);
        var assembly = typeof(ThemeCatalog).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Themes.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var theme = Parse(bytes.ToArray());
            if (resource != $"Themes.{theme.Id}.json") throw new InvalidDataException("Id de tema integrado incorrecto.");
            themes.Add(theme.Id, theme);
        }
        return themes;
    }

    public Theme Load(string id)
    {
        LocalData.RequireId(id);
        if (BuiltIns.TryGetValue(id, out var theme)) return theme;
        var dir = LocalData.DirectoryPath(appData, "Themes");
        theme = Parse(LocalData.Read(Path.Combine(dir, $"theme-{id}.json")));
        if (theme.Id != id) throw new InvalidDataException("El identificador del tema difiere del nombre de archivo.");
        return theme;
    }

    public Theme Resolve(Profile profile)
    {
        try { return Load(profile.ThemeId); }
        catch (Exception ex) when (LocalData.IsDataError(ex)) { return Default; }
    }

    public IReadOnlyList<Theme> Available()
    {
        var themes = new List<Theme>(BuiltIns.Values);
        try
        {
            var dir = LocalData.DirectoryPath(appData, "Themes");
            foreach (var file in Directory.EnumerateFiles(dir, "theme-*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var id = name["theme-".Length..];
                if (BuiltIns.ContainsKey(id) || !LocalData.ValidId(id)) continue;
                try { themes.Add(Load(id)); }
                catch (Exception ex) when (LocalData.IsDataError(ex)) { }
            }
        }
        catch (Exception ex) when (LocalData.IsDataError(ex)) { }
        return themes.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
    }
}
