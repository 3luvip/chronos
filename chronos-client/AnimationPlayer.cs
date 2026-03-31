using Godot;

public partial class AnimationPlayer : Godot.AnimationPlayer
{
	[Export] public string AnimationToPlay { get; set; } = "fade_out";

	private bool _finishedEmitted;

	[Signal]
	public delegate void SplashFinishedEventHandler();

	public override void _Ready()
	{
		_finishedEmitted = false;

		if (string.IsNullOrEmpty(AnimationToPlay) || !HasAnimation(AnimationToPlay))
		{
			EmitSignal(SignalName.SplashFinished);
			_finishedEmitted = true;
			return;
		}

		Play(AnimationToPlay);
	}

	public void _on_animation_finished(StringName animName)
	{
		if (_finishedEmitted)
		{
			return;
		}

		if (animName != AnimationToPlay)
		{
			return;
		}

		_finishedEmitted = true;
		EmitSignal(SignalName.SplashFinished);
	}
}

