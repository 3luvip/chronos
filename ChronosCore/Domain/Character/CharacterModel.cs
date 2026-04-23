using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    public sealed class CharacterModel
    {
        public uint   Id         { get; }
        public int    WorldX     { get; set; }
        public int    WorldY     { get; set; }
        public int    Direction  { get; set; } = 1;    // 1=right, -1=left (replaces cdir)
        public CharacterStatus Status { get; set; } = CharacterStatus.Stand;
 
        public int  Hp           { get; set; }
        public int  MaxHp        { get; set; }
        public bool IsMe         { get; set; }         // local player flag
 
        public EquipmentLoadout         Loadout    { get; } = new();
        public CharacterAnimController  Anim       { get; } = new();
 
        // Visual state flags
        public bool IsFlipH      => Direction == -1;
        public bool IsAlive      => Hp > 0;
 
        // Runtime visual — set by renderer after draw
        public int CharacterHeight { get; set; } = 60;
 
        public CharacterModel(uint id)
        {
            Id = id;
        }
    }
}