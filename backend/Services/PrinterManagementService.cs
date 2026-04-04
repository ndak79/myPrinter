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

                printers.Add(new PrinterInfo
                {
                    Name = name,
                    IsDefault = isDefault,
                    Status = status,
                    IsDuplex = isDuplex
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

    private bool HasDuplexCapability(string name, UInt16[]? capabilities)
    {
        var loweredName = name.ToLowerInvariant();
        Console.WriteLine($"Checking duplex capability for printer: {name}");

        // IMPORTANT: Check known single-sided printers FIRST antes de confiar en capabilities
        // Capabilities array puede tener valores que no significan duplex real
        if (loweredName.Contains("hp laser 107") ||
            loweredName.Contains("hp laser 108") ||
            loweredName.Contains("lbp2900") ||
            loweredName.Contains("lbp 2900"))
        {
            Console.WriteLine($"  Duplex: FALSE - Known single-sided printer (name: {name})");
            return false;
        }

        if (capabilities == null)
        {
            Console.WriteLine("  Duplex: false (no capabilities)");
            return false;
        }

        // Log capabilities for debugging
        Console.WriteLine($"  Capabilities array: [{string.Join(", ", capabilities)}]");

        // Capability values:
        // 3 = Can print duplex vertically  
        // 4 = Can print duplex horizontally
        bool hasDuplex = capabilities.Contains((UInt16)3) || capabilities.Contains((UInt16)4);
        Console.WriteLine($"  Duplex: {hasDuplex} (from capabilities)");

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
