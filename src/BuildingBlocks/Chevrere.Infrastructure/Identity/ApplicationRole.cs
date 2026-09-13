using Microsoft.AspNetCore.Identity;

namespace Chevrere.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole()
    {
    }

    public ApplicationRole(string name)
        : base(name)
    {
        Id = Guid.CreateVersion7();
        NormalizedName = name.ToUpperInvariant();
    }
}
