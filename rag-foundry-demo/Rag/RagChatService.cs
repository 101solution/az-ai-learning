using System.Runtime.CompilerServices;
using System.Text;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using OpenAI.Chat;

namespace RagFoundryDemo.Rag;

/// <summary>
/// The RAG pipeline for a single question: Retrieve → Augment → Generate.
/// Exposes both a one-shot <see cref="AskAsync"/> (used by the console) and a
/// token-streaming <see cref="AskStreamingAsync"/> (used by the web UI). Both share
/// the same retrieval + prompt assembly via <see cref="PrepareAsync"/>.
/// </summary>
public sealed class RagChatService
{
    // The guardrail. This is where grounding is *enforced*: answer only from the
    // supplied sources, cite them, and admit ignorance rather than inventing.
    private const string SystemPrompt = """
        You are a mining operations assistant. Answer the user's question using ONLY the
        numbered SOURCES provided below. Cite the sources you use inline as [1], [2], etc.
        If the answer is not contained in the sources, reply exactly with:
        "I don't know — that isn't in the knowledge base."
        Be concise and practical. Never invent policies, figures, or procedures.
        """;

    private const string NoAnswer = "I don't know — that isn't in the knowledge base.";

    private readonly SearchClient _search;
    private readonly ChatClient _chat;
    private readonly Embedder _embedder;
    private readonly RagConfig _cfg;

    public RagChatService(AzureClients clients, RagConfig cfg, Embedder embedder)
    {
        _search = clients.GetSearchClient();
        _chat = clients.OpenAI.GetChatClient(cfg.ChatDeployment);
        _embedder = embedder;
        _cfg = cfg;
    }

    /// <summary>One-shot grounded answer (non-streaming).</summary>
    public async Task<RagAnswer> AskAsync(
        string question, IReadOnlyList<ConversationTurn> history, CancellationToken ct = default)
    {
        var (sources, rewritten, messages) = await PrepareAsync(question, history, ct);
        if (messages is null)
            return new RagAnswer(NoAnswer, sources, rewritten);

        var completion = await _chat.CompleteChatAsync(messages, cancellationToken: ct);
        return new RagAnswer(completion.Value.Content[0].Text, sources, rewritten);
    }

    /// <summary>
    /// Grounded answer with the generation streamed token-by-token. Retrieval runs first
    /// (so <see cref="StreamingRagAnswer.Sources"/> is ready immediately); the answer text
    /// is then yielded incrementally as the model produces it.
    /// </summary>
    public async Task<StreamingRagAnswer> AskStreamingAsync(
        string question, IReadOnlyList<ConversationTurn> history, CancellationToken ct = default)
    {
        var (sources, rewritten, messages) = await PrepareAsync(question, history, ct);
        IAsyncEnumerable<string> tokens = messages is null
            ? SingleAsync(NoAnswer)
            : StreamTokensAsync(messages, ct);
        return new StreamingRagAnswer(sources, rewritten, tokens);
    }

    // Condense → retrieve → assemble messages. Returns messages == null when nothing relevant
    // was found, signalling the caller to emit the "I don't know" response.
    private async Task<(IReadOnlyList<KnowledgeChunk> Sources, string? Rewritten, List<ChatMessage>? Messages)>
        PrepareAsync(string question, IReadOnlyList<ConversationTurn> history, CancellationToken ct)
    {
        // 0. CONDENSE — fold conversation context into a standalone query.
        var searchQuery = await CondenseAsync(question, history, ct);

        // 1. RETRIEVE — embed the (standalone) query and run a hybrid (+ semantic) search.
        var queryVector = await _embedder.EmbedAsync(searchQuery, ct);

        var options = new SearchOptions
        {
            Size = _cfg.TopK,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryVector)
                    {
                        KNearestNeighborsCount = _cfg.TopK,
                        Fields = { "contentVector" }
                    }
                }
            }
        };
        options.Select.Add("id");
        options.Select.Add("title");
        options.Select.Add("source");
        options.Select.Add("chunk");

        if (_cfg.UseSemanticRanker)
        {
            options.QueryType = SearchQueryType.Semantic;
            options.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SearchIndexManager.SemanticConfig
            };
        }

        // Passing BOTH the question text and the vector = hybrid search.
        var response = await _search.SearchAsync<KnowledgeChunk>(searchQuery, options, ct);

        var sources = new List<KnowledgeChunk>();
        await foreach (var result in response.Value.GetResultsAsync())
            sources.Add(result.Document);

        var rewritten = searchQuery != question ? searchQuery : null;
        if (sources.Count == 0)
            return (sources, rewritten, null);

        // 2. AUGMENT — assemble a numbered, citable context block.
        var context = new StringBuilder();
        for (var i = 0; i < sources.Count; i++)
            context.AppendLine($"[{i + 1}] ({sources[i].Source}) {sources[i].Chunk}").AppendLine();

        // 3. Build messages: system rules + prior conversation + this turn's sources & question.
        var messages = new List<ChatMessage> { new SystemChatMessage(SystemPrompt) };
        foreach (var turn in history)
            messages.Add(turn.IsUser
                ? new UserChatMessage(turn.Text)
                : new AssistantChatMessage(turn.Text));
        messages.Add(new UserChatMessage($"SOURCES:\n{context}\nQUESTION: {question}"));

        return (sources, rewritten, messages);
    }

    private async IAsyncEnumerable<string> StreamTokensAsync(
        List<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var update in _chat.CompleteChatStreamingAsync(messages, cancellationToken: ct))
            foreach (var part in update.ContentUpdate)
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
    }

    private static async IAsyncEnumerable<string> SingleAsync(string text)
    {
        yield return text;
        await Task.CompletedTask;
    }

    // Uses the chat model to rewrite a follow-up into a standalone question, given history.
    private async Task<string> CondenseAsync(
        string question, IReadOnlyList<ConversationTurn> history, CancellationToken ct)
    {
        if (history.Count == 0) return question;

        var transcript = string.Join("\n",
            history.Select(t => $"{(t.IsUser ? "User" : "Assistant")}: {t.Text}"));

        var prompt = $"""
            Given the conversation so far and a follow-up message, rewrite the follow-up as a
            standalone question understandable without the conversation. Keep it concise and
            preserve intent. If it is already standalone, return it unchanged.
            Respond with ONLY the rewritten question.

            Conversation:
            {transcript}

            Follow-up: {question}
            Standalone question:
            """;

        var resp = await _chat.CompleteChatAsync([new UserChatMessage(prompt)], cancellationToken: ct);
        var rewritten = resp.Value.Content[0].Text.Trim();
        return string.IsNullOrWhiteSpace(rewritten) ? question : rewritten;
    }
}

/// <summary>One message in the running conversation (for query rewriting + generation).</summary>
public sealed record ConversationTurn(bool IsUser, string Text);

public sealed record RagAnswer(string Text, IReadOnlyList<KnowledgeChunk> Sources, string? RewrittenQuery = null);

/// <summary>Sources are known up front; <see cref="Tokens"/> yields the answer as it streams.</summary>
public sealed record StreamingRagAnswer(
    IReadOnlyList<KnowledgeChunk> Sources, string? RewrittenQuery, IAsyncEnumerable<string> Tokens);
