namespace YaaJuu.SharedKernel.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
