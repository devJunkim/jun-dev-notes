using System.Text;
using JunDevNotes.Publisher.Markdown;
using JunDevNotes.Publisher.WordPress;

var renderOnly = args.Length == 2 &&
    string.Equals(args[0], "--render-only", StringComparison.OrdinalIgnoreCase);

if (!renderOnly && args.Length is not (2 or 3))
{
    Console.Error.WriteLine(
        "Usage:\n" +
        "  JunDevNotes.Publisher --render-only <markdown-path>\n" +
        "  JunDevNotes.Publisher <markdown-path> <wordpress-base-url>\n" +
        "  JunDevNotes.Publisher <markdown-path> <post-id> <wordpress-base-url>");
    return 1;
}

var markdownPath = renderOnly ? args[1] : args[0];
var createsNewDraft = !renderOnly && args.Length == 2;
var postId = (int?)null;
var wordPressBaseUrl = renderOnly ? null : args[^1];

if (!renderOnly && !createsNewDraft)
{
    if (!int.TryParse(args[1], out var parsedPostId) || parsedPostId <= 0)
    {
        Console.Error.WriteLine("Invalid post ID: provide a positive integer.");
        return 1;
    }

    postId = parsedPostId;
}

try
{
    var reader = new MarkdownDocumentReader();
    var markdown = reader.Read(markdownPath);
    Console.WriteLine(
        $"Markdown parsed successfully: {Path.GetFileName(markdown.FilePath)}");

    var renderer = new WordPressBlockRenderer();
    var result = renderer.Render(markdown);
    var outputPath = Path.Combine(
        Path.GetTempPath(),
        $"{Path.GetFileNameWithoutExtension(markdown.FilePath)}.wordpress.html");

    File.WriteAllText(outputPath, result.Html, new UTF8Encoding(false));

    Console.WriteLine($"HTML written to: {outputPath}");
    Console.WriteLine($"Unordered lists rendered: {result.UnorderedListCount}");
    Console.WriteLine($"Ordered lists rendered: {result.OrderedListCount}");
    Console.WriteLine($"Fenced code blocks rendered: {result.FencedCodeBlockCount}");

    if (renderOnly)
    {
        Console.WriteLine($"Title: {markdown.Title ?? "<not set>"}");
        Console.WriteLine($"Excerpt: {markdown.Excerpt ?? "<not set>"}");
        Console.WriteLine($"Category: {markdown.Category ?? "<not set>"}");
        Console.WriteLine(
            $"SEO focus keyword: {markdown.Seo?.FocusKeyword ?? "<not set>"}");
        Console.WriteLine(
            $"SEO description: {markdown.Seo?.Description ?? "<not set>"}");
        Console.WriteLine(
            $"SEO social title: {markdown.Seo?.SocialTitle ?? "<not set>"}");
        Console.WriteLine(
            $"SEO social description: {markdown.Seo?.SocialDescription ?? "<not set>"}");
        Console.WriteLine("Render-only mode: no WordPress request was made.");
        return 0;
    }

    if (string.IsNullOrWhiteSpace(markdown.Title))
    {
        Console.Error.WriteLine(
            "Publishing requires a non-blank 'title' in the Markdown YAML front matter.");
        return 1;
    }
    if (string.IsNullOrWhiteSpace(markdown.Category))
    {
        Console.Error.WriteLine(
            "Publishing requires a non-blank 'category' in the Markdown YAML front matter.");
        return 1;
    }
    if (markdown.Seo is null)
    {
        Console.Error.WriteLine(
            "Publishing requires an 'seo' section in the Markdown YAML front matter.");
        return 1;
    }

    var missingSeoFields = new List<string>();
    if (string.IsNullOrWhiteSpace(markdown.Seo.FocusKeyword))
        missingSeoFields.Add("focusKeyword");
    if (string.IsNullOrWhiteSpace(markdown.Seo.Description))
        missingSeoFields.Add("description");
    if (string.IsNullOrWhiteSpace(markdown.Seo.SocialTitle))
        missingSeoFields.Add("socialTitle");
    if (string.IsNullOrWhiteSpace(markdown.Seo.SocialDescription))
        missingSeoFields.Add("socialDescription");

    if (missingSeoFields.Count > 0)
    {
        Console.Error.WriteLine(
            $"Publishing requires non-blank SEO values in YAML front matter. Missing: {string.Join(", ", missingSeoFields)}.");
        return 1;
    }

    var seoMeta = new WordPressSeoMeta(
        markdown.Seo.FocusKeyword!,
        markdown.Seo.Description!,
        markdown.Seo.SocialTitle!,
        markdown.Seo.SocialDescription!);

    using var httpClient = new HttpClient();
    var wordPressClient = new WordPressClient(httpClient, wordPressBaseUrl!);
    var categoryId = await wordPressClient.ResolveCategoryIdAsync(markdown.Category);
    var post = createsNewDraft
        ? await wordPressClient.CreateDraftAsync(
            result.Html,
            markdown.Title,
            markdown.Excerpt,
            categoryId,
            seoMeta)
        : await wordPressClient.UpdateDraftAsync(
            postId!.Value,
            result.Html,
            markdown.Title,
            markdown.Excerpt,
            categoryId,
            seoMeta);

    Console.WriteLine(createsNewDraft
        ? $"WordPress draft post {post.Id} created and verified."
        : $"WordPress post {post.Id} updated and verified with draft status.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Publisher failed: {exception.Message}");
    return 1;
}
