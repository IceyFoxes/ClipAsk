namespace Screenshot.Core.Diagnostics;

public sealed class CaptureTiming
{
    private const long Unset = long.MinValue;
    private readonly TimeProvider clock;
    private readonly long selectionCompleted;
    private long firstTextReceived = Unset;
    private long responseCompleted = Unset;

    public CaptureTiming(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        selectionCompleted = this.clock.GetTimestamp();
    }

    public TimeSpan? FirstTextDelay => Elapsed(Interlocked.Read(ref firstTextReceived));
    public TimeSpan? CompletionDelay => Elapsed(Interlocked.Read(ref responseCompleted));

    public void MarkFirstText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            Interlocked.CompareExchange(ref firstTextReceived, clock.GetTimestamp(), Unset);
    }

    public void MarkCompleted() =>
        Interlocked.CompareExchange(ref responseCompleted, clock.GetTimestamp(), Unset);

    private TimeSpan? Elapsed(long timestamp) =>
        timestamp == Unset ? null : clock.GetElapsedTime(selectionCompleted, timestamp);
}
