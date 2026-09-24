# Jun's Dev Notes Repository Instructions

## Purpose and scope

This repository contains source articles for [Jun's Dev Notes](https://dev.jun-kim.net/) and the .NET publisher that converts them to native Gutenberg blocks. The blog serves primarily intermediate and senior software developers with practical, production-oriented guidance rather than tutorial filler.

The current primary categories include `C#`, `.NET`, `Architecture`, `Angular`, `Cloud`, and `AI`; this is not a permanently fixed list. Store article Markdown in the matching directory under `posts/`. Keep publishing code under `tools/`.

These instructions apply repository-wide. The standard article workflow has two user-controlled stages: a request to create an article or batch authorizes preparation through verified WordPress draft creation, while a subsequent explicit approval to publish that prepared batch authorizes publication plus the corresponding Git commit and push. Other requests to write or edit local content do not authorize WordPress operations, Git commits, pushes, or changes to repository tooling.

## Categories and new categories

Use the categories specified by the user. Agents must not invent a new category merely to classify an article differently.

If the user specifies a category that does not currently exist, treat it as an intentional proposed category. Preserve it rather than moving the article into an existing category solely because the requested category is absent. Create and validate the local article with the requested category when it is compatible with repository conventions, and clearly report that the category is new.

Do not create a WordPress category automatically. Before creating a WordPress draft, verify that its requested category exists in WordPress. If it does not exist, stop and report that the category must be created or that the user must explicitly authorize its creation. Creating the local article does not provide that authorization.

If the user explicitly asks the agent to propose or create new categories, the agent may recommend appropriate names. Any WordPress category creation remains a separate external change requiring explicit authorization.

## Start with existing content

The user's request controls the article count, topics, and represented categories. If the user supplies one, three, five, or six topics, create exactly one, three, five, or six articles respectively. Do not add articles to reach a customary batch size or add missing categories to balance a batch. If the user requests multiple articles in the same category or omits one or more main categories, preserve that distribution.

Treat explicit user-supplied topics as the editorial assignment. Do not replace one with an agent-selected subject merely because another topic seems preferable. Preserve a category specified with a topic unless a genuine repository compatibility problem prevents it; explain that problem instead of silently reclassifying the article. The agent may select topics only when the user explicitly asks it to do so.

Before proposing or writing the requested articles:

- Inspect the relevant existing articles, including the most recent batches and adjacent categories.
- Check each requested topic for substantial duplication in title, examples, and explanatory scope.
- If overlap exists, preserve the intended subject where practical, narrow or differentiate the angle, and report how the overlap was handled. Do not silently substitute an unrelated topic.
- Link to an existing article when it already explains prerequisite material; do not repeat large prerequisite sections.
- Treat existing articles and `README.md` as the source of truth for format, tone, depth, and workflow. Do not introduce a new article template unless explicitly requested.

## Article format

New publishable articles must use this exact front-matter shape with nonblank values and the exact requested category name. Before WordPress draft creation, that value must exactly match an existing WordPress category unless category creation was separately authorized:

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

For the standard article workflow, a request to create an article or batch authorizes WordPress draft creation after review and validation. It never authorizes publication. Outside that workflow, local creation or validation alone does not authorize a WordPress request.

- Create WordPress drafts for newly prepared articles as part of Stage 1 unless the user explicitly requests an earlier stopping point. Update an existing WordPress draft only when the user explicitly requests that update. Use `JunDevNotes.Publisher`; normal create/update commands force and verify `draft` status.
- Publishing requires a separate, explicit user instruction. Use only the publisher's `--publish <post-id> ... <wordpress-base-url>` workflow and verify returned IDs and statuses. Never publish because validation passed or because a draft was created.
- Never modify an already-published article locally or in WordPress unless explicitly requested. A correction request should identify the affected local file and WordPress post before updating it.
- Resolve categories by their exact existing names before creating a draft. If a requested category is absent, stop; create it only with explicit authorization, then verify the exact name before continuing.
- Never expose or commit `WP_USERNAME`, `WP_APP_PASSWORD`, application passwords, tokens, or other secrets.

Read `README.md` before any WordPress operation for the current command syntax and verification behavior. Do not use the legacy PowerShell publisher when the documented `JunDevNotes.Publisher` workflow covers the task.

## Git and repository safety

- Do not commit or push unless explicitly requested, except that explicit approval to publish the currently prepared batch also authorizes committing and pushing the corresponding approved repository changes under Stage 2.
- Before editing, inspect Git status and preserve all pre-existing changes.
- Stage only explicit task paths; do not use `git add .` or another broad staging command.
- Before committing, verify the staged file allowlist and run `git diff --cached --check`.
- Never include validation projects, generated HTML, credentials, build output, or unrelated changes in a commit.
- Push only when explicitly requested, using the current branch's established upstream unless the user specifies otherwise. Verify the final tracking and working-tree status.
- Do not modify `tools/JunDevNotes.Publisher`, `README.md`, configuration, existing published articles, or unrelated files unless the task specifically requires that change. Preserve existing working behavior.

## Standard two-stage article workflow

Use these stages unless the user explicitly changes a stopping point or requested action.

### Stage 1: Create and prepare a batch

When the user asks to create an article or batch, perform the complete pre-publication workflow in one task:

1. Determine topics according to the user's request. Use supplied topics exactly. Select topics only when the user explicitly asks the agent to choose them, after inspecting existing content for appropriate non-duplicative subjects. Create exactly the requested number of articles.
2. Inspect existing articles for duplication and useful internal-link opportunities.
3. Create the requested Markdown articles using all established Jun's Dev Notes conventions.
4. Perform the required separate technical and editorial self-review.
5. Fix meaningful findings.
6. Perform all required validation, including UTF-8, front matter, SEO metadata, Markdown, internal links, publisher render-only validation, practical C#/.NET compilation or execution, practical Angular type-checking or compilation, and other appropriate technical checks.
7. Remove all temporary validation artifacts.
8. Inspect the relevant diff and Git status, and ensure only intended article or repository changes remain. Preserve and report unrelated changes separately.
9. Read `README.md`, verify that every requested category exists in WordPress, and create WordPress drafts for the completed articles using `JunDevNotes.Publisher`. If a category is absent, follow the category safety rules above and stop rather than creating it without authorization.
10. Verify each created draft's title, post ID, category, draft status, and permalink or preview URL when available.
11. Report results and limitations, then stop so the user can visually inspect the drafts.

Stage 1 never authorizes publishing, committing, or pushing. Do not require a separate request for validation or WordPress draft creation when the standard Stage 1 instructions apply.

### Stage 2: Approve and publish the prepared batch

When the user subsequently says "Publish this batch" or gives equivalent explicit approval to publish the currently prepared batch:

1. Identify the exact WordPress drafts belonging to that prepared batch. Never include unrelated drafts.
2. Publish those exact drafts using the established `JunDevNotes.Publisher` workflow.
3. Verify that every intended post is published and record its final public permalink.
4. If any publication fails, do not blindly continue to Git commit or push. Report the failure and preserve recoverability.
5. Stage only the corresponding approved local article files and any explicitly approved repository changes belonging to the batch. Never use `git add .` or another broad staging command, and report unrelated modifications separately.
6. Verify the staged allowlist and run `git diff --cached --check`.
7. Commit the batch with an appropriate descriptive message.
8. Push to the current branch's established upstream unless the user specifies otherwise.
9. Verify publication statuses, public URLs, the commit hash, the push result, and final Git status.
10. Report the completed batch, then stop.

Actual WordPress publication always requires explicit Stage 2 approval. A Stage 1 creation request never implies that approval. Explicit user instructions override the default stage actions and stopping points.
