using System.Management;
using PrinterApp.Models;

namespace PrinterApp.Services;

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
                "SELECT Name, Default, WorkOffline, PrinterStatus, Capabilities, PortName FROM Win32_Printer");

            foreach (ManagementObject printer in searcher.Get())
            {
                var name = (string)printer["Name"];
                var portName = (string)printer["PortName"];

                // Filter out virtual printers
                if (IsVirtualPrinter(name, portName))
                {
                    continue;
                }

                var isDefault = (bool)printer["Default"];
                var workOffline = (bool)printer["WorkOffline"];
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

                printers.Add(new PrinterInfo
                {
                    Name = name,
                    IsDefault = isDefault,
                    Status = status,
                    IsDuplex = isDuplex,
                    SupportsColor = supportsColor,
                    PortName = portName ?? "",
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
}
