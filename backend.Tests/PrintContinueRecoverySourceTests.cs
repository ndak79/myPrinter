using FluentAssertions;
using System.Text.RegularExpressions;

namespace backend.Tests;

public class PrintContinueRecoverySourceTests
{
    [Fact]
    public void ContinueEndpoint_PreservesClaimedJob_WhenPhase2Fails()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "backend",
            "BackendStartup.cs"));

        var match = Regex.Match(
            source,
            @"app\.MapPost\(""/api/print/continue""[\s\S]*?app\.MapPost\(""/api/print/recover/back/start""");
        match.Success.Should().BeTrue();

        var continueEndpoint = match.Value;

        continueEndpoint.Should().Contain("void RestoreClaimedJobForRecovery()");
        continueEndpoint.Should().Contain("sessions.AddJob(jobState.JobId, jobState)");
        continueEndpoint.Should().MatchRegex(@"if \(!jobState\.WaitingForFlip\)\s*\{\s*RestoreClaimedJobForRecovery\(\);\s*return Results\.BadRequest");
        continueEndpoint.Should().MatchRegex(@"catch \(InvalidOperationException ex\)[\s\S]*RestoreClaimedJobForRecovery\(\);");
        continueEndpoint.Should().MatchRegex(@"catch \(Exception ex\)[\s\S]*RestoreClaimedJobForRecovery\(\);");
        continueEndpoint.Should().NotContain("FileSessionService.DeleteIntermediateFiles(jobState)");
    }
}
