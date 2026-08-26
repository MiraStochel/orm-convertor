using System.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// S3: translating a project of 100 entities and 100 queries finishes within 30 seconds.
/// Until this scenario existed the bound was a number without a measurement. The direction
/// is EF Core to NHibernate because it exercises the most machinery: Roslyn reads both the
/// attributed entities and the LINQ chains on the way in, and every entity leaves as two
/// artifacts (class + hbm.xml) and every query as two more (method + bare HQL).
/// This run is deliberately dry - no connection is passed, so the number covers parsing
/// and generation; the same project with the catalog phase contributing is measured by
/// <see cref="CatalogTranslationPerformanceTest"/>.
/// </summary>
public class TranslationPerformanceTest
{
    private const int EntityCount = 100;
    private const int QueryCount = 100;

    private static List<ConversionSource> ProjectSources()
    {
        var sources = new List<ConversionSource>(EntityCount + QueryCount);

        for (int i = 0; i < EntityCount; i++)
        {
            sources.Add(PerformanceProject.SyntheticEntity(i));
        }

        for (int i = 0; i < QueryCount; i++)
        {
            sources.Add(PerformanceProject.SyntheticQuery(i));
        }

        return sources;
    }

    [Fact]
    public void AHundredEntitiesAndAHundredQueriesTranslateWithinTheS3Bound()
    {
        var sources = ProjectSources();

        var stopwatch = Stopwatch.StartNew();
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, sources);
        stopwatch.Stop();

        // The bound proves nothing if the run quietly translated less than the scenario
        // claims, so the artifact counts are part of the assertion.
        Assert.Equal(EntityCount, result.Sources.Count(s => s.ContentType == ConversionContentType.XML));
        Assert.Equal(QueryCount, result.Sources.Count(s => s.ContentType == ConversionContentType.HqlQuery));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"Translation took {stopwatch.Elapsed.TotalSeconds:F1} s; S3 allows 30 s.");
    }
}
