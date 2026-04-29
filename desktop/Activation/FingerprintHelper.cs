using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Run a PowerShell Get-CimInstance query. Mirrors the Python primary path only;
    /// unlike Python _wmi_query(), this C# version does not fall back to wmic.exe.
    /// Returns empty string on failure or placeholder values.
    /// </summary>
    private static string WmiQuery(string className, string field)
    {
        try
        {
            // Identical command to Python: Get-CimInstance -ClassName <class> | Select -First 1
            var ps = $"(Get-CimInstance -ClassName {className} | Select-Object -First 1).{field}";
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    // ⚠️ Use absolute System32 path — matches Python LG-04 hardening against
                    // search-path hijacking. Bare "powershell" is vulnerable if PATH is tampered.
                    FileName               = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments              = $"-NoProfile -Command \"{ps}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                }
            };
            proc.Start();
            // ⚠️ WaitForExit BEFORE ReadToEnd can deadlock if stdout pipe buffer fills (typically
            // 4KB on Windows). Safe here because WMI single-field responses are always a few bytes.
            // If WMI output ever grows (e.g., verbose warnings), switch to async reads.
            if (!proc.WaitForExit(15_000))
            {
                try { proc.Kill(); } catch { }
                return string.Empty;
            }
            var value = proc.StandardOutput.ReadToEnd().Trim();

            // Reject placeholder / empty values — same list as Python
            var bad = new[] { "", "default string", "to be filled by o.e.m.", "none" };
            if (!string.IsNullOrEmpty(value) && !bad.Contains(value.ToLowerInvariant()))
                return value;
        }
        catch { }
        return string.Empty;
    }

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
