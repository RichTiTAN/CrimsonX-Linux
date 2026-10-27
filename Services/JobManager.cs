/*
 * CrimsonX - A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.
 * Copyright (C) 2026 RichTiTAN
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
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

