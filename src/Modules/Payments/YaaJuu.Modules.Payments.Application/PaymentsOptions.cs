namespace YaaJuu.Modules.Payments.Application;

public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    public bool Enabled { get; set; } = true;

    public string? SecretsMasterKey { get; set; }

    public WompiOptions Wompi { get; set; } = new();

    public ReconciliationOptions Reconciliation { get; set; } = new();
}

public sealed class WompiOptions
{
    /// <summary>
    /// Platform runtime environment for NEW payments: Sandbox or Production.
    /// Not client-controlled. Historical attempts retain their own Environment.
    /// </summary>
    public string Environment { get; set; } = "Sandbox";

    public string SandboxBaseUrl { get; set; } = "https://sandbox.wompi.co/v1";

    public string ProductionBaseUrl { get; set; } = "https://production.wompi.co/v1";

    public int TimeoutSeconds { get; set; } = 30;

    public string? DefaultRedirectUrl { get; set; }
}

public sealed class ReconciliationOptions
{
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 30;

    public int BatchSize { get; set; } = 50;

    public int MinimumAttemptAgeSeconds { get; set; } = 15;
}
