using Microsoft.AspNetCore.Builder;
using System.Net;
using System.Net.Sockets;

namespace MyPrinter.Desktop;

static class Program
{
    public static int BackendPort { get; private set; } = 8787;
    public static WebApplication? BackendApp { get; private set; }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Find a free port (fallback if 8787 is taken)
        BackendPort = FindFreePort(8787);

        // Start ASP.NET Core backend in a background thread
        var backendThread = new Thread(() =>
        {
            try
            {
                BackendApp = PrinterApp.BackendStartup.Build(
                    [$"--urls=http://localhost:{BackendPort}"]);
                BackendApp.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể khởi động backend:\n{ex.Message}",
                    "Lỗi khởi động",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
            }
        })
        { IsBackground = true, Name = "BackendThread" };
        backendThread.Start();

        // Wait for Kestrel to be ready (max 8s)
        WaitForBackend(BackendPort, timeoutMs: 8000);

        // Launch WinForms UI
        Application.Run(new MainForm());

        // Graceful shutdown when window closes
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        BackendApp?.StopAsync(cts.Token).GetAwaiter().GetResult();
    }

    static int FindFreePort(int preferred)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, preferred);
            listener.Start();
            listener.Stop();
            return preferred;
        }
        catch
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    static void WaitForBackend(int port, int timeoutMs)
    {
        using var http = new HttpClient();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var url = $"http://localhost:{port}/api/printers";
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                var resp = http.GetAsync(url).GetAwaiter().GetResult();
                if (resp.IsSuccessStatusCode) return;
            }
            catch { /* not ready yet */ }
            Thread.Sleep(200);
        }
    }
}