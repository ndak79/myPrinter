using Microsoft.AspNetCore.Builder;
using MyPrinter.Desktop.Activation;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace MyPrinter.Desktop;

static class Program
{
    public static int BackendPort { get; private set; } = 8787;
    public static WebApplication? BackendApp { get; private set; }

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // ── ACTIVATION GATE — TẠM THỜI TẮT ĐỂ TEST ─────────────────────
        // TODO: bỏ comment block dưới đây khi deploy thật
        /*
        try
        {
            var publicKeyPem = LoadPublicKeyPem();
            var (serverUrl, productId, allowInsecure) = LoadActivationConfig();
            LicenseGuard.Configure(serverUrl, productId, publicKeyPem, allowInsecure);

            if (!LicenseGuard.IsActivated())
            {
                using var activationForm = new ActivationForm();
                if (activationForm.ShowDialog() != DialogResult.OK || !activationForm.Activated)
                    return; // User cancelled — exit cleanly
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Lỗi khởi động:\n{ex.Message}\n\n" +
                "Nguyên nhân có thể:\n" +
                "• appsettings.json thiếu hoặc sai cấu hình\n" +
                "• Embedded license_public.pem không tìm thấy\n" +
                "• Phần cứng WMI không khả dụng (không đọc được fingerprint)\n\n" +
                "Liên hệ nhà cung cấp để được hỗ trợ.",
                "Lỗi khởi động",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }
        */
        // ─────────────────────────────────────────────────────────────────

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

        // Wait for Kestrel to be ready (max 8s); abort if it never starts
        if (!WaitForBackend(BackendPort, timeoutMs: 8000))
        {
            MessageBox.Show(
                "Backend không khởi động được trong 8 giây.\nKiểm tra logs và thử lại.",
                "Lỗi khởi động",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

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

    /// <summary>
    /// Returns true when Kestrel is listening (TCP port accepts connections); false if timed out.
    /// TCP probe avoids triggering expensive WMI printer discovery (/api/printers) before UI opens.
    /// Note: TCP success only proves the listener is up, not that routes/services are warm.
    /// In this app, routes are mapped in Build() before Run() starts Kestrel, so no race exists.
    /// </summary>
    static bool WaitForBackend(int port, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                tcp.Connect("127.0.0.1", port);
                return true; // Kestrel is listening
            }
            catch { }
            Thread.Sleep(200);
        }
        return false;
    }

    private static string LoadPublicKeyPem()
    {
        var asm  = Assembly.GetExecutingAssembly();
        var name = "MyPrinter.Desktop.Activation.license_public.pem";
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{name}' not found. " +
                "Ensure Activation\\license_public.pem is marked as EmbeddedResource in the csproj.");
        return new StreamReader(stream).ReadToEnd();
    }

    private static (string ServerUrl, string ProductId, bool AllowInsecureHttp) LoadActivationConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "appsettings.json not found. Create it with Activation.ServerUrl set to your activation server URL.",
                path);

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Activation", out var act))
            throw new InvalidOperationException("appsettings.json is missing the 'Activation' section.");

        var url = act.TryGetProperty("ServerUrl", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url) || url.Contains("your-activation-server"))
            throw new InvalidOperationException(
                "Activation.ServerUrl in appsettings.json is not configured. " +
                "Replace 'https://your-activation-server.com' with the real server URL.");

        var productId = act.TryGetProperty("ProductId", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(productId))
            throw new InvalidOperationException(
                "Activation.ProductId in appsettings.json is not configured. " +
                "Set it to the registered product name, for example 'smartPrinter'.");

        var allowInsecure = act.TryGetProperty("AllowInsecureHttp", out var a) && a.GetBoolean();
        return (url, productId, allowInsecure);
    }
}
