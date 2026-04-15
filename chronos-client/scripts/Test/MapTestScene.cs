using Godot;
using Chronos.Core.Domain.Map;
using Map;

public partial class MapTestScene : Node2D
{
    [Export] public NodePath RendererPath;
    private MapRenderer _renderer;

    public async override void _Ready()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _renderer = GetNode<MapRenderer>(RendererPath);
        LoadMapTest();
    }

    private void LoadMapTest()
    {
        byte[] mapBytes = Convert.ToMapBytes(MapTestData.Layout);
        var map = new TileMapData();
        map.LoadFromBytes(mapBytes, mapId: 999, tileSetId:1);
        map.BuildTypes(new TileTypeRule[]
        {
            new(new[]{ 39 }, TileMapData.TypeOutside),
        });

        int startX = 10 * TileMapData.TileSize; // 320 px
        int startY =  6 * TileMapData.TileSize; // 192 px
        _renderer.LoadMap(map, startX, startY);


    }
}