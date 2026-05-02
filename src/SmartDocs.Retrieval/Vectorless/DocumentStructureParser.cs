using System.Text.RegularExpressions;

namespace SmartDocs.Retrieval.Vectorless;

/// <summary>
/// Parses Markdown headings (#, ##, ###, ...) into a structural tree.
/// DOCX/PDF parsers (OpenXML / PdfPig structure layer) follow the same
/// shape — emit StructuralNode tree, then call into VectorlessRetriever.
/// </summary>
public sealed partial class DocumentStructureParser
{
    [GeneratedRegex(@"^(#{1,6})\s+(.*)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    public static StructuralIndex Parse(string title, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(markdown);

        var root = new StructuralNode(title, 0, "/" + title);
        var stack = new Stack<StructuralNode>();
        stack.Push(root);

        var lines = markdown.Split('\n');
        var bodyBuf = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            var match = HeadingRegex().Match(line);
            if (match.Success)
            {
                FlushBody(stack.Peek(), bodyBuf);

                var level = match.Groups[1].Length;
                var name = match.Groups[2].Value.Trim();

                while (stack.Count > 0 && stack.Peek().Level >= level)
                {
                    stack.Pop();
                }
                if (stack.Count == 0)
                {
                    stack.Push(root);
                }
                var parentPath = stack.Peek().Path;
                var node = new StructuralNode(name, level, parentPath + "/" + name);
                stack.Peek().AddChild(node);
                stack.Push(node);
            }
            else
            {
                bodyBuf.AppendLine(line);
            }
        }
        FlushBody(stack.Peek(), bodyBuf);
        return new StructuralIndex(root);

        static void FlushBody(StructuralNode target, System.Text.StringBuilder buf)
        {
            if (buf.Length > 0)
            {
                target.AppendBody(buf.ToString().Trim());
                buf.Clear();
            }
        }
    }
}
