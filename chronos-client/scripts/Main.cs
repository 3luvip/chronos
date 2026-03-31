using Godot;

#nullable enable

public partial class Main : Control
{
    private Node? _splash;
    private bool  _transitioning;

    public override void _Ready()
    {
        ShowSplash();
    }


    private void ShowSplash()
    {
        _transitioning = false;

		var splashView = new SplashScreenView();
		_splash = splashView;
        AddChild(_splash);

        if (_splash is CanvasItem ci)
            ci.ZIndex = 100;

		splashView.SplashFinished += OnSplashDone;

        // Timeout dự phòng: nếu animation không kết thúc trong 4 giây
        GetTree().CreateTimer(4.0).Timeout += OnSplashTimeout;

        GD.Print("[Main] Splash shown.");
    }

    private void OnSplashDone()    => TryGoToLogin();
    private void OnSplashTimeout() => TryGoToLogin();

    private void TryGoToLogin()
    {
        if (_transitioning) return;
        _transitioning = true;

        if (_splash is { } s && IsInstanceValid(s))
            s.QueueFree();
        _splash = null;

        GoToLogin();
    }

    private void GoToLogin()
    {
		var loginNode = new LoginScreen();
		ScreenManager.Change(this, loginNode);
    }
}