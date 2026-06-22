using Microsoft.Extensions.AI;
using SmartDocs.Performance.Routing;

namespace SmartDocs.UnitTests.Performance;

public sealed class MultiBackendChatClientTests
{
    [Fact]
    public async Task Routes_to_ptu_while_there_is_headroom()
    {
        var ptu = new LabelChatClient("ptu");
        var payg = new LabelChatClient("payg");
        var router = new MultiBackendChatClient(ptu, payg, queueDepthThreshold: 2);

        var response = await router.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("ptu", response.Text);
        Assert.Equal(1, ptu.Calls);
        Assert.Equal(0, payg.Calls);
    }

    [Fact]
    public async Task Overflows_to_payg_once_the_queue_depth_threshold_is_saturated()
    {
        // PTU calls block until released, so we can hold both PTU slots open.
        var ptu = new BlockingChatClient("ptu");
        var payg = new LabelChatClient("payg");
        var router = new MultiBackendChatClient(ptu, payg, queueDepthThreshold: 2);

        // Occupy both PTU slots with two in-flight, not-yet-completed calls.
        var first = router.GetResponseAsync([new ChatMessage(ChatRole.User, "a")]);
        var second = router.GetResponseAsync([new ChatMessage(ChatRole.User, "b")]);
        await ptu.WaitUntilInFlightAsync(2);

        Assert.Equal(2, router.PtuInFlight);

        // A third call with no PTU headroom must take the PAYG overflow path.
        var overflow = await router.GetResponseAsync([new ChatMessage(ChatRole.User, "c")]);
        Assert.Equal("payg", overflow.Text);
        Assert.Equal(1, payg.Calls);

        // Release the held PTU calls and confirm they completed on PTU.
        ptu.ReleaseAll();
        Assert.Equal("ptu", (await first).Text);
        Assert.Equal("ptu", (await second).Text);
        Assert.Equal(0, router.PtuInFlight);
    }

    private sealed class LabelChatClient : IChatClient
    {
        private readonly string _label;
        public int Calls { get; private set; }
        public LabelChatClient(string label) => _label = label;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _label)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, _label);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        private readonly string _label;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private readonly TaskCompletionSource<int> _reachedTwo = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingChatClient(string label) => _label = label;

        public Task WaitUntilInFlightAsync(int count)
            => count <= Volatile.Read(ref _inFlight) ? Task.CompletedTask : _reachedTwo.Task;

        public void ReleaseAll() => _release.TrySetResult();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _inFlight) == 2)
            {
                _reachedTwo.TrySetResult(2);
            }
            await _release.Task.ConfigureAwait(false);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, _label));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
