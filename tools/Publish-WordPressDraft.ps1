[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $MarkdownPath,

    [Parameter(Mandatory)]
    [int] $PostId,

    [string] $WordPressUrl = 'https://dev.jun-kim.net'
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not always preload this assembly.
Add-Type -AssemblyName System.Net.Http

function ConvertTo-InlineHtml {
    param([Parameter(Mandatory)][string] $Text)

    $encoded = [Net.WebUtility]::HtmlEncode($Text)
    $encoded = [regex]::Replace(
        $encoded,
        '`([^`]+)`',
        '<code style="padding:.15em .4em;border:1px solid #e2e8f0;border-radius:4px;background:#f1f5f9;color:#0f172a;font-size:.88em;white-space:nowrap;">$1</code>')
    $encoded = [regex]::Replace(
        $encoded,
        '\*\*([^*]+)\*\*',
        '<strong style="color:#0f172a;">$1</strong>')

    return $encoded
}

function ConvertTo-WordPressHtml {
    param([Parameter(Mandatory)][string] $Markdown)

    # Split only after the complete file has been decoded as strict UTF-8.
    $lines = $Markdown -split "\r?\n"
    $html = [Text.StringBuilder]::new()
    $paragraph = [Collections.Generic.List[string]]::new()
    $codeLines = [Collections.Generic.List[string]]::new()
    $inCode = $false
    $inList = $false
    $codeLanguage = ''
    $paragraphNumber = 0

    [void] $html.AppendLine('<div class="dev-notes-article" style="font-size:18px;line-height:1.75;color:#1f2937;">')

    function Flush-Paragraph {
        if ($paragraph.Count -eq 0) {
            return
        }

        $script:paragraphNumber++
        $style = if ($paragraphNumber -eq 1) {
            ' style="font-size:1.2em;line-height:1.7;color:#334155;margin:0 0 1.25em;"'
        }
        else {
            ' style="margin:0 0 1.25em;"'
        }

        [void] $html.Append("<p$style>")
        [void] $html.Append((ConvertTo-InlineHtml ($paragraph -join ' ')))
        [void] $html.AppendLine('</p>')
        $paragraph.Clear()
    }

    function Close-List {
        if ($inList) {
            [void] $html.AppendLine('</ul>')
            $script:inList = $false
        }
    }

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]

        if ($line -match '^```(.*)$') {
            if (-not $inCode) {
                Flush-Paragraph
                Close-List
                $inCode = $true
                $codeLanguage = $Matches[1].Trim()
                $codeLines.Clear()
            }
            else {
                $languageClass = if ($codeLanguage) {
                    $safeLanguage = $codeLanguage.ToLowerInvariant() -replace '[^a-z0-9_+-]', '-'
                    'language-' + $safeLanguage
                }
                else {
                    ''
                }

                if ($languageClass) {
                    # The plugin uses highlight.php identifiers. Its C# option is
                    # keyed as "cs", while fenced Markdown conventionally uses
                    # "csharp". Keep the conventional CSS class but serialize the
                    # plugin-specific value for the editor's language selector.
                    $pluginLanguage = switch ($safeLanguage) {
                        'csharp' { 'cs' }
                        default { $safeLanguage }
                    }
                    [void] $html.AppendLine('<!-- wp:code {"language":"' + $pluginLanguage + '","className":"' + $languageClass + '"} -->')
                    [void] $html.Append('<pre class="wp-block-code ' + $languageClass + '"><code>')
                }
                else {
                    [void] $html.AppendLine('<!-- wp:code -->')
                    [void] $html.Append('<pre class="wp-block-code"><code>')
                }
                [void] $html.Append([Net.WebUtility]::HtmlEncode(($codeLines -join "`n")))
                [void] $html.AppendLine('</code></pre>')
                [void] $html.AppendLine('<!-- /wp:code -->')
                $inCode = $false
                $codeLanguage = ''
                $codeLines.Clear()
            }
            continue
        }

        if ($inCode) {
            $codeLines.Add($line)
            continue
        }

        if ($line -match '^# (.+)$') {
            continue
        }

        if ($line -match '^> (.+)$') {
            Flush-Paragraph
            Close-List
            [void] $html.AppendLine(
                '<aside style="margin:1.5em 0 2em;padding:1.1em 1.3em;border-left:5px solid #2563eb;border-radius:0 8px 8px 0;background:#eff6ff;color:#1e3a5f;">' +
                (ConvertTo-InlineHtml $Matches[1]) +
                '</aside>')
            continue
        }

        if ($line -match '^\|' -and
            ($index + 1) -lt $lines.Count -and
            $lines[$index + 1] -match '^\|(?:\s*:?-+:?\s*\|)+$') {
            Flush-Paragraph
            Close-List
            $headers = @($line.Trim('|').Split('|') | ForEach-Object { $_.Trim() })
            $index += 2
            $rows = [Collections.Generic.List[object]]::new()

            while ($index -lt $lines.Count -and $lines[$index] -match '^\|') {
                $rows.Add(@($lines[$index].Trim('|').Split('|') | ForEach-Object { $_.Trim() }))
                $index++
            }
            $index--

            [void] $html.AppendLine('<div style="margin:1.25em 0 2.25em;overflow-x:auto;border:1px solid #dbe3ee;border-radius:8px;box-shadow:0 2px 8px rgba(15,23,42,.06);"><table style="width:100%;border-collapse:collapse;margin:0;font-size:.94em;">')
            [void] $html.AppendLine('<thead><tr style="background:#0f172a;color:#fff;">')
            foreach ($cell in $headers) {
                [void] $html.AppendLine('<th style="padding:.85em 1em;text-align:left;border:0;">' + (ConvertTo-InlineHtml $cell) + '</th>')
            }
            [void] $html.AppendLine('</tr></thead><tbody>')

            $rowNumber = 0
            foreach ($row in $rows) {
                $background = if (($rowNumber % 2) -eq 0) { '#ffffff' } else { '#f8fafc' }
                [void] $html.AppendLine("<tr style=`"background:$background;`">")
                foreach ($cell in $row) {
                    [void] $html.AppendLine('<td style="padding:.8em 1em;border-top:1px solid #e2e8f0;vertical-align:top;">' + (ConvertTo-InlineHtml $cell) + '</td>')
                }
                [void] $html.AppendLine('</tr>')
                $rowNumber++
            }
            [void] $html.AppendLine('</tbody></table></div>')
            continue
        }

        if ($line -match '^(##|###) (.+)$') {
            Flush-Paragraph
            Close-List
            if ($Matches[1] -eq '##') {
                [void] $html.AppendLine('<h2 style="margin:2.2em 0 .7em;padding-bottom:.35em;border-bottom:2px solid #e2e8f0;color:#0f172a;font-size:1.75em;line-height:1.25;letter-spacing:-.02em;">' + (ConvertTo-InlineHtml $Matches[2]) + '</h2>')
            }
            else {
                [void] $html.AppendLine('<h3 style="margin:1.8em 0 .55em;color:#172554;font-size:1.3em;line-height:1.35;">' + (ConvertTo-InlineHtml $Matches[2]) + '</h3>')
            }
            continue
        }

        if ($line -match '^- (.+)$') {
            Flush-Paragraph
            if (-not $inList) {
                [void] $html.AppendLine('<ul style="margin:.5em 0 1.75em;padding-left:1.4em;">')
                $inList = $true
            }
            [void] $html.AppendLine('<li style="margin:.35em 0;padding-left:.2em;">' + (ConvertTo-InlineHtml $Matches[1]) + '</li>')
            continue
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            Flush-Paragraph
            Close-List
            continue
        }

        $paragraph.Add($line.Trim())
    }

    Flush-Paragraph
    Close-List
    if ($inCode) {
        throw 'The Markdown file contains an unclosed code fence.'
    }
    [void] $html.AppendLine('</div>')

    return $html.ToString()
}

function Assert-CodeBlocksRoundTrip {
    param(
        [Parameter(Mandatory)][string[]] $ExpectedCodeBlocks,
        [Parameter(Mandatory)][string] $WordPressHtml,
        [Parameter(Mandatory)][string] $Stage
    )

    $encodedBlocks = [regex]::Matches(
        $WordPressHtml,
        '<pre class="wp-block-code(?: [^"]+)?"><code>(.*?)</code></pre>',
        [Text.RegularExpressions.RegexOptions]::Singleline)

    if ($encodedBlocks.Count -ne $ExpectedCodeBlocks.Count) {
        throw "$Stage code verification failed: expected $($ExpectedCodeBlocks.Count) code elements but found $($encodedBlocks.Count)."
    }

    for ($index = 0; $index -lt $ExpectedCodeBlocks.Count; $index++) {
        # Browsers decode character references when rendering <code>. Comparing the
        # decoded value proves that escaped HTML preserves the original C# text.
        $decodedCode = [Net.WebUtility]::HtmlDecode($encodedBlocks[$index].Groups[1].Value)
        if ($decodedCode -cne $ExpectedCodeBlocks[$index]) {
            throw "$Stage code verification failed: block $($index + 1) does not round-trip to the original Markdown text."
        }
    }
}

$resolvedMarkdownPath = (Resolve-Path -LiteralPath $MarkdownPath).Path

# Throw on invalid byte sequences instead of silently replacing corrupt input.
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
$markdown = [IO.File]::ReadAllText($resolvedMarkdownPath, $strictUtf8)
$contentHtml = ConvertTo-WordPressHtml -Markdown $markdown
$sourceCodeMatches = [regex]::Matches(
    $markdown,
    '(?ms)^```[^\r\n]*\r?\n(.*?)\r?\n```[ \t]*$')
$expectedCodeBlocks = @($sourceCodeMatches | ForEach-Object { $_.Groups[1].Value })
$expectedCodeBlockCount = $expectedCodeBlocks.Count
$generatedCodeBlockCount = [regex]::Matches($contentHtml, '<!-- wp:code(?:\s|-->)').Count
if ($generatedCodeBlockCount -ne $expectedCodeBlockCount) {
    throw "Expected $expectedCodeBlockCount Gutenberg code blocks but generated $generatedCodeBlockCount."
}
$expectedCSharpBlockCount = [regex]::Matches($markdown, '(?m)^```csharp\s*$').Count
$generatedCSharpBlockCount = [regex]::Matches(
    $contentHtml,
    '<!-- wp:code \{"language":"cs","className":"language-csharp"\} -->').Count
if ($generatedCSharpBlockCount -ne $expectedCSharpBlockCount) {
    throw "Expected $expectedCSharpBlockCount explicit C# language attributes but generated $generatedCSharpBlockCount."
}
Assert-CodeBlocksRoundTrip `
    -ExpectedCodeBlocks $expectedCodeBlocks `
    -WordPressHtml $contentHtml `
    -Stage 'Generated HTML'

$username = [Environment]::GetEnvironmentVariable('WP_USERNAME')
$applicationPassword = [Environment]::GetEnvironmentVariable('WP_APP_PASSWORD')
if ([string]::IsNullOrWhiteSpace($username) -or
    [string]::IsNullOrWhiteSpace($applicationPassword)) {
    throw 'WP_USERNAME and WP_APP_PASSWORD must be set.'
}

$credentials = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes("${username}:${applicationPassword}"))
$apiUrl = $WordPressUrl.TrimEnd('/') + "/wp-json/wp/v2/posts/$PostId"
$requestBody = @{
    content = $contentHtml
    status = 'draft'
} | ConvertTo-Json -Depth 5

$client = [Net.Http.HttpClient]::new()
try {
    $client.DefaultRequestHeaders.Authorization =
        [Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $credentials)

    # Encode the JSON payload explicitly as UTF-8 without a BOM.
    $jsonBytes = [Text.UTF8Encoding]::new($false).GetBytes($requestBody)
    $requestContent = [Net.Http.ByteArrayContent]::new($jsonBytes)
    $requestContent.Headers.ContentType =
        [Net.Http.Headers.MediaTypeHeaderValue]::new('application/json')
    $requestContent.Headers.ContentType.CharSet = 'utf-8'

    $response = $client.PostAsync($apiUrl, $requestContent).GetAwaiter().GetResult()
    $responseBytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $responseText = $strictUtf8.GetString($responseBytes)

    if (-not $response.IsSuccessStatusCode) {
        throw "WordPress returned HTTP $([int] $response.StatusCode): $responseText"
    }

    $post = $responseText | ConvertFrom-Json
    if ($post.status -ne 'draft') {
        throw "Expected draft status but WordPress returned '$($post.status)'."
    }

    $emDash = [string][char]0x2014
    $mojibake = [string]([char[]](0x00E2, 0x20AC, 0x201D))
    if (-not $post.content.raw.Contains($emDash)) {
        throw 'Verification failed: the WordPress response does not contain an em dash.'
    }
    if ($post.content.raw.Contains($mojibake)) {
        throw 'Verification failed: the WordPress response still contains mojibake.'
    }
    $publishedCodeBlockCount = [regex]::Matches(
        $post.content.raw,
        '<!-- wp:code(?:\s|-->)').Count
    if ($publishedCodeBlockCount -ne $expectedCodeBlockCount) {
        throw "Verification failed: expected $expectedCodeBlockCount WordPress code blocks but received $publishedCodeBlockCount."
    }
    $publishedCSharpBlockCount = [regex]::Matches(
        $post.content.raw,
        '<!-- wp:code \{"language":"cs","className":"language-csharp"\} -->').Count
    if ($publishedCSharpBlockCount -ne $expectedCSharpBlockCount) {
        throw "Verification failed: expected $expectedCSharpBlockCount saved C# language attributes but received $publishedCSharpBlockCount."
    }
    Assert-CodeBlocksRoundTrip `
        -ExpectedCodeBlocks $expectedCodeBlocks `
        -WordPressHtml $post.content.raw `
        -Stage 'WordPress response'

    [pscustomobject]@{
        PostId = $post.id
        Status = $post.status
        Utf8Verified = $true
        CodeBlocksVerified = $publishedCodeBlockCount
        CSharpLanguagesVerified = $publishedCSharpBlockCount
        CodeTextRoundTripVerified = $true
        EditUrl = $WordPressUrl.TrimEnd('/') + "/wp-admin/post.php?post=$($post.id)&action=edit"
    }
}
finally {
    if ($requestContent) {
        $requestContent.Dispose()
    }
    $client.Dispose()
}
