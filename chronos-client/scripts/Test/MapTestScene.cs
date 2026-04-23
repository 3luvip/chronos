using Godot;
using Chronos.Core.Domain.Map;
using Map;
using Chronos.Core.Domain.Character;
using Player;

public partial class MapTestScene : Node2D
{
    private MapRenderer      _renderer;
    private CharacterNode    _localPlayer;
    private CharacterManager _charManager;

    public override void _Ready()
    {
        CreateNodes();
        LoadMapTest();
        LoadBackgroundItems();
        SpawnOfflinePlayer();
    }

    public override void _Process(double delta)
    {
        if (_localPlayer == null || !_renderer.IsMapLoaded) return;
        HandleInput(delta);
        _renderer.UpdateCameraTarget(
            _localPlayer.Model.WorldX,
            _localPlayer.Model.WorldY,
            _localPlayer.Model.Direction);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse &&
            mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            var mp = GetGlobalMousePosition();
            GD.Print($"World: ({_renderer.Camera.PositionX + (int)mp.X}, " +
                     $"{_renderer.Camera.PositionY + (int)mp.Y})");
        }
    }

    // ── Nodes ─────────────────────────────────────────────────────────────────

    private void CreateNodes()
    {
        _renderer = new MapRenderer();
        _renderer.Name             = "MapRenderer";
        _renderer.ResourceBasePath = "res://";
        _renderer.EnableLightLayer = false;
        _renderer.EnableWaterLayer = false;
        AddChild(_renderer);

        var atlas    = new SpriteAtlasLoader();
        var registry = BuildTestRegistry();

        _charManager = new CharacterManager(_renderer, atlas, registry);
        AddChild(_charManager);
    }

    // ── Map ───────────────────────────────────────────────────────────────────

    private void LoadMapTest()
    {
        byte[] mapBytes = Convert.ToMapBytes(MapTestData.Layout);
        var map = new TileMapData();
        map.LoadFromBytes(mapBytes, mapId: 999, tileSetId: 1);
        map.BuildTypes(new TileTypeRule[]
        {
            new(new[] { 39 },            TileMapData.TypeOutside),
            new(new[] { 2, 11, 10, 12 }, TileMapData.TypeSolidGround | TileMapData.TypeTop),
            new(new[] { 6 },             TileMapData.TypeSolidGround),
        });
        _renderer.LoadMap(map, 0, 16 * TileMapData.TileSize);
        _renderer.Camera.SetInstant(30, 0);
    }

    private void LoadBackgroundItems()
    {
        _renderer.AddBackgroundItems(MapTestData.backgroundItems);
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    private void SpawnOfflinePlayer()
    {
        _localPlayer = _charManager.Spawn(
            charId:     1,
            worldX:     200,
            worldY:     480,
            headPartId: 1,
            bodyPartId: 2,
            legPartId:  3);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private const int MOVE_SPEED = 120;

    private void HandleInput(double delta)
    {
        var model = _localPlayer.Model;
        int step  = (int)(MOVE_SPEED * delta);

        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D))
        { model.WorldX += step; model.Direction = 1;  model.Status = CharacterStatus.Run; }
        else if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A))
        { model.WorldX -= step; model.Direction = -1; model.Status = CharacterStatus.Run; }
        else
        { model.Status = CharacterStatus.Stand; }

        if (Input.IsKeyPressed(Key.Up) || Input.IsKeyPressed(Key.W))
            model.WorldY -= step;
        else if (Input.IsKeyPressed(Key.Down) || Input.IsKeyPressed(Key.S))
            model.WorldY += step;
    }

    // ── Registry ──────────────────────────────────────────────────────────────

    private static InMemoryPartRegistry BuildTestRegistry()
    {
        var reg = new InMemoryPartRegistry();

        // spriteId trỏ đến res://asset/character/SmallImage/Small{id}.png
        // PartOffset trong clip: (imageIndex, dx, dy)
        // imageIndex=0 → frame đầu tiên của part (idle thường dùng frame 0-3)

        reg.Register(new EquipmentPart(
            partId: 1, type: EquipmentPart.TYPE_HEAD,
            images: new[] {
                new PartImageEntry(1, 0, 0),
                new PartImageEntry(1, 0, 0)
            }));

        reg.Register(new EquipmentPart(
            partId: 2, type: EquipmentPart.TYPE_BODY,
            images: new[] {
                new PartImageEntry(3, -9, 16),
                new PartImageEntry(3, -9, 16),
            }));

        reg.Register(new EquipmentPart(
            partId: 3, type: EquipmentPart.TYPE_LEG,
            images: new[] {
                new PartImageEntry(11,  -8, 10),
                new PartImageEntry(11, -8, 10),

            }));

        return reg;
    }
}