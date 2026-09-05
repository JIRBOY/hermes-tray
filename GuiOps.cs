using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HermesTray
{
    /// <summary>Window enumeration & foreground activation via user32.</summary>
    public class WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public int Left, Top, Width, Height;
        public bool Minimized;

        public override string ToString() => Title;
    }

    public static class Win32
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public const int SW_RESTORE = 9;
    }

    public static class GuiOps
    {
        public static List<WindowInfo> ListWindows()
        {
            var list = new List<WindowInfo>();
            Win32.EnumWindows((h, _) =>
            {
                if (!Win32.IsWindowVisible(h)) return true;
                var sb = new StringBuilder(512);
                Win32.GetWindowText(h, sb, sb.Capacity);
                if (sb.Length == 0) return true;
                var info = new WindowInfo { Handle = h, Title = sb.ToString() };
                if (Win32.GetWindowRect(h, out var r))
                {
                    info.Left = r.Left; info.Top = r.Top;
                    info.Width = r.Right - r.Left; info.Height = r.Bottom - r.Top;
                }
                info.Minimized = Win32.IsIconic(h);
                list.Add(info);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static WindowInfo FindWindow(string titleSub)
        {
            foreach (var w in ListWindows())
                if (w.Title.IndexOf(titleSub, StringComparison.OrdinalIgnoreCase) >= 0)
                    return w;
            return null;
        }

        public static bool ActivateWindow(string titleSub)
        {
            var w = FindWindow(titleSub);
            if (w == null) return false;
            try
            {
                if (w.Minimized) Win32.ShowWindow(w.Handle, Win32.SW_RESTORE);
                Win32.SetForegroundWindow(w.Handle);
                Thread.Sleep(350);
                return true;
            }
            catch (Exception ex) { Log.Warn("ActivateWindow: " + ex.Message); return false; }
        }

        public static bool WindowAction(string titleSub, string action)
        {
            var w = FindWindow(titleSub);
            if (w == null) return false;
            switch (action)
            {
                case "activate": return ActivateWindow(titleSub);
                case "maximize":
                    Win32.ShowWindow(w.Handle, 3); return true;
                case "minimize":
                    Win32.ShowWindow(w.Handle, 6); return true;
                case "restore":
                    Win32.ShowWindow(w.Handle, Win32.SW_RESTORE); return true;
                case "close":
                    Win32.ShowWindow(w.Handle, 0); return true; // hide (not destroy) — conservative
                default: return false;
            }
        }

        public static string Screenshot(string titleSub = null)
        {
            Win32.RECT region;
            if (!string.IsNullOrEmpty(titleSub) && ActivateWindow(titleSub))
            {
                var w = FindWindow(titleSub);
                if (w == null || !Win32.GetWindowRect(w.Handle, out region))
                    region = new Win32.RECT { Left = 0, Top = 0, Right = Screen.PrimaryScreen.Bounds.Width, Bottom = Screen.PrimaryScreen.Bounds.Height };
            }
            else
            {
                var b = Screen.PrimaryScreen.Bounds;
                region = new Win32.RECT { Left = 0, Top = 0, Right = b.Width, Bottom = b.Height };
            }

            int x = Math.Max(region.Left, 0), y = Math.Max(region.Top, 0);
            var sbounds = Screen.PrimaryScreen.Bounds;
            int ex = Math.Min(region.Right, sbounds.Right);
            int ey = Math.Min(region.Bottom, sbounds.Bottom);
            int wpx = ex - x, hpx = ey - y;
            if (wpx <= 0 || hpx <= 0)
            {
                // window entirely off the primary screen — fall back to full screen
                wpx = sbounds.Width; hpx = sbounds.Height;
                x = 0; y = 0;
            }

            using var bmp = new Bitmap(wpx, hpx, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(wpx, hpx), CopyPixelOperation.SourceCopy);
            }
            Directory.CreateDirectory(AppPaths.ShotsDir);
            string path = Path.Combine(AppPaths.ShotsDir,
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            bmp.Save(path, ImageFormat.Png);
            Log.Info($"screenshot saved {path} ({wpx}x{hpx})");
            return path;
        }

        public static void Click(int x, int y)
        {
            Cursor.Position = new Point(x, y);
            Thread.Sleep(120);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            Log.Info($"click ({x},{y})");
        }

        public static void RightClick(int x, int y)
        {
            Cursor.Position = new Point(x, y);
            Thread.Sleep(120);
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void Scroll(int clicks)
        {
            mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)(-clicks * 120), UIntPtr.Zero);
        }

        public static void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            bool hasNonAscii = false;
            foreach (char c in text)
                if (c > 127) { hasNonAscii = true; break; }

            if (hasNonAscii)
            {
                // Non-ASCII: clipboard + ctrl+v via keybd_event (no SendKeys dependency)
                RunInSta(() =>
                {
                    var old = Clipboard.GetText();
                    Clipboard.SetText(text);
                    Thread.Sleep(80);
                    KeyCombo("ctrl+v");
                    Thread.Sleep(120);
                    try { if (!string.IsNullOrEmpty(old)) Clipboard.SetText(old); } catch { }
                });
            }
            else
            {
                foreach (char c in text)
                    TypeChar(c);
            }
            Log.Info($"type {text.Length} chars ({(hasNonAscii ? "clipboard" : "keybd")})");
        }

        private static void TypeChar(char c)
        {
            // VkKeyScan: low byte = VK, high bit 0x08 = needs shift
            ushort scan = (ushort)VkKeyScan(c);
            byte vk = (byte)(scan & 0xFF);
            bool needShift = (scan & 0x0100) != 0;
            if (c == '\n') { KeyCombo("enter"); return; }
            if (c == '\t') { KeyCombo("tab"); return; }
            if (needShift) KeyDown(VK_SHIFT);
            KeyDown(vk); KeyUp(vk);
            if (needShift) KeyUp(VK_SHIFT);
        }

        private static readonly Dictionary<string, byte> NamedKeys = new()
        {
            ["enter"] = 0x0D, ["return"] = 0x0D, ["tab"] = 0x09, ["esc"] = 0x1B,
            ["escape"] = 0x1B, ["space"] = 0x20, ["backspace"] = 0x08,
            ["delete"] = 0x2E, ["del"] = 0x2E, ["up"] = 0x26, ["down"] = 0x28,
            ["left"] = 0x25, ["right"] = 0x27, ["home"] = 0x24, ["end"] = 0x23,
            ["pgup"] = 0x21, ["pgdn"] = 0x22, ["capslock"] = 0x14,
            ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
            ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
            ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
            ["win"] = 0x5B, ["ctrl"] = 0x11, ["control"] = 0x11, ["alt"] = 0x12,
            ["shift"] = 0x10,
        };

        public static void KeyCombo(string combo)
        {
            var parts = combo.ToLowerInvariant().Split('+');
            var downs = new List<byte>();
            var taps = new List<byte>();
            foreach (var p in parts)
            {
                var k = p.Trim();
                if (k.Length == 0) continue;
                if (NamedKeys.TryGetValue(k, out byte vk))
                {
                    if (k is "win" or "ctrl" or "control" or "alt" or "shift") downs.Add(vk);
                    else taps.Add(vk);
                }
                else if (k.Length == 1)
                {
                    ushort scan = (ushort)VkKeyScan(k[0]);
                    byte c = (byte)(scan & 0xFF);
                    if ((scan & 0x0100) != 0) downs.Add(VK_SHIFT);
                    taps.Add(c);
                }
            }
            foreach (var d in downs) KeyDown(d);
            foreach (var t in taps) { KeyDown(t); KeyUp(t); }
            foreach (var d in downs) KeyUp(d);
            Log.Info($"key {combo}");
        }

        private static void KeyDown(byte vk) => keybd_event(vk, 0, 0, UIntPtr.Zero);
        private static void KeyUp(byte vk) => keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        [DllImport("user32.dll")] private static extern short VkKeyScan(char ch);

        /// <summary>Run an action on an STA thread (required by Clipboard).</summary>
        public static void RunInSta(Action a)
        {
            Exception captured = null;
            var t = new Thread(() =>
            {
                try { a(); }
                catch (Exception ex) { captured = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (captured != null) throw captured;
        }

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        private const uint MOUSEEVENTF_LEFTDOWN = 0x02, MOUSEEVENTF_LEFTUP = 0x04;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x08, MOUSEEVENTF_RIGHTUP = 0x10;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_SHIFT = 0x10;
    }
}
