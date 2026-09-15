namespace YaaJuu.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<YaaJuuApiFactory>
{
    public const string Name = "api";
}
