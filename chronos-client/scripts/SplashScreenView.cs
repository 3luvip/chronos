using Godot;

#nullable enable

/// <summary>
/// Splash screen dựng hoàn toàn bằng C# (không dùng PackedScene / tscn).
/// </summary>
public partial class SplashScreenView : Node2D
{
	[Signal]
	public delegate void SplashFinishedEventHandler();

	private bool _finishedEmitted;
	private Node2D _logo = null!;

	private const string BgTexturePath = "res://asset/BG.png";
	private const string SymbolTexturePath = "res://asset/godot_symbol.png";
	private const string TextTexturePath = "res://asset/Godot_text.png";

	public override void _Ready()
	{
		_finishedEmitted = false;

		BuildSceneGraph();
		StartFadeOut();
	}

	private void BuildSceneGraph()
	{
		// Background
		var bg = new Sprite2D
		{
			Texture = GD.Load<Texture2D>(BgTexturePath),
			Position = new Vector2(671.9999f, 292.00003f),
			Scale = new Vector2(191f, 99.5f),
			TextureFilter = TextureFilterEnum.Linear
		};
		AddChild(bg);

		// Logo group to be faded
		_logo = new Node2D { Name = "LOGO" };
		_logo.Modulate = new Color(1f, 1f, 1f, 1f);
		AddChild(_logo);

		var symbol = new Sprite2D
		{
			Texture = GD.Load<Texture2D>(SymbolTexturePath),
			TextureFilter = TextureFilterEnum.Linear,
			Position = new Vector2(540f, 240f),
			Scale = new Vector2(4f, 4f),
		};
		_logo.AddChild(symbol);

		var text = new Sprite2D
		{
			Texture = GD.Load<Texture2D>(TextTexturePath),
			TextureFilter = TextureFilterEnum.Linear,
			Position = new Vector2(540f, 450f),
			Scale = new Vector2(4f, 4f),
		};
		_logo.AddChild(text);
	}

	private void StartFadeOut()
	{
		// Mô phỏng animation "fade_out" trong SplashScreen.tscn:
		// alpha = 1 (t=0) -> alpha = 0.8 (t=0.2) -> alpha = 0 (t=1.0)
		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);

		tween.TweenProperty(_logo, "modulate:a", 0.8f, 0.2f);
		tween.TweenProperty(_logo, "modulate:a", 0f, 0.8f);
		tween.Finished += () =>
		{
			if (_finishedEmitted) return;
			_finishedEmitted = true;
			EmitSignal(SignalName.SplashFinished);
		};
	}
}

