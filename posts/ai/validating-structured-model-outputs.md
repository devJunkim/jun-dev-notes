---
title: "Structured AI Outputs: Validate Model Data Before Your Application Uses It"
excerpt: "Build a deterministic boundary around model-generated JSON with bounded parsing, schema checks, evidence validation, and application-owned decisions."
category: "AI"

seo:
  focusKeyword: "validate structured AI outputs"
  description: "Validate structured AI outputs with strict JSON parsing, explicit schemas, evidence checks, bounded retries, and server-owned authorization."
  socialTitle: "Structured AI Outputs Need Application Validation"
  socialDescription: "Turn model-generated JSON into a reviewable proposal instead of treating a well-shaped response as a correct or authorized decision."
---

# Structured AI Outputs: Validate Model Data Before Your Application Uses It

A model returns valid JSON naming a support queue. Deserialization succeeds, the queue exists, and the application routes the ticket. None of those facts proves that the ticket belongs there or that a sentence inside the ticket should have been treated as an instruction.

Structured output makes integration easier, but an application still needs a boundary between generated data and accepted decisions.

> **Quick answer:** Bound the response, parse it strictly, validate its shape and domain rules, and verify evidence against trusted application context. Treat the result as a proposal. The server must retain ownership of authorization, resource selection, and any consequential action.

## Define the Decision Before the Schema

Consider a model that suggests a queue for a support ticket. Give it a small set of queue labels and an explicit review outcome when the evidence is insufficient. Do not ask it to invent queue IDs, account IDs, destination URLs, or commands.

This contract permits either a route recommendation or a request for review:

```json
{
  "type": "object",
  "properties": {
    "decision": { "type": "string", "enum": ["route", "review"] },
    "queue": { "type": "string", "enum": ["billing", "delivery", "review"] },
    "evidenceQuote": { "type": ["string", "null"] }
  },
  "required": ["decision", "queue", "evidenceQuote"],
  "additionalProperties": false
}
```

The schema requires the three fields and rejects extra ones, but it deliberately leaves cross-field meaning to application validation. For example, `decision: route` with `queue: review` is structurally permitted here and semantically invalid.

JSON Schema's [object reference](https://json-schema.org/understanding-json-schema/reference/object) distinguishes declared properties from required properties and explains additional-property handling. A field being present is also different from its value being non-null.

Use a provider's schema-constrained generation mode when the selected model and endpoint support the needed contract. Verify that endpoint's supported schema subset and completion/error states. The local gate must still handle malformed responses, refusal, truncation, and transport failure; this article does not depend on one provider's SDK or promise identical behavior across providers.

## Put a Bounded Parser at the Boundary

Do not deserialize an unbounded response body directly into a business entity. Apply byte limits while receiving it, limit JSON nesting, reject unknown fields, and decide how duplicate property names are handled.

The following .NET 10 example accepts only the JSON payload extracted from a completed model response. The transport adapter remains responsible for envelope status and receive limits. The validator's size check is a second gate, not a replacement for limiting the network read.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class RoutingProposal
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("queue")]
    public required string Queue { get; init; }

    [JsonPropertyName("evidenceQuote")]
    public required string? EvidenceQuote { get; init; }
}

public static class RoutingProposalValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        MaxDepth = 8,
    };

    public static RoutingProposal Validate(byte[] utf8Json, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ArgumentNullException.ThrowIfNull(sourceText);
        if (utf8Json.Length is 0 or > 4096)
        {
            throw new JsonException("Proposal size is outside the allowed range.");
        }

        using JsonDocument document = JsonDocument.Parse(
            utf8Json, new JsonDocumentOptions { MaxDepth = 8 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Proposal must be an object.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException("Duplicate proposal field.");
            }
        }

        RoutingProposal proposal = document.RootElement
            .Deserialize<RoutingProposal>(JsonOptions)
            ?? throw new JsonException("Proposal is missing.");

        if (proposal.Decision is not ("route" or "review") ||
            proposal.Queue is not ("billing" or "delivery" or "review"))
        {
            throw new JsonException("Unknown routing value.");
        }

        if (proposal.Decision == "review")
        {
            if (proposal.Queue != "review")
            {
                throw new JsonException("Review requires the review queue.");
            }
        }
        else if (proposal.Queue == "review" ||
                 string.IsNullOrWhiteSpace(proposal.EvidenceQuote))
        {
            throw new JsonException("Routing requires a target queue and evidence.");
        }

        if (proposal.EvidenceQuote is { } quote &&
            (string.IsNullOrWhiteSpace(quote) || quote.Length > 240 ||
             !sourceText.Contains(quote, StringComparison.Ordinal)))
        {
            throw new JsonException("Evidence must be a short, exact source quote.");
        }

        return proposal;
    }
}
```

The serializer rejects missing required properties, unknown properties, and explicit nulls for the non-nullable fields. These are separate controls: see Microsoft's documentation for [required JSON properties](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/required-properties), [unmapped members](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members), and [nullable annotation enforcement](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations).

The application-specific checks then enforce allowed combinations and evidence rules. This is a validator for this fixed contract, not a general JSON Schema engine. If the schema evolves, update both the DTO and semantic gate together. The 240 limit measures UTF-16 code units; it is an application limit in addition to the byte cap.

## Evidence Presence Does Not Establish Correctness

Requiring an exact quote prevents the model from inventing the quoted text. It does not prove that the quote supports the selected queue. A ticket containing “this is not a billing problem” contains the word “billing” and still may concern delivery.

Likewise, a malicious ticket can contain both a requested label and a convincing-looking explanation. The model can produce schema-valid output influenced by that instruction. Validate the result's meaning through task-specific checks, representative evaluations, and review where the consequence justifies it.

Use the exact source version submitted to the model when checking evidence. If preprocessing removes personal data or normalizes text, validate against that transformed version and retain its safe version identifier. Comparing to a subsequently edited ticket can reject valid evidence or accept evidence from the wrong request.

## Keep Authority Outside Generated Fields

The application should map a validated queue label to a server-owned queue identifier and check the authenticated actor's permission to route that ticket. Do not let the response supply the tenant, override access rules, or choose an arbitrary network destination.

A human review path is an ordinary outcome, not a parser failure to hide. The proposed `review` decision allows the system to preserve uncertainty without manufacturing a confident route.

OWASP's [LLM application risks](https://owasp.org/projects/top-10-for-large-language-model-applications) include prompt injection and improper output handling. For this workflow, the concrete response is to keep source text as data, render quotes as text, and prevent generated fields from becoming executable instructions. A system prompt asking the model to be careful is only one layer.

The validator above returns a proposal and performs no routing. A later application command owns authorization, concurrency, and durable effects. If that command can be retried, it needs its own idempotency contract independently of model generation.

## Classify Failures Before Retrying

Different failures deserve different handling:

| Failure | Useful response |
| --- | --- |
| Timeout or transient provider error | Bounded retry within the request budget |
| Refusal or incomplete generation | Explicit unavailable/review outcome, using the provider's envelope |
| Invalid shape or semantic combination | Record a safe validation code; optionally retry generation within a small fixed budget |
| Insufficient evidence | Review or abstention, rather than forced completion |
| Authorization failure | Reject the action in application code |

Do not repeatedly ask for “valid JSON” when the real failure is unsupported evidence. If one repair attempt is justified, provide concise validation feedback rather than secrets, full logs, or an unbounded conversation history. Generation retries must not repeat an already-performed business action.

Version the schema, prompt, model configuration, and validator together. Log those versions, completion state, latency, validation outcome, and final disposition. Keep raw tickets and quotes out of routine logs unless an approved retention policy explicitly allows them.

## Test the Gate and the Model Separately

Deterministic tests should cover valid routing, review with null evidence, missing fields, explicit nulls, extra or duplicate fields, invalid labels, incompatible combinations, excessive nesting, oversized payloads, and fabricated quotes.

Model evaluations need different cases: ambiguous tickets, negation, adversarial instructions, irrelevant evidence, and changes in the task distribution. Measure correct decisions and appropriate abstentions, not only JSON acceptance rate. A perfectly parsed wrong decision remains wrong.

[Evaluating AI Coding Agents on Your Own Repository](https://dev.jun-kim.net/2026/09/17/how-to-evaluate-ai-coding-agents-on-your-own-repository/) establishes the discipline of versioned cases and failure analysis. This workflow applies that discipline to application data extraction and routing, rather than evaluating code changes.

Start with suggestions visible to an operator. Expand automation only when observed decision quality and the action's consequence support it, keeping the deterministic gate and application authorization in place.
