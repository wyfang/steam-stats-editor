// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SAM.Batch;

namespace SAM.Submission
{
    public enum SubmissionPhase { Idle, AwaitInitialRead, SettingRound, AwaitStored, AwaitRead, AwaitRecoveryRead, Waiting, Quarantined }
    public enum SubmissionMode { SingleStep, Automatic, Regular }

    public interface ISubmissionTransport
    {
        ulong RequestStats();
        bool SetStat(string id, StatValueKind kind, double value);
        bool SetAchievement(string id, bool value);
        bool StoreStats();
        bool ResetAllStats(bool achievementsToo);
    }

    public sealed class AchievementValue
    {
        public bool Value { get; set; }
        public bool IsAvailable { get; set; }
        public int Permission { get; set; }
    }

    public sealed class SubmissionSnapshot
    {
        public IReadOnlyList<StatDescriptor> Stats { get; }
        public IReadOnlyDictionary<string, AchievementValue> Achievements { get; }
        public SubmissionSnapshot(IEnumerable<StatDescriptor> stats, IDictionary<string, AchievementValue> achievements)
        {
            Stats = stats.ToList().AsReadOnly();
            Achievements = new ReadOnlyDictionary<string, AchievementValue>(
                new Dictionary<string, AchievementValue>(achievements, StringComparer.Ordinal));
        }
    }

    // All methods run on one event-loop thread. The transport is deliberately small so that
    // real callback order, failures, partial local writes and retries can be tested offline.
    public sealed class SubmissionCoordinator
    {
        private const int TimeoutSeconds = 60;
        private const int MaxTransientRetries = 5;
        private const int MaxCorrectionRetries = 2;
        private readonly ISubmissionTransport _Transport;
        private readonly Func<double> _Now;
        private Dictionary<string, double> _Targets = new(StringComparer.Ordinal);
        private Dictionary<string, bool> _AchievementTargets = new(StringComparer.Ordinal);
        private Dictionary<string, double> _Attempted = new(StringComparer.Ordinal);
        private Dictionary<string, bool> _AttemptedAchievements = new(StringComparer.Ordinal);
        private Dictionary<string, double> _Confirmed = new(StringComparer.Ordinal);
        private Dictionary<string, StatDescriptor> _OriginalSchema = new(StringComparer.Ordinal);
        private SubmissionSnapshot _Snapshot;
        private double _Deadline;
        private double _NextReadAt;
        private int _EffectiveInterval;
        private int _StoreResult;
        private bool _RecoveryMayRetry;
        private bool _Reset;
        private string _RecoveryMessage;

        public SubmissionPhase Phase { get; private set; }
        public SubmissionMode Mode { get; private set; }
        public ulong GameId { get; private set; }
        public ulong SteamId { get; private set; }
        public ulong PendingReadHandle { get; private set; }
        public bool StopRequested { get; private set; }
        public bool HasUnconfirmedWrites { get; private set; }
        public int ConfirmedRounds { get; private set; }
        public int AttemptedRounds { get; private set; }
        public int TransientRetries { get; private set; }
        public int CorrectionRetries { get; private set; }
        public string Status { get; private set; } = "等待提交。";
        public bool IsBusy => Phase != SubmissionPhase.Idle;
        public bool CanStop => IsBusy && Phase != SubmissionPhase.Quarantined;
        public bool HasInFlightRequest => Phase == SubmissionPhase.AwaitInitialRead || Phase == SubmissionPhase.AwaitStored ||
            Phase == SubmissionPhase.AwaitRead || Phase == SubmissionPhase.AwaitRecoveryRead;
        public IReadOnlyDictionary<string, double> Targets => new ReadOnlyDictionary<string, double>(_Targets);
        public IReadOnlyDictionary<string, bool> AchievementTargets => new ReadOnlyDictionary<string, bool>(_AchievementTargets);
        public IReadOnlyDictionary<string, double> AttemptedValues => new ReadOnlyDictionary<string, double>(_Attempted);
        public IReadOnlyDictionary<string, double> ConfirmedValues => new ReadOnlyDictionary<string, double>(_Confirmed);
        public event Action<string> StatusChanged;
        public event Action<string> Log;
        public event Action Stopped;

        public SubmissionCoordinator(ISubmissionTransport transport, Func<double> monotonicSeconds)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _Now = monotonicSeconds ?? throw new ArgumentNullException(nameof(monotonicSeconds));
        }

        public bool Start(ulong gameId, ulong steamId, SubmissionMode mode, int intervalSeconds,
            SubmissionSnapshot snapshot, IDictionary<string, double> targets, IDictionary<string, bool> achievements)
        {
            if (IsBusy) return false;
            if (intervalSeconds < 60 || intervalSeconds > 86400)
                return RejectStart("间隔必须为 60 到 86400 秒；Steam 没有公布保证通过的最短间隔。");
            if (mode != SubmissionMode.Regular && achievements.Count != 0)
                return RejectStart("分步提交不能混入成就改动，请先使用“提交全部改动”单独处理成就。");
            var validation = BatchValidator.ValidateTargets(snapshot.Stats, targets);
            if (!validation.Success) return RejectStart(string.Join("\n", validation.Errors));
            if (mode == SubmissionMode.Regular && validation.RequiresStepping)
                return RejectStart("统计目标超出单次 maxchange；请使用“提交一步”或“自动分步”。");
            string achievementError = ValidateAchievements(snapshot, achievements);
            if (achievementError != null) return RejectStart(achievementError);
            if (!validation.Targets.Any(t => t.Changed) && !AchievementsChanged(snapshot, achievements))
                return RejectStart("当前没有需要提交的改动。");

            Initialize(gameId, steamId, mode, intervalSeconds, snapshot);
            _Targets = validation.Targets.ToDictionary(t => t.Descriptor.Id, t => t.TargetValue, StringComparer.Ordinal);
            _AchievementTargets = new Dictionary<string, bool>(achievements, StringComparer.Ordinal);
            _OriginalSchema = snapshot.Stats.Where(s => _Targets.ContainsKey(s.Id))
                .ToDictionary(s => s.Id, CloneDescriptor, StringComparer.Ordinal);
            WriteLog($"开始 {mode}；最终目标 {_Targets.Count} 个统计、{_AchievementTargets.Count} 个成就；间隔 {_EffectiveInterval} 秒。");
            foreach (var pair in _Targets) WriteLog($"TARGET {pair.Key} = {Format(pair.Value)}");
            RequestRead(SubmissionPhase.AwaitInitialRead);
            return true;
        }

        public bool StartReset(ulong gameId, ulong steamId, SubmissionSnapshot snapshot, bool achievementsToo)
        {
            if (IsBusy) return false;
            Initialize(gameId, steamId, SubmissionMode.Regular, 120, snapshot);
            _Reset = true;
            Phase = SubmissionPhase.AwaitStored;
            _Deadline = _Now() + TimeoutSeconds;
            HasUnconfirmedWrites = true;
            WriteLog("开始用户确认的重置；ResetAllStats 会自行提交，等待 Stored 回调。");
            try
            {
                if (!_Transport.ResetAllStats(achievementsToo))
                {
                    Quarantine("ResetAllStats 返回 false，原因未知；重置结果尚未核验。");
                    return false;
                }
                AttemptedRounds = 1;
                Publish("重置请求已发出，等待 Steam 保存结果；尚未确认。");
                return true;
            }
            catch (Exception e) { Quarantine("重置调用异常，结果未知：" + e.Message); return false; }
        }

        private void Initialize(ulong gameId, ulong steamId, SubmissionMode mode, int interval, SubmissionSnapshot snapshot)
        {
            GameId = gameId;
            SteamId = steamId;
            Mode = mode;
            _EffectiveInterval = interval;
            _Snapshot = snapshot;
            _Targets = new(StringComparer.Ordinal);
            _AchievementTargets = new(StringComparer.Ordinal);
            _Attempted = new(StringComparer.Ordinal);
            _AttemptedAchievements = new(StringComparer.Ordinal);
            _OriginalSchema = new(StringComparer.Ordinal);
            _Confirmed = snapshot.Stats.Where(s => s.IsAvailable).ToDictionary(s => s.Id, s => s.CurrentValue, StringComparer.Ordinal);
            _Reset = false;
            StopRequested = false;
            HasUnconfirmedWrites = false;
            ConfirmedRounds = AttemptedRounds = TransientRetries = CorrectionRetries = 0;
        }

        public bool HandleStatsReceived(ulong requestHandle, ulong gameId, ulong steamId, int result, Func<SubmissionSnapshot> readSnapshot)
        {
            if (gameId != GameId || steamId != SteamId || Phase == SubmissionPhase.Idle) return false;
            if (Phase != SubmissionPhase.AwaitInitialRead && Phase != SubmissionPhase.AwaitRead &&
                Phase != SubmissionPhase.AwaitRecoveryRead) return true;
            if (requestHandle == 0 || requestHandle != PendingReadHandle) return false;
            PendingReadHandle = 0;
            var receivedPhase = Phase;
            WriteLog("UserStatsReceived Result=" + result);
            if (result != 1)
            {
                if (HasUnconfirmedWrites) Quarantine("重新读取失败，Result=" + result + "；不能确定尝试值是否已保存。");
                else Finish("读取失败，Result=" + result + "；未发送本轮改动。");
                return true;
            }
            try
            {
                var snapshot = readSnapshot();
                if (snapshot == null) throw new InvalidOperationException("读取结果为空。");
                _Snapshot = snapshot;
                _Confirmed = snapshot.Stats.Where(s => s.IsAvailable).ToDictionary(s => s.Id, s => s.CurrentValue, StringComparer.Ordinal);
                foreach (var id in _Targets.Keys)
                    WriteLog(_Confirmed.TryGetValue(id, out double value) ? $"READBACK {id} = {Format(value)}" : $"READBACK {id} = unavailable");
                // A successful received callback is necessary; unavailable target fields still
                // prevent claiming that a previous local write has been resolved.
                if (_Attempted.Keys.Any(id => !_Confirmed.ContainsKey(id)) || _AttemptedAchievements.Keys.Any(id =>
                    !snapshot.Achievements.TryGetValue(id, out var achievement) || !achievement.IsAvailable))
                {
                    Quarantine("尝试修改的字段无法重新读取，结果未知。");
                    return true;
                }
                HasUnconfirmedWrites = false;
                if (_Reset)
                {
                    Finish(_StoreResult == 1 ? "Steam 重置回调成功，已重新读取实际值。" :
                        "重置返回 Result=" + _StoreResult + "；已重新读取实际值，未自动重试。");
                    return true;
                }
                string schemaError = CheckSchema(snapshot);
                if (schemaError != null) { Finish(schemaError + "；已停止，最终目标保留在界面。"); return true; }
                if (receivedPhase == SubmissionPhase.AwaitRead) VerifyRound(snapshot);
                else if (receivedPhase == SubmissionPhase.AwaitRecoveryRead) RecoverAfterRejectedStore(snapshot);
                else
                {
                    if (StopRequested) Finish("已停止；本轮只读取了实际值，没有发送改动。");
                    else PrepareAndSendRound(snapshot);
                }
            }
            catch (Exception e)
            {
                if (HasUnconfirmedWrites) Quarantine("读取或验证失败，提交结果未知：" + e.Message);
                else Finish("读取或验证失败，已停止：" + e.Message);
            }
            return true;
        }

        public bool HandleStatsStored(ulong gameId, int result)
        {
            if (gameId != GameId || Phase == SubmissionPhase.Idle) return false;
            if (Phase != SubmissionPhase.AwaitStored) return true;
            _StoreResult = result;
            WriteLog("UserStatsStored Result=" + result + "；该回调不含各字段数值。");
            if (result == 1) RequestRead(SubmissionPhase.AwaitRead);
            else
            {
                _RecoveryMayRetry = false;
                if (result == 8)
                {
                    CorrectionRetries++;
                    _RecoveryMayRetry = !_Reset && CorrectionRetries <= MaxCorrectionRetries;
                    _RecoveryMessage = "Result=8：违反约束或数据过时；先读取修正值";
                    _EffectiveInterval = Math.Max(120, _EffectiveInterval);
                }
                else if (result == 10 || result == 84)
                {
                    TransientRetries++;
                    _RecoveryMayRetry = !_Reset && TransientRetries <= MaxTransientRetries;
                    _EffectiveInterval = Math.Min(86400, Math.Max(120, _EffectiveInterval * 2));
                    _RecoveryMessage = "Result=" + result + "：暂时失败；并非 StoreStats 文档保证的限流专用码";
                }
                else
                    _RecoveryMessage = "Result=" + result + (result == 25 ? "：LimitExceeded 可能是永久限制，停止重试" : "：原因未分类，停止重试");
                RequestRead(SubmissionPhase.AwaitRecoveryRead);
            }
            return true;
        }

        private void PrepareAndSendRound(SubmissionSnapshot snapshot)
        {
            var validation = BatchValidator.ValidateTargets(snapshot.Stats, _Targets);
            if (!validation.Success) { Finish(string.Join("\n", validation.Errors)); return; }
            if (Mode == SubmissionMode.Regular && validation.RequiresStepping)
            { Finish("最新读回值使目标超出单次限制；请改用分步提交。"); return; }
            string achievementError = ValidateAchievements(snapshot, _AchievementTargets);
            if (achievementError != null) { Finish(achievementError); return; }
            var next = new Dictionary<string, double>(StringComparer.Ordinal);
            var kinds = new Dictionary<string, StatValueKind>(StringComparer.Ordinal);
            foreach (var target in validation.Targets.Where(t => t.Changed))
            {
                var step = StepPlanner.Next(target.Descriptor, target.TargetValue);
                if (!step.Success) { Finish(step.Error); return; }
                if (step.Value == target.Descriptor.CurrentValue) { Finish(target.Descriptor.Id + "：没有可表示的进度，已停止。"); return; }
                next.Add(target.Descriptor.Id, step.Value);
                kinds.Add(target.Descriptor.Id, target.Descriptor.Kind);
            }
            var achievements = _AchievementTargets.Where(p => snapshot.Achievements[p.Key].Value != p.Value)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (next.Count == 0 && achievements.Count == 0) { Finish("最新读回值已达到全部最终目标，无需发送。"); return; }
            if (StopRequested) { Finish("已停止；未发送后续轮次。"); return; }
            _Attempted = next;
            _AttemptedAchievements = achievements;
            Phase = SubmissionPhase.SettingRound;
            try
            {
                foreach (var pair in next)
                {
                    HasUnconfirmedWrites = true;
                    WriteLog($"ATTEMPT {pair.Key}: {Format(_Confirmed[pair.Key])} -> {Format(pair.Value)}；最终目标 {Format(_Targets[pair.Key])}");
                    if (!_Transport.SetStat(pair.Key, kinds[pair.Key], pair.Value))
                    {
                        Quarantine("SetStat(" + pair.Key + ") 返回 false，原因未知；已处理字段可能留在 Steam 缓存中，本轮未调用 StoreStats。");
                        return;
                    }
                }
                foreach (var pair in achievements)
                {
                    HasUnconfirmedWrites = true;
                    WriteLog("ATTEMPT achievement " + pair.Key + " = " + pair.Value);
                    if (!_Transport.SetAchievement(pair.Key, pair.Value))
                    { Quarantine("SetAchievement 返回 false，原因未知；缓存可能已有部分改动，本轮未调用 StoreStats。"); return; }
                }
                Phase = SubmissionPhase.AwaitStored;
                _Deadline = _Now() + TimeoutSeconds;
                HasUnconfirmedWrites = true;
                AttemptedRounds++;
                if (!_Transport.StoreStats())
                { Quarantine("StoreStats 返回 false，原因未知；官方说明本次未发送，但先前 SetStat 缓存不代表已撤销。"); return; }
                WriteLog("StoreStats 返回 true；仅表示请求发起，等待保存回调与重新读取。");
                Publish($"第 {AttemptedRounds} 次提交请求已发出；已核验 {ConfirmedRounds} 轮，当前尝试值尚未确认。");
            }
            catch (Exception e) { Quarantine("写入调用异常，结果未知：" + e.Message); }
        }

        private void VerifyRound(SubmissionSnapshot snapshot)
        {
            foreach (var pair in _Attempted)
                if (!_Confirmed.TryGetValue(pair.Key, out double actual) || actual != pair.Value)
                { Finish($"{pair.Key}：读回值 {(_Confirmed.ContainsKey(pair.Key) ? Format(_Confirmed[pair.Key]) : "未知")} 与尝试值 {Format(pair.Value)} 不符，停止后续轮次。"); return; }
            foreach (var pair in _AttemptedAchievements)
                if (!snapshot.Achievements.TryGetValue(pair.Key, out var actual) || !actual.IsAvailable || actual.Value != pair.Value)
                { Finish(pair.Key + "：成就读回状态与尝试值不符，已停止。"); return; }
            ConfirmedRounds++;
            WriteLog("CONFIRMED 本轮所有尝试值与重新读取值一致；累计 " + ConfirmedRounds + " 轮。");
            if (!HasRemaining(snapshot)) { Finish("全部最终目标已重新读取并核验；共 " + ConfirmedRounds + " 轮。"); return; }
            if (StopRequested) { Finish("本轮已核验，已停止后续轮次；尚未达到的最终目标保留。"); return; }
            if (Mode != SubmissionMode.Automatic) { Finish("本轮已核验；最终目标尚未全部达到，可继续提交一步或自动分步。"); return; }
            WaitForNextRound("本轮已核验");
        }

        private void RecoverAfterRejectedStore(SubmissionSnapshot snapshot)
        {
            WriteLog(_RecoveryMessage + "；已重新读取实际值，不能沿用上次尝试值作为起点。");
            if (StopRequested) { Finish(_RecoveryMessage + "；实际值已读取，按停止请求不再重试。"); return; }
            if (!HasRemaining(snapshot)) { Finish(_RecoveryMessage + "；读回值已达到目标，但本轮保存回调未报告成功，已停止。"); return; }
            var validation = BatchValidator.ValidateTargets(snapshot.Stats, _Targets);
            if (!validation.Success) { Finish(_RecoveryMessage + "；" + string.Join("\n", validation.Errors)); return; }
            if (!_RecoveryMayRetry) { Finish(_RecoveryMessage + "；不再重试，已保留实际读回值和最终目标。"); return; }
            WaitForNextRound(_RecoveryMessage + $"；将根据最新值重新规划（暂时错误重试 {TransientRetries}/{MaxTransientRetries}，修正重试 {CorrectionRetries}/{MaxCorrectionRetries}）");
        }

        private void RequestRead(SubmissionPhase phase)
        {
            Phase = phase;
            _Deadline = _Now() + TimeoutSeconds;
            try
            {
                PendingReadHandle = _Transport.RequestStats();
                if (PendingReadHandle == 0)
                {
                    if (HasUnconfirmedWrites) Quarantine("读取请求未发起，无法核验已经尝试的改动。");
                    else Finish("读取请求未发起，未发送新改动。");
                    return;
                }
                Publish(phase == SubmissionPhase.AwaitInitialRead ? "正在重新读取当前值与实际字段约束；尚未发送本轮改动。" :
                    "正在重新读取实际值；保存回调本身不包含字段值。");
            }
            catch (Exception e) { Quarantine("读取请求异常，回调状态不明：" + e.Message); }
        }

        private void WaitForNextRound(string reason)
        {
            Phase = SubmissionPhase.Waiting;
            _NextReadAt = _Now() + _EffectiveInterval;
            WriteLog(reason + "；下一轮至少等待 " + _EffectiveInterval + " 秒，不会自动缩短至秒级探测。");
            Publish(reason + "；" + _EffectiveInterval + " 秒后重新读取并规划。");
        }

        public void Tick()
        {
            if (HasInFlightRequest && _Now() >= _Deadline)
            { Quarantine("等待 Steam 回调超过 60 秒；结果不明，禁止在此窗口重发，以免迟到回调误配。"); return; }
            if (Phase == SubmissionPhase.Waiting)
            {
                if (StopRequested) { Finish("已停止后续轮次；已核验的值保留。"); return; }
                int remaining = (int)Math.Max(0, Math.Ceiling(_NextReadAt - _Now()));
                if (remaining == 0) RequestRead(SubmissionPhase.AwaitInitialRead);
                else Publish($"已核验 {ConfirmedRounds} 轮；下一轮 {remaining} 秒后重新读取。暂时错误重试 {TransientRetries}/5，修正重试 {CorrectionRetries}/2。");
            }
        }

        public void RequestStop()
        {
            if (!CanStop) return;
            StopRequested = true;
            WriteLog("用户要求停止后续轮次；已发出的请求无法撤销。");
            if (Phase == SubmissionPhase.Waiting) Finish("已停止；不会再发送后续轮次。");
            else Publish("已要求停止后续轮次；当前已发请求继续等待回调和核验，无法撤销。");
        }

        public void Quarantine(string reason)
        {
            Phase = SubmissionPhase.Quarantined;
            PendingReadHandle = 0;
            StopRequested = true;
            WriteLog("QUARANTINED " + reason);
            Publish(reason + " 请关闭此游戏窗口并重新打开，重新读取后再决定；关闭不能保证撤销缓存改动。");
            Stopped?.Invoke();
        }

        private void Finish(string message)
        {
            if (Phase == SubmissionPhase.Quarantined) return;
            Phase = SubmissionPhase.Idle;
            PendingReadHandle = 0;
            WriteLog("STOP " + message);
            Publish(message);
            Stopped?.Invoke();
        }

        private bool RejectStart(string message) { Publish(message); return false; }
        private void Publish(string message) { Status = message; StatusChanged?.Invoke(message); }
        private void WriteLog(string message) { Log?.Invoke(message); }
        private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private bool HasRemaining(SubmissionSnapshot snapshot) => _Targets.Any(p =>
            !_Confirmed.TryGetValue(p.Key, out double current) || current != p.Value) || AchievementsChanged(snapshot, _AchievementTargets);

        private string CheckSchema(SubmissionSnapshot snapshot)
        {
            var schema = snapshot.Stats.ToDictionary(s => s.Id, StringComparer.Ordinal);
            foreach (var pair in _OriginalSchema)
            {
                if (!schema.TryGetValue(pair.Key, out var actual)) return pair.Key + "：字段已消失";
                var old = pair.Value;
                if (old.Kind != actual.Kind || old.Minimum != actual.Minimum || old.Maximum != actual.Maximum ||
                    old.MaxChange != actual.MaxChange || old.IncrementOnly != actual.IncrementOnly ||
                    old.Permission != actual.Permission || old.IsProtected != actual.IsProtected ||
                    old.SetByTrustedGameServer != actual.SetByTrustedGameServer)
                    return pair.Key + "：字段类型或约束已变化";
                if (!actual.IsAvailable) return pair.Key + "：字段无法读取";
            }
            return null;
        }

        private static string ValidateAchievements(SubmissionSnapshot snapshot, IDictionary<string, bool> targets)
        {
            foreach (var pair in targets)
            {
                if (!snapshot.Achievements.TryGetValue(pair.Key, out var item) || !item.IsAvailable)
                    return pair.Key + "：成就不可读取或已不存在。";
                if (item.Permission != 0 && item.Value != pair.Value) return pair.Key + "：成就有权限保护，拒绝修改。";
            }
            return null;
        }

        private static bool AchievementsChanged(SubmissionSnapshot snapshot, IDictionary<string, bool> targets) => targets.Any(p =>
            !snapshot.Achievements.TryGetValue(p.Key, out var item) || !item.IsAvailable || item.Value != p.Value);

        private static StatDescriptor CloneDescriptor(StatDescriptor s) => new()
        {
            Id = s.Id, DisplayName = s.DisplayName, Kind = s.Kind, CurrentValue = s.CurrentValue,
            Minimum = s.Minimum, Maximum = s.Maximum, MaxChange = s.MaxChange, IncrementOnly = s.IncrementOnly,
            IsProtected = s.IsProtected, IsAvailable = s.IsAvailable, Permission = s.Permission,
            SetByTrustedGameServer = s.SetByTrustedGameServer,
        };
    }
}
