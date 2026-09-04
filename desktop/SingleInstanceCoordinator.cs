using System.Diagnostics;
using System.IO.Pipes;

namespace MyPrinter.Desktop;

public sealed class SingleInstanceCoordinator : IDisposable
{
    internal const string LegacyProtocolInstanceName = "MyPrinter.Desktop.SingleInstance";
    internal const string CurrentProtocolInstanceName = "MyPrinter.Desktop.SingleInstance.v2";

    internal const byte WindowOpenRequestCode = 0x41;
    private const byte WindowOpenAccepted = 0x06;

    private readonly object _listenerSync = new();
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _listenerCancellation;
    private Task? _listenerTask;
    private bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceCoordinator(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);

        _pipeName = BuildPipeName(instanceName);
        _mutex = new Mutex(false, BuildMutexName(instanceName));
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }
    }

    public bool IsPrimary => _ownsMutex;

    internal string WindowOpenPipeName => _pipeName;

    public static SingleInstanceCoordinator Create()
        => Create(CurrentProtocolInstanceName);

    public static SingleInstanceCoordinator Create(string instanceName)
        => new(instanceName);

    public IDisposable StartWindowOpenListener(Action windowOpenRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(windowOpenRequested);

        if (!IsPrimary)
            throw new InvalidOperationException("Only the primary instance can listen for window-open requests.");

        lock (_listenerSync)
        {
            if (_listenerTask is not null)
                throw new InvalidOperationException("The window-open listener has already been started.");

            var cancellation = new CancellationTokenSource();
            var firstServer = CreatePipeServer();
            _listenerCancellation = cancellation;
            _listenerTask = ListenForWindowOpenRequestsAsync(
                firstServer,
                windowOpenRequested,
                cancellation.Token);
        }

        return new ListenerRegistration(this);
    }

    public bool TryOpenExistingWindow(TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsPrimary)
            throw new InvalidOperationException("The primary instance cannot use the secondary-instance window-open protocol.");
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "The window-open timeout must be positive.");

        try
        {
            return TryOpenExistingWindowAsync(timeout).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopWindowOpenListener();

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }

    private async Task<bool> TryOpenExistingWindowAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        await using var client = new NamedPipeClientStream(
            serverName: ".",
            pipeName: _pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        await client.ConnectAsync(cancellation.Token).ConfigureAwait(false);
        await client.WriteAsync(new[] { WindowOpenRequestCode }, cancellation.Token).ConfigureAwait(false);
        await client.FlushAsync(cancellation.Token).ConfigureAwait(false);

        var acknowledgement = new byte[1];
        var bytesRead = await client.ReadAsync(acknowledgement, cancellation.Token).ConfigureAwait(false);
        return bytesRead == 1 && acknowledgement[0] == WindowOpenAccepted;
    }

    private async Task ListenForWindowOpenRequestsAsync(
        NamedPipeServerStream firstServer,
        Action windowOpenRequested,
        CancellationToken cancellationToken)
    {
        NamedPipeServerStream? initialServer = firstServer;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await using var server = initialServer ?? CreatePipeServer();
                initialServer = null;

                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    var request = new byte[1];
                    var bytesRead = await server.ReadAsync(request, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 1 && request[0] == WindowOpenRequestCode)
                    {
                        windowOpenRequested();
                        await server.WriteAsync(
                            new[] { WindowOpenAccepted },
                            cancellationToken).ConfigureAwait(false);
                        await server.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (IOException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A caller can exit after sending the request but before reading the ACK.
                    // Keep the primary listener alive for the next launch.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (initialServer is not null)
                await initialServer.DisposeAsync().ConfigureAwait(false);
        }
    }

    private NamedPipeServerStream CreatePipeServer()
        => new(
            _pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private void StopWindowOpenListener()
    {
        CancellationTokenSource? cancellation;
        Task? listenerTask;

        lock (_listenerSync)
        {
            cancellation = _listenerCancellation;
            listenerTask = _listenerTask;
            _listenerCancellation = null;
            _listenerTask = null;
        }

        if (cancellation is null)
            return;

        cancellation.Cancel();
        try
        {
            listenerTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private static string BuildMutexName(string instanceName)
        => $@"Local\{instanceName}.Mutex";

    private static string BuildPipeName(string instanceName)
    {
        using var currentProcess = Process.GetCurrentProcess();
        return $"{instanceName}.Session{currentProcess.SessionId}.WindowOpenPipe";
    }

    private sealed class ListenerRegistration(SingleInstanceCoordinator owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.StopWindowOpenListener();
        }
    }
}
