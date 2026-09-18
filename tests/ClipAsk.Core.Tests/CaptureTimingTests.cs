using ClipAsk.Core.Diagnostics;
using Xunit;

namespace ClipAsk.Core.Tests;

public sealed class CaptureTimingTests
{
    [Fact]
    public void MeasuresFromSelectionCompletionWithoutResettingOnLaterChunks()
    {
        var clock = new ManualClock();
        var timing = new CaptureTiming(clock);
        Assert.Null(timing.FirstTextDelay);
        Assert.Null(timing.CompletionDelay);
        clock.Advance(75);
        timing.MarkFirstText(" \n");
        Assert.Null(timing.FirstTextDelay);
        clock.Advance(50);
        timing.MarkFirstText("42");
        clock.Advance(300);
        timing.MarkFirstText(" is the answer.");
        Assert.Equal(TimeSpan.FromMilliseconds(125), timing.FirstTextDelay);
        clock.Advance(75);
        timing.MarkCompleted();
        clock.Advance(100);
        timing.MarkCompleted();
        Assert.Equal(TimeSpan.FromMilliseconds(500), timing.CompletionDelay);
    }

    [Fact]
    public void DoesNotInventAFirstTextTimeWhenNoTextArrives()
    {
        var clock = new ManualClock();
        var timing = new CaptureTiming(clock);
        clock.Advance(200);
        timing.MarkCompleted();
        Assert.Null(timing.FirstTextDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(200), timing.CompletionDelay);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => timestamp;
        public void Advance(long milliseconds) => timestamp += milliseconds;
    }
}
