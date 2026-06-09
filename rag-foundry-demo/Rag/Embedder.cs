using System.ClientModel;
using OpenAI.Embeddings;

namespace RagFoundryDemo.Rag;

/// <summary>
/// Turns text into vectors using the Foundry-deployed embedding model.
/// The same model + dimensions must be used for indexing and querying, or vector
/// similarity is meaningless.
/// </summary>
public sealed class Embedder
{
    private readonly EmbeddingClient _client;
    private readonly EmbeddingGenerationOptions _options;

    public Embedder(AzureClients clients, RagConfig cfg)
    {
        _client = clients.OpenAI.GetEmbeddingClient(cfg.EmbeddingDeployment);
        _options = new EmbeddingGenerationOptions { Dimensions = cfg.EmbeddingDimensions };
    }

    /// <summary>Embed a single string (used for the user's question at query time).</summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var result = await GenerateWithRetryAsync([text], ct);
        return result[0].ToFloats().ToArray();
    }

    /// <summary>Embed many chunks, batched to stay within per-request limits.</summary>
    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        const int batchSize = 16;
        var vectors = new List<float[]>(texts.Count);
        for (var i = 0; i < texts.Count; i += batchSize)
        {
            var slice = texts.Skip(i).Take(batchSize).ToList();
            var result = await GenerateWithRetryAsync(slice, ct);
            vectors.AddRange(result.Select(e => e.ToFloats().ToArray()));
        }
        return vectors;
    }

    // Embedding endpoints throttle (HTTP 429). Back off and retry a few times.
    private async Task<OpenAIEmbeddingCollection> GenerateWithRetryAsync(IList<string> inputs, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _client.GenerateEmbeddingsAsync(inputs, _options, ct);
            }
            catch (ClientResultException ex) when (ex.Status == 429 && attempt <= 5)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                Console.WriteLine($"  rate-limited, retrying in {delay.TotalSeconds:0}s…");
                await Task.Delay(delay, ct);
            }
        }
    }
}
