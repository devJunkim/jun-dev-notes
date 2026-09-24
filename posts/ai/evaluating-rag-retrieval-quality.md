---
title: "Evaluating RAG Retrieval Quality: Measure the Context Before the Answer"
excerpt: "Evaluate retrieval-augmented generation by separating search recall, ranking, context quality, grounded answers, and production failure analysis."
category: "AI"

seo:
  focusKeyword: "RAG retrieval evaluation"
  description: "Evaluate RAG retrieval with labeled queries, recall and ranking metrics, chunk diagnostics, groundedness checks, and production feedback loops."
  socialTitle: "RAG Retrieval Evaluation Before Answer Scoring"
  socialDescription: "Find whether failures come from missing documents, poor ranking, context construction, or generation instead of blaming the model for every bad answer."
---

# Evaluating RAG Retrieval Quality: Measure the Context Before the Answer

A retrieval-augmented generation system gives a confident wrong answer. The team changes the prompt and model, but the required policy paragraph never appeared in the retrieved context. Generation could not reliably use evidence it did not receive.

End-to-end answer scores are useful, but they hide where the pipeline failed. Retrieval needs its own dataset, metrics, and diagnostics.

> **Quick answer:** Build a versioned query set with relevant document or chunk labels, measure whether required evidence is retrieved and ranked within the context budget, then evaluate groundedness and answer quality separately. Slice failures by query type, filters, tenant, and corpus version before tuning chunking or embeddings.

## Define the Retrieval Unit and Relevance Rule

Decide what the system retrieves: whole documents, sections, passages, records, or parent-child combinations. Metrics are meaningless when labels refer to documents but the runtime ranks chunks without a stable mapping.

For each evaluation query, record:

```json
{
  "queryId": "refund-window-01",
  "query": "How long can a Canadian customer request a refund?",
  "tenant": "example-retail",
  "corpusVersion": "2026-09-20",
  "relevantDocumentIds": ["policy-refunds-ca"],
  "requiredEvidence": ["refund-window-standard"],
  "answerable": true
}
```

Document relevance can be graded rather than binary. A canonical policy section may be essential, while an FAQ is helpful but insufficient. Keep the expected evidence separate from a preferred answer wording so the test does not punish a correct paraphrase.

Include unanswerable and access-controlled queries. A good retriever should not fill a missing answer with merely similar text or cross a tenant boundary to improve recall.

## Measure Recall and Ranking Within the Real Budget

Recall at `k` asks whether the relevant material appears in the first `k` results. Mean reciprocal rank emphasizes how early the first relevant result appears. NDCG supports graded relevance across a ranked list.

Choose `k` from the context construction policy, not from a flattering offline chart. Retrieving the right passage at rank 40 is irrelevant if only the first eight chunks reach the model.

Microsoft's [RAG evaluator guidance](https://learn.microsoft.com/en-us/azure/foundry/concepts/evaluation-evaluators/rag-evaluators) separates document retrieval measures from final response relevance and groundedness. That separation is useful even when the implementation uses different tooling.

Track more than one aggregate number:

| Measure | Question |
| --- | --- |
| Recall@k | Did the context budget include required evidence? |
| MRR | How early did the first relevant result appear? |
| NDCG@k | Did the ranking prioritize more useful evidence? |
| Context precision | How much retrieved context was actually relevant? |
| Empty-result rate | How often did filtering or indexing return nothing? |

High recall with low precision can still hurt: irrelevant chunks consume tokens and may distract generation. The acceptable balance depends on the task and context budget.

## Diagnose the Pipeline Stage That Lost Evidence

Record intermediate artifacts with safe identifiers and versions:

1. normalized query and any generated search variants;
2. applied tenant, permission, date, and document-type filters;
3. candidate IDs and raw retrieval scores;
4. reranked order and score;
5. chunks selected for the final context;
6. citations actually used in the response.

This trail distinguishes several failure classes. The document may be missing from the index, filtered incorrectly, poorly chunked, absent from initial candidates, demoted by reranking, dropped during context packing, or present but ignored by generation.

Do not log raw sensitive queries or passages by default. Store redacted evaluation fixtures and opaque production identifiers according to retention policy. Evaluation access must enforce the same tenant boundaries as serving.

## Test Chunking as a Retrieval Hypothesis

Chunk size is not a universal constant. Small chunks can match precise phrases but lose definitions and exceptions. Large chunks preserve context but dilute embeddings and consume the prompt budget.

Version the parser, chunk boundaries, overlap, metadata extraction, embedding model, and index configuration. When one changes, rebuild the relevant index and run the same evaluation set. Comparing a new retriever against labels produced from a different corpus snapshot can create false regressions.

Inspect boundary failures manually. Tables, headings, code blocks, footnotes, and repeated navigation text often need format-aware parsing. Overlap can recover a split sentence, but excessive overlap returns near-duplicates that crowd out independent evidence.

Parent-child retrieval can rank a focused passage and then expand to a containing section for generation. Measure both the ranked child and the final expanded context because expansion can reintroduce noise.

## Evaluate Filters and Authorization Before Similarity

Metadata filtering is part of retrieval correctness. A semantically perfect result from another customer is a security failure, not a high-quality answer.

Create tests for allowed and forbidden tenants, expired documents, draft versus published states, locale, product version, and user permissions. Apply authorization in the retrieval layer or a trusted post-filter that cannot be bypassed by model instructions.

Be careful with post-filtering after a small vector search. If nine of ten candidates are forbidden, the one remaining result may look like poor semantic retrieval when the real problem is candidate generation under filters. Prefer filter-aware search where supported and measure eligible-candidate counts.

## Evaluate Generation After Retrieval

Once the expected evidence is present, evaluate whether the answer is grounded in that context, relevant to the question, complete enough for the task, and appropriately abstains when evidence is missing.

An answer can be grounded but incomplete. It can also be factually plausible yet unsupported by the supplied context. Keep those labels distinct.

LLM-as-judge scoring can accelerate review, but calibrate it against human-labeled cases, pin the judge configuration, and monitor disagreement. A judge is another model with variance and bias, not an objective test oracle.

For high-impact workflows, verify citations deterministically: cited identifiers must have been retrieved, the referenced span must exist in the version served, and the user must be authorized to view it. Citation presence alone does not prove the sentence is supported.

## Turn Production Failures Into Evaluation Cases

Collect explicit feedback, support escalations, zero-result queries, low-confidence paths, and corrected answers. Sample them with privacy controls and turn representative failures into reviewed fixtures.

Report metrics by query class rather than only a global average: exact identifiers, policy questions, multi-hop questions, recent documents, ambiguous wording, and unanswerable requests. A change that improves common FAQ retrieval can still break rare compliance queries.

Use a shadow index or controlled traffic slice for risky changes. Compare latency, cost, recall, context size, abstention, and downstream answer quality. Keep a rollback path for the index and retrieval configuration as well as the model.

A RAG system is not one model call. Measuring the evidence path makes optimization concrete: retrieve the right material, preserve it through ranking and packing, and only then ask whether generation used it well.
