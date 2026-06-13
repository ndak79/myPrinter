using Microsoft.AspNetCore.Builder;
using MyPrinter.Desktop.Activation;
using PrinterApp.Services.WordConversion;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace MyPrinter.Desktop;

static class Program
{
    private const string ExpectedTransportKeyId = "actenc_prod_c094fac09065_v1";
    private const string ExpectedTransportPublicKey = "OPTPpTm_-MhYRoRKCYyLTPZLxqQxYxZtnP49VcaIxjM";

    public static int BackendPort { get; private set; } = 8787;
    public static WebApplication? BackendApp { get; private set; }

    [STAThread]
    static void Main(string[] args)
    {
        if (WordConversionWorkerCommand.IsWorkerCommand(args))
        {
            Environment.ExitCode = WordConversionWorkerCommand.Run(args);
            return;
        }

        var startHidden = args.Contains(WindowsStartupService.StartHiddenArgument, StringComparer.OrdinalIgnoreCase);

        ApplicationConfiguration.Initialize();

        using var singleInstance = SingleInstanceCoordinator.Create();
        if (!singleInstance.IsPrimary)
        {
            singleInstance.SignalExistingInstance();
            return;
        }

        try
        {
            var (serverUrl, productId, allowInsecure, transportKeyId, transportPublicKey) = LoadActivationConfig();
            var publicKeysetJson = LoadPublicKeysetJson(productId);
            LicenseGuard.Configure(serverUrl, productId, publicKeysetJson, allowInsecure, transportKeyId, transportPublicKey);

            if (!LicenseGuard.IsActivated())
            {
                using var activationForm = new ActivationForm();
                if (activationForm.ShowDialog() != DialogResult.OK || !activationForm.Activated)
                    return;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Startup error:\n{ex.Message}\n\n" +
                "Possible causes:\n" +
                "- smartprinter.appsettings.json is missing or invalid\n" +
                "- Public keyset file is missing or malformed\n" +
                "- Hardware fingerprint could not be collected\n\n" +
                "Please contact support.",
                "smartPrinter startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        WindowsStartupService.Default.EnsureEnabledByDefault();

        BackendPort = FindFreePort(8787);

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
                    $"Backend failed to start:\n{ex.Message}",
                    "smartPrinter startup error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                Application.Exit();
            }
        })
        { IsBackground = true, Name = "BackendThread" };
        backendThread.Start();

        if (!WaitForBackend(BackendPort, timeoutMs: 8000))
        {
            MessageBox.Show(
                "Backend did not start within 8 seconds.",
                "smartPrinter startup error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using var mainForm = new MainForm(startHidden);
        _ = mainForm.Handle;
        using var activationListener = singleInstance.StartActivationListener(() =>
        {
            if (mainForm.IsDisposed)
                return;

            try
            {
                mainForm.BeginInvoke(mainForm.ShowFromExternalActivation);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        });
        Application.Run(mainForm);

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

    static bool WaitForBackend(int port, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.Connect("127.0.0.1", port);
                return true;
            }
            catch
            {
            }

            Thread.Sleep(200);
        }

        return false;
    }

    private static string LoadPublicKeysetJson(string productId)
    {
        var fileName = $"license_keyset_{productId}.json";
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "Activation", fileName);
        }
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Public keyset '{fileName}' not found in the application directory.",
                path);

        return File.ReadAllText(path);
    }

    private static (
        string ServerUrl,
        string ProductId,
        bool AllowInsecureHttp,
        string? TransportKeyId,
        string? TransportPublicKey) LoadActivationConfig()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "smartprinter.appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "smartprinter.appsettings.json not found. Create it with Activation.ServerUrl set to your activation server URL.",
                path);

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Activation", out var act))
            throw new InvalidOperationException("smartprinter.appsettings.json is missing the 'Activation' section.");

        var url = act.TryGetProperty("ServerUrl", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(url) || url.Contains("your-activation-server"))
            throw new InvalidOperationException(
                "Activation.ServerUrl in smartprinter.appsettings.json is not configured. " +
                "Replace the placeholder with the real server URL.");

        var productId = act.TryGetProperty("ProductId", out var p) ? p.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(productId))
            throw new InvalidOperationException(
                "Activation.ProductId in smartprinter.appsettings.json is not configured. " +
                "Set it to the registered product ID, for example 'prod_smartprinter'.");

        var allowInsecure = act.TryGetProperty("AllowInsecureHttp", out var a) && a.GetBoolean();
        var transportKeyId = act.TryGetProperty("TransportKeyId", out var tk) ? tk.GetString()?.Trim() : null;
        var transportPublicKey = act.TryGetProperty("TransportPublicKey", out var tp) ? tp.GetString()?.Trim() : null;

        ValidateTransportConfig(url, allowInsecure, transportKeyId, transportPublicKey);
        return (url, productId, allowInsecure, transportKeyId, transportPublicKey);
    }

    private static void ValidateTransportConfig(
        string serverUrl,
        bool allowInsecureHttp,
        string? transportKeyId,
        string? transportPublicKey)
    {
        var uri = new Uri(serverUrl, UriKind.Absolute);
        var isLocal = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("::1", StringComparison.OrdinalIgnoreCase);
        var requiresTransport = uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !isLocal
            && allowInsecureHttp;

        var hasKeyId = !string.IsNullOrWhiteSpace(transportKeyId);
        var hasPublicKey = !string.IsNullOrWhiteSpace(transportPublicKey);
        if (requiresTransport && (!hasKeyId || !hasPublicKey))
            throw new InvalidOperationException("Non-local HTTP activation requires Activation.TransportKeyId and Activation.TransportPublicKey.");
        if (hasKeyId != hasPublicKey)
            throw new InvalidOperationException("Activation.TransportKeyId and Activation.TransportPublicKey must be configured together.");
        if (!hasKeyId)
            return;

        if (!string.Equals(transportKeyId, ExpectedTransportKeyId, StringComparison.Ordinal))
            throw new InvalidOperationException("Activation.TransportKeyId does not match the pinned ACTIVSYS transport key.");
        if (!string.Equals(transportPublicKey, ExpectedTransportPublicKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Activation.TransportPublicKey does not match the pinned ACTIVSYS transport key.");
    }
}
