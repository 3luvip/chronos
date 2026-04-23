namespace Chronos.Core.Domain.Character
{
    public static class CharacterStatusMapper
    {
        public static CharacterAnimState ToAnimState(CharacterStatus status)
            => status switch
            {
                CharacterStatus.Stand   => CharacterAnimState.Idle,
                CharacterStatus.Run     => CharacterAnimState.Run,
                CharacterStatus.Jump    => CharacterAnimState.Jump,
                CharacterStatus.Fall    => CharacterAnimState.Fall,
                CharacterStatus.Die     => CharacterAnimState.Die,
                CharacterStatus.Hurt    => CharacterAnimState.Hurt,
                CharacterStatus.Sit     => CharacterAnimState.Idle,
                CharacterStatus.Skill   => CharacterAnimState.Skill,
                CharacterStatus.Fly     => CharacterAnimState.Fly,
                CharacterStatus.Charge  => CharacterAnimState.Charge,
                CharacterStatus.Nothing => CharacterAnimState.Idle,
                _                      => CharacterAnimState.Idle,
            };
    }
}