# Jun's Dev Notes

This repository contains Markdown articles and publishing tools for
[Jun's Dev Notes](https://dev.jun-kim.net/), a technical blog for primarily
intermediate and senior developers. Articles emphasize practical,
production-oriented software development rather than tutorial filler.

Source articles live under `posts/` so they can be reviewed and versioned
before they are sent to WordPress.

## Repository structure

- `posts/` contains article sources grouped by category. Current primary
  categories include C#, .NET, Architecture, Angular, Cloud, and AI, but the
  category list is not permanently fixed.
- `tools/JunDevNotes.Publisher/` contains the current .NET 10 publisher.
- `tools/Publish-WordPressDraft.ps1` is legacy tooling. Use the documented
  `JunDevNotes.Publisher` workflow for current work.
- `AGENTS.md` defines repository-wide content, validation, WordPress, and Git
  safety rules for coding agents.
- `images/` is reserved for repository image assets.

## Content-production workflow

The user's assignment controls the number of articles, their topics, and their
categories. Do not add articles or categories to balance a batch. When topics
are supplied explicitly, preserve them rather than silently substituting
agent-selected subjects.

Before writing, inspect the existing article corpus and check every requested
topic for substantial duplication. If an existing article overlaps, preserve
the intended subject where practical, narrow or differentiate the angle, link
to useful prerequisite coverage, and report how the overlap was handled.

Match nearby published sources for structure, tone, heading hierarchy, code
style, SEO metadata, and depth. Established articles generally contain one H1
matching the title, a problem-focused introduction, a `Quick answer` blockquote,
and descriptive H2 sections. There is no fixed word count.

After writing:

1. Perform a separate technical and editorial review.
2. Fix meaningful correctness, duplication, readability, code, link, or claim
   issues.
3. Validate strict UTF-8, YAML metadata, heading structure, Markdown, and code
   fences.
4. Run publisher render-only validation for every changed article.
5. Compile or run C#/.NET examples and type-check or compile Angular examples
   where practical.
6. Inspect Git diff and status, then remove temporary validation artifacts.
7. Report completed checks and limitations, then stop.

Creating WordPress drafts, publishing posts, committing, and pushing are
separate actions. Each requires explicit instructions; completing local article
validation does not authorize the next action.

## JunDevNotes.Publisher v1

`tools/JunDevNotes.Publisher` is a .NET console application that reads a
Markdown article, parses its YAML front matter, converts its body to native
Gutenberg block markup, and updates an existing WordPress post through the
WordPress REST API. It can create a new draft or update an existing draft.

Markdown is decoded with strict UTF-8 validation. Invalid UTF-8 input fails
instead of silently replacing or corrupting characters. The renderer emits
native Gutenberg blocks without custom inline styles; presentation styling is
handled by WordPress and the site's CSS.

### How the publisher is organized

- `Program.cs` validates CLI arguments and selects scaffold, render-only,
  draft-create, draft-update, or explicit publish behavior.
- `MarkdownDocumentReader` decodes files with strict UTF-8, separates YAML
  front matter, normalizes optional metadata, and parses Markdown with Markdig.
- `WordPressBlockRenderer` converts supported Markdown blocks and inline
  elements to native Gutenberg markup. It verifies balanced list and Gutenberg
  delimiters before returning output.
- `WordPressClient` authenticates with an application password, resolves one
  exact category match, creates or updates drafts, publishes reviewed draft
  IDs, and verifies WordPress responses.
- The request and response records define the WordPress JSON contract and map
  SiteSEO fields to their REST metadata keys.

The renderer supports paragraphs, H1-H3 headings, fenced code blocks, ordered
and unordered lists, blockquotes, tables, and thematic breaks. H1 is omitted
from rendered content because WordPress displays the post title separately.
Unsupported Markdown block types fail instead of being emitted as unverified
freeform HTML.

### Markdown front matter

An article used for normal publishing must begin with this structure. The
category is the exact category requested for the article:

```yaml
---
title: "Article title"
excerpt: "Short article summary."
category: "Requested category name"

seo:
  focusKeyword: "Primary search phrase"
  description: "Search-result description."
  socialTitle: "Social sharing title"
  socialDescription: "Social sharing description."
---
```

The front matter is metadata and is not included in the rendered article body.
Before WordPress draft creation, the category must exactly match an existing
WordPress category unless category creation was separately authorized. The
publisher never creates categories automatically.

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

Render-only is the standard final compatibility check for local article work.
It does not authorize or perform a WordPress operation.

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

Do not print credential values during diagnostics. Credential presence does not
authorize draft creation or publishing.

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

Review the resulting draft in WordPress before publishing it.

Draft creation and draft updates require explicit user authorization. Before
creating a draft, verify that the requested category exists in WordPress. If it
does not, stop and request either category creation or explicit authorization
to create it. Do not move the article to another category merely because the
requested category is new.

### Publishing existing drafts explicitly

Publish one or more reviewed drafts by supplying their WordPress post IDs:

```powershell
dotnet run --project tools/JunDevNotes.Publisher -- --publish 107 108 109 https://dev.jun-kim.net
```

The `--publish` command checks each post's current status before changing it.
It sends only `{"status":"publish"}` for a draft and verifies the returned ID
and status. An already-published post is reported without an update. Other
statuses and failed requests are reported per post; processing continues for
the remaining IDs, and the command exits with a failure code if any ID failed.
Normal article creation and update commands continue to force `draft`.

Publishing is never an automatic continuation of rendering or draft creation.
Run `--publish` only after an explicit request naming or unambiguously
identifying the reviewed drafts.

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
- Preserve the established practical engineering focus and keep examples
  technically accurate.
- Never commit credentials, application passwords, API tokens, or other
  secrets.
- Treat WordPress publishing as a draft-and-review workflow.
- Do not modify published articles, the publisher, README, configuration, or
  unrelated files unless the task requires it.

## Internal links and categories

Use internal links only when they genuinely help the reader. Verify the actual
published permalink; do not infer a publication date or link to an unpublished
draft as though it were live.

Agents must not invent new categories merely to reclassify an assignment. A
user-requested category may be used for local article creation even when it is
new, but it must be reported as new. Creating a WordPress category is a
separate external change requiring explicit authorization.

## Git workflow

Local editing does not authorize a commit or push.

Before committing:

1. Run `git status` and identify pre-existing changes.
2. Stage only the explicit files that belong to the task. Do not use
   `git add .`.
3. Verify the staged allowlist with `git diff --cached --name-status`.
4. Run `git diff --cached --check` and review the staged diff.
5. Exclude validation projects, generated HTML, build output, credentials, and
   unrelated changes.

Commit and push only when explicitly requested. Push the current branch to its
established upstream unless the user specifies another destination, then verify
the final tracking and working-tree status.
