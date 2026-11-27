namespace Personalization;

internal sealed record ProfileSnapshot(int Version, string ActiveId, Profile[] Profiles);

public sealed class ProfileDraft
{
    internal ProfileSnapshot Original { get; private set; }
    private readonly Dictionary<string, Profile> profiles;
    private readonly Dictionary<string, byte[]> pendingIcons = new();
    internal IReadOnlyDictionary<string, byte[]> PendingIcons => pendingIcons;
    public byte[]? PendingPixels(string id) => pendingIcons.TryGetValue(id, out var bytes) ? bytes.ToArray() : null;
    public IReadOnlyCollection<Profile> Profiles => profiles.Values;
    public Profile Selected { get; private set; }

    internal ProfileDraft(ProfileSnapshot snapshot)
    {
        Original = snapshot;
        profiles = snapshot.Profiles.ToDictionary(p => p.Id);
        Selected = profiles[snapshot.ActiveId];
    }

    public void Select(string id)
    {
        LocalData.RequireId(id);
        Selected = profiles.TryGetValue(id, out var profile) ? profile
            : throw new InvalidDataException("El perfil seleccionado no existe.");
    }

    public void Create(string name)
    {
        CheckName(name, null);
        Replace(Selected with { Id = $"p-{Guid.NewGuid():N}", Name = name });
    }

    public void Rename(string name)
    {
        CheckName(name, Selected.Id);
        Replace(Selected with { Name = name });
    }

    private void CheckName(string name, string? exceptId)
    {
        Profile.ValidateName(name);
        if (profiles.Values.Any(p => p.Id != exceptId && StringComparer.OrdinalIgnoreCase.Equals(p.Name, name)))
            throw new InvalidDataException("Ya existe un perfil con ese nombre.");
    }

    public void Configure(string themeId, bool showLabels, DockShortcuts shortcuts) =>
        Replace(Selected with { ThemeId = themeId, ShowLabels = showLabels, Shortcuts = shortcuts });

    public void SetIcon(DockIcon app, byte[]? pixels)
    {
        if (pixels != null && pixels.Length != IconResources.PixelBytes)
            throw new InvalidDataException("Tamaño de icono no válido.");
        var id = pixels == null ? null : IconResources.Hash(pixels);
        var updated = Selected with { Icons = Selected.Icons.With(app, id) };
        var used = IconResources.Referenced(profiles.Values.Where(p => p.Id != Selected.Id).Append(updated));
        if (id != null && !pendingIcons.ContainsKey(id) && pendingIcons.Keys.Count(used.Contains) >= 128)
            throw new InvalidDataException("Aplique los cambios antes de importar más iconos (8 MiB por edición).");
        Replace(updated);
        if (id != null && !pendingIcons.ContainsKey(id)) pendingIcons.Add(id, pixels!.ToArray());
        DropUnused(used);
    }

    private void DropUnused(HashSet<string> used)
    {
        foreach (var id in pendingIcons.Keys.Where(id => !used.Contains(id)).ToArray()) pendingIcons.Remove(id);
    }

    private void Replace(Profile profile)
    {
        profile.Validate();
        profiles[profile.Id] = profile;
        Selected = profile;
    }

    public void DeleteSelected()
    {
        if (Selected.Id == Profile.DefaultId) throw new InvalidDataException("El perfil predeterminado está protegido.");
        profiles.Remove(Selected.Id);
        DropUnused(IconResources.Referenced(profiles.Values));
        Select(Profile.DefaultId);
    }

    internal ProfileSnapshot Snapshot() => new(1, Selected.Id, profiles.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray());
    internal void MarkSaved(ProfileSnapshot saved)
    {
        Original = saved;
        pendingIcons.Clear();
    }
}
