using Microsoft.Extensions.AI;
using RagInDotNet.Samples.Ch01_HelloWorldRag;

namespace SmartDocs.UnitTests.Samples;

/// <summary>
/// End-to-end smoke test for the Chapter 1 Hello-World RAG sample using
/// hand-rolled stubs for <see cref="IChatClient"/> and
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>. The stubs are
/// deterministic: each document and the question are mapped to a unique
/// one-hot embedding so we can prove the cosine-similarity ranker picks
/// the documented top result without contacting Ollama or Azure OpenAI.
/// </summary>
public sealed class HelloWorldRagTests
{
    [Fact]
    public async Task Vacation_question_retrieves_the_vacation_policy_chunk()
    {
        // Arrange — set up the stubs.
        var docs = HelloWorldRag.HardcodedHrPolicies;
        var question = "How many vacation days do I get?";

        // Make doc 0 (the vacation policy) the most similar to the question.
        // Vector layout: dimension N == documents.Count.
        // Each document i gets vector e_i (one-hot at position i, scaled).
        // The question gets vector e_0 + epsilon noise on every other axis.
        const int dim = 5;
        var embeddings = new StubEmbeddingGenerator(text =>
        {
            var v = new float[dim];
            if (text == question)
            {
                v[0] = 1.0f;
            }
            else
            {
                var idx = -1;
                for (int j = 0; j < docs.Count; j++)
                {
                    if (docs[j] == text) { idx = j; break; }
                }
                if (idx >= 0)
                {
                    v[idx] = 1.0f;
                }
            }
            return v;
        });

        // The chat client just echoes back the source-1 line so we can assert
        // the prompt was correctly assembled with [Source 1] containing the
        // vacation chunk. Real LLM calls would generate prose; for the test
        // we only need to know the right context was selected.
        var chat = new StubChatClient(prompt =>
        {
            // Find the line under "[Source 1] " in the prompt.
            const string marker = "[Source 1] ";
            var i = prompt.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0)
            {
                return "ERROR: no [Source 1] in prompt";
            }
            var start = i + marker.Length;
            var end = prompt.IndexOf('\n', start);
            return end < 0 ? prompt[start..] : prompt[start..end];
        });

        // Act
        var answer = await HelloWorldRag.AskAsync(embeddings, chat, docs, question);

        // Assert — the picked top result is the vacation policy chunk.
        Assert.Contains("20 paid vacation days", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Abstains_without_calling_the_LLM_when_top_score_is_below_threshold()
    {
        // The Chapter 1 Challenge: a no-result threshold. An out-of-corpus
        // question whose best match is below minScore must short-circuit to a
        // grounded "not enough information" WITHOUT spending an LLM call.
        var docs = HelloWorldRag.HardcodedHrPolicies;
        var question = "What is the capital of France?";

        // dim = docs.Count + 1: each document gets one-hot e_i; the out-of-corpus
        // question gets the extra axis e_n, orthogonal to every doc, so cosine == 0.
        var dim = docs.Count + 1;
        var embeddings = new StubEmbeddingGenerator(text =>
        {
            var v = new float[dim];
            var idx = -1;
            for (int j = 0; j < docs.Count; j++)
            {
                if (docs[j] == text) { idx = j; break; }
            }
            v[idx >= 0 ? idx : docs.Count] = 1.0f;
            return v;
        });

        // If the short-circuit fails and the LLM is reached, the test fails loudly.
        var chat = new StubChatClient(_ =>
            throw new InvalidOperationException("LLM was called despite abstaining"));

        var answer = await HelloWorldRag.AskAsync(
            embeddings, chat, docs, question, minScore: 0.3);

        Assert.Equal(
            "I don't have enough information to answer that question.", answer);
    }

    [Fact]
    public void Cosine_returns_one_for_identical_unit_vectors()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [1f, 0f, 0f];

        Assert.Equal(1f, HelloWorldRag.Cosine(a, b), precision: 5);
    }

    [Fact]
    public void Cosine_returns_zero_for_orthogonal_vectors()
    {
        float[] a = [1f, 0f, 0f];
        float[] b = [0f, 1f, 0f];

        Assert.Equal(0f, HelloWorldRag.Cosine(a, b), precision: 5);
    }

    [Fact]
    public void Cosine_returns_zero_when_either_vector_is_all_zero()
    {
        float[] zero = [0f, 0f, 0f];
        float[] unit = [1f, 0f, 0f];

        Assert.Equal(0f, HelloWorldRag.Cosine(zero, unit), precision: 5);
        Assert.Equal(0f, HelloWorldRag.Cosine(unit, zero), precision: 5);
    }

    [Fact]
    public void Cosine_throws_on_dimension_mismatch()
    {
        float[] a = [1f, 0f];
        float[] b = [1f, 0f, 0f];

        Assert.Throws<ArgumentException>(() => HelloWorldRag.Cosine(a, b));
    }
}
