using System.Drawing;

namespace AppBarPrototype;

internal enum AppBarEdge : uint { Left = 0, Top = 1, Right = 2, Bottom = 3 }

internal interface IAppBarPlatform
{
    bool Register(nint hwnd, uint callbackMessage);
    bool Query(nint hwnd, AppBarEdge edge, ref Rectangle rectangle);
    bool SetPosition(nint hwnd, AppBarEdge edge, ref Rectangle rectangle);
    bool Move(nint hwnd, Rectangle rectangle);
    bool Remove(nint hwnd);
    void PositionChanged(nint hwnd);
}

/// <summary>Reversible ABM_NEW/QUERYPOS/SETPOS/REMOVE protocol.</summary>
internal sealed class AppBarRegistration : IDisposable
{
    private readonly IAppBarPlatform platform;
    private readonly nint hwnd;
    private readonly AppBarEdge edge;
    private bool registered;
    private bool disposed;
    private int currentThickness;
    public bool IsRegistered => registered;
    public Rectangle ActualBounds { get; private set; }

    public AppBarRegistration(IAppBarPlatform platform, nint hwnd, AppBarEdge edge, int thickness)
    {
        this.platform = platform;
        this.hwnd = hwnd;
        this.edge = edge;
        currentThickness = thickness;
    }

    public bool Start(uint callbackMessage, Rectangle monitorBounds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (registered) return true;
        if (hwnd == 0 || currentThickness <= 0 || monitorBounds.Width <= 0 || monitorBounds.Height <= 0) return false;
        if (!platform.Register(hwnd, callbackMessage)) return false;
        registered = true;
        bool positioned;
        try { positioned = Negotiate(monitorBounds); }
        catch
        {
            // Do not lose track of registration if setup throws. Caller can retry removal.
            if (platform.Remove(hwnd)) registered = false;
            throw;
        }
        if (positioned) return true;
        // Do not leave a reservation when setup fails. Keep the error visible to caller.
        if (!platform.Remove(hwnd)) return false;
        registered = false;
        return false;
    }

    public bool Reposition(Rectangle monitorBounds) => registered && !disposed && Negotiate(monitorBounds);
    public void SetThickness(int value) { if (value > 0) currentThickness = value; }

    public void NotifyWindowPositionChanged()
    {
        if (registered && !disposed) platform.PositionChanged(hwnd);
    }

    private bool Negotiate(Rectangle monitor)
    {
        var proposed = edge switch
        {
            AppBarEdge.Left => new Rectangle(monitor.Left, monitor.Top, Math.Min(currentThickness, monitor.Width), monitor.Height),
            AppBarEdge.Right => new Rectangle(monitor.Right - Math.Min(currentThickness, monitor.Width), monitor.Top, Math.Min(currentThickness, monitor.Width), monitor.Height),
            AppBarEdge.Top => new Rectangle(monitor.Left, monitor.Top, monitor.Width, Math.Min(currentThickness, monitor.Height)),
            _ => new Rectangle(monitor.Left, monitor.Bottom - Math.Min(currentThickness, monitor.Height), monitor.Width, Math.Min(currentThickness, monitor.Height))
        };
        if (!platform.Query(hwnd, edge, ref proposed) || proposed.Width <= 0 || proposed.Height <= 0) return false;
        if (!platform.SetPosition(hwnd, edge, ref proposed) || proposed.Width <= 0 || proposed.Height <= 0) return false;
        if (!platform.Move(hwnd, proposed)) return false;
        ActualBounds = proposed;
        return true;
    }

    public bool Stop()
    {
        if (!registered) return true;
        if (!platform.Remove(hwnd)) return false;
        registered = false;
        ActualBounds = Rectangle.Empty;
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        if (!Stop()) throw new InvalidOperationException("ABM_REMOVE failed; keep the prototype open and retry cleanup.");
        disposed = true;
    }
}

internal sealed class AppBarProtocolTests
{
    public static void Run()
    {
        if (System.Runtime.InteropServices.Marshal.SizeOf<Win32AppBarPlatform.APPBARDATA>() != 48)
            throw new Exception("APPBARDATA x64 layout mismatch.");
        var fake = new FakePlatform();
        using (var bar = new AppBarRegistration(fake, 42, AppBarEdge.Left, 72))
        {
            if (!bar.Start(0x8001, new Rectangle(0, 0, 1920, 1080)) || !bar.IsRegistered) throw new Exception("AppBar did not register.");
            if (bar.ActualBounds != new Rectangle(0, 0, 72, 1080)) throw new Exception("Negotiated geometry mismatch.");
            if (!bar.Reposition(new Rectangle(0, 0, 1920, 1080)) || fake.MoveCount != 2) throw new Exception("Reposition failed.");
            bar.NotifyWindowPositionChanged();
            if (fake.PositionNotifications != 1 || !bar.Stop() || bar.IsRegistered || fake.RemoveCount != 1) throw new Exception("AppBar cleanup failed.");
        }
        var rejected = new FakePlatform { AcceptRegistration = false };
        using var notRegistered = new AppBarRegistration(rejected, 43, AppBarEdge.Left, 72);
        if (notRegistered.Start(0x8001, new Rectangle(0, 0, 1920, 1080)) || rejected.RemoveCount != 0) throw new Exception("Registration rejection mishandled.");
        var failedQuery = new FakePlatform { AcceptQuery = false };
        using var recovered = new AppBarRegistration(failedQuery, 44, AppBarEdge.Left, 72);
        if (recovered.Start(0x8001, new Rectangle(0, 0, 1920, 1080)) || recovered.IsRegistered || failedQuery.RemoveCount != 1) throw new Exception("Failed setup left reservation.");
        var failedRemoval = new FakePlatform { AcceptQuery = false, AcceptRemoval = false };
        var retained = new AppBarRegistration(failedRemoval, 45, AppBarEdge.Left, 72);
        if (retained.Start(0x8001, new Rectangle(0, 0, 1920, 1080)) || !retained.IsRegistered) throw new Exception("Failed removal lost registration state.");
        failedRemoval.AcceptRemoval = true;
        if (!retained.Stop() || retained.IsRegistered) throw new Exception("Removal retry failed.");
    }

    private sealed class FakePlatform : IAppBarPlatform
    {
        public bool AcceptRegistration = true, AcceptQuery = true, AcceptRemoval = true;
        public int MoveCount, RemoveCount, PositionNotifications;
        public bool Register(nint hwnd, uint callbackMessage) => AcceptRegistration;
        public bool Query(nint hwnd, AppBarEdge edge, ref Rectangle rectangle) => AcceptQuery;
        public bool SetPosition(nint hwnd, AppBarEdge edge, ref Rectangle rectangle) => true;
        public bool Move(nint hwnd, Rectangle rectangle) { MoveCount++; return true; }
        public bool Remove(nint hwnd) { RemoveCount++; return AcceptRemoval; }
        public void PositionChanged(nint hwnd) => PositionNotifications++;
    }
}
