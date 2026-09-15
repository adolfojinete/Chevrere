using YaaJuu.SharedKernel.Context;

namespace YaaJuu.Infrastructure.Context;

public sealed class TenantFilterBypass : ITenantFilterBypass
{
    public bool Enabled { get; set; }
}
