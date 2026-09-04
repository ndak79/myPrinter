namespace MyPrinter.Desktop;

internal sealed class WindowOpenRequestRouter
{
    private readonly object _sync = new();
    private Action? _handler;
    private bool _requestPending;

    public void RequestWindowOpen()
    {
        Action? handler;
        lock (_sync)
        {
            handler = _handler;
            if (handler is null)
            {
                _requestPending = true;
                return;
            }
        }

        handler();
    }

    public void Attach(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var replayPendingRequest = false;
        lock (_sync)
        {
            if (_handler is not null)
                throw new InvalidOperationException("A window-open handler has already been attached.");

            _handler = handler;
            replayPendingRequest = _requestPending;
            _requestPending = false;
        }

        if (replayPendingRequest)
            handler();
    }
}
