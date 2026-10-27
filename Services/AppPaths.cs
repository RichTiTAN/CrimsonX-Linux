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
using System.IO;

namespace CrimsonX.Services
{
    public static class AppPaths
    {
        public const string XrayExe = "xray";

        public const string SingboxExe = "sing-box";

        public static bool Portable { get; set; }

        public static string InstallDir { get; } = TrimSeparators(AppContext.BaseDirectory);

        private static readonly string HomeDir     = ResolveHome();
        private static readonly string DataHome    = EnvOr("XDG_DATA_HOME",   Path.Combine(HomeDir, ".local", "share"));
        private static readonly string ConfigHome  = EnvOr("XDG_CONFIG_HOME", Path.Combine(HomeDir, ".config"));
        private static readonly string StateHome   = EnvOr("XDG_STATE_HOME",  Path.Combine(HomeDir, ".local", "state"));

        public static string StateDir
            => Portable ? Path.Combine(InstallDir, "Data") : Path.Combine(DataHome, "crimsonx");

        public static string ConfigDir
            => Portable ? Path.Combine(InstallDir, "Data") : Path.Combine(ConfigHome, "crimsonx");

        public static string LogDir
            => Portable ? Path.Combine(InstallDir, "Logs") : Path.Combine(StateHome, "crimsonx", "logs");

        public static string XrayDir { get; } = Path.Combine(InstallDir, "Data", "xray");

        public static string SingboxDir { get; } = Path.Combine(InstallDir, "Data", "sing_box");

        public static string Resolve(string? relative)
        {
            string rel = NormalizeRelative(relative);
            if (rel.Length == 0) return InstallDir;

            if (Portable || IsEnginePath(rel)) return Path.Combine(InstallDir, rel);
            return Path.Combine(StateDir, rel);
        }

        public static string EngineFileName(string? name)
        {
            string value = (name ?? "").Trim();
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - 4);
            return value;
        }

        public static string EnsureDir(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log("[Paths] could not create " + path + ": " + ex.Message);
            }
            return path;
        }

        private static bool IsEnginePath(string rel)
            => rel.StartsWith("Data/xray", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("Data/sing_box", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeRelative(string? relative)
        {
            string value = (relative ?? "").Replace('\\', '/').Trim();
            while (value.StartsWith("./", StringComparison.Ordinal)) value = value.Substring(2);
            return value.TrimStart('/');
        }

        private static string TrimSeparators(string path)
            => (path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string ResolveHome()
        {
            string home = Environment.GetEnvironmentVariable("HOME") ?? "";
            if (home.Length == 0)
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return home.Length == 0 ? InstallDir : home;
        }

        private static string EnvOr(string variable, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(variable) ?? "";
            return value.Length > 0 ? value : fallback;
        }
    }
}
