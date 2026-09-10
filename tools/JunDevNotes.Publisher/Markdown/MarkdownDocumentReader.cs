using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JunDevNotes.Publisher.Markdown;

public sealed record MarkdownSeoMetadata(
    string? FocusKeyword,
    string? Description,
    string? SocialTitle,
    string? SocialDescription);

public sealed record ParsedMarkdownDocument(
    string FilePath,
    string OriginalMarkdown,
    string ContentMarkdown,
    MarkdownDocument Document,
    string? Title,
    string? Excerpt,
    string? Category,
    MarkdownSeoMetadata? Seo);

public sealed class MarkdownDocumentReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly Regex FrontMatterPattern = new(
        @"\A---[ \t]*\r?\n(?<yaml>.*?)\r?\n---[ \t]*(?:\r?\n|\z)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    private readonly MarkdownPipeline _pipeline;

    public MarkdownDocumentReader()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
    }

    public ParsedMarkdownDocument Read(string markdownPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownPath);

        var fullPath = Path.GetFullPath(markdownPath);
        var originalMarkdown = File.ReadAllText(fullPath, StrictUtf8);
        var (contentMarkdown, frontMatter) = ExtractFrontMatter(originalMarkdown);
        var document = Markdig.Markdown.Parse(contentMarkdown, _pipeline);

        return new ParsedMarkdownDocument(
            fullPath,
            originalMarkdown,
            contentMarkdown,
            document,
            NormalizeOptionalValue(frontMatter?.Title),
            NormalizeOptionalValue(frontMatter?.Excerpt),
            NormalizeOptionalValue(frontMatter?.Category),
            frontMatter?.Seo is null
                ? null
                : new MarkdownSeoMetadata(
                    NormalizeOptionalValue(frontMatter.Seo.FocusKeyword),
                    NormalizeOptionalValue(frontMatter.Seo.Description),
                    NormalizeOptionalValue(frontMatter.Seo.SocialTitle),
                    NormalizeOptionalValue(frontMatter.Seo.SocialDescription)));
    }

    private static (string ContentMarkdown, MarkdownFrontMatter? FrontMatter)
        ExtractFrontMatter(string originalMarkdown)
    {
        if (!originalMarkdown.StartsWith("---", StringComparison.Ordinal))
        {
            return (originalMarkdown, null);
        }

        var match = FrontMatterPattern.Match(originalMarkdown);
        if (!match.Success)
        {
            throw new FormatException(
                "Markdown starts with a YAML front-matter delimiter but has no valid closing delimiter.");
        }

        MarkdownFrontMatter? frontMatter;
        try
        {
            frontMatter = YamlDeserializer.Deserialize<MarkdownFrontMatter>(
                match.Groups["yaml"].Value);
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new FormatException("Markdown contains invalid YAML front matter.", exception);
        }

        return (originalMarkdown[match.Length..], frontMatter);
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class MarkdownFrontMatter
    {
        public string? Title { get; init; }

        public string? Excerpt { get; init; }

        public string? Category { get; init; }

        public MarkdownSeoFrontMatter? Seo { get; init; }
    }

    private sealed class MarkdownSeoFrontMatter
    {
        public string? FocusKeyword { get; init; }

        public string? Description { get; init; }

        public string? SocialTitle { get; init; }

        public string? SocialDescription { get; init; }
    }
}
