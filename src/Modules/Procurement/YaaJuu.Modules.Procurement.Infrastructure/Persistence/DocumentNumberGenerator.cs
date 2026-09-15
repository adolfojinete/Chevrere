using System.Globalization;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Procurement.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace YaaJuu.Modules.Procurement.Infrastructure.Persistence;

/// <summary>
/// Document numbers come from PostgreSQL sequences: nextval is atomic, never blocks a concurrent
/// caller and never hands the same value twice, even when the surrounding transaction rolls back.
/// Gaps are accepted; reuse is not.
/// </summary>
public sealed class DocumentNumberGenerator(YaaJuuDbContext dbContext) : IDocumentNumberGenerator
{
    public const string PurchaseOrderSequence = "procurement_purchase_order_number_seq";
    public const string GoodsReceiptSequence = "procurement_goods_receipt_number_seq";

    public async Task<string> NextPurchaseOrderNumberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var next = await dbContext.Database
            .SqlQueryRaw<long>("SELECT nextval('procurement_purchase_order_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return Format("PO", next);
    }

    public async Task<string> NextGoodsReceiptNumberAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var next = await dbContext.Database
            .SqlQueryRaw<long>("SELECT nextval('procurement_goods_receipt_number_seq') AS \"Value\"")
            .SingleAsync(cancellationToken);

        return Format("GR", next);
    }

    private static string Format(string prefix, long value) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{value:D8}");
}
