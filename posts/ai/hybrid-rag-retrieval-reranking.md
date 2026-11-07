---
title: "Hybrid RAG Retrieval: Fuse Keyword and Vector Search Before Reranking"
excerpt: "Combine lexical and dense retrieval with rank fusion, metadata enforcement, and measured reranking instead of trusting one search signal."
category: "AI"

seo:
  focusKeyword: "hybrid RAG retrieval"
  description: "Design hybrid RAG retrieval with keyword and vector search, reciprocal rank fusion, reranking, authorization filters, and evaluation."
  socialTitle: "Hybrid RAG Retrieval: Fusion Before Generation"
  socialDescription: "Recover exact identifiers and semantic matches, fuse rankings safely, and evaluate reranking within real latency and context budgets."
---

# Hybrid RAG Retrieval: Fuse Keyword and Vector Search Before Reranking

A dense retriever understands that “cancel my subscription” resembles “terminate a plan,” but misses an exact error code. A keyword retriever finds the code immediately but ranks a semantically irrelevant troubleshooting page above the policy that answers the user’s intent.

> **Quick answer:** Run lexical and dense retrieval over the same authorized corpus, fuse their ranked lists with a method such as reciprocal rank fusion, then rerank a bounded candidate set when evaluation shows enough value. Preserve filters and stable identifiers throughout, and measure each stage separately before sending context to the model.

## Lexical and Dense Retrieval Solve Different Problems

Lexical search is strong when the query contains discriminating text:

- product and account identifiers;
- error codes;
- exact API names;
- legal phrases;
- rare names and acronyms.

Dense vector search can recover semantic relationships when wording differs:

- “end membership” and “cancel subscription”;
- “worker cannot reach queue” and “background processor connectivity”;
- paraphrased questions and descriptive symptoms.

Neither signal dominates every corpus. Hybrid retrieval treats them as candidate generators with complementary failure modes rather than forcing one representation to solve both exact matching and semantic similarity.

## Apply the Same Security and Corpus Boundary

Both retrievers must search the same eligible document population. Apply tenant, role, publication, locale, product version, and effective-date constraints before or during candidate generation where the engine supports it.

```json
{
  "query": "ERR-1842 after password reset",
  "tenantId": "example-company",
  "roles": ["support-agent"],
  "productVersion": "2026.11",
  "topKPerRetriever": 40
}
```

Do not run unfiltered vector search and rely on the model to ignore forbidden passages. Also avoid retrieving a tiny global candidate set and filtering most of it afterward; poor eligible recall can look like a ranking problem when it is a filtering problem.

Record corpus, parser, chunking, embedding, lexical index, and authorization-policy versions so an evaluation result can be reproduced.

## Retrieve Candidates in Parallel

A simplified pipeline looks like this:

```text
normalized query
      ├── lexical retrieval ── ranked list A
      └── dense retrieval   ── ranked list B
                                  ↓
                           rank fusion
                                  ↓
                         bounded reranking
                                  ↓
                         context selection
```

Run independent retrieval calls concurrently when the backend and capacity model permit it. Set deadlines for each branch. Decide whether one branch may degrade gracefully if the other fails, and record which path produced the final result.

Do not concatenate raw relevance scores directly. BM25 scores and vector similarities have different scales and distributions, which can change across queries and index versions.

## Fuse Rankings with Reciprocal Rank Fusion

Reciprocal rank fusion uses positions rather than incomparable raw scores. For document `d` across ranked lists, a common form is:

```text
RRF(d) = Σ 1 / (k + rank_i(d))
```

A document receives more credit when it ranks highly in either list and additional credit when both retrievers find it.

```csharp
static IReadOnlyList<string> Fuse(
    IEnumerable<IReadOnlyList<string>> rankedLists,
    int constant = 60)
{
    var scores = new Dictionary<string, double>(StringComparer.Ordinal);

    foreach (IReadOnlyList<string> list in rankedLists)
    {
        for (var index = 0; index < list.Count; index++)
        {
            string id = list[index];
            scores[id] = scores.GetValueOrDefault(id)
                + 1.0 / (constant + index + 1);
        }
    }

    return scores
        .OrderByDescending(item => item.Value)
        .ThenBy(item => item.Key, StringComparer.Ordinal)
        .Select(item => item.Key)
        .ToArray();
}
```

The deterministic secondary sort makes equal scores reproducible. Production code also carries source ranks, retrieval reasons, and chunk metadata rather than only IDs.

Azure AI Search documents [reciprocal rank fusion for hybrid search](https://learn.microsoft.com/en-us/azure/search/hybrid-search-ranking) and uses it to merge parallel full-text and vector result lists. Its constant and score behavior are service details; treat the general formula as a starting point, not a universal tuned setting.

## Deduplicate at the Right Identity

The same chunk may appear in both lists. Deduplicate by stable chunk ID before reranking. Near-duplicate overlapping chunks require a separate policy.

If five adjacent chunks from one document fill the candidate set, group or cap them so independent evidence can compete. Parent-child retrieval may rank a focused child and later expand to a containing section, but expansion must preserve the exact matching span for citations.

Do not deduplicate only by normalized text. Identical policy text can belong to different tenants, versions, or effective dates that must remain distinct for authorization and citation.

## Rerank a Bounded Candidate Set

Fusion improves coverage but may not produce the best final order. A cross-encoder or model-based reranker can evaluate the query and each candidate together with richer interaction than an embedding similarity.

Reranking adds latency, cost, and another model dependency. Bound it:

- retrieve enough candidates from each source to protect recall;
- fuse and deduplicate;
- rerank only the top candidate window;
- enforce a per-query deadline;
- fall back to the fused order when the reranker is unavailable if that is safe;
- version the model and prompt or scoring configuration.

Do not send sensitive passages to an external reranking service unless the data boundary permits it. Apply authorization before reranking, not afterward.

## Separate Candidate Recall from Final Ranking

Evaluate each stage with a labeled query set:

| Stage | Useful question |
| --- | --- |
| Lexical candidates | Did exact terms and identifiers retrieve the evidence? |
| Dense candidates | Did paraphrases and semantic intent retrieve it? |
| Fused list | Did combining lists improve recall within the candidate budget? |
| Reranked list | Did required evidence move into the context window? |
| Packed context | Did deduplication or token limits drop it? |
| Generated answer | Did the model use and cite it correctly? |

Measure Recall@k for candidate generation, MRR or NDCG for ordering, eligible-result count after filters, duplicate occupancy, latency, and cost. Use `k` values that match real reranking and context budgets.

[Evaluating RAG Retrieval Quality](https://dev.jun-kim.net/2026/09/24/evaluating-rag-retrieval-quality-measure-the-context-before-the-answer/) explains how to separate retrieval and answer evaluation. Hybrid search should earn its complexity on that dataset.

## Slice by Query Type

A global average can hide regressions. Label queries such as:

- exact identifier;
- natural-language paraphrase;
- mixed identifier and intent;
- short ambiguous phrase;
- multi-constraint policy question;
- unanswerable query;
- access-controlled query.

Hybrid retrieval may improve mixed workloads while diluting a domain where dense search already ranks well. Weighted fusion, query routing, or field-specific lexical search can help, but each adds configuration that must be evaluated and versioned.

Do not assume hybrid is automatically better. The purpose of the lexical branch, fusion, and reranker is to address measured failure classes.

## Keep Diagnostics Safe and Actionable

For evaluation fixtures, retain the query, labels, candidates, source ranks, fusion score, reranker score, selected context, and version identifiers. For production, prefer redacted queries and opaque document IDs according to privacy policy.

Useful operational signals include:

- empty and low-eligible-result rates;
- lexical-only and dense-only contribution;
- reranker timeout and fallback rate;
- candidate and final context sizes;
- stale or unauthorized citation attempts;
- latency and cost by pipeline stage.

Never log full restricted passages merely because retrieval debugging is difficult.

## Roll Out Retrieval Changes Like Application Changes

Build a versioned index or partition for a new lexical analyzer, embedding model, fusion policy, or reranker. Run the same evaluation set, shadow representative queries, and compare quality, latency, and cost before shifting traffic.

Keep the previous index and configuration available for rollback. An application rollback that still points to a newly rebuilt incompatible index is not a complete recovery.

Hybrid retrieval is most useful as an explicit pipeline: complementary candidate generation, rank-aware fusion, bounded reranking, and measured context selection. That structure makes failures diagnosable instead of hiding every retrieval decision behind one opaque relevance score.
