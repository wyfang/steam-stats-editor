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
            this.MinimizeBox = false;
            var pending = displayed.ToDictionary(s => s.Id, s => s.CurrentValue, StringComparer.Ordinal);
            var changed = validation.Targets.Count(t => t.Changed);
            var summary = new Label
            {
                Dock = DockStyle.Top, Height = 70, Padding = new Padding(12, 10, 12, 4),
                Text = validation.Success
                    ? $"清单包含 {validation.Targets.Count} 项，较已读取值变化 {changed} 项。最长预计 {validation.EstimatedRounds:N0} 轮。\n填入界面不会发送到 Steam；未列出的字段保持界面现值。受限目标需分步提交。"
                    : "清单校验未通过，未修改任何界面值。请修正以下问题后重新导入。",
            };
            var errors = new TextBox
            {
                Dock = DockStyle.Top, Height = validation.Success ? 0 : 160, Visible = !validation.Success,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = string.Join(Environment.NewLine, validation.Errors), BackColor = Color.FromArgb(255, 244, 240),
            };
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, AutoGenerateColumns = false,
            };
            foreach (var column in new[] { ("Id", "字段 ID", 230), ("Name", "名称", 210), ("Current", "Steam 已读取", 115), ("Pending", "界面当前值", 115), ("Target", "清单目标值", 115), ("Status", "校验 / 约束", 280) })
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column.Item1, HeaderText = column.Item2, Width = column.Item3 });
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
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var apply = new Button { Text = "填入界面", DialogResult = DialogResult.OK, Width = 120, Height = 30, Enabled = validation.Success && validation.Targets.Count > 0 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
            var filter = new CheckBox { Text = "只看变化项", AutoSize = true, Margin = new Padding(12, 7, 18, 0) };
            filter.CheckedChanged += (s, e) => { grid.CurrentCell = null; foreach (DataGridViewRow row in grid.Rows) row.Visible = !filter.Checked || (bool)row.Tag; };
            footer.Controls.Add(apply);
            footer.Controls.Add(cancel);
            footer.Controls.Add(filter);
            this.Controls.Add(grid);
            this.Controls.Add(errors);
            this.Controls.Add(summary);
            this.Controls.Add(footer);
            this.AcceptButton = apply;
            this.CancelButton = cancel;
        }

        private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string Format(double value, StatValueKind kind) => kind == StatValueKind.Integer
            ? value.ToString("R", CultureInfo.InvariantCulture)
            : ((float)value).ToString("R", CultureInfo.InvariantCulture);
    }
}
