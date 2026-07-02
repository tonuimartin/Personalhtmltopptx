using HtmlToSlidesPro.Models;

namespace HtmlToSlidesPro.Services;

public static class SemanticCompactor
{
    public static SemanticDocument Compact(SemanticDocument doc)
    {
        return new SemanticDocument
        {
            PageTitle = doc.PageTitle,
            ViewportWidth = doc.ViewportWidth,
            ViewportHeight = doc.ViewportHeight,
            EstimatedSlideCount = doc.EstimatedSlideCount,
            ColorTokens = doc.ColorTokens.Take(12).ToList(),
            FontFamilies = doc.FontFamilies.Take(6).ToList(),
            Sections = doc.Sections.Take(20).Select(CompactSection).ToList()
        };
    }

    private static SemanticSection CompactSection(SemanticSection s) => new()
    {
        Id = s.Id,
        Tag = s.Tag,
        Classes = Truncate(s.Classes, 80),
        InferredType = s.InferredType,
        Heading = s.Heading,
        Subheading = s.Subheading,
        Paragraphs = s.Paragraphs.Take(8).Select(p => Truncate(p, 500)).ToList(),
        ListItems = s.ListItems.Take(12).Select(li => new SemanticListItem
        {
            Text = Truncate(li.Text, 200),
            Checked = li.Checked,
            IsCheckbox = li.IsCheckbox
        }).ToList(),
        Cards = s.Cards.Take(8).Select(c => new SemanticCard
        {
            Title = Truncate(c.Title, 120),
            Body = Truncate(c.Body, 300),
            Classes = Truncate(c.Classes, 60),
            Styles = c.Styles
        }).ToList(),
        Images = s.Images.Take(4).Select(i => new SemanticImage
        {
            Src = Truncate(i.Src, 120),
            Alt = Truncate(i.Alt, 80)
        }).ToList(),
        Icons = s.Icons.Take(8).ToList(),
        Styles = s.Styles,
        SlideIndex = s.SlideIndex
    };

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max] + "…";
    }
}
