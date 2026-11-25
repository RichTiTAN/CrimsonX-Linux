/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.IO;

namespace CrimsonX.Services
{
    public static class IpsecEngine
    {
        public const string RelativePath = "Data/ipsec/crimsonipsec";

        public static string ExecutablePath(string sbDir)
        {
            try
            {
                string direct = Path.Combine(AppPaths.InstallDir, RelativePath);
                if (File.Exists(direct))
                {
                    EnsureExecutable(direct);
                    return direct;
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            try
            {
                string fromApp = MainWindow.Instance?.GetAppPath(RelativePath) ?? "";
                if (fromApp.Length > 0 && File.Exists(fromApp))
                {
                    EnsureExecutable(fromApp);
                    return fromApp;
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            string fromSb = Search(Path.GetDirectoryName(sbDir ?? ""), RelativePath);
            if (fromSb.Length > 0)
            {
                EnsureExecutable(fromSb);
                return fromSb;
            }
            string fromBase = Search(AppContext.BaseDirectory, RelativePath);
            if (fromBase.Length > 0) EnsureExecutable(fromBase);
            return fromBase;
        }

        private static void EnsureExecutable(string path)
        {
            try
            {
                var mode = File.GetUnixFileMode(path);
                if ((mode & UnixFileMode.UserExecute) != 0) return;
                File.SetUnixFileMode(path, mode
                    | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                SimpleLogger.Log($"[Ipsec] marked {Path.GetFileName(path)} executable.");
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        public static bool IsInstalled(string sbDir) => ExecutablePath(sbDir).Length > 0;

        private static string Search(string start, string relative)
        {
            try
            {
                var dir = new DirectoryInfo(string.IsNullOrWhiteSpace(start) ? AppContext.BaseDirectory : start);
                for (int depth = 0; dir != null && depth < 6; depth++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
            return "";
        }
    }
}
