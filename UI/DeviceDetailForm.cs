using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PeripheralBatteryLite
{
    internal sealed class DeviceDetailForm : Form
    {
        private readonly string id;
        private readonly BatteryReading reading;
        private readonly BatteryHistoryStore history;
        private readonly BatteryAlertStore alerts;
        private readonly AppPreferences initial;
        private readonly TextBox alias;
        private readonly CheckBox notify;
        private readonly NumericUpDown threshold;
        private readonly NumericUpDown capacity;
        private readonly ComboBox autoMode;
        private readonly ComboBox range;
        private readonly BatteryHistoryChart chart;
        private readonly Label insightLabel;
        private readonly Label capacityLabel;
        private readonly Label rateLabel;
        private readonly Label voltageLabel;
        private readonly ListView notificationHistory;
        private readonly SoftButton save;

        public AppPreferences ResultSettings { get; private set; }

        public DeviceDetailForm(string deviceId, string displayName, BatteryReading snapshot,
            AppPreferences current, BatteryHistoryStore historyStore, BatteryAlertStore alertStore)
        {
            id = deviceId;
            reading = snapshot;
            history = historyStore;
            alerts = alertStore;
            initial = current.Copy();
            ResultSettings = current.Copy();
            DevicePreferences config = initial.GetDevice(id);

            Text = "设备详情 · " + displayName;
            ClientSize = new Size(680, 640);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            Font = new Font("Microsoft YaHei UI", 9f);

            LabelAt(this, displayName, 21, 16, 450, 34, 16f, true, UiTheme.Text);
            string pct = snapshot != null && snapshot.Percent.HasValue ?
                snapshot.Percent.Value + "%" : "--";
            LabelAt(this, pct, 541, 18, 118, 44, 23f, true,
                snapshot != null && snapshot.Percent.HasValue &&
                snapshot.Percent.Value <= config.AlertThreshold ?
                UiTheme.Warning : UiTheme.Accent, ContentAlignment.MiddleRight);
            string status = snapshot == null || !snapshot.LastSuccessUtc.HasValue ?
                "尚无可靠电量记录" :
                "上次成功：" + snapshot.LastSuccessUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") +
                (snapshot.Status == "Previous query failed" ? "  ·  最近查询失败，沿用历史值" : "") +
                (snapshot.Charging == true ? "  ·  充电中" : "");
            LabelAt(this, status, 23, 66, 626, 24, 8.6f, false,
                snapshot != null && snapshot.Status == "Previous query failed" ?
                UiTheme.Warning : UiTheme.Secondary);

            var tabs = new TabControl {
                Location = new Point(19, 104), Size = new Size(641, 454),
                Font = new Font("Microsoft YaHei UI", 9.5f)
            };
            var trendPage = Page("趋势与容量");
            var preferencesPage = Page("设备设置");
            var logPage = Page("提醒记录");
            tabs.TabPages.Add(trendPage);
            tabs.TabPages.Add(preferencesPage);
            tabs.TabPages.Add(logPage);
            Controls.Add(tabs);

            LabelAt(trendPage, "耗电历史", 14, 16, 310, 26, 10.6f, true, UiTheme.Text);
            range = Choice(trendPage, 474, 15, 126, "最近 7 天", "最近 30 天",
                "最近 90 天", "最近 180 天");
            range.SelectedIndex = 1;
            chart = new BatteryHistoryChart {
                Location = new Point(15, 56), Size = new Size(600, 220),
                Threshold = config.AlertThreshold
            };
            trendPage.Controls.Add(chart);
            insightLabel = LabelAt(trendPage, "耗电速度：暂无可靠预测", 18, 296, 588, 28,
                9.2f, false, UiTheme.Text);
            capacityLabel = LabelAt(trendPage, "剩余容量：未提供标称容量", 18, 326, 589, 28,
                9.2f, false, UiTheme.Text);
            rateLabel = LabelAt(trendPage, "平均等效电流：暂无可靠估算", 18, 355, 590, 24,
                9.2f, false, UiTheme.Text);
            voltageLabel = LabelAt(trendPage, "电池电压：未提供设备数据",
                18, 379, 590, 23, 9f, false, UiTheme.Text);
            LabelAt(trendPage, "mAh/mA 是估算，电压仅在设备上报并校验通过后显示。",
                18, 405, 594, 18, 8f, false, UiTheme.Secondary);
            range.SelectedIndexChanged += (s, e) => RefreshTrend();
            var export = new SoftButton(false) {
                Text = "导出历史 CSV", Location = new Point(341, 292), Size = new Size(142, 30)
            };
            export.Location = new Point(321, 14);
            export.Click += (s, e) => ExportHistory();
            trendPage.Controls.Add(export);
            RefreshTrend();

            LabelAt(preferencesPage, "设备别名", 18, 18, 170, 25, 10f, true, UiTheme.Text);
            alias = new TextBox {
                Location = new Point(18, 49), Size = new Size(585, 31),
                Text = config.Alias, MaxLength = 30,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            preferencesPage.Controls.Add(alias);
            notify = NewCheck(preferencesPage, "电量低于阈值时发送 Windows 通知",
                18, 101, 585, config.AlertsEnabled);
            LabelAt(preferencesPage, "低电量阈值", 18, 148, 160, 25, 9.5f, true, UiTheme.Text);
            threshold = new NumericUpDown {
                Location = new Point(190, 146), Size = new Size(101, 31),
                Minimum = 5, Maximum = 95, Increment = 5, Value = config.AlertThreshold,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text
            };
            preferencesPage.Controls.Add(threshold);
            LabelAt(preferencesPage, "%  ·  恢复至阈值 +5% 后允许下次提醒",
                308, 148, 298, 25, 8.2f, false, UiTheme.Secondary);

            LabelAt(preferencesPage, "后台查询策略", 18, 202, 165, 25, 9.5f, true, UiTheme.Text);
            autoMode = Choice(preferencesPage, 190, 200, 413,
                "继承全局省电策略", "仅设备活跃时", "每次到期尝试查询", "仅手动查询");
            string[] modes = { "inherit", "active", "always", "manual" };
            int idx = Array.IndexOf(modes, config.AutoQueryMode);
            autoMode.SelectedIndex = idx < 0 ? 0 : idx;

            LabelAt(preferencesPage, "标称电池容量（mAh）", 18, 255, 180, 25,
                9.5f, true, UiTheme.Text);
            capacity = new NumericUpDown {
                Location = new Point(223, 253), Size = new Size(126, 31),
                Minimum = 0, Maximum = 100000, Increment = 50,
                Value = config.RatedCapacityMah ?? 0,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text
            };
            preferencesPage.Controls.Add(capacity);
            LabelAt(preferencesPage, "0 = 未知，不估算 mAh/mA", 366, 256, 238, 24,
                8.4f, false, UiTheme.Secondary);
            string capacityHint = id == BatterySnapshotStore.Mouse ?
                "F1 V3 型号版本不同：Ultimate+ 300mAh / Extreme+ 200mAh。" :
                id == BatterySnapshotStore.Keyboard ?
                "V87 常见版本 4000mAh / Pro 8000mAh，请以实物为准。" :
                "Nova 5 官方公布最长 60 小时续航；未确认容量 mAh。";
            LabelAt(preferencesPage, capacityHint,
                18, 293, 586, 23, 8.2f, false, UiTheme.Secondary);
            var snooze = new SoftButton(false) {
                Text = "本设备静音 24 小时", Location = new Point(18, 345), Size = new Size(200, 34)
            };
            snooze.Click += (s, e) =>
            {
                alerts.Snooze(id, DateTime.UtcNow.AddHours(24));
                MessageBox.Show("已暂停该设备低电量提醒 24 小时。", "提醒已静音",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            preferencesPage.Controls.Add(snooze);
            LabelAt(preferencesPage, "不会影响手动刷新；定时查询不会在电脑休眠时运行。",
                235, 347, 367, 33, 8f, false, UiTheme.Secondary);

            LabelAt(logPage, "最近发送的低电量提醒", 17, 15, 520, 28,
                10.5f, true, UiTheme.Text);
            notificationHistory = new ListView {
                Location = new Point(16, 57), Size = new Size(597, 335),
                View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text,
                BorderStyle = BorderStyle.None
            };
            notificationHistory.Columns.Add("时间", 190);
            notificationHistory.Columns.Add("电量", 105);
            notificationHistory.Columns.Add("类型", 260);
            foreach (var entry in alerts.RecentNotifications(id, 100))
            {
                var row = new ListViewItem(entry.When.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                row.SubItems.Add(entry.Percent + "%");
                row.SubItems.Add("低电量提醒");
                notificationHistory.Items.Add(row);
            }
            logPage.Controls.Add(notificationHistory);

            var cancel = new SoftButton(false) {
                Text = "关闭", Location = new Point(454, 577), Size = new Size(92, 39)
            };
            cancel.Click += (s, e) => Close();
            save = new SoftButton(true) {
                Text = "保存设备设置", Location = new Point(553, 577), Size = new Size(107, 39)
            };
            save.Click += (s, e) => SaveSettings();
            Controls.Add(cancel);
            Controls.Add(save);
            LabelAt(this, "每台设备可设置独立阈值与轮询策略", 20, 582, 415, 27,
                8.3f, false, UiTheme.Secondary);
        }

        private void RefreshTrend()
        {
            if (chart == null || range == null) return;
            int[] days = { 7, 30, 90, 180 };
            int selected = days[Math.Max(0, range.SelectedIndex)];
            chart.Data = history.Recent(id, selected);
            chart.Threshold = initial.GetDevice(id).AlertThreshold;
            chart.Invalidate();
            var analysis = BatteryInsights.Calculate(history.Recent(id, 30),
                reading == null ? null : reading.Percent,
                initial.GetDevice(id).AlertThreshold,
                initial.GetDevice(id).RatedCapacityMah);
            insightLabel.Text = analysis.DailyPercent.HasValue ?
                "平均耗电 " + analysis.DailyPercent.Value.ToString("0.0") +
                "%/天  ·  达到提醒阈值 " + BatteryInsights.Duration(analysis.DaysToThreshold) +
                "  ·  耗尽 " + BatteryInsights.Duration(analysis.DaysToEmpty) :
                "耗电速度：暂无可靠预测（需至少三次跨 12 小时的有效下降记录）";
            capacityLabel.Text = analysis.EstimatedRemainingMah.HasValue ?
                "剩余容量（标称估算）：" + analysis.EstimatedRemainingMah.Value.ToString("0") + " mAh" :
                "剩余容量：需要在设备设置输入标称电池容量";
            voltageLabel.Text = reading != null && reading.VoltageMillivolts.HasValue ?
                "电池电压（设备报文）：" +
                (reading.VoltageMillivolts.Value / 1000.0).ToString("0.000") +
                " V  ·  " + reading.VoltageMillivolts.Value + " mV" :
                "电池电压：设备未提供或尚未验证";
            rateLabel.Text = analysis.EstimatedAverageMa.HasValue ?
                "平均等效耗电（标称估算）：" + analysis.EstimatedAverageMa.Value.ToString("0.00") +
                " mA  ·  不是瞬时测量" : "平均等效电流：暂无可靠估算";
        }

        private void SaveSettings()
        {
            var config = ResultSettings.GetDevice(id);
            config.Alias = alias.Text.Trim();
            config.AlertsEnabled = notify.Checked;
            config.AlertThreshold = (int)threshold.Value;
            config.RatedCapacityMah = capacity.Value > 0 ? (int?)capacity.Value : null;
            string[] modes = { "inherit", "active", "always", "manual" };
            config.AutoQueryMode = modes[Math.Max(0, autoMode.SelectedIndex)];
            ResultSettings.Normalize();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ExportHistory()
        {
            using (var dialog = new SaveFileDialog {
                Filter = "CSV 文件 (*.csv)|*.csv", DefaultExt = "csv",
                FileName = id.Replace(" ", "-") + "-battery-history.csv"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int[] days = { 7, 30, 90, 180 };
                    history.ExportCsv(id, dialog.FileName, days[Math.Max(0, range.SelectedIndex)]);
                    MessageBox.Show("已导出成功。", "CSV 导出", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "导出失败", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private static TabPage Page(string title)
        {
            return new TabPage(title) { BackColor = UiTheme.Background,
                ForeColor = UiTheme.Text, UseVisualStyleBackColor = false };
        }

        private static ComboBox Choice(Control parent, int x, int y, int width, params string[] entries)
        {
            var combo = new ComboBox { Location = new Point(x, y),
                Size = new Size(width, 31), DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = UiTheme.Card, ForeColor = UiTheme.Text, FlatStyle = FlatStyle.Flat };
            combo.Items.AddRange(entries);
            parent.Controls.Add(combo);
            return combo;
        }

        private static CheckBox NewCheck(Control parent, string caption, int x, int y,
            int width, bool value)
        {
            var cb = new CheckBox {
                Text = caption, Location = new Point(x, y), Size = new Size(width, 29),
                Checked = value, BackColor = UiTheme.Background, ForeColor = UiTheme.Text
            };
            parent.Controls.Add(cb);
            return cb;
        }

        private static Label LabelAt(Control parent, string text, int x, int y, int width,
            int height, float fontSize, bool bold, Color tint,
            ContentAlignment align = ContentAlignment.MiddleLeft)
        {
            var label = new Label { Text = text, Location = new Point(x, y),
                Size = new Size(width, height), BackColor = UiTheme.Background,
                ForeColor = tint, TextAlign = align,
                Font = new Font("Microsoft YaHei UI", fontSize,
                    bold ? FontStyle.Bold : FontStyle.Regular) };
            parent.Controls.Add(label);
            return label;
        }
    }

    internal sealed class BatteryHistoryChart : Control
    {
        public List<BatterySample> Data = new List<BatterySample>();
        public int Threshold = 45;

        public BatteryHistoryChart()
        {
            BackColor = UiTheme.Card;
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            var g = args.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.Card);
            Rectangle plot = new Rectangle(43, 12, Width - 56, Height - 39);
            if (plot.Width <= 0 || plot.Height <= 0) return;
            using (var f = new Font("Segoe UI", 8f))
            using (var grid = new Pen(UiTheme.CardBorder, 1))
            using (var fg = new SolidBrush(UiTheme.Secondary))
            {
                for (int pct = 0; pct <= 100; pct += 25)
                {
                    float y = plot.Bottom - pct / 100f * plot.Height;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    g.DrawString(pct + "%", f, fg, 5, y - 7);
                }
                float at = plot.Bottom - Threshold / 100f * plot.Height;
                using (var thresholdPen = new Pen(UiTheme.Warning, 1) {
                    DashStyle = DashStyle.Dash })
                    g.DrawLine(thresholdPen, plot.Left, at, plot.Right, at);
                using (var warning = new SolidBrush(UiTheme.Warning))
                    g.DrawString("阈值 " + Threshold + "%", f,
                        warning, plot.Left + 5, at + 3);

                if (Data == null || Data.Count == 0)
                {
                    using (var labelFont = new Font("Microsoft YaHei UI", 10f))
                        g.DrawString("暂无电量历史 · 下次成功查询后开始记录",
                            labelFont, fg, plot.Left + 42, plot.Top + plot.Height / 2 - 10);
                    return;
                }
                var ordered = Data.OrderBy(s => s.When).ToList();
                DateTime start = ordered.First().When;
                DateTime end = ordered.Last().When;
                double total = Math.Max(1, (end - start).TotalMinutes);
                PointF[] points = ordered.Select(s => new PointF(
                    plot.Left + (float)((s.When - start).TotalMinutes / total * plot.Width),
                    plot.Bottom - s.Percent / 100f * plot.Height)).ToArray();
                using (var line = new Pen(UiTheme.Accent, 2f))
                using (var dot = new SolidBrush(UiTheme.Accent))
                {
                    if (points.Length > 1) g.DrawLines(line, points);
                    foreach (var point in points)
                        g.FillEllipse(dot, point.X - 2.8f, point.Y - 2.8f, 5.6f, 5.6f);
                }
                g.DrawString(start.ToLocalTime().ToString("MM/dd"), f, fg,
                    plot.Left, plot.Bottom + 5);
                string finish = end.ToLocalTime().ToString("MM/dd");
                SizeF size = g.MeasureString(finish, f);
                g.DrawString(finish, f, fg, plot.Right - size.Width, plot.Bottom + 5);
            }
        }
    }
}
