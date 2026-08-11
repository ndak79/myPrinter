namespace MyPrinter.Desktop;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string DefaultInstanceName = "MyPrinter.Desktop.SingleInstance";

    private readonly Mutex _mutex;
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
    }

    public bool IsPrimary => _ownsMutex;

    public static SingleInstanceCoordinator Create()
        => Create(DefaultInstanceName);

    public static SingleInstanceCoordinator Create(string instanceName)
        => new(instanceName);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }

    private static string BuildObjectName(string instanceName, string suffix)
        => $@"Local\{instanceName}.{suffix}";
}
