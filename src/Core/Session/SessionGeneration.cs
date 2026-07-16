using System.Threading;

namespace GEmuera.Core.Session;

public readonly record struct SessionGeneration
{
    public SessionGeneration(long value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Session generation cannot be negative.");

        Value = value;
    }

    public long Value { get; }
    public static SessionGeneration Initial => new(0);
}

public readonly record struct SessionOperationId(long Value)
{
    public static SessionOperationId None => new(0);
}

public readonly record struct SessionStamp(SessionGeneration Generation, SessionOperationId OperationId);

public sealed class SessionGenerationClock
{
    private long _value;

    public SessionGeneration Current => new(Interlocked.Read(ref _value));

    public SessionGeneration PublishNext()
    {
        return new SessionGeneration(Interlocked.Increment(ref _value));
    }
}

public static class SessionCompletionGuard
{
    public static bool IsCurrent(SessionStamp completion, SessionStamp current)
    {
        return completion.Generation == current.Generation && completion.OperationId == current.OperationId;
    }
}
