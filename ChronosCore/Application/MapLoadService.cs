using System;
using Chronos.Core.Common;
using Chronos.Core.Contracts;
using Chronos.Core.Domain.Map;

namespace Chronos.Core.Application;

public enum MapLoadError
{
    FileNotFound,
    InvalidFormat,
    DimensionTooLarge,
    AssetLoadFailed,
}

public sealed class MapLoadService
{
    private readonly IFileSystem   _fs;
    private readonly ILogger       _log;
    private readonly MapAssetPaths _paths;

    public MapLoadService(IFileSystem fs, ILogger log, MapAssetPaths paths)
    {
        _fs    = fs;
        _log   = log;
        _paths = paths;
    }

    public Result<TileMapData, MapLoadError> LoadFromPath(
        string resourcePath, int mapId, int tileSetId)
    {
        if (!_fs.Exists(resourcePath))
        {
            _log.Error($"Map file not found: {resourcePath}");
            return Result<TileMapData, MapLoadError>.Fail(MapLoadError.FileNotFound);
        }

        byte[] data;
        try { data = _fs.ReadAllBytes(resourcePath); }
        catch (Exception ex)
        {
            _log.Error($"Failed reading map: {resourcePath}", ex);
            return Result<TileMapData, MapLoadError>.Fail(MapLoadError.FileNotFound);
        }

        return LoadFromBytes(data, mapId, tileSetId);
    }

    public Result<TileMapData, MapLoadError> LoadFromBytes(
        byte[] data, int mapId, int tileSetId)
    {
        var map = new TileMapData();
        try
        {
            map.LoadFromBytes(data, mapId, tileSetId);
        }
        catch (ArgumentException ex)
        {
            _log.Error($"Invalid map data: {ex.Message}");
            return ex.Message.Contains("dimensions")
                ? Result<TileMapData, MapLoadError>.Fail(MapLoadError.DimensionTooLarge)
                : Result<TileMapData, MapLoadError>.Fail(MapLoadError.InvalidFormat);
        }

        _log.Info($"Loaded map {mapId} ({map.TileWidth}×{map.TileHeight}) tileSet={tileSetId}");
        return Result<TileMapData, MapLoadError>.Ok(map);
    }
}
