using Godot;
using System;

public partial class ButtonManager : Control
{
    public int ButtonBackgroundPath { get; set; } = 0;
    public int ButtonBorderPath { get; set; } = 0;

    private Control buttonPanel;
    private NinePatchRect buttonBackground;
    private NinePatchRect buttonBorder;
    private Label buttonLabel;
    private TextureRect iconRect;
    private Button invisibleButton;
    private Tween tween;

    private ButtonConfig config;
    private bool isEnabled = true;
    private Action onPressedCallback;
    private string buttonText;

    [Signal]
    public delegate void ButtonPressedEventHandler(string text);

    [Signal]
    public delegate void ButtonHoveredEventHandler(bool isHovered);

    public override void _Ready()
    {
        if (buttonPanel == null)
        {
            Setup();
        }
    }

    public void Setup(
        string text = "Button",
        Vector2? size = null,
        ButtonConfig customConfig = null,
        Action onPressed = null)
    {
        config = customConfig ?? new ButtonConfig();
        buttonText = text;
        onPressedCallback = onPressed;
        var buttonSize = size ?? config.DefaultSize;
        CreateButtonStructure(buttonSize, text);
    }

    private void CreateButtonStructure(Vector2 size, string text)
    {
        buttonPanel?.QueueFree();

        buttonPanel = new Control();
        buttonPanel.CustomMinimumSize = size;
        AddChild(buttonPanel);
        string backgroundPath = "res://asset/PNG/Default/Transparent_center/" + ButtonBackgroundPath + ".png";
        if (!string.IsNullOrEmpty(backgroundPath))
        {
            buttonBackground = CreateNinePatchRect(
                backgroundPath,
                size,
                config.NormalBackgroundColor,
                config.PatchMargin
            );
            buttonPanel.AddChild(buttonBackground);
        }
        string boderPath = "res://asset/PNG/Default/Border/" + ButtonBorderPath + ".png";
        if (!string.IsNullOrEmpty(boderPath))
        {
            buttonBorder = CreateNinePatchRect(
                boderPath,
                size,
                config.NormalBorderColor,
                config.PatchMargin
            );
            buttonPanel.AddChild(buttonBorder);
        }

        var contentContainer = new HBoxContainer
        {
            Position = Vector2.Zero,
            Size = size,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        contentContainer.AddThemeConstantOverride("separation", config.IconMargin);

        if (config.Icon != null)
        {
            iconRect = new TextureRect
            {
                Texture = config.Icon,
                Modulate = config.IconModulate,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            contentContainer.AddChild(iconRect);
        }

        buttonLabel = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.Off,
            VerticalAlignment = VerticalAlignment.Center
        };
        buttonLabel.AddThemeColorOverride("font_color", config.NormalTextColor);
        FontSystem.Instance?.ApplyFont(buttonLabel, config.FontFamily, config.FontStyle, config.FontSize);
        contentContainer.AddChild(buttonLabel);

        buttonPanel.AddChild(contentContainer);

        invisibleButton = new Button
        {
            Position = Vector2.Zero,
            Size = size,
            Flat = true,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };

        ConnectButtonSignals();
        buttonPanel.AddChild(invisibleButton);

        if (!isEnabled)
        {
            SetEnabled(false);
        }
    }

    private NinePatchRect CreateNinePatchRect(string path, Vector2 size, Color color, int margin)
    {
        var rect = new NinePatchRect
        {
            Texture = GD.Load<Texture2D>(path),
            Size = size,
            Position = Vector2.Zero,
            Modulate = color
        };
        rect.PatchMarginLeft = margin;
        rect.PatchMarginTop = margin;
        rect.PatchMarginRight = margin;
        rect.PatchMarginBottom = margin;
        return rect;
    }

    private void ConnectButtonSignals()
    {
        invisibleButton.MouseEntered += OnHoverStart;
        invisibleButton.MouseExited += OnHoverEnd;
        invisibleButton.ButtonDown += OnButtonPressed;
        invisibleButton.ButtonUp += OnButtonReleased;
        invisibleButton.Pressed += OnButtonClicked;
    }

    private void OnHoverStart()
    {
        if (!isEnabled) return;

        EmitSignal(nameof(ButtonHovered), true);

        if (config.EnableColorAnimation)
        {
            AnimateToState(
                config.HoverBackgroundColor,
                config.HoverBorderColor,
                config.HoverTextColor,
                config.HoverAnimationDuration
            );
        }
    }

    private void OnHoverEnd()
    {
        if (!isEnabled) return;

        EmitSignal(nameof(ButtonHovered), false);

        if (config.EnableColorAnimation)
        {
            AnimateToState(
                config.NormalBackgroundColor,
                config.NormalBorderColor,
                config.NormalTextColor,
                config.HoverAnimationDuration
            );
        }
    }

    private void OnButtonPressed()
    {
        if (!isEnabled) return;

        if (config.EnableColorAnimation)
        {
            AnimateToState(
                config.PressedBackgroundColor,
                config.PressedBorderColor,
                config.PressedTextColor,
                config.PressAnimationDuration
            );
        }

        if (config.EnableScaleAnimation)
        {
            AnimateScale(new Vector2(config.PressedScale, config.PressedScale), config.PressAnimationDuration);
        }
    }

    private void OnButtonReleased()
    {
        if (!isEnabled) return;

        bool isHovered = invisibleButton.IsHovered();

        if (config.EnableColorAnimation)
        {
            var bgColor = isHovered ? config.HoverBackgroundColor : config.NormalBackgroundColor;
            var borderColor = isHovered ? config.HoverBorderColor : config.NormalBorderColor;
            var textColor = isHovered ? config.HoverTextColor : config.NormalTextColor;

            AnimateToState(bgColor, borderColor, textColor, config.ReleaseAnimationDuration);
        }

        if (config.EnableScaleAnimation)
        {
            AnimateScale(Vector2.One, config.ReleaseAnimationDuration);
        }
    }

    private void OnButtonClicked()
    {
        if (!isEnabled) return;

        EmitSignal(nameof(ButtonPressed), buttonText);
        onPressedCallback?.Invoke();
        GD.Print($"Button '{buttonText}' pressed!");
    }

    private void AnimateToState(Color bgColor, Color borderColor, Color textColor, float duration)
    {
        tween?.Kill();
        tween = CreateTween();
        tween.SetParallel(true);
        tween.SetEase(Tween.EaseType.Out);
        tween.SetTrans(Tween.TransitionType.Cubic);

        if (buttonBackground != null)
            tween.TweenProperty(buttonBackground, "modulate", bgColor, duration);
        if (buttonBorder != null)
            tween.TweenProperty(buttonBorder, "modulate", borderColor, duration);
        if (buttonLabel != null)
            tween.TweenProperty(buttonLabel, "theme_override_colors/font_color", textColor, duration);
    }

    private void AnimateScale(Vector2 scale, float duration)
    {
        if (buttonPanel == null) return;

        var scaleTween = CreateTween();
        scaleTween.SetEase(Tween.EaseType.Out);
        scaleTween.SetTrans(Tween.TransitionType.Back);
        scaleTween.TweenProperty(buttonPanel, "scale", scale, duration);
    }

    public void SetEnabled(bool enabled)
    {
        isEnabled = enabled;

        if (invisibleButton != null)
        {
            invisibleButton.Disabled = !enabled;
            invisibleButton.MouseDefaultCursorShape = enabled
                ? Control.CursorShape.PointingHand
                : Control.CursorShape.Arrow;
        }

        if (!enabled && config != null)
        {
            if (buttonBackground != null)
                buttonBackground.Modulate = config.DisabledBackgroundColor;
            if (buttonBorder != null)
                buttonBorder.Modulate = config.DisabledBorderColor;
            if (buttonLabel != null)
                buttonLabel.AddThemeColorOverride("font_color", config.DisabledTextColor);
        }
    }

    public void SetText(string text)
    {
        buttonText = text;
        if (buttonLabel != null)
        {
            buttonLabel.Text = text;
        }
    }

    public void SetIcon(Texture2D icon, Color? modulate = null)
    {
        if (config != null)
        {
            config.Icon = icon;
            if (modulate.HasValue)
                config.IconModulate = modulate.Value;
        }

        if (iconRect != null)
        {
            iconRect.Texture = icon;
            if (modulate.HasValue)
                iconRect.Modulate = modulate.Value;
        }
        else if (icon != null)
        {
            CreateButtonStructure(buttonPanel.CustomMinimumSize, buttonText);
        }
    }

    public override void _ExitTree()
    {
        tween?.Kill();
        base._ExitTree();
    }
    public class ButtonConfig
    {
        public Color NormalBackgroundColor { get; set; } = new Color("#3a3330");
        public Color HoverBackgroundColor { get; set; } = new Color("#4a4340");
        public Color PressedBackgroundColor { get; set; } = new Color("#5a5350");
        public Color DisabledBackgroundColor { get; set; } = new Color("#2a2220");

        public Color NormalBorderColor { get; set; } = new Color("#6b5d54");
        public Color HoverBorderColor { get; set; } = new Color("#8b7d74");
        public Color PressedBorderColor { get; set; } = new Color("#9b8d84");
        public Color DisabledBorderColor { get; set; } = new Color("#4b3d34");

        public Color NormalTextColor { get; set; } = new Color("#c4b5a0");
        public Color HoverTextColor { get; set; } = new Color("#d4c5b0");
        public Color PressedTextColor { get; set; } = new Color("#e4d5c0");
        public Color DisabledTextColor { get; set; } = new Color("#847560");

        public int PatchMargin { get; set; } = 8;
        public int FontSize { get; set; } = 16;
        public Vector2 DefaultSize { get; set; } = new Vector2(160, 40);

        public float HoverAnimationDuration { get; set; } = 0.2f;
        public float PressAnimationDuration { get; set; } = 0.1f;
        public float ReleaseAnimationDuration { get; set; } = 0.15f;
        public float PressedScale { get; set; } = 0.95f;
        public bool EnableScaleAnimation { get; set; } = true;
        public bool EnableColorAnimation { get; set; } = true;

        public Texture2D Icon { get; set; }
        public Color IconModulate { get; set; } = Colors.White;
        public int IconMargin { get; set; } = 8;

        public string Text { get; set; }
        public Vector2? CustomPosition { get; set; } // Vị trí tùy chỉnh (null = tự động)
        public Vector2 Size { get; set; } = new Vector2(120, 40);
        public Action OnPressed { get; set; }

        public string    FontFamily { get; set; } = "Montserrat";
        public FontStyle FontStyle  { get; set; } = FontStyle.SemiBold;
    }

}
