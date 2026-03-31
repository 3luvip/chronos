using Godot;
using System;
using System.Collections.Generic;

#nullable enable

public enum FontStyle
{
    Thin,
    ThinItalic,
    ExtraLight,
    ExtraLightItalic,
    Light,
    LightItalic,
    Regular,
    Italic,
    Medium,
    MediumItalic,
    SemiBold,
    SemiBoldItalic,
    Bold,
    BoldItalic,
    ExtraBold,
    ExtraBoldItalic,
    Black,
    BlackItalic,
}

public partial class FontSystem : Node
{
    public static FontSystem? Instance { get; private set; }

    [Export] public float UiScale                      { get; set; } = 1.0f;
    [Export] public bool  AutoScaleWithViewportWidth   { get; set; } = false;
    [Export] public float ReferenceWidth               { get; set; } = 1080.0f;

    private const string FontsRoot     = "res://fonts/";
    private const string DefaultFamily = "Montserrat";

    // Fallback chain khi thiếu weight cụ thể (theo độ gần nhau về độ đậm)
    private static readonly Dictionary<FontStyle, FontStyle[]> StyleFallbacks = new()
    {
        [FontStyle.Thin]            = new[] { FontStyle.ExtraLight, FontStyle.Light, FontStyle.Regular },
        [FontStyle.ThinItalic]      = new[] { FontStyle.Italic, FontStyle.Regular },
        [FontStyle.ExtraLight]      = new[] { FontStyle.Thin, FontStyle.Light, FontStyle.Regular },
        [FontStyle.ExtraLightItalic]= new[] { FontStyle.Italic, FontStyle.Regular },
        [FontStyle.Light]           = new[] { FontStyle.ExtraLight, FontStyle.Regular },
        [FontStyle.LightItalic]     = new[] { FontStyle.Italic, FontStyle.Regular },
        [FontStyle.Regular]         = new[] { FontStyle.Medium, FontStyle.Light },
        [FontStyle.Italic]          = new[] { FontStyle.Regular },
        [FontStyle.Medium]          = new[] { FontStyle.Regular, FontStyle.SemiBold },
        [FontStyle.MediumItalic]    = new[] { FontStyle.Italic, FontStyle.Regular },
        [FontStyle.SemiBold]        = new[] { FontStyle.Medium, FontStyle.Bold },
        [FontStyle.SemiBoldItalic]  = new[] { FontStyle.BoldItalic, FontStyle.Italic },
        [FontStyle.Bold]            = new[] { FontStyle.SemiBold, FontStyle.ExtraBold },
        [FontStyle.BoldItalic]      = new[] { FontStyle.Italic, FontStyle.Bold },
        [FontStyle.ExtraBold]       = new[] { FontStyle.Bold, FontStyle.Black },
        [FontStyle.ExtraBoldItalic] = new[] { FontStyle.BoldItalic, FontStyle.Italic },
        [FontStyle.Black]           = new[] { FontStyle.ExtraBold, FontStyle.Bold },
        [FontStyle.BlackItalic]     = new[] { FontStyle.BoldItalic, FontStyle.Italic },
    };

    // Map FontStyle → tên file
    private static readonly Dictionary<FontStyle, string> StyleFileNames = new()
    {
        [FontStyle.Thin]            = "Montserrat-Thin.ttf",
        [FontStyle.ThinItalic]      = "Montserrat-ThinItalic.ttf",
        [FontStyle.ExtraLight]      = "Montserrat-ExtraLight.ttf",
        [FontStyle.ExtraLightItalic]= "Montserrat-ExtraLightItalic.ttf",
        [FontStyle.Light]           = "Montserrat-Light.ttf",
        [FontStyle.LightItalic]     = "Montserrat-LightItalic.ttf",
        [FontStyle.Regular]         = "Montserrat-Regular.ttf",
        [FontStyle.Italic]          = "Montserrat-Italic.ttf",
        [FontStyle.Medium]          = "Montserrat-Medium.ttf",
        [FontStyle.MediumItalic]    = "Montserrat-MediumItalic.ttf",
        [FontStyle.SemiBold]        = "Montserrat-SemiBold.ttf",
        [FontStyle.SemiBoldItalic]  = "Montserrat-SemiBoldItalic.ttf",
        [FontStyle.Bold]            = "Montserrat-Bold.ttf",
        [FontStyle.BoldItalic]      = "Montserrat-BoldItalic.ttf",
        [FontStyle.ExtraBold]       = "Montserrat-ExtraBold.ttf",
        [FontStyle.ExtraBoldItalic] = "Montserrat-ExtraBoldItalic.ttf",
        [FontStyle.Black]           = "Montserrat-Black.ttf",
        [FontStyle.BlackItalic]     = "Montserrat-BlackItalic.ttf",
    };

    private readonly Dictionary<string, FamilyFiles> _families = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Montserrat", new FamilyFiles("Montserrat-Regular.ttf", "Montserrat-Bold.ttf") },
        { "Edo",        new FamilyFiles("edo.ttf",                "edo.ttf")             },
    };

    private readonly string[] _fallbackFamilies = { "Montserrat", "Edo" };

    private readonly Dictionary<string, Font>           _fontFiles  = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VariationKey, FontVariation> _variations = new();

    public override void _EnterTree()
    {
        if (Instance is not null && Instance != this)
        {
            GD.PushWarning("FontSystem: another instance already exists.");
            QueueFree();
            return;
        }
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public Font GetFont(string name, int size)
    {
        var (family, style) = ParseName(name);
        return GetFontInternal(family, style, size);
    }

    public Font GetFont(string family, FontStyle style, int size)
        => GetFontInternal(family, style, size);

    public void ApplyFont(Control node, string name, int size)
    {
        if (node is null) return;
        var (family, style) = ParseName(name);
        ApplyFontInternal(node, family, style, size);
    }

    public void ApplyFont(Control node, string family, FontStyle style, int size)
    {
        if (node is null) return;
        ApplyFontInternal(node, family, style, size);
    }

    private void ApplyFontInternal(Control node, string family, FontStyle style, int size)
    {
        node.AddThemeFontOverride("font", GetFontInternal(family, style, size));
        node.AddThemeFontSizeOverride("font_size", GetScaledSize(size));
    }

    private Font GetFontInternal(string family, FontStyle style, int requestedSize)
    {
        int size = GetScaledSize(requestedSize);
        var key  = new VariationKey(family, style, size);

        if (_variations.TryGetValue(key, out var cached)) return cached;

        var variation = new FontVariation { BaseFont = ResolveFontFile(family, style) };
        _variations[key] = variation;
        return variation;
    }

    private Font ResolveFontFile(string family, FontStyle style)
    {
        string cacheKey = $"{family}:{style}";
        if (_fontFiles.TryGetValue(cacheKey, out var cached)) return cached;

        // 1) Montserrat: dùng StyleFileNames map trực tiếp
        if (string.Equals(family, "Montserrat", StringComparison.OrdinalIgnoreCase))
        {
            if (StyleFileNames.TryGetValue(style, out string? fileName))
            {
                var loaded = ResourceLoader.Load<FontFile>($"{FontsRoot}{fileName}");
                if (loaded is not null)
                {
                    _fontFiles[cacheKey] = loaded;
                    return loaded;
                }
            }

            // Fallback theo weight gần nhất
            if (StyleFallbacks.TryGetValue(style, out var fallbacks))
            {
                foreach (var fallbackStyle in fallbacks)
                {
                    if (StyleFileNames.TryGetValue(fallbackStyle, out string? fbFile))
                    {
                        var fbLoaded = ResourceLoader.Load<FontFile>($"{FontsRoot}{fbFile}");
                        if (fbLoaded is not null)
                        {
                            GD.PushWarning($"FontSystem: Montserrat-{style} missing → fallback to {fallbackStyle}.");
                            _fontFiles[cacheKey] = fbLoaded;
                            return fbLoaded;
                        }
                    }
                }
            }
        }

        // 2) Các family khác: dùng FamilyFiles (regular/bold)
        bool wantBold   = style is FontStyle.Bold or FontStyle.ExtraBold or FontStyle.Black
                                or FontStyle.SemiBold or FontStyle.BoldItalic
                                or FontStyle.ExtraBoldItalic or FontStyle.BlackItalic;

        if (TryLoadFamilyStyle(family, wantBold ? FontStyle.Bold : FontStyle.Regular, out var file))
        {
            _fontFiles[cacheKey] = file;
            return file;
        }

        // 3) Fallback families
        foreach (var fallbackFamily in _fallbackFamilies)
        {
            if (TryLoadFamilyStyle(fallbackFamily, style, out file))
            {
                GD.PushWarning($"FontSystem: '{family} {style}' missing → '{fallbackFamily}'.");
                _fontFiles[cacheKey] = file;
                return file;
            }
        }

        // 4) Godot default
        GD.PushWarning($"FontSystem: cannot load any font for '{family} {style}'. Using ThemeDB fallback.");
        _fontFiles[cacheKey] = ThemeDB.FallbackFont;
        return ThemeDB.FallbackFont;
    }

    private bool TryLoadFamilyStyle(string family, FontStyle style, out Font fontFile)
    {
        fontFile = null!;
        if (!_families.TryGetValue(family, out var files)) return false;

        string fileName = style is FontStyle.Bold ? files.Bold : files.Regular;
        var loaded = ResourceLoader.Load<FontFile>($"{FontsRoot}{fileName}");
        if (loaded is null) return false;

        fontFile = loaded;
        return true;
    }

    private int GetScaledSize(int size)
    {
        float scaled = size;
        if (AutoScaleWithViewportWidth)
        {
            var viewport = GetViewport();
            if (viewport is not null && ReferenceWidth > 0f)
                scaled *= viewport.GetVisibleRect().Size.X / ReferenceWidth;
        }
        scaled *= UiScale;
        return Math.Max(1, Mathf.RoundToInt(scaled));
    }

    // ── ParseName ─────────────────────────────────────────
    // Hỗ trợ: "Montserrat:SemiBold" | "Montserrat.Black" | "Montserrat Bold"
    private static (string family, FontStyle style) ParseName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (DefaultFamily, FontStyle.Regular);

        string text = raw.Trim();

        char sep = text.Contains(':') ? ':' : text.Contains('.') ? '.' : ' ';
        int idx   = text.IndexOf(sep);

        if (idx > 0 && idx < text.Length - 1)
        {
            string left  = text[..idx].Trim();
            string right = text[(idx + 1)..].Trim();

            // "bold Montserrat" → swap
            if (TryParseStyle(left,  out var s1)) return (right, s1);
            if (TryParseStyle(right, out var s2)) return (left,  s2);
        }

        return (text, FontStyle.Regular);
    }

    private static bool TryParseStyle(string value, out FontStyle style)
    {
        style = FontStyle.Regular;
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Normalize: "semi bold" → "SemiBold"
        string normalized = value.Replace(" ", "").Replace("-", "");
        return Enum.TryParse(normalized, ignoreCase: true, out style);
    }

    private readonly record struct VariationKey(string Family, FontStyle Style, int Size);
    private readonly record struct FamilyFiles(string Regular, string Bold);
}