using System.Text;
using SmartDocs.Core.Documents;
using SmartDocs.Core.Tokens;

namespace SmartDocs.Generation;

/// <summary>
/// Builds the LLM prompt by injecting retrieved context into a fixed
/// template. Honors a <see cref="ContextBudgetTokens"/> limit by greedily
/// adding chunks in retrieval-rank order until adding the next chunk
/// would exceed the budget. Each chunk is injected with a numbered
/// <c>[Source N]</c> marker so the citation pipeline (Ch 24) can map a
/// claim back to the right source.
/// </summary>
public sealed class PromptTemplateEngine
{
    private readonly ITokenCounter _tokens;

    public int ContextBudgetTokens { get; }

    public PromptTemplateEngine(ITokenCounter tokens, int contextBudgetTokens = 4000)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contextBudgetTokens);
        _tokens = tokens;
        ContextBudgetTokens = contextBudgetTokens;
    }

    /// <summary>Build the prompt + the chunks that actually fit (in display order).</summary>
    public (string Prompt, IReadOnlyList<RetrievalResult> UsedSources) Build(
        string question,
        IReadOnlyList<RetrievalResult> retrieved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(retrieved);

        if (retrieved.Count == 0)
        {
            return (BuildNoContextPrompt(question), Array.Empty<RetrievalResult>());
        }

        var fixedPromptTokens = _tokens.CountTokens(BuildShellPrompt(question, contextPlaceholder: ""));
        var remaining = ContextBudgetTokens - fixedPromptTokens;
        if (remaining <= 0)
        {
            return (BuildNoContextPrompt(question), Array.Empty<RetrievalResult>());
        }

        var used = new List<RetrievalResult>();
        var contextBuilder = new StringBuilder();
        for (int i = 0; i < retrieved.Count; i++)
        {
            var line = $"[Source {i + 1}] {retrieved[i].Chunk.Text}\n";
            var lineTokens = _tokens.CountTokens(line);
            if (lineTokens > remaining)
            {
                break;
            }
            contextBuilder.Append(line);
            remaining -= lineTokens;
            used.Add(retrieved[i]);
        }

        return (BuildShellPrompt(question, contextBuilder.ToString()), used);
    }

    /// <summary>
    /// Assemble the prompt shell. The ordering here is deliberate and is the
    /// .NET design rule for provider prompt caching (Ch 21, "Layer 0"): the
    /// <em>stable</em> system/instruction prefix comes FIRST and the
    /// <em>variable</em> retrieved context and user question come LAST. Azure
    /// OpenAI / OpenAI cache the longest identical token prefix of a request
    /// (≥1024 tokens, in 128-token steps), so keeping the durable instruction
    /// block at the front lets every request reuse that cached prefix and only
    /// pay full price for the tail that actually changes. Interpolating the
    /// question early would poison the prefix and defeat the cache.
    /// </summary>
    private static string BuildShellPrompt(string question, string contextPlaceholder) =>
        $"""
        You are a helpful assistant for the Contoso Intelligent Systems knowledge base.
        Answer the question using ONLY the context below. If the answer is not in the
        context, reply exactly: "I don't know based on the available sources."
        When you reference a fact, cite the source like [Source N].

        Context:
        {contextPlaceholder}
        Question: {question}
        """;

    private static string BuildNoContextPrompt(string question) =>
        $"""
        Reply exactly: "I don't know based on the available sources."

        (No retrieved context was available for the question: {question})
        """;
}
