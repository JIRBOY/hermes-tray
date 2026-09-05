using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace HermesTray
{
    /// <summary>
    /// 进程运行管理器 — 支持 Session 0/1 隔离下远程执行命令
    /// 端点: /run (启动进程) /process (查询状态) /process/kill (终止)
    /// </summary>
    public static class RunManager
    {
        // 进程注册表: id -> ProcessEntry
        private static readonly ConcurrentDictionary<int, ProcessEntry> _procs = new();

        public class ProcessEntry
        {
            public int Id;
            public string Command;
            public string Args;
            public Process Proc;
            public StringBuilder Stdout = new();
            public StringBuilder Stderr = new();
            public bool Finished;
            public int ExitCode = -1;
            public DateTime StartTime;
            public DateTime? EndTime;
            // 限制缓冲区大小防止内存溢出
            public const int MaxBufferChars = 512_000; // 500KB
        }

        /// <summary>
        /// 启动进程
        /// </summary>
        public static (int code, string json) Run(string cmd, string args, string workdir,
                                                   bool wait, int timeoutMs, string shell)
        {
            if (string.IsNullOrEmpty(cmd))
                return (400, J(new { ok = false, error = "cmd required" }));

            // 检查文件是否存在（非shell模式）
            if (string.IsNullOrEmpty(shell) && !File.Exists(cmd) && !IsOnPath(cmd))
                return (400, J(new { ok = false, error = $"command not found: {cmd}" }));

            try
            {
                ProcessStartInfo psi;
                if (!string.IsNullOrEmpty(shell))
                {
                    // shell模式: cmd.exe /c <cmd> <args>
                    psi = new ProcessStartInfo("cmd.exe", $"/c {cmd} {args ?? ""}");
                }
                else
                {
                    psi = new ProcessStartInfo(cmd, args ?? "");
                }

                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = false;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                if (!string.IsNullOrEmpty(workdir) && Directory.Exists(workdir))
                    psi.WorkingDirectory = workdir;

                var proc = Process.Start(psi);
                if (proc == null)
                    return (500, J(new { ok = false, error = "Process.Start returned null" }));

                var entry = new ProcessEntry
                {
                    Id = proc.Id,
                    Command = cmd,
                    Args = args,
                    Proc = proc,
                    StartTime = DateTime.Now
                };
                _procs[proc.Id] = entry;

                // 异步读取输出（防止死锁）
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        lock (entry.Stdout) { if (entry.Stdout.Length < ProcessEntry.MaxBufferChars) entry.Stdout.AppendLine(e.Data); }
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        lock (entry.Stderr) { if (entry.Stderr.Length < ProcessEntry.MaxBufferChars) entry.Stderr.AppendLine(e.Data); }
                };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                if (wait)
                {
                    // 等待完成
                    bool exited = proc.WaitForExit(timeoutMs > 0 ? timeoutMs : 300_000); // 默认5分钟
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                        entry.Finished = true;
                        entry.ExitCode = -999;
                        entry.EndTime = DateTime.Now;
                        return (200, J(new
                        {
                            ok = false,
                            error = "timeout",
                            pid = proc.Id,
                            timeout_ms = timeoutMs > 0 ? timeoutMs : 300_000,
                            stdout = Truncate(entry.Stdout.ToString()),
                            stderr = Truncate(entry.Stderr.ToString())
                        }));
                    }

                    // 等待异步读取完成
                    proc.WaitForExit();
                    Thread.Sleep(200); // 给异步读取一点时间

                    entry.Finished = true;
                    entry.ExitCode = proc.ExitCode;
                    entry.EndTime = DateTime.Now;

                    return (200, J(new
                    {
                        ok = proc.ExitCode == 0,
                        pid = proc.Id,
                        exitCode = proc.ExitCode,
                        stdout = Truncate(entry.Stdout.ToString()),
                        stderr = Truncate(entry.Stderr.ToString()),
                        duration_s = (entry.EndTime.Value - entry.StartTime).TotalSeconds
                    }));
                }
                else
                {
                    // 后台模式：立即返回
                    return (200, J(new
                    {
                        ok = true,
                        pid = proc.Id,
                        status = "running",
                        message = "process started in background, use /process to check status"
                    }));
                }
            }
            catch (Exception ex)
            {
                return (500, J(new { ok = false, error = ex.Message }));
            }
        }

        /// <summary>
        /// 查询进程状态
        /// </summary>
        public static (int code, string json) Status(int pid)
        {
            if (!_procs.TryGetValue(pid, out var entry))
                return (404, J(new { ok = false, error = $"process {pid} not found in registry" }));

            // 更新完成状态
            if (!entry.Finished && entry.Proc.HasExited)
            {
                entry.Finished = true;
                entry.ExitCode = entry.Proc.ExitCode;
                entry.EndTime = entry.Proc.ExitTime;
            }

            return (200, J(new
            {
                ok = true,
                pid = entry.Id,
                command = entry.Command,
                status = entry.Finished ? "finished" : "running",
                exitCode = entry.Finished ? entry.ExitCode : (int?)null,
                stdout = Truncate(entry.Stdout.ToString()),
                stderr = Truncate(entry.Stderr.ToString()),
                start = entry.StartTime,
                end = entry.EndTime,
                duration_s = entry.Finished
                    ? (entry.EndTime.Value - entry.StartTime).TotalSeconds
                    : (DateTime.Now - entry.StartTime).TotalSeconds
            }));
        }

        /// <summary>
        /// 列出所有进程
        /// </summary>
        public static (int code, string json) List()
        {
            var arr = new System.Collections.Generic.List<object>();
            foreach (var kv in _procs)
            {
                var e = kv.Value;
                if (!e.Finished && e.Proc.HasExited)
                {
                    e.Finished = true;
                    e.ExitCode = e.Proc.ExitCode;
                    e.EndTime = e.Proc.ExitTime;
                }
                arr.Add(new
                {
                    pid = e.Id,
                    command = e.Command,
                    status = e.Finished ? "finished" : "running",
                    exitCode = e.Finished ? e.ExitCode : (int?)null,
                    start = e.StartTime,
                    duration_s = e.Finished
                        ? (e.EndTime.Value - e.StartTime).TotalSeconds
                        : (DateTime.Now - e.StartTime).TotalSeconds
                });
            }
            return (200, J(new { ok = true, count = arr.Count, processes = arr }));
        }

        /// <summary>
        /// 终止进程
        /// </summary>
        public static (int code, string json) Kill(int pid)
        {
            if (!_procs.TryGetValue(pid, out var entry))
                return (404, J(new { ok = false, error = $"process {pid} not found" }));

            if (entry.Finished)
                return (200, J(new { ok = true, message = "process already finished", pid }));

            try
            {
                entry.Proc.Kill();
                entry.Finished = true;
                entry.ExitCode = -1;
                entry.EndTime = DateTime.Now;
                return (200, J(new { ok = true, killed = pid }));
            }
            catch (Exception ex)
            {
                return (500, J(new { ok = false, error = ex.Message }));
            }
        }

        /// <summary>
        /// 清理已完成的进程记录
        /// </summary>
        public static int Cleanup(int maxAgeMinutes = 60)
        {
            int removed = 0;
            var cutoff = DateTime.Now.AddMinutes(-maxAgeMinutes);
            foreach (var kv in _procs)
            {
                if (kv.Value.Finished && kv.Value.EndTime < cutoff)
                {
                    _procs.TryRemove(kv.Key, out _);
                    removed++;
                }
            }
            return removed;
        }

        private static bool IsOnPath(string cmd)
        {
            // 简单检查：cmd.exe /c where <cmd>
            try
            {
                var p = Process.Start(new ProcessStartInfo("where", cmd)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                p?.WaitForExit(3000);
                return p?.ExitCode == 0;
            }
            catch { return false; }
        }

        private static string Truncate(string s)
        {
            if (s == null) return "";
            return s.Length > 10_000 ? s.Substring(0, 10_000) + "\n... [truncated]" : s;
        }

        private static string J(object o) => System.Text.Json.JsonSerializer.Serialize(o,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
    }
}
