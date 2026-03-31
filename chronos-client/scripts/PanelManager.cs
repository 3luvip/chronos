using Godot;
using System;
using System.Collections.Generic;

public partial class PanelManager : Control
{
    // ── Asset paths ───────────────────────────────────────────────────────
    private const string TRANSPARENT_CENTER_PATH = "res://asset/PNG/Default/Transparent_center/";
    private const string BORDER_PATH             = "res://asset/PNG/Default/Border/";
    private const string PNG                     = ".png";

    public int BorderIndex          { get; set; } = 3;
    public int TransparentCenterIndex { get; set; } = 0;

    private readonly Dictionary<string, Texture2D> _textureCache = new();

    // ── PanelConfig ───────────────────────────────────────────────────────
    public class PanelConfig
    {
        public Color BackgroundColor     { get; set; } = new Color("#1a2332");
        public Color BorderColor         { get; set; } = new Color("#6b5d54");
        public Color CornerColor         { get; set; } = new Color("#8b7355");
        public Color TitleColor          { get; set; } = new Color("#c4b5a0");
        public Color DescriptionColor    { get; set; } = new Color("#b8a89c");

        public int   TitleFontSize       { get; set; } = 20;
        public int   DescriptionFontSize { get; set; } = 14;
        public int   PatchMargin         { get; set; } = 16;
        public int   ContentMargin       { get; set; } = 40;
        public int   ContentMarginTop    { get; set; } = 30;
        public int   ContentMarginLeft   { get; set; } = 30;
        public int ContentSeparation { get; set; } = 15;

        public string    FontFamily      { get; set; } = "Montserrat";
        public FontStyle FontStyle       { get; set; } = FontStyle.Regular;

        public bool ShowCornerDecorations { get; set; } = true;

        // Button defaults dùng khi không truyền ButtonConfig riêng
        public ButtonManager.ButtonConfig ButtonDefaults { get; set; } = new();
    }

    // ── PanelData ─────────────────────────────────────────────────────────
    public class PanelData
    {
        public Control        Container        { get; set; }
        public NinePatchRect  Background       { get; set; }
        public NinePatchRect  Border           { get; set; }
        public Label          TitleLabel       { get; set; }
        public Label          DescriptionLabel { get; set; }
        public Control        ContentArea      { get; set; }
        public Control        ButtonContainer  { get; set; }
        public List<ButtonManager> Buttons          { get; set; } = new();
        public PanelConfig    Style            { get; set; }
    }

    public enum ButtonPlacement
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
        Custom 
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    public override void _Ready()
    {
        LoadTexture($"{TRANSPARENT_CENTER_PATH}{TransparentCenterIndex}{PNG}");
        LoadTexture($"{BORDER_PATH}{BorderIndex}{PNG}");
    }

    // ── Public API ────────────────────────────────────────────────────────

    
    /// Tạo panel với title + description + buttons đơn giản (string[]).
    
    public PanelData CreatePanel(
        Vector2         position,
        Vector2         size,
        string          title           = "",
        string          description     = "",
        string[]        buttons         = null,
        PanelConfig     style           = null,
        Action<string>  onButtonPressed = null,
        ButtonPlacement placement       = ButtonPlacement.BottomCenter,
        Vector2?        buttonOffset    = null,
        int             buttonSeparation = 10)
    {
        style ??= new PanelConfig();
        var data = BuildBase(position, size, style);

        AppendLabels(data, title, description, style);

        if (buttons?.Length > 0)
            AttachSimpleButtons(data, buttons, size, style,
                                onButtonPressed, placement,
                                buttonOffset ?? Vector2.Zero, buttonSeparation);

        FinalizePanel(data, size, style);
        return data;
    }

    /// Tạo panel với ButtonConfig[] — hỗ trợ custom position, icon, animation riêng từng button.
    public PanelData CreatePanelAdvanced(
        Vector2         position,
        Vector2         size,
        string          title           = "",
        string          description     = "",
        ButtonManager.ButtonConfig[]  buttonConfigs   = null,
        PanelConfig     style           = null,
        ButtonPlacement placement       = ButtonPlacement.BottomCenter,
        int             buttonSeparation = 10)
    {
        style ??= new PanelConfig();
        var data = BuildBase(position, size, style);

        AppendLabels(data, title, description, style);

        if (buttonConfigs?.Length > 0)
            AttachAdvancedButtons(data, buttonConfigs, size, style, placement, buttonSeparation);

        FinalizePanel(data, size, style);
        return data;
    }

    
    
    public async void ShowNotification(
        string      message,
        float       duration = 3.0f,
        Vector2?    position = null,
        PanelConfig style    = null)
    {
        var pos = position ?? new Vector2(GetViewportRect().Size.X / 2 - 200, 50);
        var panel = CreatePanel(pos, new Vector2(400, 100),
                                description: message, style: style ?? new PanelConfig());
        try
        {
            panel.Container.Modulate = Colors.Transparent;

            var tweenIn = CreateTween();
            tweenIn.TweenProperty(panel.Container, "modulate:a", 1f, 0.3f);
            await ToSignal(tweenIn, Tween.SignalName.Finished);

            await ToSignal(GetTree().CreateTimer(duration), SceneTreeTimer.SignalName.Timeout);

            var tweenOut = CreateTween();
            tweenOut.TweenProperty(panel.Container, "modulate:a", 0f, 0.3f);
            await ToSignal(tweenOut, Tween.SignalName.Finished);
        }
        finally
        {
            panel.Container.QueueFree();
        }
    }

    // ── Build pipeline ────────────────────────────────────────────────────

    private PanelData BuildBase(Vector2 position, Vector2 size, PanelConfig style)
    {
        var data = new PanelData { Style = style };

        data.Container = new Control { Position = position, Size = size };
        AddChild(data.Container);

        // Visuals
        var bgTex = LoadTexture($"{TRANSPARENT_CENTER_PATH}{TransparentCenterIndex}{PNG}");
        if (bgTex != null)
        {
            data.Background = MakeNinePatch(bgTex, size, style.BackgroundColor, style.PatchMargin);
            data.Container.AddChild(data.Background);
        }

        var borderTex = LoadTexture($"{BORDER_PATH}{BorderIndex}{PNG}");
        if (borderTex != null)
        {
            data.Border = MakeNinePatch(borderTex, size, style.BorderColor, style.PatchMargin);
            data.Container.AddChild(data.Border);
        }

        // Content area
        var margin = new MarginContainer { Position = Vector2.Zero, Size = size };
        margin.AddThemeConstantOverride("margin_left",   style.ContentMargin);
        margin.AddThemeConstantOverride("margin_right",  style.ContentMargin);
        margin.AddThemeConstantOverride("margin_top",    style.ContentMarginTop);
        margin.AddThemeConstantOverride("margin_bottom", style.ContentMarginTop);
        margin.AddThemeConstantOverride("margin_left",   style.ContentMarginLeft);
        data.Container.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", style.ContentSeparation);
        margin.AddChild(vbox);
        data.ContentArea = vbox;

        return data;
    }

    private void AppendLabels(PanelData data, string title, string description, PanelConfig style)
    {
        var vbox = (VBoxContainer)data.ContentArea;

        if (!string.IsNullOrEmpty(title))
        {
            data.TitleLabel = MakeLabel(title, style.TitleColor, style.TitleFontSize,
                                        style.FontFamily, style.FontStyle,
                                        HorizontalAlignment.Center,
                                        TextServer.AutowrapMode.Off);
            vbox.AddChild(data.TitleLabel);
        }

        if (!string.IsNullOrEmpty(description))
        {
            data.DescriptionLabel = MakeLabel(description, style.DescriptionColor,
                                              style.DescriptionFontSize,
                                              style.FontFamily, style.FontStyle,
                                              HorizontalAlignment.Center,
                                              TextServer.AutowrapMode.WordSmart);
            vbox.AddChild(data.DescriptionLabel);
        }
    }

    private void FinalizePanel(PanelData data, Vector2 size, PanelConfig style)
    {
        if (style.ShowCornerDecorations)
            AddCornerDecorations(data.Container, size, style.CornerColor);
    }

    // ── Button attachment ─────────────────────────────────────────────────

    private void AttachSimpleButtons(
        PanelData      data,
        string[]       texts,
        Vector2        panelSize,
        PanelConfig    style,
        Action<string> onPressed,
        ButtonPlacement placement,
        Vector2        offset,
        int            separation)
    {
        var cfg       = style.ButtonDefaults;
        float totalW  = texts.Length * cfg.DefaultSize.X + (texts.Length - 1) * separation;

        var container = new HBoxContainer();
        container.AddThemeConstantOverride("separation", separation);

        foreach (var text in texts)
        {
            var btn = new ButtonManager();
            btn.Setup(text, cfg.DefaultSize, cfg, () => onPressed?.Invoke(text));
            data.Buttons.Add(btn);
            container.AddChild(btn);
        }


        container.Position = Clamp(
            CalcPlacement(placement, panelSize, totalW, cfg.DefaultSize.Y) + offset,
            new Vector2(totalW, cfg.DefaultSize.Y), panelSize);

        data.ButtonContainer = container;
        data.Container.AddChild(container);
    }

    private void AttachAdvancedButtons(
        PanelData       data,
        ButtonManager.ButtonConfig[]  configs,
        Vector2         panelSize,
        PanelConfig     style,
        ButtonPlacement placement,
        int             separation)
    {
        var container = new Control { Size = panelSize };
        data.Container.AddChild(container);
        data.ButtonContainer = container;

        // Tính tổng width của các auto-positioned buttons
        float autoW = 0; int autoN = 0;
        foreach (var c in configs)
        {
            if (!c.CustomPosition.HasValue) { autoW += c.Size.X; autoN++; }
        }
        autoW += Math.Max(0, autoN - 1) * separation;

        float startX = CalcPlacement(placement, panelSize, autoW, configs[0].Size.Y).X;
        float startY = CalcPlacement(placement, panelSize, autoW, configs[0].Size.Y).Y;
        float curX   = startX;

        foreach (var c in configs)
        {
            var btn = new ButtonManager();
            btn.Setup(c.Text, c.Size, c, c.OnPressed);

            btn.Position = c.CustomPosition.HasValue
                ? Clamp(c.CustomPosition.Value, c.Size, panelSize)
                : new Vector2(curX, startY);

            if (!c.CustomPosition.HasValue) curX += c.Size.X + separation;

            container.AddChild(btn);
            data.Buttons.Add(btn);
        }
    }

    // ── Node factories ────────────────────────────────────────────────────

    private Label MakeLabel(
        string                  text,
        Color                   color,
        int                     size,
        string                  family,
        FontStyle               style,
        HorizontalAlignment     hAlign,
        TextServer.AutowrapMode wrap)
    {
        var label = new Label
        {
            Text                = text,
            AutowrapMode        = wrap,
            HorizontalAlignment = hAlign,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", color);
        FontSystem.Instance?.ApplyFont(label, family, style, size);
        return label;
    }

    private NinePatchRect MakeNinePatch(Texture2D tex, Vector2 size, Color modulate, int margin)
    {
        var r = new NinePatchRect
        {
            Texture  = tex, Size = size,
            Position = Vector2.Zero, Modulate = modulate,
            PatchMarginLeft   = margin, PatchMarginRight  = margin,
            PatchMarginTop    = margin, PatchMarginBottom = margin,
        };
        return r;
    }

    private void AddCornerDecorations(Control parent, Vector2 s, Color color)
    {
        ReadOnlySpan<(Vector2 pos, Vector2 size)> corners = stackalloc (Vector2, Vector2)[]
        {
            (new Vector2(0,        0       ), new Vector2(3,  20)),
            (new Vector2(0,        0       ), new Vector2(20, 3 )),
            (new Vector2(s.X - 3,  0       ), new Vector2(3,  20)),
            (new Vector2(s.X - 20, 0       ), new Vector2(20, 3 )),
            (new Vector2(0,        s.Y - 20), new Vector2(3,  20)),
            (new Vector2(0,        s.Y - 3 ), new Vector2(20, 3 )),
            (new Vector2(s.X - 3,  s.Y - 20), new Vector2(3,  20)),
            (new Vector2(s.X - 20, s.Y - 3 ), new Vector2(20, 3 )),
        };
        foreach (var (pos, sz) in corners)
            parent.AddChild(new ColorRect { Color = color, Size = sz, Position = pos });
    }

    // ── Layout math ───────────────────────────────────────────────────────

    private static Vector2 CalcPlacement(ButtonPlacement p, Vector2 panel, float w, float h)
    {
        const float M = 20f;
        float cx = (panel.X - w) / 2f;
        float cy = (panel.Y - h) / 2f;
        return p switch
        {
            ButtonPlacement.TopLeft      => new Vector2(M,          M),
            ButtonPlacement.TopCenter    => new Vector2(cx,         M),
            ButtonPlacement.TopRight     => new Vector2(panel.X - w - M, M),
            ButtonPlacement.MiddleLeft   => new Vector2(M,          cy),
            ButtonPlacement.MiddleCenter => new Vector2(cx,         cy),
            ButtonPlacement.MiddleRight  => new Vector2(panel.X - w - M, cy),
            ButtonPlacement.BottomLeft   => new Vector2(M,          panel.Y - h - M),
            ButtonPlacement.BottomCenter => new Vector2(cx,         panel.Y - h - M),
            ButtonPlacement.BottomRight  => new Vector2(panel.X - w - M, panel.Y - h - M),
            _                            => new Vector2(cx,         panel.Y - h - M),
        };
    }

    private static Vector2 Clamp(Vector2 pos, Vector2 btnSize, Vector2 panel, float pad = 10f) =>
        new(Mathf.Clamp(pos.X, pad, panel.X - btnSize.X - pad),
            Mathf.Clamp(pos.Y, pad, panel.Y - btnSize.Y - pad));

    // ── Texture cache ─────────────────────────────────────────────────────

    private Texture2D LoadTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_textureCache.TryGetValue(path, out var hit)) return hit;
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"[PanelManager] Texture not found: {path}");
            return null;
        }
        var tex = GD.Load<Texture2D>(path);
        if (tex != null) _textureCache[path] = tex;
        return tex;
    }
}