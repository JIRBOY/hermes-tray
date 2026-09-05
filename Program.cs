using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace HermesTray
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static NotifyIcon _tray;
        private static HttpApi _api;
        private static bool _exiting;

        [STAThread]
        private static void Main()
        {
            _mutex = new Mutex(true, "HermesTray_SingleInstance", out bool isNew);
            if (!isNew)
            {
                // already running — poke it (nothing to do); exit quietly
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _api = new HttpApi();
            var serverThread = new Thread(() => _api.Start(AppPaths.Port)) { IsBackground = true };
            serverThread.Start();

            _tray = new NotifyIcon
            {
                Icon = CreateIcon(),
                Text = "Hermes Tray — GUI helper for Hegel",
                Visible = true
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("状态 (session / user)", null, (s, e) =>
                _tray.ShowBalloonTip(3000, "Hermes Tray",
                    $"Session: {(Environment.ProcessPath != null ? CurrentSession() : -1)}\nUser: {Environment.UserName}\nPort: {AppPaths.Port}",
                    ToolTipIcon.Info));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开截图目录", null, (s, e) =>
            {
                try { System.IO.Directory.CreateDirectory(AppPaths.ShotsDir); Process.Start("explorer.exe", AppPaths.ShotsDir); } catch { }
            });
            menu.Items.Add("打开日志", null, (s, e) =>
            {
                try { Process.Start("notepad.exe", AppPaths.LogFile); } catch { }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出 Hermes Tray", null, (s, e) => Exit());

            _tray.ContextMenuStrip = menu;
            _tray.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    _tray.ShowBalloonTip(3000, "Hermes Tray",
                        $"运行中 — Session {CurrentSession()} | {Environment.UserName} | 端口 {AppPaths.Port}",
                        ToolTipIcon.Info);
            };

            Log.Info($"=== HermesTray started session={CurrentSession()} user={Environment.UserName} pid={Environment.ProcessId} ===");

            Application.ApplicationExit += (s, e) => Cleanup();
            Application.Run();
        }

        private static int CurrentSession()
        {
            try { return Process.GetCurrentProcess().SessionId; }
            catch { return -1; }
        }

        private static void Exit()
        {
            if (_exiting) return;
            _exiting = true;
            Log.Info("=== HermesTray exiting ===");
            Cleanup();
            Application.Exit();
        }

        private static void Cleanup()
        {
            try { _api?.Stop(); } catch { }
            try { _tray?.Dispose(); } catch { }
        }

        private static Icon CreateIcon()
        {
            // simple 16x16: filled circle with 'H' — pure GDI, no asset file needed
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using var bg = new SolidBrush(Color.FromArgb(40, 55, 90));   // dark navy
                g.FillEllipse(bg, 0, 0, 16, 16);
                using var pen = new Pen(Color.White, 1.6f);
                g.DrawLine(pen, 5, 4, 5, 12);
                g.DrawLine(pen, 11, 4, 11, 12);
                g.DrawLine(pen, 5, 8, 11, 8);
            }
            IntPtr hIcon = bmp.GetHicon();
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
    }
}
