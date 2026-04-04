using FluentAssertions;
using PrinterApp.Models;

namespace backend.Tests;

public class WatermarkOptionsTests
{
    [Fact]
    public void WatermarkOptions_DefaultValues_AreCorrect()
    {
        var opts = new WatermarkOptions();

        opts.Text.Should().Be("DRAFT");
        opts.FontSize.Should().Be(48);
        opts.Opacity.Should().Be(30);
        opts.Color.Should().Be("#94a3b8");
    }

    [Fact]
    public void WatermarkOptions_CanOverrideText()
    {
        var opts = new WatermarkOptions { Text = "CONFIDENTIAL" };

        opts.Text.Should().Be("CONFIDENTIAL");
    }

    [Fact]
    public void PrinterInfo_DefaultValues()
    {
        var info = new PrinterInfo();

        info.SupportsColor.Should().BeFalse();
        info.PortName.Should().Be("");
    }

    [Fact]
    public void PrinterInfo_CanSetProperties()
    {
        var info = new PrinterInfo
        {
            SupportsColor = true,
            PortName = "USB001"
        };

        info.SupportsColor.Should().BeTrue();
        info.PortName.Should().Be("USB001");
    }

    [Fact]
    public void PrinterInfo_DefaultName_IsEmpty()
    {
        var info = new PrinterInfo();
        info.Name.Should().Be("");
    }

    [Fact]
    public void PrintJobState_DefaultJobId_IsNotEmpty()
    {
        var state = new PrintJobState();
        state.JobId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PrintJobState_TwoInstances_HaveDifferentJobIds()
    {
        var state1 = new PrintJobState();
        var state2 = new PrintJobState();
        state1.JobId.Should().NotBe(state2.JobId);
    }

    [Fact]
    public void WatermarkOptions_CanOverrideAllProperties()
    {
        var opts = new WatermarkOptions
        {
            Text = "TOP SECRET",
            FontSize = 72,
            Opacity = 50,
            Color = "#ff0000"
        };

        opts.Text.Should().Be("TOP SECRET");
        opts.FontSize.Should().Be(72);
        opts.Opacity.Should().Be(50);
        opts.Color.Should().Be("#ff0000");
    }
}
