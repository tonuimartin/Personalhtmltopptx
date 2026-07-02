using System.Globalization;
using System.Text.RegularExpressions;
using HtmlToSlidesPro.Models;

namespace HtmlToSlidesPro.Services;

public sealed partial class CssParser
{
    public sealed record RgbaColor(byte R, byte G, byte B, double A);

    public sealed record GradientStop(double Position, RgbaColor Color);

    public sealed record LinearGradient(double AngleDeg, List<GradientStop> Stops);

    public sealed record BoxShadowData(double OffsetX, double OffsetY, double Blur, double Spread, RgbaColor Color);

    public RgbaColor? ParseColor(string? css)
    {
        if (string.IsNullOrWhiteSpace(css)) return null;
        css = css.Trim();
        if (css.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return new RgbaColor(0, 0, 0, 0);

        var rgb = RgbRegex().Match(css);
        if (rgb.Success)
        {
            return new RgbaColor(
                byte.Parse(rgb.Groups[1].Value),
                byte.Parse(rgb.Groups[2].Value),
                byte.Parse(rgb.Groups[3].Value),
                rgb.Groups[4].Success ? double.Parse(rgb.Groups[4].Value, CultureInfo.InvariantCulture) : 1);
        }

        var rgba = RgbaRegex().Match(css);
        if (rgba.Success)
        {
            return new RgbaColor(
                byte.Parse(rgba.Groups[1].Value),
                byte.Parse(rgba.Groups[2].Value),
                byte.Parse(rgba.Groups[3].Value),
                double.Parse(rgba.Groups[4].Value, CultureInfo.InvariantCulture));
        }

        if (css.StartsWith('#'))
        {
            var hex = css[1..];
            if (hex.Length == 3)
            {
                return new RgbaColor(
                    Convert.ToByte(new string(hex[0], 2), 16),
                    Convert.ToByte(new string(hex[1], 2), 16),
                    Convert.ToByte(new string(hex[2], 2), 16),
                    1);
            }

            if (hex.Length is 6 or 8)
            {
                var a = hex.Length == 8 ? Convert.ToByte(hex[6..8], 16) / 255.0 : 1.0;
                return new RgbaColor(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16),
                    a);
            }
        }

        return null;
    }

    public bool IsVisibleFill(RgbaColor? c) => c is { A: > 0.01 };

    public LinearGradient? ParseLinearGradient(string? backgroundImage)
    {
        if (string.IsNullOrWhiteSpace(backgroundImage) || !backgroundImage.Contains("linear-gradient", StringComparison.OrdinalIgnoreCase))
            return null;

        var angle = 180.0;
        var angleMatch = AngleDegRegex().Match(backgroundImage);
        if (angleMatch.Success)
            angle = double.Parse(angleMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        else if (backgroundImage.Contains("to right", StringComparison.OrdinalIgnoreCase)) angle = 90;
        else if (backgroundImage.Contains("to left", StringComparison.OrdinalIgnoreCase)) angle = 270;
        else if (backgroundImage.Contains("to top", StringComparison.OrdinalIgnoreCase)) angle = 0;

        var stops = new List<GradientStop>();
        foreach (Match m in ColorStopRegex().Matches(backgroundImage))
        {
            var color = ParseColor(m.Groups[1].Value.Trim());
            if (color is null) continue;
            var pos = m.Groups[2].Success
                ? double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100.0
                : stops.Count / Math.Max(1.0, ColorStopRegex().Matches(backgroundImage).Count - 1.0);
            stops.Add(new GradientStop(pos, color));
        }

        if (stops.Count < 2) return null;
        return new LinearGradient(angle, stops);
    }

    public RgbaColor? ParseRadialMidpointColor(string? backgroundImage)
    {
        var linear = ParseLinearGradient(backgroundImage);
        if (linear is not null && linear.Stops.Count > 0)
            return linear.Stops[linear.Stops.Count / 2].Color;

        if (string.IsNullOrWhiteSpace(backgroundImage) || !backgroundImage.Contains("radial-gradient", StringComparison.OrdinalIgnoreCase))
            return null;

        var first = ColorStopRegex().Match(backgroundImage);
        return first.Success ? ParseColor(first.Groups[1].Value.Trim()) : null;
    }

    public BoxShadowData? ParseBoxShadow(string? boxShadow)
    {
        if (string.IsNullOrWhiteSpace(boxShadow) || boxShadow.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;

        var colorMatch = ColorInShadowRegex().Match(boxShadow);
        if (!colorMatch.Success) return null;
        var color = ParseColor(colorMatch.Value);
        if (color is null) return null;

        var nums = NumberRegex().Matches(boxShadow.Replace(colorMatch.Value, ""));
        if (nums.Count < 2) return null;

        var ox = double.Parse(nums[0].Value, CultureInfo.InvariantCulture);
        var oy = double.Parse(nums[1].Value, CultureInfo.InvariantCulture);
        var blur = nums.Count > 2 ? double.Parse(nums[2].Value, CultureInfo.InvariantCulture) : 0;
        var spread = nums.Count > 3 ? double.Parse(nums[3].Value, CultureInfo.InvariantCulture) : 0;
        return new BoxShadowData(ox, oy, blur, spread, color);
    }

    public double ParsePx(string? value, double fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var m = PxRegex().Match(value);
        return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : fallback;
    }

    public double ParseFontSizePt(string? fontSize, int viewportWidthPx)
    {
        var px = ParsePx(fontSize, 16);
        return CoordinateMapper.PxToPoints(px, viewportWidthPx);
    }

    public bool IsBold(string? fontWeight)
    {
        if (string.IsNullOrWhiteSpace(fontWeight)) return false;
        if (fontWeight.Equals("bold", StringComparison.OrdinalIgnoreCase)) return true;
        return int.TryParse(fontWeight, out var w) && w >= 600;
    }

    public string ToHex(RgbaColor c, bool includeAlpha = false)
    {
        if (includeAlpha)
            return $"{c.R:X2}{c.G:X2}{c.B:X2}{(int)Math.Round(c.A * 255):X2}";
        return $"{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    [GeneratedRegex(@"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex RgbRegex();

    [GeneratedRegex(@"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex RgbaRegex();

    [GeneratedRegex(@"(-?\d+(?:\.\d+)?)deg", RegexOptions.IgnoreCase)]
    private static partial Regex AngleDegRegex();

    [GeneratedRegex(@"(rgba?\([^)]+\)|#[0-9a-fA-F]{3,8})\s*(\d+(?:\.\d+)?%)?", RegexOptions.IgnoreCase)]
    private static partial Regex ColorStopRegex();

    [GeneratedRegex(@"(rgba?\([^)]+\)|#[0-9a-fA-F]{3,8})", RegexOptions.IgnoreCase)]
    private static partial Regex ColorInShadowRegex();

    [GeneratedRegex(@"-?\d+(?:\.\d+)?px", RegexOptions.IgnoreCase)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(-?\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase)]
    private static partial Regex PxRegex();
}
