using System;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
// Note: SHA256 and Encoding are NOT imported here — ComputeFingerprint() uses fully-qualified
// System.Security.Cryptography.SHA256 and System.Text.Encoding.UTF8 to avoid ambiguity.

namespace MyPrinter.Desktop.Activation;

/// <summary>
/// 4-layer hardware fingerprint: System UUID | CPU ID | GPU PNP ID | MAC address.
/// Matches Python license_guard.py get_fingerprint() on normal supported machines,
/// except for documented intentional divergences (see spec parity notes in Task 3).
/// </summary>
internal static class FingerprintHelper
{
    // Virtual adapter keywords to filter — matches Python _VIRTUAL_KEYWORDS exactly
    private static readonly string[] VirtualKeywords =
        ["virtual", "vmware", "hyper-v", "vpn", "loopback", "docker", "wsl",
         "bluetooth", "teredo", "isatap", "pseudo", "tunnel", "tap-windows"];

    private static string? _cachedFingerprint; // WMI can take up to 15s/call; cache for process lifetime

    public static string GetFingerprint()
    {
        return _cachedFingerprint ??= ComputeFingerprint();
    }

    private static string ComputeFingerprint()
    {
        var uuid = GetSystemUuid();
        var cpu  = GetCpuId();
        var gpu  = GetGpuId();
        var mac  = GetRealMac();

        // Reject activation if too many components are missing — prevents |||UNKNOWN collisions
        // that would produce identical fingerprints on any machine where WMI fails entirely.
        int nonEmpty = (string.IsNullOrEmpty(uuid) ? 0 : 1)
                     + (string.IsNullOrEmpty(cpu)  ? 0 : 1)
                     + (string.IsNullOrEmpty(gpu)  ? 0 : 1)
                     + (mac == "UNKNOWN"            ? 0 : 1);
        if (nonEmpty < 2)
            throw new InvalidOperationException(
                "Hardware fingerprint is too weak — fewer than 2 components could be read. " +
                "WMI may be unavailable or restricted on this system.");

        var raw  = $"{uuid}|{cpu}|{gpu}|{mac}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string WmiQuery(string className, string field)
    {
        if (!IsWmiIdentifier(className) || !IsWmiIdentifier(field))
            return string.Empty;

        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {field} FROM {className}");
            searcher.Options.Timeout = TimeSpan.FromSeconds(15);
            using var results = searcher.Get();

            foreach (ManagementBaseObject result in results)
            {
                using (result)
                {
                    var value = result[field]?.ToString()?.Trim() ?? string.Empty;
                    var bad = new[] { "", "default string", "to be filled by o.e.m.", "none" };

                    if (!bad.Contains(value.ToLowerInvariant()))
                        return value;
                }
            }
        }
        catch { }

        return string.Empty;
    }

    private static bool IsWmiIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value) && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');

    internal static string GetSystemUuid() => WmiQuery("Win32_ComputerSystemProduct", "UUID");
    internal static string GetCpuId()      => WmiQuery("Win32_Processor",             "ProcessorId");
    internal static string GetGpuId()      => WmiQuery("Win32_VideoController",        "PNPDeviceID");

    /// <summary>
    /// Layer 4: Real physical MAC address.
    /// Matches Python get_real_mac(): filter virtual adapters by name keyword, return first valid MAC.
    /// No OperationalStatus.Up check — Python psutil does not check this either.
    /// </summary>
    internal static string GetRealMac()
    {
        try
        {
            foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
            {
                var name = iface.Name.ToLowerInvariant();

                // Skip loopback
                if (iface.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;

                // Skip virtual adapters by name — same keyword list as Python
                if (VirtualKeywords.Any(k => name.Contains(k))) continue;

                var mac = iface.GetPhysicalAddress().ToString(); // "AABBCCDDEEFF"
                if (!string.IsNullOrEmpty(mac) && mac != "000000000000" && mac.Length == 12)
                {
                    // Format as AA:BB:CC:DD:EE:FF — matches Python output exactly
                    return string.Join(":", Enumerable.Range(0, 6)
                        .Select(i => mac.Substring(i * 2, 2)))
                        .ToUpperInvariant();
                }
            }
        }
        catch { }
        return "UNKNOWN";
    }
}
