# Demo: RAG on Azure AI Foundry (.NET)

## Context
A developer-facing demo of how to stand up an "AI product" with **Retrieval-Augmented
Generation (RAG)** on **Azure AI Foundry**. The deliverable is a small, reliable, self-contained
.NET app plus a demo script. Decisions confirmed via interview:

- **Format:** code-based .NET console app (web UI is a stretch goal, not built first).
- **Audience:** .NET developers — the code shows the RAG mechanics explicitly (Retrieve → Augment → Generate), not a black box.
- **Data:** synthetic, **mining-flavoured** knowledge base (site safety, equipment maintenance, shift procedures) — no external data needed.
- **Azure resources:** none yet — we provide a provisioning script + a portal checklist fallback.
- **Retrieval:** **hybrid (vector + keyword) + semantic ranker** → requires Azure AI Search **Basic** tier (Free tier has no semantic ranker).

Goal on stage: `ingest` builds a vector index from the docs; `chat` answers grounded questions with citations, and visibly says "I don't know" when the answer isn't in the docs — the core RAG value prop.

## Architecture
```
data/*.md ──chunk──> embed (Foundry embedding model) ──> push ──> Azure AI Search (vector + text + semantic)
                                                                          │
user question ──embed──> hybrid+semantic query ──top-K chunks──> grounded prompt ──> Foundry chat model ──> answer + [citations]
```
- **Foundry-deployed models** called via `Azure.AI.OpenAI` v2 (`AzureOpenAIClient` → `GetEmbeddingClient` / `GetChatClient`). Correct, deterministic choice for explicit RAG (not a persistent agent, so `AIProjectClient` is not needed here — noted as a "level-up" in the README).
- **Azure AI Search** via `Azure.Search.Documents` **12.0.0** for the index + hybrid/semantic query.
- **Auth:** keyless `DefaultAzureCredential` by default (az login + RBAC); automatic fallback to API keys if present in config.

## Files (under `rag-foundry-demo/`)
Project + packages scaffolded: `Azure.AI.OpenAI 2.1.0`, `Azure.Search.Documents 12.0.0`,
`Azure.Identity`, `Microsoft.Extensions.Configuration[.Json/.EnvironmentVariables]`.

- `Program.cs` — arg routing (`ingest` | `chat` | `reset`), load config, build clients, dispatch.
- `RagConfig.cs` — bind `appsettings.json` + env vars: endpoints, chat/embedding deployment names, index name, embedding dimensions, `UseSemanticRanker`, `TopK`, chunk size/overlap, optional API keys.
- `Rag/AzureClients.cs` — client factory; chooses `AzureKeyCredential` vs `DefaultAzureCredential`.
- `KnowledgeChunk.cs` — index model: `id`, `title`, `source`, `chunk`, `contentVector` (with `JsonPropertyName`).
- `Rag/DocumentChunker.cs` — load `data/*.md`, split into overlapping chunks (paragraph-aware, ~500-token window with overlap), tag each with source filename + title + ordinal.
- `Rag/Embedder.cs` — batch `GenerateEmbeddings`, `Dimensions` from config, retry on 429.
- `Rag/SearchIndexManager.cs` — schema (`SimpleField` key, `SearchableField` title/source/chunk, `VectorSearchField("contentVector", dims, "vector-profile")`), `VectorSearch` (Hnsw + profile), `SemanticConfiguration`; `CreateOrUpdateIndex`; upload batches.
- `Rag/RagChatService.cs` — per question: embed → `SearchClient.SearchAsync<KnowledgeChunk>(question, options)` with `VectorizedQuery` + (if enabled) `QueryType.Semantic` + `SemanticSearchOptions`; build numbered grounded context; `ChatClient.CompleteChatAsync` with a system prompt that forbids ungrounded answers and requires `[n]` citations; print answer + source list.
- `data/` — ~5 mining-themed markdown docs (`site-safety-induction.md`, `haul-truck-maintenance.md`, `shift-handover-procedure.md`, `ppe-policy.md`, `incident-reporting.md`).
- `provision.ps1` — az CLI: resource group → Azure AI Services (Foundry) resource → deploy chat (`gpt-4.1`) + embedding (`text-embedding-3-small`) → Azure AI Search **Basic** with semantic search enabled → RBAC role assignments → print env values.
- `appsettings.json` (committed template) + `appsettings.local.json` (git-ignored) + `.env.example`.
- `.gitignore` — `bin/`, `obj/`, `appsettings.local.json`.
- `README.md` — prerequisites, provision, configure, `ingest`, `chat`, demo script + sample questions, architecture diagram, cost & cleanup, troubleshooting, "level-up" notes.

## Key SDK specifics (verified for installed versions)
- Vector field: `new VectorSearchField("contentVector", dimensions, "vector-profile")` (12.0.0).
- Query vector: `new VectorizedQuery(queryVector){ KNearestNeighborsCount = TopK, Fields = { "contentVector" } }`.
- Semantic: `options.QueryType = SearchQueryType.Semantic; options.SemanticSearch = new SemanticSearchOptions { SemanticConfigurationName = "semantic-config" };`
- Embeddings: `client.GetEmbeddingClient(dep).GenerateEmbeddings(texts, new EmbeddingGenerationOptions{ Dimensions = dims }); ...ToFloats().ToArray()`.
- Chat: `client.GetChatClient(dep).CompleteChatAsync(messages)` → `completion.Value.Content[0].Text`.

## Verification
1. `dotnet build` — zero errors.
2. Provision via `provision.ps1` (needs `az login` + subscription).
3. Configure `appsettings.local.json` with printed endpoints/deployment names.
4. `dotnet run -- ingest` → index created + N chunks uploaded.
5. `dotnet run -- chat` → "What PPE is required on the haul road?" (grounded + cited) and "What's the parental leave policy?" (should answer "I don't know").

## Out of scope (mentioned in README, not built first)
Web UI, Foundry Agent + AI Search tool variant, integrated vectorization, evaluation/observability.

---
_Presentation deck for this plan: `slides.html` (reveal.js — open in any browser)._
