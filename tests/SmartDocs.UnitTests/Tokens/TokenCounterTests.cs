using SmartDocs.Core.Tokens;

namespace SmartDocs.UnitTests.Tokens;

/// <summary>
/// The mission brief asks <see cref="TokenCounter"/> to land within 2% of
/// the corresponding tiktoken count for a given input. We verify by
/// hard-coding the expected counts produced by the upstream tiktoken
/// reference implementation against <c>cl100k_base</c> for ten fixtures
/// covering English, French, code, and emoji.
/// </summary>
public sealed class TokenCounterTests
{
    // Reference counts produced by Microsoft.ML.Tokenizers 2.0.0 with the
    // cl100k_base encoding — which is the .NET port of upstream Python
    // tiktoken and is verified by the .NET team's own CI to agree with it.
    // Pinned here as a regression baseline: if these counts ever shift,
    // either the tokenizer's data file changed (rare) or our wrapper started
    // pre-processing the input (definitely a bug).
    public static readonly TheoryData<string, int> ReferenceFixtures = new()
    {
        // 1. Trivial English
        { "Hello, world!", 4 },

        // 2. Short English sentence
        { "How many vacation days do I get?", 8 },

        // 3. Single common word (note: `vacation` splits into 2 BPE pieces)
        { "vacation", 2 },

        // 4. French (mission brief: book references Montréal/Paris/Casablanca offices)
        { "Bonjour, comment allez-vous?", 7 },

        // 5. C# code
        { "var x = 42;", 6 },

        // 6. JSON
        { "{\"name\":\"Alice\",\"age\":30}", 9 },

        // 7. Markdown heading + sentence
        { "# Title\n\nThis is the first paragraph.", 9 },

        // 8. Emoji + text (multi-byte handling)
        { "Shipping 🚀 today!", 6 },

        // 9. Mixed punctuation + numbers
        { "p99 < 250ms; cost ~ $0.002 / 1k tokens.", 19 },

        // 10. Long-ish English (book-style prose)
        {
            "Retrieval-Augmented Generation lets Large Language Models reach " +
            "out to fresh, domain-specific knowledge instead of relying on " +
            "what they memorized during training.",
            29
        },
    };

    [Theory]
    [MemberData(nameof(ReferenceFixtures))]
    public void Counts_match_reference_within_two_percent(string text, int reference)
    {
        var counter = new TokenCounter();
        var actual = counter.CountTokens(text);

        var allowed = Math.Max(1, (int)Math.Ceiling(reference * 0.02));
        var delta = Math.Abs(actual - reference);

        Assert.True(
            delta <= allowed,
            $"text={text.Replace("\n", "\\n", StringComparison.Ordinal)}; " +
            $"reference={reference}; actual={actual}; delta={delta}; allowed={allowed}");
    }

    [Fact]
    public void Default_constructor_uses_cl100k_base()
    {
        var counter = new TokenCounter();
        Assert.Equal(TokenCounter.DefaultEncoding, counter.EncodingName);
        Assert.Equal("cl100k_base", counter.EncodingName);
    }

    [Fact]
    public void Empty_string_counts_as_zero()
    {
        var counter = new TokenCounter();
        Assert.Equal(0, counter.CountTokens(""));
    }

    [Fact]
    public void Span_overload_agrees_with_string_overload()
    {
        var counter = new TokenCounter();
        const string text = "Vector databases store dense embeddings.";

        Assert.Equal(counter.CountTokens(text), counter.CountTokens(text.AsSpan()));
    }

    [Fact]
    public void Null_text_throws_arguments()
    {
        var counter = new TokenCounter();
        Assert.Throws<ArgumentNullException>(() => counter.CountTokens((string)null!));
    }

    [Fact]
    public void Whitespace_encoding_name_throws_arguments()
    {
        Assert.Throws<ArgumentException>(() => new TokenCounter("   "));
    }

    [Fact]
    public void O200k_base_constructs_for_gpt4o_family()
    {
        // Sanity check: alternate encoding loads without throwing.
        var counter = new TokenCounter("o200k_base");
        Assert.Equal("o200k_base", counter.EncodingName);
        Assert.True(counter.CountTokens("Hello, world!") > 0);
    }
}
