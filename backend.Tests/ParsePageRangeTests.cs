using FluentAssertions;
using Moq;
using PrinterApp.Services;

namespace backend.Tests;

public class ParsePageRangeTests
{
    private readonly PrintAlgorithmService _sut;

    public ParsePageRangeTests()
    {
        var mockWordService = new Mock<IWordInteropService>();
        _sut = new PrintAlgorithmService(mockWordService.Object);
    }

    [Fact]
    public void ParsePageRange_SimpleRange_ReturnsCorrectPages()
    {
        var result = _sut.ParsePageRange("1-3", 5);
        result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void ParsePageRange_CommaSeparated_ReturnsCorrectPages()
    {
        var result = _sut.ParsePageRange("2,4,6", 6);
        result.Should().BeEquivalentTo(new[] { 2, 4, 6 });
    }

    [Fact]
    public void ParsePageRange_ReversedRange_ReturnsAscending()
    {
        var result = _sut.ParsePageRange("3-1", 5);
        result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public void ParsePageRange_RangeExceedsTotalPages_ClampedToMax()
    {
        var result = _sut.ParsePageRange("1-10", 5);
        result.Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void ParsePageRange_OutOfBounds_ReturnsEmpty()
    {
        var result = _sut.ParsePageRange("0,99", 5);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParsePageRange_InvalidText_ReturnsEmpty()
    {
        var result = _sut.ParsePageRange("abc", 5);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParsePageRange_Duplicates_ReturnsDistinct()
    {
        var result = _sut.ParsePageRange("1,1,2", 5);
        result.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public void ParsePageRange_SinglePage_ReturnsSingleElement()
    {
        var result = _sut.ParsePageRange("5", 5);
        result.Should().BeEquivalentTo(new[] { 5 });
    }

    [Fact]
    public void ParsePageRange_SinglePageOverMax_ReturnsEmpty()
    {
        var result = _sut.ParsePageRange("6", 5);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParsePageRange_MixedRangeAndSingle_ReturnsAll()
    {
        var result = _sut.ParsePageRange("1-3,5", 5);
        result.Should().BeEquivalentTo(new[] { 1, 2, 3, 5 });
    }

    [Fact]
    public void ParsePageRange_WhitespaceInRange_HandlesGracefully()
    {
        var result = _sut.ParsePageRange(" 1 - 3 , 5 ", 5);
        result.Should().BeEquivalentTo(new[] { 1, 2, 3, 5 });
    }
}
