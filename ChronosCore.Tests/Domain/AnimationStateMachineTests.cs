using Chronos.Core.Domain.Animation;
using FluentAssertions;
using Xunit;

namespace Chronos.Core.Tests.Domain;

public sealed class AnimationStateMachineTests
{
    [Fact]
    public void RequestTransition_ToSameState_ReturnsFalse()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Idle).Should().BeFalse();
    }

    [Fact]
    public void RequestTransition_DuringOnceAnim_ReturnsFalse()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Attack);
        sm.RequestTransition(AnimationState.Run).Should().BeFalse();
    }

    [Fact]
    public void Tick_AdvancesFrame()
    {
        var sm = new AnimationStateMachine();
        sm.RequestTransition(AnimationState.Run); // 10fps = 0.1s interval

        sm.Tick(0.05f); // half interval — no advance
        sm.CurrentFrame.Should().Be(0);

        sm.Tick(0.06f); // crosses interval
        sm.CurrentFrame.Should().Be(1);
    }

    [Fact]
    public void Tick_OnceAnimComplete_FiresEvent()
    {
        var sm = new AnimationStateMachine();
        AnimationState? completed = null;
        sm.AnimationCompleted += s => completed = s;

        sm.RequestTransition(AnimationState.Attack); // 6 frames at 12fps = 0.5s
        for (int i = 0; i < 100; i++) sm.Tick(0.01f);

        completed.Should().Be(AnimationState.Attack);
        sm.CurrentState.Should().Be(AnimationState.Idle);
    }
}
