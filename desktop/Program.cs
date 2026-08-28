using Microsoft.AspNetCore.Builder;
using PrinterApp.Services.WordConversion;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace MyPrinter.Desktop;

static class Program
{
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

}
