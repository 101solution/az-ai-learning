using System.Text.Json.Serialization;

namespace RagFoundryDemo;

/// <summary>
/// One indexed unit of knowledge: a slice of a source document plus its embedding.
/// The JSON names map 1:1 to the Azure AI Search index fields.
/// </summary>
public sealed class KnowledgeChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";   // file name — shown in citations

    [JsonPropertyName("chunk")]
    public string Chunk { get; set; } = "";     // human-readable text (keyword search + LLM context)

    [JsonPropertyName("contentVector")]
    public float[]? ContentVector { get; set; } // embedding (vector search)
}
