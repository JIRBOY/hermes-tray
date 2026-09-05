using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HermesTray
{
    public static class GuiOps2
    {
        // ---- Additional P/Invoke ----
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        // ---- Status ----
        public static object GetStatus()
        {
            var bounds = Screen.PrimaryScreen.Bounds;
            return new
            {
                session = GetSessionId(),
                user = Environment.UserName,
                pid = Environment.ProcessId,
                version = "0.2.1",
                screen = new { width = bounds.Width, height = bounds.Height },
                uptime_s = (int)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds
            };
        }

        private static int GetSessionId()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().SessionId; }
            catch { return -1; }
        }

        // ---- Focus ----
        public static bool Focus(string titleSub) => Win32.SetForegroundWindow(FindHandle(titleSub));

        private static IntPtr FindHandle(string titleSub)
        {
            foreach (var w in GuiOps.ListWindows())
                if (w.Title.IndexOf(titleSub, StringComparison.OrdinalIgnoreCase) >= 0)
                    return w.Handle;
            return IntPtr.Zero;
        }

        // ---- Mouse ----
        public static int[] GetMousePos()
        {
            GetCursorPos(out var p);
            return new[] { p.X, p.Y };
        }

        public static void MoveMouse(int x, int y)
        {
            SetCursorPos(x, y);
            Thread.Sleep(50);
        }

        // ---- Screen ----
        public static int[] GetScreenSize()
        {
            var b = Screen.PrimaryScreen.Bounds;
            return new[] { b.Width, b.Height };
        }

        // ---- Wait (element) ----
        public static bool WaitForWindow(string titleSub, int timeoutMs = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (FindHandle(titleSub) != IntPtr.Zero) return true;
                Thread.Sleep(200);
            }
            return false;
        }

        // ---- Exists ----
        public static bool WindowExists(string titleSub) => FindHandle(titleSub) != IntPtr.Zero;

        // ---- Screenshot base64 ----
        public static string ScreenshotBase64(string titleSub = null)
        {
            string path = GuiOps.Screenshot(titleSub);
            if (path == null) return null;
            byte[] bytes = File.ReadAllBytes(path);
            return Convert.ToBase64String(bytes);
        }
    }
}
