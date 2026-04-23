using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    /// <summary>
    /// One equipment part (head, body, leg, weapon, …).
    /// type: 0=head, 1=body, 2=leg, 3=bag  (matches legacy Part.type).
    /// pi[]: per-animation-step images (matches legacy Part.pi[]).
    /// </summary>
    public sealed class EquipmentPart
    {
        public const int TYPE_HEAD   = 0;
        public const int TYPE_BODY   = 1;
        public const int TYPE_LEG    = 2;
        public const int TYPE_BAG    = 3;
 
        public int               PartId   { get; }
        public int               Type     { get; }
        public PartImageEntry[]  Images   { get; }   // replaces pi[]
 
        public EquipmentPart(int partId, int type, PartImageEntry[] images)
        {
            PartId = partId;
            Type   = type;
            Images = images ?? throw new ArgumentNullException(nameof(images));
        }
 
        /// <summary>Safe image lookup (returns default if index out of range).</summary>
        public PartImageEntry GetImage(int index)
            => (uint)index < (uint)Images.Length ? Images[index] : default;
    }
}