# Live "teach it something new" demo

A high-impact beat: ask something the knowledge base **doesn't** cover, get an honest
"I don't know", then **add a document, re-ingest, and ask again** — and watch the answer
appear. It shows the core RAG value: *update the docs, update the answers — no retraining.*

The topic here is **heat stress** — deliberately left out of the indexed `data/` folder.
The ready-made doc is staged at `demo/heat-stress-management.md` (not indexed yet).

## The script

**1. Ask (before) — in the web app or `dotnet run -- chat`:**
> What should I do to manage heat stress on site?

→ Expected: **"I don't know — that isn't in the knowledge base."**

**2. Add the document** (drop the staged file into the indexed folder):
```powershell
Copy-Item demo\heat-stress-management.md data\
```
*(Or just edit/create a file in `data\` live, to make the point even more vivid.)*

**3. Re-ingest** — no rebuild needed (DocumentChunker reads the source `data\` folder):
```powershell
dotnet run --no-build -- ingest
```
→ "Indexed N chunks" — a few seconds.

**4. Ask again — same question:**
> What should I do to manage heat stress on site?

→ Now a grounded answer (hydration, acclimatisation, work/rest cycles, heat-illness first aid),
cited to **[heat-stress-management.md]**.

## Talking points
- Nothing was retrained or fine-tuned — the model is unchanged. We just gave it new **retrievable
  knowledge**.
- The same applies to *correcting* or *updating* a policy: edit the doc, re-ingest, done.
- Both the local app and the deployed Azure app pick it up immediately — they query the same
  shared Azure AI Search index.

## Reset after the demo (to restore the clean "before" state / run it again)
```powershell
Remove-Item data\heat-stress-management.md
dotnet run --no-build -- reset      # delete the index — removes the heat-stress chunks
dotnet run --no-build -- ingest     # rebuild from data\ (heat-stress now gone)
```
> Note: a plain `ingest` only *adds/updates* chunks — it does **not** delete chunks for a
> removed doc. To fully remove heat-stress, use `reset` then `ingest` as above.
