using Godot;
using System;
using System.Threading;

#nullable enable

// ═══════════════════════════════════════════════════════════════════════════
// LoginScreen.cs
//
// Trách nhiệm:
//   • Hiện giao diện login (parallax background, logo, 3 button chính).
//   • Quản lý panel "Change Account" (nhập username/password → login thật).
//   • Quản lý panel "Select Server".
//   • Kết nối tới server qua ChronosTcpClient (TLS + HMAC).
//   • Lưu session sau khi login thành công; cho phép logout.
//   • Toàn bộ luồng session-based security nằm ở đây.
// ═══════════════════════════════════════════════════════════════════════════

public partial class LoginScreen : Node2D
{
    // ── Paths ─────────────────────────────────────────────────────────────
    private const string LogoShineShaderPath = "res://shaders/logo_shine.gdshader";
    private const string ParallaxShaderPath  = "res://shaders/parallax_scroll.gdshader";

    // ── Network config (production: đọc từ project settings / env) ────────
    private const string DefaultHost       = "127.0.0.1";
    private const int    DefaultPort       = 14446;
    private const string DefaultHmacSecret = "b3464e02f95c8c1b0f72d925598b88d8b6581fc5b6e80f40724f5e91e781b0fe";
    private const bool   UseTls            = true;
    private const bool   SkipTlsCert       = true;   // false trong production
    private const bool   UseHmac           = true;

    // ── Session state ─────────────────────────────────────────────────────
    private ChronosTcpClient?        _client;
    private CancellationTokenSource? _cts;
    private LoginResult?             _session;        // null khi chưa login
    private int                      _currentServerId = 1;

    // ── UI state ──────────────────────────────────────────────────────────
    private ParallaxLayer[] _layers = Array.Empty<ParallaxLayer>();
    private float           _elapsed;
    private const float     ResetInterval = 1000f;
    private Sprite2D        _logo = null!;

    private VBoxContainer _loginForm     = null!;
    private ButtonManager _btnChangeServer = null!;
    private ButtonManager _btnNewPlayer    = null!;
    private ButtonManager _btnLogout       = null!;
    private string        _currentServer  = "Server 1";

    private Control?      _serverPanel;
    private PanelManager? _changeAccountPanel;
    private PanelManager? _loggedInPanel;

    // ── Parallax layer configs ─────────────────────────────────────────────
    private static readonly LayerConfig[] Configs =
    {
        new("Sky",          "res://MountainsLayers/sky.png",           Vector2.Zero,       0f,    1f,   -240f, 1081f, 400f,  6),
        new("FarClouds",    "res://MountainsLayers/far-clouds.png",    new Vector2(-4, 0), 0.02f, 0f,   264f,  1080f, 904f,  1),
        new("NearClouds",   "res://MountainsLayers/near-clouds.png",   new Vector2(4, 0),  0.02f, 0f,   280f,  1080f, 920f,  1),
        new("FarMountains", "res://MountainsLayers/far-mountains.png", new Vector2(-3, 0), 0.02f, 0f,   319f,  1080f, 959f,  1),
        new("Mountains",    "res://MountainsLayers/mountains.png",     new Vector2(6, 0),  0.02f, 0f,   385f,  1080f, 1025f, 1),
        new("Trees",        "res://MountainsLayers/trees.png",         new Vector2(6, 0),  0.02f, 0f,   400f,  1080f, 1040f, 1),
    };

    // ── Shared button style ───────────────────────────────────────────────
    private static readonly ButtonManager.ButtonConfig BtnCfg = new()
    {
        DefaultSize             = new Vector2(200, 40),
        Size                    = new Vector2(200, 40),
        NormalBackgroundColor   = new Color("#2a1f0e"),
        HoverBackgroundColor    = new Color("#3a2f1e"),
        PressedBackgroundColor  = new Color("#1a1208"),
        DisabledBackgroundColor = new Color("#1a1510"),
        NormalBorderColor       = new Color("#c8a84b"),
        HoverBorderColor        = new Color("#f0d080"),
        PressedBorderColor      = new Color("#a08030"),
        DisabledBorderColor     = new Color("#4a3d20"),
        NormalTextColor         = new Color("#f0d080"),
        HoverTextColor          = new Color("#ffe8a0"),
        PressedTextColor        = new Color("#c8a84b"),
        DisabledTextColor       = new Color("#6a5a30"),
        FontFamily              = "Edo",
        FontStyle               = FontStyle.Regular,
        FontSize                = 16,
    };

    // ── Shared LineEdit style ─────────────────────────────────────────────
    private static LineEditManager.LineEditConfig MakeInputCfg(bool isPassword = false) => new()
    {
        Width                = 280,
        InputHeight          = 42,
        LabelHeight          = 20,
        LabelSpacing         = 4,
        ErrorSpacing         = 3,
        BackgroundColor      = new Color("#1a1208"),
        FocusBackgroundColor = new Color("#2a1f0e"),
        BorderColor          = new Color("#c8a84b"),
        FocusBorderColor     = new Color("#f0d080"),
        ErrorColor           = new Color("#ff6b6b"),
        BorderWidth          = 1,
        CornerRadius         = 3,
        TextColor            = new Color("#f0d080"),
        PlaceholderColor     = new Color("#8a7040"),
        LabelColor           = new Color("#f0d080"),
        CaretColor           = new Color("#f0d080"),
        SelectionColor       = new Color("#c8a84b", 0.35f),
        FontSize             = 15,
        LabelFontSize        = 13,
        ShowClearButton      = false,
        IsPassword           = isPassword,
        CaretBlink           = true,
        AnimateFocus         = true,
        ShowErrorLabel       = true,
        ShowErrorBorder      = true,
        Required             = true,
        MinLength            = isPassword ? 6 : 0,
    };

    private static readonly PanelManager.PanelConfig PanelCfg = new()
    {
        BackgroundColor       = new Color("#1a1208"),
        BorderColor           = new Color("#c8a84b"),
        CornerColor           = new Color("#f0d080"),
        TitleColor            = new Color("#f0d080"),
        TitleFontSize         = 20,
        PatchMargin           = 16,
        ContentMargin         = 30,
        ContentMarginLeft     = 50,
        ContentMarginTop      = 60,
        ContentSeparation     = 14,
        FontFamily            = "Edo",
        FontStyle             = FontStyle.Regular,
        ShowCornerDecorations = true,
    };

    // ═════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ═════════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        SetUpParallaxBackground();
        CreateLogo();
        CreateLoginForm();
    }

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed > ResetInterval)
        {
            _elapsed = 0f;
            foreach (var layer in _layers)
                layer.ResetTimeOffset();
        }
    }

    public override void _ExitTree()
    {
        // Huỷ mọi network operation đang chờ khi scene bị unload
        _cts?.Cancel();
        _cts?.Dispose();
        _client?.Dispose();
    }

    // ═════════════════════════════════════════════════════════════════════
    // Background & Logo
    // ═════════════════════════════════════════════════════════════════════

    private void SetUpParallaxBackground()
    {
        _layers = new ParallaxLayer[Configs.Length];
        for (int i = 0; i < Configs.Length; i++)
        {
            _layers[i] = new ParallaxLayer(Configs[i]);
            _layers[i].AttachTo(this);
        }
    }

    private void CreateLogo()
    {
        _logo = new Sprite2D
        {
            Name     = "Logo",
            Texture  = GD.Load<Texture2D>("res://asset/lgo_1.png"),
            Position = new Vector2(544, 80),
            Scale    = new Vector2(0.15f, 0.144f),
            Material = BuildLogoShineShader(),
        };
        AddChild(_logo);
    }

    private ShaderMaterial BuildLogoShineShader()
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>(LogoShineShaderPath) };
        mat.SetShaderParameter("shine_speed",     0.4f);
        mat.SetShaderParameter("shine_width",     0.15f);
        mat.SetShaderParameter("shine_angle",     0.3f);
        mat.SetShaderParameter("shine_intensity", 1.2f);
        mat.SetShaderParameter("pause_duration",  3.0f);
        mat.SetShaderParameter("shine_color",     new Color(1f, 1f, 1f, 1f));
        return mat;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Login form (3 nút chính)
    // ═════════════════════════════════════════════════════════════════════

    private void CreateLoginForm()
    {
        float logoBottomY = _logo.Position.Y
                          + _logo.Texture.GetSize().Y * _logo.Scale.Y / 2f;

        _loginForm = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _loginForm.AddThemeConstantOverride("separation", 45);
        _loginForm.Position    = new Vector2(GetViewportRect().Size.X / 2.4f, logoBottomY + 100);
        _loginForm.AnchorRight = 1f;
        _loginForm.OffsetRight = 0f;
        AddChild(_loginForm);

        _btnNewPlayer = new ButtonManager();
        _btnNewPlayer.Setup("New Player", null, BtnCfg, OnNewPlayerPressed);
        _loginForm.AddChild(_btnNewPlayer);

        var btnChangeAccount = new ButtonManager();
        btnChangeAccount.Setup("Change Account", null, BtnCfg, OnChangeAccountPressed);
        _loginForm.AddChild(btnChangeAccount);

        _btnChangeServer = new ButtonManager();
        _btnChangeServer.Setup(_currentServer, null, BtnCfg, OpenServerPanel);
        _loginForm.AddChild(_btnChangeServer);

        // Nút Logout — ẩn cho đến khi login thành công
        _btnLogout = new ButtonManager();
        _btnLogout.Setup("Logout", null, BtnCfg, OnLogoutPressed);
        _btnLogout.SetEnabled(false);
        _btnLogout.Visible = false;
        _loginForm.AddChild(_btnLogout);
    }

    // ═════════════════════════════════════════════════════════════════════
    // New Player (stub)
    // ═════════════════════════════════════════════════════════════════════

    private void OnNewPlayerPressed()
    {
        // TODO: mở màn hình đăng ký tài khoản mới
        GD.Print("[LoginScreen] New Player pressed — not implemented yet.");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Change Account panel
    // ═════════════════════════════════════════════════════════════════════

    private void OnChangeAccountPressed()
    {
        _loginForm.Visible = false;

        _changeAccountPanel ??= new PanelManager();
        if (_changeAccountPanel.GetParent() == null)
            AddChild(_changeAccountPanel);

        var viewSize  = GetViewportRect().Size;
        var panelSize = new Vector2(380, 310);
        var panelPos  = (viewSize - panelSize) / 2f;

        // Overlay mờ
        var overlay = new ColorRect
        {
            Color    = new Color(0f, 0f, 0f, 0.6f),
            Size     = viewSize,
            Position = Vector2.Zero,
        };
        _changeAccountPanel.AddChild(overlay);

        var panelData = _changeAccountPanel.CreatePanel(
            position: new Vector2(panelPos.X, panelPos.Y + 35),
            size:     panelSize,
            title:    "",
            style:    PanelCfg);

        // Tiêu đề phụ (tự đặt thay vì dùng PanelManager.title để căn chỉnh)
        var titleLbl = new Label
        {
            Text                = "Change Account",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft          = 0f,
            AnchorRight         = 1f,
            OffsetTop           = 18f,
            OffsetBottom        = 48f,
        };
        titleLbl.AddThemeColorOverride("font_color", new Color("#f0d080"));
        FontSystem.Instance?.ApplyFont(titleLbl, "Edo", FontStyle.Regular, 20);
        panelData.Container.AddChild(titleLbl);

        // ── Fields ────────────────────────────────────────────────────────
        var vbox = (VBoxContainer)panelData.ContentArea;
        vbox.AddThemeConstantOverride("separation", 85);

        var userInput = new LineEditManager();
        userInput.Setup("Username", "Enter username...", MakeInputCfg());
        vbox.AddChild(userInput);

        var passwordInput = new LineEditManager();
        passwordInput.Setup("Password", "Enter password...", MakeInputCfg(isPassword: true));
        vbox.AddChild(passwordInput);

        // ── Buttons ───────────────────────────────────────────────────────
        var buttonRow = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        vbox.AddChild(buttonRow);

        var hbox = new HBoxContainer
        {
            Alignment           = BoxContainer.AlignmentMode.Begin,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        hbox.AddThemeConstantOverride("separation", 150);
        buttonRow.AddChild(hbox);

        // Tham chiếu label trạng thái (hiện lỗi hoặc thông báo đang kết nối)
        var statusLbl = new Label
        {
            Text                = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode        = TextServer.AutowrapMode.WordSmart,
        };
        statusLbl.AddThemeColorOverride("font_color", new Color("#ff6b6b"));
        FontSystem.Instance?.ApplyFont(statusLbl, "Montserrat", FontStyle.Regular, 12);

        var btnLogin = new ButtonManager();
        btnLogin.Setup("Login", new Vector2(140, 40), BtnCfg, () =>
        {
            if (!userInput.IsValid || !passwordInput.IsValid) return;
            // Ẩn panel và bắt đầu login
            _changeAccountPanel?.QueueFree();
            _changeAccountPanel = null;
            DoLogin(userInput.Text.Trim(), passwordInput.Text);
        });
        hbox.AddChild(btnLogin);

        var btnCancel = new ButtonManager();
        btnCancel.Setup("Cancel", new Vector2(140, 40), BtnCfg, CloseChangeAccountPanel);
        hbox.AddChild(btnCancel);

        vbox.AddChild(statusLbl);
    }

    private void CloseChangeAccountPanel()
    {
        _changeAccountPanel?.QueueFree();
        _changeAccountPanel = null;
        _loginForm.Visible  = true;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Network — Login / Logout
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kết nối và thực hiện OP_LOGIN. TLS + HMAC bắt buộc (cấu hình ở đầu file).
    /// Session được lưu trong _session sau khi thành công.
    /// </summary>
    private async void DoLogin(string username, string password)
    {
        SetUiBusy(true);
        ShowStatusToast("Connecting...");

        // Huỷ session cũ nếu còn
        _cts?.Cancel();
        _cts?.Dispose();
        _client?.Dispose();

        _cts    = new CancellationTokenSource();
        _client = new ChronosTcpClient(new ClientOptions
        {
            UseTls                = UseTls,
            SkipTlsCertValidation = SkipTlsCert,
            UseHmac               = UseHmac,
            HmacSecret            = DefaultHmacSecret,
        });

        try
        {
            await _client.ConnectAsync(DefaultHost, DefaultPort, _cts.Token);
            ShowStatusToast("Authenticating...");

            // client_id = 1 (fixed; game server sẽ quản lý đúng khi cần)
            var result = await _client.LoginAsync(
                serverId:  _currentServerId,
                clientId:  1,
                username:  username,
                password:  password,
                ct:        _cts.Token);

            if (!result.Ok)
            {
                ShowStatusToast($"Login failed: {result.Error}", isError: true);
                SetUiBusy(false);
                _loginForm.Visible = true;
                return;
            }

            // ── Login thành công ──────────────────────────────────────────
            _session = result;
            GD.Print($"[LoginScreen] Logged in. user_id={result.UserId} session={result.SessionIdEcho}");

            OnLoginSuccess(username, result);
        }
        catch (OperationCanceledException)
        {
            GD.Print("[LoginScreen] Login cancelled.");
        }
        catch (Exception ex)
        {
            ShowStatusToast($"Error: {ex.Message}", isError: true);
            GD.PrintErr($"[LoginScreen] Login error: {ex}");
            _loginForm.Visible = true;
        }
        finally
        {
            SetUiBusy(false);
        }
    }

    /// <summary>
    /// Cập nhật UI sau khi login thành công.
    /// Hiện thông tin session và nút Logout.
    /// </summary>
    private void OnLoginSuccess(string username, LoginResult result)
    {
        // Cập nhật form: ẩn New Player / Change Account; hiện Logout
        _btnNewPlayer.SetEnabled(false);
        _btnLogout.Visible = true;
        _btnLogout.SetEnabled(true);

        // Hiện panel tóm tắt tài khoản
        ShowLoggedInPanel(username, result);

        _loginForm.Visible = true;
    }

    /// <summary>
    /// Gửi OP_LOGOUT với session_id hiện tại.
    /// Session sẽ bị xoá ở server và client sau khi thành công.
    /// </summary>
    private async void OnLogoutPressed()
    {
        if (_client is null || _session is null) return;

        SetUiBusy(true);
        try
        {
            await _client.LogoutAsync(_cts?.Token ?? CancellationToken.None);
            GD.Print("[LoginScreen] Logged out.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LoginScreen] Logout error: {ex.Message}");
        }
        finally
        {
            _session = null;
            _client?.Dispose();
            _client = null;

            _btnLogout.Visible = false;
            _btnLogout.SetEnabled(false);
            _btnNewPlayer.SetEnabled(true);

            _loggedInPanel?.QueueFree();
            _loggedInPanel = null;

            SetUiBusy(false);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Logged-in info panel
    // ═════════════════════════════════════════════════════════════════════

    private void ShowLoggedInPanel(string username, LoginResult result)
    {
        _loggedInPanel?.QueueFree();
        _loggedInPanel = new PanelManager();
        AddChild(_loggedInPanel);

        var viewSize  = GetViewportRect().Size;
        var panelSize = new Vector2(300, 160);
        // Góc dưới phải màn hình
        var panelPos  = new Vector2(viewSize.X - panelSize.X - 20, viewSize.Y - panelSize.Y - 20);

        var loggedCfg = new PanelManager.PanelConfig
        {
            BackgroundColor       = new Color("#1a1208", 0.92f),
            BorderColor           = new Color("#c8a84b"),
            CornerColor           = new Color("#f0d080"),
            TitleColor            = new Color("#f0d080"),
            TitleFontSize         = 14,
            PatchMargin           = 12,
            ContentMargin         = 20,
            ContentMarginLeft     = 20,
            ContentMarginTop      = 16,
            ContentSeparation     = 8,
            FontFamily            = "Montserrat",
            FontStyle             = FontStyle.Regular,
            ShowCornerDecorations = true,
        };

        var data = _loggedInPanel.CreatePanel(panelPos, panelSize, style: loggedCfg);
        var vbox = (VBoxContainer)data.ContentArea;

        void AddRow(string label, string value)
        {
            var lbl = new Label { Text = $"{label}: {value}", AutowrapMode = TextServer.AutowrapMode.Off };
            lbl.AddThemeColorOverride("font_color", new Color("#f0d080"));
            FontSystem.Instance?.ApplyFont(lbl, "Montserrat", FontStyle.SemiBold, 13);
            vbox.AddChild(lbl);
        }

        AddRow("User",    username);
        AddRow("Gold",    result.Gold.ToString("N0"));
        AddRow("VND",     result.Vnd.ToString("N0"));
        AddRow("Server",  _currentServer);
        AddRow("Session", result.SessionIdEcho.ToString("X8"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Select Server panel
    // ═════════════════════════════════════════════════════════════════════

    private void OpenServerPanel()
    {
        _loginForm.Visible = false;

        _serverPanel = new Control
        {
            AnchorRight  = 1f,
            AnchorBottom = 1f,
            OffsetRight  = 0f,
            OffsetBottom = 0f,
        };
        AddChild(_serverPanel);

        var viewSize  = GetViewportRect().Size;
        var panelSize = new Vector2(420, 300);
        var panelPos  = (viewSize - panelSize) / 2f;

        _serverPanel.AddChild(new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            Size  = viewSize,
        });

        var panel = new Control { Position = panelPos, Size = panelSize };
        _serverPanel.AddChild(panel);

        var title = new Label
        {
            Text                = "Select Server",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Size                = new Vector2(panelSize.X, 70),
            Position            = new Vector2(0, 16),
        };
        title.AddThemeColorOverride("font_color", new Color("#f0d080"));
        FontSystem.Instance?.ApplyFont(title, "Edo", FontStyle.Regular, 22);
        panel.AddChild(title);

        var serverBtnCfg = new ButtonManager.ButtonConfig
        {
            DefaultSize             = new Vector2(120, 44),
            Size                    = new Vector2(120, 44),
            NormalBackgroundColor   = new Color("#2a1f0e"),
            HoverBackgroundColor    = new Color("#3a2f1e"),
            PressedBackgroundColor  = new Color("#1a1208"),
            DisabledBackgroundColor = new Color("#1a1510"),
            NormalBorderColor       = new Color("#c8a84b"),
            HoverBorderColor        = new Color("#f0d080"),
            PressedBorderColor      = new Color("#a08030"),
            DisabledBorderColor     = new Color("#4a3d20"),
            NormalTextColor         = new Color("#f0d080"),
            HoverTextColor          = new Color("#ffe8a0"),
            PressedTextColor        = new Color("#c8a84b"),
            DisabledTextColor       = new Color("#6a5a30"),
            FontFamily              = "Edo",
            FontStyle               = FontStyle.Regular,
            FontSize                = 14,
        };

        // Grid 2×3
        var grid = new VBoxContainer();
        grid.AddThemeConstantOverride("separation", 12);
        grid.Position = new Vector2(0, 90);
        grid.Size     = new Vector2(panelSize.X, panelSize.Y - 150);
        panel.AddChild(grid);

        // Ánh xạ tên server → server_id (1-based)
        string[][] rows =
        {
            new[] { "Server 1", "Server 2", "Server 3" },
            new[] { "Server 4", "Server 5", "Server 6" },
        };

        foreach (var row in rows)
        {
            var hbox = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
            };
            hbox.AddThemeConstantOverride("separation", 12);

            foreach (var serverName in row)
            {
                var name = serverName;
                var btn  = new ButtonManager();
                btn.Setup(name, null, serverBtnCfg, () => OnServerSelected(name));
                hbox.AddChild(btn);
            }
            grid.AddChild(hbox);
        }

        var cancelRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            Position  = new Vector2(0, panelSize.Y - 56),
            Size      = new Vector2(panelSize.X, 48),
        };
        panel.AddChild(cancelRow);

        var cancelBtn = new ButtonManager();
        cancelBtn.Setup("Cancel", new Vector2(120, 40), serverBtnCfg, CloseServerPanel);
        cancelRow.AddChild(cancelBtn);
    }

    private void OnServerSelected(string serverName)
    {
        _currentServer   = serverName;
        _currentServerId = ParseServerId(serverName);
        _btnChangeServer.SetText(serverName);
        CloseServerPanel();
    }

    private void CloseServerPanel()
    {
        _serverPanel?.QueueFree();
        _serverPanel       = null;
        _loginForm.Visible = true;
    }

    /// <summary>Trích server_id từ tên "Server N" → N.</summary>
    private static int ParseServerId(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[^1], out int id))
            return id;
        return 1;
    }

    // ═════════════════════════════════════════════════════════════════════
    // UI helpers
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Khoá / mở toàn bộ form khi đang thực hiện network call.</summary>
    private void SetUiBusy(bool busy)
    {
        if (_loginForm is null) return;
        foreach (var child in _loginForm.GetChildren())
        {
            if (child is ButtonManager bm)
                bm.SetEnabled(!busy);
        }
    }

    private Label? _toastLabel;

    /// <summary>Hiện thông báo trạng thái tạm thời trên màn hình.</summary>
    private void ShowStatusToast(string message, bool isError = false)
    {
        if (_toastLabel is null)
        {
            _toastLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode        = TextServer.AutowrapMode.WordSmart,
                Position            = new Vector2(GetViewportRect().Size.X / 2f - 200, 20),
                CustomMinimumSize   = new Vector2(400, 30),
                ZIndex              = 50,
            };
            FontSystem.Instance?.ApplyFont(_toastLabel, "Montserrat", FontStyle.SemiBold, 14);
            AddChild(_toastLabel);
        }

        _toastLabel.Text = message;
        _toastLabel.AddThemeColorOverride(
            "font_color",
            isError ? new Color("#ff6b6b") : new Color("#f0d080"));
        _toastLabel.Visible = true;
    }

    // ═════════════════════════════════════════════════════════════════════
    // ParallaxLayer helper (không đổi)
    // ═════════════════════════════════════════════════════════════════════

    private record LayerConfig(
        string Name, string TexturePath, Vector2 Direction,
        float SpeedScale, float OffsetLeft, float OffsetTop,
        float OffsetRight, float OffsetBottom, int StretchMode);

    private sealed class ParallaxLayer
    {
        private readonly TextureRect  _rect;
        private readonly bool         _hasShader;
        private readonly ShaderMaterial? _mat;
        private const float           ResetInterval = 1000f;

        public ParallaxLayer(LayerConfig cfg)
        {
            _hasShader = cfg.Direction != Vector2.Zero || cfg.SpeedScale > 0f;
            _mat       = _hasShader ? BuildMaterial(cfg.Direction, cfg.SpeedScale) : null;
            _rect      = new TextureRect
            {
                Name          = cfg.Name,
                Texture       = GD.Load<Texture2D>(cfg.TexturePath),
                Material      = _mat,
                StretchMode   = (TextureRect.StretchModeEnum)cfg.StretchMode,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                OffsetLeft    = cfg.OffsetLeft,
                OffsetTop     = cfg.OffsetTop,
                OffsetRight   = cfg.OffsetRight,
                OffsetBottom  = cfg.OffsetBottom,
            };
        }

        public void AttachTo(Node parent) => parent.AddChild(_rect);

        public void ResetTimeOffset()
        {
            if (!_hasShader || _mat is null) return;
            float t = (float)Time.GetTicksMsec() / 1000f;
            _mat.SetShaderParameter("time_offset", t % ResetInterval);
        }

        private static ShaderMaterial BuildMaterial(Vector2 dir, float speed)
        {
            var mat = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://shaders/parallax_scroll.gdshader")
            };
            mat.SetShaderParameter("direction",   dir);
            mat.SetShaderParameter("speed_scale", speed);
            mat.SetShaderParameter("time_offset", 0f);
            return mat;
        }
    }
}