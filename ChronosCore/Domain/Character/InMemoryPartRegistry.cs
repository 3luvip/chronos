using System;
using System.Collections.Generic;

namespace Chronos.Core.Domain.Character
{
    public sealed class InMemoryPartRegistry : IPartRegistry
    {
        private readonly Dictionary<int, EquipmentPart> _parts = new();
 
        public void Register(EquipmentPart part) => _parts[part.PartId] = part;
 
        public EquipmentPart? GetPart(int partId)
            => _parts.TryGetValue(partId, out var p) ? p : null;
    }
    
}