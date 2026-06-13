using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
// Note: SHA256 and Encoding are NOT imported here - ComputeFingerprint() uses fully-qualified
// System.Security.Cryptography.SHA256 and System.Text.Encoding.UTF8 to avoid ambiguity.

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// 4-layer hardware fingerprint: System UUID | CPU ID | GPU PNP ID | MAC address.
/// Matches Python license_guard.py get_fingerprint() on normal supported machines,
/// except for documented intentional divergences (see spec parity notes in Task 3).
/// </summary>
internal static class FingerprintHelper
{
    // Virtual adapter keywords to filter - matches Python _VIRTUAL_KEYWORDS exactly.
    private static readonly string[] VirtualKeywords =
        ["virtual", "vmware", "hyper-v", "vpn", "loopback", "docker", "wsl",
         "bluetooth", "teredo", "isatap", "pseudo", "tunnel", "tap-windows"];

    private static string? _cachedFingerprint; // WMI can take up to 15s/call; cache for process lifetime
    private static IReadOnlyList<string>? _cachedFingerprintCandidates;

    public static string GetFingerprint()
    {
        return _cachedFingerprint ??= GetFingerprintCandidates()[0];
    }

    public static IReadOnlyList<string> GetFingerprintCandidates()
    {
        return _cachedFingerprintCandidates ??= ComputeFingerprintCandidates();
    }

    private static string ComputeFingerprint()
        => ComputeFingerprintCandidates()[0];

    private static IReadOnlyList<string> ComputeFingerprintCandidates()
    {
        return BuildFingerprintCandidates(
            WmiQueryAll("Win32_ComputerSystemProduct", "UUID"),
            WmiQueryAll("Win32_Processor", "ProcessorId"),
            WmiQueryAll("Win32_VideoController", "PNPDeviceID"),
            GetRealMacs());
    }

    private static IReadOnlyList<string> BuildFingerprintCandidates(
        string[] uuids,
        string[] cpus,
        string[] gpus,
        string[] macs)
    {
        var uuidCandidates = NormalizeCandidates(uuids, string.Empty);
        var cpuCandidates = NormalizeCandidates(cpus, string.Empty);
        var gpuCandidates = NormalizeCandidates(gpus, string.Empty);
        var macCandidates = NormalizeCandidates(macs, "UNKNOWN");

        var fingerprints = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var uuid in uuidCandidates)
        foreach (var cpu in cpuCandidates)
        foreach (var gpu in gpuCandidates)
        foreach (var mac in macCandidates)
        {
            // Reject activation if too many components are missing - prevents |||UNKNOWN collisions
            // that would produce identical fingerprints on any machine where WMI fails entirely.
            int nonEmpty = (string.IsNullOrEmpty(uuid) ? 0 : 1)
                         + (string.IsNullOrEmpty(cpu)  ? 0 : 1)
                         + (string.IsNullOrEmpty(gpu)  ? 0 : 1)
                         + (mac == "UNKNOWN"            ? 0 : 1);
            if (nonEmpty < 2)
                continue;

            var fingerprint = HashRawFingerprint($"{uuid}|{cpu}|{gpu}|{mac}");
            if (seen.Add(fingerprint))
                fingerprints.Add(fingerprint);
        }

        if (fingerprints.Count == 0)
            throw new InvalidOperationException(
                "Hardware fingerprint is too weak - fewer than 2 components could be read. " +
                "WMI may be unavailable or restricted on this system.");

        return fingerprints;
    }

    private static string[] NormalizeCandidates(string[] values, string fallback)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            normalized.Add(fallback);

        return normalized.ToArray();
    }

    private static string HashRawFingerprint(string raw)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string WmiQuery(string className, string field)
        => WmiQueryAll(className, field).FirstOrDefault() ?? string.Empty;

    private static string[] WmiQueryAll(string className, string field)
    {
        if (!IsWmiIdentifier(className) || !IsWmiIdentifier(field))
            return [];

        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {field} FROM {className}");
            searcher.Options.Timeout = TimeSpan.FromSeconds(15);
            using var results = searcher.Get();
            var values = new List<string>();

            foreach (ManagementBaseObject result in results)
            {
                using (result)
                {
                    var value = result[field]?.ToString()?.Trim() ?? string.Empty;
                    var bad = new[] { "", "default string", "to be filled by o.e.m.", "none" };

                    if (!bad.Contains(value.ToLowerInvariant()))
                        values.Add(value);
                }
            }

            return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch { }

        return [];
    }

    private static bool IsWmiIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value) && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    internal static string GetSystemUuid() => WmiQuery("Win32_ComputerSystemProduct", "UUID");
    internal static string GetCpuId()      => WmiQuery("Win32_Processor",             "ProcessorId");
    internal static string GetGpuId()      => WmiQuery("Win32_VideoController",        "PNPDeviceID");

    /// <summary>
    /// Layer 4: Real physical MAC address.
    /// Matches Python get_real_mac(): filter virtual adapters by name keyword, return first valid MAC.
    /// No OperationalStatus.Up check - Python psutil does not check this either.
    /// </summary>
    internal static string GetRealMac()
        => GetRealMacs().FirstOrDefault() ?? "UNKNOWN";

    private static string[] GetRealMacs()
    {
        try
        {
            var macs = new List<string>();
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                var name = iface.Name.ToLowerInvariant();

                // Skip loopback.
                if (iface.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;

                // Skip virtual adapters by name - same keyword list as Python.
                if (VirtualKeywords.Any(k => name.Contains(k))) continue;

                var mac = iface.GetPhysicalAddress().ToString(); // "AABBCCDDEEFF"
                if (!string.IsNullOrEmpty(mac) && mac != "000000000000" && mac.Length == 12)
                {
                    // Format as AA:BB:CC:DD:EE:FF - matches Python output exactly.
                    macs.Add(string.Join(":", Enumerable.Range(0, 6)
                        .Select(i => mac.Substring(i * 2, 2)))
                        .ToUpperInvariant());
                }
            }

            return macs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch { }

        return [];
    }
}
