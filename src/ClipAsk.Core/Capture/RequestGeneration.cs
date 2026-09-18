namespace ClipAsk.Core.Capture;

public sealed class RequestGeneration
{
    private long generation;

    public long Next() => Interlocked.Increment(ref generation);

    public bool IsCurrent(long value) => Interlocked.Read(ref generation) == value;
}
