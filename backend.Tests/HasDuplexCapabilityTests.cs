using FluentAssertions;
using PrinterApp.Services;

namespace backend.Tests;

public class HasDuplexCapabilityTests
{
    private readonly PrinterManagementService _sut = new();

    [Fact]
    public void HasDuplexCapability_NullCapabilities_ReturnsFalse()
    {
        var result = _sut.HasDuplexCapability("SomePrinter", null);
        result.Should().BeFalse();
    }

    [Fact]
    public void HasDuplexCapability_EmptyCapabilities_ReturnsFalse()
    {
        var result = _sut.HasDuplexCapability("SomePrinter", Array.Empty<UInt16>());
        result.Should().BeFalse();
    }

    [Fact]
    public void HasDuplexCapability_Code3_ReturnsTrue()
    {
        var result = _sut.HasDuplexCapability("SomePrinter", new UInt16[] { 3 });
        result.Should().BeTrue();
    }

    [Fact]
    public void HasDuplexCapability_Code4_ReturnsTrue()
    {
        var result = _sut.HasDuplexCapability("SomePrinter", new UInt16[] { 4 });
        result.Should().BeTrue();
    }

    [Fact]
    public void HasDuplexCapability_Codes5And6_ReturnsFalse()
    {
        var result = _sut.HasDuplexCapability("SomePrinter", new UInt16[] { 5, 6 });
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Canon LBP2900")]
    [InlineData("Canon LBP 2900")]
    public void HasDuplexCapability_KnownSingleSidedCanon_ReturnsFalse(string name)
    {
        var result = _sut.HasDuplexCapability(name, new UInt16[] { 3 });
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("HP Laser 107")]
    [InlineData("HP Laser 108")]
    public void HasDuplexCapability_KnownSingleSidedHP_ReturnsFalse(string name)
    {
        var result = _sut.HasDuplexCapability(name, new UInt16[] { 3 });
        result.Should().BeFalse();
    }

    [Fact]
    public void HasDuplexCapability_BrotherDCP_WithCode3_ReturnsTrue()
    {
        var result = _sut.HasDuplexCapability("Brother DCP-L2520D", new UInt16[] { 3 });
        result.Should().BeTrue();
    }

    [Fact]
    public void HasDuplexCapability_EpsonET2850_WithCodes3And5_ReturnsTrue()
    {
        var result = _sut.HasDuplexCapability("EPSON ET-2850", new UInt16[] { 3, 5 });
        result.Should().BeTrue();
    }

    [Fact]
    public void HasDuplexCapability_NullCapabilities_KnownSingleSided_ReturnsFalse()
    {
        var result = _sut.HasDuplexCapability("Canon LBP2900", null);
        result.Should().BeFalse();
    }
}
