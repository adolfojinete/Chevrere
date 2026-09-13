using Chevrere.SharedKernel.Context;

namespace Chevrere.Infrastructure.Context;

public sealed class TenantFilterBypass : ITenantFilterBypass
{
    public bool Enabled { get; set; }
}
