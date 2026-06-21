namespace RagInDotNet.Samples.Ch16_VectorlessRetrieval;

/// <summary>The kind of query, used to bucket recall in the comparison table.</summary>
internal enum QueryKind
{
    /// <summary>Names a formal identifier (e.g. "Article 17").</summary>
    Identifier,

    /// <summary>A topic / paraphrase query that shares vocabulary with the corpus.</summary>
    Topic,

    /// <summary>An identifier mixed with a topic.</summary>
    Both,
}

/// <summary>A single pre-tagged evaluation query and its gold node id.</summary>
/// <param name="Query">The natural-language query.</param>
/// <param name="Kind">Whether it names an identifier, a topic, or both.</param>
/// <param name="GoldId">The structural node id (and vector chunk id) that answers it.</param>
internal sealed record EvalQuery(string Query, QueryKind Kind, string GoldId);

/// <summary>
/// A small, hand-shaped GDPR-like corpus and the eval set that exercises it.
/// The articles cross-reference each other (e.g. Article 17 cites Article 19),
/// which is the case structural retrieval handles exactly and vector retrieval
/// cannot follow.
/// </summary>
internal static class Corpus
{
    public const string Title = "GDPR";

    // Node ids are formal: "Article 17" -> "Art17" (DocumentStructureParser.DefaultIdFor).
    public const string Markdown = """
        # Article 16
        Right to rectification. The data subject has the right to obtain rectification
        of inaccurate personal data concerning them without undue delay.

        # Article 17
        Right to erasure (right to be forgotten). The data subject has the right to
        obtain erasure of personal data without undue delay. Where the controller has
        made the data public, Article 19 applies regarding notification to recipients.

        # Article 18
        Right to restriction of processing. The data subject may obtain restriction
        of processing where accuracy is contested, as set out in Article 16.

        # Article 19
        Notification obligation regarding rectification or erasure. The controller
        shall communicate any erasure under Article 17 to each recipient, unless this
        proves impossible or involves disproportionate effort.

        # Article 20
        Right to data portability. The data subject has the right to receive personal
        data in a structured, commonly used, machine-readable format and to transmit
        it to another controller.

        # Article 21
        Right to object. The data subject has the right to object to processing of
        personal data, including profiling. See Article 17 for subsequent erasure.

        # Article 33
        Notification of a personal data breach to the supervisory authority. In the
        case of a breach, the controller shall notify the supervisory authority within
        72 hours of becoming aware of it.

        # Article 34
        Communication of a personal data breach to the data subject. When a breach is
        likely to result in a high risk, the controller shall communicate it to the
        data subject, following the notification under Article 33.
        """;

    /// <summary>
    /// The pre-tagged eval set: identifier, topic, and mixed queries. The gold id
    /// is the node a perfect retriever returns in its top-K.
    /// </summary>
    public static IReadOnlyList<EvalQuery> EvalSet { get; } =
    [
        // --- Identifier queries: name a formal article. Structural wins. -------
        new("What does Article 17 say?", QueryKind.Identifier, "Art17"),
        new("Show me Article 19", QueryKind.Identifier, "Art19"),
        new("Article 33 obligations", QueryKind.Identifier, "Art33"),
        new("Retrieve Article 20", QueryKind.Identifier, "Art20"),
        new("Article 16 text", QueryKind.Identifier, "Art16"),
        new("Give me Article 34", QueryKind.Identifier, "Art34"),
        new("What is in Article 18?", QueryKind.Identifier, "Art18"),
        new("Article 21 please", QueryKind.Identifier, "Art21"),

        // --- Topic queries: share vocabulary, name no identifier. Vector helps. -
        new("right to be forgotten and erasure of personal data", QueryKind.Topic, "Art17"),
        new("portability of data in a machine-readable format", QueryKind.Topic, "Art20"),
        new("notify the supervisory authority within 72 hours of a breach", QueryKind.Topic, "Art33"),
        new("correct inaccurate personal data", QueryKind.Topic, "Art16"),
        new("object to profiling", QueryKind.Topic, "Art21"),
        new("communicate a high-risk breach to the data subject", QueryKind.Topic, "Art34"),

        // --- Mixed queries: identifier plus surrounding topic. Fused leg helps. -
        new("how does Article 17 erasure relate to the notification obligation", QueryKind.Both, "Art17"),
        new("Article 33 breach notification within seventy two hours to authority", QueryKind.Both, "Art33"),
    ];
}
