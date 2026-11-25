/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace CrimsonX.Services
{
    public static class SimpleLogger
    {
        private static string LogFile = Path.Combine(AppPaths.LogDir, "error.log");
        private static bool _dirCreated = false;
        private static bool _fallbackUsed = false;
        
        private static readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>();
        private static readonly Task _consumerTask;

        static SimpleLogger()
        {
            _consumerTask = Task.Run(async () =>
            {
                try
                {
                    var reader = _logChannel.Reader;
                    var sb = new StringBuilder();
                    int buffered = 0;
                    while (await reader.WaitToReadAsync())
                    {
                        while (reader.TryRead(out var msg))
                        {
                            sb.Append(msg);
                            if (++buffered >= 32)
                            {
                                WriteBuffer(sb);
                                buffered = 0;
                            }
                        }
                        if (buffered > 0)
                        {
                            WriteBuffer(sb);
                            buffered = 0;
                        }
                    }
                }
                catch { }
            });
        }

        public static void Shutdown()
        {
            try
            {
                _logChannel.Writer.TryComplete();
                _consumerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        private static int _writeCount = 0;

        private static void WriteBuffer(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            if (TryWrite(sb)) return;
            if (_fallbackUsed) return;
            _fallbackUsed = true;
            _dirCreated = false;
            LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrimsonX", "Logs", "error.log");
            TryWrite(sb);
        }

        private static bool TryWrite(StringBuilder sb)
        {
            try
            {
                if (!_dirCreated)
                {
                    var dir = Path.GetDirectoryName(LogFile);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    _dirCreated = true;
                }
                File.AppendAllText(LogFile, sb.ToString());
                sb.Clear();
                if (++_writeCount >= 50)
                {
                    _writeCount = 0;
                    TrimLogFile();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TrimLogFile()
        {
            try
            {
                if (!File.Exists(LogFile)) return;
                var lines = File.ReadAllLines(LogFile);
                if (lines.Length > 1000)
                {
                    var newLines = new string[1000];
                    Array.Copy(lines, lines.Length - 1000, newLines, 0, 1000);
                    File.WriteAllLines(LogFile, newLines);
                }
            }
            catch { }
        }

        public static bool EnableLogging { get; set; } = false;

        public static void LogFatal(Exception ex)
        {
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL {ex}\n\n";
                _logChannel.Writer.TryWrite(msg);
            }
            catch { }
        }

        public static void Log(Exception ex)
        {
            if (!EnableLogging) return;
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n";
                _logChannel.Writer.TryWrite(msg);
            }
            catch { }
        }
        
        public static void Log(string message)
        {
            if (!EnableLogging) return;
            try
            {
                string msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
                _logChannel.Writer.TryWrite(msg);
            }
            catch { }
        }

        private static readonly object _onceLock = new object();
        private static readonly Dictionary<string, int> _onceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly HashSet<string> _firstKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void LogFirst(string key, string message)
        {
            if (!EnableLogging) return;
            lock (_onceLock)
            {
                if (_firstKeys.Count >= LogDedupe.MaxKeys) _firstKeys.Clear();
                if (!_firstKeys.Add(key ?? "")) return;
            }
            Log(message);
        }

        public static void LogOnce(string key, string message)
        {
            string line = message;
            lock (_onceLock)
            {
                if (!LogDedupe.ShouldLog(key, _onceCounts, out bool summary, out int repeats)) return;
                if (summary)
                    line = $"{message} (this notice repeated {repeats} times; further repeats are suppressed)";
            }
            Log(line);
        }
    }
}
