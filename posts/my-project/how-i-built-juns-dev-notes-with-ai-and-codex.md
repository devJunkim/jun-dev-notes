---
title: "How I Built Jun's Dev Notes with AI and Codex"
excerpt: "A behind-the-scenes look at how I built dev.jun-kim.net with OpenAI Codex, Markdown, a custom .NET publisher, native Gutenberg blocks, and a human-reviewed publishing workflow."
category: "My Project"

seo:
  focusKeyword: "Jun's Dev Notes AI Codex"
  description: "See how Jun's Dev Notes uses OpenAI Codex, Markdown, a custom .NET publisher, native Gutenberg blocks, and a human-reviewed workflow."
  socialTitle: "How I Built Jun's Dev Notes with AI and Codex"
  socialDescription: "Inside the AI-assisted workflow behind dev.jun-kim.net, from Markdown and validation to native Gutenberg drafts and human approval."
---

# How I Built Jun's Dev Notes with AI and Codex

[Jun's Dev Notes](https://dev.jun-kim.net/) is my technical blog for practical software-engineering articles about C#, .NET, architecture, Angular, cloud development, and AI. It runs on WordPress, but the content workflow behind it is code-first and AI-assisted.

I built the workflow with OpenAI Codex in agentic mode. Codex helps inspect the repository, prepare and review articles, run validation, and operate the publishing tools. I still choose the topics, set the standards, review the result, and explicitly approve publication.

> **Quick answer:** Articles begin as Markdown in Git. Codex works within repository instructions to research, draft, review, and validate them. A custom .NET publisher converts the Markdown into native Gutenberg blocks and creates a WordPress draft. I review that draft before a separate, explicit publishing step.

## Project at a Glance

| AI-assisted | Code-first | Human-reviewed |
| --- | --- | --- |
| Codex helps research, draft, review, and validate the work. | Markdown, Git, and a custom .NET publisher provide a reproducible source and workflow. | WordPress receives a draft first, and publication requires my explicit approval. |

## Why I Created Jun's Dev Notes

I wanted a place to document the engineering knowledge that matters after the introductory tutorial is over: trade-offs, production constraints, failure modes, security boundaries, and operational concerns.

The challenge was not simply generating more text. A useful technical blog needs consistent structure, accurate examples, version-aware claims, working links, and a repeatable review process. It also needs source files that can be compared, corrected, and versioned like code.

That led to a workflow with three clear layers:

1. Markdown and Git provide the source of truth.
2. Codex assists with bounded research, writing, review, and validation tasks.
3. WordPress provides the public reading and editorial experience.

## How Codex Agentic Mode Helped

[OpenAI Codex](https://developers.openai.com/api/docs/guides/code-generation) is a coding agent that can work across a software-development task rather than only suggest the next line of text. For this project, that means it can read the repository instructions, inspect related articles, edit files, run commands, interpret validation results, and help with the WordPress workflow.

I used that agentic workflow to:

- inspect existing posts before selecting or drafting a topic;
- find overlap and differentiate a new article instead of repeating old material;
- research version-sensitive technical claims against authoritative documentation;
- write and revise Markdown using the site's established format;
- check metadata, headings, code fences, links, and UTF-8 encoding;
- compile or run code examples when practical;
- render every finished article into WordPress-compatible blocks;
- create WordPress drafts for visual review; and
- verify the final status and permalink after an approved publication.

The important part is that the agent works inside explicit boundaries. Repository instructions define the expected tone, supported Markdown, validation commands, category rules, credential handling, and the difference between creating a draft and publishing it.

## How the Publishing Workflow Works

### 1. An article starts as Markdown

Each article lives under the `posts` directory and is grouped by category. Its front matter contains the title, excerpt, exact WordPress category, focus keyword, search description, and social-sharing metadata.

Keeping the source in Markdown makes the editorial history reviewable. Changes can be compared in Git, and the WordPress copy can be reproduced from the same source.

### 2. The article is reviewed before WordPress

Codex performs a separate technical and editorial pass after drafting. The review checks correctness, duplication, readability, unsupported claims, security implications, code consistency, and whether every section earns its place.

Deterministic checks then validate the file structure and renderer compatibility. When an article contains C# or .NET examples, the workflow compiles or runs them where practical. Angular examples are type-checked or compiled when practical.

### 3. A custom .NET publisher creates Gutenberg blocks

The repository includes `JunDevNotes.Publisher`, a .NET console application built specifically for this workflow. It reads strict UTF-8 Markdown, parses the YAML front matter, and converts supported content into native WordPress Gutenberg blocks.

Headings, paragraphs, lists, quotes, code blocks, tables, and thematic breaks become ordinary blocks rather than one large Classic or Custom HTML block. WordPress remains responsible for the visual presentation through the theme and site CSS.

### 4. WordPress receives a draft, not an automatic publication

The publisher resolves the requested category by its exact name, sends the Gutenberg content and metadata through the WordPress REST API, and verifies the returned title, excerpt, category, status, and SEO fields.

The normal creation workflow forces the post to remain a draft. Passing validation does not publish anything.

### 5. I review and approve the public result

I inspect the WordPress draft for layout, readability, links, and overall quality. Publication is a separate action that requires my explicit approval. The corresponding Git commit and push happen only after that approval.

This human-in-the-loop boundary is intentional. AI accelerates the work and expands the checks, but it does not own the editorial decision.

## What I Learned

AI is most useful here when it is part of an engineering system rather than treated as a one-click content generator. The quality comes from combining the agent with clear repository instructions, a versioned source format, deterministic validation, purpose-built publishing code, and human review.

The result is a workflow that is faster to operate without hiding how the content was produced. It also makes failures easier to diagnose: a Markdown problem, renderer limitation, category mismatch, validation failure, or WordPress response can be identified at a specific boundary.

---

## View the Project on GitHub

The Markdown sources, repository instructions, and .NET Gutenberg publisher are available in the public repository:

**[View Jun's Dev Notes on GitHub](https://github.com/devJunkim/jun-dev-notes)**

The repository shows both the finished content and the safeguards around the agentic workflow. It is the best place to see how Codex, .NET, Git, and WordPress work together behind the site.
