using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;

namespace RagFoundryDemo.Rag;

/// <summary>
/// Owns the Azure AI Search index: its schema (text + vector + semantic) and document
/// uploads. One index stores both the human-readable chunk and its embedding — that
/// dual storage is what makes hybrid (keyword + vector) search possible.
/// </summary>
public sealed class SearchIndexManager
{
    public const string VectorProfile = "vector-profile";
    public const string HnswConfig = "hnsw";
    public const string SemanticConfig = "semantic-config";

    private readonly SearchIndexClient _indexClient;
    private readonly SearchClient _searchClient;
    private readonly RagConfig _cfg;

    public SearchIndexManager(AzureClients clients, RagConfig cfg)
    {
        _indexClient = clients.SearchIndex;
        _searchClient = clients.GetSearchClient();
        _cfg = cfg;
    }

    public async Task CreateOrUpdateIndexAsync(CancellationToken ct = default)
    {
        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
            new SearchableField("title")  { IsFilterable = true },
            new SearchableField("source") { IsFilterable = true },   // shown in citations
            new SearchableField("chunk"),                            // keyword search + LLM context
            new VectorSearchField("contentVector", _cfg.EmbeddingDimensions, VectorProfile)
        };

        var vectorSearch = new VectorSearch();
        vectorSearch.Algorithms.Add(new HnswAlgorithmConfiguration(HnswConfig));
        vectorSearch.Profiles.Add(new VectorSearchProfile(VectorProfile, HnswConfig));

        var index = new SearchIndex(_cfg.IndexName)
        {
            Fields = fields,
            VectorSearch = vectorSearch
        };

        if (_cfg.UseSemanticRanker)
        {
            var semantic = new SemanticSearch();
            semantic.Configurations.Add(new SemanticConfiguration(
                SemanticConfig,
                new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("title"),
                    ContentFields = { new SemanticField("chunk") }
                }));
            index.SemanticSearch = semantic;
        }

        await _indexClient.CreateOrUpdateIndexAsync(index, cancellationToken: ct);
    }

    public async Task UploadAsync(IReadOnlyList<KnowledgeChunk> chunks, CancellationToken ct = default)
    {
        const int batch = 100; // Azure AI Search accepts up to 1000 docs per batch
        for (var i = 0; i < chunks.Count; i += batch)
        {
            var slice = chunks.Skip(i).Take(batch).ToList();
            await _searchClient.MergeOrUploadDocumentsAsync(slice, cancellationToken: ct);
        }
    }

    public async Task<bool> DeleteIndexIfExistsAsync(CancellationToken ct = default)
    {
        try
        {
            await _indexClient.DeleteIndexAsync(_cfg.IndexName, ct);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }
}
