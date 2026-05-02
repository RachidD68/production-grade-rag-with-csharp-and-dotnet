using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using SmartDocs.Core.Documents;

namespace SmartDocs.Routing.Filtering;

/// <summary>
/// Compiles a <see cref="MetadataFilter"/> into a Qdrant filter object
/// (the JSON shape Qdrant's REST and gRPC clients accept). Supported
/// expression nodes for Phase 3:
/// <list type="bullet">
///   <item>equality on string / int properties of <see cref="DocumentMetadata"/></item>
///   <item><c>AndAlso</c> of two such conditions</item>
/// </list>
/// More node types (range, OR, NOT) land in Phase 6 / Ch 22 alongside
/// freshness queries.
/// </summary>
public sealed class QdrantFilterCompiler
{
    /// <summary>Compile to the JSON object form Qdrant expects.</summary>
    public static string Compile(MetadataFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var conditions = new List<object>();
        Visit(filter.Predicate.Body, filter.Predicate.Parameters[0], conditions);
        if (conditions.Count == 0)
        {
            return "{}";
        }
        var must = new { must = conditions };
        return JsonSerializer.Serialize(must);
    }

    private static void Visit(Expression node, ParameterExpression param, List<object> conditions)
    {
        switch (node.NodeType)
        {
            case ExpressionType.AndAlso:
                {
                    var bin = (BinaryExpression)node;
                    Visit(bin.Left, param, conditions);
                    Visit(bin.Right, param, conditions);
                    return;
                }
            case ExpressionType.Equal:
                {
                    var bin = (BinaryExpression)node;
                    if (TryExtractEquality(bin, param, out var key, out var value))
                    {
                        conditions.Add(new { key, match = new { value } });
                        return;
                    }
                    break;
                }
        }
        throw new NotSupportedException(
            $"Unsupported expression node {node.NodeType}: {node}. Phase 3 supports `==` and `&&` only.");
    }

    private static bool TryExtractEquality(
        BinaryExpression bin,
        ParameterExpression param,
        out string key,
        out object? value)
    {
        // `m.PropName == constant` (or vice versa).
        if (TryExtractMember(bin.Left, param, out var prop) &&
            TryExtractValue(bin.Right, out var v))
        {
            key = ToPayloadKey(prop);
            value = v;
            return true;
        }
        if (TryExtractMember(bin.Right, param, out prop) &&
            TryExtractValue(bin.Left, out v))
        {
            key = ToPayloadKey(prop);
            value = v;
            return true;
        }
        key = "";
        value = null;
        return false;
    }

    private static bool TryExtractMember(Expression expr, ParameterExpression param, out string property)
    {
        if (expr is MemberExpression me && me.Expression == param)
        {
            property = me.Member.Name;
            return true;
        }
        property = "";
        return false;
    }

    private static bool TryExtractValue(Expression expr, out object? value)
    {
        if (expr is ConstantExpression ce)
        {
            value = ce.Value;
            return true;
        }
        if (expr is MemberExpression me && me.Expression is ConstantExpression target)
        {
            // Capture from a closure (e.g. local variable in test).
            value = me.Member switch
            {
                System.Reflection.FieldInfo f => f.GetValue(target.Value),
                System.Reflection.PropertyInfo p => p.GetValue(target.Value),
                _ => null,
            };
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>
    /// Map a CLR property name to the Qdrant payload key used by
    /// <c>QdrantVectorStore</c> (snake_case, see Ch 6).
    /// </summary>
    private static string ToPayloadKey(string property) => property switch
    {
        nameof(DocumentMetadata.Department) => "department",
        nameof(DocumentMetadata.Office) => "office",
        nameof(DocumentMetadata.ConfidentialityLevel) => "confidentiality",
        nameof(DocumentMetadata.DocumentType) => "document_type",
        nameof(DocumentMetadata.FiscalYear) => "fiscal_year",
        nameof(DocumentMetadata.Author) => "author",
        nameof(DocumentMetadata.Silo) => "silo",
        nameof(DocumentMetadata.Title) => "title",
        nameof(DocumentMetadata.Id) => "document_id",
        _ => ToSnakeCase(property),
    };

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                sb.Append('_');
            }
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
