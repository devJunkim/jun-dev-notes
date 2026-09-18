---
title: "How to Evaluate AI Coding Agents on Your Own Repository"
excerpt: "Build repository-level evaluations for AI coding agents using realistic tasks, executable checks, review rubrics, and failure analysis instead of demo impressions."
category: "AI"

seo:
  focusKeyword: "evaluate AI coding agents"
  description: "Evaluate AI coding agents with representative repository tasks, isolated environments, deterministic tests, review rubrics, and regression analysis."
  socialTitle: "How to Evaluate AI Coding Agents"
  socialDescription: "Turn coding-agent trials into repeatable repository evaluations that measure correctness, scope control, and review cost."
---

# How to Evaluate AI Coding Agents on Your Own Repository

A coding agent can look excellent on a tidy one-file exercise and still struggle with the conventions, dependencies, and incomplete tests in a real repository. A useful evaluation measures completed engineering work under the same constraints a maintainer faces.

> **Quick answer:** Create a versioned set of representative tasks with hidden executable checks and a human review rubric. Run each task in an isolated, repeatable environment, record both outcome and cost, and analyze failure categories instead of reducing the result to a single pass rate.

## Start with the Decision the Evaluation Supports

An evaluation for choosing an autocomplete tool is different from one for allowing an agent to open pull requests. Define the allowed autonomy, languages, repository size, tools, and risk level before selecting tasks.

Useful questions include:

- Can the agent repair bounded defects without changing public behavior elsewhere?
- Can it add a feature across API, application, persistence, and tests?
- Does it preserve repository boundaries and unrelated changes?
- Can it diagnose a failure without immediately rewriting code?
- Does it report validation limits honestly?

Do not claim that one score measures general software-engineering ability. It measures performance on a task distribution, environment, prompt, model version, and tool configuration.

## Build a Representative Task Set

Sample work from the repository's actual backlog and incident history, then sanitize it. Include several task shapes:

| Task shape | What it reveals |
| --- | --- |
| Local bug with a clear failing test | Basic code navigation and repair |
| Cross-layer feature | Boundary and contract reasoning |
| Ambiguous production symptom | Diagnosis and evidence gathering |
| Dependency or framework migration | API accuracy and scope control |
| Security-sensitive fix | Unsafe shortcuts and regression risk |
| Documentation with executable examples | Technical explanation and validation honesty |

Avoid a suite made entirely of issues already described in public commits or common tutorials. That can measure memorization or retrieval rather than adaptation to your codebase.

Keep tasks independent so one mutation cannot help the next. Pin each task to a repository commit and define the starting state, request, allowed tools, time budget, and prohibited actions.

## Separate Instructions from the Answer Key

The task prompt should contain what a developer would reasonably receive: the symptom, acceptance criteria, and business constraints. Repository-wide conventions belong in maintained instructions, not as hints tailored to each expected patch.

[How to Write an Effective AGENTS.md for AI Coding Agents](https://dev.jun-kim.net/2026/09/15/how-to-write-an-effective-agents-md-for-ai-coding-agents/) explains how to document reusable commands and boundaries. In an evaluation, version that file with the task environment. Changing instructions changes the evaluated system and should be recorded as such.

Keep hidden tests and rubric details unavailable to the agent. They may check externally visible behavior, edge cases, dependency direction, or forbidden file changes. Hidden checks should not demand undocumented trivia; they should distinguish robust implementations from solutions overfit to visible tests.

## Score More Than Test Passage

Executable tests provide strong evidence, but a patch can pass them while weakening authorization, duplicating architecture, or changing an unrelated public contract.

Use multiple dimensions:

- functional correctness, including relevant failure paths;
- regression safety and meaningful added tests;
- scope discipline and preservation of unrelated work;
- security and secret handling;
- architectural fit and maintainability;
- validation accuracy—whether the report matches what actually ran;
- review effort needed before the change could be accepted.

Define anchors for human ratings. “Architecture: 2/4” is hard to reproduce; “introduces a forbidden Domain-to-Infrastructure reference” is observable. Use two reviewers on a subset and discuss disagreements to refine the rubric.

Track terminal outcomes too: completed, correctly blocked, incorrectly blocked, timed out, or unsafe action attempted. A safe refusal can be better than a fabricated success when credentials or external approval are genuinely missing.

## Run in an Isolated and Reproducible Environment

Each run should start from the same commit in a disposable workspace with controlled dependencies. Use synthetic credentials and isolated services. Do not give an evaluation agent production access merely to see whether it uses that access responsibly.

Capture the final diff, commands, test outputs, elapsed time, tool calls, and agent report. Where policy permits, retain enough of the interaction to classify failures without retaining secrets or sensitive source beyond its approved boundary.

Pinning the environment does not mean ignoring reality. Run a separate robustness track for unavailable networks, flaky tests, misleading error messages, or pre-existing working-tree changes. Label it clearly so environmental noise does not corrupt the core comparison.

## Control Variance Before Comparing Systems

Agent results can vary across runs. Repeat tasks, keep budgets comparable, and report distributions rather than only the best attempt. A model given twice the time and tool calls is not a like-for-like comparison.

Hold the task set, repository commit, instructions, and execution environment constant when comparing agent versions. If a better repository guide raises scores, that is a valuable system improvement—not evidence that only the model changed.

Watch for contamination. Once task solutions enter prompts, documentation, or public commits, future runs may no longer represent first exposure. Rotate tasks and maintain a sealed holdout set for consequential decisions.

## Analyze Failures by Cause

A failed test says where the evaluation detected a problem, not why the agent produced it. Classify failures such as:

- missed repository context;
- incorrect framework or API assumption;
- weak problem decomposition;
- test overfitting;
- incomplete validation;
- scope expansion;
- tool or environment failure;
- misleading completion report.

Those categories suggest different interventions. Better instructions may fix a missed command; a deterministic lint rule may prevent a boundary violation; a different model or workflow may be needed for long-horizon changes.

This is where evaluation improves the repository itself. Repeated human and agent mistakes often identify missing tests, unclear ownership, or fragile setup—not merely a weak agent.

## Measure Review Cost and Risk

Lines changed and token counts are poor proxies for value. Record time to first useful patch, reviewer minutes, number and severity of required corrections, and whether the reviewer could understand the reasoning from the diff and evidence.

A fast patch that takes an hour to untangle is not necessarily more productive than a slower, focused patch. Conversely, requiring stylistic perfection on a prototype task may hide meaningful gains. Weight dimensions according to the intended workflow.

[Using AI Coding Agents Without Losing Control of Your Codebase](https://dev.jun-kim.net/2026/09/13/using-ai-coding-agents-without-losing-control-of-your-codebase/) describes the operational controls that remain necessary after an agent performs well in evaluation.

## Treat the Evaluation as Versioned Engineering

Store task definitions, environment manifests, graders, and rubric versions like code. Review changes to hidden tests so they do not accidentally encode one implementation. Re-run a stable subset after model, tool, instruction, or repository changes.

Do not turn one aggregate number into a promise about production. Report the task mix, sample size, run count, confidence or variance, known exclusions, and observed unsafe failures. The goal is not to manufacture a leaderboard; it is to make a better decision about where an agent helps and where human judgment remains the controlling boundary.
