using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A root written by the name of a set the context of the conversion declares - <c>ctx.Categories</c>
/// over <c>DbSet&lt;Category&gt; Categories</c> - is the query over that entity, <c>Set&lt;Category&gt;()</c>,
/// whatever the name says and wherever the context stands (decision 185). It used to resolve by the
/// name alone - table, entity, or the entity with an "s" (rule Q2, decision 050) - so a set named by
/// another plural over a table named otherwise went out over a table and a result type that do not
/// exist (<c>Categories</c>, <c>Categorie</c>), with nothing but a Convention record. The two passes
/// read through one context found through the entity builder, as MyBatis's do (decision 084), which
/// is how the query pass knows what the entity pass read out of the context class.
/// </summary>
public class SetNamedUnlikeItsEntityTest
{
    private const string Entities = """
        public class Order
        {
            public int OrderID { get; set; }

            public int CategoryID { get; set; }

            public decimal Amount { get; set; }
        }


        """;

    private const string Category = """
        public class Category
        {
            public int CategoryID { get; set; }

            public string Name { get; set; } = "";
        }


        """;

    private const string AnnotatedCategory = "[Table(\"Category\")]\n" + Category;

    private const string Context = """
        public class ShopContext : DbContext
        {
            public DbSet<Order> Orders { get; set; } = null!;

            public DbSet<Category> Categories { get; set; } = null!;
        }


        """;

    private const string ConfiguringContext = """
        public class ShopContext : DbContext
        {
            public DbSet<Order> Orders { get; set; } = null!;

            public DbSet<Category> Categories { get; set; } = null!;

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                modelBuilder.Entity<Category>().ToTable("Category");
            }
        }


        """;

    private const string Configuration = """
        public class CategoryMap : IEntityTypeConfiguration<Category>
        {
            public void Configure(EntityTypeBuilder<Category> builder)
            {
                builder.ToTable("Category");
            }
        }


        """;

    private const string Chain = ".Where(c => c.CategoryID > 1).ToList()";

    private static ConversionSource Unit(string name, string content)
        => new() { ContentType = ConversionContentType.CSharp, Content = content, Name = name };

    private static ConversionResult Convert(params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [.. units]);

    private static string Static(string code) => "public static class Queries\n{\n    " + code + "\n}\n";

    private static List<string> Queries(ConversionResult result)
        => [.. result.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery).Select(s => s.Content)];

    private static List<ConversionRecord> Failures(ConversionResult result)
        => [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Failure)];

    private static bool DerivesTheResultType(ConversionRecord record)
        => record.Kind == ConversionRecordKind.Convention && record.Reason.Contains("was derived from the table name", StringComparison.Ordinal);

    /// <summary>The three places the table Category may be named: the annotation, OnModelCreating, a configuration class.</summary>
    private static ConversionSource[] Model(string where) => where switch
    {
        "annotation" => [Unit("Model.cs", Entities + AnnotatedCategory), Unit("ShopContext.cs", Context)],
        "OnModelCreating" => [Unit("Model.cs", Entities + Category), Unit("ShopContext.cs", ConfiguringContext)],
        "configuration" => [Unit("Model.cs", Entities + Category), Unit("ShopContext.cs", Context), Unit("CategoryMap.cs", Configuration)],
        _ => throw new ArgumentOutOfRangeException(nameof(where)),
    };

    /// <summary>The query over the set written by name and written as Set&lt;Category&gt;(), over the same model.</summary>
    private static (ConversionResult Named, ConversionResult Typed) Both(string where, string queryUnit)
        => (Convert([.. Model(where), Unit("Queries.cs", queryUnit.Replace("{set}", "Categories", StringComparison.Ordinal))]),
            Convert([.. Model(where), Unit("Queries.cs", queryUnit.Replace("{set}", "Set<Category>()", StringComparison.Ordinal))]));

    private static void AssertTheSameQuery((ConversionResult Named, ConversionResult Typed) both)
    {
        var expected = Assert.Single(Queries(both.Typed));

        Assert.Contains("Category", expected, StringComparison.Ordinal);
        Assert.DoesNotContain("Categories", expected, StringComparison.Ordinal);
        Assert.Equal(expected, Assert.Single(Queries(both.Named)));
        Assert.Empty(Failures(both.Named));
        Assert.DoesNotContain(both.Named.Records, DerivesTheResultType);
    }

    [Theory]
    [InlineData("annotation")]
    [InlineData("OnModelCreating")]
    [InlineData("configuration")]
    public void ARootNamedBySetOfAContextInAnotherUnitIsTheQueryOverItsEntity(string where)
        => AssertTheSameQuery(Both(where, Static("public static List<Category> Big(ShopContext ctx) => ctx.{set}" + Chain + ";")));

    [Theory]
    [InlineData("public List<Category> Big() => _ctx.{set}" + Chain + ";")]
    [InlineData("public List<Category> Big() => this._ctx.{set}" + Chain + ";")]
    public void ARootNamedBySetThroughAFieldIsTheQueryOverItsEntity(string code)
        => AssertTheSameQuery(Both("annotation", $$"""
            public class CategoryService(ShopContext ctx)
            {
                private readonly ShopContext _ctx = ctx;

                {{code}}
            }
            """));

    [Fact]
    public void ARootNamedBySetOfAContextInTheSameUnitIsTheQueryOverItsEntity()
    {
        var both = (
            Convert(Unit("All.cs", Entities + AnnotatedCategory + Context + Static("public static List<Category> Big(ShopContext ctx) => ctx.Categories" + Chain + ";"))),
            Convert(Unit("All.cs", Entities + AnnotatedCategory + Context + Static("public static List<Category> Big(ShopContext ctx) => ctx.Set<Category>()" + Chain + ";"))));

        AssertTheSameQuery(both);
    }

    [Theory]
    [InlineData("Categories", "Set<Category>()")]
    [InlineData("this.Categories", "this.Set<Category>()")]
    public void ARootNamedBySetInsideTheContextIsTheQueryOverItsEntity(string named, string typed)
    {
        static string Querying(string root) => $$"""
            public class ShopContext : DbContext
            {
                public DbSet<Order> Orders { get; set; } = null!;

                public DbSet<Category> Categories { get; set; } = null!;

                public List<Category> Big() => {{root}}{{Chain}};
            }
            """;

        var both = (
            Convert(Unit("Model.cs", Entities + AnnotatedCategory), Unit("ShopContext.cs", Querying(named))),
            Convert(Unit("Model.cs", Entities + AnnotatedCategory), Unit("ShopContext.cs", Querying(typed))));

        AssertTheSameQuery(both);
    }

    /// <summary>The set named as the inner sequence of a join and as the source of a subquery.</summary>
    [Theory]
    [InlineData("ctx.Orders.Join(ctx.{set}, o => o.CategoryID, c => c.CategoryID, (o, c) => new { o.OrderID, c.Name }).ToList()")]
    [InlineData("ctx.Orders.Where(o => ctx.{set}.Any(c => c.CategoryID == o.CategoryID && c.Name == \"A\")).ToList()")]
    public void ASetNamedInsideTheQueryIsItsEntity(string chain)
        => AssertTheSameQuery(Both("annotation", Static("public static object Big(ShopContext ctx) => " + chain + ";")));

    /// <summary>A set named by its entity with an "s" reads as before: both ways of asking give one answer.</summary>
    [Fact]
    public void ASetNamedByItsEntityWithAnSIsUnchanged()
    {
        var named = Convert([.. Model("annotation"), Unit("Queries.cs", Static("public static List<Order> Big(ShopContext ctx) => ctx.Orders.Where(o => o.Amount > 100).ToList();"))]);
        var typed = Convert([.. Model("annotation"), Unit("Queries.cs", Static("public static List<Order> Big(ShopContext ctx) => ctx.Set<Order>().Where(o => o.Amount > 100).ToList();"))]);

        Assert.Equal(Assert.Single(Queries(typed)), Assert.Single(Queries(named)));
        Assert.Contains("Orders", Assert.Single(Queries(named)), StringComparison.Ordinal);
        Assert.Empty(Failures(named));
    }

    /// <summary>
    /// The stated limit: without the context in the conversion nothing knows what the name exposes,
    /// and the root reads by rule Q2 as before - the name as the table, the result type derived
    /// from it, and the Convention record that says so.
    /// </summary>
    [Fact]
    public void WithoutTheContextTheNameReadsByTheNamingConventionAsBefore()
    {
        var result = Convert(Unit("Model.cs", Entities + AnnotatedCategory), Unit("Queries.cs", Static("public static List<Category> Big(ShopContext ctx) => ctx.Categories" + Chain + ";")));

        var query = Assert.Single(Queries(result));

        Assert.Contains("Categories", query, StringComparison.Ordinal);
        Assert.Contains(result.Records, r => DerivesTheResultType(r) && r.Reason.Contains("'Categorie'", StringComparison.Ordinal));
    }

    /// <summary>Two contexts exposing different entities under one name: no model of EF Core has it, and the name decides nothing.</summary>
    [Fact]
    public void ANameTwoContextsDeclareOverDifferentEntitiesReadsByTheNamingConvention()
    {
        const string model = """
            [Table("Widget")]
            public class Widget
            {
                public int WidgetID { get; set; }
            }

            [Table("Gadget")]
            public class Gadget
            {
                public int GadgetID { get; set; }
            }

            public class FirstContext : DbContext
            {
                public DbSet<Widget> Items { get; set; } = null!;
            }

            public class SecondContext : DbContext
            {
                public DbSet<Gadget> Items { get; set; } = null!;
            }


            """;

        var result = Convert(Unit("Model.cs", model), Unit("Queries.cs", Static("public static List<Widget> Big(FirstContext ctx) => ctx.Items.Where(i => i.WidgetID > 1).ToList();")));

        var query = Assert.Single(Queries(result));

        Assert.Contains("Items", query, StringComparison.Ordinal);
        Assert.Contains(result.Records, r => DerivesTheResultType(r) && r.Reason.Contains("'Item'", StringComparison.Ordinal));
    }
}
