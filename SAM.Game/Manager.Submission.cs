// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SAM.Batch;
using SAM.Submission;
using APITypes = SAM.API.Types;

namespace SAM.Game
{
    internal partial class Manager
    {
        private SubmissionCoordinator _Submission;
        private API.Callbacks.UserStatsStored _UserStatsStoredCallback;
        private API.Callbacks.SteamApiCallCompleted _SteamApiCallCompletedCallback;
        private readonly Stopwatch _SubmissionClock = Stopwatch.StartNew();
        private Timer _SubmissionTimer;
        private bool _NormalStatsReadPending;
        private double _NormalReadDeadline;
        private ulong _NormalStatsCallHandle;
        private ulong _NormalStatsSteamId;
        private ulong _ProcessingStatsCallHandle;
        private ulong _LastReadSteamId;
        private bool _StatsReadReady;
        private string _SubmissionLogPath;
        private bool _SubmissionLogWarningShown;

        private bool SubmissionBusy => this._NormalStatsReadPending || (this._Submission?.IsBusy ?? false);
        private bool SubmissionCanStop => this._Submission?.CanStop ?? false;

        private void InitializeBatchSubmission()
        {
            this._Submission = new SubmissionCoordinator(new SteamSubmissionTransport(this),
                () => this._SubmissionClock.Elapsed.TotalSeconds);
            this._Submission.StatusChanged += message =>
            {
                this.SetBatchStatus(message);
                if (this.SubmissionBusy) this.DisableInput();
                else this.EnableInput();
            };
            this._Submission.Log += this.LogSubmission;
            this._Submission.Stopped += this.RestoreSubmissionPreview;
            this._UserStatsStoredCallback = this._SteamClient.CreateAndRegisterCallback<API.Callbacks.UserStatsStored>();
            this._UserStatsStoredCallback.OnRun += this.OnBatchStatsStored;
            this._SteamApiCallCompletedCallback = this._SteamClient.CreateAndRegisterCallback<API.Callbacks.SteamApiCallCompleted>();
            this._SteamApiCallCompletedCallback.OnRun += this.OnStatsApiCallCompleted;
            this._SubmissionTimer = new Timer { Interval = 1000 };
            this._SubmissionTimer.Tick += this.OnSubmissionTimer;
            this._SubmissionTimer.Start();
            this.FormClosing += this.OnSubmissionFormClosing;
            this.FormClosed += (s, e) => this._SubmissionTimer.Dispose();
            try
            {
                // Resolve from the executable, not the shortcut's working directory.
                // Packaged SAM.Game.exe lives in app/ next to the root launcher.
                var applicationDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                if (string.Equals(applicationDirectory.Name, "app", StringComparison.OrdinalIgnoreCase) &&
                    applicationDirectory.Parent != null &&
                    File.Exists(Path.Combine(applicationDirectory.Parent.FullName, "SteamStatsEditor.exe")))
                    applicationDirectory = applicationDirectory.Parent;
                var directory = Path.Combine(applicationDirectory.FullName, "logs");
                Directory.CreateDirectory(directory);
                this._SubmissionLogPath = Path.Combine(directory,
                    this._GameId.ToString(CultureInfo.InvariantCulture) + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(this._SubmissionLogPath, "Steam Stats Editor submission log\r\n", new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                this._SubmissionLogPath = null;
                // The in-window log remains available for manual export.
                this.AddBatchLog("无法在程序目录创建自动日志文件，请确认程序文件夹可写；可在日志窗口手动保存。" + e.Message);
            }
        }

        private void BeginNormalStatsRead()
        {
            if (this.SubmissionBusy) return;
            this._NormalStatsReadPending = true;
            this._NormalReadDeadline = this._SubmissionClock.Elapsed.TotalSeconds + 60;
            this._StatsReadReady = false;
            this.DisableInput();
            this.SetBatchStatus("正在读取 Steam 当前值与字段定义…");
            try
            {
                this._NormalStatsSteamId = this._SteamClient.SteamUser.GetSteamId();
                var handle = this._SteamClient.SteamUserStats.RequestUserStats(this._NormalStatsSteamId);
                this._NormalStatsCallHandle = (ulong)handle;
                if (handle == API.CallHandle.Invalid)
                {
                    this._NormalStatsReadPending = false;
                    this.SetBatchStatus("读取请求未发起，原因未知；请确认 Steam 在线后重新读取。");
                    this.EnableInput();
                }
            }
            catch (Exception e) { this.FailBatchSubmission("读取请求异常，结果不明：" + e.Message, true); }
        }

        private void StartBatchSubmission(bool automatic, int intervalSeconds)
        {
            this.BeginSubmission(automatic ? SubmissionMode.Automatic : SubmissionMode.SingleStep, intervalSeconds);
        }

        private void StartRegularSubmission()
        {
            this.BeginSubmission(SubmissionMode.Regular, 120);
        }

        private void BeginSubmission(SubmissionMode mode, int intervalSeconds)
        {
            if (intervalSeconds == 0 || this.SubmissionBusy || !this._StatsReadReady) return;
            try
            {
                if (!this.Validate()) { this.SetBatchStatus("当前编辑值无效，请先修正。"); return; }
                var targets = this.CaptureBatchTargets();
                var achievements = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (ListViewItem item in this._AchievementListView.Items)
                    if (item.Tag is Stats.AchievementInfo info && info.IsAchieved != item.Checked)
                        achievements.Add(info.Id, item.Checked);
                var snapshot = this.CaptureSubmissionSnapshot(false);
                var validation = BatchValidator.ValidateTargets(snapshot.Stats, targets);
                if (!validation.Success)
                {
                    this.ShowSubmissionMessage(string.Join("\n", validation.Errors));
                    return;
                }
                if (mode != SubmissionMode.Regular && achievements.Count != 0)
                { this.ShowSubmissionMessage("分步提交不能混入成就改动。请先单独处理成就，再开始统计分步。"); return; }
                if (mode == SubmissionMode.Regular && validation.RequiresStepping)
                { this.ShowSubmissionMessage("统计改动超出单轮限制。普通提交已拒绝；请先处理成就，再使用统计分步。"); return; }
                if (!validation.Targets.Any(t => t.Changed) && achievements.Count == 0)
                { this.SetBatchStatus("当前没有待提交改动。"); return; }
                var modeText = mode == SubmissionMode.Automatic ? "自动分步直到最终目标" :
                    mode == SubmissionMode.SingleStep ? "只提交并核验一步" : "一次提交全部改动";
                string preview = string.Join("\n", validation.Targets.Where(t => t.Changed).Take(8)
                    .Select(t => t.Descriptor.Id + ": " + FormatSubmissionNumber(t.Descriptor.CurrentValue) + " → " + FormatSubmissionNumber(t.TargetValue)));
                string message = modeText + $"\n统计字段：{targets.Count}；成就：{achievements.Count}；预计统计轮数：{validation.EstimatedRounds}。\n" +
                    (mode == SubmissionMode.SingleStep ? "超出一步的最终目标会保留，供下次继续。\n" : "") +
                    "\n" + preview + (targets.Count > 8 ? "\n…其余目标见表格。" : "") +
                    "\n\n提交前会重新读取并验证。已发出的本轮请求不能撤销；停止只阻止后续轮次。" +
                    $"\n本次间隔 {intervalSeconds} 秒；暂时错误最多按当前间隔翻倍重试 5 次，数据修正最多重新规划 2 次，不会探测秒级最快速度。";
                if (MessageBox.Show(this, message, "确认提交", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                this._Submission.Start((ulong)this._GameId, this._SteamClient.SteamUser.GetSteamId(), mode,
                    intervalSeconds, snapshot, targets, achievements);
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException || e is OverflowException || e is FormatException)
            {
                this.ShowSubmissionMessage("未开始提交：" + e.Message);
            }
        }

        private void StartBatchReset(bool achievementsToo)
        {
            if (this.SubmissionBusy || !this._StatsReadReady) return;
            try
            {
                this._Submission.StartReset((ulong)this._GameId, this._SteamClient.SteamUser.GetSteamId(),
                    this.CaptureSubmissionSnapshot(false), achievementsToo);
            }
            catch (Exception e) { this.FailBatchSubmission("无法开始重置：" + e.Message, true); }
        }

        private void StopBatchSubmission() => this._Submission.RequestStop();

        private bool HandleBatchStatsReceived(APITypes.UserStatsReceived param)
        {
            // Callback 1101 is shared and contains no request handle. Only the result
            // extracted from our exact completed RequestUserStats call may advance state.
            if (this._ProcessingStatsCallHandle == 0) return true;
            return this._Submission.HandleStatsReceived(this._ProcessingStatsCallHandle, param.GameId, param.SteamIdUser, param.Result,
                () => this.CaptureSubmissionSnapshot(true));
        }

        private void OnStatsApiCallCompleted(APITypes.SteamApiCallCompleted completed)
        {
            bool normal = this._NormalStatsReadPending && completed.AsyncCall == this._NormalStatsCallHandle;
            bool submission = this._Submission.PendingReadHandle != 0 && completed.AsyncCall == this._Submission.PendingReadHandle;
            if (!normal && !submission) return;
            try
            {
                if (completed.CallbackId != 1101)
                    throw new InvalidOperationException("RequestUserStats 的回调类型不匹配：" + completed.CallbackId);
                if (!this._SteamClient.SteamUtils.GetUserStatsCallResult(completed.AsyncCall, completed.ParameterSize,
                    out var result, out bool ioFailure))
                    throw new InvalidOperationException("无法提取指定请求的结果，I/O failure=" + ioFailure);
                ulong expectedUser = normal ? this._NormalStatsSteamId : this._Submission.SteamId;
                if (result.GameId != (ulong)this._GameId || result.SteamIdUser != expectedUser ||
                    this._SteamClient.SteamUser.GetSteamId() != expectedUser)
                    throw new InvalidOperationException("指定请求返回的游戏或用户身份不匹配。");
                this.LogSubmission($"RequestUserStats handle={completed.AsyncCall}, bytes={completed.ParameterSize}, Result={result.Result}；仅消费对应请求结果。");
                if (normal) this._NormalStatsCallHandle = 0;
                if (result.Result == 1) this._LastReadSteamId = result.SteamIdUser;
                this._ProcessingStatsCallHandle = completed.AsyncCall;
                try { this.OnUserStatsReceived(result); }
                finally { this._ProcessingStatsCallHandle = 0; }
            }
            catch (Exception e) { this.FailBatchSubmission("读取结果无法可靠关联：" + e.Message, true); }
        }

        private SubmissionSnapshot CaptureSubmissionSnapshot(bool reload)
        {
            if (reload)
            {
                this._StatsReadReady = false;
                if (!this.LoadUserGameStatsSchema()) throw new InvalidOperationException("无法读取当前游戏的真实字段定义。");
                this.GetStatistics();
                this.GetAchievements();
                this._StatsReadReady = true;
            }
            var achievements = new Dictionary<string, AchievementValue>(StringComparer.Ordinal);
            foreach (var definition in this._AchievementDefinitions)
            {
                bool available = this._SteamClient.SteamUserStats.GetUserAchievement(this._LastReadSteamId, definition.Id, out bool value);
                achievements.Add(definition.Id, new AchievementValue
                { Value = value, IsAvailable = available, Permission = definition.Permission });
            }
            var snapshot = new SubmissionSnapshot(this.CaptureBatchSchema(), achievements);
            if (reload && this._Submission.Targets.Count > 0)
                this.ApplyBatchTargets(this._Submission.Targets.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
            return snapshot;
        }

        private void OnBatchStatsStored(APITypes.UserStatsStored param)
        {
            if (this._Submission.IsBusy && this._Submission.SteamId != 0 &&
                this._SteamClient.SteamUser.GetSteamId() != this._Submission.SteamId)
            {
                this.FailBatchSubmission("Steam 登录用户发生变化，不能将回调归入原提交。", true);
                return;
            }
            this._Submission.HandleStatsStored(param.GameId, param.Result);
        }

        private void OnSubmissionTimer(object sender, EventArgs e)
        {
            if (this._NormalStatsReadPending && this._SubmissionClock.Elapsed.TotalSeconds >= this._NormalReadDeadline)
            {
                this.FailBatchSubmission("读取回调超过 60 秒；为避免迟到回调覆盖新状态，需要重新打开游戏窗口。", true);
                return;
            }
            this._Submission.Tick();
        }

        private void FailBatchSubmission(string message, bool quarantine)
        {
            this._NormalStatsReadPending = false;
            this._NormalStatsCallHandle = 0;
            if (quarantine) this._Submission.Quarantine(message);
            else
            {
                this._Submission.RequestStop();
                this.SetBatchStatus(message);
                this.LogSubmission(message);
            }
        }

        private void RestoreSubmissionPreview()
        {
            try
            {
                // OriginalValue remains the actual readback. Reapplying only the separate
                // final target preserves the distinction between confirmed and attempted.
                if (this._Submission.Targets.Count > 0)
                    this.ApplyBatchTargets(this._Submission.Targets.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
                this._IsUpdatingAchievementList = true;
                try
                {
                    foreach (ListViewItem item in this._AchievementListView.Items)
                        if (item.Tag is Stats.AchievementInfo info && info.Permission == 0 &&
                            this._Submission.AchievementTargets.TryGetValue(info.Id, out bool target)) item.Checked = target;
                }
                finally { this._IsUpdatingAchievementList = false; }
                if (this.SubmissionBusy) this.DisableInput();
                else this.EnableInput();
            }
            catch (Exception e)
            {
                this.LogSubmission("最终目标未能全部回填：" + e.Message);
                this.SetBatchStatus(this._Submission.Status + " 部分最终目标无法回填，请参见日志中的 TARGET 记录。");
            }
        }

        private void OnSubmissionFormClosing(object sender, FormClosingEventArgs e)
        {
            if (this._Submission.HasInFlightRequest || this._Submission.HasUnconfirmedWrites)
            {
                if (MessageBox.Show(this,
                    "当前有已发出的请求或尚未核验的 Steam 缓存改动。关闭不会撤销它们，Steam 还可能在退出时保存。\n仍要关闭此游戏窗口吗？",
                    "提交尚未核验", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                { e.Cancel = true; return; }
                this.LogSubmission("用户关闭窗口；有请求或缓存尚未核验，不承诺撤销。");
            }
            this._Submission.RequestStop();
        }

        private void LogSubmission(string message)
        {
            this.AddBatchLog(message);
            if (this._SubmissionLogPath == null) return;
            try
            {
                File.AppendAllText(this._SubmissionLogPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (!this._SubmissionLogWarningShown)
                {
                    this._SubmissionLogWarningShown = true;
                    this.AddBatchLog("自动日志写入失败，请从日志窗口手动保存：" + e.Message);
                }
            }
        }

        private void ShowSubmissionMessage(string message)
        {
            this.SetBatchStatus(message);
            MessageBox.Show(this, message, "未提交", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string FormatSubmissionNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // Missing constraints use their documented defaults. Present but malformed values
        // must never silently become zero/unlimited or lose permission protection.
        private static int ReadSchemaInteger(KeyValue parent, string key, int missingValue)
        {
            var node = parent[key];
            if (!node.Valid) return missingValue;
            if (node.Type == KeyValueType.Int32) return (int)node.Value;
            if ((node.Type == KeyValueType.String || node.Type == KeyValueType.WideString) &&
                int.TryParse((string)node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)) return integer;
            if (node.Type == KeyValueType.UInt64 && (ulong)node.Value <= int.MaxValue) return (int)(ulong)node.Value;
            if (node.Type == KeyValueType.Float32)
            {
                double value = (float)node.Value;
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= int.MinValue && value <= int.MaxValue && Math.Truncate(value) == value)
                    return (int)value;
            }
            throw new InvalidDataException("字段 " + parent["name"].AsString("未知") + " 的 " + key + " 不是有效整数，已拒绝加载。");
        }

        private static float ReadSchemaFloat(KeyValue parent, string key, float missingValue)
        {
            var node = parent[key];
            if (!node.Valid) return missingValue;
            float value;
            if (node.Type == KeyValueType.Float32) value = (float)node.Value;
            else if (node.Type == KeyValueType.Int32 && SchemaConstraintParser.TryFloat(((int)node.Value).ToString(CultureInfo.InvariantCulture), out float integer)) value = integer;
            else if (node.Type == KeyValueType.UInt64 && SchemaConstraintParser.TryFloat(((ulong)node.Value).ToString(CultureInfo.InvariantCulture), out float unsigned)) value = unsigned;
            else if ((node.Type == KeyValueType.String || node.Type == KeyValueType.WideString) &&
                SchemaConstraintParser.TryFloat((string)node.Value, out float parsed)) value = parsed;
            else value = float.NaN;
            if (!float.IsNaN(value) && !float.IsInfinity(value)) return value;
            throw new InvalidDataException("字段 " + parent["name"].AsString("未知") + " 的 " + key + " 不是有限浮点数，已拒绝加载。");
        }

        private sealed class SteamSubmissionTransport : ISubmissionTransport
        {
            private readonly Manager _Owner;
            public SteamSubmissionTransport(Manager owner) { this._Owner = owner; }
            public ulong RequestStats() => (ulong)this._Owner._SteamClient.SteamUserStats.RequestUserStats(this._Owner._SteamClient.SteamUser.GetSteamId());
            public bool SetStat(string id, StatValueKind kind, double value) => kind switch
            {
                StatValueKind.Integer => this._Owner._SteamClient.SteamUserStats.SetStatValue(id, checked((int)value)),
                StatValueKind.Float => this._Owner._SteamClient.SteamUserStats.SetStatValue(id, (float)value),
                _ => false,
            };
            public bool SetAchievement(string id, bool value) => this._Owner._SteamClient.SteamUserStats.SetAchievement(id, value);
            public bool StoreStats() => this._Owner._SteamClient.SteamUserStats.StoreStats();
            public bool ResetAllStats(bool achievementsToo) => this._Owner._SteamClient.SteamUserStats.ResetAllStats(achievementsToo);
        }
    }
}
