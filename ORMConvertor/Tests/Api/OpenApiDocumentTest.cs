using System.Net;
using System.Text.Json;
using OrmConvertor;

namespace Tests.Api;

/// <summary>
/// The OpenAPI document as decision 041 argues for it: the REST contract is one of the three
/// surfaces the version number protects, so the description of that contract has to be
/// readable wherever the tool runs - not only on a development machine.
/// <para>
/// The committed <c>ORMConvertorAPI/openapi.json</c> is deliberately not compared against:
/// <c>architecture.md</c> §6.5 makes it a derived reading artifact whose authority is the
/// running document, and a test over it would grade the shape of generated schemas instead of
/// the behaviour of the interface (decision 043).
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class OpenApiDocumentTest(ApiTestHost host)
{
    private const string DocumentPath = ApiTestHost.BasePath + "/openapi/v1.json";

    /// <summary>
    /// The published set of routes. Written out rather than read back from the endpoint table
    /// on purpose: the document is generated from what the application maps, so comparing the
    /// two would be a tautology. Against a written list, adding, renaming or dropping a route
    /// has to be an edit here as well - which is what makes it the visible, deliberate change
    /// decision 041 treats as MAJOR.
    /// </summary>
    private static readonly string[] MappedPaths =
    [
        "/required-content",
        "/required-content-advisor",
        "/convert",
        "/samples",
        "/samples-advisor",
        "/advisor-test",
        "/advisor/run",
        "/archive",
    ];

    private readonly HttpClient client = host.CreateClient();

    /// <summary>
    /// Production is the environment the deployment assumes, and the document answers there.
    /// This is the whole point of taking <c>MapOpenApi()</c> out of <c>IsDevelopment()</c>: a
    /// contract nobody can read where the tool actually runs does not protect anything.
    /// </summary>
    [Fact]
    public async Task TheDocumentIsServedOutsideDevelopment()
    {
        var response = await client.GetAsync(DocumentPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The document names the version the build produced, not one of its own - the same number
    /// the run record reports (S6, decisions 034 and 041).
    /// </summary>
    [Fact]
    public async Task TheDocumentCarriesTheVersionOfTheBuild()
    {
        using var document = await ReadDocumentAsync();

        var info = document.RootElement.GetProperty("info");
        Assert.Equal("ORMConvertor", info.GetProperty("title").GetString());
        Assert.Equal(ToolRelease.Version, info.GetProperty("version").GetString());
    }

    /// <summary>
    /// The document describes exactly the published set of routes - no more, because an
    /// endpoint excluded from the description would be a route no client can discover, and no
    /// fewer, because a route that quietly disappeared would break every client that used it.
    /// </summary>
    [Fact]
    public async Task TheDocumentDescribesEveryMappedRoute()
    {
        using var document = await ReadDocumentAsync();

        var described = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Select(path => path.Name)
            .ToHashSet();

        Assert.Equal(MappedPaths.ToHashSet(), described);
    }

    /// <summary>
    /// The server the document names carries the path base, so a client generated from it asks
    /// under <c>/orm</c> rather than at the root.
    /// </summary>
    [Fact]
    public async Task TheDocumentNamesTheServerUnderThePathBase()
    {
        using var document = await ReadDocumentAsync();

        var servers = document.RootElement.GetProperty("servers").EnumerateArray().ToList();
        Assert.NotEmpty(servers);
        Assert.All(servers, server =>
            Assert.EndsWith(ApiTestHost.BasePath, server.GetProperty("url").GetString()!, StringComparison.Ordinal));
    }

    /// <summary>
    /// Swagger UI is the development convenience the document is not: it answers in
    /// Development and is absent in Production (§6.5). Both halves are asserted, because a UI
    /// that quietly appeared in Production would put a second, interactive surface in front of
    /// endpoints the deployment assumes nobody browses to.
    /// </summary>
    [Fact]
    public async Task SwaggerUiIsDevelopmentOnly()
    {
        var inProduction = await client.GetAsync(ApiTestHost.BasePath + "/swagger/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, inProduction.StatusCode);

        using var development = new DevelopmentApiTestHost();
        var developmentClient = development.CreateClient();

        var inDevelopment = await developmentClient.GetAsync(ApiTestHost.BasePath + "/swagger/index.html", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, inDevelopment.StatusCode);

        var documentInDevelopment = await developmentClient.GetAsync(DocumentPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, documentInDevelopment.StatusCode);
    }

    private async Task<JsonDocument> ReadDocumentAsync() =>
        JsonDocument.Parse(await client.GetStringAsync(DocumentPath, TestContext.Current.CancellationToken));
}
