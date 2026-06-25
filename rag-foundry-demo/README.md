# RAG on Azure AI Foundry — .NET demo

A small, self-contained .NET console app that shows how to build a **Retrieval-Augmented
Generation (RAG)** assistant on **Azure AI Foundry**: ingest your documents into a vector
index, then answer questions **grounded in those documents** with citations — and say
*"I don't know"* when the answer isn't there.

The knowledge base is a synthetic **mining operations** corpus (safety, equipment,
shift procedures) under [`data/`](./data) — no external data required.

> Presenting this? Open **[`slides.html`](./slides.html)** in a browser for the deck, and
> see **[`PLAN.md`](./PLAN.md)** for the design rationale.

## How it works

```
data/*.md ──chunk──▶ embed ──▶ Azure AI Search (text + vector + semantic)
                                          │
question ──embed──▶ hybrid + semantic search ──top-K──▶ grounded prompt ──▶ gpt-4.1 ──▶ answer + [citations]
```

| Concern | Choice |
|---|---|
| Models | Azure AI Foundry deployments — `gpt-4.1` (chat) + `text-embedding-3-small` (1536-dim) |
| Retrieval | **Hybrid** (keyword ∪ vector) + **semantic ranker** via `Azure.Search.Documents` 12.0 |
| Model calls | `Azure.AI.OpenAI` v2 (`AzureOpenAIClient`) |
| Auth | Keyless `DefaultAzureCredential` by default; API-key fallback if you set keys |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) + an Azure subscription
- `az login`

## 1. Provision Azure resources (from zero)

```powershell
./provision.ps1 -ResourceGroup rg-rag-foundry-demo -Location eastus2
```

This creates a resource group, an **Azure AI Services (Foundry)** account with `gpt-4.1`
and `text-embedding-3-small` deployments, an **Azure AI Search** service (**Basic** tier —
required for the semantic ranker), assigns RBAC roles, and **writes `appsettings.local.json`
for you** with the endpoints + API keys filled in (it's git-ignored). Pass `-NoLocalConfig`
to skip that and configure manually instead.

<details>
<summary>Prefer the portal? Manual checklist</summary>

1. **Resource group** in your region.
2. **Azure AI Foundry** project (or an Azure AI Services resource). Deploy two models:
   `gpt-4.1` and `text-embedding-3-small`. Note the **Azure OpenAI endpoint**.
3. **Azure AI Search** — **Basic** tier; on the service, enable **Semantic ranker** (Free plan)
   and set **API access control** to *Both* (RBAC + keys).
4. Role assignments on your user: **Cognitive Services OpenAI User** (on the AI resource),
   **Search Index Data Contributor** + **Search Service Contributor** (on the Search service).
</details>

## 2. Configure

`provision.ps1` already wrote `appsettings.local.json` with your endpoints + keys, so you
can skip straight to step 3. (If you used `-NoLocalConfig`, copy
`appsettings.local.json.example` → `appsettings.local.json` and paste the printed values.)

`appsettings.local.json` is git-ignored. You can override any setting with a
`RAG_`-prefixed environment variable (e.g. `RAG_TopK=8`).

## 3. Run

```bash
dotnet run -- config     # show resolved config + auth mode (sanity check)
dotnet run -- chunk      # load + chunk data/*.md (offline, no Azure)
dotnet run -- ingest     # chunk → embed → index into Azure AI Search
dotnet run -- chat       # grounded Q&A loop with citations
dotnet run -- reset      # delete the index
```

## Demo script

After `ingest`, run `chat` and walk these three questions:

1. **"What PPE is required on the haul road?"**
   → grounded answer citing `ppe-policy.md` (full hi-vis, duress beacon/radio, cut-resistant gloves).
2. **"How do I report a near-miss?"**
   → steps from `incident-reporting.md` (radio supervisor → log in SafeTrack same shift).
3. **"What's the company's parental-leave policy?"** ← the punchline
   → *"I don't know — that isn't in the knowledge base."*

The third question is the point: a grounded assistant **declines** rather than hallucinating.
Then edit a doc, re-run `ingest`, and show the answer change — no retraining.

## Web UI (Blazor Server, streaming)

A browser chat over the **same** `RagChatService` lives in [`web/`](./web) — a separate
project that references this one, so the RAG logic isn't duplicated. Answers **stream in
token-by-token**, follow-ups show the rewritten query, and citations link to source files.

```bash
dotnet run --project web        # then open the printed https://localhost:xxxx URL
```

No extra setup: the web project reuses `appsettings.local.json` (so with API keys configured
it "just works" — no credential env vars needed). The console app is unaffected.

## Auth notes

- **Keyless (default & recommended):** `DefaultAzureCredential` uses your `az login` (or a
  managed identity in production). Requires the RBAC roles the script assigns. Role
  propagation can take a few minutes after provisioning.
- **API keys (fallback):** set `OpenAIApiKey` / `SearchApiKey` in `appsettings.local.json`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `Missing config: …` | Fill `appsettings.local.json` or set `RAG_*` env vars. |
| `401 … "token failed validation"` that never clears | You're on a host with a **managed identity** (e.g. an Azure VM) that `DefaultAzureCredential` picks ahead of your `az login`. Force developer creds: set env var **`AZURE_TOKEN_CREDENTIALS=dev`**, then re-run. (Alternatively use API keys.) |
| `401/403` on first run, then works | RBAC roles still propagating — wait a few minutes. |
| `SemanticConfiguration` / semantic errors | Semantic ranker needs Search **Basic+** with the semantic plan enabled. Or set `"UseSemanticRanker": false`. |
| Embedding dimension mismatch | `EmbeddingDimensions` must match the model (1536 for `-small`, 3072 for `-large`) **and** the indexed data. Re-run `reset` then `ingest` after changing it. |

## Cost, teardown & re-enable

Chat/embedding are pay-per-token (cents for this corpus). The only standing cost is **Azure
AI Search Basic** (~US$0.13/hour while it exists). Two helper scripts manage this:

**Stop the cost between demos** — deletes *only* Search (keeps the Foundry account + model
deployments, which cost ~$0 idle):

```powershell
./teardown.ps1            # delete Search only
./teardown.ps1 -All       # delete the whole resource group
```

**Bring it back** — recreates Search (re-using the existing Foundry account), refreshes
`appsettings.local.json` with the new key, and rebuilds the index. ~5 minutes:

```powershell
./reenable.ps1            # recreate Search + write config + ingest
```

> `reenable.ps1` uses your current `az` context (pass `-Subscription <id>` to override), then runs the idempotent `provision.ps1`
> (which skips the existing Foundry account and only recreates Search). It also works after
> a full `-All` teardown — in that case it recreates everything. A recreated Search service
> gets a **new** key, which is why `appsettings.local.json` is rewritten and the index
> (which lived inside Search) must be re-ingested.

## Level-ups (not built here)

- **Foundry Agent + Azure AI Search tool** — let an agent (`AIProjectClient`) do retrieval as a
  tool, with memory and multi-turn threads.
- **Integrated vectorization** — let Azure AI Search chunk + embed during indexing (skillsets/indexers).
- **Agentic retrieval** — query planning, parallel sub-queries, answer synthesis (Foundry IQ).
- ✅ **Web UI** — built: a streaming Blazor Server chat in [`web/`](./web) (see above).
- **Evaluation & observability** — groundedness/relevance metrics, OpenTelemetry tracing.

## License

MIT — see [`LICENSE`](../LICENSE).
