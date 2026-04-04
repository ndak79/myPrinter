using FluentAssertions;
using Moq;
using PrinterApp.Services;

namespace backend.Tests;

public class BookletOrderTests
{
    private readonly PrintAlgorithmService _sut;

    public BookletOrderTests()
    {
        var mockWordService = new Mock<IWordInteropService>();
        _sut = new PrintAlgorithmService(mockWordService.Object);
    }

    [Fact]
    public void CalculateBookletOrder_4Pages_ReturnsCorrectOrder()
    {
        var result = _sut.CalculateBookletOrder(4);
        result.Should().BeEquivalentTo(new[] { 4, 1, 2, 3 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void CalculateBookletOrder_8Pages_ReturnsCorrectOrder()
    {
        var result = _sut.CalculateBookletOrder(8);
        result.Should().BeEquivalentTo(new[] { 8, 1, 2, 7, 6, 3, 4, 5 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void CalculateBookletOrder_12Pages_FirstFourCorrect()
    {
        var result = _sut.CalculateBookletOrder(12);
        result.Take(4).Should().BeEquivalentTo(new[] { 12, 1, 2, 11 }, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void CalculateBookletOrder_12Pages_HasCorrectLength()
    {
        var result = _sut.CalculateBookletOrder(12);
        result.Should().HaveCount(12);
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 12)]
    public void RoundUpToMultipleOf4_ReturnsCorrectValue(int input, int expected)
    {
        var result = _sut.RoundUpToMultipleOf4(input);
        result.Should().Be(expected);
    }
}
