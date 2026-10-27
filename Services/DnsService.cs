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
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class DnsService
    {
        private const int CommandTimeoutMs = 5000;
        private const int ReadBackTimeoutMs = 1500;
        private const int MaxAttempts = 2;

        private static readonly Regex Ipv4Regex = new Regex(
            @"^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]\d|\d)\.){3}(25[0-5]|2[0-4]\d|1\d{2}|[1-9]\d|\d)$",
            RegexOptions.Compiled);

        private static readonly string[] VirtualInterfacePrefixes =
        {
            "lo", "tun", "tap", "wg", "docker", "veth", "virbr", "br-", "podman", "cni",
            "zt", "tailscale", "CrimsonX"
        };

        public static bool IsValidIpv4(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            return Ipv4Regex.IsMatch(address.Trim());
        }

        public static NetworkInterface? GetMainPhysicalAdapter()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                    .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Where(nic => nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .Where(nic => !IsVirtualName(nic.Name))
                    .Where(nic => nic.GetIPProperties().GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(System.Net.IPAddress.Any)))
                    .OrderBy(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
                    .ThenBy(nic => nic.Name, StringComparer.Ordinal)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return null;
            }
        }

        private static bool IsVirtualName(string name)
        {
            string value = (name ?? "").Trim();
            foreach (string prefix in VirtualInterfacePrefixes)
            {
                if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static NetworkInterface? FindAdapter(string adapterName)
        {
            if (string.IsNullOrWhiteSpace(adapterName)) return null;
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => string.Equals(n.Name, adapterName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return null;
            }
        }

        public static string[] GetCurrentDns(NetworkInterface nic)
        {
            try
            {
                return nic.GetIPProperties().DnsAddresses
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.ToString())
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static string[] GetCurrentDns(string adapterName)
        {
            var nic = FindAdapter(adapterName);
            return nic == null ? Array.Empty<string>() : GetCurrentDns(nic);
        }

        public static bool TryReadConfiguredDns(NetworkInterface nic, out string[] servers)
        {
            servers = Array.Empty<string>();
            if (nic == null) return false;

            if (HasCommand("resolvectl"))
            {
                var (ok, output, _) = RunCommand("resolvectl", new[] { "dns", nic.Name }, CommandTimeoutMs);
                if (ok)
                {
                    servers = ParseIpv4List(output);
                    return true;
                }
            }

            servers = GetCurrentDns(nic);
            return servers.Length > 0;
        }

        // ── Capturing and putting back the pre-connect DNS state ──

        public static DnsState CaptureState(NetworkInterface nic)
        {
            bool readable = TryReadConfiguredDns(nic, out var configured);
            bool wasAutomatic = readable && configured.Length == 0;
            string[] servers = configured.Length > 0 ? configured : GetCurrentDns(nic);
            SimpleLogger.Log($"[DnsService] Captured {nic.Name}: automatic={wasAutomatic}, "
                           + $"servers=[{string.Join(", ", servers)}], readable={readable}");
            return new DnsState
            {
                AdapterName = nic.Name,
                WasDhcp     = wasAutomatic,
                Servers     = servers
            };
        }

        public static bool RestoreState(DnsState state, int budgetMs, out string error)
        {
            var servers = NormalizeList(state.Servers);
            if (state.WasDhcp || servers.Length == 0)
                return ResetToAutomatic(state.AdapterName, budgetMs, out error);
            return ApplyList(state.AdapterName, servers, budgetMs, out error);
        }

        public static string[] DescribeRestore(DnsState state)
        {
            var servers = NormalizeList(state.Servers);
            if (state.WasDhcp || servers.Length == 0)
                return new[] { BuildDhcpCommand(state.AdapterName) };
            return BuildApplyCommands(state.AdapterName, servers);
        }

        public static bool SetDns(string adapterName, string primary, string? secondary, int budgetMs, out string error)
        {
            var servers = string.IsNullOrWhiteSpace(secondary)
                ? new[] { primary }
                : new[] { primary, secondary! };
            return ApplyList(adapterName, NormalizeList(servers), budgetMs, out error);
        }

        public static string[] BuildApplyCommands(string adapterName, string[] servers)
        {
            if (HasCommand("resolvectl"))
                return new[] { "resolvectl dns " + adapterName + " " + string.Join(' ', servers) };
            return new[] { "/etc/resolv.conf <- " + string.Join(' ', servers) };
        }

        public static string BuildDhcpCommand(string adapterName)
            => HasCommand("resolvectl")
                ? "resolvectl revert " + adapterName
                : "/etc/resolv.conf <- restore the backup";

        public static bool IsConfigured(string adapterName, string server)
            => GetCurrentDns(adapterName).Any(s => string.Equals(s, server, StringComparison.OrdinalIgnoreCase));

        private static bool ApplyList(string adapterName, string[] servers, int budgetMs, out string error)
        {
            error = "";
            if (servers.Length == 0)
            {
                error = "no usable DNS server was given";
                return false;
            }

            if (ListMatches(GetCurrentDns(adapterName), servers))
            {
                SimpleLogger.LogFirst($"dns-already|{adapterName}",
                    $"[DnsService] {adapterName} already uses [{string.Join(", ", servers)}]");
                return true;
            }

            var sw = Stopwatch.StartNew();
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (Remaining(sw, budgetMs) <= 0)
                {
                    error = $"the {budgetMs} ms budget ran out before {adapterName} was updated";
                    break;
                }

                if (HasCommand("resolvectl"))
                {
                    var args = new List<string> { "dns", adapterName };
                    args.AddRange(servers);
                    var (ran, _, stderr) = RunCommand("resolvectl", args.ToArray(),
                        Math.Min(CommandTimeoutMs, Remaining(sw, budgetMs)));
                    if (!ran) error = stderr.Trim();
                }
                else if (!WriteResolvConf(servers, out string writeError))
                {
                    error = writeError;
                }

                if (WaitFor(() => ListMatches(GetCurrentDns(adapterName), servers), ClosingWait(sw, budgetMs)))
                {
                    SimpleLogger.Log($"[DnsService] applied [{string.Join(", ", servers)}] on {adapterName}");
                    return true;
                }
            }

            if (error.Length == 0) error = "the change did not show up when it was read back";
            SimpleLogger.Log($"[DnsService] {adapterName} was not updated: {error}");
            return false;
        }

        private static bool ResetToAutomatic(string adapterName, int budgetMs, out string error)
        {
            error = "";
            var sw = Stopwatch.StartNew();
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                if (Remaining(sw, budgetMs) <= 0)
                {
                    error = "the budget ran out during the DNS reset";
                    break;
                }

                if (HasCommand("resolvectl"))
                {
                    var (ran, _, stderr) = RunCommand("resolvectl", new[] { "revert", adapterName },
                        Math.Min(CommandTimeoutMs, Remaining(sw, budgetMs)));
                    if (!ran) error = stderr.Trim();
                }
                else if (!RestoreResolvConfBackup(out string restoreError))
                {
                    error = restoreError;
                }

                if (WaitFor(() =>
                    {
                        var nic = FindAdapter(adapterName);
                        if (nic == null) return false;
                        return TryReadConfiguredDns(nic, out var current) && current.Length == 0;
                    }, ClosingWait(sw, budgetMs)))
                {
                    SimpleLogger.Log($"[DnsService] {adapterName} is back on automatic DNS");
                    return true;
                }
            }

            if (error.Length == 0) error = "the DNS reset did not show up when it was read back";
            SimpleLogger.Log($"[DnsService] the reset on {adapterName} is incomplete: {error}");
            return false;
        }


        private const string ResolvConfPath = "/etc/resolv.conf";

        private static string ResolvBackupPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "crimsonx");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "resolv.conf.backup");
        }

        private static bool WriteResolvConf(string[] servers, out string error)
        {
            error = "";
            try
            {
                string backup = ResolvBackupPath();
                if (File.Exists(ResolvConfPath) && !File.Exists(backup))
                    File.Copy(ResolvConfPath, backup, overwrite: false);

                var text = new StringBuilder();
                text.AppendLine("# Written by CrimsonX while the system DNS was overridden.");
                foreach (string server in servers) text.AppendLine("nameserver " + server);
                File.WriteAllText(ResolvConfPath, text.ToString());
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message + " (/etc/resolv.conf can only be written as root)";
                return false;
            }
        }

        private static bool RestoreResolvConfBackup(out string error)
        {
            error = "";
            try
            {
                string backup = ResolvBackupPath();
                if (!File.Exists(backup))
                {
                    error = "no /etc/resolv.conf backup is stored";
                    return false;
                }
                File.Copy(backup, ResolvConfPath, overwrite: true);
                File.Delete(backup);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }


        private static string[] NormalizeList(IEnumerable<string> servers)
        {
            var list = new List<string>();
            foreach (string server in servers ?? Array.Empty<string>())
            {
                string value = (server ?? "").Trim();
                if (value.Length == 0 || value == "0.0.0.0") continue;
                if (!IsValidIpv4(value)) continue;
                if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(value);
            }
            return list.ToArray();
        }

        private static string[] ParseIpv4List(string text)
        {
            var found = new List<string>();
            foreach (Match match in Regex.Matches(text ?? "", @"\b\d{1,3}(?:\.\d{1,3}){3}\b"))
            {
                string value = match.Value;
                if (!IsValidIpv4(value)) continue;
                if (found.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) continue;
                found.Add(value);
            }
            return found.ToArray();
        }

        private static bool ListMatches(string[] current, string[] servers)
            => current.Length == servers.Length
            && servers.All(s => current.Any(c => string.Equals(c, s, StringComparison.OrdinalIgnoreCase)));

        private static bool WaitFor(Func<bool> check, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                try { if (check()) return true; }
                catch { }
                if (sw.ElapsedMilliseconds >= timeoutMs) return false;
                System.Threading.Thread.Sleep(250);
            }
        }

        private static int Remaining(Stopwatch sw, int budgetMs)
            => (int)(budgetMs - sw.ElapsedMilliseconds);

        private static int ClosingWait(Stopwatch sw, int budgetMs)
            => Math.Clamp(Math.Min(ReadBackTimeoutMs, Remaining(sw, budgetMs)), 250, ReadBackTimeoutMs);

        private static bool HasCommand(string command)
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                try { if (File.Exists(Path.Combine(dir, command))) return true; }
                catch { }
            }
            return false;
        }

        private static (bool Ok, string Out, string Err) RunCommand(string file, string[] args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(file)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (string arg in args) psi.ArgumentList.Add(arg);

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "", file + " could not be started");

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(Math.Max(500, timeoutMs)))
                {
                    try { proc.Kill(true); } catch { }
                    return (false, "", file + " timed out");
                }
                Task.WaitAll(new Task[] { outTask, errTask }, 1500);
                string stdout = outTask.IsCompletedSuccessfully ? outTask.Result : "";
                string stderr = errTask.IsCompletedSuccessfully ? errTask.Result : "";
                bool ok = proc.ExitCode == 0;
                if (!ok)
                {
                    SimpleLogger.Log($"[DnsService] {file} {string.Join(' ', args)} exited {proc.ExitCode}: "
                                   + (stderr.Length > 0 ? stderr.Trim() : stdout.Trim()));
                }
                return (ok, stdout, stderr);
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }
    }
}
