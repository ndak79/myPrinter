using System.Collections.Generic;
using FluentAssertions;
using PrinterApp.Models;
using Xunit;

namespace backend.Tests;

public class PrintRequestModelTests
{
    [Fact]
    public void RotationDirection_Values_AreCorrect()
    {
        ((int)RotationDirection.None).Should().Be(0);
        ((int)RotationDirection.CW90).Should().Be(90);
        ((int)RotationDirection.CCW90).Should().Be(270);
        ((int)RotationDirection.Rotate180).Should().Be(180);
        ((int)RotationDirection.FlipHorizontal).Should().Be(-1);
        ((int)RotationDirection.FlipVertical).Should().Be(-2);
    }

    [Fact]
    public void PageRotation_DefaultRotation_IsNone()
    {
        var rotation = new PageRotation();
        rotation.Rotation.Should().Be(RotationDirection.None);
    }

    [Fact]
    public void PrintRequest_DefaultPageOrder_IsNull()
    {
        var request = new PrintRequest();
        request.PageOrder.Should().BeNull();
    }

    [Fact]
    public void PrintRequest_DefaultPageRotations_IsNull()
    {
        var request = new PrintRequest();
        request.PageRotations.Should().BeNull();
    }

    [Fact]
    public void PrintRequest_CanSetPageOrder()
    {
        var request = new PrintRequest
        {
            PageOrder = new[] { 3, 1, 2 }
        };
        request.PageOrder.Should().BeEquivalentTo(new[] { 3, 1, 2 }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void PrintRequest_CanSetPageRotations()
    {
        var request = new PrintRequest
        {
            PageRotations = new List<PageRotation>
            {
                new PageRotation { PageNumber = 1, Rotation = RotationDirection.CW90 }
            }
        };
        request.PageRotations.Should().ContainSingle();
        request.PageRotations[0].PageNumber.Should().Be(1);
        request.PageRotations[0].Rotation.Should().Be(RotationDirection.CW90);
    }
}
