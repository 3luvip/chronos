using Godot;
using System;

public partial class ChangeServerPanel : Node2D
{
    private PanelManager _panelManager;
    private PanelManager.PanelData _panelData;
    private Action<string> _onServerSelected;

    private static readonly ButtonManager.ButtonConfig BtnConfig = new()
    {
        DefaultSize             = new Vector2(160, 44),
        Size                    = new Vector2(160, 44),
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
        FontSize                = 15,
    };

    private static readonly PanelManager.PanelConfig PanelCfg = new()
    {
        BackgroundColor     = new Color("#1a1208"),
        BorderColor         = new Color("#c8a84b"),
        CornerColor         = new Color("#f0d080"),
        TitleColor          = new Color("#f0d080"),
        DescriptionColor    = new Color("#ffe8a0"),
        TitleFontSize       = 22,
        PatchMargin         = 16,
        ContentMargin       = 30,
        ContentMarginTop    = 24,
        ContentSeparation   = 16,
        FontFamily          = "Edo",
        FontStyle           = FontStyle.Regular,
        ShowCornerDecorations = true,
        ButtonDefaults      = BtnConfig,
    };

    public override void _Ready()
    {
        _panelManager = new PanelManager();
        AddChild(_panelManager);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Hiển thị panel chọn server.
    /// onSelected trả về tên server đã chọn cho LoginScreen xử lý.
    /// </summary>
    public void Show(Action<string> onSelected)
    {
        _onServerSelected = onSelected;

        var viewSize  = GetViewportRect().Size;
        var panelSize = new Vector2(420, 320);
        var position  = (viewSize - panelSize) / 2f;

        // Build content thủ công để có layout 2x3
        _panelData = _panelManager.CreatePanel(
            position, panelSize,
            title: "Select Server",
            style: PanelCfg);

        AppendServerGrid(_panelData);
        _panelData.Container.Visible = true;
    }

    public void Hide()
    {
        _panelData?.Container.QueueFree();
        _panelData = null;
    }

    // ── Server grid ───────────────────────────────────────────────────────

    private void AppendServerGrid(PanelManager.PanelData data)
    {
        var vbox = (VBoxContainer)data.ContentArea;

        // 2 hàng, mỗi hàng 3 button
        string[][] rows =
        {
            new[] { "Server 1", "Server 2", "Server 3" },
            new[] { "Server 4", "Server 5", "Server 6" },
        };

        foreach (var row in rows)
        {
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 12);

            foreach (var serverName in row)
            {
                var name   = serverName; // capture
                var btn    = new ButtonManager();
                btn.Setup(name, new Vector2(110, 44), BtnConfig,
                          () => OnServerChosen(name));
                hbox.AddChild(btn);
            }

            vbox.AddChild(hbox);
        }

        // Nút Cancel
        var cancelRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        var cancelBtn = new ButtonManager();
        cancelBtn.Setup("Cancel", new Vector2(110, 40), BtnConfig, Hide);
        cancelRow.AddChild(cancelBtn);
        vbox.AddChild(cancelRow);
    }

    private void OnServerChosen(string serverName)
    {
        _onServerSelected?.Invoke(serverName);
        Hide();
    }
}