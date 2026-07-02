using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using P = DocumentFormat.OpenXml.Presentation;

namespace HtmlToSlidesPro.Services;

public static class OoxmlStyleHelper
{
    public static void ApplyLinearGradient(P.Shape shape, CssParser.LinearGradient gradient, CssParser css)
    {
        var spPr = shape.GetFirstChild<P.ShapeProperties>()
                   ?? shape.PrependChild(new P.ShapeProperties());

        spPr.RemoveAllChildren<GradientFill>();
        spPr.RemoveAllChildren<SolidFill>();

        var angle = gradient.AngleDeg % 360;
        if (angle < 0) angle += 360;

        var lin = new LinearGradientFill
        {
            Angle = (int)(angle * 60000),
            Scaled = true
        };

        var gsLst = new GradientStopList();
        foreach (var stop in gradient.Stops.OrderBy(s => s.Position))
        {
            gsLst.Append(new GradientStop
            {
                Position = (int)Math.Round(stop.Position * 100000),
                RgbColorModelHex = new RgbColorModelHex { Val = css.ToHex(stop.Color) }
            });
        }

        lin.Append(gsLst);
        spPr.Append(new GradientFill(lin));
    }

    public static void ApplyOuterShadow(P.Shape shape, CssParser.BoxShadowData shadow, int viewportWidthPx, CssParser css)
    {
        var spPr = shape.GetFirstChild<P.ShapeProperties>()
                   ?? shape.PrependChild(new P.ShapeProperties());
        var effectLst = spPr.GetFirstChild<EffectList>() ?? spPr.AppendChild(new EffectList());

        var blurPt = CoordinateMapper.PxToPoints(shadow.Blur, viewportWidthPx) * 12700;
        var distPt = CoordinateMapper.PxToPoints(Math.Sqrt(shadow.OffsetX * shadow.OffsetX + shadow.OffsetY * shadow.OffsetY), viewportWidthPx) * 12700;
        var dir = (int)(Math.Atan2(shadow.OffsetY, shadow.OffsetX) * 180 / Math.PI * 60000);

        var outer = new OuterShadow
        {
            BlurRadius = (long)Math.Max(0, blurPt),
            Distance = (long)Math.Max(0, distPt),
            Direction = dir,
            Alignment = RectangleAlignmentValues.TopLeft
        };
        outer.Append(new RgbColorModelHex { Val = css.ToHex(shadow.Color) });
        outer.Append(new Alpha { Val = (int)Math.Round(shadow.Color.A * 100000) });

        effectLst.Append(outer);
    }

    public static void ApplyRoundedCorners(P.Shape shape, double borderRadiusPx, double widthPx, double heightPx)
    {
        var spPr = shape.GetFirstChild<P.ShapeProperties>()
                   ?? shape.PrependChild(new P.ShapeProperties());
        var prstGeom = spPr.GetFirstChild<PresetGeometry>()
                      ?? spPr.AppendChild(new PresetGeometry { Preset = ShapeTypeValues.RoundRectangle });
        prstGeom.Preset = ShapeTypeValues.RoundRectangle;

        var adj = prstGeom.GetFirstChild<AdjustValueList>() ?? prstGeom.AppendChild(new AdjustValueList());
        adj.RemoveAllChildren<ShapeGuide>();
        var radius = Math.Min(borderRadiusPx, Math.Min(widthPx, heightPx) / 2);
        var relative = Math.Min(0.5, radius / Math.Max(1, Math.Min(widthPx, heightPx)));
        adj.Append(new ShapeGuide { Name = "adj", Formula = $"val {relative.ToString(System.Globalization.CultureInfo.InvariantCulture)}" });
    }

    public static void ApplyOutline(P.Shape shape, CssParser.RgbaColor color, double widthPx, int viewportWidthPx, CssParser css)
    {
        var spPr = shape.GetFirstChild<P.ShapeProperties>()
                   ?? shape.PrependChild(new P.ShapeProperties());
        var outline = spPr.GetFirstChild<Outline>() ?? spPr.AppendChild(new Outline());
        outline.Width = (int)Math.Max(12700, CoordinateMapper.PxToPoints(widthPx, viewportWidthPx) * 12700);
        outline.RemoveAllChildren<SolidFill>();
        var fill = new SolidFill();
        fill.Append(new RgbColorModelHex { Val = css.ToHex(color) });
        outline.Append(fill);
    }
}

public static class FontEmbedder
{
    public static void EmbedFontFiles(string pptxPath, string fontsDirectory)
    {
        if (!Directory.Exists(fontsDirectory)) return;

        var fontFiles = Directory.GetFiles(fontsDirectory, "*.ttf");
        if (fontFiles.Length == 0) return;

        var sidecarDir = pptxPath + ".fonts";
        Directory.CreateDirectory(sidecarDir);
        foreach (var file in fontFiles)
            System.IO.File.Copy(file, System.IO.Path.Combine(sidecarDir, System.IO.Path.GetFileName(file)), overwrite: true);
    }
}
