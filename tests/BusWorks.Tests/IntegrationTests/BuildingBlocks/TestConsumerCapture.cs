using System.Threading.Channels;
using BusWorks.Abstractions.Consumer;

namespace BusWorks.Tests.IntegrationTests.BuildingBlocks;

/// <summary>
/// Thread-safe, channel-backed capture buffer for integration tests that drive the real
/// <c>ServiceBusProcessorBackgroundService</c>. Each test consumer writes to this singleton;
/// tests <c>await</c> delivery without a per-invocation <see cref="TaskCompletionSource{T}"/>.
/// </summary>
/// <remarks>
/// Registered as an open-generic singleton
/// (<c>services.AddSingleton(typeof(TestConsumerCapture&lt;&gt;))</c>)
/// so one instance is created per concrete event type on first resolution.
/// </remarks>
internal sealed class TestConsumerCapture<TEvent>
{
    private readonly Channel<(TEvent Event, MessageContext Metadata)> _channel =
        Channel.CreateUnbounded<(TEvent, MessageContext)>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    private int _failsRemaining;

    // ── Failure injection ──────────────────────────────────────────────────

    /// <summary>
    /// Makes the next <paramref name="n"/> calls to <see cref="WriteAsync"/> throw instead
    /// of capturing the event. The background service catches the exception and calls
    /// <c>AbandonMessageAsync</c> (or <c>DeadLetterMessageAsync</c> when the delivery budget
    /// is exhausted), exactly as it does in production.
    /// </summary>
    public void FailNextN(int n) => Interlocked.Exchange(ref _failsRemaining, n);

    // ── Write ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the test consumer after deserialization. Throws when
    /// <see cref="FailNextN"/> has been primed, so the background service exercises
    /// its abandon / dead-letter error path.
    /// </summary>
    public async ValueTask WriteAsync(
        TEvent @event,
        MessageContext metadata,
        CancellationToken cancellationToken = default)
    {
        // Atomically decrement the fail counter; throw until it reaches zero.
        while (true)
        {
            int current = _failsRemaining;
            if (current <= 0) break;
            if (Interlocked.CompareExchange(ref _failsRemaining, current - 1, current) == current)
                throw new InvalidOperationException(
                    $"Simulated consumer failure — {current - 1} failure(s) remaining before success.");
        }

        await _channel.Writer.WriteAsync((@event, metadata), cancellationToken);
    }

    // ── Read ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the next captured delivery.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// Thrown when no event arrives within <paramref name="timeout"/> and the caller's
    /// token has not been cancelled (i.e. the timeout fired, not a deliberate cancellation).
    /// </exception>
    public async Task<(TEvent Event, MessageContext Metadata)> ReadAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
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
                "Verify the consumer is registered in DI and the background service is running.");
        }
    }
}
