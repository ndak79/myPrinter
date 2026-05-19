namespace MyPrinter.Desktop;

public sealed class RuntimeLicenseMonitor : IDisposable
{
    private readonly Func<bool> _isActivated;
    private readonly TimeSpan _interval;
    private readonly Action _onInvalidated;
    private readonly System.Threading.Timer _timer;

    private int _isChecking;
    private int _hasInvalidated;
    private bool _disposed;

    public RuntimeLicenseMonitor(Func<bool> isActivated, TimeSpan interval, Action onInvalidated)
    {
        ArgumentNullException.ThrowIfNull(isActivated);
        ArgumentNullException.ThrowIfNull(onInvalidated);

        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");

        _isActivated = isActivated;
        _interval = interval;
        _onInvalidated = onInvalidated;
        _timer = new System.Threading.Timer(_ =>
        {
            if (!_disposed)
                _ = CheckNowAsync();
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer.Change(_interval, _interval);
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Restart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Exchange(ref _hasInvalidated, 0);
        Start();
    }

    public async Task CheckNowAsync()
    {
        if (_disposed)
            return;

        if (Volatile.Read(ref _hasInvalidated) == 1)
            return;
        if (Interlocked.Exchange(ref _isChecking, 1) == 1)
            return;

        try
        {
            var isActivated = await Task.Run(_isActivated).ConfigureAwait(false);
            if (!isActivated && Interlocked.Exchange(ref _hasInvalidated, 1) == 0)
            {
                Stop();
                _onInvalidated();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
    }
}
