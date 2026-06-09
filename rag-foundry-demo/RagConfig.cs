using Microsoft.Extensions.Configuration;

namespace RagFoundryDemo;

/// <summary>
/// All knobs for the demo. Bound from appsettings.json, then appsettings.local.json
/// (git-ignored, holds your real endpoints), then RAG_* environment variables.
/// Later sources win, so env vars override files.
/// </summary>
public sealed class RagConfig
{
    // --- Azure AI Foundry model deployments (called via the Azure OpenAI endpoint) ---
    public string OpenAIEndpoint { get; set; } = "";        // https://<resource>.openai.azure.com/
    public string? OpenAIApiKey { get; set; }               // optional: empty => keyless (DefaultAzureCredential)
    public string ChatDeployment { get; set; } = "gpt-4.1";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;    // 1536 for -small, 3072 for -large

    // --- Azure AI Search ---
    public string SearchEndpoint { get; set; } = "";        // https://<service>.search.windows.net
    public string? SearchApiKey { get; set; }               // optional: empty => keyless (DefaultAzureCredential)
    public string IndexName { get; set; } = "mining-knowledge";

    // --- Retrieval ---
    public bool UseSemanticRanker { get; set; } = true;     // needs Search Basic tier or higher
    public int TopK { get; set; } = 5;

    // --- Chunking (character-based; simple and predictable for a demo) ---
    public int ChunkSize { get; set; } = 900;
    public int ChunkOverlap { get; set; } = 150;
    public string DataPath { get; set; } = "data";

    public bool UsesOpenAIKey => !string.IsNullOrWhiteSpace(OpenAIApiKey);
    public bool UsesSearchKey => !string.IsNullOrWhiteSpace(SearchApiKey);

    public static RagConfig Load()
    {
        var root = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.local.json", optional: true)
            .AddEnvironmentVariables(prefix: "RAG_")
            .Build();

        var cfg = new RagConfig();
        root.Bind(cfg);
        return cfg;
    }

    /// <summary>Throws with a clear message if required endpoints are missing.</summary>
    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(OpenAIEndpoint) || OpenAIEndpoint.Contains('<')) missing.Add(nameof(OpenAIEndpoint));
        if (string.IsNullOrWhiteSpace(SearchEndpoint) || SearchEndpoint.Contains('<')) missing.Add(nameof(SearchEndpoint));
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing config: {string.Join(", ", missing)}. " +
                "Copy appsettings.json to appsettings.local.json and fill in your endpoints " +
                "(the provision.ps1 script prints them), or set RAG_* environment variables.");
        }
    }
}
