using FluentAssertions;

namespace backend.Tests;

public sealed class PrintCopySequencingSourceTests
{
    private static readonly string BackendStartupSource =
        File.ReadAllText(Path.Combine("..", "..", "..", "..", "backend", "BackendStartup.cs"));

    [Fact]
    public void PrintEndpoint_DoesNotLoopOrDelayBetweenCopies()
    {
        var endpoint = Extract(
            BackendStartupSource,
            "app.MapPost(\"/api/print\"",
            "app.MapGet(\"/api/print/recovery-context\"");

        endpoint.Should().NotContain("Task.Delay(2000)");

        var manualBranch = Extract(
            endpoint,
            "// Store before Phase 1",
            "return Results.Ok");
        manualBranch.Should().NotMatchRegex(@"for\s*\(\s*int\s+copy\s*=");
        manualBranch.Should().Contain("printAlgorithm.ExecutePrintJob(jobState, firstPhase: true);");
    }

    [Fact]
    public void ContinueEndpoint_DoesNotLoopOrDelayBetweenCopies()
    {
        var endpoint = Extract(
            BackendStartupSource,
            "app.MapPost(\"/api/print/continue\"",
            "app.MapPost(\"/api/print/recover/back/start\"");

        endpoint.Should().NotMatchRegex(@"for\s*\(\s*int\s+copy\s*=");
        endpoint.Should().NotContain("Task.Delay(2000)");
    }

    private static string Extract(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"source should contain {startMarker}");

        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"source should contain {endMarker} after {startMarker}");

        return source[start..end];
    }
}
