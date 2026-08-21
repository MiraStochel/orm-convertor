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
/// </summary>
public class TranslationPerformanceTest
{
    private const int EntityCount = 100;
    private const int QueryCount = 100;

    private static List<ConversionSource> ProjectSources()
    {
        var sources = new List<ConversionSource>(EntityCount + QueryCount);

        // The table name differs from the entity name on purpose: the HQL builder has to
        // invert the mapping per query, so a same-named table would let a broken inversion
        // pass unmeasured.
        for (int i = 0; i < EntityCount; i++)
        {
            sources.Add(new()
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = $$"""
                    using System.ComponentModel.DataAnnotations;
                    using System.ComponentModel.DataAnnotations.Schema;

                    namespace Perf;

                    [Table("Entity{{i}}Rows", Schema = "Perf")]
                    public class Entity{{i}}
                    {
                        [Key]
                        public int Entity{{i}}Id { get; set; }

                        [MaxLength(200)]
                        public string Name { get; set; }

                        public decimal Amount { get; set; }

                        public DateTime CreatedAt { get; set; }

                        public bool IsActive { get; set; }
                    }
                    """,
            });
        }

        for (int i = 0; i < QueryCount; i++)
        {
            sources.Add(new()
            {
                ContentType = ConversionContentType.CSharpQuery,
                Content = $$"""
                    public void Query()
                    {
                        var q = ctx.Set<Entity{{i}}>()
                            .Where(e => e.Amount > {{i}})
                            .OrderBy(e => e.Name)
                            .Select(e => new { Name = e.Name, Amount = e.Amount })
                            .ToList();
                    }
                    """,
            });
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
