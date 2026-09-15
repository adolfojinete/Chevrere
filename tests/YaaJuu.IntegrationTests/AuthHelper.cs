using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Domain;

namespace YaaJuu.IntegrationTests;

internal static class AuthHelper
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public const string AdminEmail = "admin@yaajuu.test";
    public const string AdminPassword = "AdminTest!23456";

    public static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
        return payload?.AccessToken ?? throw new InvalidOperationException("Login did not return a token.");
    }

    public static async Task<HttpClient> AuthenticatedAdminAsync(YaaJuuApiFactory factory)
    {
        var client = factory.CreateClientUnredirected();
        var token = await LoginAsync(client, AdminEmail, AdminPassword);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    public static CreateFranchiseeRequest FranchiseeRequest(
        string suffix,
        string ownerEmail,
        string identification)
    {
        return new CreateFranchiseeRequest(
            TenantCode: $"OP-{suffix}",
            TenantName: $"Operador {suffix}",
            FranchiseeCode: $"FR-{suffix}",
            LegalName: $"Operador {suffix} SAS",
            TradeName: $"YaaJuu {suffix}",
            IdentificationType: IdentificationType.Nit,
            IdentificationNumber: identification,
            Email: $"ops-{suffix}@example.com",
            Phone: "+573001112233",
            OwnerEmail: ownerEmail,
            OwnerDisplayName: $"Owner {suffix}",
            OwnerPassword: "OwnerTest!23456",
            StoreCode: $"DS-{suffix}",
            StoreName: $"Dark Store {suffix}",
            AddressInternal: "Calle 50 # 10-20",
            Latitude: 4.65m,
            Longitude: -74.06m,
            PlanCode: "STANDARD");
    }
}
