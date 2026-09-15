using System.ComponentModel.DataAnnotations;

namespace YaaJuu.Infrastructure.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "yaajuu";

    [Required]
    public string Audience { get; set; } = "yaajuu-api";

    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(5, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;
}
