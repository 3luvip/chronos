namespace Chronos.Core.Domain.Character
{
    public sealed class EquipmentLoadout
    {
        public int HeadPartId   { get; set; } = -1;
        public int BodyPartId   { get; set; } = -1;
        public int LegPartId    { get; set; } = -1;
        public int WeaponPartId { get; set; } = -1;
        public int BagPartId    { get; set; } = -1;
        public int AuraPartId   { get; set; } = -1;
        public int MountPartId  { get; set; } = -1;
 
        // Legacy: bag < 0 means no bag
        public bool HasBag    => BagPartId   >= 0;
        public bool HasAura   => AuraPartId  >= 0;
        public bool HasMount  => MountPartId >= 0;
        public bool HasWeapon => WeaponPartId >= 0;
    }
}