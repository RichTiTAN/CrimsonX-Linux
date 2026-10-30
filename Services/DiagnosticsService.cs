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
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CrimsonX.Services
{
    public static class DiagnosticsService
    {
        private static readonly StringBuilder Report = new StringBuilder();

        public static int Run(string[] args)
        {
            Report.Clear();

            Line("CrimsonX doctor");
            Line("===============");
            Line("version      : " + UpdateService.AppVersion);
            Line("os           : " + RuntimeInformation.OSDescription);
            Line("arch         : " + RuntimeInformation.ProcessArchitecture
                                 + "  (rid " + RuntimeInformation.RuntimeIdentifier + ")");
            Line("glibc        : " + GlibcVersion());
            Line("");

            Line("-- paths --");
            Line("install dir  : " + AppPaths.InstallDir + Opt(AppPaths.InstallDir));
            Line("state dir    : " + AppPaths.StateDir + Opt(AppPaths.StateDir));
            Line("config dir   : " + AppPaths.ConfigDir);
            Line("log dir      : " + AppPaths.LogDir);
            Line("portable     : " + AppPaths.Portable);
            Line("settings     : " + AppPaths.Resolve("Data/multiplexer_settings.bin")
                                 + (File.Exists(AppPaths.Resolve("Data/multiplexer_settings.bin")) ? " (exists)" : " (not created yet)"));
            Line("");

            Line("-- desktop --");
            Line("session type : " + Env("XDG_SESSION_TYPE"));
            Line("desktop      : " + Env("XDG_CURRENT_DESKTOP"));
            Line("DISPLAY      : " + Env("DISPLAY"));
            Line("WAYLAND      : " + Env("WAYLAND_DISPLAY"));
            Line("x11 sockets  : " + (Directory.Exists("/tmp/.X11-unix")
                                       ? string.Join(" ", Directory.GetFiles("/tmp/.X11-unix")) : "none"));
            Line("");

            Line("-- runtime libraries --");
            foreach (string lib in new[] { "libX11.so.6", "libICE.so.6", "libSM.so.6", "libfontconfig.so.1", "libicuuc.so" })
                Line("  " + lib.PadRight(22) + (LdConfigHas(lib) ? "ok" : "MISSING"));
            Line("");

            AppendDesktopBackends();
            AppendDnsBackends();
            AppendPrivileges();
            AppendEngines();
            AppendFonts();

            Line("-- end of report --");

            string text = Report.ToString();
            Console.WriteLine(text);

            try
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                string file = Path.Combine(AppPaths.LogDir, "doctor.txt");
                File.WriteAllText(file, text);
                Console.WriteLine("saved to " + file);
            }
            catch (Exception ex)
            {
                Console.WriteLine("could not save the report: " + ex.Message);
            }
            return 0;
        }

        private static void Line(string text) => Report.AppendLine(text ?? "");


        private static void AppendDesktopBackends()
        {
            Line("-- proxy backend --");
            Line("  gsettings (GNOME)  : " + (HasCommand("gsettings") ? "available" : "missing"));
            Line("  kwriteconfig6 (KDE): " + (HasCommand("kwriteconfig6") ? "available" : "missing"));
            Line("  kwriteconfig5 (KDE): " + (HasCommand("kwriteconfig5") ? "available" : "missing"));
            string envFile = Path.Combine(Env("HOME", ""), ".config", "environment.d", "99-crimsonx-proxy.conf");
            Line("  env file fallback  : " + envFile + (File.Exists(envFile) ? " (present)" : ""));
            Line("  note               : the desktop proxy is advisory on Linux - apps that");
            Line("                       ignore it bypass it. VPN Mode is the reliable option.");
            Line("");
        }


        private static void AppendDnsBackends()
        {
            Line("-- system dns --");
            Line("  resolvectl         : " + (HasCommand("resolvectl") ? "available" : "missing"));
            string target = "(unknown)";
            try { target = new FileInfo("/etc/resolv.conf").LinkTarget ?? "(a regular file)"; }
            catch (Exception ex) { target = "unreadable: " + ex.Message; }
            Line("  /etc/resolv.conf   : " + target);

            var (ok, output) = Run("cat", "/etc/resolv.conf", 4000);
            if (ok)
            {
                foreach (string line in output.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("nameserver", StringComparison.Ordinal))
                        Line("    " + trimmed);
                }
            }
            Line("");
        }


        private static void AppendPrivileges()
        {
            Line("-- privileges --");
            bool privileged = false;
            try { privileged = Environment.IsPrivilegedProcess; } catch { }
            Line("  running as root    : " + (privileged ? "yes" : "no"));
            Line("  CAP_NET_ADMIN      : " + (HasCapNetAdmin() ? "granted" : "not granted (VPN Mode needs elevation)"));
            Line("  /dev/net/tun       : " + (File.Exists("/dev/net/tun") ? "present" : "MISSING (VPN Mode cannot work)"));
            Line("  pkexec             : " + (HasCommand("pkexec") ? "available" : "missing"));
            Line("  polkit agent       : " + (PolkitAgentRunning() ? "running" : "not detected"));
            Line("  sing-box setcap    : " + (ProcessService.HasFileCapability(
                Path.Combine(AppPaths.SingboxDir, AppPaths.SingboxExe), "cap_net_admin")
                ? "granted (VPN Mode needs no password)"
                : "not granted (VPN Mode will ask through pkexec)"));
            Line("");
        }


        private static void AppendEngines()
        {
            Line("-- engines --");
            AppendEngine(AppPaths.XrayExe, AppPaths.XrayDir, "-version");
            AppendEngine(AppPaths.SingboxExe, AppPaths.SingboxDir, "version");
            AppendGeo(AppPaths.XrayDir);
            Line("");
        }

        private static void AppendGeo(string xrayDir)
        {
            foreach (string name in new[] { "geosite.dat", "geoip.dat" })
            {
                string path = Path.Combine(xrayDir, name);
                bool present = File.Exists(path);
                long size = 0;
                try { if (present) size = new FileInfo(path).Length; } catch { }
                Line("  " + name.PadRight(19) + (present
                    ? "present (" + (size / 1024 / 1024) + " MB)"
                    : "MISSING - xray cannot start configs that use geosite:/geoip: rules"));
            }
        }

        private static void AppendEngine(string name, string dir, string versionArgs)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path))
            {
                Line("  " + name.PadRight(12) + "MISSING  (" + path + ")");
                return;
            }
            bool executable = false;
            try
            {
                var mode = File.GetUnixFileMode(path);
                executable = (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
            }
            catch { }
            var (ok, output) = Run(path, versionArgs, 6000);
            string first = "";
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) { first = trimmed; break; }
            }
            Line("  " + name.PadRight(12) + (executable ? "executable" : "NOT-EXEC")
                 + "  " + ElfArch(path)
                 + "  " + (first.Length > 0 ? first : (ok ? "(ran)" : "(failed)")));
        }


        private static void AppendFonts()
        {
            Line("-- fonts --");
            var (ok, output) = Run("fc-list", "", 6000);
            if (!ok)
            {
                Line("  fc-list            : missing (install fontconfig)");
                Line("");
                return;
            }
            bool persian = output.IndexOf("Vazir", StringComparison.OrdinalIgnoreCase) >= 0
                        || output.IndexOf("Noto Sans Arabic", StringComparison.OrdinalIgnoreCase) >= 0
                        || output.IndexOf("Noto Naskh", StringComparison.OrdinalIgnoreCase) >= 0;
            Line("  persian/arabic     : " + (persian ? "available" : "MISSING (Persian text may show empty boxes)"));
            Line("");
        }


        private static string Env(string name) => Env(name, "(unset)");

        private static string Env(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name) ?? "";
            return value.Length == 0 ? fallback : value;
        }

        private static string Opt(string path)
        {
            try { return Directory.Exists(path) ? " (exists)" : " (missing)"; }
            catch { return ""; }
        }

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

        private static bool LdConfigHas(string library)
        {
            foreach (string tool in new[] { "ldconfig", "/sbin/ldconfig", "/usr/sbin/ldconfig", "/usr/bin/ldconfig" })
            {
                var (ok, output) = Run(tool, "-p", 6000);
                if (!ok) continue;
                if (output.IndexOf(library, StringComparison.Ordinal) >= 0) return true;
                break;
            }
            foreach (string dir in new[]
                     {
                         "/usr/lib", "/usr/lib64", "/lib", "/lib64", "/usr/local/lib",
                         "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib/arm-linux-gnueabihf"
                     })
            {
                try
                {
                    if (Directory.Exists(dir) && Directory.GetFiles(dir, library + "*").Length > 0) return true;
                }
                catch { }
            }
            return false;
        }

        private static bool HasCapNetAdmin()
        {
            try
            {
                foreach (string line in File.ReadAllLines("/proc/self/status"))
                {
                    if (!line.StartsWith("CapEff:", StringComparison.Ordinal)) continue;
                    string hex = line.Substring(7).Trim();
                    if (!ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong mask))
                        return false;
                    return (mask & (1UL << 12)) != 0; // CAP_NET_ADMIN
                }
            }
            catch { }
            return false;
        }

        private static bool PolkitAgentRunning()
        {
            try
            {
                foreach (string dir in Directory.GetDirectories("/proc"))
                {
                    string comm = Path.Combine(dir, "comm");
                    if (!File.Exists(comm)) continue;
                    string name;
                    try { name = File.ReadAllText(comm).Trim(); }
                    catch { continue; }
                    if (name.IndexOf("polkit", StringComparison.OrdinalIgnoreCase) >= 0
                        && name.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static string ElfArch(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                var header = new byte[20];
                if (stream.Read(header, 0, 20) != 20) return "(short read)";
                if (header[0] != 0x7F || header[1] != 0x45) return "(not ELF)";
                int machine = header[18] | (header[19] << 8);
                return machine switch
                {
                    0x3E => "x86-64",
                    0xB7 => "aarch64",
                    0x28 => "arm32",
                    0xF3 => "riscv64",
                    _ => "machine 0x" + machine.ToString("X2")
                };
            }
            catch (Exception ex)
            {
                return "(" + ex.Message + ")";
            }
        }

        private static string GlibcVersion()
        {
            var (ok, output) = Run("ldd", "--version", 4000);
            if (!ok) return "(ldd not available)";
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) return trimmed;
            }
            return "(unknown)";
        }

        private static (bool Ok, string Out) Run(string file, string argLine, int timeoutMs)
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
                foreach (string arg in (argLine ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    psi.ArgumentList.Add(arg);

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "");

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                if (!proc.WaitForExit(Math.Max(500, timeoutMs)))
                {
                    try { proc.Kill(true); } catch { }
                    return (false, "");
                }
                System.Threading.Tasks.Task.WaitAll(
                    new System.Threading.Tasks.Task[] { outTask, errTask }, 1500);

                string stdout = outTask.IsCompletedSuccessfully ? outTask.Result : "";
                string stderr = errTask.IsCompletedSuccessfully ? errTask.Result : "";
                return (proc.ExitCode == 0, stdout.Length > 0 ? stdout : stderr);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
