# Jun's Dev Notes

This repository contains Markdown articles and publishing tools for
[Jun's Dev Notes](https://dev.jun-kim.net/), a technical blog focused on
practical C#, .NET, architecture, and modern software development.

Source articles live under `posts/` so they can be reviewed and versioned
before they are sent to WordPress.

## JunDevNotes.Publisher v1

`tools/JunDevNotes.Publisher` is a .NET console application that reads a
Markdown article, parses its YAML front matter, converts its body to native
Gutenberg block markup, and updates an existing WordPress post through the
WordPress REST API. It can create a new draft or update an existing draft.

Markdown is decoded with strict UTF-8 validation. Invalid UTF-8 input fails
instead of silently replacing or corrupting characters. The renderer emits
native Gutenberg blocks without custom inline styles; presentation styling is
handled by WordPress and the site's CSS.

### Markdown front matter

An article used for normal publishing must begin with this structure:

```yaml
---
title: "Article title"
excerpt: "Short article summary."
category: "Exact WordPress category name"

seo:
  focusKeyword: "Primary search phrase"
  description: "Search-result description."
  socialTitle: "Social sharing title"
  socialDescription: "Social sharing description."
---
```

The front matter is metadata and is not included in the rendered article body.

Here is the real front matter from
`posts/csharp/value-types-vs-reference-types.md`:

```yaml
---
title: "C# Value Types vs Reference Types: A Complete Guide"
excerpt: "Learn how value types and reference types work in C#, how they behave when assigned or passed to methods, and why understanding the difference matters in real-world .NET development."
category: "C#"

seo:
  focusKeyword: "C# value types vs reference types"
  description: "Learn how C# value types and reference types differ, how assignment and method calls behave, and when these differences matter in real-world .NET code."
  socialTitle: "C# Value Types vs Reference Types: A Complete Guide"
  socialDescription: "Learn how C# value types and reference types differ, how assignment and method calls behave, and when these differences matter in real-world .NET code."
---
```

### Render-only mode

From the repository root, run:

```powershell
dotnet run --project tools/JunDevNotes.Publisher -- --render-only posts/csharp/value-types-vs-reference-types.md
```

Render-only mode:

- Parses the Markdown body and YAML front matter.
- Renders the body as native Gutenberg HTML.
- Writes `<article-name>.wordpress.html` in the operating system's temporary
  directory.
- Prints the parsed title, excerpt, category, and SEO values for inspection.
- Makes no WordPress request.
- Does not require credentials, a post ID, or a WordPress URL.

### Creating a new article file

Create a Markdown scaffold at a requested path with:

```powershell
dotnet run --project tools/JunDevNotes.Publisher -- --new posts/csharp/my-new-article.md
```

The command creates missing parent directories and writes the standard empty
front matter plus an `# Article Title` heading as UTF-8 without a BOM. It
prints the new file's full path, requires no WordPress credentials, and makes
no WordPress request. It refuses to overwrite an existing file.

### WordPress credentials

Normal publishing requires these environment variables:

- `WP_USERNAME`: the WordPress username.
- `WP_APP_PASSWORD`: a WordPress Application Password, not the account's
  interactive login password.

Do not commit either value. In PowerShell, set them for the current terminal
session as `$env:WP_USERNAME` and `$env:WP_APP_PASSWORD`.

### Creating or updating a draft

Create a new draft by omitting the post ID:

```powershell
dotnet run --project <project-path> -- <markdown-path> <wordpress-base-url>
```

Example:

```powershell
dotnet run --project tools/JunDevNotes.Publisher -- posts/csharp/value-types-vs-reference-types.md https://dev.jun-kim.net
```

This sends the article to `/wp-json/wp/v2/posts` and prints the new WordPress
post ID after the response is verified.

Update an existing draft by supplying its post ID:

```powershell
dotnet run --project <project-path> -- <markdown-path> <post-id> <wordpress-base-url>
```

Example:

```powershell
dotnet run --project tools/JunDevNotes.Publisher -- posts/csharp/value-types-vs-reference-types.md 32 https://dev.jun-kim.net
```

This sends the article to `/wp-json/wp/v2/posts/{post-id}` and verifies that
the response refers to the same post ID.

Both commands:

- Resolves the category by exact name and fails if it cannot find one exact
  match.
- Sends the Gutenberg content, title, excerpt, resolved category ID, and
  SiteSEO metadata.
- Always forces `status = "draft"`.
- Never automatically publishes a post.
- Never creates categories automatically.

Review the resulting draft in WordPress and publish it manually when it is
ready.

### SiteSEO metadata

The publisher maps front-matter SEO values to WordPress REST metadata as
follows:

| Front matter | WordPress meta key |
| --- | --- |
| `focusKeyword` | `_siteseo_analysis_target_kw` |
| `description` | `_siteseo_titles_desc` |
| `socialTitle` | `_siteseo_social_fb_title` |
| `socialDescription` | `_siteseo_social_fb_desc` |

Normal publishing fails before the post update if the `seo` section or any of
these four values is missing or blank.

### Post-save verification

After WordPress accepts a create or update request, the publisher compares the response with
the values sent. It verifies:

- Post ID.
- Draft status.
- Raw title.
- Raw excerpt.
- Category ID.
- All four SiteSEO meta values.

A mismatch fails the command with an error identifying the field that did not
match. Verification uses the original update response and does not send a
second update request.

## Repository guidelines

- Keep article sources in Markdown under `posts/`.
- Keep the publisher separate from article content under `tools/`.
- Preserve the blog's C#/.NET focus and keep examples technically accurate.
- Never commit credentials, application passwords, API tokens, or other
  secrets.
- Treat WordPress publishing as a draft-and-review workflow.
