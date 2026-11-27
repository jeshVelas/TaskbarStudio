using System.Security.Cryptography;

namespace Personalization;

public enum DockIcon { Notepad, Calculator, Explorer }

public sealed record IconReferences(string? Notepad = null, string? Calculator = null, string? Explorer = null)
{
    public string? Get(DockIcon icon) => icon switch
    {
        DockIcon.Notepad => Notepad, DockIcon.Calculator => Calculator, DockIcon.Explorer => Explorer,
        _ => throw new ArgumentOutOfRangeException(nameof(icon))
    };

    internal IconReferences With(DockIcon icon, string? id) => icon switch
    {
        DockIcon.Notepad => this with { Notepad = id }, DockIcon.Calculator => this with { Calculator = id },
        DockIcon.Explorer => this with { Explorer = id }, _ => throw new ArgumentOutOfRangeException(nameof(icon))
    };
}

// Fixed-size BGRA pixels avoid parsing external image formats when reopening a profile.
public sealed class IconResources(string appData)
{
    public const int Side = 128;
    public const int PixelBytes = Side * Side * 4;
    public const int MaxImportBytes = 2 * 1024 * 1024;
    private string Folder => LocalData.DirectoryPath(appData, "Icons");
    private static bool ValidHash(string? id) => id is { Length: 64 } &&
        id.AsSpan().IndexOfAnyExcept("0123456789abcdef") < 0;
    internal static string Hash(byte[] pixels) => Convert.ToHexStringLower(SHA256.HashData(pixels));
    private static string FileName(string id) => $"icon-{id}.bgra";

    public static byte[] ReadImport(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path.AsSpan(2).Contains(':'))
            throw new InvalidDataException("Seleccione un archivo de una unidad local.");
        var full = Path.GetFullPath(path);
        var drive = new DriveInfo(Path.GetPathRoot(full)!);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Ram or DriveType.CDRom))
            throw new InvalidDataException("Seleccione un archivo de una unidad local.");
        var parents = new Stack<string>();
        for (var parent = Directory.GetParent(full); parent != null; parent = parent.Parent) parents.Push(parent.FullName);
        foreach (var parent in parents) LocalData.RejectLink(parent);
        return LocalData.Read(full, MaxImportBytes);
    }

    public byte[] ReadPixels(string id)
    {
        if (!ValidHash(id)) throw new InvalidDataException("Referencia de icono no válida.");
        var bytes = LocalData.Read(Path.Combine(Folder, FileName(id)), PixelBytes);
        if (bytes.Length != PixelBytes || Hash(bytes) != id) throw new InvalidDataException("Icono dañado.");
        return bytes;
    }

    public byte[] ReadThemePng(string id)
    {
        LocalData.RequireId(id);
        return LocalData.Read(Path.Combine(LocalData.DirectoryPath(appData, "Themes"), $"icon-{id}.png"), MaxImportBytes);
    }

    internal static HashSet<string> Referenced(IEnumerable<Profile> profiles)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in profiles)
            foreach (var icon in Enum.GetValues<DockIcon>())
                if (profile.Icons.Get(icon) is string id && ValidHash(id)) ids.Add(id);
        return ids;
    }

    internal void Publish(string id, byte[] pixels)
    {
        try { ReadPixels(id); return; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException) { }
        LocalData.WriteBytes(Folder, FileName(id), pixels, PixelBytes);
    }

    // Called under the catalog writer lock. Never descend into directories or remove unrelated files.
    internal void Clean(HashSet<string> keep, IEnumerable<string>? only = null)
    {
        try
        {
            var dir = Folder;
            var paths = only == null ? Directory.EnumerateFiles(dir) : only.Select(id => Path.Combine(dir, FileName(id)));
            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                bool staging = name.EndsWith(".tmp", StringComparison.Ordinal) &&
                    Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _);
                if (!staging && !(name.Length == 74 && name.StartsWith("icon-", StringComparison.Ordinal) &&
                    name.EndsWith(".bgra", StringComparison.Ordinal) && ValidHash(name[5..^5]) && !keep.Contains(name[5..^5]))) continue;
                try { LocalData.RejectLink(path); File.Delete(path); }
                catch (Exception ex) when (LocalData.IsDataError(ex)) { }
            }
        }
        catch (Exception ex) when (LocalData.IsDataError(ex)) { } // Retry on the next successful Apply.
    }
}
