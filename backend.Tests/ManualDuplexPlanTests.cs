using FluentAssertions;
using PrinterApp.Models;

namespace backend.Tests;

public class ManualDuplexPlanTests
{
    private static List<ManualDuplexPageInfo> MakePages(int count, bool landscape = false) =>
        Enumerable.Range(1, count)
            .Select(i => new ManualDuplexPageInfo
            {
                ProcessedIndex = i,
                OriginalPageNumber = i,
                IsLandscape = landscape
            })
            .ToList();

    [Fact]
    public void Build_2Pages_FaceDown_CorrectPhases()
    {
        var pages = MakePages(2);
        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages, faceDownStack: true);

        plan.Phase1Pages.Should().BeEquivalentTo(new[] { 1 }, opts => opts.WithStrictOrdering());
        plan.Phase2Pages.Should().BeEquivalentTo(new[] { 2 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Build_4Pages_FaceDown_CorrectPhases()
    {
        var pages = MakePages(4);
        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages, faceDownStack: true);

        plan.Phase1Pages.Should().BeEquivalentTo(new[] { 1, 3 }, opts => opts.WithStrictOrdering());
        plan.Phase2Pages.Should().BeEquivalentTo(new[] { 4, 2 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Build_4Pages_NoFaceDown_CorrectPhases()
    {
        var pages = MakePages(4);
        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages, faceDownStack: false);

        plan.Phase1Pages.Should().BeEquivalentTo(new[] { 1, 3 }, opts => opts.WithStrictOrdering());
        plan.Phase2Pages.Should().BeEquivalentTo(new[] { 2, 4 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Build_6Pages_FaceDown_CorrectPhases()
    {
        var pages = MakePages(6);
        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages, faceDownStack: true);

        plan.Phase1Pages.Should().BeEquivalentTo(new[] { 1, 3, 5 }, opts => opts.WithStrictOrdering());
        plan.Phase2Pages.Should().BeEquivalentTo(new[] { 6, 4, 2 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void Build_4Pages_SheetCount_IsHalfOfPages()
    {
        var pages = MakePages(4);
        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages);

        plan.Sheets.Should().HaveCount(2);
    }

    [Fact]
    public void Build_4Pages_FirstSheet_HasCorrectFrontBack()
    {
        var pages = MakePages(4);
        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages);

        plan.Sheets[0].Front.ProcessedIndex.Should().Be(1);
        plan.Sheets[0].Back.ProcessedIndex.Should().Be(2);
    }

    [Fact]
    public void Build_EmptyPages_ThrowsArgumentException()
    {
        var pages = new List<ManualDuplexPageInfo>();
        var act = () => ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_NullPages_ThrowsArgumentNullException()
    {
        var act = () => ManualDuplexPlan.Build(@"C:\fake\processed.pdf", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ResetsProcessedIndex_To1BasedSequence()
    {
        var pages = new List<ManualDuplexPageInfo>
        {
            new() { ProcessedIndex = 99, OriginalPageNumber = 1, IsLandscape = false },
            new() { ProcessedIndex = 100, OriginalPageNumber = 2, IsLandscape = false },
        };

        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages);

        plan.ProcessedPages[0].ProcessedIndex.Should().Be(1);
        plan.ProcessedPages[1].ProcessedIndex.Should().Be(2);
    }

    [Fact]
    public void Build_SetsProcessedPdfPath()
    {
        var pages = MakePages(2);
        var plan = ManualDuplexPlan.Build(@"C:\fake\processed.pdf", pages);

        plan.ProcessedPdfPath.Should().Be(@"C:\fake\processed.pdf");
    }
}
