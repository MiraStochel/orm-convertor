using AdvisorBenchmarking;
using Model;
using OrmConvertor;
using SampleData;
using Tests.Catalog;

namespace Tests.Combined;

/// <summary>
/// The harness of the Advisor is a generated source too, so the second sentence of S4 binds it
/// although the area it belongs to is exempt (decision 160): the connection string is a value
/// the harness takes in <c>Setup(string)</c> at run time, never text of its source, and so never
/// a literal of the assembly compiled from it. The harness is built here as a run builds it -
/// the translation of the Advisor sample into each measured framework - without a database
/// and without the native library.
/// </summary>
public class HarnessCarriesNoCredentialsTest
{
    private static readonly string[] ConnectionMarkers =
    [
        "const string ConnectionString",
        "Server=",
        "Data Source=",
        "User ID=",
        "Password",
        "Integrated Security",
    ];

    public static TheoryData<ORMEnum> MeasuredFrameworks => [ORMEnum.Dapper, ORMEnum.EFCore];

    [Theory]
    [MemberData(nameof(MeasuredFrameworks))]
    public void TheHarnessTakesItsConnectionAtRunTime(ORMEnum framework)
    {
        List<ConversionSource> units =
        [
            new() { Content = AdvisorEFCoreSamples.Entity, ContentType = ConversionContentType.CSharp },
            new() { Content = AdvisorEFCoreSamples.Query1, ContentType = ConversionContentType.CSharp },
        ];
        var translation = ConversionHandler.Convert(ORMEnum.EFCore, framework, units).Sources;

        var harness = BenchmarkHarnessBuilder.Build(framework, translation, new FakeCatalogReader());

        Assert.Contains("public void Setup(string connectionString)", harness.Source, StringComparison.Ordinal);
        foreach (var marker in ConnectionMarkers)
        {
            Assert.DoesNotContain(marker, harness.Source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
