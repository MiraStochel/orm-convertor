using Model;

namespace Tests.Combined;

/// <summary>
/// The synthetic sources of the S3 project: EF Core entities whose stated tables exist
/// in no catalog, and one LINQ query per entity. Shared by the dry performance scenario
/// and the catalog-connected one, so the two measurements differ in the connection, not
/// in the project they translate.
/// </summary>
internal static class PerformanceProject
{
    // The table name differs from the entity name on purpose: the HQL builder has to
    // invert the mapping per query, so a same-named table would let a broken inversion
    // pass unmeasured.
    public static ConversionSource SyntheticEntity(int i) => new()
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
    };

    public static ConversionSource SyntheticQuery(int i) => new()
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
    };
}
