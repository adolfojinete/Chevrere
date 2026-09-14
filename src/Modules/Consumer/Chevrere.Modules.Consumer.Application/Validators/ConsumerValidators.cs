using Chevrere.Modules.Consumer.Application.Contracts;
using Chevrere.Modules.Consumer.Domain.ValueObjects;
using FluentValidation;

namespace Chevrere.Modules.Consumer.Application.Validators;

public sealed class ConfigureServiceAreaRequestValidator : AbstractValidator<ConfigureServiceAreaRequest>
{
    public ConfigureServiceAreaRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
        RuleFor(x => x.ServiceRadiusMeters)
            .InclusiveBetween(ServiceRadius.MinMeters, ServiceRadius.MaxMeters);
    }
}

public sealed class ConsumerLocationRequestValidator : AbstractValidator<ConsumerLocationRequest>
{
    public ConsumerLocationRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
    }
}

public sealed class ConsumerCatalogSearchRequestValidator : AbstractValidator<ConsumerCatalogSearchRequest>
{
    public ConsumerCatalogSearchRequestValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90d, 90d);
        RuleFor(x => x.Longitude).InclusiveBetween(-180d, 180d);
        RuleFor(x => x.Search).MaximumLength(120);
    }
}
