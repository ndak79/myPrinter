using FluentAssertions;
using MyPrinter.Desktop;

namespace desktop.Tests;

public class WindowOpenRequestRouterTests
{
    [Fact]
    public void Request_before_window_is_ready_is_replayed_once_when_handler_attaches()
    {
        var router = new WindowOpenRequestRouter();
        var openCount = 0;

        router.RequestWindowOpen();
        router.RequestWindowOpen();
        router.Attach(() => openCount++);

        openCount.Should().Be(1);
    }

    [Fact]
    public void Requests_after_window_is_ready_are_forwarded_immediately()
    {
        var router = new WindowOpenRequestRouter();
        var openCount = 0;
        router.Attach(() => openCount++);

        router.RequestWindowOpen();
        router.RequestWindowOpen();

        openCount.Should().Be(2);
    }
}
