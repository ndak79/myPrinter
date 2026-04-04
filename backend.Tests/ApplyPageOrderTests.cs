using System;
using System.Linq;
using FluentAssertions;
using PrinterApp.Services;
using Xunit;

namespace backend.Tests;

public class ApplyPageOrderTests
{
    [Fact]
    public void ApplyPageOrder_NullPageOrder_ReturnsOriginal()
    {
        var selectedPages = new[] { 1, 2, 3 };
        var result = PrintAlgorithmService.ApplyPageOrder(selectedPages, null);
        result.Should().BeEquivalentTo(selectedPages, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_EmptyPageOrder_ReturnsOriginal()
    {
        var selectedPages = new[] { 1, 2, 3 };
        var result = PrintAlgorithmService.ApplyPageOrder(selectedPages, Array.Empty<int>());
        result.Should().BeEquivalentTo(selectedPages, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_ReordersSelectedPages()
    {
        var selectedPages = new[] { 1, 2, 3 };
        var pageOrder = new[] { 3, 1, 2 };
        var result = PrintAlgorithmService.ApplyPageOrder(selectedPages, pageOrder);
        result.Should().BeEquivalentTo(new[] { 3, 1, 2 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_FiltersOutUnselectedPages()
    {
        var selectedPages = new[] { 1, 3 };
        var pageOrder = new[] { 5, 3, 1 };
        var result = PrintAlgorithmService.ApplyPageOrder(selectedPages, pageOrder);
        result.Should().BeEquivalentTo(new[] { 3, 1 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_AppendsRemainingSelectedPages()
    {
        var selectedPages = new[] { 1, 2, 3 };
        var pageOrder = new[] { 2 };
        var result = PrintAlgorithmService.ApplyPageOrder(selectedPages, pageOrder);
        result.Should().BeEquivalentTo(new[] { 2, 1, 3 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_DuplicatesInPageOrder_HandledGracefully()
    {
        var selectedPages = new[] { 1, 2, 3 };
        var pageOrder = new[] { 2, 2, 1 };
        var result = PrintAlgorithmService.ApplyPageOrder(selectedPages, pageOrder);
        // The current implementation uses pageOrder.Where(p => selectedSet.Contains(p)).ToList()
        // So duplicates in pageOrder will be preserved if they are in selectedPages.
        // Then it appends remaining selected pages.
        // 2, 2, 1 -> remaining is 3. Result: 2, 2, 1, 3
        result.Should().BeEquivalentTo(new[] { 2, 2, 1, 3 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void ApplyPageOrder_PageOrderContainsOnlyUnselectedPages_ReturnsOriginal()
    {
        var selectedPages = new[] { 1, 2 };
        var pageOrder = new[] { 5, 6 };
        var result = PrintAlgorithmService.ApplyPageOrder(selectedPages, pageOrder);
        result.Should().BeEquivalentTo(new[] { 1, 2 }, options => options.WithStrictOrdering());
    }
}
