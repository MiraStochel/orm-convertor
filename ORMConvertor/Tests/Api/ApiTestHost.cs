using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Tests.Api;

/// <summary>
/// The whole application started in memory, answered over a real <see cref="HttpClient"/>
/// (decision 043). Routing, the <c>/orm</c> path base, model binding, status codes and JSON
/// serialization all take part in the verdict, which is what separates these tests from the
/// ones that call the orchestration directly.
/// <para>
/// Two settings are the point of this class rather than its plumbing:
/// </para>
/// <list type="bullet">
/// <item><b>The environment is pinned to Production.</b> <see cref="WebApplicationFactory{T}"/>
/// starts in Development by default, and there <c>WebApplication.CreateBuilder</c> reads the
/// API project's user secrets - so on a machine with a catalog configured the same request
/// answers <c>Reached</c> and a read time, and on CI <c>NotConfigured</c> and null. A contract
/// test whose answer depends on whose machine it runs on guards nothing. Production is also
/// the environment the deployment assumption talks about, which makes the one difference
/// between the two - Swagger UI is development-only, the OpenAPI document is not - a testable
/// claim.</item>
/// <item><b>The catalog connection string is blanked.</b> It overrides both the file and the
/// environment variable, so the completion phase never runs and <c>CatalogState</c> and
/// <c>CatalogReadMilliseconds</c> are facts of the contract instead of facts of the machine.
/// Conversion with a catalog is proved against the database by the verification tests, not
/// through the interface.</item>
/// </list>
/// </summary>
public sealed class ApiTestHost : WebApplicationFactory<ORMConvertorAPI.Program>
{
    /// <summary>Path base the application serves everything under.</summary>
    internal const string BasePath = "/orm";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CatalogDatabase"] = string.Empty,
            }));
    }
}

/// <summary>
/// The same application in Development, for the one test that asserts what the development
/// environment adds. Kept apart from <see cref="ApiTestHost"/> so that nothing else can
/// accidentally answer with a development-only pipeline.
/// </summary>
public sealed class DevelopmentApiTestHost : WebApplicationFactory<ORMConvertorAPI.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment(Environments.Development);
}

/// <summary>
/// One started host for all HTTP tests: booting the application per test class would pay the
/// startup cost several times over for no added assurance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiTestHost>
{
    public const string Name = "ApiOverHttp";
}
