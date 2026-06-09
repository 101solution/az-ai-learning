using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;

namespace RagFoundryDemo.Rag;

/// <summary>
/// Builds the Azure SDK clients. Auth is keyless by default (DefaultAzureCredential —
/// uses `az login`, managed identity, etc.) and falls back to an API key only if one
/// is supplied in config. This is the recommended production posture, with an escape
/// hatch so the demo always runs.
/// </summary>
public sealed class AzureClients
{
    private readonly RagConfig _cfg;
    private readonly DefaultAzureCredential _credential = new();

    public AzureOpenAIClient OpenAI { get; }
    public SearchIndexClient SearchIndex { get; }

    public AzureClients(RagConfig cfg)
    {
        _cfg = cfg;

        OpenAI = cfg.UsesOpenAIKey
            ? new AzureOpenAIClient(new Uri(cfg.OpenAIEndpoint), new AzureKeyCredential(cfg.OpenAIApiKey!))
            : new AzureOpenAIClient(new Uri(cfg.OpenAIEndpoint), _credential);

        SearchIndex = cfg.UsesSearchKey
            ? new SearchIndexClient(new Uri(cfg.SearchEndpoint), new AzureKeyCredential(cfg.SearchApiKey!))
            : new SearchIndexClient(new Uri(cfg.SearchEndpoint), _credential);
    }

    /// <summary>A query client bound to the configured index.</summary>
    public SearchClient GetSearchClient() =>
        _cfg.UsesSearchKey
            ? new SearchClient(new Uri(_cfg.SearchEndpoint), _cfg.IndexName, new AzureKeyCredential(_cfg.SearchApiKey!))
            : new SearchClient(new Uri(_cfg.SearchEndpoint), _cfg.IndexName, _credential);
}
