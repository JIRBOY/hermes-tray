using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace HermesTray
{
    /// <summary>Paths & token shared between processes (Hermes in Session 0 reads the same files).</summary>
    public static class AppPaths
    {
        public static readonly string BaseDir = Path.GetDirectoryName(
            Environment.ProcessPath ?? typeof(AppPaths).Assembly.Location) ?? ".";

        // Prefer the shared helper dir so Hermes (Session 0) can find token/shots/log in one place.
        public static readonly string SharedDir =
            @"D:\Manager\AppData\Hermes\gui_helper";

        public static readonly string TokenFile = Path.Combine(SharedDir, "token.txt");
        public static readonly string LogFile = Path.Combine(SharedDir, "hermes_tray.log");
        public static readonly string ShotsDir = Path.Combine(SharedDir, "shots");
        public static readonly int Port = 51234;

        private static string _token;

        public static string Token
        {
            get
            {
                if (_token != null) return _token;
                try
                {
                    Directory.CreateDirectory(SharedDir);
                    if (File.Exists(TokenFile))
                    {
                        string t = File.ReadAllText(TokenFile).Trim();
                        if (t.Length >= 16) { _token = t; return _token; }
                    }
                    _token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                    File.WriteAllText(TokenFile, _token);
                    Log.Info("generated new token");
                }
                catch (Exception ex)
                {
                    Log.Warn("token init failed: " + ex.Message);
                    _token = "fallback-token";
                }
                return _token;
            }
        }
    }

    /// <summary>Tiny thread-safe file logger.</summary>
    public static class Log
    {
        private static readonly object Gate = new object();

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Gate)
                {
                    File.AppendAllText(AppPaths.LogFile,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
                }
            }
            catch { /* logging must never kill the tray */ }
        }
    }
}
