using System.Runtime.InteropServices;

namespace NativeTrial;

internal sealed class ShellTaskbar(Func<nint> owner) : ITaskbar
{
    internal const string PositionLimitation =
        "En la compilación 26200, la prueba de ocultación requiere la barra en el borde inferior.";
    [StructLayout(LayoutKind.Sequential)]
    internal struct AppBarData
    {
        public uint Size;
        public nint Window;
        public uint Callback, Edge;
        public int Left, Top, Right, Bottom;
        public nint Parameter;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);

    private const uint GetState = 4, GetTaskbarPos = 5, SetState = 10;
    private const uint AutoHide = 1, AlwaysOnTop = 2;

    private AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = owner() };

    private AppBarData Position()
    {
        var data = Data();
        if (SHAppBarMessage(GetTaskbarPos, ref data) == 0)
            throw new InvalidOperationException("La barra de Windows no está disponible. Vuelva a consultar.");
        return data;
    }

    internal static bool AtBottom(AppBarData position, int primaryHeight) =>
        primaryHeight > 0 && position.Top > 0 && position.Bottom >= primaryHeight &&
        position.Bottom > position.Top && position.Right - position.Left > position.Bottom - position.Top;

    private (uint Flags, bool CanVerify) State()
    {
        var before = Position();
        var data = Data();
        uint flags = (uint)SHAppBarMessage(GetState, ref data);
        var after = Position(); // Zero in GETSTATE also denotes a valid state.
        if (Environment.OSVersion.Version.Build != 26200) return (flags, true);
        int height = GetSystemMetrics(1); // SM_CYSCREEN, primary monitor, PerMonitorV2 process
        return (flags, AtBottom(before, height) && AtBottom(after, height));
    }

    public (bool AutoHide, bool CanVerify) Inspect()
    {
        var state = State();
        return ((state.Flags & AutoHide) != 0, state.CanVerify);
    }

    private uint VerifiableFlags()
    {
        var state = State();
        if (!state.CanVerify) throw new InvalidOperationException(PositionLimitation);
        return state.Flags;
    }

    public bool Read() => (VerifiableFlags() & AutoHide) != 0;

    public nuint Set(bool enabled)
    {
        uint flags = VerifiableFlags() & AlwaysOnTop;
        var data = Data();
        data.Parameter = (nint)(flags | (enabled ? AutoHide : 0));
        nuint result = SHAppBarMessage(SetState, ref data);
        if (result == 0)
            throw new InvalidOperationException("ABM_SETSTATE devolvió 0. Se conserva el registro.");
        return result;
    }
}
