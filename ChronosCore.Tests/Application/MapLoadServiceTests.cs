using Chronos.Core.Application;
using Chronos.Core.Domain.Map;
using Chronos.Core.Tests.Doubles;
using FluentAssertions;
using Xunit;

namespace Chronos.Core.Tests.Application;

public sealed class MapLoadServiceTests
{
    private readonly MemoryFileSystem _fs  = new();
    private readonly SpyLogger        _log = new();

    private MapLoadService BuildSut() =>
        new(_fs, _log, new MapAssetPaths("res://"));

    [Fact]
    public void LoadFromPath_FileNotFound_ReturnsError()
    {
        var result = BuildSut().LoadFromPath("res://maps/99.bin", 99, 1);

        result.IsOk.Should().BeFalse();
        result.Error.Should().Be(MapLoadError.FileNotFound);
        _log.ErrorMessages.Should().NotBeEmpty();
    }

    [Fact]
    public void LoadFromPath_ValidFile_ReturnsLoadedMap()
    {
        _fs.AddFile("res://maps/1.bin", new byte[] { 0, 5, 0, 3 });

        var result = BuildSut().LoadFromPath("res://maps/1.bin", 1, 2);

        result.IsOk.Should().BeTrue();
        result.Value.TileWidth.Should().Be(5);
        result.Value.TileHeight.Should().Be(3);
    }
}
