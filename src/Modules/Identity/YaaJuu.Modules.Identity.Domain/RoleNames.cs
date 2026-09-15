namespace YaaJuu.Modules.Identity.Domain;

public static class RoleNames
{
    public const string PlatformSuperAdmin = "PlatformSuperAdmin";
    public const string PlatformAdmin = "PlatformAdmin";
    public const string PlatformSupport = "PlatformSupport";
    public const string FranchiseeOwner = "FranchiseeOwner";
    public const string Consumer = "Consumer";

    public static readonly IReadOnlyCollection<string> PlatformRoles =
    [
        PlatformSuperAdmin,
        PlatformAdmin,
        PlatformSupport
    ];

    public static readonly IReadOnlyCollection<string> All =
    [
        PlatformSuperAdmin,
        PlatformAdmin,
        PlatformSupport,
        FranchiseeOwner,
        Consumer
    ];

    public static bool IsPlatformRole(string role) =>
        PlatformRoles.Contains(role, StringComparer.Ordinal);
}
