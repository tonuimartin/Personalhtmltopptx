namespace HtmlToSlidesPro.Models;

public sealed class SlidePlan
{
    public List<SlidePlanSlide> Slides { get; set; } = [];
}

public sealed class SlidePlanSlide
{
    public string? BackgroundColor { get; set; }
    public string? Notes { get; set; }
    public List<SlidePlanShape> Shapes { get; set; } = [];
}

public sealed class SlidePlanShape
{
    public string Type { get; set; } = "text";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public string? Text { get; set; }
    public string? Fill { get; set; }
    public string? Color { get; set; }
    public double? FontSize { get; set; }
    public bool? Bold { get; set; }
    public string? Align { get; set; }
    public string? FontFamily { get; set; }
    public double? CornerRadius { get; set; }
    public string? SourceId { get; set; }
}
