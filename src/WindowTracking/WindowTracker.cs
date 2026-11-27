using System.Collections.Concurrent;
using System.Threading;

namespace WindowTracking;

/// <summary>
/// Owns a WinEvent message thread and publishes effective window snapshot changes.
/// HWNDs are paired with process IDs during reconciliation because Windows can reuse them.
/// </summary>
public sealed class WindowTracker : IDisposable
{
    private const int QueueCapacity = 1024;
    private readonly WindowEnumerator enumerator;
    private readonly IWindowEventSource eventSource;
    private readonly ConcurrentQueue<WindowSignal> pending = new();
    private readonly AutoResetEvent signalReady = new(false);
    private readonly ManualResetEvent stop = new(false);
    private readonly object lifecycle = new();
    private readonly Dictionary<WindowKey, TrackedWindow> current = [];
    private Thread? hookThread;
    private Thread? consumerThread;
    private bool disposed;

    private readonly record struct WindowKey(nint Id, int ProcessId);

    public WindowTracker() : this(new WindowEnumerator(), new WinEventSource()) { }

    internal WindowTracker(WindowEnumerator enumerator, IWindowEventSource eventSource)
    {
        this.enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
        this.eventSource = eventSource ?? throw new ArgumentNullException(nameof(eventSource));
    }

    public event EventHandler<WindowChange>? Changed;
    public event EventHandler<Exception>? TrackingError;
    public event EventHandler? Started;

    public IReadOnlyList<TrackedWindow> Snapshot
    {
        get { lock (lifecycle) return current.Values.ToArray(); }
    }

    public bool IsRunning { get { lock (lifecycle) return hookThread is { IsAlive: true } && consumerThread is { IsAlive: true }; } }

    public void Start()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (hookThread is { IsAlive: true } || consumerThread is { IsAlive: true }) return;
            stop.Reset();
            consumerThread = new Thread(Consume) { IsBackground = true, Name = "Taskbar Studio window state" };
            hookThread = new Thread(RunHooks) { IsBackground = true, Name = "Taskbar Studio window events" };
            consumerThread.Start();
            hookThread.Start();
        }
    }

    /// <summary>Schedules a one-shot full enumeration on the hook thread.</summary>
    public void Reconcile() => Enqueue(new WindowSignal(WindowSignalKind.Reconcile, 0));

    private void RunHooks()
    {
        try
        {
            eventSource.Run(OnSignal, stop);
        }
        catch (Exception ex)
        {
            TrackingError?.Invoke(this, ex);
        }
        finally
        {
            stop.Set();
            signalReady.Set();
        }
    }

    private void Consume()
    {
        while (!stop.WaitOne(0) || !pending.IsEmpty)
        {
            signalReady.WaitOne(100);
            while (pending.TryDequeue(out var signal))
            {
                try { Process(signal); }
                catch (Exception ex) { TrackingError?.Invoke(this, ex); }
            }
        }
    }

    private void OnSignal(WindowSignal signal)
    {
        if (signal.Kind == WindowSignalKind.Reconcile && Snapshot.Count == 0)
            Started?.Invoke(this, EventArgs.Empty);
        Enqueue(signal);
    }

    private void Enqueue(WindowSignal signal)
    {
        if (stop.WaitOne(0)) return;
        if (pending.Count >= QueueCapacity)
        {
            while (pending.TryDequeue(out _)) { }
            signal = new WindowSignal(WindowSignalKind.Reconcile, 0);
        }
        pending.Enqueue(signal);
        signalReady.Set();
    }

    private void Process(WindowSignal signal)
    {
        var snapshot = enumerator.Enumerate();
        var next = snapshot.ToDictionary(window => new WindowKey(window.Id, window.ProcessId));
        WindowChange change;
        lock (lifecycle)
        {
            var added = next.Where(pair => !current.ContainsKey(pair.Key)).Select(pair => pair.Value).ToArray();
            var removed = current.Where(pair => !next.ContainsKey(pair.Key)).Select(pair => pair.Value).ToArray();
            var updated = next.Where(pair => current.TryGetValue(pair.Key, out var old) && old != pair.Value)
                .Select(pair => new WindowUpdate(current[pair.Key], pair.Value)).ToArray();
            change = new WindowChange(added, removed, updated);
            current.Clear();
            foreach (var pair in next) current.Add(pair.Key, pair.Value);
        }
        if (change.Added.Count + change.Removed.Count + change.Updated.Count != 0)
            Changed?.Invoke(this, change);
    }

    public void Dispose()
    {
        Thread? hookWorker;
        Thread? consumerWorker;
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            stop.Set();
            eventSource.Dispose();
            signalReady.Set();
            hookWorker = hookThread;
            consumerWorker = consumerThread;
        }
        if (hookWorker != Thread.CurrentThread) hookWorker?.Join(TimeSpan.FromSeconds(3));
        signalReady.Set();
        if (consumerWorker != Thread.CurrentThread) consumerWorker?.Join(TimeSpan.FromSeconds(3));
        eventSource.Dispose();
        signalReady.Dispose();
        stop.Dispose();
    }
}
