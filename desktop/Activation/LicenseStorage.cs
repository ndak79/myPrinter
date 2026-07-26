using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyPrinter.Desktop.Activation;

internal static class LicenseStorage
{
    private static readonly MachineLicenseStore MachineStore = MachineLicenseStore.CreateDefault();
    private static string PersistentPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // Intentional divergence from Python: C# fails hard if LOCALAPPDATA is empty or
        // directory creation fails (throws). Python _persistent_data_dir() falls back to
        // _exe_dir() when LOCALAPPDATA is unavailable or Directory.CreateDirectory fails.
        // If this matters for your deployment (locked-down profiles, containers, SYSTEM service),
        // add a fallback: if (string.IsNullOrEmpty(appData)) appData = AppContext.BaseDirectory;
        var dir = Path.Combine(appData, "myPrinter");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "license.dat");
    }

    /// <summary>
    /// Best-effort migration of legacy EXE-adjacent license.dat to persistent LOCALAPPDATA dir.
    /// Matches Python _migrate_legacy_license_file() - LG-02 hardening: MOVE not copy.
    /// Copy-without-delete allowed revoked-license rollback: revocation deletes persistent file,
    /// but surviving legacy copy gets re-migrated on next launch, restoring the revoked license.
    /// </summary>
    private static void MigrateLegacyFile()
    {
        try
        {
            var persistent = PersistentPath();
            var legacyPath = Path.Combine(AppContext.BaseDirectory, "license.dat");

            if (File.Exists(persistent))
            {
                // Active file exists - delete stale legacy copy so it can never roll back.
                // Matches Python: os.remove(_LEGACY_LICENSE_FILE) when _LICENSE_FILE exists.
                try { File.Delete(legacyPath); } catch { }
                return;
            }

            if (File.Exists(legacyPath))
            {
                // Move (not copy) - atomic on same drive, deletes source. Matches Python shutil.move().
                File.Move(legacyPath, persistent, overwrite: false);
            }
        }
        catch { } // best-effort only
    }

    private static byte[] DeriveKey(string fingerprint)
    {
        // MUST match Python: hashlib.sha256(("smartocr-license-v2:" + fingerprint).encode()).digest()
        return SHA256.HashData(Encoding.UTF8.GetBytes("smartocr-license-v2:" + fingerprint));
    }

    public static bool TrySave(LicenseToken token)
    {
        return MachineStore.TrySave(token);
    }

    /// <summary>Convenience wrapper - callers that don't care about persistence failure.</summary>
    public static void Save(LicenseToken token) => TrySave(token);

    public static LicenseToken? Load() => MachineStore.Load();

    public static bool HasMachineLicense() => MachineStore.Exists();

    public static LicenseToken? MigrateLegacy(IEnumerable<string> fingerprints)
    {
        if (MachineStore.Exists())
            return null;

        var token = LoadLegacy(fingerprints);
        if (token == null || !MachineStore.TrySave(token))
            return null;

        DeleteLegacyFiles();
        return token;
    }

    public static LicenseToken? Load(string fingerprint)
        => LoadLegacy(new[] { fingerprint });

    private static LicenseToken? LoadLegacy(IEnumerable<string> fingerprints)
    {
        MigrateLegacyFile();
        try
        {
            var path = PersistentPath();
            if (!File.Exists(path)) return null;

            var blob = File.ReadAllBytes(path);
            if (blob.Length == 0) return null;

            if (blob[0] == 0x02)
            {
                // Encrypted format (current) - minimum: 1 + 12 (nonce) + 1 (cipher) + 16 (tag) = 30
                if (blob.Length < 30) return null;

                foreach (var fingerprint in fingerprints
                    .Where(fp => !string.IsNullOrWhiteSpace(fp))
                    .Distinct(StringComparer.Ordinal))
                {
                    var token = TryLoadEncrypted(blob, fingerprint);
                    if (token != null)
                        return token;
                }

                return null;
            }
            else
            {
                // Legacy: plaintext JSON (Python < v2 format) - try to parse and re-encrypt.
                var token = JsonSerializer.Deserialize<LicenseToken>(blob);
                return token;
            }
        }
        catch { return null; }
    }

    private static LicenseToken? TryLoadEncrypted(byte[] blob, string fingerprint)
    {
        try
        {
            var key = DeriveKey(fingerprint);
            var nonce = blob[1..13];
            var tagLen = AesGcm.TagByteSizes.MaxSize; // 16
            var cipher = blob[13..(blob.Length - tagLen)];
            var tag = blob[(blob.Length - tagLen)..];

            using var aes = new AesGcm(key, tagLen);
            var plain = new byte[cipher.Length];
            aes.Decrypt(nonce, cipher, tag, plain);

            return JsonSerializer.Deserialize<LicenseToken>(plain);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        MachineStore.Delete();
        DeleteLegacyFiles();
    }

    private static void DeleteLegacyFiles()
    {
        // Delete both persistent and legacy paths - matches Python which deletes both
        // _LICENSE_FILE and _LEGACY_LICENSE_FILE on revoke to prevent rollback resurrection.
        try { File.Delete(PersistentPath()); } catch { }
        try { File.Delete(Path.Combine(AppContext.BaseDirectory, "license.dat")); } catch { }
    }
}
