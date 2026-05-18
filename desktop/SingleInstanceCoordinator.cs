namespace MyPrinter.Desktop;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string DefaultInstanceName = "MyPrinter.Desktop.SingleInstance";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceCoordinator(string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);

        _mutex = new Mutex(false, BuildObjectName(instanceName, "Mutex"));
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            BuildObjectName(instanceName, "Activation"));
    }

    public bool IsPrimary => _ownsMutex;

    public static SingleInstanceCoordinator Create()
        => Create(DefaultInstanceName);

    public static SingleInstanceCoordinator Create(string instanceName)
        => new(instanceName);

    public IDisposable StartActivationListener(Action activationRequested)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(activationRequested);

        if (!IsPrimary)
            throw new InvalidOperationException("Only the primary instance can listen for activation requests.");

        _activationWait ??= ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                    activationRequested();
            },
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);

        return new ListenerRegistration(this);
    }

    public void SignalExistingInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activationEvent.Set();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _activationWait?.Unregister(null);
        _activationWait = null;

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _activationEvent.Dispose();
        _mutex.Dispose();
    }

    private static string BuildObjectName(string instanceName, string suffix)
        => $@"Local\{instanceName}.{suffix}";

    private sealed class ListenerRegistration(SingleInstanceCoordinator owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._activationWait?.Unregister(null);
                owner._activationWait = null;
            }
        }
    }
}
