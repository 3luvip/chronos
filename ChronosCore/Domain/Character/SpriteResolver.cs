using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    /// <summary>
    /// Resolves a CharacterModel + AnimationFrame into a list of SpriteDrawCalls.
    /// Pure domain logic — replaces the anchor/transform math in mGraphics.drawRegion.
    ///
    /// Legacy anchor flags:
    ///   num=2, anchor=24 (TOP|RIGHT) when cdir=-1
    ///   num=0, anchor=0  (TOP|LEFT)  when cdir=1
    ///   anchor2=TOP_RIGHT or 0
    /// All replaced by computing final screen x from WorldX + dx * flipSign.
    /// </summary>
    public sealed class SpriteResolver
    {
        // Z-order constants (replaces draw-call ordering in paintCharBody)
        public const int Z_MOUNT_BACK  = 10;
        public const int Z_AURA_BACK   = 20;
        public const int Z_SHADOW      = 25;
        public const int Z_LEG         = 28;
        public const int Z_BODY        = 30;
        public const int Z_HEAD        = 32;
        public const int Z_WEAPON      = 35;
        public const int Z_HAT_FRONT   = 38;
        public const int Z_AURA_FRONT  = 40;
        public const int Z_MOUNT_FRONT = 45;
        public const int Z_SKILL_FX    = 50;
        public const int Z_DAMAGE_FX   = 60;
 
        private readonly IPartRegistry _registry;
        private readonly int _camX;
        private readonly int _camY;
 
        public SpriteResolver(IPartRegistry registry, int camX, int camY)
        {
            _registry = registry;
            _camX     = camX;
            _camY     = camY;
        }
 
        /// <summary>Resolve all draw calls for one character this frame.</summary>
        public void Resolve(CharacterModel model, AnimationFrame frame,
                            List<SpriteDrawCall> output)
        {
            int baseScreenX = model.WorldX - _camX;
            int baseScreenY = model.WorldY - _camY;
            int flipSign    = model.IsFlipH ? -1 : 1;
 
            ResolveBodyParts(model, frame, baseScreenX, baseScreenY, flipSign, output);
 
            if (model.Loadout.HasAura)
                ResolveAura(model, frame, baseScreenX, baseScreenY, flipSign, output);
 
            if (model.Loadout.HasMount)
                ResolveMount(model, baseScreenX, baseScreenY, flipSign, output);
 
            ResolveShadow(baseScreenX, baseScreenY, output);
        }
 
        // ── Body parts ────────────────────────────────────────────────────────
 
        private void ResolveBodyParts(CharacterModel model, AnimationFrame frame,
                                      int bx, int by, int flip,
                                      List<SpriteDrawCall> output)
        {
            ResolvePartSlot(model.Loadout.HeadPartId,
                frame.Parts[AnimationFrame.PART_HEAD], bx, by, flip, Z_HEAD, output);
 
            ResolvePartSlot(model.Loadout.LegPartId,
                frame.Parts[AnimationFrame.PART_LEG], bx, by, flip, Z_LEG, output);
 
            ResolvePartSlot(model.Loadout.BodyPartId,
                frame.Parts[AnimationFrame.PART_BODY], bx, by, flip, Z_BODY, output);
 
            if (model.Loadout.HasWeapon)
                ResolvePartSlot(model.Loadout.WeaponPartId,
                    frame.Parts[AnimationFrame.PART_BODY], bx, by, flip, Z_WEAPON, output);
 
            if (model.Loadout.HasBag)
                ResolvePartSlot(model.Loadout.BagPartId,
                    frame.Parts[AnimationFrame.PART_BAG], bx, by, flip, Z_BODY - 1, output);
        }
 
        private void ResolvePartSlot(int partId, PartOffset offset,
                                     int bx, int by, int flip,
                                     int zOrder, List<SpriteDrawCall> output)
        {
            if (partId < 0) return;
            var part = _registry.GetPart(partId);
            if (part == null) return;
 
            var img = part.GetImage(offset.ImageIndex);
            if (img.SpriteId <= 0) return;
 
            // Legacy formula: x = cx + (charInfo_dx + pi.dx) * flipSign
            //                 y = cy - charInfo_dy + pi.dy
            int screenX = bx + (offset.Dx + img.Dx) * flip;
            int screenY = by - offset.Dy + img.Dy;
 
            output.Add(new SpriteDrawCall(img.SpriteId, screenX, screenY,
                flipH: flip < 0, zOrder: zOrder));
        }
 
        private void ResolveAura(CharacterModel model, AnimationFrame frame,
                                 int bx, int by, int flip, List<SpriteDrawCall> output)
        {
            // Aura back (drawn behind body)
            ResolvePartSlot(model.Loadout.AuraPartId,
                frame.Parts[AnimationFrame.PART_BODY], bx, by, flip, Z_AURA_BACK, output);
            // Aura front (drawn in front of everything except skill fx)
            ResolvePartSlot(model.Loadout.AuraPartId,
                frame.Parts[AnimationFrame.PART_BODY], bx, by, flip, Z_AURA_FRONT, output);
        }
 
        private void ResolveMount(CharacterModel model,
                                  int bx, int by, int flip, List<SpriteDrawCall> output)
        {
            // Mount back
            ResolvePartSlot(model.Loadout.MountPartId,
                default, bx, by, flip, Z_MOUNT_BACK, output);
            // Mount front
            ResolvePartSlot(model.Loadout.MountPartId,
                default, bx, by, flip, Z_MOUNT_FRONT, output);
        }
 
        private static void ResolveShadow(int bx, int by, List<SpriteDrawCall> output)
        {
            // Shadow is a fixed sprite below feet
            output.Add(new SpriteDrawCall(SpriteIds.Shadow, bx, by + 2,
                zOrder: Z_SHADOW, alpha: 0.6f));
        }
    }
}