using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

public partial class LoadingScreen : Node2D
{
    private const string DefaultAtlasPath = "res://asset/loading.png";
    private const int FrameSize = 64;
    private const int FrameCount = 9;

    [Signal]
    public delegate void LoadingFinishedEventHandler();

    [Export] public string AtlasTexturePath { get; set; } = DefaultAtlasPath;
    [Export] public string NextScenePath { get; set; } = "res://scenes/Main.tscn";
    [Export] public bool AutoLoadOnReady { get; set; } = true;
    [Export] public float MinimumDisplaySeconds { get; set; } = 1.0f;
    [Export] public bool RequireServerReady { get; set; } = false;
    [Export] public float ServerWaitTimeoutSeconds { get; set; } = 20.0f;

    private Panel? _panel;
    private AnimatedSprite2D? _spinner;
    private readonly TaskCompletionSource _serverReadyTcs = new();

    public override void _Ready()
    {
        BuildLoadingView();
        _spinner?.Play("default");

        if (AutoLoadOnReady && !string.IsNullOrWhiteSpace(NextScenePath))
        {
            _ = LoadAndChangeSceneAsync(NextScenePath);
        }
    }

    private void BuildLoadingView()
    {
        if (GetNodeOrNull("Panel") is Panel existingPanel)
        {
            _panel = existingPanel;
            _spinner = existingPanel.GetNodeOrNull<AnimatedSprite2D>("AnimatedSprite2D");
            if (_spinner is not null)
            {
                return;
            }

            existingPanel.QueueFree();
        }

        var atlasImage = GD.Load<Texture2D>(AtlasTexturePath);
        if (atlasImage is null)
        {
            GD.PushError($"LoadingScreen: cannot load atlas '{AtlasTexturePath}'.");
            return;
        }

        _panel = new Panel
        {
            Name = "Panel",
            OffsetRight = 1080f,
            OffsetBottom = 640f,
        };
        AddChild(_panel);

        var frames = new SpriteFrames();
        frames.SetAnimationLoop("default", true);
        frames.SetAnimationSpeed("default", 60.0);

        for (int i = 0; i < FrameCount; i++)
        {
            var atlasTex = new AtlasTexture
            {
                Atlas = atlasImage,
                Region = new Rect2(0, i * FrameSize, FrameSize, FrameSize),
            };
            frames.AddFrame("default", atlasTex, 1f);
        }

        _spinner = new AnimatedSprite2D
        {
            Name = "AnimatedSprite2D",
            Position = new Vector2(544f, 288f),
            Scale = new Vector2(0.6250005f, 0.56250006f),
            SpriteFrames = frames,
        };
        _panel.AddChild(_spinner);
    }

    public Task StartLoadingAsync(string scenePath)
    {
        NextScenePath = scenePath;
        return LoadAndChangeSceneAsync(scenePath);
    }

    public Task StartLoadingWithServerWaitAsync(string scenePath, float? timeoutSeconds = null)
    {
        NextScenePath = scenePath;
        RequireServerReady = true;
        if (timeoutSeconds.HasValue)
        {
            ServerWaitTimeoutSeconds = timeoutSeconds.Value;
        }
        return LoadAndChangeSceneAsync(scenePath);
    }

    public Task StartLoadingAsync(string scenePath, Task serverTask)
    {
        return StartLoadingAsync(scenePath, serverTask, CancellationToken.None);
    }

    public async Task StartLoadingAsync(string scenePath, Task serverTask, CancellationToken cancellationToken)
    {
        if (serverTask is null)
        {
            await LoadAndChangeSceneAsync(scenePath);
            return;
        }

        NextScenePath = scenePath;
        await LoadAndChangeSceneAndWaitTaskAsync(scenePath, serverTask, cancellationToken);
    }

    public void SetServerReady()
    {
        _serverReadyTcs.TrySetResult();
    }

    private async Task LoadAndChangeSceneAsync(string scenePath)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            GD.PushWarning("LoadingScreen: NextScenePath is empty.");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        ulong startMs = Time.GetTicksMsec();
        Error requestErr = ResourceLoader.LoadThreadedRequest(scenePath, "PackedScene");
        if (requestErr != Error.Ok)
        {
            GD.PushError($"LoadingScreen: cannot start threaded load for '{scenePath}'. Error={requestErr}");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        ResourceLoader.ThreadLoadStatus status;
        while (true)
        {
            status = ResourceLoader.LoadThreadedGetStatus(scenePath);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded ||
                status == ResourceLoader.ThreadLoadStatus.Failed ||
                status == ResourceLoader.ThreadLoadStatus.InvalidResource)
            {
                break;
            }

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        float elapsedSeconds = (Time.GetTicksMsec() - startMs) / 1000.0f;
        float waitSeconds = Mathf.Max(0.0f, MinimumDisplaySeconds - elapsedSeconds);
        if (waitSeconds > 0.0f)
        {
            await ToSignal(GetTree().CreateTimer(waitSeconds), SceneTreeTimer.SignalName.Timeout);
        }

        if (RequireServerReady)
        {
            bool serverOk = await WaitForServerReadyAsync();
            if (!serverOk)
            {
                GD.PushError("LoadingScreen: timeout while waiting server data.");
                EmitSignal(SignalName.LoadingFinished);
                return;
            }
        }

        if (status != ResourceLoader.ThreadLoadStatus.Loaded)
        {
            GD.PushError($"LoadingScreen: failed loading '{scenePath}'. Status={status}");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        var packed = ResourceLoader.LoadThreadedGet(scenePath) as PackedScene;
        if (packed is null)
        {
            GD.PushError($"LoadingScreen: loaded resource is not a PackedScene: '{scenePath}'.");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        GetTree().ChangeSceneToPacked(packed);
        EmitSignal(SignalName.LoadingFinished);
    }

    private async Task LoadAndChangeSceneAndWaitTaskAsync(string scenePath, Task serverTask, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            GD.PushWarning("LoadingScreen: NextScenePath is empty.");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        ulong startMs = Time.GetTicksMsec();
        Error requestErr = ResourceLoader.LoadThreadedRequest(scenePath, "PackedScene");
        if (requestErr != Error.Ok)
        {
            GD.PushError($"LoadingScreen: cannot start threaded load for '{scenePath}'. Error={requestErr}");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        ResourceLoader.ThreadLoadStatus status;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            status = ResourceLoader.LoadThreadedGetStatus(scenePath);
            if (status == ResourceLoader.ThreadLoadStatus.Loaded ||
                status == ResourceLoader.ThreadLoadStatus.Failed ||
                status == ResourceLoader.ThreadLoadStatus.InvalidResource)
            {
                break;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        float elapsedSeconds = (Time.GetTicksMsec() - startMs) / 1000.0f;
        float waitSeconds = Mathf.Max(0.0f, MinimumDisplaySeconds - elapsedSeconds);
        if (waitSeconds > 0.0f)
        {
            await ToSignal(GetTree().CreateTimer(waitSeconds), SceneTreeTimer.SignalName.Timeout);
        }

        await serverTask;

        if (status != ResourceLoader.ThreadLoadStatus.Loaded)
        {
            GD.PushError($"LoadingScreen: failed loading '{scenePath}'. Status={status}");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        var packed = ResourceLoader.LoadThreadedGet(scenePath) as PackedScene;
        if (packed is null)
        {
            GD.PushError($"LoadingScreen: loaded resource is not a PackedScene: '{scenePath}'.");
            EmitSignal(SignalName.LoadingFinished);
            return;
        }

        GetTree().ChangeSceneToPacked(packed);
        EmitSignal(SignalName.LoadingFinished);
    }

    private async Task<bool> WaitForServerReadyAsync()
    {
        if (_serverReadyTcs.Task.IsCompleted)
        {
            return true;
        }

        if (ServerWaitTimeoutSeconds <= 0f)
        {
            await _serverReadyTcs.Task;
            return true;
        }

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ServerWaitTimeoutSeconds));
        var completed = await Task.WhenAny(_serverReadyTcs.Task, timeoutTask);
        return completed == _serverReadyTcs.Task;
    }
}
