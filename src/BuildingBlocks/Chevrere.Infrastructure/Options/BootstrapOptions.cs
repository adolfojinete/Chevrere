namespace Chevrere.Infrastructure.Options;

public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public SuperAdminBootstrapOptions SuperAdmin { get; set; } = new();
}

public sealed class SuperAdminBootstrapOptions
{
    public string Email { get; set; } = "admin@chevrere.local";

    public string Password { get; set; } = string.Empty;

    public string DisplayName { get; set; } = "Platform Super Admin";
}
