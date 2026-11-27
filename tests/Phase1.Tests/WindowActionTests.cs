using WindowActions;
using WindowTracking;

namespace Phase1.Tests;

internal static partial class Program
{
    private static void TestWindowActions()
    {
        Test("Window actions activate, restore, minimize and honor foreground denial", _ =>
        {
            var target = Target();
            var platform = new FakeActionPlatform(target);
            var service = new WindowActionService(platform);
            Check(service.Activate(target).Status == WindowActionStatus.Accepted && platform.ForegroundCalls == 1,
                "Activation was not requested");

            platform.ForegroundAllowed = false;
            Check(service.Activate(target).Status == WindowActionStatus.ForegroundDenied,
                "SetForegroundWindow denial was not surfaced");
            platform.ForegroundAllowed = true;

            platform.Iconic = true;
            int beforeRestore = platform.RestoreCalls;
            Check(service.Activate(target).Status == WindowActionStatus.Accepted &&
                platform.RestoreCalls == beforeRestore + 1 && !platform.Iconic,
                "Activation did not restore a minimized target first");

            Check(service.Minimize(target).Status == WindowActionStatus.Accepted && platform.Iconic,
                "Minimize request was not issued");
            Check(service.Minimize(target).Status == WindowActionStatus.AlreadyInRequestedState,
                "Repeated minimize did not remain idempotent");
            Check(service.Restore(target).Status == WindowActionStatus.Accepted && !platform.Iconic,
                "Restore request was not issued");
            Check(service.Restore(target).Status == WindowActionStatus.AlreadyInRequestedState,
                "Repeated restore did not remain idempotent");
        });

        Test("Window actions reject stale, ineligible, protected and disappearing targets", _ =>
        {
            var target = Target();
            var platform = new FakeActionPlatform(target);
            var service = new WindowActionService(platform);
            platform.Alive = false;
            Check(service.Minimize(target).Status == WindowActionStatus.WindowUnavailable && platform.MinimizeCalls == 0,
                "Closed HWND received an action");
            platform.Alive = true;

            platform.Candidate = platform.Candidate with { ProcessId = target.ProcessId + 1 };
            Check(service.Activate(target).Status == WindowActionStatus.WindowChanged && platform.ForegroundCalls == 0,
                "Reused HWND with a different process was accepted");
            platform.Candidate = platform.Candidate with { ProcessId = target.ProcessId, ThreadId = target.ThreadId + 1 };
            Check(service.Activate(target).Status == WindowActionStatus.WindowChanged,
                "Reused HWND with a different UI thread was accepted");
            platform.Candidate = Eligible(target);

            foreach (var invalid in new[]
            {
                platform.Candidate with { IsVisible = false },
                platform.Candidate with { IsOwnProcess = true },
                platform.Candidate with { IsCloaked = true },
                platform.Candidate with { IsToolWindow = true }
            })
            {
                platform.Candidate = invalid;
                Check(service.Minimize(target).Status == WindowActionStatus.NotEligible,
                    "Ineligible window received an action");
            }
            platform.Candidate = Eligible(target);
            platform.IntegrityAllowed = false;
            Check(service.Restore(target).Status == WindowActionStatus.NotEligible,
                "Higher-integrity target was not rejected");
            platform.IntegrityAllowed = true;

            platform.Iconic = true;
            platform.DisappearOnRestore = true;
            Check(service.Activate(target).Status == WindowActionStatus.WindowUnavailable && platform.ForegroundCalls == 0,
                "Target disappearing during restore received foreground activation");

            var invalidIdentity = target with { ThreadId = 0 };
            Check(service.Activate(invalidIdentity).Status == WindowActionStatus.WindowUnavailable,
                "Target without thread identity was accepted");
            platform.Alive = true;
            platform.DisappearOnRestore = false;
            platform.Iconic = false;
            platform.ThrowOnInspect = true;
            Check(service.Minimize(target).Status == WindowActionStatus.Failed,
                "Native inspection failure was not contained");
        });
    }

    private static TrackedWindow Target() => new((nint)0x711, "Test window", 7711, "safe-test", false, false, 77);

    private static WindowCandidate Eligible(TrackedWindow target) => new(target.Id, target.Id, target.Title,
        target.ProcessId, target.ProcessName, true, false, false, false, false, false, null, target.ThreadId);

    private sealed class FakeActionPlatform(TrackedWindow target) : IWindowActionPlatform
    {
        internal WindowCandidate Candidate { get; set; } = Eligible(target);
        internal bool Alive { get; set; } = true;
        internal bool Iconic { get; set; }
        internal bool ForegroundAllowed { get; set; } = true;
        internal bool IntegrityAllowed { get; set; } = true;
        internal bool DisappearOnRestore { get; set; }
        internal bool ThrowOnInspect { get; set; }
        internal int ForegroundCalls { get; private set; }
        internal int RestoreCalls { get; private set; }
        internal int MinimizeCalls { get; private set; }

        public bool IsWindow(nint id) => Alive && id == target.Id;
        public WindowCandidate? Inspect(nint id)
        {
            if (ThrowOnInspect) throw new InvalidOperationException("Simulated native query failure.");
            return IsWindow(id) ? Candidate : null;
        }
        public bool IsSameOrLowerIntegrity(int processId) => IntegrityAllowed && processId == target.ProcessId;
        public bool IsIconic(nint id) => Iconic;
        public void RestoreAsync(nint id)
        {
            RestoreCalls++;
            if (DisappearOnRestore) Alive = false;
            else Iconic = false;
        }
        public void MinimizeAsync(nint id) { MinimizeCalls++; Iconic = true; }
        public bool SetForegroundWindow(nint id) { ForegroundCalls++; return ForegroundAllowed; }
    }
}
