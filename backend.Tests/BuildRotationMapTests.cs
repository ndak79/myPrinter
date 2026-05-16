using System;
using System.Collections.Generic;
using FluentAssertions;
using PrinterApp.Models;
using PrinterApp.Services;
using Xunit;

namespace backend.Tests;

public class BuildRotationMapTests
{
    [Fact]
    public void BuildRotationMap_NullInput_ReturnsEmpty()
    {
        var result = PrintAlgorithmService.BuildRotationMap(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildRotationMap_EmptyList_ReturnsEmpty()
    {
        var result = PrintAlgorithmService.BuildRotationMap(new List<PageRotation>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildRotationMap_SingleRotation_ReturnsSingleEntry()
    {
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.CW90 }
        };
        var result = PrintAlgorithmService.BuildRotationMap(rotations);
        result.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new KeyValuePair<int, RotationDirection>(1, RotationDirection.CW90));
    }

    [Fact]
    public void BuildRotationMap_MultipleRotations_ReturnsAll()
    {
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.CW90 },
            new PageRotation { PageNumber = 3, Rotation = RotationDirection.Rotate180 }
        };
        var result = PrintAlgorithmService.BuildRotationMap(rotations);
        result.Should().HaveCount(2);
        result[1].Should().Be(RotationDirection.CW90);
        result[3].Should().Be(RotationDirection.Rotate180);
    }

    [Fact]
    public void BuildRotationMap_NoneRotation_IsFiltered()
    {
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.CW90 },
            new PageRotation { PageNumber = 2, Rotation = RotationDirection.None }
        };
        var result = PrintAlgorithmService.BuildRotationMap(rotations);
        result.Should().ContainSingle();
        result.Should().ContainKey(1);
        result.Should().NotContainKey(2);
    }

    [Fact]
    public void BuildRotationMap_DuplicatePageNumbers_LastWins()
    {
        var rotations = new List<PageRotation>
        {
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.CW90 },
            new PageRotation { PageNumber = 1, Rotation = RotationDirection.Rotate180 }
        };

        var result = PrintAlgorithmService.BuildRotationMap(rotations);

        result.Should().ContainSingle();
        result[1].Should().Be(RotationDirection.Rotate180);
    }

    [Fact]
    public void RemapRotations_EmptyMap_ReturnsEmpty()
    {
        var map = new Dictionary<int, RotationDirection>();
        var selectedPages = new[] { 1, 2, 3 };
        var result = PrintAlgorithmService.RemapRotations(map, selectedPages);
        result.Should().BeEmpty();
    }

    [Fact]
    public void RemapRotations_RemapsCorrectly()
    {
        var map = new Dictionary<int, RotationDirection>
        {
            { 3, RotationDirection.CW90 },
            { 7, RotationDirection.Rotate180 }
        };
        var selectedPages = new[] { 3, 5, 7 };
        var result = PrintAlgorithmService.RemapRotations(map, selectedPages);
        
        // 3 is at index 0 -> 1-based index 1
        // 7 is at index 2 -> 1-based index 3
        result.Should().HaveCount(2);
        result[1].Should().Be(RotationDirection.CW90);
        result[3].Should().Be(RotationDirection.Rotate180);
    }

    [Fact]
    public void RemapRotations_UnmatchedOriginalPage_IsDropped()
    {
        var map = new Dictionary<int, RotationDirection>
        {
            { 10, RotationDirection.CW90 }
        };
        var selectedPages = new[] { 1, 2, 3 };
        var result = PrintAlgorithmService.RemapRotations(map, selectedPages);
        result.Should().BeEmpty();
    }

    [Fact]
    public void RemapRotations_PreservesRotationDirection()
    {
        var map = new Dictionary<int, RotationDirection>
        {
            { 2, RotationDirection.FlipHorizontal }
        };
        var selectedPages = new[] { 1, 2 };
        var result = PrintAlgorithmService.RemapRotations(map, selectedPages);
        result[2].Should().Be(RotationDirection.FlipHorizontal);
    }
}
