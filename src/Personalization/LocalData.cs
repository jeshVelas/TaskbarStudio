using System.Text.Json;
using System.Text.Json.Serialization;

namespace Personalization;

internal static class LocalData
{
    internal const int MaxBytes = 16 * 1024;
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 8,
        WriteIndented = true
    };

    internal static bool ValidId(string? id)
    {
        if (id is not { Length: >= 1 and <= 48 } || !char.IsAsciiLetterLower(id[0])) return false;
        return id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
    }

    internal static void RequireId(string id)
    {
        if (!ValidId(id)) throw new InvalidDataException("Identificador no válido.");
    }

    internal static bool IsDataError(Exception ex) =>
        ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;

    internal static T Parse<T>(ReadOnlySpan<byte> bytes, int limit = MaxBytes) where T : class
    {
        CheckSize(bytes.Length, limit);
        // Accept UTF-8 files written with a BOM by Windows text editors.
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) bytes = bytes[3..];
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("JSON vacío.");
    }

    // The caller supplies a trusted base (LocalApplicationData in the application).
    // Only fixed directory names and prefixed, validated identifiers are appended.
    internal static string DirectoryPath(string appData, string name)
    {
        var root = Path.Combine(appData, "TaskbarStudio");
        RejectLink(root);
        var path = Path.Combine(root, name);
        RejectLink(path);
        return path;
    }

    internal static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("No se admiten enlaces ni puntos de reanálisis.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    internal static byte[] Read(string path, int limit = MaxBytes)
    {
        RejectLink(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        long length = stream.Length;
        CheckSize(length, limit);
        var bytes = new byte[(int)length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void CheckSize(long length, int limit)
    {
        if (length > limit) throw new InvalidDataException($"El archivo supera los {limit / 1024} KiB.");
    }

    internal static void Write<T>(string dir, string filename, T value, int limit = MaxBytes)
        => WriteBytes(dir, filename, JsonSerializer.SerializeToUtf8Bytes(value, Json), limit);

    internal static void WriteBytes(string dir, string filename, byte[] bytes, int limit)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, filename);
        RejectLink(path);
        CheckSize(bytes.Length, limit);
        var tmp = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");
        var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (stream)
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); }
            catch (Exception ex) when (IsDataError(ex)) { }
            throw;
        }
    }
}
