using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PeripheralBatteryLite
{
    // A custom-drawn, small native Windows panel. No browser or webview.
    internal sealed class BatteryPanel : Form
    {
        private const int PanelWidth = 408;
        private List<BatteryReading> readings = new List<BatteryReading>();
        private List<IBatteryProvider> providers = new List<IBatteryProvider>();
        private AppPreferences prefs = new AppPreferences();
        private string footerNote = "";
        public bool Pinned;
        public SoftButton RefreshButton;
        public SoftButton SettingsButton;
        public event EventHandler RefreshRequested;
        public event EventHandler SettingsRequested;
        public event Action<int, IntPtr> RawInputReceived;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);

        public BatteryPanel()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.Text;
            DoubleBuffered = true;
            ClientSize = new Size(PanelWidth, 424);
            RefreshButton = new SoftButton(true)
            {
                Text = "刷新电量", Location = new Point(278, 378), Size = new Size(110, 34)
            };
            RefreshButton.Click += (s, e) => { if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); };
            SettingsButton = new SoftButton(false)
            {
                Text = "设置", Location = new Point(16, 378), Size = new Size(82, 34)
            };
            SettingsButton.Click += (s, e) => { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            Controls.Add(RefreshButton);
            Controls.Add(SettingsButton);
            MouseDown += OnDrag;
            // Rounded native window; works without DWM/Win11-specific APIs.
            SetRoundedRegion();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ClassStyle |= 0x00020000; // CS_DROPSHADOW (no extra compositing process)
                return p;
            }
        }

        private void SetRoundedRegion()
        {
            using (var path = UiTheme.Rounded(new Rectangle(0, 0, Width, Height), 15))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        public void UpdateData(List<BatteryReading> snapshot, List<IBatteryProvider> registry,
            AppPreferences settings)
        {
            readings = snapshot ?? new List<BatteryReading>();
            providers = registry ?? new List<IBatteryProvider>();
            prefs = settings ?? new AppPreferences();
            Pinned = prefs.PinPanel;
            int count = Math.Max(3, Math.Min(5, providers.Count));
            int height = 130 + count * 88 + 30;
            if (ClientSize.Height != height)
            {
                ClientSize = new Size(PanelWidth, height);
                RefreshButton.Location = new Point(278, height - 46);
                SettingsButton.Location = new Point(16, height - 46);
                SetRoundedRegion();
            }
            Invalidate();
        }

        public void SetLoading(bool active)
        {
            RefreshButton.Enabled = !active;
            RefreshButton.Text = active ? "正在查询..." : "刷新电量";
            Invalidate();
        }

        public void SetFooter(string message)
        {
            footerNote = message ?? "";
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(UiTheme.Background);
            UiTheme.BorderRound(g, UiTheme.CardBorder, new Rectangle(0, 0, Width - 1, Height - 1), 15);

            UiTheme.FillRound(g, UiTheme.Accent, new Rectangle(18, 24, 5, 32), 2);
            UiTheme.TextAt(g, "Peripheral Battery", 15f, true, UiTheme.Text,
                new RectangleF(34, 18, 265, 28), ContentAlignment.MiddleLeft);
            UiTheme.TextAt(g, "外设电量   /   上次记录", 8.3f, false, UiTheme.Secondary,
                new RectangleF(35, 49, 255, 20), ContentAlignment.MiddleLeft);

            string badge = prefs.IntervalMinutes == 0 ? "手动刷新" :
                (prefs.IntervalMinutes == 30 ? "30 分钟" :
                (prefs.IntervalMinutes == 60 ? "1 小时" :
                (prefs.IntervalMinutes == 120 ? "2 小时" : "5 小时")));
            UiTheme.FillRound(g, Color.FromArgb(38, 55, 71),
                new Rectangle(299, 26, 90, 25), 11);
            UiTheme.TextAt(g, badge, 8.4f, true, UiTheme.Accent,
                new RectangleF(300, 25, 88, 27), ContentAlignment.MiddleCenter);

            var visible = providers.Take(5).ToList();
            for (int i = 0; i < Math.Max(3, visible.Count); ++i)
            {
                IBatteryProvider p = i < visible.Count ? visible[i] : null;
                BatteryReading r = p == null ? null :
                    readings.FirstOrDefault(x => x.Name == p.Id);
                DrawDevice(g, new Rectangle(16, 83 + i * 88, Width - 32, 80), p, r, i);
            }
            string footer = !string.IsNullOrEmpty(footerNote) ? footerNote :
                prefs.IntervalMinutes == 0 ? "不会后台查询设备" :
                (prefs.OnlyWhenDeviceActive ? "自动刷新 · 仅限活跃设备" : "自动刷新已启用");
            UiTheme.TextAt(g, footer, 7.6f, false, UiTheme.Secondary,
                new RectangleF(110, Height - 45, 158, 29), ContentAlignment.MiddleLeft);
        }

        private static void DrawDevice(Graphics g, Rectangle area,
            IBatteryProvider provider, BatteryReading reading, int index)
        {
            UiTheme.FillRound(g, UiTheme.Card, area, 11);
            UiTheme.BorderRound(g, UiTheme.CardBorder, area, 11);
            string title = provider == null ? "未连接设备" : provider.DisplayName;
            string symbol = provider == null ? "?" :
                provider.Activity == ActivityCategory.Mouse ? "M" :
                provider.Activity == ActivityCategory.Keyboard ? "K" :
                provider.Activity == ActivityCategory.Audio ? "H" : "D";
            Color symbolTint = index == 0 ? UiTheme.Accent :
                index == 1 ? UiTheme.Good : Color.FromArgb(170, 168, 255);
            UiTheme.FillRound(g, Color.FromArgb(36, 49, 62),
                new Rectangle(area.X + 11, area.Y + 11, 31, 31), 8);
            UiTheme.TextAt(g, symbol, 9.5f, true, symbolTint,
                new RectangleF(area.X + 11, area.Y + 12, 31, 30), ContentAlignment.MiddleCenter);
            UiTheme.TextAt(g, title, 9.5f, true, UiTheme.Text,
                new RectangleF(area.X + 52, area.Y + 8, area.Width - 170, 26),
                ContentAlignment.MiddleLeft);
            bool valid = reading != null && reading.Percent.HasValue;
            int percent = valid ? reading.Percent.Value : 0;
            string level = valid ? percent.ToString() + "%" : "--";
            Color levelColor = !valid ? UiTheme.Secondary : percent <= 20 ?
                UiTheme.Warning : UiTheme.Accent;
            UiTheme.TextAt(g, level, 16f, true, levelColor,
                new RectangleF(area.Right - 113, area.Y + 8, 97, 33),
                ContentAlignment.MiddleRight);
            // Success and failure timestamps are independent. A failed read
            // must never hide the timestamp behind the cached percentage.
            string[] lines = DescribeReading(reading);
            UiTheme.TextAt(g, lines[0], 7.5f, false, UiTheme.Secondary,
                new RectangleF(area.X + 52, area.Y + 32, area.Width - 71, 16),
                ContentAlignment.MiddleLeft);
            UiTheme.TextAt(g, lines[1], 7.5f, false,
                reading != null && reading.Status == "Previous query failed"
                    ? UiTheme.Warning : UiTheme.Secondary,
                new RectangleF(area.X + 52, area.Y + 48, area.Width - 71, 16),
                ContentAlignment.MiddleLeft);

            var track = new Rectangle(area.X + 13, area.Bottom - 13, area.Width - 26, 5);
            UiTheme.FillRound(g, UiTheme.Track, track, 2);
            if (valid && percent > 0)
            {
                var fill = new Rectangle(track.X, track.Y,
                    Math.Max(3, track.Width * percent / 100), track.Height);
                UiTheme.FillRound(g, levelColor, fill, 2);
            }
        }

        // Pure formatter makes UI stale/failure behavior testable without a
        // display, hardware, or touching the user's battery cache.
        internal static string[] DescribeReading(BatteryReading reading)
        {
            if (reading == null || !reading.LastQueryUtc.HasValue)
                return new[] { "尚未查询 · 点击刷新", "本地缓存 · 不触发设备查询" };

            string attempted = reading.LastQueryUtc.Value.ToLocalTime().ToString("MM-dd HH:mm");
            if (reading.Status == "Previous query failed")
            {
                if (reading.LastSuccessUtc.HasValue)
                    return new[] {
                        "上次成功 " + reading.LastSuccessUtc.Value.ToLocalTime().ToString("MM-dd HH:mm"),
                        "最近失败 " + attempted + " · 沿用旧值"
                    };
                return new[] { "最近查询 " + attempted, "查询失败 · 暂无有效电量" };
            }

            if (reading.LastSuccessUtc.HasValue)
                return new[] {
                    "上次成功 " + reading.LastSuccessUtc.Value.ToLocalTime().ToString("MM-dd HH:mm"),
                    reading.Charging == true ? "充电中 · 已缓存" : "缓存电量 · 非实时值"
                };
            return new[] { "最近查询 " + attempted, "尚无有效电量" };
        }

        internal static bool SelfTest()
        {
            DateTime success = new DateTime(2026, 10, 24, 11, 0, 0, DateTimeKind.Utc);
            DateTime failed = success.AddHours(2);
            string expectedSuccess = success.ToLocalTime().ToString("MM-dd HH:mm");
            string expectedFailure = failed.ToLocalTime().ToString("MM-dd HH:mm");
            var oldReading = new BatteryReading {
                Percent = 85, LastSuccessUtc = success, LastQueryUtc = failed,
                Status = "Previous query failed"
            };
            string[] stale = DescribeReading(oldReading);
            string[] fresh = DescribeReading(new BatteryReading {
                Percent = 85, LastSuccessUtc = success, LastQueryUtc = success,
                Status = "Cached"
            });
            string[] empty = DescribeReading(null);
            return stale[0].Contains(expectedSuccess) &&
                   stale[1].Contains(expectedFailure) &&
                   stale[1].Contains("失败") &&
                   fresh[0].Contains(expectedSuccess) &&
                   fresh[1].Contains("缓存") &&
                   empty[0].Contains("尚未查询");
        }

        private void OnDrag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x00FF && RawInputReceived != null)
                RawInputReceived(m.Msg, m.LParam);
            base.WndProc(ref m);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!Pinned) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
