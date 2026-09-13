namespace Chevrere.Infrastructure.Options;

public sealed class SubscriptionOptions
{
    public const string SectionName = "Subscriptions";

    /// <summary>
    /// Configurable hypothesis. Not a contractual business rule.
    /// Allowed values: Trial, Active.
    /// </summary>
    public string DefaultInitialStatus { get; set; } = "Trial";

    /// <summary>Configurable hypothesis. Zero starts the first period immediately.</summary>
    public int TrialDays { get; set; } = 14;

    /// <summary>Configurable hypothesis. Used when entering grace period; not applied automatically yet.</summary>
    public int GracePeriodDays { get; set; } = 7;
}
