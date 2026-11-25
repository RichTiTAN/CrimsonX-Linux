/*
 * CrimsonX - source available software.
 * Copyright (C) 2026 RichTiTAN. All rights reserved.
 * Licensed under the PolyForm Strict License 1.0.0.
 * https://polyformproject.org/licenses/strict/1.0.0
 * Licensing: CrimsonVPN.Support@proton.me
 */

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CrimsonX.Services
{
    internal static class SecureDataKey
    {
        private static readonly object Sync = new object();
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CrimsonX.DataKey.v1");
        private static byte[] _current;

        internal static readonly byte[] LegacyKey = Encoding.UTF8.GetBytes(AppSecrets.CacheKey);
        internal static readonly byte[] LegacyIv = Encoding.UTF8.GetBytes(AppSecrets.CacheIv);

        internal static string KeyPath() => SecureJsonStore.PathFor("data.key");

        internal static byte[] Current()
        {
            lock (Sync)
            {
                if (_current == null) _current = LoadOrCreate() ?? LegacyKey;
                return _current;
            }
        }

        internal static bool UsingLegacy()
        {
            byte[] key = Current();
            return key.Length == LegacyKey.Length && CryptographicOperations.FixedTimeEquals(key, LegacyKey);
        }

        private static byte[] LoadOrCreate()
        {
            string path = KeyPath();
            if (File.Exists(path))
            {
                try
                {
                    byte[] stored = Unprotect(File.ReadAllBytes(path), Entropy);
                    if (stored.Length == 32) return stored;
                }
                catch (Exception ex)
                {
                    SimpleLogger.Log($"[DataKey] The stored key could not be read: {ex.Message}");
                    BackupUnreadable(path);
                }
            }
            try
            {
                byte[] fresh = RandomNumberGenerator.GetBytes(32);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, Protect(fresh, Entropy));
                TryHardenPermissions(path);
                return fresh;
            }
            catch (Exception ex)
            {
                SimpleLogger.Log($"[DataKey] the key file could not be written, keeping the built-in key: {ex.Message}");
                return null;
            }
        }

        private static void BackupUnreadable(string path)
        {
            try
            {
                File.Move(path, path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", overwrite: true);
            }
            catch (Exception ex)
            {
                SimpleLogger.Log(ex);
            }
        }

        private static byte[] Protect(byte[] plain, byte[] entropy)
            => Encoding.UTF8.GetBytes(Convert.ToBase64String(plain));

        private static byte[] Unprotect(byte[] protectedBytes, byte[] entropy)
            => Convert.FromBase64String(Encoding.UTF8.GetString(protectedBytes).Trim());

        private static void TryHardenPermissions(string path)
        {
            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    File.SetUnixFileMode(dir,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Log("[DataKey] could not tighten the key file permissions: " + ex.Message);
            }
        }
    }
}
