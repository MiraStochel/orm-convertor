using System.Diagnostics;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using OrmConvertor;
using Tests.Database;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// S3 with a connected database: the same project size as
/// <see cref="TranslationPerformanceTest"/>, translated with the completion phase
/// reading the catalog of the schema the collection owns. Until this scenario existed
/// the 30-second bound was measured dry, so the catalog phase contributed zero to it and
/// the claim about translation performance with a connected database had no number.
/// Three of the hundred entities match the schema's tables, so the phase does everything
/// it does in a real conversion - the batched image read over all hundred entities, fact
/// completion, foreign keys and the junction probe - not just a lookup that finds
/// nothing. The reader is created for this one conversion and dies with it, exactly the
/// cost /convert pays per request; the collection's cached reader would let an earlier
/// test pre-pay the reads and the number would depend on test order.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class CatalogTranslationPerformanceTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    private const int SyntheticEntityCount = 97;
    private const int SchemaEntityCount = 3;
    private const int EntityCount = SyntheticEntityCount + SchemaEntityCount;
    private const int QueryCount = 100;

    private static ConversionSource SchemaEntity(string content) => new()
    {
        ContentType = ConversionContentType.CSharpEntity,
        Content = content,
    };

    private static ConversionSource SchemaQuery(string entity, string filter, string order, string projection) => new()
    {
        ContentType = ConversionContentType.CSharpQuery,
        Content = $$"""
            public void Query()
            {
                var q = ctx.Set<{{entity}}>()
                    .Where(e => e.{{filter}} > 0)
                    .OrderBy(e => e.{{order}})
                    .Select(e => new { Value = e.{{projection}} })
                    .ToList();
            }
            """,
    };

    private static List<ConversionSource> ProjectSources()
    {
        var sources = new List<ConversionSource>(EntityCount + QueryCount);

        for (int i = 0; i < SyntheticEntityCount; i++)
        {
            sources.Add(PerformanceProject.SyntheticEntity(i));
        }

        sources.Add(SchemaEntity(DapperSourceEntities.CustomerSource));
        sources.Add(SchemaEntity(DapperSourceEntities.OrderSource));
        sources.Add(SchemaEntity(DapperSourceEntities.OrderLineSource));

        for (int i = 0; i < QueryCount - SchemaEntityCount; i++)
        {
            sources.Add(PerformanceProject.SyntheticQuery(i));
        }

        sources.Add(SchemaQuery("Customer", "CustomerId", "Name", "Name"));
        sources.Add(SchemaQuery("Order", "CompanyId", "OrderDate", "OrderId"));
        sources.Add(SchemaQuery("OrderLine", "Quantity", "Description", "Description"));

        return sources;
    }

    [Fact]
    public void TheS3ProjectTranslatesWithinTheBoundWithAConnectedCatalog()
    {
        fixture.SkipIfUnavailable();

        var sources = ProjectSources();

        ConversionResult result;
        var stopwatch = Stopwatch.StartNew();
        using (var reader = new SqlServerCatalogReader(TestDatabase.ConnectionString!))
        {
            result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, sources, reader);
        }
        stopwatch.Stop();

        // The bound proves nothing if the run quietly translated less than the scenario
        // claims, or translated it dry: the artifact counts, the reached catalog and the
        // supplied facts are part of the assertion. OrderLine's hbm.xml exists only
        // because the catalog supplied its key - a dry run refuses the entity.
        Assert.Equal(CatalogConnectionState.Reached, result.CatalogState);
        Assert.NotNull(result.CatalogReadTime);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Supplied);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(EntityCount, result.Sources.Count(s => s.ContentType == ConversionContentType.XML));
        Assert.Equal(QueryCount, result.Sources.Count(s => s.ContentType == ConversionContentType.HqlQuery));

        // The number is machine-bound and quoted in the deployment view, so every run
        // states it instead of keeping it retrievable only from a failure message.
        output.WriteLine(
            $"Translation with a connected catalog: {stopwatch.Elapsed.TotalSeconds:F2} s total, "
            + $"catalog read {result.CatalogReadTime!.Value.TotalMilliseconds:F0} ms.");

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(30),
            $"Translation with a connected catalog took {stopwatch.Elapsed.TotalSeconds:F1} s, "
            + $"of which the catalog read {result.CatalogReadTime.Value.TotalMilliseconds:F0} ms; S3 allows 30 s.");
    }
}
