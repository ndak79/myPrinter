// Standalone CLI entry point — delegates all setup to BackendStartup
using System.Runtime.Versioning;
[assembly: SupportedOSPlatform("windows")]

var app = PrinterApp.BackendStartup.Build(args);

Console.WriteLine("🖨️  Printer App Backend running...");
Console.WriteLine("   GET  /api/printers");
Console.WriteLine("   POST /api/upload");
Console.WriteLine("   POST /api/convert");
Console.WriteLine("   POST /api/print");
Console.WriteLine("   POST /api/print/continue");
Console.WriteLine("   DEL  /api/print/cancel");

app.Run();
