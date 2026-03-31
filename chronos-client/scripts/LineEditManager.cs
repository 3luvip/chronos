using Godot;
using System;
using System.Text.RegularExpressions;

public partial class LineEditManager : Control
{

    private LineEdit lineEdit;
        private Label label;
        private TextureRect iconRect;
        private Button clearButton;
        private Label errorLabel;
        private Control container;

        private LineEditConfig config;
        private bool isValid = true;
        private Action<string> onTextChanged;
        private Action<string> onTextSubmitted;
        private Func<string, bool> customValidator;

        [Signal]
        public delegate void TextChangedEventHandler(string newText);

        [Signal]
        public delegate void TextSubmittedEventHandler(string text);

        [Signal]
        public delegate void ValidationChangedEventHandler(bool isValid);

        public string Text
        {
            get => lineEdit?.Text ?? "";
            set
            {
                if (lineEdit != null)
                    lineEdit.Text = value;
            }
        }

        public bool IsValid => isValid;

        public override void _Ready()
        {
            if (lineEdit == null)
            {
                Setup();
            }
        }

        public void Setup(
            string labelText = "",
            string placeholder = "",
            LineEditConfig customConfig = null,
            Action<string> onChanged = null,
            Action<string> onSubmitted = null,
            Func<string, bool> validator = null)
        {
            config = customConfig ?? new LineEditConfig();
            onTextChanged = onChanged;
            onTextSubmitted = onSubmitted;
            customValidator = validator;

            CreateStructure(labelText, placeholder);
        }

        private void CreateStructure(string labelText, string placeholder)
        {
            container?.QueueFree();

            container = new Control
            {
                CustomMinimumSize = new Vector2(config.Width, config.TotalHeight)
            };
            AddChild(container);

            float currentY = 0;

            // Label (optional)
            if (!string.IsNullOrEmpty(labelText))
            {
                label = CreateLabel(labelText);
                label.Position = new Vector2(0, currentY);
                container.AddChild(label);
                currentY += config.LabelHeight + config.LabelSpacing;
            }

            // Input container
            var inputContainer = CreateInputContainer(currentY, placeholder);
            container.AddChild(inputContainer);
            currentY += config.InputHeight + config.ErrorSpacing;

            // Error label
            if (config.ShowErrorLabel)
            {
                errorLabel = CreateErrorLabel();
                errorLabel.Position = new Vector2(0, currentY);
                errorLabel.Visible = false;
                container.AddChild(errorLabel);
            }
        }

        private Label CreateLabel(string text)
        {
            var lbl = new Label
            {
                Text = text,
                CustomMinimumSize = new Vector2(config.Width, config.LabelHeight),
                HorizontalAlignment = config.LabelAlignment
            };
            lbl.AddThemeColorOverride("font_color", config.LabelColor);
            lbl.AddThemeFontSizeOverride("font_size", config.LabelFontSize);
            return lbl;
        }

        private Control CreateInputContainer(float yPosition, string placeholder)
        {
            var inputCont = new Control
            {
                Position = new Vector2(0, yPosition),
                CustomMinimumSize = new Vector2(config.Width, config.InputHeight)
            };

            // Background
            var background = CreateBackground();
            inputCont.AddChild(background);

            // Icon (optional)
            float leftPadding = config.ContentPadding;
            if (config.Icon != null)
            {
                iconRect = new TextureRect
                {
                    Texture = config.Icon,
                    Position = new Vector2(config.IconPadding, (config.InputHeight - config.IconSize) / 2),
                    CustomMinimumSize = new Vector2(config.IconSize, config.IconSize),
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Modulate = config.IconColor
                };
                inputCont.AddChild(iconRect);
                leftPadding = config.IconPadding + config.IconSize + config.IconSpacing;
            }

            // LineEdit
            float rightPadding = config.ContentPadding;
            if (config.ShowClearButton)
            {
                rightPadding = config.ClearButtonSize + config.ContentPadding * 2;
            }

            lineEdit = CreateLineEdit(placeholder, leftPadding, rightPadding);
            inputCont.AddChild(lineEdit);

            // Clear button (optional)
            if (config.ShowClearButton)
            {
                clearButton = CreateClearButton();
                clearButton.Position = new Vector2(
                    config.Width - config.ClearButtonSize - config.ContentPadding,
                    (config.InputHeight - config.ClearButtonSize) / 2
                );
                clearButton.Visible = false;
                inputCont.AddChild(clearButton);
            }

            return inputCont;
        }

        private NinePatchRect CreateBackground()
        {
            var bg = new NinePatchRect
            {
                Size = new Vector2(config.Width, config.InputHeight),
                Position = Vector2.Zero
            };

            var styleBox = new StyleBoxFlat
            {
                BgColor = config.BackgroundColor,
                BorderColor = config.BorderColor,
                BorderWidthLeft = config.BorderWidth,
                BorderWidthRight = config.BorderWidth,
                BorderWidthTop = config.BorderWidth,
                BorderWidthBottom = config.BorderWidth,
                CornerRadiusTopLeft = config.CornerRadius,
                CornerRadiusTopRight = config.CornerRadius,
                CornerRadiusBottomLeft = config.CornerRadius,
                CornerRadiusBottomRight = config.CornerRadius,
                ContentMarginLeft = 0,
                ContentMarginRight = 0,
                ContentMarginTop = 0,
                ContentMarginBottom = 0
            };

            // Apply style through texture (workaround)
            bg.Modulate = config.BackgroundColor;
            
            return bg;
        }

        private LineEdit CreateLineEdit(string placeholder, float leftPadding, float rightPadding)
        {
            var edit = new LineEdit
            {
                PlaceholderText = placeholder,
                Position = new Vector2(leftPadding, 0),
                Size = new Vector2(config.Width - leftPadding - rightPadding, config.InputHeight),
                Secret = config.IsPassword,
                MaxLength = config.MaxLength,
                Editable = config.Editable,
                ContextMenuEnabled = config.ContextMenuEnabled,
                VirtualKeyboardEnabled = config.VirtualKeyboardEnabled,
                ClearButtonEnabled = false, // We use custom clear button
                SelectAllOnFocus = config.SelectAllOnFocus,
                CaretBlink = config.CaretBlink
            };

            // Apply styles
            ApplyLineEditStyle(edit);

            // Connect signals
            edit.TextChanged += OnLineEditTextChanged;
            edit.TextSubmitted += OnLineEditTextSubmitted;
            edit.FocusEntered += OnLineEditFocusEntered;
            edit.FocusExited += OnLineEditFocusExited;

            return edit;
        }

        private void ApplyLineEditStyle(LineEdit edit)
        {
            // Normal state
            var normalStyle = new StyleBoxFlat
            {
                BgColor = config.BackgroundColor,
                BorderColor = config.BorderColor,
                BorderWidthLeft = config.BorderWidth,
                BorderWidthRight = config.BorderWidth,
                BorderWidthTop = config.BorderWidth,
                BorderWidthBottom = config.BorderWidth,
                CornerRadiusTopLeft = config.CornerRadius,
                CornerRadiusTopRight = config.CornerRadius,
                CornerRadiusBottomLeft = config.CornerRadius,
                CornerRadiusBottomRight = config.CornerRadius,
                ContentMarginLeft = (int)config.ContentPadding,
                ContentMarginRight = (int)config.ContentPadding,
                ContentMarginTop = 0,
                ContentMarginBottom = 0
            };
            edit.AddThemeStyleboxOverride("normal", normalStyle);

            // Focus state
            var focusStyle = new StyleBoxFlat
            {
                BgColor = config.FocusBackgroundColor,
                BorderColor = config.FocusBorderColor,
                BorderWidthLeft = config.BorderWidth,
                BorderWidthRight = config.BorderWidth,
                BorderWidthTop = config.BorderWidth,
                BorderWidthBottom = config.BorderWidth,
                CornerRadiusTopLeft = config.CornerRadius,
                CornerRadiusTopRight = config.CornerRadius,
                CornerRadiusBottomLeft = config.CornerRadius,
                CornerRadiusBottomRight = config.CornerRadius,
                ContentMarginLeft = (int)config.ContentPadding,
                ContentMarginRight = (int)config.ContentPadding,
                ContentMarginTop = 0,
                ContentMarginBottom = 0
            };
            edit.AddThemeStyleboxOverride("focus", focusStyle);

            // Colors
            edit.AddThemeColorOverride("font_color", config.TextColor);
            edit.AddThemeColorOverride("font_placeholder_color", config.PlaceholderColor);
            edit.AddThemeColorOverride("caret_color", config.CaretColor);
            edit.AddThemeColorOverride("selection_color", config.SelectionColor);
            edit.AddThemeFontSizeOverride("font_size", config.FontSize);
        }

        private Button CreateClearButton()
        {
            var btn = new Button
            {
                Text = config.ClearButtonText,
                CustomMinimumSize = new Vector2(config.ClearButtonSize, config.ClearButtonSize),
                Flat = true
            };
            btn.AddThemeColorOverride("font_color", config.ClearButtonColor);
            btn.AddThemeFontSizeOverride("font_size", config.ClearButtonFontSize);
            btn.Pressed += OnClearButtonPressed;
            return btn;
        }

        private Label CreateErrorLabel()
        {
            var lbl = new Label
            {
                CustomMinimumSize = new Vector2(config.Width, config.ErrorLabelHeight),
                HorizontalAlignment = HorizontalAlignment.Left,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            lbl.AddThemeColorOverride("font_color", config.ErrorColor);
            lbl.AddThemeFontSizeOverride("font_size", config.ErrorFontSize);
            return lbl;
        }

        private void OnLineEditTextChanged(string newText)
        {
            // Show/hide clear button
            if (clearButton != null)
            {
                clearButton.Visible = !string.IsNullOrEmpty(newText) && config.ShowClearButton;
            }

            // Apply regex filter
            if (!string.IsNullOrEmpty(config.AllowedCharactersRegex))
            {
                var filtered = Regex.Replace(newText, config.AllowedCharactersRegex, "");
                if (filtered != newText)
                {
                    lineEdit.Text = filtered;
                    return;
                }
            }

            // Validate
            ValidateInput(newText);

            // Emit signals
            EmitSignal(SignalName.TextChanged, newText);
            onTextChanged?.Invoke(newText);
        }

        private void OnLineEditTextSubmitted(string text)
        {
            EmitSignal(SignalName.TextSubmitted, text);
            onTextSubmitted?.Invoke(text);
        }

        private void OnLineEditFocusEntered()
        {
            if (config.AnimateFocus)
            {
                AnimateFocusState(true);
            }
        }

        private void OnLineEditFocusExited()
        {
            if (config.AnimateFocus)
            {
                AnimateFocusState(false);
            }
            
            // Final validation on focus lost
            ValidateInput(lineEdit.Text);
        }

        private void OnClearButtonPressed()
        {
            lineEdit.Clear();
            lineEdit.GrabFocus();
        }

        private void ValidateInput(string text)
        {
            bool wasValid = isValid;
            isValid = true;
            string errorMessage = "";

            // Required validation
            if (config.Required && string.IsNullOrEmpty(text))
            {
                isValid = false;
                errorMessage = config.RequiredErrorMessage;
            }
            // Min length validation
            else if (config.MinLength > 0 && text.Length < config.MinLength && !string.IsNullOrEmpty(text))
            {
                isValid = false;
                errorMessage = string.Format(config.MinLengthErrorMessage, config.MinLength);
            }
            // Custom validation
            else if (customValidator != null && !customValidator(text))
            {
                isValid = false;
                errorMessage = config.CustomErrorMessage;
            }

            // Update error display
            if (errorLabel != null)
            {
                errorLabel.Text = errorMessage;
                errorLabel.Visible = !isValid && config.ShowErrorLabel;
            }

            // Update border color
            if (!isValid && config.ShowErrorBorder)
            {
                UpdateBorderColor(config.ErrorColor);
            }
            else if (lineEdit.HasFocus())
            {
                UpdateBorderColor(config.FocusBorderColor);
            }
            else
            {
                UpdateBorderColor(config.BorderColor);
            }

            // Emit validation changed
            if (wasValid != isValid)
            {
                EmitSignal(SignalName.ValidationChanged, isValid);
            }
        }

        private void UpdateBorderColor(Color color)
        {
            if (lineEdit == null) return;

            var normalStyle = lineEdit.GetThemeStylebox("normal") as StyleBoxFlat;
            if (normalStyle != null)
            {
                normalStyle.BorderColor = color;
            }

            var focusStyle = lineEdit.GetThemeStylebox("focus") as StyleBoxFlat;
            if (focusStyle != null)
            {
                focusStyle.BorderColor = color;
            }
        }

        private void AnimateFocusState(bool focused)
        {
            var tween = CreateTween();
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Cubic);
            
            float targetScale = focused ? 1.02f : 1.0f;
            tween.TweenProperty(container, "scale", new Vector2(targetScale, targetScale), 0.2f);
        }

        // Public API methods
        public void SetError(string message)
        {
            isValid = false;
            if (errorLabel != null)
            {
                errorLabel.Text = message;
                errorLabel.Visible = true;
            }
            UpdateBorderColor(config.ErrorColor);
        }

        public void ClearError()
        {
            isValid = true;
            if (errorLabel != null)
            {
                errorLabel.Visible = false;
            }
            UpdateBorderColor(config.BorderColor);
        }

        public void Clear()
        {
            if (lineEdit != null)
            {
                lineEdit.Clear();
            }
        }

        public void Focus()
        {
            lineEdit?.GrabFocus();
        }

        public void SetEditable(bool editable)
        {
            if (lineEdit != null)
            {
                lineEdit.Editable = editable;
            }
        }

        public void SetSecret(bool secret)
        {
            if (lineEdit != null)
            {
                lineEdit.Secret = secret;
            }
        }

    public class LineEditConfig
    {
        public float Width { get; set; } = 300;
        public float InputHeight { get; set; } = 40;
        public float LabelHeight { get; set; } = 20;
        public float ErrorLabelHeight { get; set; } = 18;
        public float TotalHeight => LabelHeight + LabelSpacing + InputHeight + ErrorSpacing + ErrorLabelHeight;
        public float LabelSpacing { get; set; } = 5;
        public float ErrorSpacing { get; set; } = 3;
        public float ContentPadding { get; set; } = 12;
        public float IconPadding { get; set; } = 10;
        public float IconSpacing { get; set; } = 8;

        public Color BackgroundColor { get; set; } = new Color(0.1f, 0.1f, 0.15f, 0.8f);
        public Color FocusBackgroundColor { get; set; } = new Color(0.15f, 0.15f, 0.2f, 0.9f);
        
        public Color BorderColor { get; set; } = new Color(0.3f, 0.5f, 0.8f, 0.8f);
        public Color FocusBorderColor { get; set; } = new Color(0.4f, 0.6f, 1.0f, 1.0f);
        public Color ErrorColor { get; set; } = new Color(1.0f, 0.3f, 0.3f, 1.0f);
        public int BorderWidth { get; set; } = 2;
        public int CornerRadius { get; set; } = 5;

        public Color TextColor { get; set; } = new Color(1, 1, 1);
        public Color PlaceholderColor { get; set; } = new Color(0.7f, 0.7f, 0.7f, 0.5f);
        public Color LabelColor { get; set; } = new Color(1, 1, 1, 0.9f);
        public Color CaretColor { get; set; } = new Color(1, 1, 1);
        public Color SelectionColor { get; set; } = new Color(0.4f, 0.6f, 1.0f, 0.4f);

        public int FontSize { get; set; } = 16;
        public int LabelFontSize { get; set; } = 14;
        public int ErrorFontSize { get; set; } = 12;

        public Texture2D Icon { get; set; }
        public Color IconColor { get; set; } = new Color(0.7f, 0.7f, 0.7f);
        public float IconSize { get; set; } = 20;

        public bool ShowClearButton { get; set; } = true;
        public string ClearButtonText { get; set; } = "✕";
        public Color ClearButtonColor { get; set; } = new Color(0.7f, 0.7f, 0.7f);
        public float ClearButtonSize { get; set; } = 24;
        public int ClearButtonFontSize { get; set; } = 16;

        public bool IsPassword { get; set; } = false;
        public int MaxLength { get; set; } = 0;
        public bool Editable { get; set; } = true;
        public bool ContextMenuEnabled { get; set; } = true;
        public bool VirtualKeyboardEnabled { get; set; } = true;
        public bool SelectAllOnFocus { get; set; } = false;
        public bool CaretBlink { get; set; } = true;

        public bool Required { get; set; } = false;
        public int MinLength { get; set; } = 0;
        public bool ShowErrorLabel { get; set; } = true;
        public bool ShowErrorBorder { get; set; } = true;
        public string AllowedCharactersRegex { get; set; } = "";

        public string RequiredErrorMessage { get; set; } = "This field is required";
        public string MinLengthErrorMessage { get; set; } = "Minimum {0} characters";
        public string CustomErrorMessage { get; set; } = "Invalid value";

        public bool AnimateFocus { get; set; } = true;

        public HorizontalAlignment LabelAlignment { get; set; } = HorizontalAlignment.Left;
    }
}