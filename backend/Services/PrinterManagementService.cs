using System.Management;
using System.Runtime.Versioning;
using PrinterApp.Models;

namespace PrinterApp.Services;

[SupportedOSPlatform("windows")]
public class PrinterManagementService
{
    private DateTime _lastRefresh = DateTime.MinValue;
    private List<PrinterInfo> _cachedPrinters = new();

    public List<PrinterInfo> GetAllPrinters(bool forceRefresh = false)
    {
        // WMI queries are relatively expensive, so cache the results for a short time
        if (!forceRefresh && _cachedPrinters.Count > 0)
        {
            var age = DateTime.Now - _lastRefresh;
            if (age.TotalSeconds < 5)
            {
                return _cachedPrinters;
            }
        }

        var printers = new List<PrinterInfo>();

        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT Name, Default, WorkOffline, PrinterStatus, Capabilities, PortName, AveragePagesPerMinute FROM Win32_Printer");

            foreach (ManagementObject printer in searcher.Get())
            {
                // BUG-4A fix: WMI can return null for Name/PortName — cast with null-coalesce
                var name = printer["Name"] as string ?? string.Empty;
                var portName = printer["PortName"] as string ?? string.Empty;

                // Filter out virtual printers
                if (IsVirtualPrinter(name, portName))
                {
                    continue;
                }

                // BUG-4B fix: WMI bool fields can be null → pattern-match instead of direct cast
                var isDefault = printer["Default"] is bool b1 && b1;
                var workOffline = printer["WorkOffline"] is bool b2 && b2;
                var statusValue = printer["PrinterStatus"] != null
                    ? Convert.ToUInt16(printer["PrinterStatus"])
                    : (UInt16)0;
                var capabilities = printer["Capabilities"] as UInt16[];

                var status = MapPrinterStatus(statusValue, workOffline);
                bool isDuplex = HasDuplexCapability(name, capabilities);

                // Color heuristic: WMI capability 4 = color (note: code 4 also means duplex short-edge in some drivers,
                // so we combine with name heuristic)
                bool supportsColor = (capabilities != null && capabilities.Contains((UInt16)4)) ||
                                     name.ToLowerInvariant().Contains("color") ||
                                     name.ToLowerInvariant().Contains("colour") ||
                                     System.Text.RegularExpressions.Regex.IsMatch(name, @"[Cc]\d{3,4}");

                var ppmEstimate = EstimatePpm(printer, name, portName);

                printers.Add(new PrinterInfo
                {
                    Name = name,
                    IsDefault = isDefault,
                    Status = status,
                    IsDuplex = isDuplex,
                    SupportsColor = supportsColor,
                    PortName = portName ?? "",
                    PpmEstimate = ppmEstimate,
                });
            }

            Console.WriteLine($"Total physical printers found: {printers.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while querying printers: {ex.Message}");
        }

        _cachedPrinters = printers;
        _lastRefresh = DateTime.Now;

        return printers;
    }

    private bool IsVirtualPrinter(string name, string portName)
    {
        var loweredName = name.ToLowerInvariant();
        var loweredPort = portName.ToLowerInvariant();

        if (loweredPort.Contains("nul") ||
            loweredPort.Contains("pdf") ||
            loweredPort.Contains("xps"))
        {
            return true;
        }

        if (loweredName.Contains("pdf") ||
            loweredName.Contains("xps") ||
            loweredName.Contains("onenote") ||
            loweredName.Contains("microsoft print to pdf") ||
            loweredName.Contains("snagit"))
        {
            return true;
        }

        return false;
    }

    internal bool HasDuplexCapability(string name, UInt16[]? capabilities)
    {
        Console.WriteLine($"[HasDuplexCapability] Checking: {name}");

        if (capabilities == null || capabilities.Length == 0)
        {
            Console.WriteLine("  No capabilities data — assuming single-sided");
            return false;
        }

        Console.WriteLine($"  Capabilities: [{string.Join(", ", capabilities)}]");

        // WMI capability codes: 3 = duplex long edge, 4 = duplex short edge
        bool hasDuplex = capabilities.Contains((UInt16)3) || capabilities.Contains((UInt16)4);

        // Known exceptions: printers that report duplex capability but are actually single-sided
        var knownSingleSided = new[] { "lbp2900", "lbp 2900", "hp laser 107", "hp laser 108" };
        var lowName = name.ToLowerInvariant();
        if (hasDuplex && knownSingleSided.Any(s => lowName.Contains(s)))
        {
            Console.WriteLine($"  Override: known single-sided model despite capabilities — {name}");
            return false;
        }

        Console.WriteLine($"  Duplex: {hasDuplex}");
        return hasDuplex;
    }

    private PrinterStatus MapPrinterStatus(UInt16 statusValue, bool workOffline)
    {
        if (workOffline)
        {
            return PrinterStatus.Offline;
        }

        return statusValue switch
        {
            3 => PrinterStatus.Ready,
            4 => PrinterStatus.Printing,
            5 => PrinterStatus.Warmup,
            7 => PrinterStatus.Offline,
            1 => PrinterStatus.Other,
            // BUG-4C fix: statuses 6–11 are error/jam/paper-out states — report as Offline
            // so IsPrinterAvailable returns false and prevents wasted print jobs
            >= 6 and <= 11 => PrinterStatus.Offline,
            _ => PrinterStatus.Unknown
        };
    }

    public bool IsPrinterAvailable(string printerName)
    {
        var printers = GetAllPrinters();
        var printer = printers.FirstOrDefault(p =>
            string.Equals(p.Name, printerName, System.StringComparison.OrdinalIgnoreCase));

        return printer != null && printer.Status != PrinterStatus.Offline;
    }

    /// <summary>
    /// Estimates PPM for a printer using (in priority order):
    /// 1. WMI AveragePagesPerMinute (if > 0)
    /// 2. Name-based lookup table of common printers
    /// 3. Port-type heuristic: USB = 12, Network = 20, LPT = 8
    /// 4. Default fallback: 10 ppm
    /// </summary>
    private static int EstimatePpm(System.Management.ManagementObject printer, string name, string portName)
    {
        // 1. WMI AveragePagesPerMinute
        try
        {
            var wmiPpm = printer["AveragePagesPerMinute"];
            if (wmiPpm != null)
            {
                int ppm = Convert.ToInt32(wmiPpm);
                if (ppm > 0)
                {
                    Console.WriteLine($"[EstimatePpm] {name}: WMI AveragePagesPerMinute = {ppm}");
                    return ppm;
                }
            }
        }
        catch { /* WMI field unavailable */ }

        // 2. Name-based lookup (common models)
        var lowerName = name.ToLowerInvariant();
        var nameLookup = new[]
        {
            (new[] { "lbp2900", "lbp 2900" },          8),
            (new[] { "lbp6000", "lbp6020", "lbp6030" }, 18),
            (new[] { "lbp6230", "lbp6240" },            22),
            (new[] { "lbp621", "lbp623", "lbp663" },   33),
            (new[] { "laserjet p1", "laserjet p10", "laserjet p11", "laserjet p12", "laserjet p13", "laserjet p14", "laserjet p15", "laserjet p16", "laserjet p17", "laserjet p18", "laserjet p19" }, 19),
            (new[] { "laserjet p2", "laserjet p3", "laserjet p4" }, 25),
            (new[] { "laserjet m1", "laserjet m2", "laserjet m3", "laserjet m4" }, 22),
            (new[] { "dcp-", "hl-l23", "hl-l24", "hl-l25" },    24),
            (new[] { "hl-l53", "hl-l54", "hl-l63", "hl-l64" },  40),
            (new[] { "deskjet", "officejet" },          10),
            (new[] { "epson l", "epson m" },             9),
            (new[] { "phaser 3", "workcentre 3" },      25),
        };
        foreach (var (keywords, ppm) in nameLookup)
        {
            foreach (var kw in keywords)
                if (lowerName.Contains(kw))
                {
                    Console.WriteLine($"[EstimatePpm] {name}: matched keyword '{kw}' -> {ppm} ppm");
                    return ppm;
                }
        }

        // 3. Port-type heuristic
        var lowerPort = portName?.ToLowerInvariant() ?? "";
        if (lowerPort.StartsWith("ip_") || lowerPort.StartsWith("tcp") || lowerPort.StartsWith("wsd"))
        {
            Console.WriteLine($"[EstimatePpm] {name}: network port -> 20 ppm");
            return 20;
        }
        if (lowerPort.StartsWith("lpt"))
        {
            Console.WriteLine($"[EstimatePpm] {name}: LPT port -> 8 ppm");
            return 8;
        }
        if (lowerPort.StartsWith("usb"))
        {
            Console.WriteLine($"[EstimatePpm] {name}: USB port -> 12 ppm");
            return 12;
        }

        Console.WriteLine($"[EstimatePpm] {name}: fallback -> 10 ppm");
        return 10;
    }

}
