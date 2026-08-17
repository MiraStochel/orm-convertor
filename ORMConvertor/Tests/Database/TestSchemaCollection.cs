namespace Tests.Database;

/// <summary>
/// Every database-dependent test belongs to this collection, so the schema is created
/// and dropped once for all of them and they do not run against each other in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class TestSchemaCollection : ICollectionFixture<TestSchemaFixture>
{
    public const string Name = "TestDatabaseSchema";
}
