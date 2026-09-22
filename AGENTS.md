# Jun's Dev Notes Repository Instructions

## Purpose and scope

This repository contains source articles for [Jun's Dev Notes](https://dev.jun-kim.net/) and the .NET publisher that converts them to native Gutenberg blocks. The blog serves primarily intermediate and senior software developers with practical, production-oriented guidance rather than tutorial filler.

The main categories are `C#`, `.NET`, `Architecture`, `Angular`, `Cloud`, and `AI`. Store article Markdown in the matching directory under `posts/`. Keep publishing code under `tools/`.

These instructions apply repository-wide. A task to write or edit local content does not authorize WordPress operations, Git commits, pushes, or changes to repository tooling.

## Start with existing content

Before proposing or writing an article:

- Inspect the relevant existing articles, including the most recent batches and adjacent categories.
- Check the proposed subject for substantial duplication in title, examples, and explanatory scope.
- If overlap exists, narrow or differentiate the angle while preserving the requested subject. Report that decision.
- Link to an existing article when it already explains prerequisite material; do not repeat large prerequisite sections.
- Treat existing articles and `README.md` as the source of truth for format, tone, depth, and workflow. Do not introduce a new article template unless explicitly requested.

## Article format

New publishable articles must use this exact front-matter shape with nonblank values and the exact WordPress category name:

```yaml
---
title: "Article title"
excerpt: "Short article summary."
category: "C#"

seo:
  focusKeyword: "Primary search phrase"
  description: "Search-result description."
  socialTitle: "Social sharing title"
  socialDescription: "Social sharing description."
---
```

Follow the front matter with one `#` heading matching the article title. Use a concise opening that frames the problem, the established `> **Quick answer:**` callout, and descriptive `##` sections. Use `###` only when a section genuinely needs subdivision. Match the scope and approximate depth of nearby recent articles; there is no fixed word count.

Use ordinary Markdown supported by `tools/JunDevNotes.Publisher`: paragraphs, H1-H3 headings, fenced code blocks, ordered or unordered lists, blockquotes, tables, and thematic breaks. The renderer omits H1 from WordPress content because WordPress supplies the post title. Unsupported Markdown block types fail rendering, so render every finished article.

Use triple-backtick code fences with an accurate language identifier such as `csharp`, `typescript`, `html`, `json`, `yaml`, `bash`, or `text`. Keep examples focused, internally consistent, and free of real credentials or sensitive data.

SEO text should be specific and natural. Keep the focus keyword aligned with the title and search intent; make the excerpt, description, social title, and social description useful summaries rather than keyword repetition.

## Writing and technical quality

- Explain why and how a technique works, not only its syntax.
- Include production constraints, failure modes, security, operations, and trade-offs where they materially affect the subject.
- Prefer precise, qualified language over universal rules. Distinguish framework requirements from recommendations and examples.
- Avoid filler, repeated conclusions, unsupported statistics, invented performance claims, and dogmatic “always” or “never” statements without a real invariant.
- Use current appropriate APIs. Do not present preview behavior as stable without saying so.
- Verify framework, language, cloud, security, and version-sensitive claims against primary or authoritative documentation when needed.
- Compile and run C#/.NET examples where practical. Type-check or compile Angular examples where practical. Use temporary harnesses only for validation and remove them afterward.
- Report exactly what ran. Never describe an example as compiled, tested, rendered, or verified unless that check actually completed successfully.

## Internal links

Add internal links only when they help the reader continue into prerequisite or closely related material. Match targets against the actual published WordPress permalink; do not infer a date from a filename or another article. Verify targets before completion.

Do not force links for SEO, link unrelated articles, or link to a local file or unpublished WordPress draft as though it were a published article. If a batch article is not yet published, refer to it without inventing a public URL or add the link only after its live permalink is known and an update is explicitly requested.

## Review and validation

After drafting, perform a separate technical and editorial review. Check correctness, meaningful overlap with the full corpus, readability, code consistency, link relevance and validity, unsupported claims, security implications, and whether each section earns its place. Fix meaningful findings before reporting completion.

For every article batch:

1. Confirm strict UTF-8 text, balanced and valid YAML front matter, all required metadata, one body H1, and balanced code fences.
2. Run publisher render-only validation for every changed article from the repository root:

   ```powershell
   dotnet run --project tools/JunDevNotes.Publisher -- --render-only posts/<category>/<article>.md
   ```

3. Compile/run C# and .NET examples and type-check/compile Angular examples where practical. State any checks that could not run and why.
4. Inspect `git diff --check`, the relevant diff, and `git status --short` for unintended changes.
5. Remove temporary validation projects, generated files, package installations, and `.validation` directories before completion. Generated `*.wordpress.html` belongs in the operating-system temporary directory and must not be committed.

Do not change an article merely to satisfy an unrelated formatter or to create validation infrastructure. Preserve unrelated user changes.

## WordPress safety

Local creation and validation do not authorize a WordPress request.

- Create or update a WordPress draft only when the user explicitly requests it. Use `JunDevNotes.Publisher`; normal create/update commands force and verify `draft` status.
- Publishing requires a separate, explicit user instruction. Use only the publisher's `--publish <post-id> ... <wordpress-base-url>` workflow and verify returned IDs and statuses. Never publish because validation passed or because a draft was created.
- Never modify an already-published article locally or in WordPress unless explicitly requested. A correction request should identify the affected local file and WordPress post before updating it.
- Resolve categories by their exact existing names. Do not create categories automatically.
- Never expose or commit `WP_USERNAME`, `WP_APP_PASSWORD`, application passwords, tokens, or other secrets.

Read `README.md` before any WordPress operation for the current command syntax and verification behavior. Do not use the legacy PowerShell publisher when the documented `JunDevNotes.Publisher` workflow covers the task.

## Git and repository safety

- Do not commit or push unless explicitly requested.
- Before editing, inspect Git status and preserve all pre-existing changes.
- Stage only explicit task paths; do not use `git add .` or another broad staging command.
- Before committing, verify the staged file allowlist and run `git diff --cached --check`.
- Never include validation projects, generated HTML, credentials, build output, or unrelated changes in a commit.
- Push only when explicitly requested, using the current branch's established upstream unless the user specifies otherwise. Verify the final tracking and working-tree status.
- Do not modify `tools/JunDevNotes.Publisher`, `README.md`, configuration, existing published articles, or unrelated files unless the task specifically requires that change. Preserve existing working behavior.

## Standard article-batch workflow

Use this sequence unless the user explicitly changes it:

```text
Inspect existing content
-> check proposed topics for overlap
-> write articles
-> add useful verified internal links
-> perform a separate self-review
-> fix meaningful findings
-> validate metadata, Markdown, and examples
-> run publisher render-only for every article
-> inspect Git diff and status
-> report results and limitations
-> STOP
```

WordPress draft creation, publishing, committing, and pushing are separate actions. Each requires explicit instructions; authorization for one does not imply authorization for the next.
