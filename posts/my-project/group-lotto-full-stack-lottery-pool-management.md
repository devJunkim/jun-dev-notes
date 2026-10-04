---
title: "Group Lotto: Building a Full-Stack Lottery Pool Management System"
excerpt: "How I replaced a spreadsheet-heavy group lottery workflow with an Angular 22 and ASP.NET Core 10 application for participants, balances, tickets, results, and auditable transactions."
category: "My Project"

seo:
  focusKeyword: "Group Lotto application"
  description: "See how I built the Group Lotto application with Angular 22, ASP.NET Core 10, EF Core, SQL Server, browser OCR, and an agentic coding workflow."
  socialTitle: "Group Lotto: A Full-Stack Angular and .NET Project"
  socialDescription: "Inside Group Lotto: a practical Angular and ASP.NET Core system for managing a lottery group, its balances, tickets, results, and audit history."
---

# Group Lotto: Building a Full-Stack Lottery Pool Management System

For years, I have helped manage a lottery group of ten people. The recurring work sounds simple: collect contributions, buy tickets, share ticket and result images, and report each person's remaining balance. In practice, keeping spreadsheets, transfers, ticket images, results, and balances synchronized created a surprising amount of repetitive administration.

I built the **Group Lotto application** to replace that spreadsheet-heavy process with one auditable system. It combines an Angular 22 client with an ASP.NET Core 10 Web API, Entity Framework Core, SQL Server, and browser-based OCR for ticket images.

> **Quick answer:** Group Lotto centralizes participant balances, financial transactions, lottery tickets, draw results, and lottery history. The application treats money as an auditable ledger, keeps corrections traceable through reversals, and requires human review before OCR-extracted ticket data is saved.

## Project at a Glance

| Area | Implementation | Purpose |
| --- | --- | --- |
| Client | Angular 22 | Lazy-loaded workflows, reactive forms, signals, and browser OCR |
| API | ASP.NET Core 10 | Versioned REST endpoints, validation, Problem Details, and OpenAPI |
| Persistence | EF Core and SQL Server | Participant, ledger, ticket, draw, and import records |
| Ticket processing | Tesseract.js | Extract candidate values from ticket and result images for review |
| Quality | .NET tests, Angular tests, and API integration tests | Protect domain rules and user-visible behavior |

The current application contains six primary Angular workflows, seven API controller groups, 25 versioned REST endpoints, and support for Lotto Max, Lotto 6/49, and Ontario 49.

## Why I Built It

Excel was a reasonable starting point, but the workflow became harder to trust as more responsibilities accumulated. A contribution could arrive by e-Transfer but be missed in the workbook. A ticket image could be shared while its cost or result remained unrecorded. Correcting a mistake could overwrite the history needed to explain a balance later.

The real requirement was not simply a more attractive spreadsheet. I needed a small financial system with explicit business events, durable history, and a clear review process.

Group Lotto therefore focuses on three outcomes:

1. Make each participant's balance explainable.
2. Keep ticket purchases and results connected to the financial history.
3. Reduce repetitive entry without allowing automation to silently invent data.

The lottery-history and lucky-number tools are deliberately secondary. They are useful and fun, but transparency and reliable group administration are the core of the product.

## The Main User Workflows

### Dashboard

The dashboard provides a current view of group finances and upcoming draws. It shows the group balance, active-participant count, negative-balance alerts, participant balances, recent transactions, ticket-cost estimates, and configurable draw cards.

Draw settings for jackpots, dates, visibility, MAXMILLIONS, bonus prizes, and Gold Ball information are stored in the database rather than embedded in the client.

### Participants

The participant workflow maintains the group roster and each person's default contribution. Users can search and filter the roster, edit participant details, and deactivate someone without deleting their financial history.

Deletion is allowed only when domain constraints make it safe. Historical data takes priority over making a record disappear from the interface.

### Auditable Transactions

Deposits, purchases, prizes, opening balances, and adjustments are posted as transaction batches. A batch can include one participant or the entire active group, with shared or individually adjusted amounts.

Corrections use a full reversal instead of editing the original transaction. Both records remain visible, preserving an audit trail that explains how the current balances were produced.

### Tickets and Results

Tickets can be entered manually or prepared from screenshots. The browser enhances an image and uses Tesseract.js to extract candidate values such as the lottery type, draw date, ticket number, cost, award, and linked free-play ticket.

OCR output is never treated as authoritative. The user reviews the extracted fields before the API applies domain validation and saves the result. This boundary keeps automation useful without allowing an uncertain image-reading step to alter financial records silently.

### Lottery History and Lucky Numbers

The application imports historical draw information and calculates number-frequency summaries. It can generate frequency-weighted selections for the supported games, while clearly stating that historical frequency does not predict future winning numbers.

## How the Architecture Works

The system separates presentation, application behavior, domain rules, and infrastructure concerns.

| Layer | Responsibilities |
| --- | --- |
| Angular client | Routing, feature screens, forms, HTTP calls, OCR review, and API status |
| ASP.NET Core API | REST endpoints, request validation, exception translation, health checks, and OpenAPI |
| Application and domain | Use cases, participant rules, ledger behavior, ticket validation, and reversals |
| Infrastructure | EF Core repositories, SQL Server, workbook imports, and external lottery-history calls |

A typical ticket-image operation follows a deliberate sequence:

1. The user drops one or more screenshots into the browser.
2. Tesseract extracts possible ticket or result values.
3. The user reviews and corrects the proposed fields.
4. The Angular client sends the confirmed request to the API.
5. Domain validation runs before EF Core persists the change.

The development proxy routes Angular requests for `/api` and `/health` to the ASP.NET Core process. Health endpoints distinguish basic process liveness from readiness checks that include required dependencies.

## Designing the Financial History

The most important technical decision was to represent money as a ledger rather than a mutable balance field.

Each financial action becomes a batch with participant-level entries. The current balance is a result of that history. This makes deposits, purchases, prizes, adjustments, and reversals explainable instead of leaving only the latest number.

The same principle appears in the import workflow. Workbook imports support a dry run, require reconciliation before committing financial data, and record source checksums to prevent the same workbook from being imported accidentally more than once.

These safeguards add implementation work, but they address the actual failure modes of the original process: missed payments, duplicate entry, destructive corrections, and unclear balances.

## Building It with Agentic Coding

I developed Group Lotto through a human-directed, agent-assisted workflow. I supplied the real process, business rules, priorities, and review feedback. Coding agents helped translate those decisions into application structure, Angular components, API endpoints, tests, documentation, and iterative fixes.

The agentic workflow was especially useful for tasks that cross several layers at once. A ticket-processing change, for example, may require an Angular form update, a request contract, domain validation, persistence changes, and tests. An agent can inspect those connections and prepare a coherent implementation, while I remain responsible for deciding what the system should do and reviewing the result.

This project reinforced an important lesson: AI works best as part of an engineering process with explicit boundaries. Repository instructions, automated checks, tests, reviewable diffs, and human approval matter more than the ability to generate code quickly.

## Production Boundaries

The project includes validation, health checks, Problem Details responses, import safeguards, and automated tests. It also has an important deployment boundary: the current API pipeline includes authorization middleware, but its controllers do not yet enforce authentication or role policies.

Before exposing the application to an untrusted network, I would add an identity provider, endpoint authorization policies, secret management, HTTPS enforcement, operational monitoring, backups, and deployment-specific CORS configuration.

Calling out this limitation is intentional. A portfolio project should explain not only what works, but also what must change before the system is treated as production-ready.

---

## View Group Lotto on GitHub

The repository contains the Angular client, ASP.NET Core API, domain and infrastructure projects, tests, database migrations, and the detailed product and architecture guide.

**[Explore Group Lotto on GitHub](https://github.com/devJunkim/CoxLotto)**
