using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PeripheralBatteryLite
{
    internal sealed class BatteryTrayApp : ApplicationContext, IDisposable
    {
        private readonly AppPreferencesStore preferencesStore;
        private readonly BatterySnapshotStore cache;
        private readonly List<IBatteryProvider> providers;
        private readonly RawInputActivityTracker activity;
        private readonly BatteryPanel panel;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer scheduler;
        private readonly ToolStripMenuItem autostartMenu;
        private readonly ToolStripMenuItem pinnedMenu;
        private AppPreferences prefs;
        private Icon currentIcon;
        private int isRefreshing;
        private bool closing;
        private bool settingsOpen;
        private bool updatingMenus;

        public BatteryTrayApp()
        {
            preferencesStore = new AppPreferencesStore();
            prefs = preferencesStore.Load();
            providers = ProviderRegistry.Load(prefs.EnableExternalPlugins);
            cache = new BatterySnapshotStore();
            panel = new BatteryPanel();
            activity = new RawInputActivityTracker();

            panel.RawInputReceived += activity.ProcessMessage;
            panel.RefreshRequested += (s, e) => Refresh(false, providers);
            panel.SettingsRequested += (s, e) => ShowSettings();
            var windowHandle = panel.Handle;
            activity.Register(windowHandle);

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开电量面板", null, (s, e) => ShowPanel());
            menu.Items.Add("立即刷新三台设备", null, (s, e) => Refresh(false, providers));
            menu.Items.Add("偏好设置...", null, (s, e) => ShowSettings());
            menu.Items.Add(new ToolStripSeparator());
            pinnedMenu = new ToolStripMenuItem("固定悬浮窗") { Checked = prefs.PinPanel, CheckOnClick = true };
            pinnedMenu.CheckedChanged += (s, e) =>
            {
                if (closing || updatingMenus) return;
                prefs.PinPanel = pinnedMenu.Checked;
                panel.Pinned = prefs.PinPanel;
                preferencesStore.Save(prefs);
                if (panel.Pinned) ShowPanel();
            };
            menu.Items.Add(pinnedMenu);
            autostartMenu = new ToolStripMenuItem("随 Windows 启动") { Checked = StartupRegistration.Enabled, CheckOnClick = true };
            autostartMenu.CheckedChanged += (s, e) =>
            {
                if (closing || updatingMenus) return;
                string error;
                if (!StartupRegistration.SetEnabled(autostartMenu.Checked, out error))
                {
                    updatingMenus = true;
                    try { autostartMenu.Checked = StartupRegistration.Enabled; }
                    finally { updatingMenus = false; }
                    MessageBox.Show(error, "开机自启动配置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            menu.Items.Add(autostartMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => Quit());

            currentIcon = CreateIcon("--");
            tray = new NotifyIcon
            {
                Icon = currentIcon, Visible = true, ContextMenuStrip = menu,
                Text = "Peripheral Battery · 本地电量记录"
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowPanel(); };

            // The scheduler inspects only cached timestamps and passive input events.
            // It does not run any device queries while automatic polling is OFF.
            scheduler = new System.Windows.Forms.Timer { Interval = 30000, Enabled = prefs.IntervalMinutes != 0 };
            scheduler.Tick += (s, e) => TickAutoRefresh();

            DisplayCache();
        }

        private void TickAutoRefresh()
        {
            if (closing || isRefreshing != 0 || prefs.IntervalMinutes == 0) return;
            List<BatteryReading> saved = cache.Snapshot(providers.Select(p => p.Id));
            List<IBatteryProvider> due = BatteryCoordinator.SelectDue(
                providers, saved, prefs, activity, DateTime.UtcNow);
            if (due.Count != 0) Refresh(true, due);
        }

        private void Refresh(bool automatic, IList<IBatteryProvider> selected)
        {
            if (closing || selected == null || selected.Count == 0 ||
                Interlocked.CompareExchange(ref isRefreshing, 1, 0) != 0) return;
            var scheduled = selected.ToList();
            if (!automatic) panel.SetLoading(true);
            panel.SetFooter(automatic ? "后台更新符合条件的活跃设备..." : "正在查询外设...");
            ThreadPool.QueueUserWorkItem(o =>
            {
                List<BatteryReading> fresh = BatteryCoordinator.Query(scheduled);
                try
                {
                    if (closing || !panel.IsHandleCreated)
                    {
                        Interlocked.Exchange(ref isRefreshing, 0);
                        return;
                    }
                    panel.BeginInvoke((Action)(() =>
                    {
                        try
                        {
                            if (closing) return;
                            cache.MergeAndSave(fresh);
                            DisplayCache();
                            panel.SetFooter(cache.LastSaveError == null ? "" : "缓存保存失败，请检查磁盘权限");
                        }
                        finally
                        {
                            panel.SetLoading(false);
                            Interlocked.Exchange(ref isRefreshing, 0);
                        }
                    }));
                }
                catch (Exception) { Interlocked.Exchange(ref isRefreshing, 0); }
            });
        }

        private void DisplayCache()
        {
            List<BatteryReading> snapshots = cache.Snapshot(providers.Select(p => p.Id));
            panel.UpdateData(snapshots, providers, prefs);
            var mouse = snapshots.FirstOrDefault(r => r.Name == BatterySnapshotStore.Mouse);
            string number = mouse != null && mouse.Percent.HasValue ? mouse.Percent.Value.ToString() : "--";
            Icon next = CreateIcon(number);
            tray.Icon = next;
            if (currentIcon != null) currentIcon.Dispose();
            currentIcon = next;
            string mouseLabel = Value(snapshots, BatterySnapshotStore.Mouse);
            string keyboard = Value(snapshots, BatterySnapshotStore.Keyboard);
            string headset = Value(snapshots, BatterySnapshotStore.Headset);
            string tooltip = "F1V3 " + mouseLabel + " | V87 " + keyboard + " | Nova5 " + headset;
            tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        }

        private static string Value(List<BatteryReading> values, string name)
        {
            var r = values.FirstOrDefault(x => x.Name == name);
            return r != null && r.Percent.HasValue ? r.Percent.Value + "%" : "--";
        }

        private void ShowSettings()
        {
            if (settingsOpen || closing) return;
            settingsOpen = true;
            try
            {
                using (var settings = new SettingsForm(prefs, StartupRegistration.Enabled,
                    providers.Count, activity.IsRegistered))
                {
                    if (settings.ShowDialog() != DialogResult.OK) return;
                    AppPreferences next = settings.ResultSettings;
                    bool requiresRestart = next.EnableExternalPlugins != prefs.EnableExternalPlugins;
                    if (!preferencesStore.Save(next))
                    {
                        MessageBox.Show(preferencesStore.LastError ?? "Unable to save settings",
                            "无法保存设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    prefs = next;
                    panel.Pinned = prefs.PinPanel;
                    updatingMenus = true;
                    try { pinnedMenu.Checked = prefs.PinPanel; }
                    finally { updatingMenus = false; }
                    // Apply the persisted options to in-memory scheduling immediately.
                    scheduler.Enabled = prefs.IntervalMinutes > 0;
                    string error;
                    if (StartupRegistration.Enabled != settings.ResultStartup &&
                        !StartupRegistration.SetEnabled(settings.ResultStartup, out error))
                    {
                        MessageBox.Show(error, "开机自启动配置失败",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    updatingMenus = true;
                    try { autostartMenu.Checked = StartupRegistration.Enabled; }
                    finally { updatingMenus = false; }
                    DisplayCache();
                    if (requiresRestart)
                        MessageBox.Show("外部插件开关将在下次启动程序后生效。",
                            "插件设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally { settingsOpen = false; }
        }

        private void ShowPanel()
        {
            if (closing) return;
            if (panel.Visible && !panel.Pinned) { panel.Hide(); return; }
            if (!panel.Visible)
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                int x = Cursor.Position.X - panel.Width + 20;
                int y = Cursor.Position.Y - panel.Height - 12;
                panel.Location = new Point(
                    Math.Max(area.Left + 4, Math.Min(x, area.Right - panel.Width - 4)),
                    Math.Max(area.Top + 4, Math.Min(y, area.Bottom - panel.Height - 4)));
                panel.Show();
            }
            panel.BringToFront();
            panel.Activate();
        }

        private static Icon CreateIcon(string number)
        {
            using (var img = new Bitmap(64, 64))
            using (Graphics g = Graphics.FromImage(img))
            using (var font = new Font("Segoe UI", number.Length > 2 ? 23f : 30f,
                FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var fill = new SolidBrush(UiTheme.Background))
                    g.FillEllipse(fill, 1, 1, 62, 62);
                using (var pen = new Pen(UiTheme.Accent, 4))
                    g.DrawEllipse(pen, 3, 3, 58, 58);
                using (var brush = new SolidBrush(UiTheme.Text))
                {
                    SizeF text = g.MeasureString(number, font);
                    g.DrawString(number, font, brush,
                        (64 - text.Width) / 2, (64 - text.Height) / 2);
                }
                IntPtr handle = img.GetHicon();
                try { using (Icon source = Icon.FromHandle(handle)) return (Icon)source.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        private void Quit()
        {
            if (closing) return;
            closing = true;
            scheduler.Stop();
            scheduler.Dispose();
            activity.Dispose();
            tray.Visible = false;
            tray.Dispose();
            panel.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            ExitThread();
        }

        void IDisposable.Dispose() { Quit(); }
    }
}
