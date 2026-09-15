using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Infrastructure.Context;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
