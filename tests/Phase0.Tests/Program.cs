using System.Drawing;
using System.Runtime.InteropServices;
using NativeTrial;
using FloatingDock;

int passed = 0;
void Check(bool ok)
{
    if (!ok) throw new Exception("Assertion failed");
}
void Fails<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
void Test(string name, Action<string> run)
{
    string dir = Directory.CreateTempSubdirectory("taskbar-trial-").FullName;
    try { run(dir); Console.WriteLine("PASS " + name); passed++; }
    finally { Directory.Delete(dir, true); }
}

Test("APPBARDATA matches the Windows SDK x64 layout", dir =>
{
    Check(IntPtr.Size == 8 && Marshal.SizeOf<ShellTaskbar.AppBarData>() == 48);
    foreach (var (field, offset) in new[] { ("Size", 0), ("Window", 8), ("Callback", 16),
        ("Edge", 20), ("Left", 24), ("Top", 28), ("Right", 32), ("Bottom", 36), ("Parameter", 40) })
        Check(Marshal.OffsetOf<ShellTaskbar.AppBarData>(field) == offset);
});
Test("Only the bottom taskbar supports a verifiable trial on build 26200", dir =>
{
    foreach (var (left, top, right, bottom, expected) in new[] {
        (0, 1020, 1920, 1080, true), (0, 1079, 1920, 1080, true),
        (0, 0, 60, 1080, false), (1860, 0, 1920, 1080, false), (0, 0, 1920, 60, false) })
    {
        var position = new ShellTaskbar.AppBarData { Left = left, Top = top, Right = right, Bottom = bottom };
        Check(ShellTaskbar.AtBottom(position, 1080) == expected);
    }
});
Test("A masked state cannot start a trial or discard recovery", dir =>
{
    var shell = new FakeShell(false) { CanVerify = false };
    var trial = new Trial(shell, dir);
    Fails<InvalidOperationException>(() => trial.Apply(true));
    Check(trial.Original == null && shell.Writes == 0);
    shell.CanVerify = true;
    trial.Apply(true);
    shell.CanVerify = false;
    Fails<InvalidOperationException>(trial.Restore);
    Check(trial.Original == false && shell.Writes == 1);
    shell.CanVerify = true;
    trial.Restore();
    Check(trial.Original == null && !shell.State);
});
foreach (bool initial in new[] { false, true })
{
    Test($"Apply, verify and restore x20 from {initial}", dir =>
    {
        var shell = new FakeShell(initial);
        var trial = new Trial(shell, dir);
        for (int i = 0; i < 20; i++)
        {
            trial.Apply(!initial);
            Check(trial.Original == initial && shell.State != initial);
            Fails<InvalidOperationException>(() => trial.Apply(initial));
            trial.Restore();
            Check(shell.State == initial && trial.Original == null);
        }
        Check(shell.Writes == 40);
    });
}
Test("Already requested state is a no-op", dir =>
{
    var shell = new FakeShell(false);
    var trial = new Trial(shell, dir);
    trial.Apply(false);
    Check(shell.Writes == 0 && trial.Original == null);
});
Test("An external change survives restoration", dir =>
{
    var shell = new FakeShell(false);
    var trial = new Trial(shell, dir);
    trial.Apply(true);
    shell.State = false;
    trial.Restore();
    Check(shell.Writes == 1 && !shell.State && trial.Original == null);
});
foreach (bool keep in new[] { false, true })
{
    Test($"Reopen pending transaction, keep={keep}", dir =>
    {
        var shell = new FakeShell(false);
        new Trial(shell, dir).Apply(true);
        var reopened = new Trial(shell, dir);
        Check(reopened.Original == false && shell.Writes == 1);
        if (keep) reopened.Keep(); else reopened.Restore();
        Check(shell.State == keep && reopened.Original == null);
    });
}
Test("Failed write leaves recoverable journal", dir =>
{
    var shell = new FakeShell(true) { ThrowOnWrite = true };
    var trial = new Trial(shell, dir);
    Fails<IOException>(() => trial.Apply(false));
    Check(trial.Original == true);
    new Trial(shell, dir).Restore();
    Check(shell.State && trial.Original == null);
});
Test("Unconfirmed change and failed restoration retain intent", dir =>
{
    var shell = new FakeShell(false) { IgnoreWrite = true };
    var trial = new Trial(shell, dir);
    Fails<InvalidOperationException>(() => trial.Apply(true));
    shell.State = true;
    Fails<InvalidOperationException>(trial.Restore);
    Check(trial.Original == false);
    shell.IgnoreWrite = false;
    trial.Restore();
    Check(!shell.State && trial.Original == null);
});
Test("Damaged journal blocks writes", dir =>
{
    File.WriteAllText(Path.Combine(dir, "pending.v1"), "unknown");
    var shell = new FakeShell(false);
    var trial = new Trial(shell, dir);
    Fails<InvalidDataException>(() => trial.Apply(true));
    Fails<InvalidDataException>(trial.Restore);
    trial.Keep();
    Check(shell.Writes == 0 && trial.Original == null);
});
Test("Dock placement with offset work areas and scaling", dir =>
{
    foreach (int scale in new[] { 100, 125, 150, 200 })
    foreach (int left in new[] { -1920, 0, 1920 })
    {
        var area = new Rectangle(left, 42, 1600, 900);
        var window = new Size(350 * scale / 100, 90 * scale / 100);
        var point = DockPlacement.BottomCenter(area, window, scale / 10);
        Check(area.Contains(new Rectangle(point, window)) && point.Y == area.Bottom - window.Height - scale / 10);
    }
});
Console.WriteLine($"{passed} tests passed.");

internal sealed class FakeShell(bool state) : ITaskbar
{
    public bool State = state, IgnoreWrite, ThrowOnWrite;
    public bool CanVerify = true;
    public int Writes;
    public bool Read() => CanVerify ? State : throw new InvalidOperationException("State masked");
    public nuint Set(bool value)
    {
        Writes++;
        if (ThrowOnWrite) throw new IOException();
        if (!IgnoreWrite) State = value;
        return 1;
    }
}
