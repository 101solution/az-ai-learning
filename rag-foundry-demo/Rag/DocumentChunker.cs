using System.Text;

namespace RagFoundryDemo.Rag;

/// <summary>A slice of a source document, ready to embed and index.</summary>
public sealed record DocChunk(string Source, string Title, string Text, int Ordinal);

/// <summary>
/// Loads data/*.md and splits each file into overlapping, paragraph-aware chunks.
/// Chunking is the single biggest lever on RAG quality: too big wastes tokens and
/// dilutes relevance; too small loses context. Overlap preserves meaning across cuts.
/// </summary>
public static class DocumentChunker
{
    public static IReadOnlyList<DocChunk> LoadAndChunk(string dataPath, int chunkSize, int overlap)
    {
        var dir = ResolveDir(dataPath);
        var files = Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                             .ToList();
        if (files.Count == 0)
            throw new InvalidOperationException($"No .md files found in '{dir}'.");

        var result = new List<DocChunk>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var text = File.ReadAllText(file);
            var title = ExtractTitle(text, name);
            var ordinal = 0;
            foreach (var chunk in SplitParagraphs(text, chunkSize, overlap))
                result.Add(new DocChunk(name, title, chunk, ordinal++));
        }
        return result;
    }

    private static string ResolveDir(string dataPath)
    {
        if (Path.IsPathRooted(dataPath) && Directory.Exists(dataPath)) return dataPath;
        foreach (var baseDir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var p = Path.Combine(baseDir, dataPath);
            if (Directory.Exists(p)) return p;
        }
        throw new DirectoryNotFoundException(
            $"Data folder '{dataPath}' not found (looked next to the binary and in the current directory).");
    }

    private static string ExtractTitle(string text, string fallback)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("# ")) return t[2..].Trim();
        }
        return fallback;
    }

    private static IEnumerable<string> SplitParagraphs(string text, int chunkSize, int overlap)
    {
        var paragraphs = text.Replace("\r\n", "\n")
                             .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                             .Select(p => p.Trim())
                             .Where(p => p.Length > 0)
                             .ToList();

        var buffer = new StringBuilder();
        foreach (var para in paragraphs)
        {
            // Flush when adding this paragraph would overflow the window.
            if (buffer.Length > 0 && buffer.Length + para.Length + 2 > chunkSize)
            {
                var chunk = buffer.ToString().Trim();
                yield return chunk;

                // Seed the next chunk with a tail overlap so context isn't lost at the seam.
                var tail = chunk.Length > overlap ? chunk[^overlap..] : chunk;
                buffer.Clear();
                buffer.Append(tail).Append("\n\n");
            }
            buffer.Append(para).Append("\n\n");
        }

        var last = buffer.ToString().Trim();
        if (last.Length > 0) yield return last;
    }
}
