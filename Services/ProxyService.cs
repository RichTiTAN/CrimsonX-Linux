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
using System.Threading.Tasks;

namespace CrimsonX.Services
{
    public static class ProxyService
    {
        public static void SetSystemProxy(bool enable) => SetSystemProxy(enable, ExitNodeChain.XrayProxyPort);

        public static void SetSystemProxy(bool enable, int port)
        {
            if (port <= 0) port = ExitNodeChain.XrayProxyPort;
            CrimsonX.Services.BackgroundTask.Run("system proxy", () => Task.Run(() =>
            {
                try
                {
                    if (enable) Enable(port);
                    else Disable();
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log(ex);
                }
            }));
        }

        public static void DisableSystemProxy() => SetSystemProxy(false);

        public static void RefreshProxy()
        {
            SimpleLogger.Log("[Proxy] the desktop proxy settings were refreshed.");
        }

        private static void Enable(int port)
        {
            string endpoint = "127.0.0.1:" + port;
            string desktop = DesktopName();
            SimpleLogger.Log($"[Proxy] enabling the desktop proxy on {endpoint} (desktop: {(desktop.Length == 0 ? "unknown" : desktop)})");

            bool kdeFirst = desktop.Contains("kde", StringComparison.OrdinalIgnoreCase)
                         || desktop.Contains("plasma", StringComparison.OrdinalIgnoreCase);
            bool gnomeFirst = desktop.Contains("gnome", StringComparison.OrdinalIgnoreCase)
                           || desktop.Contains("unity", StringComparison.OrdinalIgnoreCase)
                           || desktop.Contains("pantheon", StringComparison.OrdinalIgnoreCase);

            if (kdeFirst && KdeEnable(port)) return;
            if (gnomeFirst && GnomeEnable(port)) return;
            if (KdeEnable(port)) return;
            if (GnomeEnable(port)) return;
            WriteEnvironmentFile(port);
        }

        private static void Disable()
        {
            bool touched = false;
            if (KdeDisable()) touched = true;
            if (GnomeDisable()) touched = true;
            if (RemoveEnvironmentFile()) touched = true;
            SimpleLogger.Log(touched
                ? "[Proxy] the desktop proxy is off."
                : "[Proxy] no proxy backend was available to turn off.");
        }

        private static string DesktopName()
        {
            string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "";
            if (desktop.Length == 0) desktop = Environment.GetEnvironmentVariable("DESKTOP_SESSION") ?? "";
            return desktop.Trim();
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

        private static (bool Ok, string Out, string Err) Run(string file, string[] args, int timeoutMs)
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
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(true); } catch { }
                    return (false, "", file + " timed out after " + timeoutMs + " ms");
                }
                Task.WaitAll(new Task[] { outTask, errTask }, 1500);
                return (proc.ExitCode == 0,
                        outTask.IsCompletedSuccessfully ? outTask.Result : "",
                        errTask.IsCompletedSuccessfully ? errTask.Result : "");
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        private static bool RunOk(string file, params string[] args)
        {
            var (ok, _, err) = Run(file, args, 4000);
            if (!ok) SimpleLogger.Log($"[Proxy] {file} {string.Join(' ', args)} failed: {err.Trim()}");
            return ok;
        }


        private static bool GnomeEnable(int port)
        {
            if (!HasCommand("gsettings")) return false;
            string p = port.ToString();
            return RunOk("gsettings", "set", "org.gnome.system.proxy", "mode", "manual")
                 & RunOk("gsettings", "set", "org.gnome.system.proxy.http", "host", "127.0.0.1")
                 & RunOk("gsettings", "set", "org.gnome.system.proxy.http", "port", p)
                 & RunOk("gsettings", "set", "org.gnome.system.proxy.https", "host", "127.0.0.1")
                 & RunOk("gsettings", "set", "org.gnome.system.proxy.https", "port", p)
                 & RunOk("gsettings", "set", "org.gnome.system.proxy", "ignore-hosts", "['localhost','127.0.0.1','::1']");
        }

        private static bool GnomeDisable()
        {
            if (!HasCommand("gsettings")) return false;
            return RunOk("gsettings", "set", "org.gnome.system.proxy", "mode", "none");
        }


        private static string KdeTool()
        {
            if (HasCommand("kwriteconfig6")) return "kwriteconfig6";
            if (HasCommand("kwriteconfig5")) return "kwriteconfig5";
            return "";
        }

        private static bool KdeWrite(string tool, string key, string value)
            => RunOk(tool, "--file", "kioslaverc", "--group", "Proxy Settings", "--key", key, value);

        private static bool KdeEnable(int port)
        {
            string tool = KdeTool();
            if (tool.Length == 0) return false;
            string proxy = "http://127.0.0.1:" + port;
            return KdeWrite(tool, "ProxyType", "1")
                 & KdeWrite(tool, "httpProxy", proxy)
                 & KdeWrite(tool, "httpsProxy", proxy)
                 & KdeWrite(tool, "ftpProxy", proxy)
                 & KdeWrite(tool, "NoProxyFor", "localhost,127.0.0.1,::1");
        }

        private static bool KdeDisable()
        {
            string tool = KdeTool();
            if (tool.Length == 0) return false;
            return KdeWrite(tool, "ProxyType", "0");
        }


        private static string EnvironmentFilePath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("HOME") ?? "";
            return Path.Combine(home, ".config", "environment.d", "99-crimsonx-proxy.conf");
        }

        private static void WriteEnvironmentFile(int port)
        {
            try
            {
                string path = EnvironmentFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string proxy = "http://127.0.0.1:" + port;
                File.WriteAllText(path,
                    "# Written by CrimsonX while the proxy was connected.\n" +
                    "http_proxy=" + proxy + "\n" +
                    "https_proxy=" + proxy + "\n" +
                    "HTTP_PROXY=" + proxy + "\n" +
                    "HTTPS_PROXY=" + proxy + "\n" +
                    "no_proxy=localhost,127.0.0.1,::1\n" +
                    "NO_PROXY=localhost,127.0.0.1,::1\n");
                SimpleLogger.Log("[Proxy] no desktop proxy backend was found, so " + path
                    + " was written instead. Environment variables only apply after logging in again; "
                    + "VPN Mode avoids this entirely.");
            }
            catch (Exception ex)
            {
                SimpleLogger.Log("[Proxy] the environment file could not be written: " + ex.Message);
            }
        }

        private static bool RemoveEnvironmentFile()
        {
            try
            {
                string path = EnvironmentFilePath();
                if (!File.Exists(path)) return false;
                File.Delete(path);
                SimpleLogger.Log("[Proxy] removed " + path);
                return true;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
                return false;
            }
        }
    }
}
