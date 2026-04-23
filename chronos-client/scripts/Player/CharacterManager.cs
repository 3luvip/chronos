using Godot;
using System;
using System.Collections.Generic;
using Map;
using Chronos.Core.Domain.Character;
namespace Player
{
    // ─────────────────────────────────────────────────────────────────────────
    // 5. CHARACTER MANAGER — scene-level manager for all characters
    // ─────────────────────────────────────────────────────────────────────────
 
    /// <summary>
    /// Manages the full set of characters in the current scene.
    /// Handles: spawn, despawn, update-from-server, render dispatch.
    /// Replaces the global array of Char objects in GameScr.
    ///
    /// Create by code from MapTestScene (or equivalent):
    ///   var mgr = new CharacterManager(mapRenderer, atlas);
    ///   AddChild(mgr);
    /// </summary>
    public partial class CharacterManager : Node2D
    {
        private readonly Dictionary<uint, CharacterNode> _characters = new();
        private readonly MapRenderer      _mapRenderer;
        private readonly SpriteAtlasLoader _atlas;
        private readonly InMemoryPartRegistry _partRegistry;
 
        public CharacterManager(MapRenderer mapRenderer, SpriteAtlasLoader atlas,
                                InMemoryPartRegistry partRegistry)
        {
            _mapRenderer  = mapRenderer;
            _atlas        = atlas;
            _partRegistry = partRegistry;
            Name          = "CharacterManager";
        }
 
        // ── Spawn / despawn ───────────────────────────────────────────────────
 
        public CharacterNode Spawn(uint charId, int worldX, int worldY,
                                   int headPartId, int bodyPartId, int legPartId)
        {
            if (_characters.ContainsKey(charId))
            {
                GD.PrintErr($"[CharacterManager] Character {charId} already exists.");
                return _characters[charId];
            }
 
            var model = new CharacterModel(charId)
            {
                WorldX = worldX,
                WorldY = worldY,
                Hp     = 100,
                MaxHp  = 100,
            };
            model.Loadout.HeadPartId = headPartId;
            model.Loadout.BodyPartId = bodyPartId;
            model.Loadout.LegPartId  = legPartId;
 
            RegisterDefaultClips(model);
 
            var node = CharacterNode.Create(model, _atlas, _mapRenderer, _partRegistry);
            AddChild(node);
            _characters[charId] = node;
 
            GD.Print($"[CharacterManager] Spawned char {charId} at ({worldX},{worldY})");
            return node;
        }
 
        public void Despawn(uint charId)
        {
            if (!_characters.TryGetValue(charId, out var node)) return;
            node.QueueFree();
            _characters.Remove(charId);
        }
 
        // ── Server sync ───────────────────────────────────────────────────────
 
        public void UpdateFromServer(uint charId, int worldX, int worldY,
                                     int direction, CharacterStatus status)
        {
            if (!_characters.TryGetValue(charId, out var node)) return;
            node.Model.WorldX    = worldX;
            node.Model.WorldY    = worldY;
            node.Model.Direction = direction;
            node.Model.Status    = status;
        }
 
        public void EquipItem(uint charId, int slot, int partId)
        {
            if (!_characters.TryGetValue(charId, out var node)) return;
            var loadout = node.Model.Loadout;
            switch (slot)
            {
                case EquipmentPart.TYPE_HEAD: loadout.HeadPartId   = partId; break;
                case EquipmentPart.TYPE_BODY: loadout.BodyPartId   = partId; break;
                case EquipmentPart.TYPE_LEG:  loadout.LegPartId    = partId; break;
                case EquipmentPart.TYPE_BAG:  loadout.BagPartId    = partId; break;
            }
        }
 
        // ── Default animation setup ───────────────────────────────────────────
 
        private static void RegisterDefaultClips(CharacterModel model)
        {
            // In production, clip data comes from AnimationClipRegistry.FromLegacyCharInfo()
            // Using placeholder clips for now
            RegisterPlaceholderClip(model, CharacterAnimState.Idle,   8f,  true,  8);
            RegisterPlaceholderClip(model, CharacterAnimState.Run,    10f, true,  4);
            RegisterPlaceholderClip(model, CharacterAnimState.Jump,   8f,  false, 1);
            RegisterPlaceholderClip(model, CharacterAnimState.Fall,   8f,  true,  3);
            RegisterPlaceholderClip(model, CharacterAnimState.Attack, 12f, false, 7);
            RegisterPlaceholderClip(model, CharacterAnimState.Hurt,   8f,  false, 1);
            RegisterPlaceholderClip(model, CharacterAnimState.Die,    6f,  false, 8);
        }
 
        private static void RegisterPlaceholderClip(CharacterModel model,
            CharacterAnimState state, float fps, bool loop, int frameCount)
        {
            var frames = new AnimationFrame[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                frames[i] = new AnimationFrame(new PartOffset[]
                {
                    new(i, -13, 34),   // head
                    new(i,  -8, 10),   // leg
                    new(i,  -9, 16),   // body
                    new(0,   0,  0),   // bag (empty)
                });
            }
 
            var clip = new AnimationClip(state.ToString().ToLower(), fps, loop, frames);
            model.Anim.RegisterClip(state, clip);
        }
    }
}