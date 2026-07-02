namespace HtmlToSlidesPro.Models;

public sealed class SemanticDocument
{
    public string PageTitle { get; set; } = "";
    public int ViewportWidth { get; set; }
    public int ViewportHeight { get; set; }
    public int EstimatedSlideCount { get; set; }
    public List<string> ColorTokens { get; set; } = [];
    public List<string> FontFamilies { get; set; } = [];
    public List<SemanticSection> Sections { get; set; } = [];
}

public sealed class SemanticSection
{
    public string Id { get; set; } = "";
    public string Tag { get; set; } = "";
    public string Classes { get; set; } = "";
    public string InferredType { get; set; } = "";
    public string? Heading { get; set; }
    public string? Subheading { get; set; }
    public List<string> Paragraphs { get; set; } = [];
    public List<SemanticListItem> ListItems { get; set; } = [];
    public List<SemanticCard> Cards { get; set; } = [];
    public List<SemanticImage> Images { get; set; } = [];
    public List<SemanticIcon> Icons { get; set; } = [];
    public SemanticStyles Styles { get; set; } = new();
    public int SlideIndex { get; set; }
}

public sealed class SemanticListItem
{
    public string Text { get; set; } = "";
    public bool Checked { get; set; }
    public bool IsCheckbox { get; set; }
}

public sealed class SemanticCard
{
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string Classes { get; set; } = "";
    public SemanticStyles Styles { get; set; } = new();
}

public sealed class SemanticImage
{
    public string Src { get; set; } = "";
    public string Alt { get; set; } = "";
}

public sealed class SemanticIcon
{
    public string Name { get; set; } = "";
    public string Classes { get; set; } = "";
    public string? Label { get; set; }
}

public sealed class SemanticStyles
{
    public string BackgroundColor { get; set; } = "";
    public string Color { get; set; } = "";
    public string FontFamily { get; set; } = "";
    public string FontSize { get; set; } = "";
    public string BorderRadius { get; set; } = "";
    public string BoxShadow { get; set; } = "";
    public string TextAlign { get; set; } = "";
}
