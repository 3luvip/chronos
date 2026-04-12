using System.Collections.Generic;

namespace Chronos.Core.Domain.Character;

public sealed class EquipmentRegistry
{
    private readonly Dictionary<(byte partType, ushort spriteId), CharacterPart> _parts = new();

    public void Register(byte partType, ushort spriteId, CharacterPart part) =>
        _parts[(partType, spriteId)] = part;

    public CharacterPart? Get(byte partType, ushort spriteId) =>
        _parts.TryGetValue((partType, spriteId), out var p) ? p : null;

    public bool IsLoaded => _parts.Count > 0;

    public void Clear() => _parts.Clear();
}
