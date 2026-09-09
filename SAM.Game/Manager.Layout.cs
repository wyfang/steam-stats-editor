// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SAM.Game
{
    internal partial class Manager
    {
        private void InitializeManagerLayout()
        {
            this.SuspendLayout();
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.Size = new Size(1180, 780);
            this.MinimumSize = new Size(1000, 640);
            this.BackColor = Color.FromArgb(245, 247, 250);
            this.StartPosition = FormStartPosition.CenterScreen;

            var layout = new TableLayoutPanel
            {
                Name = "ManagerLayout", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Padding = new Padding(16, 12, 16, 12), Margin = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            this._MainToolStrip.Items.Clear();
            this.ConfigureManagerStrip(this._MainToolStrip);
            this._MainToolStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel("清单") { ForeColor = Color.DimGray },
                this._BatchExportButton, this._BatchImportButton, this._ReloadButton,
                new ToolStripSeparator(), this._BatchLogButton,
            });
            var more = new ToolStripDropDownButton("更多操作") { Name = "MoreActions" };
            more.DropDownItems.Add(this._ResetButton);
            this._ResetButton.Text = "重置统计／成就…";
            this._MainToolStrip.Items.Add(more);
            this._BatchExportButton.Name = "BatchExportButton";
            this._BatchImportButton.Name = "BatchImportButton";
            this._BatchLogButton.Name = "BatchLogButton";

            var submissionStrip = new ToolStrip { Name = "SubmissionToolStrip" };
            this.ConfigureManagerStrip(submissionStrip);
            this._StoreButton.Alignment = ToolStripItemAlignment.Left;
            this._StoreButton.Text = "提交全部改动…";
            this._BatchOnceButton.Name = "BatchOnceButton";
            this._BatchStartButton.Name = "BatchStartButton";
            this._BatchStopButton.Name = "BatchStopButton";
            this._BatchStartButton.Text = "自动分步…";
            this._BatchStopButton.ForeColor = Color.FromArgb(157, 49, 49);
            this._BatchIntervalBox.Name = "BatchIntervalBox";
            this._BatchIntervalBox.Font = this.Font;
            this._BatchIntervalBox.Width = 66;
            this._BatchIntervalBox.AccessibleName = "自动分步间隔秒数";
            submissionStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel("提交") { ForeColor = Color.DimGray },
                this._StoreButton, this._BatchOnceButton, new ToolStripSeparator(),
                this._BatchStartButton, new ToolStripLabel("间隔"), this._BatchIntervalBox,
                new ToolStripLabel("秒"), this._BatchStopButton,
            });
            foreach (ToolStrip strip in new[] { this._MainToolStrip, submissionStrip })
            {
                foreach (ToolStripItem item in strip.Items) ConfigureManagerItem(item);
            }

            var hint = new Label
            {
                Name = "BatchWorkflowHint", Text = "导出清单 → 修改文本 → 导入预览 → 提交并核验。导入只填入界面目标值。",
                AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(87, 100, 118),
                Margin = new Padding(4, 2, 4, 14),
            };
            this._MainTabControl.Dock = DockStyle.Fill;
            this._MainTabControl.Margin = Padding.Empty;
            this._MainTabControl.Padding = new Point(18, 8);
            this._StatisticsTabPage.Padding = new Padding(12);
            this._AchievementsTabPage.Padding = new Padding(12);
            this._StatisticsTabPage.BackColor = Color.White;

            var statsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty,
            };
            statsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            this._EnableStatsEditingCheckBox.Dock = DockStyle.Fill;
            this._EnableStatsEditingCheckBox.Margin = new Padding(0, 0, 0, 12);
            this._EnableStatsEditingCheckBox.Padding = new Padding(0, 4, 0, 4);
            this._EnableStatsEditingCheckBox.AutoSize = true;
            var grid = this._StatisticsDataGridView;
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = Color.FromArgb(227, 232, 239);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 240, 247);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(29, 43, 64);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = 40;
            grid.RowTemplate.Height = 34;
            grid.DefaultCellStyle.Padding = new Padding(8, 3, 8, 3);
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.MinimumWidth = column.Name == "apiId" ? 190 : 110;
                column.FillWeight = column.Name == "apiId" ? 27 : column.Name == "name" ? 25 : 16;
            }
            // Preserve the original Value column at index 1: edit handlers depend on it.
            grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns[1].DefaultCellStyle.BackColor = Color.FromArgb(241, 247, 255);
            grid.Columns["confirmed"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            grid.Columns["confirmed"].HeaderText = "Steam 已读取值";
            grid.Columns["confirmed"].MinimumWidth = 140;
            grid.Columns[1].HeaderText = "界面目标值";
            grid.Columns[1].MinimumWidth = 130;
            statsLayout.Controls.Add(this._EnableStatsEditingCheckBox, 0, 0);
            statsLayout.Controls.Add(grid, 0, 1);
            this._StatisticsTabPage.Controls.Clear();
            this._StatisticsTabPage.Controls.Add(statsLayout);

            this.ConfigureManagerStrip(this._AchievementsToolStrip);
            this._AchievementsToolStrip.Dock = DockStyle.Top;
            this._LockAllButton.Text = "全部锁定";
            this._InvertAllButton.Text = "反选";
            this._UnlockAllButton.Text = "全部解锁";
            this._DisplayLabel.Text = "仅显示";
            this._DisplayLockedOnlyButton.Text = "未解锁";
            this._DisplayUnlockedOnlyButton.Text = "已解锁";
            this._MatchingStringLabel.Text = "搜索";
            this._MatchingStringTextBox.Width = 160;
            foreach (ToolStripItem item in this._AchievementsToolStrip.Items)
            {
                ConfigureManagerItem(item);
                if (item is ToolStripButton) item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            }
            this._AchievementListView.BorderStyle = BorderStyle.None;
            this._AchievementNameColumnHeader.Text = "成就名称";
            this._AchievementDescriptionColumnHeader.Text = "说明";
            this._AchievementUnlockTimeColumnHeader.Text = "解锁时间";

            this._MainStatusStrip.Font = this.Font;
            this._MainStatusStrip.Padding = new Padding(16, 5, 16, 5);
            this._MainStatusStrip.BackColor = Color.FromArgb(234, 238, 244);
            this.Controls.Clear();
            layout.Controls.Add(this._MainToolStrip, 0, 0);
            layout.Controls.Add(submissionStrip, 0, 1);
            layout.Controls.Add(hint, 0, 2);
            layout.Controls.Add(this._MainTabControl, 0, 3);
            this.Controls.Add(layout);
            this.Controls.Add(this._MainStatusStrip);
            this.ResumeLayout(true);
        }

        private void ConfigureManagerStrip(ToolStrip strip)
        {
            strip.Font = this.Font;
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.RenderMode = ToolStripRenderMode.System;
            strip.BackColor = Color.White;
            strip.Dock = DockStyle.Fill;
            strip.AutoSize = true;
            strip.CanOverflow = false;
            strip.LayoutStyle = ToolStripLayoutStyle.Flow;
            strip.Padding = new Padding(6);
            strip.Margin = new Padding(0, 0, 0, 8);
        }

        private static void ConfigureManagerItem(ToolStripItem item)
        {
            item.Overflow = ToolStripItemOverflow.Never;
            item.Margin = new Padding(2, 4, 8, 4);
            if (item is ToolStripButton || item is ToolStripDropDownButton)
                item.Padding = new Padding(10, 6, 10, 6);
            else if (item is ToolStripLabel)
                item.Padding = new Padding(2, 6, 2, 6);
        }

        // Used only by the Windows offline layout checks. No Steam client or timers are created.
        internal static Manager CreateOfflinePreview() => new Manager(730, null, true);

        private void InitializeOfflinePreview()
        {
            this.Text += " | 合成数据预览";
            this._Statistics.Add(new Stats.IntStatInfo { Id = "sample_count", DisplayName = "示例计数", OriginalValue = 10, IntValue = 12 });
            this._Statistics.Add(new Stats.IntStatInfo { Id = "sample_step", DisplayName = "分步统计示例", OriginalValue = 20, IntValue = 25, IsIncrementOnly = true });
            this._Statistics.Add(new Stats.IntStatInfo { Id = "sample_protected", DisplayName = "受保护字段", OriginalValue = 0, IntValue = 0, Permission = 2 });
            this._Statistics.Add(new Stats.FloatStatInfo { Id = "sample_float", DisplayName = "浮点统计示例", OriginalValue = 0.1f, FloatValue = 0.2f });
            this._DownloadStatusLabel.Visible = false;
            this.SetOfflineSubmissionPreview(false);
        }

        internal void SetOfflineSubmissionPreview(bool running)
        {
            if (!this._OfflinePreview) throw new InvalidOperationException("Only available for the offline preview.");
            this.SetBatchControlsEnabled(!running);
            this._StoreButton.Enabled = !running;
            this._ReloadButton.Enabled = !running;
            this._BatchStopButton.Enabled = running;
            this.SetBatchStatus(running ? "合成预览：等待下一轮，可停止后续轮次。" : "合成预览：已读取 4 项统计，导入只更新界面目标值。");
        }
    }
}
