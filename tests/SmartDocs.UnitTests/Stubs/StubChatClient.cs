using Microsoft.Extensions.AI;

namespace SmartDocs.UnitTests;

/// <summary>
/// Deterministic <see cref="IChatClient"/> that returns a single text
/// message produced by a caller-supplied function applied to the assembled
/// prompt. The lambda receives the full text of all user messages joined
/// with newlines so tests can assert on the prompt structure.
/// </summary>
internal sealed class StubChatClient : IChatClient
{
    private readonly Func<string, string> _respond;

    public StubChatClient(Func<string, string> respond)
    {
        _respond = respond;
    }

    public ChatClientMetadata Metadata { get; } = new("stub");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = string.Join(
            Environment.NewLine,
            messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        var reply = _respond(text);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return EnumerateAsync(messages);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> EnumerateAsync(IEnumerable<ChatMessage> messages)
    {
        var text = string.Join(
            Environment.NewLine,
            messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, _respond(text));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() { }
}
