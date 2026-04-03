using Markdig;
namespace wish_drom.Services;

public class MarkdownService
{
    private static readonly MarkdownPipeline _pipeline;

    static MarkdownService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
    }

    public static string Parse(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var html = Markdown.ToHtml(markdown, _pipeline);
        return html;
    }
}
