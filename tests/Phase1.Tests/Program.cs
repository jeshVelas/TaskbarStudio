using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Personalization;
using AppBarPrototype;

namespace Phase1.Tests;

internal static partial class Program
{
    private static int passed;
    private static readonly string Example = JsonSerializer.Serialize(ThemeCatalog.Default,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Test(string name, Action<string> body)
    {
        var temp = Directory.CreateTempSubdirectory("taskbar-theme-test-");
        try { body(temp.FullName); Console.WriteLine("PASS " + name); passed++; }
        finally { temp.Delete(recursive: true); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException or JsonException) { return; }
        throw new Exception("Invalid data was accepted.");
    }

    private static Theme Parse(string json) => ThemeCatalog.Parse(Encoding.UTF8.GetBytes(json));

    [STAThread]
    private static void Main(string[] args)
    {
        if (args is ["--window-action-target", string title])
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var target = new Form { Text = title, Width = 360, Height = 150, ShowInTaskbar = true,
                StartPosition = FormStartPosition.CenterScreen };
            Application.Run(target);
            return;
        }
        if (args.SequenceEqual(["--window-action-live"]))
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Test("Real Win32 activation, minimize, restore and stale-window handling", WindowActionLiveSmoke.Run);
            return;
        }
        if (args.SequenceEqual(["--dock-smoke"]) || args.SequenceEqual(["--window-integration"]) ||
            args.SequenceEqual(["--launch-smoke"]) || args is ["--verify-reopened-icon", _])
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2) { DockSmoke.VerifyReopenedIcon(args[1]); return; }
            if (args[0] == "--launch-smoke") { Test("Open the three Windows applications from the bar", DockSmoke.RealLaunch); return; }
            if (args[0] == "--window-integration")
            {
                Test("Live window items, state indicators, own-process exclusion and dock shutdown", WindowIntegrationSmoke.Run);
                return;
            }
            Test("Dock selection, cancellation, live styling and reopen", DockSmoke.Run);
            Test("Profile selector creation, rename, cancel, live shortcuts and restart", DockSmoke.Profiles);
            Test("Icon preview, cancel, live replacement, profile switch, reopen and restore", DockSmoke.Icons);
            Test("Live window items, state indicators, own-process exclusion and dock shutdown", WindowIntegrationSmoke.Run);
            Test("Borderless bar, compact layouts, keyboard, drag, close and desktop isolation", DockSmoke.Surface);
            return;
        }
        Test("Embedded default, independent profile and second theme", dir =>
        {
            var catalog = new ThemeCatalog(dir);
            Check(ThemeCatalog.Default.Name == "Minimal Dark" && catalog.Available().Count == 2, "Built-ins");
            Check(catalog.Resolve(Profile.Default()) == Parse(Example), "Default theme");
            Check(catalog.Load("minimal-light").Layout != ThemeCatalog.Default.Layout, "Distinct layout");
            Check(!Directory.EnumerateFileSystemEntries(dir).Any(), "Read created files");
        });
        Test("Invalid JSON, unknown fields, duplicates, versions and nulls", _ =>
        {
            foreach (var bad in new[] { "{}", "null", "{", Example + "{}", Example.Replace("\"version\":1", "\"version\":2"),
                Example.Replace("\"id\":", "\"id\":\"one\",\"id\":"), Example.Replace("\"colors\":{", "\"colors\":null,\"other\":{"),
                Example.Replace("\"name\":", "\"command\":\"calc.exe\",\"name\":"),
                Example.Replace("\"name\":", "\"iconPath\":\"../file\",\"name\":") })
                Reject(() => Parse(bad));
            foreach (var key in new[] { "name", "layout", "colors", "id" })
            {
                var json = JsonNode.Parse(Example)!.AsObject();
                json[key] = null;
                Reject(() => Parse(json.ToJsonString()));
                json.Remove(key);
                Reject(() => Parse(json.ToJsonString()));
            }
        });
        Test("RGB colors and numeric limits on all dimensions", _ =>
        {
            foreach (var color in new[] { "red", "#123", "#12345Z", "#12345678" })
            {
                var json = JsonNode.Parse(Example)!;
                json["colors"]!["background"] = color;
                Reject(() => Parse(json.ToJsonString()));
            }
            foreach (var (key, min, max) in new[] { ("iconSize",16,48), ("buttonWidth",56,144),
                ("padding",0,16), ("gap",0,16), ("bottomMargin",0,32) })
            {
                var json = JsonNode.Parse(Example)!;
                foreach (int value in new[] { min - 1, min, max, max + 1 })
                {
                    json["layout"]![key] = value;
                    if (value < min || value > max) Reject(() => Parse(json.ToJsonString()));
                    else Parse(json.ToJsonString());
                }
            }
        });
        Test("Unsafe identifiers rejected before IO", dir =>
        {
            var catalog = new ThemeCatalog(dir);
            var profiles = new ProfileStore(dir);
            foreach (var id in new[] { "", "../escape", @"..\escape", @"C:\Windows", @"\\server\file",
                "https://site", "theme:stream", "theme.json", "name ", new string('x',49) })
            {
                Reject(() => catalog.Load(id));
                Reject(() => profiles.Load(id));
                Reject(() => profiles.Save(new Profile(1, id, "minimal-dark")));
                Reject(() => profiles.Save(new Profile(1, "default", id)));
            }
            Check(!Directory.EnumerateFileSystemEntries(dir).Any(), "Unsafe write");
        });
        Test("Local JSON discovery, reserved IDs and safe fallback", dir =>
        {
            var catalog = new ThemeCatalog(dir);
            var themes = Directory.CreateDirectory(Path.Combine(dir,"TaskbarStudio","Themes")).FullName;
            var custom = Example.Replace("minimal-dark", "ocean").Replace("Minimal Dark", "Ocean");
            var path = Path.Combine(themes, "theme-ocean.json");
            File.WriteAllText(path, custom, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(themes,"theme-minimal-dark.json"), custom);
            File.WriteAllText(Path.Combine(themes,"theme-other.json"), custom);
            File.WriteAllText(Path.Combine(themes,"theme-large.json"), new string(' ', 16385));
            Check(catalog.Available().Select(t => t.Id).Order().SequenceEqual(new[] { "minimal-dark","minimal-light","ocean" }), "Catalog");
            Check(catalog.Load("ocean").Name == "Ocean" && catalog.Load("minimal-dark").Name == "Minimal Dark","Resolution");
            Reject(() => catalog.Load("other"));
            Reject(() => catalog.Load("large"));
            File.Delete(path);
            Check(catalog.Resolve(new Profile(1,"default","ocean")) == ThemeCatalog.Default,"Deleted theme");
        });
        Test("Separate profiles round trip, overwrite and invalid saves", dir =>
        {
            var store = new ProfileStore(dir);
            var light = new Profile(1, "default", "minimal-light");
            store.Save(light);
            store.Save(Profile.Default("work"));
            Check(new ProfileStore(dir).Load() == light && store.Load("work") == Profile.Default("work"), "Reopen");
            Reject(() => store.Save(light with {Version=2}));
            Check(store.Load() == light, "Invalid save changed profile");
            store.Save(Profile.Default());
            Check(store.Load() == Profile.Default(), "Overwrite");
        });
        Test("Missing and malformed profiles recover without destructive writes", dir =>
        {
            var store = new ProfileStore(dir);
            Check(store.Load() == Profile.Default(), "Missing");
            var profiles = Directory.CreateDirectory(Path.Combine(dir,"TaskbarStudio","Profiles"));
            var file = Path.Combine(profiles.FullName,"profile-default.json");
            foreach (var bad in new[] { "{", """{"version":1,"id":"default","themeId":"../x"}""",
                """{"version":1,"id":"different","themeId":"ocean"}""",
                """{"version":2,"id":"default","themeId":"ocean"}""", new string(' ',16385) })
            {
                File.WriteAllText(file,bad);
                Check(store.Load() == Profile.Default() && File.ReadAllText(file) == bad, "Damaged file");
            }
            store.Save(Profile.Default());
            Check(File.ReadAllText(file).Contains("minimal-dark"), "Explicit recovery");
        });
        Test("Concurrent profile replacement keeps complete JSON and cleans staging files", dir =>
        {
            var store = new ProfileStore(dir);
            store.Save(new Profile(1,"work","ocean"));
            Parallel.For(0, 24, i =>
            {
                store.Save(new Profile(1,"work",i % 2 == 0 ? "ocean" : "sand"));
                Check(store.Load("work").ThemeId is "ocean" or "sand", "Partial profile");
            });
            Check(Directory.GetFiles(dir,"*.tmp",SearchOption.AllDirectories).Length == 0, "Temporary file left");
        });
        Test("Failed replacement preserves the previous selection", dir =>
        {
            var store = new ProfileStore(dir);
            var original = new Profile(1, "default", "minimal-light");
            store.Save(original);
            var file = Path.Combine(dir, "TaskbarStudio", "Profiles", "profile-default.json");
            File.SetAttributes(file, FileAttributes.ReadOnly);
            try
            {
                try { store.Save(Profile.Default()); throw new Exception("Replaced read-only profile"); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
                Check(store.Load() == original, "Lost saved selection");
                Check(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length == 0, "Temporary file left");
            }
            finally { File.SetAttributes(file, FileAttributes.Normal); }
        });
        Test("Directory junctions cannot redirect themes or profiles", dir =>
        {
            var root = Directory.CreateDirectory(Path.Combine(dir, "TaskbarStudio")).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(dir, "outside")).FullName;
            File.WriteAllText(Path.Combine(outside, "theme-ocean.json"), Example.Replace("minimal-dark", "ocean"));
            foreach (string name in new[] { "Themes", "Profiles", "Icons" })
            {
                var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
                {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                    Arguments = $@"/d /c mklink /J {name} ..\outside"
                };
                using var proc = System.Diagnostics.Process.Start(start)!;
                proc.WaitForExit();
                Check(proc.ExitCode == 0, "Could not create fixture");
                try
                {
                    if (name == "Themes")
                    {
                        var catalog = new ThemeCatalog(dir);
                        Reject(() => catalog.Load("ocean"));
                        Check(catalog.Available().Count == 2, "Followed junction in catalog");
                    }
                    else if (name == "Profiles")
                    {
                        var store = new ProfileStore(dir);
                        Reject(() => store.Save(Profile.Default()));
                        Check(!File.Exists(Path.Combine(outside, "profile-default.json")), "External write");
                    }
                    else
                    {
                        var store = new ProfileStore(dir);
                        var draft = store.BeginEdit();
                        draft.SetIcon(DockIcon.Notepad, new byte[IconResources.PixelBytes]);
                        Reject(() => store.Apply(draft));
                        Reject(() => IconResources.ReadImport(Path.Combine(root, name, "example.png")));
                        Check(!Directory.EnumerateFiles(outside, "*.bgra").Any() && !File.Exists(CatalogPath(dir)),
                            "Wrote through junction or committed a missing icon");
                    }
                }
                finally { Directory.Delete(Path.Combine(root, name)); }
            }
        });
        TestProfiles();
        TestIcons();
        TestWindowTracker();
        TestWindowActions();
        Test("Bar placement at 96, 120, 144, 192 and 288 DPI and fixed launcher identities", _ =>
        {
            foreach (int dpi in new[] { 96, 120, 144, 192, 288 })
            {
                int Px(int logical) => logical * dpi / 96;
                var area = new Rectangle(-1920 + Px(48), -240, 1920 - Px(48), 1080 - Px(48));
                var size = new Size(Px(364), Px(66));
                var initial = FloatingDock.DockPlacement.BottomCenter(area, size, Px(10));
                Check(area.Contains(new Rectangle(initial, size)) && initial.Y + size.Height == area.Bottom - Px(10), "Initial placement");
                var edge = FloatingDock.DockPlacement.Constrain(area, size, new Point(-9999, 9999));
                Check(edge == new Point(area.Left, area.Bottom - size.Height), "Drag outside scaled work area");
                Check(FloatingDock.DockPlacement.Constrain(area, size, initial) == initial, "Valid position moved");
            }
            var items = FloatingDock.LauncherItem.BuiltIns.Cast<FloatingDock.LauncherItem>().ToArray();
            Check(items.Select(i => i.Id).SequenceEqual(["notepad", "calculator", "explorer"]), "Launcher IDs");
            Check(items.All(i => Path.IsPathFullyQualified(i.Target) && File.Exists(i.Target)), "Missing launcher targets");
        });
        Test("AppBar registration, negotiation, rejection and reversible removal", _ => AppBarProtocolTests.Run());
        Console.WriteLine($"{passed} test groups passed.");
    }
}
