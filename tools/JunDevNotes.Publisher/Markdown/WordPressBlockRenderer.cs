using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace JunDevNotes.Publisher.Markdown;

public sealed record WordPressRenderResult(
    string Html,
    int UnorderedListCount,
    int OrderedListCount,
    int FencedCodeBlockCount);

public sealed class WordPressBlockRenderer
{
    private readonly StringBuilder _html = new();
    private int _unorderedListCount;
    private int _orderedListCount;
    private int _fencedCodeBlockCount;

    public WordPressRenderResult Render(ParsedMarkdownDocument markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        _html.Clear();
        _unorderedListCount = 0;
        _orderedListCount = 0;
        _fencedCodeBlockCount = 0;

        RenderBlocks(markdown.Document);

        var renderedHtml = _html.ToString();
        VerifyBalancedLists(renderedHtml);
        VerifyNoTopLevelFreeformHtml(renderedHtml);

        return new WordPressRenderResult(
            renderedHtml,
            _unorderedListCount,
            _orderedListCount,
            _fencedCodeBlockCount);
    }

    private void RenderBlocks(ContainerBlock container)
    {
        foreach (var block in container)
        {
            RenderBlock(block);
        }
    }

    private void RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RenderHeading(heading);
                break;
            case ParagraphBlock paragraph:
                RenderParagraph(paragraph);
                break;
            case FencedCodeBlock codeBlock:
                RenderFencedCodeBlock(codeBlock);
                break;
            case ListBlock list:
                RenderList(list);
                break;
            case QuoteBlock quote:
                RenderBlockquote(quote);
                break;
            case Table table:
                RenderTable(table);
                break;
            case ThematicBreakBlock:
                _html.AppendLine("<!-- wp:separator -->");
                _html.AppendLine("<hr class=\"wp-block-separator has-alpha-channel-opacity\"/>");
                _html.AppendLine("<!-- /wp:separator -->");
                break;
            default:
                throw new NotSupportedException(
                    $"Markdown block type '{block.GetType().Name}' is not supported.");
        }
    }

    private void RenderHeading(HeadingBlock heading)
    {
        if (heading.Level == 1)
        {
            return;
        }

        if (heading.Level == 2)
        {
            _html.AppendLine("<!-- wp:heading -->");
            _html.Append("<h2>");
            RenderInlines(heading.Inline);
            _html.AppendLine("</h2>");
            _html.AppendLine("<!-- /wp:heading -->");
            return;
        }

        if (heading.Level == 3)
        {
            _html.AppendLine("<!-- wp:heading {\"level\":3} -->");
            _html.Append("<h3>");
            RenderInlines(heading.Inline);
            _html.AppendLine("</h3>");
            _html.AppendLine("<!-- /wp:heading -->");
        }
    }

    private void RenderParagraph(ParagraphBlock paragraph)
    {
        _html.AppendLine("<!-- wp:paragraph -->");
        _html.Append("<p>");
        RenderInlines(paragraph.Inline);
        _html.AppendLine("</p>");
        _html.AppendLine("<!-- /wp:paragraph -->");
    }

    private void RenderList(ListBlock list)
    {
        var tag = list.IsOrdered ? "ol" : "ul";
        if (list.IsOrdered)
        {
            _orderedListCount++;
        }
        else
        {
            _unorderedListCount++;
        }

        if (list.IsOrdered)
        {
            _html.Append("<!-- wp:list {\"ordered\":true");
            if (list.OrderedStart is not null && list.OrderedStart != "1")
            {
                _html.Append(",\"start\":").Append(list.OrderedStart);
            }
            _html.AppendLine("} -->");
        }
        else
        {
            _html.AppendLine("<!-- wp:list -->");
        }

        _html.Append('<').Append(tag).Append(" class=\"wp-block-list\"");
        if (list.IsOrdered && list.OrderedStart is not null && list.OrderedStart != "1")
        {
            _html.Append(" start=\"").Append(WebUtility.HtmlEncode(list.OrderedStart)).Append('"');
        }
        _html.AppendLine(">");

        foreach (var item in list.OfType<ListItemBlock>())
        {
            _html.AppendLine("<!-- wp:list-item -->");
            _html.Append("<li>");
            RenderListItemContents(item);
            _html.AppendLine("</li>");
            _html.AppendLine("<!-- /wp:list-item -->");
        }

        _html.Append("</").Append(tag).AppendLine(">");
        _html.AppendLine("<!-- /wp:list -->");
    }

    private void RenderListItemContents(ListItemBlock item)
    {
        foreach (var block in item)
        {
            if (block is ParagraphBlock paragraph)
            {
                RenderInlines(paragraph.Inline);
            }
            else
            {
                RenderBlock(block);
            }
        }
    }

    private void RenderBlockquote(QuoteBlock quote)
    {
        _html.AppendLine("<!-- wp:quote -->");
        _html.AppendLine("<blockquote class=\"wp-block-quote\">");
        RenderBlocks(quote);
        _html.AppendLine("</blockquote>");
        _html.AppendLine("<!-- /wp:quote -->");
    }

    private void RenderFencedCodeBlock(FencedCodeBlock codeBlock)
    {
        _fencedCodeBlockCount++;

        var markdownLanguage = codeBlock.Info?.ToString()?.Trim().ToLowerInvariant()
            ?? string.Empty;
        var safeLanguage = Regex.Replace(markdownLanguage, "[^a-z0-9_+-]", "-");
        var pluginLanguage = safeLanguage == "csharp" ? "cs" : safeLanguage;
        var languageClass = string.IsNullOrEmpty(safeLanguage)
            ? null
            : $"language-{safeLanguage}";

        if (languageClass is null)
        {
            _html.AppendLine("<!-- wp:code -->");
            _html.Append("<pre class=\"wp-block-code\"><code>");
        }
        else
        {
            _html.Append("<!-- wp:code {\"language\":\"")
                .Append(WebUtility.HtmlEncode(pluginLanguage))
                .Append("\",\"className\":\"")
                .Append(WebUtility.HtmlEncode(languageClass))
                .AppendLine("\"} -->");
            _html.Append("<pre class=\"wp-block-code ")
                .Append(WebUtility.HtmlEncode(languageClass))
                .Append("\"><code>");
        }

        _html.Append(WebUtility.HtmlEncode(codeBlock.Lines.ToString()));
        _html.AppendLine("</code></pre>");
        _html.AppendLine("<!-- /wp:code -->");
    }

    private void RenderTable(Table table)
    {
        _html.AppendLine("<!-- wp:table -->");
        _html.AppendLine("<figure class=\"wp-block-table\">");
        _html.AppendLine("<table>");

        var bodyStarted = false;
        foreach (var row in table.OfType<TableRow>())
        {
            if (row.IsHeader)
            {
                _html.AppendLine("<thead><tr>");
                foreach (var cell in row.OfType<TableCell>())
                {
                    _html.Append("<th>");
                    RenderTableCell(cell);
                    _html.AppendLine("</th>");
                }
                _html.AppendLine("</tr></thead>");
                continue;
            }

            if (!bodyStarted)
            {
                _html.AppendLine("<tbody>");
                bodyStarted = true;
            }

            _html.AppendLine("<tr>");
            foreach (var cell in row.OfType<TableCell>())
            {
                _html.Append("<td>");
                RenderTableCell(cell);
                _html.AppendLine("</td>");
            }
            _html.AppendLine("</tr>");
        }

        if (bodyStarted)
        {
            _html.AppendLine("</tbody>");
        }
        _html.AppendLine("</table>");
        _html.AppendLine("</figure>");
        _html.AppendLine("<!-- /wp:table -->");
    }

    private void RenderTableCell(TableCell cell)
    {
        foreach (var block in cell)
        {
            if (block is ParagraphBlock paragraph)
            {
                RenderInlines(paragraph.Inline);
            }
            else
            {
                RenderBlock(block);
            }
        }
    }

    private void RenderInlines(ContainerInline? container)
    {
        if (container is null)
        {
            return;
        }

        for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            RenderInline(inline);
        }
    }

    private void RenderInline(Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                _html.Append(WebUtility.HtmlEncode(literal.Content.ToString()));
                break;
            case CodeInline code:
                _html.Append("<code>")
                    .Append(WebUtility.HtmlEncode(code.Content))
                    .Append("</code>");
                break;
            case EmphasisInline emphasis:
                RenderEmphasis(emphasis);
                break;
            case LinkInline link when !link.IsImage:
                RenderLink(link);
                break;
            case LineBreakInline lineBreak:
                _html.Append(lineBreak.IsHard ? "<br>" : "\n");
                break;
            case ContainerInline nested:
                RenderInlines(nested);
                break;
        }
    }

    private void RenderEmphasis(EmphasisInline emphasis)
    {
        var tag = emphasis.DelimiterCount >= 2 ? "strong" : "em";
        _html.Append('<').Append(tag).Append('>');
        RenderInlines(emphasis);
        _html.Append("</").Append(tag).Append('>');
    }

    private void RenderLink(LinkInline link)
    {
        var url = link.GetDynamicUrl is null
            ? link.Url
            : link.GetDynamicUrl() ?? link.Url;

        _html.Append("<a href=\"")
            .Append(WebUtility.HtmlEncode(url ?? string.Empty))
            .Append('"');
        if (!string.IsNullOrEmpty(link.Title))
        {
            _html.Append(" title=\"")
                .Append(WebUtility.HtmlEncode(link.Title))
                .Append('"');
        }
        _html.Append('>');
        RenderInlines(link);
        _html.Append("</a>");
    }

    private static void VerifyBalancedLists(string html)
    {
        var openingUnordered = Regex.Matches(html, "<ul(?:\\s|>)").Count;
        var closingUnordered = Regex.Matches(html, "</ul>").Count;
        var openingOrdered = Regex.Matches(html, "<ol(?:\\s|>)").Count;
        var closingOrdered = Regex.Matches(html, "</ol>").Count;

        if (openingUnordered != closingUnordered)
        {
            throw new InvalidOperationException(
                $"Generated HTML has unbalanced unordered lists: {openingUnordered} opening and {closingUnordered} closing tags.");
        }

        if (openingOrdered != closingOrdered)
        {
            throw new InvalidOperationException(
                $"Generated HTML has unbalanced ordered lists: {openingOrdered} opening and {closingOrdered} closing tags.");
        }
    }

    private static void VerifyNoTopLevelFreeformHtml(string html)
    {
        var delimiterPattern = new Regex("<!--\\s*(/?)wp:[^>]+-->");
        var depth = 0;
        var previousEnd = 0;

        foreach (Match delimiter in delimiterPattern.Matches(html))
        {
            var contentBeforeDelimiter = html[previousEnd..delimiter.Index];
            if (depth == 0 && !string.IsNullOrWhiteSpace(contentBeforeDelimiter))
            {
                throw new InvalidOperationException(
                    "Generated output contains freeform HTML outside Gutenberg block delimiters.");
            }

            if (delimiter.Groups[1].Value == "/")
            {
                depth--;
                if (depth < 0)
                {
                    throw new InvalidOperationException(
                        "Generated output contains an unmatched Gutenberg closing delimiter.");
                }
            }
            else
            {
                depth++;
            }

            previousEnd = delimiter.Index + delimiter.Length;
        }

        if (depth != 0)
        {
            throw new InvalidOperationException(
                "Generated output contains unbalanced Gutenberg block delimiters.");
        }

        if (!string.IsNullOrWhiteSpace(html[previousEnd..]))
        {
            throw new InvalidOperationException(
                "Generated output contains trailing freeform HTML outside Gutenberg blocks.");
        }
    }
}
