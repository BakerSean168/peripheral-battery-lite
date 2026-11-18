using System;
using System.Drawing;
using System.Windows.Forms;

namespace PeripheralBatteryLite
{
    internal sealed class SettingsForm : Form
    {
        private readonly ComboBox interval;
        private readonly CheckBox onlyActive;
        private readonly CheckBox quiet;
        private readonly ComboBox quietStart;
        private readonly ComboBox quietEnd;
        private readonly CheckBox startup;
        private readonly CheckBox pinned;
        private readonly CheckBox loadPlugins;
        private readonly ComboBox trayMode;
        private readonly ComboBox retention;
        private readonly SoftButton save;
        private readonly SoftButton cancel;
        private static readonly int[] Intervals = { 0, 30, 60, 120, 240, 300 };
        private static readonly int[] Retentions = { 30, 90, 180, 365 };

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
            ClientSize = new Size(520, 585);

            LabelAt(this, "偏好设置", 21, 16, 450, 32, 16f, true, UiTheme.Text);
            LabelAt(this, "可控后台查询 · 按设备提醒 · 本地数据", 22, 51, 465, 19,
                8.8f, false, UiTheme.Secondary);

            var tabs = new TabControl {
                Location = new Point(19, 90), Size = new Size(482, 420),
                Font = new Font("Microsoft YaHei UI", 9.3f)
            };
            var polling = Page("自动查询 / 通知");
            var display = Page("托盘 / 数据");
            tabs.TabPages.Add(polling);
            tabs.TabPages.Add(display);
            Controls.Add(tabs);

            LabelAt(polling, "后台自动查询间隔", 18, 18, 390, 22, 10f, true, UiTheme.Text);
            interval = Choice(polling, 18, 49, 426,
                "关闭 · 仅手动刷新", "每 30 分钟", "每 1 小时", "每 2 小时",
                "每 4 小时", "每 5 小时");
            interval.SelectedIndex = Math.Max(0, Array.IndexOf(Intervals, current.IntervalMinutes));

            onlyActive = NewCheck(polling, "默认仅查询近期活跃的设备（更省电）",
                18, 95, 427, current.OnlyWhenDeviceActive);
            LabelAt(polling, "启用后不主动唤醒未活动的鼠标/键盘；耳机将被跳过。",
                37, 125, 410, 19, 8f, false, UiTheme.Secondary);
            LabelAt(polling, "每台设备可在详情页覆盖此规则，包括允许耳机定时查询。",
                37, 146, 410, 20, 8f, false, UiTheme.Secondary);

            quiet = NewCheck(polling, "免打扰时段内不弹出低电量通知",
                18, 190, 426, current.QuietHoursEnabled);
            LabelAt(polling, "开始时间", 18, 228, 90, 23, 9f, false, UiTheme.Text);
            quietStart = HourChoice(polling, 115, 225, current.QuietStartHour);
            LabelAt(polling, "结束时间", 241, 228, 90, 23, 9f, false, UiTheme.Text);
            quietEnd = HourChoice(polling, 330, 225, current.QuietEndHour);
            quiet.CheckedChanged += (s, e) => {
                quietStart.Enabled = quiet.Checked;
                quietEnd.Enabled = quiet.Checked;
            };
            quietStart.Enabled = quiet.Checked;
            quietEnd.Enabled = quiet.Checked;
            startup = NewCheck(polling, "随 Windows 登录自动启动（通知需要程序运行）",
                18, 286, 430, startsWithWindows);
            LabelAt(polling, "通知阈值、别名、单设备查询模式请在设备详情中设置。",
                18, 333, 438, 24, 8.3f, false, UiTheme.Secondary);

            LabelAt(display, "托盘图标显示", 18, 18, 425, 22, 10f, true, UiTheme.Text);
            trayMode = Choice(display, 18, 49, 426, "鼠标 · ATK F1V3", "键盘 · VXE V87",
                "耳机 · Nova 5", "电量最低设备（仅最近 24 小时成功值）");
            string[] trayIds = { BatterySnapshotStore.Mouse, BatterySnapshotStore.Keyboard,
                BatterySnapshotStore.Headset, "lowest" };
            int index = Array.IndexOf(trayIds, current.TrayIconDeviceId);
            trayMode.SelectedIndex = index < 0 ? 0 : index;

            LabelAt(display, "历史记录保存时长", 18, 103, 390, 22, 10f, true, UiTheme.Text);
            retention = Choice(display, 18, 133, 426,
                "30 天", "90 天", "180 天", "365 天");
            retention.SelectedIndex = Math.Max(0, Array.IndexOf(Retentions, current.HistoryRetentionDays));
            LabelAt(display, "仅保存在本机，统计为估算，历史可按设备导出 CSV。",
                18, 171, 435, 21, 8f, false, UiTheme.Secondary);

            pinned = NewCheck(display, "默认固定电量悬浮窗", 18, 213, 430, current.PinPanel);
            loadPlugins = NewCheck(display, "允许加载 Plugins 目录内的第三方 DLL",
                18, 260, 430, current.EnableExternalPlugins);
            LabelAt(display, "插件以当前用户权限执行，仅启用可信来源。",
                37, 294, 417, 18, 8f, false, UiTheme.Warning);
            LabelAt(display, "提示：mAh 与平均 mA 仅在输入电池标称容量后显示估算值。",
                18, 342, 434, 25, 8f, false, UiTheme.Secondary);

            string providerState = "已内置 " + providerCount + " 个协议提供器";
            if (!hasRawInput && current.IntervalMinutes > 0 && current.OnlyWhenDeviceActive)
                providerState += " · Raw Input 当前未注册";
            LabelAt(this, providerState, 20, 516, 310, 20, 8.2f, false, UiTheme.Secondary);
            cancel = new SoftButton(false) { Text = "取消", Location = new Point(323, 535), Size = new Size(80, 36) };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            save = new SoftButton(true) { Text = "保存设置", Location = new Point(412, 535), Size = new Size(88, 36) };
            save.Click += SaveClicked;
            Controls.Add(cancel);
            Controls.Add(save);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static TabPage Page(string title)
        {
            return new TabPage(title) { BackColor = UiTheme.Background, ForeColor = UiTheme.Text,
                Padding = new Padding(4), UseVisualStyleBackColor = false };
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            var result = ResultSettings.Copy();
            result.IntervalMinutes = Intervals[Math.Max(0, interval.SelectedIndex)];
            result.OnlyWhenDeviceActive = onlyActive.Checked;
            result.QuietHoursEnabled = quiet.Checked;
            result.QuietStartHour = quietStart.SelectedIndex;
            result.QuietEndHour = quietEnd.SelectedIndex;
            result.PinPanel = pinned.Checked;
            result.EnableExternalPlugins = loadPlugins.Checked;
            result.HistoryRetentionDays = Retentions[Math.Max(0, retention.SelectedIndex)];
            string[] trayIds = { BatterySnapshotStore.Mouse, BatterySnapshotStore.Keyboard,
                BatterySnapshotStore.Headset, "lowest" };
            result.TrayIconDeviceId = trayIds[Math.Max(0, trayMode.SelectedIndex)];
            if (!ResultSettings.EnableExternalPlugins && result.EnableExternalPlugins)
            {
                if (MessageBox.Show(
                    "第三方 DLL 插件会在程序内执行代码。\n请只加载自己编写或已审核的插件。\n确认允许加载吗？",
                    "外部插件安全提醒", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                    != DialogResult.Yes) return;
            }
            ResultSettings = result;
            ResultStartup = startup.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static ComboBox Choice(Control parent, int x, int y, int width, params string[] values)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(x, y), Size = new Size(width, 31),
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9.3f) };
            combo.Items.AddRange(values);
            parent.Controls.Add(combo);
            return combo;
        }

        private static ComboBox HourChoice(Control parent, int x, int y, int selected)
        {
            var c = Choice(parent, x, y, 106);
            for (int i = 0; i < 24; i++) c.Items.Add(i.ToString("00") + ":00");
            c.SelectedIndex = Math.Max(0, Math.Min(23, selected));
            return c;
        }

        private static CheckBox NewCheck(Control parent, string caption, int x, int y,
            int width, bool value)
        {
            var box = new CheckBox { Text = caption, Location = new Point(x, y),
                Size = new Size(width, 29), Checked = value,
                Font = new Font("Microsoft YaHei UI", 9f),
                ForeColor = UiTheme.Text, BackColor = UiTheme.Background,
                FlatStyle = FlatStyle.Flat };
            parent.Controls.Add(box);
            return box;
        }

        private static void LabelAt(Control parent, string text, int x, int y, int width,
            int height, float size, bool bold, Color color)
        {
            parent.Controls.Add(new Label {
                Text = text, Location = new Point(x, y), Size = new Size(width, height),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = color,
                BackColor = UiTheme.Background,
                Font = new Font("Microsoft YaHei UI", size,
                    bold ? FontStyle.Bold : FontStyle.Regular)
            });
        }
    }
}
