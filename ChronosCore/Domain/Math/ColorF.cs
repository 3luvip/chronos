namespace Chronos.Core.Domain;

public readonly record struct ColorF(float R, float G, float B, float A = 1f)
{
    public static readonly ColorF White       = new(1f, 1f, 1f);
    public static readonly ColorF Black       = new(0f, 0f, 0f);
    public static readonly ColorF Transparent = new(0f, 0f, 0f, 0f);
    public static readonly ColorF Red         = new(1f, 0f, 0f);

    /// Parse hex string "#RRGGBB" hoặc "#RRGGBBAA"
    public static ColorF FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        float r = Convert(hex, 0);
        float g = Convert(hex, 2);
        float b = Convert(hex, 4);
        float a = hex.Length >= 8 ? Convert(hex, 6) : 1f;
        return new(r, g, b, a);

        static float Convert(string h, int idx) =>
            System.Convert.ToInt32(h.Substring(idx, 2), 16) / 255f;
    }

    public ColorF WithAlpha(float a) => this with { A = a };
}
