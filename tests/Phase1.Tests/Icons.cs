using System.Buffers.Binary;
using System.Drawing.Imaging;
using FloatingDock;
using Personalization;

namespace Phase1.Tests;

internal static partial class Program
{
    internal static byte[] SamplePng(Color color, int side = 32)
    {
        using var bitmap = new Bitmap(side, side);
        using (var g = Graphics.FromImage(bitmap)) g.Clear(color);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static byte[] Ico(params (byte Size, byte[] Image)[] frames)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((short)0);
        writer.Write((short)1);
        writer.Write((short)frames.Length);
        int offset = 6 + 16 * frames.Length;
        foreach (var (size, payload) in frames)
        {
            writer.Write(size); writer.Write(size); writer.Write((short)0);
            writer.Write((short)1); writer.Write((short)32);
            writer.Write(payload.Length); writer.Write(offset);
            offset += payload.Length;
        }
        foreach (var frame in frames) writer.Write(frame.Image);
        return output.ToArray();
    }

    internal static int Center(Image image) => ((Bitmap)image).GetPixel(image.Width / 2, image.Height / 2).ToArgb();

    private static void TestIcons()
    {
        Test("Import real PNG, multi-frame ICO with PNG and traditional ICO with DIB", dir =>
        {
            var png = SamplePng(Color.Red);
            var files = new[] { ("red.png", png), ("red.ico", Ico((16, SamplePng(Color.Blue, 16)), (32, png))) };
            foreach (var (name, bytes) in files)
            {
                var path = Path.Combine(dir, name);
                File.WriteAllBytes(path, bytes);
                using var bitmap = IconImage.Render(IconImage.Import(path), 32);
                Check(Center(bitmap) == Color.Red.ToArgb(), "Wrong image/frame");
                File.Delete(path);
            }
            var dib = new byte[40 + 16 * 16 * 4 + 16 * 4];
            BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
            dib[4] = 16; dib[8] = 32; dib[12] = 1; dib[14] = 32;
            for (int i = 40; i < 40 + 16 * 16 * 4; i += 4) { dib[i] = 255; dib[i + 3] = 255; }
            using var traditional = IconImage.Render(IconImage.Decode(Ico((16, dib)), true), 16);
            Check(Center(traditional) == Color.Blue.ToArgb(), "Wrong DIB pixels");
        });
        Test("Reject missing, remote, disguised, oversized, truncated and corrupt imports", dir =>
        {
            var path = Path.Combine(dir, "candidate.png");
            try { IconImage.Import(path); throw new Exception("Missing file accepted"); } catch (FileNotFoundException) { }
            foreach (var badPath in new[] { @"\\server\share\icon.png", "https://example.invalid/icon.png",
                "../icon.png", @"C:\test.png:extra.png", @"\\?\C:\external.png" })
                Reject(() => IconImage.Import(badPath));
            var valid = SamplePng(Color.Green);
            var corrupt = (byte[])valid.Clone();
            corrupt[^1] ^= 1;
            var huge = (byte[])valid.Clone();
            BinaryPrimitives.WriteInt32BigEndian(huge.AsSpan(16), 1025);
            foreach (var bytes in new[] { "MZ"u8.ToArray(), "GIF89a"u8.ToArray(), valid[..^12], corrupt,
                huge, new byte[IconResources.MaxImportBytes + 1], Ico((32, valid)) })
            {
                File.WriteAllBytes(path, bytes);
                Reject(() => IconImage.Import(path));
            }
            var broken = Ico((32, valid));
            BinaryPrimitives.WriteInt32LittleEndian(broken.AsSpan(18), int.MaxValue);
            foreach (var bytes in new[] { valid, broken, Ico((16, valid)), Ico((32, new byte[12])) })
                Reject(() => IconImage.Decode(bytes, true));
        });
        Test("Independent profile icons survive source removal and shared assets are collected at last use", dir =>
        {
            var store = new ProfileStore(dir);
            var edit = store.BeginEdit();
            var path = Path.Combine(dir, "source.png");
            File.WriteAllBytes(path, SamplePng(Color.Orange));
            edit.SetIcon(DockIcon.Notepad, IconImage.Import(path));
            File.Delete(path);
            string id = edit.Selected.Icons.Notepad!;
            edit.Create("Gaming");
            edit.SetIcon(DockIcon.Calculator, edit.PendingPixels(id));
            edit.SetIcon(DockIcon.Notepad, null);
            store.Apply(edit);
            var reopened = new ProfileStore(dir);
            Check(reopened.LoadActive().Icons == new IconReferences(Calculator: id) && reopened.Load().Icons.Notepad == id,
                "Cross-profile edit or lost selection");
            using var actual = IconImage.Render(reopened.Icons.ReadPixels(id), 28);
            Check(Center(actual) == Color.Orange.ToArgb(), "Failed after source removal");
            var folder = Path.Combine(dir, "TaskbarStudio", "Icons");
            Check(Directory.GetFiles(folder).Length == 1, "Duplicate resources");
            edit.DeleteSelected();
            store.Apply(edit);
            Check(Directory.GetFiles(folder).Length == 1, "Deleted shared resource");
            edit.SetIcon(DockIcon.Notepad, null);
            store.Apply(edit);
            Check(Directory.GetFiles(folder).Length == 0, "Orphan after restore");
        });
        Test("Profile overrides theme, corrupt resources fall through to theme and built-in", dir =>
        {
            var store = new ProfileStore(dir);
            var themes = Directory.CreateDirectory(Path.Combine(dir, "TaskbarStudio", "Themes")).FullName;
            var path = Path.Combine(themes, "icon-ocean.png");
            File.WriteAllBytes(path, SamplePng(Color.Blue));
            var json = System.Text.Json.Nodes.JsonNode.Parse(Example)!;
            json["id"] = "ocean";
            json["icons"] = new System.Text.Json.Nodes.JsonObject { ["explorer"] = "ocean" };
            File.WriteAllText(Path.Combine(themes, "theme-ocean.json"), json.ToJsonString());
            var theme = new ThemeCatalog(dir).Load("ocean");
            var edit = store.BeginEdit();
            edit.SetIcon(DockIcon.Explorer, IconImage.Decode(SamplePng(Color.Red), false));
            store.Apply(edit);
            var renderer = new IconRenderer(store.Icons);
            int Read()
            {
                using var image = renderer.Render(DockIcon.Explorer, edit.Selected, theme, 28);
                return Center(image);
            }
            Check(Read() == Color.Red.ToArgb(), "Profile priority");
            var asset = Directory.GetFiles(Path.Combine(dir, "TaskbarStudio", "Icons")).Single();
            File.WriteAllBytes(asset, new byte[IconResources.PixelBytes]);
            renderer.Clear();
            Check(Read() == Color.Blue.ToArgb(), "Hash mismatch did not fall through");
            File.Delete(path);
            Check(Read() == Color.Blue.ToArgb(), "Cached image reloaded");
            renderer.Clear();
            using var builtin = DockIcons.Render(DockIcon.Explorer, 28);
            Check(Read() == Center(builtin), "Missing theme icon");
            File.WriteAllText(path, "broken");
            renderer.Clear();
            Check(Read() == Center(builtin), "Corrupt theme icon");
            Reject(() => store.Icons.ReadThemePng("../outside"));
            Reject(() => store.Icons.ReadPixels("../outside"));
        });
        Test("Failed import and catalog replacement preserve originals and allow retry without orphans", dir =>
        {
            var store = new ProfileStore(dir);
            var edit = store.BeginEdit();
            edit.SetIcon(DockIcon.Explorer, IconImage.Decode(SamplePng(Color.Red), false));
            store.Apply(edit);
            var profile = edit.Selected;
            var path = CatalogPath(dir);
            var before = File.ReadAllBytes(path);
            Reject(() => edit.SetIcon(DockIcon.Explorer, IconImage.Decode([1, 2, 3], true)));
            Check(edit.Selected == profile && File.ReadAllBytes(path).SequenceEqual(before), "Failed import mutated draft");
            edit.SetIcon(DockIcon.Explorer, IconImage.Decode(SamplePng(Color.Blue), false));
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                try { store.Apply(edit); throw new Exception("Replaced read-only catalog"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                Check(store.LoadActive() == profile && File.ReadAllBytes(path).SequenceEqual(before), "Partial commit");
                var files = Directory.GetFiles(Path.Combine(dir, "TaskbarStudio", "Icons"));
                Check(files.Length == 1 && files[0].Contains(profile.Icons.Explorer!), "Orphan or lost old asset");
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            store.Apply(edit);
            Check(store.LoadActive() == edit.Selected && Directory.GetFiles(Path.Combine(dir, "TaskbarStudio", "Icons")).Length == 1,
                "Retry failed");
        });
        Test("Bounded cache evicts older images and recovery only cleans managed files", dir =>
        {
            var store = new ProfileStore(dir);
            var renderer = new IconRenderer(store.Icons);
            var folder = Directory.CreateDirectory(Path.Combine(dir, "TaskbarStudio", "Themes")).FullName;
            var png = SamplePng(Color.Red);
            for (int i = 0; i <= 12; i++)
            {
                var id = $"color-{i}";
                File.WriteAllBytes(Path.Combine(folder, $"icon-{id}.png"), png);
                using var image = renderer.Render(DockIcon.Notepad, Profile.Default(),
                    ThemeCatalog.Default with { Icons = new(Notepad: id) }, 28);
                Check(Center(image) == Color.Red.ToArgb(), "Wrong cached theme icon");
            }
            File.WriteAllBytes(Path.Combine(folder, "icon-color-0.png"), SamplePng(Color.Blue));
            using var evicted = renderer.Render(DockIcon.Notepad, Profile.Default(),
                ThemeCatalog.Default with { Icons = new(Notepad: "color-0") }, 28);
            Check(Center(evicted) == Color.Blue.ToArgb(), "Cache retained more than twelve entries");
            var managed = Directory.CreateDirectory(Path.Combine(dir, "TaskbarStudio", "Icons")).FullName;
            File.WriteAllBytes(Path.Combine(managed, $"icon-{new string('a', 64)}.bgra"), [1]);
            File.WriteAllBytes(Path.Combine(managed, $"{Guid.NewGuid():N}.tmp"), [1]);
            var unrelated = Path.Combine(managed, "example.png");
            File.WriteAllBytes(unrelated, png);
            store.Apply(store.BeginEdit());
            Check(Directory.GetFiles(managed).SequenceEqual([unrelated]), "Orphan survived or unrelated file was deleted");
        });
    }
}
