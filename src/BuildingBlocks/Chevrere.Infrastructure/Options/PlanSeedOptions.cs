namespace Chevrere.Infrastructure.Options;

public sealed class PlanSeedOptions
{
    public const string SectionName = "PlanSeed";

    public string Code { get; set; } = "STANDARD";

    public string Name { get; set; } = "Chevrere Standard";

    public string Description { get; set; } = "Plan inicial de hipótesis comercial. El precio es configurable.";

    public decimal MonthlyPrice { get; set; } = 350_000m;

    public string Currency { get; set; } = "COP";
}
