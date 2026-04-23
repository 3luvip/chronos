using Chronos.Core.Domain.Character;
using Godot;
using Map;
namespace Player
{
    // 3. CHARACTER NODE — root node, owns CharacterModel and CharacterRenderer
    //    Created purely by code (no scene file needed)
    // ─────────────────────────────────────────────────────────────────────────
 
    /// <summary>
    /// Root node for one character. Created in code:
    ///
    ///   var charNode = CharacterNode.Create(model, atlas, mapRenderer);
    ///   AddChild(charNode);
    /// </summary>
    public partial class CharacterNode : Node2D
    {
        public CharacterModel         Model    { get; private set; }
        
        public CharacterRendererNode  Renderer { get; private set; }
 
        public static CharacterNode Create(CharacterModel model,
                                           SpriteAtlasLoader atlas,
                                           MapRenderer mapRenderer,
                                           IPartRegistry registry)
        {
            var node     = new CharacterNode();
            node.Model   = model;
            node.Name    = $"Character_{model.Id}";
 
            var renderer = new CharacterRendererNode();
            renderer.Model   = model;
            renderer.Atlas   = atlas;
            renderer.MapRef  = mapRenderer;
            renderer.Registry = registry;
            renderer.Name    = "Renderer";
 
            node.AddChild(renderer);
            node.Renderer = renderer;
 
            return node;
        }
 
        public override void _Process(double delta)
        {
            // Keep GlobalPosition at origin — renderer uses raw screen coords
            GlobalPosition = Vector2.Zero;
        }
    }    
}