using System.Text.Json.Nodes;
using Personalization;

namespace Phase1.Tests;

internal static partial class Program
{
    private static string CatalogPath(string dir) => Path.Combine(dir, "TaskbarStudio", "Profiles", "profiles.json");

    private static void TestProfiles()
    {
        Test("Create, rename and switch profiles without changing their identifiers", dir =>
        {
            var store = new ProfileStore(dir);
            var draft = store.BeginEdit();
            draft.Create("Oficina");
            var id = draft.Selected.Id;
            var shortcuts = new DockShortcuts(false, true, true);
            draft.Configure("minimal-light", false, shortcuts);
            draft.Rename("Trabajo");
            Check(draft.Selected.Id == id && draft.Selected.Name == "Trabajo", "Unstable rename");
            draft.Select(Profile.DefaultId);
            Check(draft.Selected == Profile.Default(), "Creating a profile changed its source");
            draft.Select(id);
            store.Apply(draft);

            var reopened = new ProfileStore(dir);
            Check(reopened.LoadActive() == draft.Selected && reopened.Load(id).Shortcuts == shortcuts, "Active profile lost");
            var other = reopened.BeginEdit();
            other.Select(Profile.DefaultId);
            reopened.Apply(other);
            Check(store.LoadActive() == Profile.Default() && store.Load(id) == draft.Selected, "Switch lost settings");
        });
        Test("Protect default and persist deletion of the active profile", dir =>
        {
            var store = new ProfileStore(dir);
            var draft = store.BeginEdit();
            draft.Rename("Principal");
            Reject(draft.DeleteSelected);
            draft.Create("Temporal");
            var removed = draft.Selected.Id;
            store.Apply(draft);
            draft.DeleteSelected();
            Check(store.LoadActive().Id == removed, "Deleted before Apply");
            store.Apply(draft);
            var reopened = store.BeginEdit();
            Check(reopened.Profiles.Count == 1 && reopened.Selected.Name == "Principal", "Deletion not persisted");
            Reject(() => reopened.Select(removed));
        });
        Test("Discarding a draft preserves selections, names, shortcuts and storage bytes", dir =>
        {
            var store = new ProfileStore(dir);
            var setup = store.BeginEdit();
            setup.Create("Original");
            store.Apply(setup);
            var before = File.ReadAllBytes(CatalogPath(dir));
            var edit = store.BeginEdit();
            edit.Rename("Cambio");
            edit.Configure("minimal-light", false, new(true, false, false));
            edit.DeleteSelected();
            edit.Create("Descartado");
            Check(before.SequenceEqual(File.ReadAllBytes(CatalogPath(dir))) && store.LoadActive() == setup.Selected,
                "Draft changed persisted state");
        });
        Test("Reject invalid names, theme IDs and empty shortcut selections", dir =>
        {
            var draft = new ProfileStore(dir).BeginEdit();
            foreach (var name in new[] { "", " ", " Hola", "Dos\nlíneas", "a\u202Eb", new string('x', 65) })
            {
                Reject(() => draft.Create(name));
                Reject(() => draft.Rename(name));
            }
            Reject(() => draft.Create("PREDETERMINADO"));
            Reject(() => draft.Configure("../tema", true, DockShortcuts.All));
            Reject(() => draft.Configure("minimal-dark", true, new(false, false, false)));
            Reject(() => draft.Select("missing"));
            Check(draft.Profiles.Count == 1 && draft.Selected == Profile.Default(), "Invalid edit changed state");
        });
        Test("Import v1 files at Apply and keep deleted legacy profiles deleted", dir =>
        {
            var folder = Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath(dir))!).FullName;
            const string original = """{"version":1,"id":"default","themeId":"minimal-light"}""";
            var path = Path.Combine(folder, "profile-default.json");
            File.WriteAllText(path, original);
            File.WriteAllText(Path.Combine(folder, "profile-work.json"), original.Replace("default", "work"));
            var store = new ProfileStore(dir);
            var edit = store.BeginEdit();
            Check(edit.Selected.ThemeId == "minimal-light" && edit.Selected.Shortcuts == DockShortcuts.All && edit.Selected.ShowLabels,
                "Wrong v1 defaults");
            Check(!File.Exists(CatalogPath(dir)), "Read migrated eagerly");
            edit.Select("work");
            edit.DeleteSelected();
            store.Apply(edit);
            Check(store.BeginEdit().Profiles.Count == 1 && File.ReadAllText(path) == original, "Incorrect migration");
        });
        Test("Recover corrupt catalogs and missing active IDs without writing on startup", dir =>
        {
            var store = new ProfileStore(dir);
            var edit = store.BeginEdit();
            edit.Create("Personal");
            store.Apply(edit);
            var path = CatalogPath(dir);
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            node["activeId"] = "missing";
            File.WriteAllText(path, node.ToJsonString());
            Check(store.BeginEdit().Profiles.Count == 2 && store.LoadActive() == Profile.Default(), "Missing active ID");
            var entry = node["profiles"]![0]!;
            entry["shortcuts"]!["command"] = "program.exe";
            node["activeId"] = edit.Selected.Id;
            File.WriteAllText(path, node.ToJsonString());
            Check(store.LoadActive() == edit.Selected && store.Load() == Profile.Default(), "Invalid sibling hid valid active profile");
            node["profiles"]![1]!["id"] = "../bad";
            foreach (var bad in new[] { node.ToJsonString(), "{", "null", """{"version":1,"activeId":"default","profiles":[null]}""",
                """{"version":1,"version":1,"activeId":"default","profiles":[]}""", new string(' ', 256 * 1024 + 1) })
            {
                File.WriteAllText(path, bad);
                Check(store.LoadActive() == Profile.Default() && File.ReadAllText(path) == bad, "Destructive fallback");
            }
            store.Apply(store.BeginEdit());
            Check(JsonNode.Parse(File.ReadAllText(path))!["activeId"]!.GetValue<string>() == Profile.DefaultId, "Explicit repair");
        });
        Test("Stale drafts and failed replacements preserve the committed collection", dir =>
        {
            var store = new ProfileStore(dir);
            var first = store.BeginEdit();
            var stale = store.BeginEdit();
            first.Create("Primero");
            store.Apply(first);
            stale.Create("Segundo");
            try { store.Apply(stale); throw new Exception("Accepted stale draft"); }
            catch (IOException) { }
            var path = CatalogPath(dir);
            var before = File.ReadAllBytes(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                first.DeleteSelected();
                try { store.Apply(first); throw new Exception("Replaced read-only catalog"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                Check(File.ReadAllBytes(path).SequenceEqual(before) && store.LoadActive().Name == "Primero", "Partial commit");
                Check(!Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any(), "Left temporary files");
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        });
    }
}
