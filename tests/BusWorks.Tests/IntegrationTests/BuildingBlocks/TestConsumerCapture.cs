using System.Threading.Channels;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

/// <summary>
/// Thread-safe, channel-backed capture buffer that test consumers write into after processing
/// a message, allowing the arranging test to <c>await</c> delivery without a
/// per-invocation <see cref="TaskCompletionSource{T}"/>.
/// </summary>
/// <remarks>
/// Registered in the DI container as an open-generic singleton
/// (<c>services.AddSingleton(typeof(TestConsumerCapture&lt;&gt;))</c>).
/// One singleton per concrete event type is created on first resolution.
/// </remarks>
internal sealed class TestConsumerCapture<TEvent>
{
    private readonly Channel<TEvent> _channel = Channel.CreateUnbounded<TEvent>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    /// <summary>Writes a captured event into the channel.</summary>
    public ValueTask WriteAsync(TEvent @event, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(@event, cancellationToken);

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the next event written by the consumer.
    /// Combines the caller's <paramref name="cancellationToken"/> with the timeout so that
    /// either a test-level cancellation or an exceeded wait window surfaces as a clear error.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// Thrown when no event arrives within <paramref name="timeout"/> and the caller's token
    /// has not been cancelled (i.e. the timeout fired, not a deliberate cancellation).
    /// </exception>
    public async Task<TEvent> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No event of type '{typeof(TEvent).Name}' was captured within {timeout.TotalSeconds} s. " +
                "Check that the consumer is registered in DI and the message was published successfully.");
        }
    }
}


