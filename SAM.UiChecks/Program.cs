// Windows 离线 UI 检查：仅使用合成字段，不连接 Steam，不提交数据。
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SAM.Batch;
using SAM.Game;
using SAM.Game.Stats;
using SAM.Picker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            CheckFloatEdits("en-US", "0.1", "1,234.5");
            CheckFloatEdits("de-DE", "0,1", "1.234,5");
            string output = args.Length > 0 ? args[0] : "ui-artifacts";
            Directory.CreateDirectory(output);
            var schema = new List<StatDescriptor>
            {
                new StatDescriptor { Id = "sample_count", DisplayName = "示例计数（合成测试数据）", Kind = StatValueKind.Integer, CurrentValue = 10, Minimum = 0, Maximum = int.MaxValue },
                new StatDescriptor { Id = "sample_step", DisplayName = "每次最多增加 1", Kind = StatValueKind.Integer, CurrentValue = 20, Minimum = 0, Maximum = int.MaxValue, MaxChange = 1, IncrementOnly = true },
                new StatDescriptor { Id = "sample_zero", DisplayName = "明确设为零", Kind = StatValueKind.Integer, CurrentValue = 50, Minimum = 0, Maximum = int.MaxValue },
                new StatDescriptor { Id = "sample_float", DisplayName = "浮点值往返显示", Kind = StatValueKind.Float, CurrentValue = 0.1f, Minimum = 0, Maximum = 1 },
            };
            string header = "format = steam-stats-v1\nappid = 730\n[stats]\n";
            Check(BatchValidator.Validate(BatchText.Parse(header + "sample_count = 12\nsample_step = 25\nsample_zero = 0\nsample_float = 0.2\n", 730), schema), schema, output, "preview-valid", true);
            Check(BatchValidator.Validate(BatchText.Parse(header + "unknown_field = 123\n", 730), schema), schema, output, "preview-invalid", false);
            CheckApplicationWindow(() => GamePicker.CreateOfflinePreview(), output, "picker", false);
            CheckApplicationWindow(() => Manager.CreateOfflinePreview(), output, "manager", true);
            File.WriteAllText(Path.Combine(output, "verification-scope.txt"),
                "本检查直接引用并运行本次构建的 SAM.Game 与 SAM.Picker 界面，使用合成数据，不连接 Steam。\r\n" +
                "default / minimum 为 Windows 原生窗口截图；scale150-simulated 仅调用 Control.Scale(1.5)，不是实际 150% DPI 环境测试。\r\n" +
                "检查工具栏操作始终可见、没有溢出菜单、按钮在父容器边界内、同行控件不重叠且具有间距，并检查运行状态的停止按钮。\r\n");
            Console.WriteLine("PASS: actual Windows picker, manager, achievements and import previews rendered at default/minimum sizes; 150% Control.Scale simulation rendered (not a real DPI test); toolbar actions remain visible with spacing; stop is enabled during the offline running simulation; invalid float edits retained; valid imports enabled; invalid imports blocked.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(ValidationResult result, IEnumerable<StatDescriptor> schema, string output, string name, bool valid)
    {
        using var form = new BatchPreviewDialog(result, schema, "synthetic-fixture.txt");
        form.Show();
        Application.DoEvents();
        var apply = All(form).OfType<Button>().Single(b => b.DialogResult == DialogResult.OK);
        if (apply.Enabled != valid) throw new Exception("Unexpected apply state: " + name);
        var grid = All(form).OfType<DataGridView>().Single();
        if (valid)
        {
            if (grid.Rows.Count != 4) throw new Exception("Expected all four targets in preview.");
            var floatRow = grid.Rows.Cast<DataGridViewRow>().Single(r => (string)r.Cells["Id"].Value == "sample_float");
            if ((string)floatRow.Cells["Current"].Value != "0.1" || (string)floatRow.Cells["Target"].Value != "0.2")
                throw new Exception("Float preview did not preserve round-trip decimal text.");
        }
        foreach (var size in new[] { new Size(1140, 720), new Size(900, 540) })
        {
            form.Size = size;
            Application.DoEvents();
            SaveWindow(form, output, name + "-" + size.Width);
            CheckButtons(form, name, 12);
        }
        form.Close();
    }

    private static void CheckApplicationWindow(Func<Form> create, string output, string name, bool manager)
    {
        using (var form = create())
        {
            form.Show();
            Application.DoEvents();
            SaveWindow(form, output, name + "-default");
            CheckApplicationLayout(form, name + "-default", manager);
            if (manager)
            {
                var grid = All(form).OfType<DataGridView>().Single();
                if (grid.Columns[1].DataPropertyName != "Value" || grid.Columns["confirmed"].DataPropertyName != "ConfirmedValue")
                    throw new Exception("Manager value columns no longer bind to separate target and readback values.");
                var row = grid.Rows.Cast<DataGridViewRow>().Single(item => (string)item.Cells["apiId"].Value == "sample_count");
                if (Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture) != "12" ||
                    Convert.ToString(row.Cells["confirmed"].Value, CultureInfo.InvariantCulture) != "10")
                    throw new Exception("Manager must show the pending target 12 separately from the readback value 10.");
            }
            form.Size = form.MinimumSize;
            Application.DoEvents();
            SaveWindow(form, output, name + "-minimum");
            CheckApplicationLayout(form, name + "-minimum", manager);
            if (manager)
            {
                var managerForm = (Manager)form;
                managerForm.SetOfflineSubmissionPreview(true);
                Application.DoEvents();
                SaveWindow(form, output, name + "-minimum-running");
                var stop = FindAction(form, "停止");
                if (!stop.Enabled) throw new Exception("Stop must remain enabled during the offline running preview.");
                CheckToolbars(form, name + "-minimum-running");
                managerForm.SetOfflineSubmissionPreview(false);
                Application.DoEvents();
                var tabs = All(form).OfType<TabControl>().Single();
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(page => page.Text.StartsWith("成就", StringComparison.Ordinal));
                Application.DoEvents();
                SaveWindow(form, output, name + "-achievements-minimum");
                CheckApplicationLayout(form, name + "-achievements-minimum", manager);
            }
            form.Close();
        }
        using (var form = create())
        {
            form.Show();
            Application.DoEvents();
            form.Scale(new SizeF(1.5F, 1.5F));
            form.Text += " · 150% Scale 布局模拟（非真实 DPI）";
            Application.DoEvents();
            SaveWindow(form, output, name + "-scale150-simulated");
            CheckApplicationLayout(form, name + "-scale150-simulated", manager);
            form.Close();
        }
    }

    private static void CheckApplicationLayout(Form form, string context, bool manager)
    {
        CheckToolbars(form, context);
        CheckButtons(form, context, 8);
        if (!manager) return;
        foreach (string prefix in new[] { "导入清单", "提交一步", "自动分步" })
        {
            var action = FindAction(form, prefix);
            if (!action.Enabled) throw new Exception(context + ": idle action is disabled: " + action.Text);
        }
        FindAction(form, "停止");
    }

    private static ToolStripItem FindAction(Form form, string textPrefix)
    {
        var result = All(form).OfType<ToolStrip>().Where(strip => !(strip is StatusStrip))
            .SelectMany(strip => strip.Items.Cast<ToolStripItem>())
            .Single(item => item.Text.StartsWith(textPrefix, StringComparison.Ordinal));
        if (!result.Available || !result.Visible || result.IsOnOverflow || result.Placement != ToolStripItemPlacement.Main)
            throw new Exception("Required action is hidden or moved to overflow: " + result.Text);
        return result;
    }

    private static void CheckToolbars(Form form, string context)
    {
        foreach (var strip in All(form).OfType<ToolStrip>().Where(strip => strip.Visible && !(strip is StatusStrip)))
        {
            var items = strip.Items.Cast<ToolStripItem>().Where(item => item.Available && !(item is ToolStripSeparator)).ToArray();
            foreach (var item in items)
            {
                if (item.IsOnOverflow || item.Placement != ToolStripItemPlacement.Main || !item.Visible)
                    throw new Exception(context + ": toolbar item hidden or in overflow: " + item.Text);
                if (!strip.ClientRectangle.Contains(item.Bounds))
                    throw new Exception(context + ": toolbar item extends beyond its strip: " + item.Text + " " + item.Bounds);
                CheckInsideAncestors(strip, item.Bounds, context + ": " + item.Text);
            }
            for (int i = 0; i < items.Length; i++)
            for (int j = i + 1; j < items.Length; j++)
                CheckHorizontalGap(items[i].Bounds, items[j].Bounds, 4, context + ": " + items[i].Text + " / " + items[j].Text);
        }
    }

    private static void CheckButtons(Form form, string context, int minimumGap)
    {
        var buttons = All(form).OfType<Button>().Where(button => button.Visible).ToArray();
        foreach (var button in buttons)
            CheckInsideAncestors(button.Parent, button.Bounds, context + ": " + button.Text);
        for (int i = 0; i < buttons.Length; i++)
        for (int j = i + 1; j < buttons.Length; j++)
        {
            if (buttons[i].Parent != buttons[j].Parent) continue;
            CheckHorizontalGap(buttons[i].Bounds, buttons[j].Bounds, minimumGap, context + ": " + buttons[i].Text + " / " + buttons[j].Text);
        }
    }

    private static void CheckInsideAncestors(Control owner, Rectangle bounds, string context)
    {
        var screenBounds = owner.RectangleToScreen(bounds);
        for (Control ancestor = owner; ancestor != null; ancestor = ancestor.Parent)
        {
            if (!ancestor.RectangleToScreen(ancestor.ClientRectangle).Contains(screenBounds))
                throw new Exception(context + ": clipped by " + ancestor.GetType().Name + " " + ancestor.Name);
        }
    }

    private static void CheckHorizontalGap(Rectangle first, Rectangle second, int minimumGap, string context)
    {
        if (Math.Min(first.Bottom, second.Bottom) <= Math.Max(first.Top, second.Top)) return;
        var left = first.Left <= second.Left ? first : second;
        var right = first.Left <= second.Left ? second : first;
        int gap = right.Left - left.Right;
        if (gap < minimumGap) throw new Exception(context + ": horizontal control gap is " + gap + " px; required " + minimumGap + " px.");
    }

    private static void SaveWindow(Form form, string output, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
        Console.WriteLine("Rendered " + name + " (" + form.Width + "x" + form.Height + ")");
    }

    private static void CheckFloatEdits(string cultureName, string smallValue, string groupedValue)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        try
        {
            var stat = new FloatStatInfo { Id = "synthetic_float", OriginalValue = 5f, FloatValue = 5f };
            using var source = new BindingSource { DataSource = new BindingList<StatInfo> { stat } };
            using var form = new Form { Width = 480, Height = 240 };
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
                DataSource = source,
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Value", Name = "Value", Width = 250 });
            int errors = 0;
            grid.DataError += (sender, e) => { errors++; e.ThrowException = false; e.Cancel = true; };
            form.Controls.Add(grid);
            form.Show();
            Application.DoEvents();

            EditFloatCell(grid, smallValue);
            if (!grid.EndEdit() || stat.FloatValue != 0.1f)
                throw new Exception("Valid decimal input was rejected: " + cultureName);
            source.EndEdit();

            foreach (string invalid in new[] { "NaN", "Infinity", "-Infinity", "16777217", "1e-1000" })
            {
                float before = stat.FloatValue;
                int previousErrors = errors;
                EditFloatCell(grid, invalid);
                if (grid.EndEdit() || errors <= previousErrors || stat.FloatValue != before)
                    throw new Exception("Invalid float input changed the bound value: " + cultureName + " / " + invalid);
                if (!(grid.EditingControl is TextBox editor) || editor.Text != invalid)
                    throw new Exception("Invalid float text was discarded before correction: " + invalid);
                grid.CancelEdit();
            }

            EditFloatCell(grid, groupedValue);
            if (!grid.EndEdit() || stat.FloatValue != 1234.5f)
                throw new Exception("Culture-specific grouping or decimal separator was rejected: " + cultureName);
            source.EndEdit();
            form.Close();
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static void EditFloatCell(DataGridView grid, string text)
    {
        grid.CurrentCell = grid.Rows[0].Cells["Value"];
        if (!grid.BeginEdit(true) || !(grid.EditingControl is TextBox editor))
            throw new Exception("Could not begin the float cell edit.");
        editor.Text = text;
    }

    private static IEnumerable<Control> All(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in All(child)) yield return descendant;
        }
    }
}
