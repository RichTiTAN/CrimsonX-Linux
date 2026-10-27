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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace CrimsonX.Services
{
    public static class AppIconResolver
    {
        private static readonly string[] SystemApplicationRoots =
        {
            "/usr/share/applications",
            "/usr/local/share/applications",
            "/var/lib/flatpak/exports/share/applications",
            "/var/lib/snapd/desktop/applications"
        };

        private static readonly string[] IconThemeRoots =
        {
            "/usr/share/icons",
            "/usr/local/share/icons",
            "/var/lib/flatpak/exports/share/icons"
        };

        private static readonly string[] IconSizeDirectories =
        {
            "512x512", "256x256", "192x192", "128x128", "96x96", "64x64", "48x48", "32x32", "24x24", "16x16"
        };

        private static readonly string[] IconCategories = { "apps", "applications", "devices", "places" };

        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public static string ToBase64Png(string? executablePath)
        {
            string path = (executablePath ?? "").Trim();
            if (path.Length == 0) return "";
            if (Cache.TryGetValue(path, out string cached)) return cached;

            string result = "";
            try
            {
                string iconFile = ResolveIconFile(path);
                if (iconFile.Length > 0)
                {
                    byte[] bytes = File.ReadAllBytes(iconFile);
                    if (bytes.Length > 0 && IsPng(bytes)) result = Convert.ToBase64String(bytes);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log("[IconResolver] " + ex.Message);
            }

            Cache[path] = result;
            return result;
        }

        private static bool IsPng(byte[] bytes)
        {
            if (bytes.Length < 8) return false;
            return bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
        }

        private static string ResolveIconFile(string executablePath)
        {
            string fullPath = SafeFullPath(executablePath);

            foreach (string root in ApplicationRoots())
            {
                if (!Directory.Exists(root)) continue;
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFiles(root, "*.desktop", SearchOption.TopDirectoryOnly); }
                catch { continue; }

                foreach (string entry in entries)
                {
                    string icon = IconFromDesktopEntry(entry, fullPath);
                    if (icon.Length == 0) continue;
                    string resolved = FindIconFile(icon);
                    if (resolved.Length > 0) return resolved;
                }
            }

            string binaryName = Path.GetFileNameWithoutExtension(executablePath);
            return binaryName.Length > 0 ? FindIconFile(binaryName) : "";
        }

        private static IEnumerable<string> ApplicationRoots()
        {
            string? userShare = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(userShare))
            {
                yield return Path.Combine(userShare, "applications");
                yield return Path.Combine(userShare, "flatpak", "exports", "share", "applications");
            }
            foreach (string root in SystemApplicationRoots) yield return root;
        }

        private static string IconFromDesktopEntry(string entryPath, string fullPath)
        {
            string iconValue = "";
            bool matches = false;
            try
            {
                foreach (string rawLine in File.ReadLines(entryPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;

                    if (line.StartsWith("Icon=", StringComparison.Ordinal))
                    {
                        if (iconValue.Length == 0) iconValue = line.Substring(5).Trim();
                        continue;
                    }

                    if (matches) continue;

                    if (line.StartsWith("Exec=", StringComparison.Ordinal))
                    {
                        string command = FirstToken(line.Substring(5));
                        if (command.Length > 0 && SameTarget(command, fullPath)) matches = true;
                    }
                    else if (line.StartsWith("TryExec=", StringComparison.Ordinal))
                    {
                        string command = FirstToken(line.Substring(8));
                        if (command.Length > 0 && SameTarget(command, fullPath)) matches = true;
                    }
                }
            }
            catch
            {
                return "";
            }
            return matches ? iconValue : "";
        }

        private static string FirstToken(string commandLine)
        {
            string trimmed = (commandLine ?? "").Trim();
            if (trimmed.Length == 0) return "";
            if (trimmed[0] == '"')
            {
                int end = trimmed.IndexOf('"', 1);
                return end > 1 ? trimmed.Substring(1, end - 1) : "";
            }
            int space = trimmed.IndexOf(' ');
            string token = space < 0 ? trimmed : trimmed.Substring(0, space);
            return token.StartsWith('%') ? "" : token;
        }

        private static bool SameTarget(string command, string fullPath)
        {
            if (command.Length == 0 || fullPath.Length == 0) return false;
            if (!command.StartsWith('/'))
            {
                return string.Equals(Path.GetFileName(command), Path.GetFileName(fullPath), StringComparison.Ordinal);
            }
            return string.Equals(SafeFullPath(command), fullPath, StringComparison.Ordinal);
        }

        private static string SafeFullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        private static string FindIconFile(string iconName)
        {
            string name = (iconName ?? "").Trim();
            if (name.Length == 0) return "";

            if (name.StartsWith('/'))
                return File.Exists(name) ? name : "";

            foreach (string root in IconThemeRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string size in IconSizeDirectories)
                {
                    foreach (string category in IconCategories)
                    {
                        string candidate = Path.Combine(root, "hicolor", size, category, name + ".png");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                foreach (string category in IconCategories)
                {
                    string scalable = Path.Combine(root, "hicolor", "scalable", category, name + ".png");
                    if (File.Exists(scalable)) return scalable;
                }
            }

            string pixmap = Path.Combine("/usr/share/pixmaps", name + ".png");
            if (File.Exists(pixmap)) return pixmap;
            return "";
        }
    }
}
