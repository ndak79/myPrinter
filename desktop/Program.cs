using Microsoft.AspNetCore.Builder;
using PrinterApp.Services.WordConversion;
using System.Net;
using System.Net.Sockets;

namespace MyPrinter.Desktop;

static class Program
{
    private static int _fatalErrorReported;

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

        var diagnostics = StartupDiagnostics.Default;
        try
        {
            ConfigureUnhandledExceptionReporting(diagnostics);
            diagnostics.RecordMessage($"Process started. Arguments: {FormatArguments(args)}");
            RunDesktop(args, diagnostics);
            diagnostics.RecordMessage(
                Environment.ExitCode == 0
                    ? "Process stopped normally."
                    : $"Process stopped after an error (exit code {Environment.ExitCode}).");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            ReportFatalError(diagnostics, "Smart Printer could not start", ex);
        }
    }

    private static void RunDesktop(string[] args, StartupDiagnostics diagnostics)
    {
        var startHidden = args.Contains(
            WindowsStartupService.StartHiddenArgument,
            StringComparer.OrdinalIgnoreCase);

        using var singleInstance = SingleInstanceCoordinator.Create();
        if (!singleInstance.IsPrimary)
        {
            if (singleInstance.TryOpenExistingWindow(TimeSpan.FromSeconds(3)))
            {
                diagnostics.RecordMessage("Existing instance acknowledged the window-open request.");
                return;
            }

            diagnostics.RecordMessage(
                "Existing instance did not acknowledge the window-open request; starting a visible recovery instance.");
            startHidden = false;
        }

        var windowOpenRouter = new WindowOpenRequestRouter();
        using var windowOpenListener = singleInstance.IsPrimary
            ? singleInstance.StartWindowOpenListener(windowOpenRouter.RequestWindowOpen)
            : null;

        ApplicationConfiguration.Initialize();

        var startupResult = WindowsStartupService.Default.EnsureEnabledByDefault();
        if (!startupResult.Succeeded)
        {
            diagnostics.RecordMessage(
                $"Windows auto-start could not be configured: {startupResult.Error}");
        }

        BackendPort = FindFreePort(8787);
        var backendFailure = new TaskCompletionSource<Exception>(
            TaskCreationOptions.RunContinuationsAsynchronously);

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
                diagnostics.RecordException("Backend thread failed", ex);
                backendFailure.TrySetResult(ex);
                Application.Exit();
            }
        })
        { IsBackground = true, Name = "BackendThread" };
        backendThread.Start();

        if (!WaitForBackend(BackendPort, timeoutMs: 8000, backendFailure.Task))
        {
            if (backendFailure.Task.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "The local backend failed during startup.",
                    backendFailure.Task.Result);
            }

            throw new TimeoutException("The local backend did not start within 8 seconds.");
        }

        using var mainForm = new MainForm(startHidden);
        _ = mainForm.Handle;
        windowOpenRouter.Attach(() => OpenMainForm(mainForm));
        Application.Run(mainForm);

        if (backendFailure.Task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(
                "The local backend stopped unexpectedly.",
                backendFailure.Task.Result);
        }

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        BackendApp?.StopAsync(stopTimeout.Token).GetAwaiter().GetResult();
    }

    private static void OpenMainForm(MainForm mainForm)
    {
        if (mainForm.IsDisposed)
            return;

        try
        {
            if (mainForm.InvokeRequired)
                mainForm.BeginInvoke(mainForm.ShowFromExternalRequest);
            else
                mainForm.ShowFromExternalRequest();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ConfigureUnhandledExceptionReporting(StartupDiagnostics diagnostics)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
        {
            Environment.ExitCode = 1;
            ReportFatalError(diagnostics, "An unexpected UI error occurred", eventArgs.Exception);
            Application.Exit();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            var exception = eventArgs.ExceptionObject as Exception
                ?? new InvalidOperationException(eventArgs.ExceptionObject?.ToString() ?? "Unknown fatal error.");
            ReportFatalError(diagnostics, "Unhandled process error", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            diagnostics.RecordException("Unobserved background task error", eventArgs.Exception);
            eventArgs.SetObserved();
        };
    }

    private static void ReportFatalError(
        StartupDiagnostics diagnostics,
        string context,
        Exception exception)
    {
        diagnostics.RecordException(context, exception);
        if (Interlocked.Exchange(ref _fatalErrorReported, 1) != 0)
            return;

        try
        {
            MessageBox.Show(
                $"Smart Printer không thể tiếp tục.\n\n" +
                $"{exception.Message}\n\n" +
                $"Nhật ký chẩn đoán:\n{diagnostics.LogPath}",
                "Smart Printer - Lỗi khởi động",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            // The persistent log remains available if Windows cannot display a dialog.
        }
    }

    private static int FindFreePort(int preferred)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, preferred);
            listener.Start();
            listener.Stop();
            return preferred;
        }
        catch (SocketException)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private static bool WaitForBackend(
        int port,
        int timeoutMs,
        Task<Exception> backendFailure)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs && !backendFailure.IsCompleted)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.Connect("127.0.0.1", port);
                return true;
            }
            catch (SocketException)
            {
            }

            Thread.Sleep(200);
        }

        return false;
    }

    private static string FormatArguments(IEnumerable<string> args)
        => string.Join(" ", args.Select(argument => argument.Contains(' ')
            ? $"\"{argument}\""
            : argument));
}
