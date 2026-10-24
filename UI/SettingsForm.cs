using System;
using System.Drawing;
using System.Windows.Forms;

namespace PeripheralBatteryLite
{
    internal sealed class SettingsForm : Form
    {
        private readonly ComboBox interval;
        private readonly CheckBox onlyActive;
        private readonly CheckBox startup;
        private readonly CheckBox pinned;
        private readonly CheckBox loadPlugins;
        private readonly SoftButton save;
        private readonly SoftButton cancel;

        public AppPreferences ResultSettings { get; private set; }
        public bool ResultStartup { get; private set; }

        public SettingsForm(AppPreferences current, bool startsWithWindows, int providerCount,
            bool hasRawInput)
        {
            ResultSettings = current.Copy();
            ResultStartup = startsWithWindows;
            Text = "Peripheral Battery · 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(440, 474);

            LabelAt("偏好设置", 19, 15, 390, 31, 15.3f, true, UiTheme.Text);
            LabelAt("睡眠友好 · 可控轮询 · 本地运行", 20, 47, 390, 20, 8.8f, false, UiTheme.Secondary);

            LabelAt("自动查询间隔", 22, 88, 210, 27, 10f, true, UiTheme.Text);
            interval = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(22, 121), Size = new Size(392, 30),
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text,
                FlatStyle = FlatStyle.Flat, Font = new Font("Microsoft YaHei UI", 9.4f)
            };
            interval.Items.Add("关闭 · 仅手动刷新");
            interval.Items.Add("每 30 分钟");
            interval.Items.Add("每 1 小时");
            interval.Items.Add("每 2 小时");
            interval.Items.Add("每 5 小时");
            int[] choices = { 0, 30, 60, 120, 300 };
            interval.SelectedIndex = Array.IndexOf(choices, current.IntervalMinutes);
            if (interval.SelectedIndex < 0) interval.SelectedIndex = 0;
            Controls.Add(interval);

            onlyActive = NewCheck("仅在设备近期活动时自动查询（省电）", 22, 168, 400, current.OnlyWhenDeviceActive);
            LabelAt("通过键盘/鼠标的输入事件判断；耳机无法被动确认活跃，", 42, 195,
                382, 18, 7.6f, false, UiTheme.Secondary);
            LabelAt("开启此选项时耳机仅支持手动刷新。", 42, 214,
                374, 18, 7.6f, false, UiTheme.Secondary);

            startup = NewCheck("随 Windows 登录自动启动", 22, 250, 400, startsWithWindows);
            pinned = NewCheck("默认固定电量悬浮窗", 22, 286, 400, current.PinPanel);
            loadPlugins = NewCheck("允许加载 Plugins 目录内的第三方 DLL", 22, 324,
                405, current.EnableExternalPlugins);
            LabelAt("扩展插件以当前用户权限运行，仅启用可信来源。", 42, 350,
                375, 18, 7.8f, false, UiTheme.Warning);

            string providerState = "已内置 " + providerCount + " 个协议提供器";
            if (!hasRawInput) providerState += " · Raw Input 未注册，活动限制仅支持手动";
            LabelAt(providerState, 22, 386, 396, 20, 8.2f, false, UiTheme.Secondary);

            cancel = new SoftButton(false) { Text = "取消", Location = new Point(226, 424), Size = new Size(88, 34) };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            save = new SoftButton(true) { Text = "保存设置", Location = new Point(324, 424), Size = new Size(90, 34) };
            save.Click += SaveClicked;
            Controls.Add(cancel);
            Controls.Add(save);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            int[] values = { 0, 30, 60, 120, 300 };
            var result = new AppPreferences
            {
                IntervalMinutes = values[Math.Max(0, interval.SelectedIndex)],
                OnlyWhenDeviceActive = onlyActive.Checked,
                EnableExternalPlugins = loadPlugins.Checked,
                PinPanel = pinned.Checked
            };
            if (!ResultSettings.EnableExternalPlugins && result.EnableExternalPlugins)
            {
                DialogResult confirmed = MessageBox.Show(
                    "第三方 DLL 插件会在程序内执行代码。\n请只加载自己编写或已审核的插件。\n确认允许加载吗？",
                    "外部插件安全提醒", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirmed != DialogResult.Yes) return;
            }
            ResultSettings = result;
            ResultStartup = startup.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private CheckBox NewCheck(string caption, int x, int y, int width, bool check)
        {
            var box = new CheckBox
            {
                Text = caption, Location = new Point(x, y), Size = new Size(width, 29),
                Checked = check, Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = UiTheme.Text, BackColor = UiTheme.Background,
                FlatStyle = FlatStyle.Flat
            };
            Controls.Add(box);
            return box;
        }

        private void LabelAt(string text, int x, int y, int width, int height, float size,
            bool bold, Color color)
        {
            Controls.Add(new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(width, height),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = color,
                BackColor = UiTheme.Background,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular)
            });
        }
    }
}
