using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.Modules.Identity.Application.Login;
using YaaJuu.Modules.Subscriptions.Application.Commands;
using YaaJuu.Modules.Tenancy.Application.Commands.CreateFranchisee;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Domain;

namespace YaaJuu.UnitTests.Application;

public sealed class ValidatorTests
{
    [Fact]
    public void Login_requires_email_and_password()
    {
        var validator = new LoginRequestValidator();
        Assert.False(validator.Validate(new LoginRequest("", "")).IsValid);
        Assert.True(validator.Validate(new LoginRequest("a@b.com", "secret")).IsValid);
    }

    [Fact]
    public void Change_plan_requires_ids()
    {
        var validator = new ChangePlanCommandValidator();
        Assert.False(validator.Validate(new ChangePlanCommand(Guid.Empty, "")).IsValid);
        Assert.True(validator.Validate(new ChangePlanCommand(Guid.CreateVersion7(), "STANDARD")).IsValid);
    }

    [Fact]
    public void Create_franchisee_validates_required_fields_and_geo()
    {
        var validator = new CreateFranchiseeCommandValidator();
        var invalid = new CreateFranchiseeRequest(
            "", "", null, "", "", IdentificationType.Nit, "1", "bad", "1", "bad", "", "short", "", "", "", 1, null, "");
        Assert.False(validator.Validate(invalid).IsValid);

        var valid = new CreateFranchiseeRequest(
            "OP-NORTE",
            "Operador",
            "FR-NORTE",
            "Legal",
            "Trade",
            IdentificationType.Nit,
            "900123456",
            "ops@example.com",
            "+573001112233",
            "owner@example.com",
            "Owner",
            "OwnerTest!23456",
            "DS-001",
            "Store",
            "Calle 1",
            4.6m,
            -74.0m,
            "STANDARD");
        Assert.True(validator.Validate(valid).IsValid);
    }
}
