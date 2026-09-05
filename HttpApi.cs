using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace HermesTray
{
    /// <summary>
    /// Minimal HTTP/JSON server on 127.0.0.1 (no URL ACL needed — plain TcpListener).
    /// Token auth via X-Agent-Token header (or ?token= for GET).
    /// Endpoints: /health /windows /screenshot /window /click /type /key /scroll /app
    /// </summary>
    public class HttpApi
    {
        private TcpListener _listener;
        private volatile bool _running = true;

        public void Start(int port)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
            Log.Info($"HTTP listening on 127.0.0.1:{port}");
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
                }
                catch (Exception ex)
                {
                    if (_running) Log.Warn("accept: " + ex.Message);
                    Thread.Sleep(200);
                }
            }
        }

        public void Stop() { _running = false; try { _listener?.Stop(); } catch { } }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = 15000;
                    // --- read request head ---
                    var head = new StringBuilder();
                    int contentLength = 0;
                    string method = "", path = "", query = "";
                    string requestLine = null;
                    var buf = new byte[1];
                    var bodyBytes = new byte[0];

                    // read headers byte by byte until \r\n\r\n (requests are tiny)
                    var sb = new StringBuilder();
                    int consecutive = 0;
                    while (true)
                    {
                        int n = stream.Read(buf, 0, 1);
                        if (n <= 0) return;
                        char c = (char)buf[0];
                        sb.Append(c);
                        if (c == '\n')
                        {
                            consecutive++;
                            if (consecutive >= 2) break;
                        }
                        else if (c != '\r') consecutive = 0;
                        if (sb.Length > 16384) return; // header too large
                    }

                    var headerText = sb.ToString();
                    var lines = headerText.Replace("\r\n", "\n").Split('\n');
                    foreach (var raw in lines)
                    {
                        var line = raw.TrimEnd('\r');
                        if (requestLine == null && line.Length > 0)
                        {
                            requestLine = line;
                            var parts = line.Split(' ');
                            if (parts.Length >= 2)
                            {
                                method = parts[0].ToUpperInvariant();
                                var uri = parts[1];
                                int qi = uri.IndexOf('?');
                                if (qi >= 0) { path = uri.Substring(0, qi); query = uri.Substring(qi + 1); }
                                else path = uri;
                            }
                        }
                        else
                        {
                            int ci = line.IndexOf(':');
                            if (ci > 0)
                            {
                                var name = line.Substring(0, ci).Trim();
                                var val = line.Substring(ci + 1).Trim();
                                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                                    int.TryParse(val, out contentLength);
                                if (name.Equals("X-Agent-Token", StringComparison.OrdinalIgnoreCase))
                                    _reqToken = val;
                            }
                        }
                    }

                    if (contentLength > 0 && contentLength < 1_000_000)
                    {
                        bodyBytes = new byte[contentLength];
                        int got = 0;
                        while (got < contentLength)
                        {
                            int r = stream.Read(bodyBytes, got, contentLength - got);
                            if (r <= 0) break;
                            got += r;
                        }
                    }

                    // --- auth ---
                    bool tokenOk = (_reqToken == AppPaths.Token);
                    if (!tokenOk && query.Length > 0)
                    {
                        // manual query parse (avoid System.Web dependency)
                        foreach (var pair in query.Split('&'))
                        {
                            var kv = pair.Split('=');
                            if (kv.Length == 2 && kv[0] == "token" &&
                                Uri.UnescapeDataString(kv[1]) == AppPaths.Token)
                            { tokenOk = true; break; }
                        }
                    }

                    var result = Handle(method, path, bodyBytes, tokenOk);
                    WriteResponse(stream, result.code, result.json);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("handler: " + ex.Message);
            }
        }

        private string _reqToken = "";

        private static void WriteResponse(NetworkStream s, int code, string json)
        {
            if (json == null) json = "{}";
            var payload = Encoding.UTF8.GetBytes(json);
            var head = $"HTTP/1.1 {code} {(code == 200 ? "OK" : code == 401 ? "Unauthorized" : code == 404 ? "Not Found" : "Error")}\r\n" +
                       "Content-Type: application/json; charset=utf-8\r\n" +
                       "Connection: close\r\n" +
                       $"Content-Length: {payload.Length}\r\n\r\n";
            var hb = Encoding.ASCII.GetBytes(head);
            s.Write(hb, 0, hb.Length);
            s.Write(payload, 0, payload.Length);
            s.Flush();
        }

        private static JsonDocument ParseBody(byte[] body)
        {
            if (body == null || body.Length == 0) return null;
            try { return JsonDocument.Parse(body); }
            catch { return null; }
        }

        private static string Jstr(JsonDocument doc, string key)
        {
            if (doc == null) return null;
            if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return null;
        }

        private static int? Jint(JsonDocument doc, string key)
        {
            if (doc == null) return null;
            if (doc.RootElement.TryGetProperty(key, out var v) &&
                (v.ValueKind == JsonValueKind.Number || v.ValueKind == JsonValueKind.String))
                return v.GetInt32();
            return null;
        }

        private static (int code, string json) Handle(string method, string path, byte[] body, bool authed)
        {
            if (!authed)
                return (401, Json(new { ok = false, error = "unauthorized" }));

            var doc = ParseBody(body);

            try
            {
                switch (path)
                {
                    case "/health":
                        return (200, Json(new
                        {
                            ok = true,
                            session = Environment.ProcessPath == null ? -1 : GetSessionId(),
                            user = Environment.UserName,
                            pid = Environment.ProcessId,
                            version = "0.2.0",
                            foreground = GetForegroundTitle()
                        }));

                    case "/windows":
                    {
                        var wins = GuiOps.ListWindows();
                        var arr = new List<object>();
                        foreach (var w in wins)
                            arr.Add(new { title = w.Title, handle = w.Handle.ToInt64(),
                                           left = w.Left, top = w.Top,
                                           width = w.Width, height = w.Height,
                                           minimized = w.Minimized });
                        return (200, Json(new { ok = true, count = arr.Count, windows = arr }));
                    }

                    case "/screenshot":
                    {
                        string title = QueryValue(body, doc, "title");
                        string p = GuiOps.Screenshot(title);
                        return p == null ? (500, Json(new { ok = false, error = "screenshot failed" }))
                                         : (200, Json(new { ok = true, path = p }));
                    }

                    case "/window":
                    {
                        string action = Jstr(doc, "action"), title = Jstr(doc, "title");
                        if (string.IsNullOrEmpty(title))
                            return (400, Json(new { ok = false, error = "title required" }));
                        bool done = GuiOps.WindowAction(title, action ?? "activate");
                        return done ? (200, Json(new { ok = true, action = action, window = title }))
                                    : (404, Json(new { ok = false, error = "window not found: " + title }));
                    }

                    case "/click":
                    {
                        int? x = Jint(doc, "x"), y = Jint(doc, "y");
                        if (x == null || y == null)
                            return (400, Json(new { ok = false, error = "x,y required" }));
                        GuiOps.Click(x.Value, y.Value);
                        return (200, Json(new { ok = true, clicked = new[] { x.Value, y.Value } }));
                    }

                    case "/rightclick":
                    {
                        int? x = Jint(doc, "x"), y = Jint(doc, "y");
                        if (x == null || y == null)
                            return (400, Json(new { ok = false, error = "x,y required" }));
                        GuiOps.RightClick(x.Value, y.Value);
                        return (200, Json(new { ok = true }));
                    }

                    case "/type":
                    {
                        string text = Jstr(doc, "text") ?? "";
                        GuiOps.TypeText(text);
                        return (200, Json(new { ok = true, typed = text.Length }));
                    }

                    case "/key":
                    {
                        string combo = Jstr(doc, "combo") ?? "";
                        GuiOps.KeyCombo(combo);
                        return (200, Json(new { ok = true, combo = combo }));
                    }

                    case "/scroll":
                    {
                        int? clicks = Jint(doc, "clicks") ?? 0;
                        GuiOps.Scroll(clicks.Value);
                        return (200, Json(new { ok = true, scrolled = clicks.Value }));
                    }

                    case "/app":
                    {
                        string action = Jstr(doc, "action");
                        if (action == "start")
                        {
                            string path_ = Jstr(doc, "path");
                            if (string.IsNullOrEmpty(path_))
                                return (400, Json(new { ok = false, error = "path required" }));
                            var psi = new ProcessStartInfo(path_) { UseShellExecute = true };
                            if (Jstr(doc, "args") is string args && !string.IsNullOrEmpty(args))
                                psi.Arguments = args;
                            Process.Start(psi);
                            return (200, Json(new { ok = true, started = path_ }));
                        }
                        return (400, Json(new { ok = false, error = "unknown action" }));
                    }

                    case "/status":
                        return (200, Json(GuiOps2.GetStatus()));

                    case "/focus":
                    {
                        string title = Jstr(doc, "title");
                        if (string.IsNullOrEmpty(title))
                            return (400, Json(new { ok = false, error = "title required" }));
                        return (200, Json(new { ok = GuiOps2.Focus(title) }));
                    }

                    case "/mouse-pos":
                        return (200, Json(new { ok = true, pos = GuiOps2.GetMousePos() }));

                    case "/move-mouse":
                    {
                        int? x = Jint(doc, "x"), y = Jint(doc, "y");
                        if (x == null || y == null)
                            return (400, Json(new { ok = false, error = "x,y required" }));
                        GuiOps2.MoveMouse(x.Value, y.Value);
                        return (200, Json(new { ok = true, moved = new[] { x.Value, y.Value } }));
                    }

                    case "/screen-size":
                        return (200, Json(new { ok = true, size = GuiOps2.GetScreenSize() }));

                    case "/wait":
                    {
                        string title = Jstr(doc, "title");
                        int timeout = Jint(doc, "timeout_ms") ?? 5000;
                        if (string.IsNullOrEmpty(title))
                            return (400, Json(new { ok = false, error = "title required" }));
                        bool found = GuiOps2.WaitForWindow(title, timeout);
                        return (200, Json(new { ok = found, title = title, timeout_ms = timeout }));
                    }

                    case "/exists":
                    {
                        string title = Jstr(doc, "title");
                        if (string.IsNullOrEmpty(title))
                            return (400, Json(new { ok = false, error = "title required" }));
                        return (200, Json(new { ok = GuiOps2.WindowExists(title), title = title }));
                    }

                    case "/screenshot-b64":
                    {
                        string title = QueryValue(body, doc, "title");
                        string b64 = GuiOps2.ScreenshotBase64(title);
                        return b64 == null
                            ? (500, Json(new { ok = false, error = "screenshot failed" }))
                            : (200, Json(new { ok = true, base64 = b64 }));
                    }

                    case "/shutdown":
                    {
                        Log.Info("shutdown requested via API");
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            Thread.Sleep(500);
                            Application.Exit();
                        });
                        return (200, Json(new { ok = true, message = "shutting down" }));
                    }

                    // === /run — 远程执行进程 ===
                    case "/run":
                    {
                        string cmd = Jstr(doc, "cmd");
                        string rArgs = Jstr(doc, "args") ?? "";
                        string workdir = Jstr(doc, "workdir");
                        bool wait = doc?.RootElement.TryGetProperty("wait", out var wv) == true && wv.GetBoolean();
                        int timeout = Jint(doc, "timeout_ms") ?? 300_000;
                        string shell = Jstr(doc, "shell");
                        return RunManager.Run(cmd, rArgs, workdir, wait, timeout, shell);
                    }

                    // === /process — 查询进程状态 ===
                    case "/process":
                    {
                        int? pid = Jint(doc, "pid");
                        if (pid == null)
                            return (400, Json(new { ok = false, error = "pid required" }));
                        return RunManager.Status(pid.Value);
                    }

                    // === /process/list — 列出所有进程 ===
                    case "/process/list":
                        return RunManager.List();

                    // === /process/kill — 终止进程 ===
                    case "/process/kill":
                    {
                        int? pid = Jint(doc, "pid");
                        if (pid == null)
                            return (400, Json(new { ok = false, error = "pid required" }));
                        return RunManager.Kill(pid.Value);
                    }

                    // === /process/cleanup — 清理旧记录 ===
                    case "/process/cleanup":
                    {
                        int maxAge = Jint(doc, "max_age_minutes") ?? 60;
                        int removed = RunManager.Cleanup(maxAge);
                        return (200, Json(new { ok = true, removed }));
                    }

                    default:
                        return (404, Json(new { ok = false, error = "not found" }));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{path}: {ex}");
                return (500, Json(new { ok = false, error = ex.Message }));
            }
        }

        private static string QueryValue(byte[] body, JsonDocument doc, string key)
            => Jstr(doc, key);

        private static int GetSessionId()
        {
            try
            {
                uint pid = (uint)Environment.ProcessId;
                var p = Process.GetProcessById((int)pid);
                return p.SessionId;
            }
            catch { return -1; }
        }

        private static string GetForegroundTitle()
        {
            var h = Win32.GetForegroundWindow();
            if (h == IntPtr.Zero) return null;
            var sb = new StringBuilder(512);
            Win32.GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string Json(object o) => JsonSerializer.Serialize(o,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
