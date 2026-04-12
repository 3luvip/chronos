using Chronos.Core.Domain;

namespace Chronos.Core.Domain.Character;

public sealed class CharacterState
{
    public required uint Id       { get; init; }
    public          Vec2 Position { get; set; }
    public          Vec2 Velocity { get; set; }
    public          bool FacingRight { get; set; } = true;
    public          int  Hp       { get; set; }
    public          int  MaxHp    { get; set; }
    public          float MoveSpeed { get; set; } = 5f;

    public uint? HeadSpriteId   { get; set; }
    public uint? BodySpriteId   { get; set; }
    public uint? LegsSpriteId   { get; set; }
    public uint? WeaponSpriteId { get; set; }
    public uint? AuraSpriteId   { get; set; }

    public bool  IsAlive    => Hp > 0;
    public float HpPercent  => MaxHp <= 0 ? 0f : (float)Hp / MaxHp;
}
