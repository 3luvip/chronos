using Chronos.Core.Domain.Map;
using FluentAssertions;
using Xunit;

namespace Chronos.Core.Tests.Domain;

public sealed class TileMapDataTests
{
    [Fact]
    public void LoadFromBytes_ValidData_SetsCorrectDimensions()
    {
        var map = new TileMapData();
        // Width=5, Height=3, 15 tile bytes
        var data = new byte[] { 0, 5, 0, 3, 1,2,3,4,5,6,7,8,9,10,11,12,13,14,15 };

        map.LoadFromBytes(data, mapId: 1, tileSetId: 2);

        map.TileWidth.Should().Be(5);
        map.TileHeight.Should().Be(3);
        map.MapId.Should().Be(1);
    }

    [Fact]
    public void LoadFromBytes_TooShort_ThrowsArgumentException()
    {
        var map = new TileMapData();
        var act = () => map.LoadFromBytes(new byte[] { 0, 1 }, 1, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TileTypeAt_OutOfBounds_Returns1000()
    {
        var map = new TileMapData();
        map.LoadFromBytes(new byte[] { 0, 2, 0, 2, 0, 0, 0, 0 }, 1, 1);

        map.TileTypeAt(99, 99).Should().Be(1000);
    }
}
