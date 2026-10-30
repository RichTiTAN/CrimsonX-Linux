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
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;

namespace CrimsonX.Services
{
    public static class UpdateService
    {
        public const string AppVersion = "3.2.0";

        private const string DefaultVersionUrl  = "https://raw.githubusercontent.com/RichTiTAN/CrimsonX-Linux/main/version.json";
        private const string DefaultReleaseBase = "https://github.com/RichTiTAN/CrimsonX-Linux/releases/latest/download";

        private static string VersionUrl  => EnvOr("CRIMSONX_VERSION_URL", DefaultVersionUrl);
        private static string ReleaseBase => EnvOr("CRIMSONX_RELEASE_BASE", DefaultReleaseBase);

        private static string EnvOr(string name, string fallback)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim().TrimEnd('/');
        }

        public static string ArchTag()
        {
            switch (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture)
            {
                case System.Runtime.InteropServices.Architecture.Arm64: return "arm64";
                case System.Runtime.InteropServices.Architecture.Arm:   return "arm";
                default:                                                return "x64";
            }
        }

        private static HttpClient BuildClient(TimeSpan timeout, bool throughApp)
        {
            var handler = new System.Net.Http.SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(8) };
            if (throughApp)
            {
                handler.Proxy = new System.Net.WebProxy("http://127.0.0.1:" + ExitNodeChain.XrayProxyPort);
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }
            return new HttpClient(handler) { Timeout = timeout };
        }

        public static async Task<bool> UseAppProxyAsync(bool engineRunning, string? mode)
        {
            if (!engineRunning) return false;
            if (string.Equals(mode, "Clear Proxy", StringComparison.OrdinalIgnoreCase)) return false;
            return await AppProxyListeningAsync().ConfigureAwait(false);
        }

        private static async Task<bool> AppProxyListeningAsync()
        {
            try
            {
                using var socket = new System.Net.Sockets.TcpClient();
                using var timeout = new CancellationTokenSource(300);
                await socket.ConnectAsync(System.Net.IPAddress.Loopback, ExitNodeChain.XrayProxyPort, timeout.Token).ConfigureAwait(false);
                return socket.Connected;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsNewerVersion(string? remoteVersion)
        {
            return Version.TryParse(remoteVersion, out var remote)
                && Version.TryParse(AppVersion, out var local)
                && remote > local;
        }

        public static async Task<(string? remoteVer, string? remoteMin)> CheckForUpdatesAsync(CancellationToken token = default, bool engineRunning = false, string? mode = null)
        {
            bool throughApp = await UseAppProxyAsync(engineRunning, mode).ConfigureAwait(false);
            using var client = BuildClient(TimeSpan.FromSeconds(15), throughApp);
            var url = $"{VersionUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var response = await client.GetAsync(url, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var raw = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var json = JObject.Parse(raw);
            var remoteVer = json["version"]?.ToString() ?? "0.0.0";
            var remoteMin = json["minAutoUpdateVersion"]?.ToString() ?? "0.0.0";
            if (IsNewerVersion(remoteVer))
            {
                return (remoteVer, remoteMin);
            }
            return (null, null);
        }

        public static async Task DownloadAndInstallUpdateAsync(string remoteVersion, string baseDir, Action<string> progressCallback, CancellationToken token, Action<int>? percentCallback = null, bool engineRunning = false, string? mode = null)
        {
            bool throughApp = await UseAppProxyAsync(engineRunning, mode).ConfigureAwait(false);
            using var headClient = BuildClient(TimeSpan.FromSeconds(15), throughApp);
            using var fileClient = BuildClient(Timeout.InfiniteTimeSpan, throughApp);
            var archiveUrl = $"{ReleaseBase}/CrimsonX-linux-{ArchTag()}.tar.gz";
            string version = VersionToken(remoteVersion);
            var archivePath = Path.Combine(baseDir, $"update_temp_{version}.tar.gz");
            var extPath = Path.Combine(baseDir, $"update_extracted_{version}");
            PurgeOtherVersions(baseDir, version);
            try
            {
                long existingLen = 0;
                if (File.Exists(archivePath))
                    existingLen = new FileInfo(archivePath).Length;
                using var headReq = new HttpRequestMessage(HttpMethod.Head, archiveUrl);
                using var headRes = await headClient.SendAsync(headReq, token).ConfigureAwait(false);
                headRes.EnsureSuccessStatusCode();
                var total = headRes.Content.Headers.ContentLength ?? -1L;
                if (total > 0 && existingLen == total)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        progressCallback($"UPDATE ALREADY DOWNLOADED... EXTRACTING");
                        percentCallback?.Invoke(100);
                    });
                }
                else
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, archiveUrl);
                    if (existingLen > 0 && existingLen < total)
                    {
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLen, null);
                        Dispatcher.UIThread.Post(() => progressCallback($"RESUMING DOWNLOAD..."));
                    }
                    else
                    {
                        existingLen = 0;
                        if (File.Exists(archivePath)) File.Delete(archivePath);
                        Dispatcher.UIThread.Post(() =>
                        {
                            progressCallback($"DOWNLOADING UPDATE... 0% (CLICK TO CANCEL)");
                            percentCallback?.Invoke(0);
                        });
                    }
                    using var dlResponse = await fileClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    dlResponse.EnsureSuccessStatusCode();
                    var streamTotal = total;
                    using var fs = new FileStream(archivePath, existingLen > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    if (existingLen > 0 && dlResponse.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        existingLen = 0;
                        fs.SetLength(0);
                    }
                    using var stream = await dlResponse.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    var buffer = new byte[81920];
                    long downloaded = existingLen;
                    int read;
                    int lastPct = -1;
                    while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        downloaded += read;
                        if (streamTotal > 0)
                        {
                            int pct = (int)(downloaded * 100 / streamTotal);
                            if (pct != lastPct)
                            {
                                lastPct = pct;
                                Dispatcher.UIThread.Post(() =>
                                {
                                    progressCallback($"DOWNLOADING UPDATE... {pct}% (CLICK TO CANCEL)");
                                    percentCallback?.Invoke(pct);
                                });
                            }
                        }
                    }
                }
                if (Directory.Exists(extPath)) Directory.Delete(extPath, true);
                Dispatcher.UIThread.Post(() =>
                {
                    progressCallback("EXTRACTING UPDATE...");
                    percentCallback?.Invoke(100);
                });
                try
                {
                    await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        ExtractArchive(archivePath, extPath);
                    }, token).ConfigureAwait(false);
                }
                catch (InvalidDataException)
                {
                    throw new Exception("The downloaded update file is corrupt. It will be re-downloaded next time.");
                }
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                string exeName = Path.GetFileName(currentExe);
                if (string.IsNullOrEmpty(exeName)) exeName = "CrimsonX";
                var appFile = Directory.GetFiles(extPath, exeName, SearchOption.AllDirectories).FirstOrDefault();
                if (appFile == null) throw new Exception($"The downloaded update does not contain {exeName}.");
                var sourceDir = Path.GetDirectoryName(appFile)!;
                WriteUpdaterScript(baseDir, sourceDir, extPath, archivePath, currentExe);
            }
            catch (Exception ex)
            {
                bool isCorrupt = ex.Message.Contains("corrupt") || ex is InvalidDataException;
                if (isCorrupt)
                {
                    try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
                }
                try { if (Directory.Exists(extPath)) Directory.Delete(extPath, true); } catch { }
                throw;
            }
        }

        private static void ExtractArchive(string archivePath, string extPath)
        {
            Directory.CreateDirectory(extPath);
            using var file = File.OpenRead(archivePath);
            using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
            System.Formats.Tar.TarFile.ExtractToDirectory(gzip, extPath, overwriteFiles: true);
        }

        private static void WriteUpdaterScript(string baseDir, string sourceDir, string extPath, string archivePath, string currentExe)
        {
            static string Quote(string path, string label)
            {
                if (path.IndexOfAny(new[] { '\'', '"', '`', '\n', '\r' }) >= 0)
                    throw new Exception($"{label} contains characters the update script cannot handle: {path}");
                return "'" + path + "'";
            }

            int pid = Process.GetCurrentProcess().Id;
            var sh = new System.Text.StringBuilder();
            sh.Append("#!/bin/sh\n");
            sh.Append("# Written by CrimsonX to hand this install over to an update.\n");
            sh.Append("set -u\n");
            sh.Append("BASE=").Append(Quote(baseDir, "Application directory")).Append('\n');
            sh.Append("SRC=").Append(Quote(sourceDir, "Update source directory")).Append('\n');
            sh.Append("EXE=").Append(Quote(currentExe, "Application executable")).Append('\n');
            sh.Append("LOG=\"$BASE/updater.log\"\n");
            sh.Append("say() { printf '[%s] %s\\n' \"$(date '+%Y-%m-%d %H:%M:%S')\" \"$*\" >>\"$LOG\"; }\n");
            sh.Append("say 'update hand-off started'\n");
            sh.Append("i=0\n");
            sh.Append("while kill -0 ").Append(pid).Append(" 2>/dev/null; do\n");
            sh.Append("    sleep 1\n");
            sh.Append("    i=$((i+1))\n");
            sh.Append("    if [ \"$i\" -ge 120 ]; then\n");
            sh.Append("        say 'the app never exited'\n");
            sh.Append("        break\n");
            sh.Append("    fi\n");
            sh.Append("done\n");
            sh.Append("SB=\"$BASE/Data/sing_box/sing-box\"\n");
            sh.Append("KEEP=\"\"\n");
            sh.Append("if [ -f \"$SB\" ] && [ -f \"$SRC/Data/sing_box/sing-box\" ] && cmp -s \"$SB\" \"$SRC/Data/sing_box/sing-box\" && getcap \"$SB\" 2>/dev/null | grep -q cap_net_admin; then\n");
            sh.Append("    KEEP=\"$BASE/Data/sing_box/.sing-box.keep\"\n");
            sh.Append("    mv \"$SB\" \"$KEEP\" 2>/dev/null || KEEP=\"\"\n");
            sh.Append("fi\n");
            sh.Append("copied=0\n");
            sh.Append("tries=0\n");
            sh.Append("while :; do\n");
            sh.Append("    if cp -a \"$SRC/.\" \"$BASE/\" >>\"$LOG\" 2>&1; then\n");
            sh.Append("        say 'copy complete'\n");
            sh.Append("        copied=1\n");
            sh.Append("        break\n");
            sh.Append("    fi\n");
            sh.Append("    tries=$((tries+1))\n");
            sh.Append("    say \"copy attempt $tries failed\"\n");
            sh.Append("    if [ \"$tries\" -lt 20 ]; then\n");
            sh.Append("        sleep 1\n");
            sh.Append("    else\n");
            sh.Append("        say \"gave up after $tries attempts\"\n");
            sh.Append("        break\n");
            sh.Append("    fi\n");
            sh.Append("done\n");
            sh.Append("if [ -n \"$KEEP\" ]; then\n");
            sh.Append("    if mv -f \"$KEEP\" \"$SB\" >>\"$LOG\" 2>&1; then\n");
            sh.Append("        say 'the identical sing-box was kept, VPN capability intact'\n");
            sh.Append("    fi\n");
            sh.Append("fi\n");
            sh.Append("if [ \"$copied\" -eq 1 ]; then\n");
            sh.Append("    if [ -f \"$SB\" ]; then\n");
            sh.Append("        if command -v getcap >/dev/null 2>&1 && getcap \"$SB\" 2>/dev/null | grep -q cap_net_admin; then\n");
            sh.Append("            say 'VPN capability still in place'\n");
            sh.Append("        elif [ \"$(id -u)\" -eq 0 ] && command -v setcap >/dev/null 2>&1; then\n");
            sh.Append("            if setcap cap_net_admin,cap_net_raw+ep \"$SB\" >>\"$LOG\" 2>&1; then\n");
            sh.Append("                say 'capability restored'\n");
            sh.Append("            else\n");
            sh.Append("                say 'could not restore the VPN capability; run install.sh again'\n");
            sh.Append("            fi\n");
            sh.Append("        else\n");
            sh.Append("            say 'VPN Mode will ask for a password until install.sh is run again'\n");
            sh.Append("        fi\n");
            sh.Append("    fi\n");
            sh.Append("    rm -rf ").Append(Quote(extPath, "Update extraction directory")).Append('\n');
            sh.Append("    rm -f ").Append(Quote(archivePath, "Update archive path")).Append('\n');
            sh.Append("fi\n");
            sh.Append("say 'restarting'\n");
            sh.Append("if command -v setsid >/dev/null 2>&1; then\n");
            sh.Append("    setsid \"$EXE\" </dev/null >/dev/null 2>&1 &\n");
            sh.Append("else\n");
            sh.Append("    \"$EXE\" </dev/null >/dev/null 2>&1 &\n");
            sh.Append("fi\n");
            sh.Append("rm -f \"$0\"\n");

            var scriptPath = Path.Combine(baseDir, "updater.sh");
            File.WriteAllText(scriptPath, sh.ToString());
            File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var psi = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"if command -v setsid >/dev/null 2>&1; then setsid sh {Quote(scriptPath, "Update script path")} </dev/null >/dev/null 2>&1 & else nohup sh {Quote(scriptPath, "Update script path")} </dev/null >/dev/null 2>&1 & fi");
            using (Process.Start(psi))
            {
            }
        }

        private static string VersionToken(string? remoteVersion)
        {
            string raw = (remoteVersion ?? "").Trim();
            var token = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (token.Length >= 24) break;
                token.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            }
            return token.Length == 0 ? "unknown" : token.ToString();
        }

        private static void PurgeOtherVersions(string baseDir, string current)
        {
            try
            {
                foreach (string file in Directory.GetFiles(baseDir, "update_temp*.tar.gz"))
                {
                    if (string.Equals(Path.GetFileName(file), "update_temp_" + current + ".tar.gz", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }
                foreach (string dir in Directory.GetDirectories(baseDir, "update_extracted*"))
                {
                    if (string.Equals(Path.GetFileName(dir), "update_extracted_" + current, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }
    }
}
