using RagFoundryDemo;
using RagFoundryDemo.Rag;

// Entry point + command router. Commands are added incrementally:
//   config  → show resolved configuration (Step 1)
//   chunk   → load + chunk data/*.md, no Azure needed   (Step 2)
//   ingest  → build the search index from data/*.md     (Step 3)
//   chat    → grounded Q&A loop                          (Step 4)
//   reset   → delete the index                           (Step 5)

var cfg = RagConfig.Load();
var command = (args.Length > 0 ? args[0] : "help").ToLowerInvariant();

try
{
    switch (command)
    {
        case "config":
            PrintConfig(cfg);
            break;

        case "chunk":
            ShowChunks(cfg);
            break;

        case "ingest":
            await RunIngestAsync(cfg);
            break;

        case "chat":
            await RunChatAsync(cfg);
            break;

        case "reset":
            await RunResetAsync(cfg);
            break;

        default:
            Console.WriteLine("""
                RAG on Azure AI Foundry — demo

                Usage: dotnet run -- <command>
                  config   Show the resolved configuration and auth mode
                  chunk    Load and chunk data/*.md (offline, no Azure)
                  ingest   Chunk, embed, and index data/*.md into Azure AI Search
                  chat     Ask grounded questions with citations
                  reset    Delete the search index
                """);
            break;
    }
}
catch (Exception ex)
{
    // Clean, demo-friendly errors instead of raw stack traces.
    Console.Error.WriteLine($"\nError: {ex.Message}");
    if (ex.InnerException is not null)
        Console.Error.WriteLine($"  ↳ {ex.InnerException.Message}");
    Environment.ExitCode = 1;
}

static void PrintConfig(RagConfig c)
{
    Console.WriteLine("Resolved configuration");
    Console.WriteLine("----------------------");
    Console.WriteLine($"  OpenAI endpoint      : {Show(c.OpenAIEndpoint)}");
    Console.WriteLine($"  Chat deployment      : {c.ChatDeployment}");
    Console.WriteLine($"  Embedding deployment : {c.EmbeddingDeployment} ({c.EmbeddingDimensions} dims)");
    Console.WriteLine($"  OpenAI auth          : {(c.UsesOpenAIKey ? "API key" : "keyless (DefaultAzureCredential)")}");
    Console.WriteLine($"  Search endpoint      : {Show(c.SearchEndpoint)}");
    Console.WriteLine($"  Index name           : {c.IndexName}");
    Console.WriteLine($"  Search auth          : {(c.UsesSearchKey ? "API key" : "keyless (DefaultAzureCredential)")}");
    Console.WriteLine($"  Semantic ranker      : {(c.UseSemanticRanker ? "on" : "off")}   TopK: {c.TopK}");
    Console.WriteLine($"  Chunking             : {c.ChunkSize} chars, {c.ChunkOverlap} overlap   Data: {c.DataPath}");

    static string Show(string v) => string.IsNullOrWhiteSpace(v) || v.Contains('<') ? "(not set)" : v;
}

static void ShowChunks(RagConfig c)
{
    var chunks = DocumentChunker.LoadAndChunk(c.DataPath, c.ChunkSize, c.ChunkOverlap);
    var bySource = chunks.GroupBy(x => x.Source).OrderBy(g => g.Key);

    Console.WriteLine($"Loaded {bySource.Count()} document(s) → {chunks.Count} chunk(s)\n");
    foreach (var g in bySource)
        Console.WriteLine($"  {g.Key,-32} {g.Count(),2} chunk(s)   \"{g.First().Title}\"");

    var sample = chunks[0];
    var preview = sample.Text.Length > 220 ? sample.Text[..220] + "…" : sample.Text;
    Console.WriteLine($"\nSample chunk [{sample.Source} #{sample.Ordinal}]:\n{preview}");
}

static async Task RunIngestAsync(RagConfig cfg)
{
    cfg.Validate();
    var clients = new AzureClients(cfg);
    var index = new SearchIndexManager(clients, cfg);
    var embedder = new Embedder(clients, cfg);

    Console.WriteLine("1/4  Loading and chunking documents…");
    var docChunks = DocumentChunker.LoadAndChunk(cfg.DataPath, cfg.ChunkSize, cfg.ChunkOverlap);
    Console.WriteLine($"     {docChunks.Count} chunks from data/*.md");

    Console.WriteLine($"2/4  Creating/updating index '{cfg.IndexName}'…");
    await index.CreateOrUpdateIndexAsync();

    Console.WriteLine($"3/4  Embedding {docChunks.Count} chunks with {cfg.EmbeddingDeployment}…");
    var vectors = await embedder.EmbedBatchAsync(docChunks.Select(c => c.Text).ToList());

    var docs = docChunks.Select((c, i) => new KnowledgeChunk
    {
        Id = $"{Path.GetFileNameWithoutExtension(c.Source)}-{c.Ordinal}",
        Title = c.Title,
        Source = c.Source,
        Chunk = c.Text,
        ContentVector = vectors[i]
    }).ToList();

    Console.WriteLine("4/4  Uploading to Azure AI Search…");
    await index.UploadAsync(docs);
    Console.WriteLine($"\nDone. Indexed {docs.Count} chunks into '{cfg.IndexName}'. Run:  dotnet run -- chat");
}

static async Task RunChatAsync(RagConfig cfg)
{
    cfg.Validate();
    var clients = new AzureClients(cfg);
    var embedder = new Embedder(clients, cfg);
    var rag = new RagChatService(clients, cfg, embedder);

    var history = new List<ConversationTurn>();
    const int maxTurns = 8; // keep the last few exchanges to bound prompt tokens

    Console.WriteLine($"Grounded chat over '{cfg.IndexName}'. Multi-turn — follow-ups use prior context.");
    Console.WriteLine("Type 'clear' to reset the conversation, 'exit' to quit.\n");
    while (true)
    {
        Console.Write("you> ");
        var question = Console.ReadLine();
        if (question is null || question.Trim() is "exit" or "quit") break;
        if (string.IsNullOrWhiteSpace(question)) continue;
        if (question.Trim() is "clear" or "reset")
        {
            history.Clear();
            Console.WriteLine("(conversation cleared)\n");
            continue;
        }

        var answer = await rag.AskAsync(question, history);

        if (answer.RewrittenQuery is not null)
            Console.WriteLine($"  (interpreted as: \"{answer.RewrittenQuery}\")");

        Console.WriteLine($"\nbot> {answer.Text}\n");
        if (answer.Sources.Count > 0)
        {
            Console.WriteLine("sources:");
            var n = 1;
            foreach (var s in answer.Sources)
                Console.WriteLine($"  [{n++}] {s.Source} — {s.Title}");
            Console.WriteLine();
        }

        // Record this exchange and trim to the most recent turns.
        history.Add(new ConversationTurn(true, question));
        history.Add(new ConversationTurn(false, answer.Text));
        if (history.Count > maxTurns)
            history.RemoveRange(0, history.Count - maxTurns);
    }
}

static async Task RunResetAsync(RagConfig cfg)
{
    cfg.Validate();
    var clients = new AzureClients(cfg);
    var index = new SearchIndexManager(clients, cfg);
    var deleted = await index.DeleteIndexIfExistsAsync();
    Console.WriteLine(deleted
        ? $"Deleted index '{cfg.IndexName}'."
        : $"Index '{cfg.IndexName}' did not exist — nothing to delete.");
}
