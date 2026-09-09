// Steam Stats Editor additions by wyfang, 2026. Distributed under LICENSE.txt (zlib).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SAM.Batch;

namespace SAM.Game
{
    internal sealed class BatchPreviewDialog : Form
    {
        public BatchPreviewDialog(ValidationResult validation, IEnumerable<StatDescriptor> displayed, string fileName)
        {
            this.Text = "导入差异预览 · " + fileName;
            this.Size = new Size(1140, 720);
            this.MinimumSize = new Size(900, 540);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.FromArgb(245, 247, 250);
            this.MinimizeBox = false;
            var pending = displayed.ToDictionary(s => s.Id, s => s.CurrentValue, StringComparer.Ordinal);
            var changed = validation.Targets.Count(t => t.Changed);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Padding = new Padding(20, 18, 20, 0),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, validation.Success ? 0 : 146));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var summary = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2,
                Margin = new Padding(0, 0, 0, 16),
            };
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            summary.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            summary.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 8),
                Font = new Font(this.Font.FontFamily, 12F, FontStyle.Bold),
                ForeColor = validation.Success ? Color.FromArgb(30, 45, 62) : Color.FromArgb(160, 48, 32),
                Text = validation.Success
                    ? $"{validation.Targets.Count} 项已校验 · {changed} 项与 Steam 已读取值不同"
                    : "清单校验未通过",
            }, 0, 0);
            summary.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty,
                ForeColor = Color.FromArgb(78, 91, 106),
                Text = validation.Success
                    ? $"核对文件目标值后填入界面；这一步不会发送到 Steam。未列出的字段保留界面目标值。\n受限目标需分步提交，按当前读值最长预计 {validation.EstimatedRounds:N0} 轮。"
                    : "未修改任何界面值。请修正以下问题后重新导入。",
            }, 0, 1);
            var errors = new TextBox
            {
                Dock = DockStyle.Fill, Visible = !validation.Success, Margin = new Padding(0, 0, 0, 16),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = string.Join(Environment.NewLine, validation.Errors), BackColor = Color.FromArgb(255, 244, 240),
                ForeColor = Color.FromArgb(145, 41, 29), BorderStyle = BorderStyle.FixedSingle,
            };
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoGenerateColumns = false,
                Margin = Padding.Empty, EnableHeadersVisualStyles = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                GridColor = Color.FromArgb(233, 237, 242),
                ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            grid.RowTemplate.Height = 34;
            grid.DefaultCellStyle.Padding = new Padding(10, 4, 10, 4);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(35, 46, 60);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(214, 232, 250);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(18, 48, 76);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(231, 236, 243);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(55, 69, 86);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 6, 10, 6);
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            foreach (var column in new[] { ("Id", "字段 ID", 22, 180), ("Name", "名称", 21, 150), ("Current", "Steam 已读取值", 13, 118), ("Pending", "界面目标值", 12, 106), ("Target", "文件目标值", 12, 106), ("Status", "校验 / 约束", 20, 160) })
            {
                var valueColumn = column.Item1 == "Current" || column.Item1 == "Pending" || column.Item1 == "Target";
                grid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = column.Item1, HeaderText = column.Item2, FillWeight = column.Item3, MinimumWidth = column.Item4,
                    DefaultCellStyle = new DataGridViewCellStyle
                    {
                        Alignment = valueColumn ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft,
                    },
                });
            }
            foreach (var target in validation.Targets)
            {
                var stat = target.Descriptor;
                var constraints = new List<string>();
                if (!target.Changed) constraints.Add("与已读取值相同");
                if (stat.MaxChange > 0) constraints.Add("每次最多 " + Format(stat.MaxChange));
                if (stat.IncrementOnly) constraints.Add("只增不减");
                if (stat.IsProtected || stat.Permission != 0 || stat.SetByTrustedGameServer) constraints.Add("只读");
                if (target.RequiresStepping) constraints.Add($"{target.EstimatedRounds:N0} 轮");
                int index = grid.Rows.Add(stat.Id, stat.DisplayName, Format(stat.CurrentValue, stat.Kind), pending.TryGetValue(stat.Id, out var value) ? Format(value, stat.Kind) : "未读取", Format(target.TargetValue, stat.Kind), string.Join("；", constraints));
                grid.Rows[index].Tag = target.Changed || (pending.TryGetValue(stat.Id, out var uiValue) && uiValue != target.TargetValue);
                if ((bool)grid.Rows[index].Tag) grid.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(233, 245, 252);
            }
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0, 18, 0, 18), Margin = Padding.Empty,
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var actions = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty,
            };
            var apply = new Button
            {
                Text = "确认填入界面", DialogResult = DialogResult.OK, Width = 144, Height = 38,
                Margin = Padding.Empty, Enabled = validation.Success && validation.Targets.Count > 0,
                BackColor = Color.FromArgb(35, 99, 170), ForeColor = Color.White, FlatStyle = FlatStyle.Flat,
            };
            apply.FlatAppearance.BorderSize = 0;
            var cancel = new Button
            {
                Text = "取消", DialogResult = DialogResult.Cancel, Width = 96, Height = 38,
                Margin = new Padding(0, 0, 12, 0), UseVisualStyleBackColor = true,
            };
            var filter = new CheckBox
            {
                Text = "只看变化项", AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = Padding.Empty, Enabled = validation.Success,
            };
            filter.CheckedChanged += (s, e) => { grid.CurrentCell = null; foreach (DataGridViewRow row in grid.Rows) row.Visible = !filter.Checked || (bool)row.Tag; };
            actions.Controls.Add(cancel);
            actions.Controls.Add(apply);
            footer.Controls.Add(filter, 0, 0);
            footer.Controls.Add(actions, 1, 0);
            layout.Controls.Add(summary, 0, 0);
            layout.Controls.Add(errors, 0, 1);
            layout.Controls.Add(grid, 0, 2);
            layout.Controls.Add(footer, 0, 3);
            this.Controls.Add(layout);
            this.AcceptButton = apply;
            this.CancelButton = cancel;
        }

        private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Format(double value, StatValueKind kind) => kind == StatValueKind.Integer
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : ((float)value).ToString("R", CultureInfo.InvariantCulture);
    }
}
