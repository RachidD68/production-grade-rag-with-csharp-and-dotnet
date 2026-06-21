using SmartDocs.Retrieval.Hybrid;

namespace SmartDocs.UnitTests.Retrieval;

/// <summary>
/// Unit tests for the pure, database-free helpers of
/// <see cref="PostgresHybridRetriever"/> — the RRF-in-SQL builder and the
/// pgvector literal formatter. No live PostgreSQL is required (or used).
/// </summary>
public sealed class PostgresHybridSqlTests
{
    [Fact]
    public void BuildHybridSql_uses_rrf_constant_60_in_both_legs()
    {
        var sql = PostgresHybridRetriever.BuildHybridSql(new PostgresHybridOptions());

        // c = 60 must appear in the fused scoring (matches RrfMerger / FusionService).
        Assert.Equal(60, PostgresHybridRetriever.RrfConstant);
        Assert.Contains("1.0 / (60 + d.rank)", sql, StringComparison.Ordinal);
        Assert.Contains("1.0 / (60 + s.rank)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHybridSql_uses_cosine_operator_and_full_text_search()
    {
        var sql = PostgresHybridRetriever.BuildHybridSql(new PostgresHybridOptions());

        // Dense leg ranks by the pgvector cosine-distance operator.
        Assert.Contains("<=> @embedding::vector", sql, StringComparison.Ordinal);
        // Sparse leg ranks by cover-density FTS (ts_rank_cd + websearch_to_tsquery).
        Assert.Contains("ts_rank_cd", sql, StringComparison.Ordinal);
        Assert.Contains("websearch_to_tsquery('english', @query)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHybridSql_quotes_configured_identifiers()
    {
        var options = new PostgresHybridOptions
        {
            TableName = "doc_chunks",
            EmbeddingColumn = "emb",
            TsVectorColumn = "fts",
        };

        var sql = PostgresHybridRetriever.BuildHybridSql(options);

        Assert.Contains("\"doc_chunks\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"emb\" <=> @embedding::vector", sql, StringComparison.Ordinal);
        Assert.Contains("\"fts\" @@ websearch_to_tsquery", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatVectorLiteral_renders_pgvector_bracket_form()
    {
        var literal = PostgresHybridRetriever.FormatVectorLiteral(new float[] { 0.5f, -1.25f, 2f });

        Assert.Equal("[0.5,-1.25,2]", literal);
    }

    [Fact]
    public void FormatVectorLiteral_handles_empty_vector()
    {
        var literal = PostgresHybridRetriever.FormatVectorLiteral(ReadOnlySpan<float>.Empty);

        Assert.Equal("[]", literal);
    }

    [Fact]
    public void BuildSparseQueryVector_dedupes_terms_and_accumulates_frequency()
    {
        var (values, indices) = QdrantHybridRetriever.BuildSparseQueryVector("alpha beta alpha");

        // Two distinct terms -> two sparse dimensions.
        Assert.Equal(2, values.Length);
        Assert.Equal(values.Length, indices.Length);
        // The repeated term accumulates a term frequency of 2.
        Assert.Contains(2f, values);
        Assert.Contains(1f, values);
    }
}
