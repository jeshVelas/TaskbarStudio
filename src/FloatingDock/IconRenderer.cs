using Personalization;

namespace FloatingDock;

// The cache owns managed pixel arrays. Each control owns its rendered Bitmap.
internal sealed class IconRenderer(IconResources resources)
{
    private readonly Dictionary<(bool Theme, string Id), byte[]?> cache = new();
    private readonly Queue<(bool Theme, string Id)> order = new();

    internal void Clear()
    {
        cache.Clear();
        order.Clear();
    }

    internal Bitmap Render(DockIcon app, Profile profile, Theme theme, int size, ProfileDraft? draft = null)
    {
        byte[]? pixels = null;
        if (profile.Icons.Get(app) is string id) pixels = draft?.PendingPixels(id) ?? Get(false, id);
        if (pixels == null && theme.Icons.Get(app) is string themeId) pixels = Get(true, themeId);
        return pixels == null ? DockIcons.Render(app, size) : IconImage.Render(pixels, size);
    }

    private byte[]? Get(bool theme, string id)
    {
        var key = (theme, id);
        if (cache.TryGetValue(key, out var pixels)) return pixels;
        try { pixels = theme ? IconImage.Decode(resources.ReadThemePng(id), ico: false) : resources.ReadPixels(id); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { pixels = null; }
        if (order.Count == 12) cache.Remove(order.Dequeue());
        order.Enqueue(key);
        cache.Add(key, pixels);
        return pixels;
    }
}
