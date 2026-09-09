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
            Console.WriteLine("PASS: Windows import previews rendered; invalid float edits retained without model changes; culture-aware valid floats accepted; valid imports enabled; invalid imports blocked; action buttons inside client bounds.");
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
            var buttonBounds = form.RectangleToClient(apply.RectangleToScreen(apply.ClientRectangle));
            if (!form.ClientRectangle.Contains(buttonBounds)) throw new Exception("Apply button clipped: " + name);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            bitmap.Save(Path.Combine(output, name + "-" + size.Width + ".png"), ImageFormat.Png);
        }
        form.Close();
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
