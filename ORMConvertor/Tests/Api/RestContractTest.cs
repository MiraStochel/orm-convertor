using System.Net;
using System.Text.Json;
using Model;
using ORMConvertorAPI.Data;

namespace Tests.Api;

/// <summary>
/// What the interface answers on the reading endpoints, over HTTP (decision 043). The facts
/// asserted here are the ones that live between the orchestration and the client and that no
/// in-process test can see: the path base, the media type, and the shape the values take on
/// the wire. <c>Combined/ApiContentContractTest</c> binds required units to samples inside the
/// process; this one asserts that the binding survives the trip out.
/// </summary>
[Collection(ApiCollection.Name)]
public class RestContractTest(ApiTestHost host)
{
    private readonly HttpClient client = host.CreateClient();

    public static TheoryData<string> ReadingEndpoints =>
    [
        "/required-content",
        "/required-content-advisor",
        "/samples",
        "/samples-advisor",
        "/examples",
        "/ldbc",
    ];

    [Theory]
    [MemberData(nameof(ReadingEndpoints))]
    public async Task EveryReadingEndpointAnswersJsonUnderThePathBase(string path)
    {
        var response = await client.GetAsync(ApiTestHost.BasePath + path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// The units the interface asks for reach the client unchanged. Both halves matter: the
    /// ids and content types are what the frontend keys its input boxes by, and the framework
    /// is named by <see cref="ORMEnum"/>, which decides which side of the conversion the boxes
    /// belong to.
    /// </summary>
    [Fact]
    public async Task RequiredContentCarriesTheUnitsTheServerDeclares()
    {
        using var wire = await ReadAsync("/required-content");

        var expected = RequiredContent.GetRequiredContent;
        Assert.Equal(expected.Count, wire.RootElement.GetArrayLength());

        foreach (var (definition, element) in expected.Zip(wire.RootElement.EnumerateArray()))
        {
            Assert.Equal((int)definition.OrmType, element.GetProperty("ormType").GetInt32());

            var units = element.GetProperty("required").EnumerateArray().ToList();
            Assert.Equal(definition.Required.Count, units.Count);

            foreach (var (unit, serialized) in definition.Required.Zip(units))
            {
                Assert.Equal(unit.Id, serialized.GetProperty("id").GetInt32());
                Assert.Equal((int)unit.ContentType, serialized.GetProperty("contentType").GetInt32());
                Assert.Equal(unit.Description, serialized.GetProperty("description").GetString());
            }
        }
    }

    /// <summary>
    /// Enums travel as numbers. It is worth an assertion of its own because the change that
    /// would break it - registering a <c>JsonStringEnumConverter</c> - looks like a formatting
    /// preference and is a MAJOR break of the contract (decision 041): every client that reads
    /// <c>contentType</c> or <c>ormType</c> as a number would stop.
    /// </summary>
    [Fact]
    public async Task EnumsAreNumbersOnTheWire()
    {
        using var wire = await ReadAsync("/required-content");

        var first = wire.RootElement[0];
        Assert.Equal(JsonValueKind.Number, first.GetProperty("ormType").ValueKind);
        Assert.Equal(JsonValueKind.Number, first.GetProperty("required")[0].GetProperty("contentType").ValueKind);
    }

    /// <summary>
    /// Samples arrive as an object keyed by the id of the unit they fill, not as an array -
    /// that is how the frontend looks a sample up for the box the user is standing in.
    /// </summary>
    [Fact]
    public async Task SamplesAreKeyedByTheUnitTheyFill()
    {
        using var wire = await ReadAsync("/samples");

        Assert.Equal(JsonValueKind.Object, wire.RootElement.ValueKind);

        var keys = wire.RootElement.EnumerateObject().Select(property => int.Parse(property.Name)).ToHashSet();
        Assert.Equal(Samples.GetSamples.Keys.ToHashSet(), keys);

        foreach (var unit in RequiredContent.GetRequiredContent.SelectMany(definition => definition.Required))
        {
            Assert.True(keys.Contains(unit.Id), $"The interface asks for unit {unit.Id} and /samples does not answer with it.");
            Assert.False(
                string.IsNullOrWhiteSpace(wire.RootElement.GetProperty(unit.Id.ToString()).GetString()),
                $"Sample {unit.Id} came over the wire empty.");
        }
    }

    /// <summary>
    /// The examples arrive as whole conversion inputs (decision 099), in the order the page
    /// shows them: the direction as numbers of the enum, and every unit with the name, content
    /// type and content the page then sends to /convert unchanged. <c>Combined/ExampleCatalogTest</c>
    /// asserts that each of them converts; this asserts that what converts is what the client
    /// receives.
    /// </summary>
    [Fact]
    public async Task ExamplesArriveAsWholeConversionInputs()
    {
        using var wire = await ReadAsync("/examples");

        Assert.Equal(JsonValueKind.Array, wire.RootElement.ValueKind);

        var expected = Examples.GetExamples;
        Assert.Equal(expected.Count, wire.RootElement.GetArrayLength());

        foreach (var (example, element) in expected.Zip(wire.RootElement.EnumerateArray()))
        {
            Assert.Equal(example.Key, element.GetProperty("key").GetString());
            Assert.Equal((int)example.SourceOrm, element.GetProperty("sourceOrm").GetInt32());
            Assert.Equal((int)example.TargetOrm, element.GetProperty("targetOrm").GetInt32());

            var units = element.GetProperty("units").EnumerateArray().ToList();
            Assert.Equal(example.Units.Count, units.Count);

            foreach (var (unit, serialized) in example.Units.Zip(units))
            {
                Assert.Equal(unit.Name, serialized.GetProperty("name").GetString());
                Assert.Equal((int)unit.ContentType, serialized.GetProperty("contentType").GetInt32());
                Assert.Equal(unit.Content, serialized.GetProperty("content").GetString());
            }
        }
    }

    /// <summary>
    /// The LDBC catalog (decision 110) arrives whole: the source framework and the entity
    /// units the page sends to /convert, and every query with its workload and translation as
    /// numbers of their enums, its text, its example parameters and the targets that refuse it.
    /// <c>Combined/LdbcCatalogTest</c> holds what the catalog claims; this asserts that the
    /// client receives the catalog the claims are about.
    /// </summary>
    [Fact]
    public async Task TheLdbcCatalogArrivesWithEveryQuery()
    {
        using var wire = await ReadAsync("/ldbc");

        var expected = Ldbc.GetCatalog;
        Assert.Equal((int)expected.SourceOrm, wire.RootElement.GetProperty("sourceOrm").GetInt32());

        var entities = wire.RootElement.GetProperty("entities").EnumerateArray().ToList();
        Assert.Equal(expected.Entities.Count, entities.Count);
        foreach (var (unit, serialized) in expected.Entities.Zip(entities))
        {
            Assert.Equal(unit.Name, serialized.GetProperty("name").GetString());
            Assert.Equal((int)unit.ContentType, serialized.GetProperty("contentType").GetInt32());
            Assert.Equal(unit.Content, serialized.GetProperty("content").GetString());
        }

        var queries = wire.RootElement.GetProperty("queries").EnumerateArray().ToList();
        Assert.Equal(expected.Queries.Count, queries.Count);
        foreach (var (query, serialized) in expected.Queries.Zip(queries))
        {
            Assert.Equal(query.Key, serialized.GetProperty("key").GetString());
            Assert.Equal((int)query.Workload, serialized.GetProperty("workload").GetInt32());
            Assert.Equal((int)query.Translation, serialized.GetProperty("translation").GetInt32());
            Assert.Equal(query.Sql, serialized.GetProperty("sql").GetString());
            Assert.Equal(query.Parameters.Count, serialized.GetProperty("parameters").GetArrayLength());
            Assert.Equal(
                query.RefusedBy.Select(refusal => (int)refusal.Target),
                serialized.GetProperty("refusedBy").EnumerateArray().Select(refusal => refusal.GetProperty("target").GetInt32()));
            Assert.Equal(
                query.FallbackBy.Select(fallback => (int)fallback.Target),
                serialized.GetProperty("fallbackBy").EnumerateArray().Select(fallback => fallback.GetProperty("target").GetInt32()));
        }
    }

    /// <summary>
    /// A path nobody mapped answers 404, and not the frontend's entry document: the static
    /// pipeline sits behind the endpoints, so a mistyped route has to fail as a route.
    /// </summary>
    [Fact]
    public async Task AnUnmappedPathIsNotFound()
    {
        var response = await client.GetAsync(ApiTestHost.BasePath + "/no-such-endpoint", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The path base answers with the frontend's entry document. This is a claim about the
    /// server's static pipeline - <c>UsePathBase</c>, <c>UseDefaultFiles</c> and
    /// <c>MapStaticAssets</c> - and not about the page: decision 032 leaves the frontend
    /// without automated tests and that does not change here.
    /// </summary>
    [Fact]
    public async Task ThePathBaseServesTheFrontendEntryDocument()
    {
        var response = await client.GetAsync(ApiTestHost.BasePath + "/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    private async Task<JsonDocument> ReadAsync(string path) =>
        JsonDocument.Parse(await client.GetStringAsync(ApiTestHost.BasePath + path, TestContext.Current.CancellationToken));
}
