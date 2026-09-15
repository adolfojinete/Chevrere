using System.Globalization;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Orders.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Orders.Infrastructure.Persistence;

public sealed class OrderNumberGenerator(YaaJuuDbContext dbContext) : IOrderNumberGenerator
{
    public const string Sequence = "orders_order_number_seq";

    public async Task<string> NextOrderNumberAsync(CancellationToken cancellationToken)
    {
        var next = await dbContext.Database
            .SqlQueryRaw<long>("SELECT nextval('orders_order_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return string.Create(CultureInfo.InvariantCulture, $"ORD-{next:D8}");
    }
}
