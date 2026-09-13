namespace Chevrere.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ChevrereApiFactory>
{
    public const string Name = "api";
}
