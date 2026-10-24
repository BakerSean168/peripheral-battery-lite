using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PeripheralBatteryLite
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0)
                {
                    if (args[0] == "--self-test")
                    {
                        bool ok = BatteryProtocols.SelfTest();
                        Console.WriteLine(ok ? "PASS: mouse, keyboard, SteelSeries and malformed-frame tests" : "FAIL");
                        Environment.ExitCode = ok ? 0 : 1;
                        return;
                    }
                    if (args[0] == "--cache-self-test")
                    {
                        bool ok = BatterySnapshotStore.SelfTest();
                        Console.WriteLine(ok ? "PASS: persistent cache, timestamps, failure-retention and corrupt-cache tests" : "FAIL");
                        Environment.ExitCode = ok ? 0 : 1;
                        return;
                    }
                    if (args[0] == "--cache-show")
                    {
                        var store = new BatterySnapshotStore();
                        foreach (var reading in store.Snapshot())
                        {
                            Console.WriteLine(reading.Json + " last_success_utc=" +
                                (reading.LastSuccessUtc.HasValue ? reading.LastSuccessUtc.Value.ToString("o") : "null") +
                                " last_attempt_utc=" +
                                (reading.LastQueryUtc.HasValue ? reading.LastQueryUtc.Value.ToString("o") : "null") +
                                " cache_status=" + reading.Status);
                        }
                        Console.WriteLine("CACHE_PATH=" + store.FilePath);
                        return;
                    }
                    if (args[0] == "--refresh-cache")
                    {
                        var store = new BatterySnapshotStore();
                        store.MergeAndSave(BatteryProtocols.ReadAll());
                        foreach (var reading in store.Snapshot())
                            Console.WriteLine(reading.Json + " last_success_utc=" +
                                (reading.LastSuccessUtc.HasValue ? reading.LastSuccessUtc.Value.ToString("o") : "null") +
                                " cache_status=" + reading.Status);
                        if (store.LastSaveError != null) Console.WriteLine("SAVE_ERROR=" + store.LastSaveError);
                        Environment.ExitCode = store.LastSaveError == null ? 0 : 2;
                        return;
                    }
                    if (args[0] == "--probe")
                    {
                        Console.WriteLine(BatteryProtocols.Probe());
                        return;
                    }
                    if (args[0] == "--mouse-only")
                    {
                        var mouse = BatteryProtocols.ReadMouseOnly();
                        Console.WriteLine(mouse.Json);
                        if (mouse.Status != "OK") Console.WriteLine("diagnostic: " + mouse.Detail);
                        Environment.ExitCode = mouse.Percent.HasValue ? 0 : 2;
                        return;
                    }
                    if (args[0] == "--keyboard-only")
                    {
                        var keyboard = V87KeyboardBattery.Read(HidWindows.Enumerate());
                        Console.WriteLine(keyboard.Json);
                        if (keyboard.Status != "OK") Console.WriteLine("diagnostic: " + keyboard.Detail);
                        Environment.ExitCode = keyboard.Percent.HasValue ? 0 : 2;
                        return;
                    }
                    if (args[0] == "--headset-only")
                    {
                        var headset = SteelSeriesBattery.Read(HidWindows.Enumerate());
                        Console.WriteLine(headset.Json);
                        if (headset.Status != "OK") Console.WriteLine("diagnostic: " + headset.Detail);
                        Environment.ExitCode = headset.Percent.HasValue ? 0 : 2;
                        return;
                    }
                    if (args[0] == "--once")
                    {
                        var readings = BatteryProtocols.ReadAll();
                        foreach (var r in readings)
                        {
                            Console.WriteLine(r.Json);
                            if (r.Status != "OK") Console.WriteLine("diagnostic: " + r.Name + ": " + r.Detail);
                        }
                        Environment.ExitCode = readings.TrueForAll(x => x.Percent.HasValue) ? 0 : 2;
                        return;
                    }
                }
                bool owns;
                using (var mutex = new Mutex(true, @"Local\PeripheralBatteryLite-9E1C819F", out owns))
                {
                    if (!owns) return;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var app = new BatteryTrayApp()) Application.Run(app);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
                Environment.ExitCode = 3;
            }
        }
    }

    internal sealed class BatteryPanel : Form
    {
        public Label MouseValue, KeyboardValue, HeadsetValue, MouseHint, KeyboardHint, HeadsetHint, Footer;
        public Button RefreshButton;
        public event EventHandler RefreshRequested;
        public bool Pinned;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);

        public BatteryPanel()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(328, 248);
            BackColor = Color.FromArgb(24, 28, 37);
            ForeColor = Color.FromArgb(237, 241, 247);
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Microsoft YaHei UI", 9f);
            Padding = new Padding(12);
            var title = LabelAt("外设电量  ·  Peripheral Battery Lite", 17, 11, 300, 25, 10f, Color.White, true);
            var rule = new Panel { Location = new Point(14, 40), Size = new Size(300, 1), BackColor = Color.FromArgb(65, 72, 86) };
            Controls.Add(rule);
            LabelAt("ATK F1V3", 18, 50, 200, 19, 9f, Color.White, true);
            MouseValue = LabelAt("--", 232, 51, 77, 26, 15f, Color.FromArgb(112, 211, 244), true);
            MouseValue.TextAlign = ContentAlignment.MiddleRight;
            MouseHint = LabelAt("查询中...", 18, 70, 280, 17, 8f, Color.Silver, false);
            var rule2 = new Panel { Location = new Point(14, 100), Size = new Size(300, 1), BackColor = Color.FromArgb(65, 72, 86) };
            Controls.Add(rule2);
            LabelAt("VXE V87", 18, 110, 200, 19, 9f, Color.White, true);
            KeyboardValue = LabelAt("--", 232, 109, 77, 26, 15f, Color.FromArgb(112, 211, 244), true);
            KeyboardValue.TextAlign = ContentAlignment.MiddleRight;
            KeyboardHint = LabelAt("查询中...", 18, 130, 280, 17, 8f, Color.Silver, false);
            var rule3 = new Panel { Location = new Point(14, 158), Size = new Size(300, 1), BackColor = Color.FromArgb(65, 72, 86) };
            Controls.Add(rule3);
            LabelAt("SteelSeries Nova 5", 18, 169, 212, 19, 9f, Color.White, true);
            HeadsetValue = LabelAt("--", 232, 168, 77, 26, 15f, Color.FromArgb(112, 211, 244), true);
            HeadsetValue.TextAlign = ContentAlignment.MiddleRight;
            HeadsetHint = LabelAt("查询中...", 18, 190, 280, 17, 8f, Color.Silver, false);
            Footer = LabelAt("仅手动刷新 · 本地缓存", 18, 222, 224, 18, 7.7f,
                Color.FromArgb(149, 160, 177), false);
            RefreshButton = new Button
            {
                Text = "刷新", Location = new Point(248, 216), Size = new Size(66, 26),
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
                BackColor = Color.FromArgb(50, 92, 123),
                Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            RefreshButton.FlatAppearance.BorderSize = 0;
            RefreshButton.Click += (s, e) => { if (RefreshRequested != null) RefreshRequested(this, EventArgs.Empty); };
            Controls.Add(RefreshButton);
            foreach (Control control in Controls)
                if (!(control is Panel) && !(control is Button)) control.MouseDown += Drag;
            MouseDown += Drag;
        }

        private Label LabelAt(string text, int x, int y, int width, int height, float fontSize, Color color, bool bold)
        {
            var label = new Label
            {
                AutoSize = false, Location = new Point(x, y), Size = new Size(width, height),
                Text = text, ForeColor = color, BackColor = Color.Transparent,
                Font = new Font("Microsoft YaHei UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular)
            };
            Controls.Add(label);
            return label;
        }

        private void Drag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
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

        public void SetReadings(List<BatteryReading> data)
        {
            if (data.Count < 3) return;
            UpdateRow(MouseValue, MouseHint, data[0]);
            UpdateRow(KeyboardValue, KeyboardHint, data[1]);
            UpdateRow(HeadsetValue, HeadsetHint, data[2]);
            Footer.Text = "仅手动刷新 · 本地缓存";
        }

        private static void UpdateRow(Label value, Label hint, BatteryReading r)
        {
            value.Text = r.Display;
            value.ForeColor = !r.Percent.HasValue ? Color.FromArgb(165, 174, 187)
                : r.Percent.Value <= 20 ? Color.FromArgb(255, 139, 124) : Color.FromArgb(112, 211, 244);
            if (!r.LastQueryUtc.HasValue)
                hint.Text = "尚未查询 · 点击刷新";
            else if (!r.Percent.HasValue)
                hint.Text = "查询失败 · " + FormatLocalTime(r.LastQueryUtc.Value);
            else if (r.Status == "Previous query failed")
                hint.Text = "本次失败 · 上次成功 " + FormatLocalTime(r.LastSuccessUtc.Value);
            else
                hint.Text = "上次查询 " + FormatLocalTime(r.LastSuccessUtc.Value);
        }

        private static string FormatLocalTime(DateTime utc)
        {
            return utc.ToLocalTime().ToString("MM-dd HH:mm:ss");
        }
    }

    internal sealed class BatteryTrayApp : ApplicationContext, IDisposable
    {
        private readonly NotifyIcon tray;
        private readonly BatteryPanel panel;
        private readonly BatterySnapshotStore cache;
        private readonly ToolStripMenuItem pinned;
        private readonly ToolStripMenuItem startup;
        private Icon currentIcon;
        private int refreshing;
        private bool quitting;
        private List<BatteryReading> latest;

        public BatteryTrayApp()
        {
            cache = new BatterySnapshotStore();
            panel = new BatteryPanel();
            panel.RefreshRequested += (s, e) => Refresh();
            panel.CreateControl();
            var handle = panel.Handle; // Create a UI handle even before the popup is shown.
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开电量面板", null, (s, e) => ShowPanel());
            menu.Items.Add("立即刷新", null, (s, e) => Refresh());
            pinned = new ToolStripMenuItem("固定悬浮窗") { CheckOnClick = true };
            pinned.CheckedChanged += (s, e) => { panel.Pinned = pinned.Checked; if (pinned.Checked) ShowPanel(); };
            menu.Items.Add(pinned);
            startup = new ToolStripMenuItem("开机启动") { CheckOnClick = true, Checked = HasStartup() };
            startup.CheckedChanged += (s, e) => SetStartup(startup.Checked);
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => Quit());
            currentIcon = CreateNumberIcon("--");
            tray = new NotifyIcon
            {
                Icon = currentIcon, Visible = true, ContextMenuStrip = menu, Text = "外设电量 · 正在检测"
            };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowPanel();
            };
            ShowCached(); // Startup reads only local storage; never polls USB.
        }

        private void Refresh()
        {
            if (quitting || Interlocked.CompareExchange(ref refreshing, 1, 0) != 0) return;
            panel.RefreshButton.Enabled = false;
            panel.Footer.Text = "正在查询外设...";
            System.Threading.ThreadPool.QueueUserWorkItem(o =>
            {
                List<BatteryReading> result = null;
                try { result = BatteryProtocols.ReadAll(); }
                catch (Exception ex)
                {
                    result = new List<BatteryReading>
                    {
                        new BatteryReading { Name="ATK F1V3", Status="检测失败", Detail=ex.Message },
                        new BatteryReading { Name="VXE V87", Status="检测失败", Detail=ex.Message },
                        new BatteryReading { Name="SteelSeries Arctis Nova 5", Status="检测失败", Detail=ex.Message }
                    };
                }
                try
                {
                    if (!quitting && panel.IsHandleCreated)
                        panel.BeginInvoke((Action)(() =>
                        {
                            if (quitting) return;
                            cache.MergeAndSave(result);
                            ShowCached();
                            panel.RefreshButton.Enabled = true;
                            if (cache.LastSaveError != null)
                                panel.Footer.Text = "查询完成，缓存保存失败";
                        }));
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                finally { Interlocked.Exchange(ref refreshing, 0); }
            });
        }

        private void ShowCached()
        {
            List<BatteryReading> result = cache.Snapshot();
            if (quitting || result.Count < 3) return;
            latest = result;
            panel.SetReadings(result);
            string display = result[0].Percent.HasValue ? result[0].Percent.Value.ToString() : "--";
            Icon next = CreateNumberIcon(display);
            tray.Icon = next;
            if (currentIcon != null) currentIcon.Dispose();
            currentIcon = next;
            string mouse = "ATK " + result[0].Display;
            string keyboard = result[1].Percent.HasValue ? "V87 " + result[1].Display
                : (result[1].Connection.Contains("2.4G") ? "V87 receiver" : "V87 --");
            string headset = "Nova5 " + result[2].Display;
            string tooltip = mouse + " | " + keyboard + " | " + headset;
            tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        }

        private void ShowPanel()
        {
            if (panel.Visible && !panel.Pinned) { panel.Hide(); return; }
            if (!panel.Visible)
            {
                var region = Screen.FromPoint(Cursor.Position).WorkingArea;
                var candidate = new Point(Cursor.Position.X - panel.Width + 25, Cursor.Position.Y - panel.Height - 10);
                candidate.X = Math.Max(region.Left + 5, Math.Min(candidate.X, region.Right - panel.Width - 5));
                candidate.Y = Math.Max(region.Top + 5, Math.Min(candidate.Y, region.Bottom - panel.Height - 5));
                panel.Location = candidate;
                panel.Show();
            }
            panel.BringToFront();
            panel.Activate();
        }

        private static Icon CreateNumberIcon(string str)
        {
            using (var bitmap = new Bitmap(64, 64))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Segoe UI", str.Length > 2 ? 25f : 33f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var bg = new SolidBrush(Color.FromArgb(28, 34, 44)))
                    graphics.FillEllipse(bg, 1, 1, 62, 62);
                using (var pen = new Pen(Color.FromArgb(105, 205, 239), 3))
                    graphics.DrawEllipse(pen, 2, 2, 60, 60);
                using (var brush = new SolidBrush(Color.White))
                {
                    SizeF size = graphics.MeasureString(str, font);
                    graphics.DrawString(str, font, brush, (64 - size.Width) / 2, (64 - size.Height) / 2);
                }
                IntPtr hicon = bitmap.GetHicon();
                try { using (var temp = Icon.FromHandle(hicon)) return (Icon)temp.Clone(); }
                finally { DestroyIcon(hicon); }
            }
        }

        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

        private static bool HasStartup()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("PeripheralBatteryLite") != null;
        }

        private static void SetStartup(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue("PeripheralBatteryLite", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("PeripheralBatteryLite", false);
                }
            }
            catch (Exception) { /* no administrator permission required; ignore locked-down registry */ }
        }

        private void Quit()
        {
            quitting = true;
            tray.Visible = false;
            tray.Dispose();
            panel.Pinned = false;
            panel.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            ExitThread();
        }

        void IDisposable.Dispose()
        {
            if (!quitting) Quit();
        }
    }
}