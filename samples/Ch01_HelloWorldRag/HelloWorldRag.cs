using Microsoft.Extensions.AI;

namespace RagInDotNet.Samples.Ch01_HelloWorldRag;

/// <summary>
/// The minimal RAG primitives Chapter 1 walks the reader through. Lifted
/// out of <c>Program.cs</c> so the unit test in <c>SmartDocs.UnitTests</c>
/// can exercise the full embed → search → ask flow with stubbed
/// <see cref="IChatClient"/> / <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>
/// implementations.
/// </summary>
internal static class HelloWorldRag
{
    /// <summary>
    /// The five HR policy snippets the chapter starts from. Hand-picked so
    /// the answer to "How many vacation days do I get?" is unambiguously
    /// the first entry, regardless of the embedding model used.
    /// </summary>
    public static readonly IReadOnlyList<string> HardcodedHrPolicies =
    [
        "Employees at the Montreal office receive 20 paid vacation days per fiscal year, accrued monthly.",
        "Sick leave is unlimited for employees in good standing; please notify your manager within 24 hours.",
        "Remote work is allowed up to 3 days per week with prior manager approval.",
        "Annual performance reviews occur in March; salary adjustments take effect on May 1.",
        "Parental leave provides 18 weeks of fully paid time off, available to all primary and secondary caregivers.",
    ];

    /// <summary>
    /// Build an in-memory index, retrieve the top-K most similar chunks to
    /// the question, and ask the chat model to answer using only those
    /// chunks. Mirrors the brute-force vector store + cosine similarity
    /// from §1 of the chapter.
    /// </summary>
    public static async Task<string> AskAsync(
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        IChatClient chat,
        IReadOnlyList<string> documents,
        string question,
        int topK = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        // 1. Embed the corpus.
        var docEmbeddings = await embeddings.GenerateAsync(documents, cancellationToken: cancellationToken);
        var index = new List<(string Text, ReadOnlyMemory<float> Vector)>(documents.Count);
        for (int i = 0; i < documents.Count; i++)
        {
            index.Add((documents[i], docEmbeddings[i].Vector));
        }

        // 2. Embed the question.
        var qEmbedding = (await embeddings.GenerateAsync([question], cancellationToken: cancellationToken))[0].Vector;

        // 3. Retrieve top-K by cosine similarity (brute force).
        var ranked = index
            .Select(entry => (entry.Text, Score: Cosine(qEmbedding.Span, entry.Vector.Span)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        // 4. Augment & generate.
        var contextBlock = string.Join(
            Environment.NewLine,
            ranked.Select((r, i) => $"[Source {i + 1}] {r.Text}"));

        var prompt =
            $"""
            You are a helpful assistant. Answer the question using ONLY the context below.
            If the answer is not in the context, say "I don't know".

            Context:
            {contextBlock}

            Question: {question}
            """;

        var response = await chat.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// Cosine similarity over two vectors. Throws if dimensions differ.
    /// Accumulates in <see cref="double"/> to stay safe against float overflow
    /// on un-normalised, high-dimensional inputs; modern embeddings are
    /// normalised so the precision delta is invisible in practice.
    /// </summary>
    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"Vector length mismatch: {a.Length} vs {b.Length}", nameof(b));
        }

        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            magA += (double)a[i] * a[i];
            magB += (double)b[i] * b[i];
        }

        if (magA == 0 || magB == 0)
        {
            return 0f;
        }

        return (float)(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)));
    }
}
