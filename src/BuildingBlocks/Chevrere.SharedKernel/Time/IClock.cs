namespace Chevrere.SharedKernel.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
