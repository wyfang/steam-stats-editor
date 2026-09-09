// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Collections.Generic;
using SAM.Batch;
using SAM.Submission;

internal static class Program
{
    private static int _Passed;
    private static void Main()
    {
        Test("No write before fresh read; callback identity", () =>
        {
            var f = new Fixture(); f.Start();
            Equal(1, f.Transport.Reads); Equal(0, f.Transport.Sets); Equal(0, f.Transport.Stores);
            False(f.Controller.HandleStatsReceived(f.Controller.PendingReadHandle, 999, 7, 1, () => f.Snapshot(0)));
            False(f.Controller.HandleStatsReceived(f.Controller.PendingReadHandle, 42, 999, 1, () => f.Snapshot(0)));
            False(f.Controller.HandleStatsReceived(99999, 42, 7, 1, () => f.Snapshot(0)));
            Equal(0, f.Transport.Sets);
            f.Read(0); Equal(1, f.Transport.Stores); Equal(10d, f.Controller.AttemptedValues["count"]);
            False(f.Controller.Start(42, 7, SubmissionMode.Automatic, 120, f.Snapshot(0), f.Targets, new Dictionary<string, bool>()));
            Equal(1, f.Transport.Stores);
        });
        Test("Success needs Stored then matching readback", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0);
            Equal(0, f.Controller.ConfirmedRounds); Equal(0d, f.Controller.ConfirmedValues["count"]);
            f.Stored(1); Equal(0, f.Controller.ConfirmedRounds); Equal(SubmissionPhase.AwaitRead, f.Controller.Phase);
            f.Read(10); Equal(1, f.Controller.ConfirmedRounds); Equal(SubmissionPhase.Waiting, f.Controller.Phase);
            Equal(25d, f.Controller.Targets["count"]);
            f.Time += 119; f.Controller.Tick(); Equal(2, f.Transport.Reads);
            f.Time += 1; f.Controller.Tick(); Equal(3, f.Transport.Reads); Equal(1, f.Transport.Stores);
            f.Read(12); Equal(22d, f.Controller.AttemptedValues["count"]);
        });
        Test("Single step retains final target and stops", () =>
        {
            var f = new Fixture(); f.Start(SubmissionMode.SingleStep); f.Read(0); f.Stored(1); f.Read(10);
            Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(10d, f.Controller.ConfirmedValues["count"]);
            Equal(25d, f.Controller.Targets["count"]); f.Time += 1000; f.Controller.Tick(); Equal(1, f.Transport.Stores);
        });
        Test("Regular refuses excessive step; automatic refuses achievements", () =>
        {
            var f = new Fixture(); False(f.Start(SubmissionMode.Regular)); Equal(0, f.Transport.Reads);
            False(f.Controller.Start(42, 7, SubmissionMode.Automatic, 120, f.Snapshot(0), f.Targets,
                new Dictionary<string, bool> { ["award"] = true })); Equal(0, f.Transport.Sets);
        });
        Test("Achievement regular submission shares one tracked store", () =>
        {
            var f = new Fixture(); f.Targets["count"] = 5;
            True(f.Controller.Start(42, 7, SubmissionMode.Regular, 120, f.Snapshot(0), f.Targets,
                new Dictionary<string, bool> { ["award"] = true }));
            f.Read(0); Equal(1, f.Transport.Stores); Equal(1, f.Transport.AchievementSets);
            f.Stored(1); f.Read(5, true); Equal(1, f.Controller.ConfirmedRounds); Equal(SubmissionPhase.Idle, f.Controller.Phase);
        });
        Test("Partial local SetStat failure never stores and quarantines", () =>
        {
            var f = new Fixture { TwoStats = true }; f.Targets["second"] = 10; f.Transport.FailSetNumber = 2;
            f.Start(); f.Read(0); Equal(2, f.Transport.Sets); Equal(0, f.Transport.Stores);
            Equal(SubmissionPhase.Quarantined, f.Controller.Phase); True(f.Controller.HasUnconfirmedWrites);
            False(f.Start());
        });
        Test("Store false is unknown; cache is not claimed rolled back", () =>
        {
            var f = new Fixture(); f.Transport.StoreReturn = false; f.Start(); f.Read(0);
            Equal(SubmissionPhase.Quarantined, f.Controller.Phase); True(f.Controller.HasUnconfirmedWrites);
            True(f.Controller.Status.Contains("false")); Equal(0, f.Controller.ConfirmedRounds);
        });
        Test("Timeout permanently quarantines this instance and ignores late callback", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0); f.Time += 60; f.Controller.Tick();
            Equal(SubmissionPhase.Quarantined, f.Controller.Phase); int reads = f.Transport.Reads;
            f.Stored(1); Equal(reads, f.Transport.Reads); False(f.Start());
        });
        Test("Read timeout also cannot misattribute late reply", () =>
        {
            var f = new Fixture(); f.Start(); f.Time = 60; f.Controller.Tick(); f.Read(0);
            Equal(SubmissionPhase.Quarantined, f.Controller.Phase); Equal(0, f.Transport.Sets);
        });
        Test("Stop in flight keeps current verification but sends no next round", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0); f.Controller.RequestStop();
            Equal(SubmissionPhase.AwaitStored, f.Controller.Phase); f.Stored(1); f.Read(10);
            Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(1, f.Controller.ConfirmedRounds);
            f.Time += 10000; f.Controller.Tick(); Equal(1, f.Transport.Stores);
        });
        Test("Stop initial read sends no mutations", () =>
        {
            var f = new Fixture(); f.Start(); f.Controller.RequestStop(); f.Read(0);
            Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(0, f.Transport.Sets);
        });
        Test("Unexpected readback stops with actual value retained", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0); f.Stored(1); f.Read(4);
            Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(0, f.Controller.ConfirmedRounds);
            Equal(4d, f.Controller.ConfirmedValues["count"]); Equal(10d, f.Controller.AttemptedValues["count"]);
        });
        Test("Schema change before first write rejects", () =>
        {
            var f = new Fixture(); f.Start(); f.MaxChange = 5; f.Read(0);
            Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(0, f.Transport.Sets);
        });
        Test("Unavailable attempted field quarantines without inventing confirmation", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0); f.Stored(1); f.Available = false; f.Read(0);
            Equal(SubmissionPhase.Quarantined, f.Controller.Phase); Equal(0, f.Controller.ConfirmedRounds);
        });
        Test("InvalidParam re-reads corrected base and limits replanning", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0); f.Stored(8); f.Read(3);
            Equal(SubmissionPhase.Waiting, f.Controller.Phase); Equal(3d, f.Controller.ConfirmedValues["count"]);
            f.Time += 120; f.Controller.Tick(); f.Read(4); Equal(14d, f.Controller.AttemptedValues["count"]);
            f.Stored(8); f.Read(4); f.Time += 120; f.Controller.Tick(); f.Read(4);
            f.Stored(8); f.Read(4); Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(3, f.Controller.CorrectionRetries);
        });
        Test("Busy and RateLimitExceeded back off and stop after five retries", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0);
            for (int rejection = 1; rejection <= 6; rejection++)
            {
                f.Stored(rejection % 2 == 0 ? 10 : 84); f.Read(0);
                if (rejection == 6) break;
                Equal(SubmissionPhase.Waiting, f.Controller.Phase);
                int reads = f.Transport.Reads; int delay = 120 * (1 << rejection);
                f.Time += delay - 1; f.Controller.Tick(); Equal(reads, f.Transport.Reads);
                f.Time += 1; f.Controller.Tick(); Equal(reads + 1, f.Transport.Reads); f.Read(0);
            }
            Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(6, f.Transport.Stores); Equal(0, f.Controller.ConfirmedRounds);
        });
        Test("Permanent and unknown errors read back once then stop", () =>
        {
            foreach (int result in new[] { 0, 2, 15, 25, 999 })
            {
                var f = new Fixture(); f.Start(); f.Read(0); f.Stored(result);
                Equal(SubmissionPhase.AwaitRecoveryRead, f.Controller.Phase); f.Read(0);
                Equal(SubmissionPhase.Idle, f.Controller.Phase); Equal(0, f.Controller.TransientRetries);
                f.Time += 100000; f.Controller.Tick(); Equal(1, f.Transport.Stores);
            }
        });
        Test("Failed recovery read quarantines uncertain write", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0); f.Stored(84);
            f.Controller.HandleStatsReceived(f.Controller.PendingReadHandle, 42, 7, 2, () => throw new Exception("must not read"));
            Equal(SubmissionPhase.Quarantined, f.Controller.Phase); Equal(1, f.Transport.Stores);
        });
        Test("Stop while retry is pending prevents resend", () =>
        {
            var f = new Fixture(); f.Start(); f.Read(0); f.Stored(84); f.Controller.RequestStop(); f.Read(0);
            Equal(SubmissionPhase.Idle, f.Controller.Phase); f.Time += 10000; f.Controller.Tick(); Equal(1, f.Transport.Stores);
        });
        Test("Reset uses implicit store callback and never double-stores", () =>
        {
            var f = new Fixture(); True(f.Controller.StartReset(42, 7, f.Snapshot(10), true));
            Equal(1, f.Transport.Resets); Equal(0, f.Transport.Stores); f.Stored(1); f.Read(0);
            Equal(SubmissionPhase.Idle, f.Controller.Phase);
        });
        Test("Late duplicate read cannot satisfy a different request handle", () =>
        {
            var f = new Fixture(); f.Start(); ulong firstHandle = f.Controller.PendingReadHandle;
            f.Read(0); f.Stored(1); ulong nextHandle = f.Controller.PendingReadHandle;
            True(firstHandle != nextHandle);
            False(f.Controller.HandleStatsReceived(firstHandle, 42, 7, 1, () => throw new Exception("stale payload must not be read")));
            Equal(SubmissionPhase.AwaitRead, f.Controller.Phase); Equal(0, f.Controller.ConfirmedRounds);
            f.Read(10); Equal(1, f.Controller.ConfirmedRounds);
        });
        Test("Explicit sixty-second interval is honored", () =>
        {
            var f = new Fixture();
            True(f.Controller.Start(42, 7, SubmissionMode.Automatic, 60, f.Snapshot(0), f.Targets, new Dictionary<string, bool>()));
            f.Read(0); f.Stored(1); f.Read(10); f.Time = 59; f.Controller.Tick(); Equal(2, f.Transport.Reads);
            f.Time = 60; f.Controller.Tick(); Equal(3, f.Transport.Reads);
        });
        Test("Native read payload handles tail padding without moving SteamID", () =>
        {
            foreach (int length in new[] { 20, 24 })
            {
                byte[] bytes = new byte[length];
                Array.Copy(BitConverter.GetBytes(42UL), 0, bytes, 0, 8);
                Array.Copy(BitConverter.GetBytes(8), 0, bytes, 8, 4);
                Array.Copy(BitConverter.GetBytes(0x1234567887654321UL), 0, bytes, 12, 8);
                if (length == 24) for (int i = 20; i < 24; i++) bytes[i] = 255;
                var decoded = SAM.API.StatsCallResultDecoder.Decode(bytes);
                Equal(42UL, decoded.GameId); Equal(8, decoded.Result); Equal(0x1234567887654321UL, decoded.SteamIdUser);
            }
            foreach (int length in new[] { 0, 16, 19, 21, 32 })
            {
                bool rejected = false;
                try { SAM.API.StatsCallResultDecoder.Decode(new byte[length]); }
                catch (ArgumentException) { rejected = true; }
                True(rejected);
            }
        });
        Test("Schema float constraints never underflow or silently round into wider permission", () =>
        {
            foreach (string token in new[] { "1e-999", "1e-50", "NaN", "Infinity", "16777217", "100.0000000001", "18446744073709551615", "1,5" })
                False(SchemaConstraintParser.TryFloat(token, out _));
            foreach (string token in new[] { "0", "0.0", "100.000", "1e2", "0.1", "0.10000000149011612", "16777216", "1E-45" })
                True(SchemaConstraintParser.TryFloat(token, out _));
        });
        Console.WriteLine($"PASS {_Passed} submission tests (offline fake transport; no Steam calls).");
    }

    private static void Test(string name, Action action)
    {
        try { action(); _Passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); Environment.Exit(1); }
    }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, actual {actual}"); }
    private static void True(bool value) { if (!value) throw new Exception("Expected true"); }
    private static void False(bool value) => True(!value);

    private sealed class Fixture
    {
        public readonly FakeTransport Transport = new();
        public readonly SubmissionCoordinator Controller;
        public readonly Dictionary<string, double> Targets = new() { ["count"] = 25 };
        public double Time;
        public double MaxChange = 10;
        public bool Available = true;
        public bool TwoStats;
        public Fixture() { Controller = new SubmissionCoordinator(Transport, () => Time); }
        public SubmissionSnapshot Snapshot(double value, bool achievement = false)
        {
            var stats = new List<StatDescriptor> { new() { Id = "count", Kind = StatValueKind.Integer,
                CurrentValue = value, Minimum = 0, Maximum = 100, MaxChange = MaxChange, IsAvailable = Available } };
            if (TwoStats) stats.Add(new StatDescriptor { Id = "second", Kind = StatValueKind.Integer, CurrentValue = 0, Minimum = 0, Maximum = 100, MaxChange = 10 });
            return new SubmissionSnapshot(stats, new Dictionary<string, AchievementValue>
            { ["award"] = new() { Value = achievement, IsAvailable = true } });
        }
        public bool Start(SubmissionMode mode = SubmissionMode.Automatic) => Controller.Start(42, 7, mode, 120, Snapshot(0), Targets, new Dictionary<string, bool>());
        public void Read(double value, bool achievement = false) => Controller.HandleStatsReceived(Controller.PendingReadHandle, 42, 7, 1, () => Snapshot(value, achievement));
        public void Stored(int result) => Controller.HandleStatsStored(42, result);
    }

    private sealed class FakeTransport : ISubmissionTransport
    {
        public int Reads, Sets, Stores, AchievementSets, Resets;
        public int FailSetNumber;
        public bool StoreReturn = true;
        public ulong RequestStats() { Reads++; return (ulong)Reads; }
        public bool SetStat(string id, StatValueKind kind, double value) { Sets++; return Sets != FailSetNumber; }
        public bool SetAchievement(string id, bool value) { AchievementSets++; return true; }
        public bool StoreStats() { Stores++; return StoreReturn; }
        public bool ResetAllStats(bool achievementsToo) { Resets++; return true; }
    }
}
