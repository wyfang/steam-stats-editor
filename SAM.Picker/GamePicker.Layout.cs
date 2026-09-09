// Steam Stats Editor additions. Distributed under the repository's zlib license.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace SAM.Picker
{
    internal partial class GamePicker
    {
        private Button _OpenSelectedGameButton;

        private void InitializeModernPickerLayout()
        {
            this.SuspendLayout();
            this.Text = "Steam Stats Editor · 选择游戏";
            this.Font = new Font("Microsoft YaHei UI", 9F);
            this.ClientSize = new Size(1040, 700);
            this.MinimumSize = new Size(860, 560);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(245, 247, 250);

            var layout = new TableLayoutPanel
            {
                Name = "PickerLayout",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(20, 18, 20, 12),
                Margin = Padding.Empty,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new FlowLayoutPanel
            {
                Name = "PickerHeading",
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 16),
            };
            heading.Controls.Add(new Label
            {
                Text = "选择要编辑的游戏",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 43, 64),
                Margin = new Padding(0, 0, 0, 6),
            });
            heading.Controls.Add(new Label
            {
                Text = "双击游戏封面，或选中游戏后点击“打开编辑器”。",
                AutoSize = true,
                ForeColor = Color.FromArgb(87, 100, 118),
                Margin = Padding.Empty,
            });

            this._PickerToolStrip.Items.Clear();
            ConfigurePickerStrip(this._PickerToolStrip);
            this._RefreshGamesButton.Text = "刷新游戏";
            this._RefreshGamesButton.ToolTipText = "重新读取当前账户的游戏列表";
            this._FindGamesLabel.Text = "搜索游戏";
            this._FindGamesLabel.Margin = new Padding(12, 0, 6, 0);
            this._SearchGameTextBox.AutoSize = false;
            this._SearchGameTextBox.Width = 240;
            this._SearchGameTextBox.Font = this.Font;
            this._SearchGameTextBox.ToolTipText = "输入游戏名称筛选列表";
            this._SearchGameTextBox.AccessibleName = "搜索游戏名称";
            this._SearchGameTextBox.Margin = new Padding(0, 6, 12, 6);
            this._FilterDropDownButton.Text = "游戏类型";
            this._FilterDropDownButton.ToolTipText = "选择列表中显示的游戏类型";
            this._FilterDropDownButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            this._FilterGamesMenuItem.Text = "正式游戏";
            this._FilterDemosMenuItem.Text = "试玩版";
            this._FilterModsMenuItem.Text = "模组";
            this._FilterJunkMenuItem.Text = "其他";
            ConfigurePickerButton(this._RefreshGamesButton);
            ConfigurePickerButton(this._FilterDropDownButton);
            this._PickerToolStrip.Items.AddRange(new ToolStripItem[]
            {
                this._RefreshGamesButton,
                this._FindGamesLabel,
                this._SearchGameTextBox,
                this._FilterDropDownButton,
            });

            var addGameStrip = new ToolStrip { Name = "PickerAddGameStrip" };
            ConfigurePickerStrip(addGameStrip);
            addGameStrip.Margin = new Padding(0, 0, 0, 16);
            this._AddGameTextBox.AutoSize = false;
            this._AddGameTextBox.Width = 116;
            this._AddGameTextBox.Font = this.Font;
            this._AddGameTextBox.Margin = new Padding(8, 6, 10, 6);
            this._AddGameTextBox.ToolTipText = "Steam 商店网址中的数字编号；CS2 为 730";
            this._AddGameTextBox.AccessibleName = "游戏 App ID";
            this._AddGameTextBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    this.OnAddGame(this._AddGameButton, EventArgs.Empty);
                }
            };
            this._AddGameButton.Text = "查找游戏";
            this._AddGameButton.ToolTipText = "按 App ID 显示当前账户拥有的游戏";
            ConfigurePickerButton(this._AddGameButton);
            addGameStrip.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripLabel("按 App ID 查找") { Margin = new Padding(8, 0, 0, 0) },
                this._AddGameTextBox,
                this._AddGameButton,
                new ToolStripLabel("列表中找不到时可输入编号，例如 CS2：730")
                {
                    ForeColor = Color.FromArgb(87, 100, 118),
                    Margin = new Padding(14, 0, 0, 0),
                },
            });
            foreach (var strip in new[] { this._PickerToolStrip, addGameStrip })
            {
                foreach (ToolStripItem item in strip.Items)
                {
                    if (item is ToolStripLabel)
                    {
                        item.Padding = new Padding(2, 8, 2, 8);
                        item.Margin = new Padding(item.Margin.Left, 2, item.Margin.Right, 2);
                    }
                }
            }

            var listFrame = new Panel
            {
                Name = "PickerGameListFrame",
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(12),
                Margin = Padding.Empty,
            };
            this._GameListView.BackColor = Color.White;
            this._GameListView.ForeColor = Color.FromArgb(29, 43, 64);
            this._GameListView.BorderStyle = BorderStyle.None;
            this._GameListView.Margin = Padding.Empty;
            this._GameListView.ShowItemToolTips = true;
            this._GameListView.SelectedIndexChanged += (_, _) =>
                this._OpenSelectedGameButton.Enabled = this._GameListView.SelectedIndices.Count > 0;
            listFrame.Controls.Add(this._GameListView);

            var footer = new FlowLayoutPanel
            {
                Name = "PickerActions",
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true,
                Margin = new Padding(0, 14, 0, 0),
            };
            this._OpenSelectedGameButton = new Button
            {
                Name = "OpenSelectedGameButton",
                Text = "打开编辑器",
                AutoSize = true,
                MinimumSize = new Size(152, 40),
                Padding = new Padding(18, 8, 18, 8),
                Margin = Padding.Empty,
                BackColor = Color.FromArgb(33, 91, 180),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                UseVisualStyleBackColor = false,
            };
            this._OpenSelectedGameButton.FlatAppearance.BorderSize = 0;
            this._OpenSelectedGameButton.BackColor = Color.FromArgb(224, 229, 236);
            this._OpenSelectedGameButton.EnabledChanged += (_, _) =>
                this._OpenSelectedGameButton.BackColor = this._OpenSelectedGameButton.Enabled
                    ? Color.FromArgb(33, 91, 180) : Color.FromArgb(224, 229, 236);
            this._OpenSelectedGameButton.Click += (_, _) => this.OnActivateGame(this._GameListView, EventArgs.Empty);
            footer.Controls.Add(this._OpenSelectedGameButton);

            this._PickerStatusStrip.Font = this.Font;
            this._PickerStatusStrip.BackColor = Color.FromArgb(234, 238, 244);
            this._PickerStatusStrip.Padding = new Padding(20, 5, 20, 5);
            this._PickerStatusStrip.SizingGrip = true;

            this.Controls.Clear();
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(this._PickerToolStrip, 0, 1);
            layout.Controls.Add(addGameStrip, 0, 2);
            layout.Controls.Add(listFrame, 0, 3);
            layout.Controls.Add(footer, 0, 4);
            this.Controls.Add(layout);
            this.Controls.Add(this._PickerStatusStrip);
            this.ResumeLayout(true);
        }

        private void ConfigurePickerStrip(ToolStrip strip)
        {
            strip.Font = this.Font;
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.RenderMode = ToolStripRenderMode.System;
            strip.BackColor = Color.White;
            strip.Padding = new Padding(6);
            strip.Margin = new Padding(0, 0, 0, 8);
            strip.Dock = DockStyle.Fill;
            strip.AutoSize = true;
            strip.CanOverflow = false;
            strip.LayoutStyle = ToolStripLayoutStyle.Flow;
        }

        private static void ConfigurePickerButton(ToolStripItem item)
        {
            item.Padding = new Padding(10, 6, 10, 6);
            item.Margin = new Padding(2, 2, 8, 2);
        }
    }
}
