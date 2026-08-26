using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using OrmConvertor;
using SampleData;

namespace Tests.Api;

/// <summary>
/// The translation endpoint over HTTP (decision 043). <c>/convert</c> is the one route the
/// whole pipeline goes through, so this is where the run record S6 promises and the artifacts
/// S2 calls deterministic have to survive serialization - and where the failure paths have to
/// answer with a status code rather than with a stack trace.
/// <para>
/// The request bodies are written as JSON rather than built from the DTO types on purpose:
/// the contract is the wire format, and a test that shares the server's types would not
/// notice a property being renamed on both sides at once.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class ConvertEndpointTest(ApiTestHost host)
{
    private readonly HttpClient client = host.CreateClient();

    private static object EFCoreEntityRequest => new
    {
        sourceOrm = (int)ORMEnum.EFCore,
        targetOrm = (int)ORMEnum.NHibernate,
        sources = new[]
        {
            new { contentType = (int)ConversionContentType.CSharpEntity, content = CustomerSampleEFCore.Entity },
        },
    };

    /// <summary>
    /// Every field S6 asks the run record to carry arrives filled. The version is checked
    /// against <see cref="ToolRelease.Version"/> rather than against a literal, because the
    /// number is written once for the whole solution (decision 034) and a test that repeated
    /// it would be the second place it lives.
    /// </summary>
    [Fact]
    public async Task TheRunRecordArrivesComplete()
    {
        using var body = await ConvertAsync(EFCoreEntityRequest);

        Assert.NotEqual(Guid.Empty, body.RootElement.GetProperty("runId").GetGuid());
        Assert.Equal(ToolRelease.Version, body.RootElement.GetProperty("toolVersion").GetString());
        Assert.Equal((int)ORMEnum.EFCore, body.RootElement.GetProperty("sourceFramework").GetInt32());
        Assert.Equal((int)ORMEnum.NHibernate, body.RootElement.GetProperty("targetFramework").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("sourceFrameworkVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("targetFrameworkVersion").GetString()));
    }

    /// <summary>
    /// The identifier is fresh on every call, which is the only thing that lets two answers be
    /// told apart afterwards (S6).
    /// </summary>
    [Fact]
    public async Task EveryCallGetsItsOwnRunIdentifier()
    {
        using var first = await ConvertAsync(EFCoreEntityRequest);
        using var second = await ConvertAsync(EFCoreEntityRequest);

        Assert.NotEqual(
            first.RootElement.GetProperty("runId").GetGuid(),
            second.RootElement.GetProperty("runId").GetGuid());
    }

    /// <summary>
    /// Without a catalog the answer says so instead of staying silent about it, and the read
    /// time stays null rather than claiming a read that never happened (S3, decision 015). The
    /// test host blanks the connection string, so this is the state the contract describes, not
    /// the state of the machine.
    /// </summary>
    [Fact]
    public async Task TheAnswerReportsThatTheCatalogTookNoPart()
    {
        using var body = await ConvertAsync(EFCoreEntityRequest);

        Assert.Equal((int)CatalogConnectionState.NotConfigured, body.RootElement.GetProperty("catalogState").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("catalogReadMilliseconds").ValueKind);
    }

    /// <summary>
    /// The generated artifacts come back as the target framework's own pair, each naming the
    /// language it is written in (decision 025).
    /// </summary>
    [Fact]
    public async Task TheTargetArtifactsComeBackNamedByTheirLanguage()
    {
        using var body = await ConvertAsync(EFCoreEntityRequest);

        var artifacts = body.RootElement.GetProperty("sources").EnumerateArray().ToList();
        var byType = artifacts.ToDictionary(
            artifact => (ConversionContentType)artifact.GetProperty("contentType").GetInt32(),
            artifact => artifact.GetProperty("content").GetString()!);

        Assert.Contains(ConversionContentType.CSharpEntity, byType.Keys);
        Assert.Contains(ConversionContentType.XML, byType.Keys);
        Assert.Contains("public class Customer", byType[ConversionContentType.CSharpEntity]);
        Assert.Contains("<hibernate-mapping", byType[ConversionContentType.XML]);
    }

    /// <summary>
    /// The trip through JSON changes nothing about the artifact. <c>Combined/RunRecordTest</c>
    /// asserts that two in-process runs agree byte for byte (S2); this asserts that the wire
    /// agrees with them - generated code carries CRLF line endings, and that is exactly the
    /// kind of detail a serialization layer can quietly rewrite.
    /// </summary>
    [Fact]
    public async Task TheArtifactOnTheWireIsTheArtifactTheOrchestrationProduced()
    {
        using var body = await ConvertAsync(EFCoreEntityRequest);

        var inProcess = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.NHibernate,
            [new ConversionSource
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = CustomerSampleEFCore.Entity,
            }],
            catalogConnectionString: null);

        var overTheWire = body.RootElement.GetProperty("sources").EnumerateArray().ToList();
        Assert.Equal(inProcess.Sources.Count, overTheWire.Count);

        foreach (var (expected, actual) in inProcess.Sources.Zip(overTheWire))
        {
            Assert.Equal((int)expected.ContentType, actual.GetProperty("contentType").GetInt32());
            Assert.Equal(expected.Content, actual.GetProperty("content").GetString(), ignoreLineEndingDifferences: false);
        }
    }

    /// <summary>
    /// Diagnostic records reach the client as data, not as a message in a log (decision 010),
    /// and each one carries the fields that make it readable: what kind it is, which framework
    /// it speaks about and why it was written.
    /// </summary>
    [Fact]
    public async Task DiagnosticRecordsArriveAsData()
    {
        using var body = await ConvertAsync(EFCoreEntityRequest);

        var records = body.RootElement.GetProperty("records").EnumerateArray().ToList();
        Assert.NotEmpty(records);

        foreach (var record in records)
        {
            Assert.Equal(JsonValueKind.Number, record.GetProperty("kind").ValueKind);
            Assert.Equal(JsonValueKind.Number, record.GetProperty("framework").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("reason").GetString()));
        }
    }

    /// <summary>
    /// A framework the tool does not know is refused with 400 and a reason, not with a 500, and
    /// the reason arrives in the shape the OpenAPI document promises: <c>ProblemDetails</c> per
    /// RFC 9457, served as <c>application/problem+json</c> (decision 044). The reason itself is
    /// in <c>detail</c>, because <c>title</c> is the framework's generic "Bad Request".
    /// </summary>
    [Fact]
    public async Task AnUnknownFrameworkIsRefusedWithAReason()
    {
        var response = await client.PostAsJsonAsync(
            ApiTestHost.BasePath + "/convert",
            new { sourceOrm = 99, targetOrm = (int)ORMEnum.NHibernate, sources = Array.Empty<object>() },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JsonValueKind.Object, body.RootElement.ValueKind);
        Assert.Equal((int)HttpStatusCode.BadRequest, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
        Assert.Contains("Source ORM", body.RootElement.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Input the parsers cannot read answers 200 with no artifact - and with records saying
    /// why, instead of the empty <c>records</c> array that used to come back: one about the
    /// unit that yielded nothing (decision 066) and one about the run that generated nothing
    /// (decision 045). The status code stays 200 on purpose: a partial conversion has to
    /// hand over what it produced, so the reason belongs in the records and not in the
    /// status line.
    /// </summary>
    [Fact]
    public async Task InputThatYieldsNothingIsAnsweredWithAReasonRatherThanSilence()
    {
        using var body = await ConvertAsync(new
        {
            sourceOrm = (int)ORMEnum.EFCore,
            targetOrm = (int)ORMEnum.NHibernate,
            sources = new[]
            {
                new { contentType = (int)ConversionContentType.CSharpEntity, content = "this is not C#" },
            },
        });

        Assert.Empty(body.RootElement.GetProperty("sources").EnumerateArray());

        var records = body.RootElement.GetProperty("records").EnumerateArray().ToList();
        Assert.Equal(2, records.Count);
        Assert.All(records, record =>
        {
            Assert.Equal((int)ConversionRecordKind.Failure, record.GetProperty("kind").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("reason").GetString()));
        });
        Assert.Contains(records, record => record.GetProperty("unit").GetString() == "unit 1");
        Assert.Contains(records, record => record.GetProperty("unit").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task AMalformedBodyIsRefused()
    {
        var response = await client.PostAsync(
            ApiTestHost.BasePath + "/convert",
            new StringContent("{ this is not json", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A body that is not JSON at all is refused before it reaches the handler, so the parsers
    /// never see input that was never meant for them.
    /// </summary>
    [Fact]
    public async Task ABodyOfTheWrongMediaTypeIsRefused()
    {
        var response = await client.PostAsync(
            ApiTestHost.BasePath + "/convert",
            new StringContent("public class Customer { }", Encoding.UTF8, "text/plain"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    /// <summary>
    /// The unit's client-given name travels the whole way (decision 066): sent with the
    /// unit, it comes back on the record about that unit, so the caller can point at the
    /// file it uploaded even when another unit of the same run produced artifacts.
    /// </summary>
    [Fact]
    public async Task ARecordPointsAtTheUnitByItsGivenName()
    {
        var request = new
        {
            sourceOrm = (int)ORMEnum.EFCore,
            targetOrm = (int)ORMEnum.NHibernate,
            sources = new object[]
            {
                new { contentType = (int)ConversionContentType.CSharpEntity, content = CustomerSampleEFCore.Entity },
                new { contentType = (int)ConversionContentType.CSharpEntity, content = "this is not C#", name = "Broken.cs" },
            },
        };

        using var body = await ConvertAsync(request);

        Assert.NotEqual(0, body.RootElement.GetProperty("sources").GetArrayLength());
        Assert.Contains(
            body.RootElement.GetProperty("records").EnumerateArray(),
            record => record.GetProperty("unit").GetString() == "Broken.cs");
    }

    private async Task<JsonDocument> ConvertAsync(object request)
    {
        var response = await client.PostAsJsonAsync(
            ApiTestHost.BasePath + "/convert",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
