using System.Text.Json;

namespace Personalization;

public sealed class ProfileStore(string appData)
{
    private const string CatalogFile = "profiles.json";
    private const int CatalogLimit = 256 * 1024;
    private string DirectoryName => LocalData.DirectoryPath(appData, "Profiles");
    public IconResources Icons { get; } = new(appData);

    public Profile Load(string id = Profile.DefaultId)
    {
        LocalData.RequireId(id);
        try
        {
            using var access = new ProfileLock();
            var dir = DirectoryName;
            var state = ReadCatalog(dir);
            return state == null ? ReadLegacy(dir, id) :
                state.Profiles.FirstOrDefault(p => p.Id == id) ?? Profile.Default(id);
        }
        catch (Exception ex) when (LocalData.IsDataError(ex)) { return Profile.Default(id); }
    }

    public void Save(Profile profile)
    {
        profile.Validate();
        using var access = new ProfileLock();
        var dir = DirectoryName;
        var state = ReadCatalog(dir);
        if (state == null)
            LocalData.Write(dir, $"profile-{profile.Id}.json", profile);
        else
        {
            var updated = state.Profiles.Where(p => p.Id != profile.Id).Append(profile).ToArray();
            LocalData.Write(dir, CatalogFile, state with { Profiles = updated }, CatalogLimit);
            Icons.Clean(IconResources.Referenced(updated));
        }
    }

    public Profile LoadActive()
    {
        try { return BeginEdit().Selected; }
        catch (Exception ex) when (LocalData.IsDataError(ex)) { return Profile.Default(); }
    }

    public ProfileDraft BeginEdit()
    {
        using var access = new ProfileLock();
        return new ProfileDraft(ReadState(DirectoryName));
    }

    public void Apply(ProfileDraft draft)
    {
        using var access = new ProfileLock();
        var dir = DirectoryName;
        var current = ReadState(dir);
        if (current.ActiveId != draft.Original.ActiveId || !current.Profiles.SequenceEqual(draft.Original.Profiles))
            throw new IOException("Los perfiles cambiaron desde que abrió el selector. Vuelva a abrirlo.");
        var saved = draft.Snapshot();
        try
        {
            foreach (var (id, pixels) in draft.PendingIcons) Icons.Publish(id, pixels);
            LocalData.Write(dir, CatalogFile, saved, CatalogLimit);
        }
        catch
        {
            Icons.Clean(IconResources.Referenced(current.Profiles), draft.PendingIcons.Keys);
            throw;
        }
        draft.MarkSaved(saved);
        Icons.Clean(IconResources.Referenced(saved.Profiles));
    }

    private static Profile ReadLegacy(string dir, string id)
    {
        var profile = LocalData.Parse<Profile>(LocalData.Read(Path.Combine(dir, $"profile-{id}.json")));
        profile.Validate();
        if (profile.Id != id) throw new InvalidDataException("El identificador difiere del nombre de archivo.");
        return profile;
    }

    private static ProfileSnapshot Initial() => new(1, Profile.DefaultId, [Profile.Default()]);

    private sealed record StoredCatalog(int Version, string ActiveId, JsonElement[] Profiles);

    private static ProfileSnapshot? ReadCatalog(string dir)
    {
        try
        {
            var bytes = LocalData.Read(Path.Combine(dir, CatalogFile), CatalogLimit);
            var state = LocalData.Parse<StoredCatalog>(bytes, CatalogLimit);
            if (state.Version != 1) throw new InvalidDataException("Versión de catálogo no válida.");
            var valid = new Dictionary<string, Profile>();
            var seen = new HashSet<string>();
            foreach (var entry in state.Profiles)
            {
                try
                {
                    var p = entry.Deserialize<Profile>(LocalData.Json);
                    if (p == null) continue;
                    p.Validate();
                    if (seen.Add(p.Id)) valid.Add(p.Id, p);
                    else valid.Remove(p.Id); // An ambiguous ID cannot be restored reliably.
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException) { }
            }
            valid.TryAdd(Profile.DefaultId, Profile.Default());
            var active = valid.ContainsKey(state.ActiveId) ? state.ActiveId : Profile.DefaultId;
            return new(1, active, valid.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is InvalidDataException or JsonException) { return Initial(); }
    }

    private static ProfileSnapshot ReadState(string dir)
    {
        var catalog = ReadCatalog(dir);
        if (catalog != null) return catalog;
        // Import v1 files only until the first successful Apply. Keep originals intact.
        var profiles = new Dictionary<string, Profile> { [Profile.DefaultId] = Profile.Default() };
        try
        {
            foreach (var path in Directory.EnumerateFiles(dir, "profile-*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(path)["profile-".Length..];
                if (!LocalData.ValidId(id)) continue;
                try { profiles[id] = ReadLegacy(dir, id); }
                catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or JsonException) { }
            }
        }
        catch (DirectoryNotFoundException) { }
        return new(1, Profile.DefaultId, profiles.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray());
    }

    // Coordinate short reads and replacements across dock instances owned by this user.
    private sealed class ProfileLock : IDisposable
    {
        private readonly Mutex mutex;

        internal ProfileLock()
        {
            mutex = new Mutex(false, "TaskbarStudio.Profile.Catalog",
                new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false });
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(2)))
                    throw new IOException("El perfil está ocupado. Vuelva a intentarlo.");
            }
            catch (AbandonedMutexException) { } // Ownership transferred after the previous writer exited.
            catch { mutex.Dispose(); throw; }
        }

        public void Dispose()
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
