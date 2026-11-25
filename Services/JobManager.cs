/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CrimsonX.Services
{
    public static class JobManager
    {
        private sealed class Tracked
        {
            public int Pid;
            public DateTime StartedUtc;
        }

        private static readonly object Sync = new object();
        private static readonly List<Tracked> Entries = new List<Tracked>();

        public static void Initialize()
        {
        }

        public static void Shutdown()
        {
            Tracked[] snapshot;
            lock (Sync)
            {
                snapshot = Entries.ToArray();
                Entries.Clear();
            }

            foreach (var entry in snapshot)
            {
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(entry.Pid);
                    if (process.StartTime.ToUniversalTime() != entry.StartedUtc) continue; // pid was reused
                    if (process.HasExited) continue;
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
                catch (ArgumentException)
                {
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log("[JobManager] " + ex.Message);
                }
            }
        }

        public static void AddProcess(System.Diagnostics.Process? process)
        {
            if (process == null) return;
            try
            {
                var entry = new Tracked
                {
                    Pid = process.Id,
                    StartedUtc = process.StartTime.ToUniversalTime()
                };
                lock (Sync) { Entries.Add(entry); }
            }
            catch
            {
            }
        }
    }
}

