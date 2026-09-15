using YaaJuu.Modules.Orders.Application.Contracts;
using FluentValidation;

namespace YaaJuu.Modules.Orders.Application.Validators;

public sealed class OrderLocationRequestValidator : AbstractValidator<OrderLocationRequest>
{
    public OrderLocationRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
    }
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
    }
}

public sealed class ConsumerLocationQuantityRequestValidator : AbstractValidator<ConsumerLocationQuantityRequest>
{
    public ConsumerLocationQuantityRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}
