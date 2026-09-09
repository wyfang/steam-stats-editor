// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SAM.Batch;

namespace SAM.Game
{
    internal partial class Manager
    {
        private ToolStripDropDownButton _BatchExportButton;
        private ToolStripButton _BatchImportButton;
        private ToolStripButton _BatchOnceButton;
        private ToolStripButton _BatchStartButton;
        private ToolStripButton _BatchStopButton;
        private ToolStripButton _BatchLogButton;
        private ToolStripTextBox _BatchIntervalBox;
        private readonly List<string> _BatchLogLines = new();
        private Form _BatchLogWindow;
        private TextBox _BatchLogText;
        private Button _BatchLogStopButton;

        private void InitializeBatchUi()
        {
            int titleSeparator = this.Text.IndexOf(" | ", StringComparison.Ordinal);
            this.Text = "Steam Stats Editor · 统计清单" + (titleSeparator >= 0 ? this.Text.Substring(titleSeparator) : "");
            this._MainTabControl.SelectedTab = this._StatisticsTabPage;
            this._StatisticsTabPage.Text = "统计数据";
            this._AchievementsTabPage.Text = "成就（SAM）";
            this._ReloadButton.Text = "重新读取";
            this._StoreButton.Text = "提交全部改动";
            this._StoreButton.ToolTipText = "一次提交当前统计与成就改动；超出单步限制时请使用分步提交。";
            this._ResetButton.Text = "重置";
            this._EnableStatsEditingCheckBox.Text = "允许在表格中编辑目标值（修改后仍需提交）";

            this._StatisticsDataGridView.Columns[0].HeaderText = "名称";
            this._StatisticsDataGridView.Columns[0].Width = 290;
            this._StatisticsDataGridView.Columns[1].HeaderText = "目标值";
            this._StatisticsDataGridView.Columns[1].Width = 145;
            this._StatisticsDataGridView.Columns[2].HeaderText = "字段属性";
            this._StatisticsDataGridView.Columns[2].Width = 190;
            this._StatisticsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "apiId", HeaderText = "字段 ID", DataPropertyName = "Id", ReadOnly = true, Width = 300,
            });
            this._StatisticsDataGridView.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "confirmed", HeaderText = "已读取值", DataPropertyName = "ConfirmedValue", ReadOnly = true, Width = 135, DisplayIndex = 1,
            });
            this._StatisticsDataGridView.Width = this._StatisticsTabPage.ClientSize.Width - 12;
            this._StatisticsDataGridView.AllowUserToAddRows = false;
            this._StatisticsDataGridView.RowHeadersVisible = false;
            this._StatisticsDataGridView.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 250);

            this._BatchExportButton = new ToolStripDropDownButton("导出清单");
            this._BatchExportButton.DropDownItems.Add("导出 Steam 已读取值", null, (s, e) => this.ExportBatchText(false));
            this._BatchExportButton.DropDownItems.Add("导出界面目标值", null, (s, e) => this.ExportBatchText(true));
            this._BatchImportButton = new ToolStripButton("导入清单…", null, this.ImportBatchText);
            this._BatchOnceButton = new ToolStripButton("提交一步", null, (s, e) => this.StartBatchSubmission(false, this.ReadBatchInterval()));
            this._BatchStartButton = new ToolStripButton("自动分步…", null, (s, e) => this.StartBatchSubmission(true, this.ReadBatchInterval()));
            this._BatchStopButton = new ToolStripButton("停止后续轮次", null, (s, e) => this.StopBatchSubmission()) { Enabled = false };
            this._BatchIntervalBox = new ToolStripTextBox { Text = "120", AutoSize = false, Width = 48, ToolTipText = "每轮间隔，单位秒；最少 60 秒，默认 120 秒。" };
            this._BatchLogButton = new ToolStripButton("日志…", null, (s, e) => this.ShowBatchLog());
            this.InitializeManagerLayout();
            this.SetBatchControlsEnabled(false);
        }

        private int ReadBatchInterval()
        {
            if (!int.TryParse(this._BatchIntervalBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds < 60 || seconds > 86400)
            {
                MessageBox.Show(this, "间隔请输入 60 到 86400 之间的整数秒。", "间隔无效", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            return seconds;
        }

        private List<StatDescriptor> CaptureBatchSchema(bool useOriginalValues = true)
        {
            var values = this._Statistics.ToDictionary(s => s.Id, StringComparer.Ordinal);
            var result = new List<StatDescriptor>();
            foreach (var definition in this._StatDefinitions)
            {
                values.TryGetValue(definition.Id, out var info);
                var item = new StatDescriptor
                {
                    Id = definition.Id,
                    DisplayName = definition.DisplayName,
                    Permission = definition.Permission,
                    IsProtected = definition.Permission != 0,
                    IsAvailable = info != null,
                };
                if (definition is Stats.IntegerStatDefinition i)
                {
                    item.Kind = StatValueKind.Integer;
                    item.Minimum = i.MinValue;
                    item.Maximum = i.MaxValue;
                    item.MaxChange = i.MaxChange;
                    item.IncrementOnly = i.IncrementOnly;
                    item.SetByTrustedGameServer = i.SetByTrustedGameServer;
                    if (info is Stats.IntStatInfo v) item.CurrentValue = useOriginalValues ? v.OriginalValue : v.IntValue;
                }
                else if (definition is Stats.FloatStatDefinition f)
                {
                    item.Kind = f.IsAverageRate ? StatValueKind.AverageRate : StatValueKind.Float;
                    item.Minimum = f.MinValue;
                    item.Maximum = f.MaxValue;
                    item.MaxChange = f.MaxChange;
                    item.IncrementOnly = f.IncrementOnly;
                    item.SetByTrustedGameServer = f.SetByTrustedGameServer;
                    if (info is Stats.FloatStatInfo v) item.CurrentValue = useOriginalValues ? v.OriginalValue : v.FloatValue;
                }
                else continue;
                result.Add(item);
            }
            return result;
        }

        private Dictionary<string, double> CaptureBatchTargets()
        {
            if (!this.TryCommitBatchEdit()) throw new InvalidOperationException("表格包含未完成的无效输入，请先修正目标值。");
            return this._Statistics.Where(s => s.IsModified).ToDictionary(s => s.Id,
                s => s is Stats.IntStatInfo i ? (double)i.IntValue : ((Stats.FloatStatInfo)s).FloatValue,
                StringComparer.Ordinal);
        }

        private bool TryCommitBatchEdit()
        {
            if (!this._StatisticsDataGridView.EndEdit()) return false;
            ((BindingSource)this._StatisticsDataGridView.DataSource).EndEdit();
            return true;
        }

        private void ApplyBatchTargets(IDictionary<string, double> targets)
        {
            foreach (var stat in this._Statistics)
            {
                if (!targets.TryGetValue(stat.Id, out double value)) continue;
                if (stat is Stats.IntStatInfo i) i.IntValue = checked((int)value);
                else if (stat is Stats.FloatStatInfo f) f.FloatValue = (float)value;
            }
            this._EnableStatsEditingCheckBox.Checked = true;
            this._MainTabControl.SelectedTab = this._StatisticsTabPage;
            ((BindingSource)this._StatisticsDataGridView.DataSource).ResetBindings(false);
            this._StatisticsDataGridView.Refresh();
        }

        private void SetBatchControlsEnabled(bool enabled)
        {
            if (this._BatchImportButton == null) return;
            this._BatchImportButton.Enabled = enabled;
            this._BatchExportButton.Enabled = enabled;
            this._BatchOnceButton.Enabled = enabled;
            this._BatchStartButton.Enabled = enabled;
            this._BatchIntervalBox.Enabled = enabled;
            this._BatchStopButton.Enabled = !enabled && this.SubmissionCanStop;
            if (this._BatchLogStopButton != null) this._BatchLogStopButton.Enabled = this.SubmissionCanStop;
            this._MainTabControl.Enabled = enabled;
            this._ResetButton.Enabled = enabled;
        }

        private void SetBatchStatus(string message)
        {
            this._GameStatusLabel.Text = message;
            this._GameStatusLabel.ToolTipText = message;
        }

        private void AddBatchLog(string message)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message;
            this._BatchLogLines.Add(line);
            bool trimmed = this._BatchLogLines.Count > 20000;
            if (trimmed) this._BatchLogLines.RemoveRange(0, 1000);
            if (this._BatchLogText != null)
            {
                if (trimmed) this._BatchLogText.Text = string.Join(Environment.NewLine, this._BatchLogLines);
                else this._BatchLogText.AppendText((this._BatchLogText.TextLength > 0 ? Environment.NewLine : "") + line);
            }
        }

        private void ExportBatchText(bool useTargets)
        {
            if (this.SubmissionBusy) return;
            if (!this.TryCommitBatchEdit())
            {
                MessageBox.Show(this, "请先修正表格中未完成的输入。", "暂时无法导出");
                return;
            }
            if (useTargets)
            {
                var validation = BatchValidator.ValidateTargets(this.CaptureBatchSchema(), this.CaptureBatchTargets());
                if (!validation.Success)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, validation.Errors), "目标值无效，未导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            using var dialog = new SaveFileDialog
            {
                Title = useTargets ? "导出界面目标值" : "导出 Steam 已读取值",
                Filter = "文本清单 (*.txt)|*.txt", DefaultExt = "txt", AddExtension = true,
                FileName = $"steam-{this._GameId}-{(useTargets ? "targets" : "current")}.txt",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var schema = this.CaptureBatchSchema(!useTargets);
                File.WriteAllText(dialog.FileName, BatchText.Export(checked((uint)this._GameId), schema), new UTF8Encoding(false));
                this.SetBatchStatus($"已导出 {schema.Count} 项字段定义；未读取成功的字段只列为注释。清单尚未提交。 ");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ImportBatchText(object sender, EventArgs e)
        {
            if (this.SubmissionBusy) return;
            if (!this.TryCommitBatchEdit())
            {
                MessageBox.Show(this, "请先修正表格中未完成的输入。", "暂时无法导入");
                return;
            }
            using var dialog = new OpenFileDialog { Title = "导入目标清单", Filter = "文本清单 (*.txt)|*.txt|所有文件 (*.*)|*.*", Multiselect = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024) throw new IOException("清单超过 4 MB，请只保留本次需要的字段。");
                var text = File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true));
                var parsed = BatchText.Parse(text, checked((uint)this._GameId));
                var validation = BatchValidator.Validate(parsed, this.CaptureBatchSchema());
                using var preview = new BatchPreviewDialog(validation, this.CaptureBatchSchema(false), Path.GetFileName(dialog.FileName));
                if (preview.ShowDialog(this) != DialogResult.OK) return;
                this.ApplyBatchTargets(validation.Targets.ToDictionary(t => t.Descriptor.Id, t => t.TargetValue, StringComparer.Ordinal));
                this.SetBatchStatus($"已将 {validation.Targets.Count} 项目标填入表格；尚未向 Steam 发送。可提交一步或自动分步。 ");
                this.AddBatchLog($"导入 {validation.Targets.Count} 项目标，只修改界面待提交值。");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DecoderFallbackException || ex is ArgumentException)
            {
                MessageBox.Show(this, ex.Message, "导入失败，界面保持原值", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowBatchLog()
        {
            if (this._BatchLogWindow != null) { this._BatchLogWindow.Activate(); return; }
            var dialog = new Form { Text = "提交日志 · 实时更新", Width = 960, Height = 580, MinimumSize = new Size(640, 400), Font = this.Font, Padding = new Padding(16), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = true };
            var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10), Text = string.Join(Environment.NewLine, this._BatchLogLines) };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 16, 0, 0), FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存日志…", Width = 120, Height = 38, Margin = new Padding(12, 0, 0, 0) };
            var stop = new Button { Text = "停止后续轮次", Width = 160, Height = 38, Margin = Padding.Empty, Enabled = this.SubmissionCanStop };
            stop.Click += (s, e) => this.StopBatchSubmission();
            save.Click += (s, e) =>
            {
                using var picker = new SaveFileDialog { Filter = "文本日志 (*.txt)|*.txt", FileName = $"steam-{this._GameId}-log.txt" };
                if (picker.ShowDialog(dialog) != DialogResult.OK) return;
                try { File.WriteAllText(picker.FileName, text.Text, new UTF8Encoding(false)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { MessageBox.Show(dialog, ex.Message, "保存失败"); }
            };
            this._BatchLogWindow = dialog;
            this._BatchLogText = text;
            this._BatchLogStopButton = stop;
            dialog.FormClosed += (s, e) =>
            {
                this._BatchLogWindow = null;
                this._BatchLogText = null;
                this._BatchLogStopButton = null;
            };
            footer.Controls.Add(save);
            footer.Controls.Add(stop);
            dialog.Controls.Add(text);
            dialog.Controls.Add(footer);
            dialog.Show(this);
        }
    }
}
