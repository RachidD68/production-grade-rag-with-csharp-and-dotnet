namespace SmartDocs.Retrieval.Vectorless;

/// <summary>
/// A node in the structural index tree. The Markdown / DOCX heading
/// hierarchy is captured as nested <see cref="StructuralNode"/>s; each
/// leaf carries the body text.
/// </summary>
public sealed class StructuralNode
{
    public string Title { get; }
    public string Body { get; private set; }
    public IReadOnlyList<StructuralNode> Children => _children;
    public StructuralNode? Parent { get; internal set; }
    public string Path { get; }
    public int Level { get; }

    private readonly List<StructuralNode> _children = [];

    public StructuralNode(string title, int level, string path, string body = "")
    {
        Title = title;
        Level = level;
        Path = path;
        Body = body;
    }

    public StructuralNode AddChild(StructuralNode child)
    {
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    public void AppendBody(string text)
    {
        Body = string.IsNullOrEmpty(Body) ? text : Body + "\n" + text;
    }

    public IEnumerable<StructuralNode> AllNodes()
    {
        yield return this;
        foreach (var c in _children)
        {
            foreach (var n in c.AllNodes())
            {
                yield return n;
            }
        }
    }
}

public sealed class StructuralIndex
{
    public StructuralNode Root { get; }
    public StructuralIndex(StructuralNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
    }
}
