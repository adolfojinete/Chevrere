using Chevrere.SharedKernel.Time;

namespace Chevrere.Infrastructure.Context;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
