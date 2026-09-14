using Chevrere.Modules.Procurement.Application.Contracts;
using Chevrere.SharedKernel.Domain.ValueObjects;
using FluentValidation;

namespace Chevrere.Modules.Procurement.Application.Validators;

public sealed class CreateSupplierRequestValidator : AbstractValidator<CreateSupplierRequest>
{
    public CreateSupplierRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TaxIdentification).MaximumLength(50);
        RuleFor(x => x.ContactName).MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class UpdateSupplierRequestValidator : AbstractValidator<UpdateSupplierRequest>
{
    public UpdateSupplierRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TaxIdentification).MaximumLength(50);
        RuleFor(x => x.ContactName).MaximumLength(160);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public sealed class PurchaseOrderItemRequestValidator : AbstractValidator<PurchaseOrderItemRequest>
{
    public PurchaseOrderItemRequestValidator()
    {
        RuleFor(x => x.GlobalProductId).NotEmpty();
        RuleFor(x => x.OrderedQuantity).GreaterThan(0);
        RuleFor(x => x.UnitCostAmount).GreaterThan(0);
        RuleFor(x => x.UnitCostCurrency)
            .NotEmpty()
            .Must(c => !string.IsNullOrWhiteSpace(c) && c.Trim().Length == 3)
            .WithMessage("Currency must be a 3-letter ISO code.")
            .Must(c => CurrencyCodes.IsSupported(c.Trim().ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {CurrencyCodes.Cop}.");
    }
}

public sealed class CreatePurchaseOrderRequestValidator : AbstractValidator<CreatePurchaseOrderRequest>
{
    public CreatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new PurchaseOrderItemRequestValidator());
    }
}

public sealed class UpdatePurchaseOrderRequestValidator : AbstractValidator<UpdatePurchaseOrderRequest>
{
    public UpdatePurchaseOrderRequestValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).SetValidator(new PurchaseOrderItemRequestValidator());
    }
}

public sealed class CancelPurchaseOrderRequestValidator : AbstractValidator<CancelPurchaseOrderRequest>
{
    public CancelPurchaseOrderRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public sealed class ReceiveGoodsLineRequestValidator : AbstractValidator<ReceiveGoodsLineRequest>
{
    public ReceiveGoodsLineRequestValidator()
    {
        RuleFor(x => x.PurchaseOrderItemId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class ReceiveGoodsRequestValidator : AbstractValidator<ReceiveGoodsRequest>
{
    public ReceiveGoodsRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).SetValidator(new ReceiveGoodsLineRequestValidator());
    }
}
