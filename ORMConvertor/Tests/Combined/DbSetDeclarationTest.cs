using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A property declared DbSet&lt;T&gt; declares a set of the context whatever supplies its value
/// (decision 158; measured in <c>EFCore/EFCoreDbSetDeclarationProviderTest</c>): a root alone that
/// is its value - the expression body, the get, the assignment in the constructor - hands no
/// query over, and the table is named after the property in every spelling. The queries of the
/// unit are the chains over the set, numbered as if the sets were auto-properties. Every set
/// written as an expression used to come out as a query over the whole table.
/// </summary>
public class DbSetDeclarationTest
{
    private const string Entities = """
        public class Customer
        {
            public int CustomerID { get; set; }

            public decimal CreditLimit { get; set; }
        }
        """;

    private static ConversionResult Convert(ORMEnum target, string context)
        => ConversionHandler.Convert(ORMEnum.EFCore, target, [new() { ContentType = ConversionContentType.CSharp, Content = Entities + "\n\n" + context, Name = "Shop.cs" }]);

    private static List<string> Artifacts(ConversionResult result, ConversionContentType type)
        => [.. result.Sources.Where(s => s.ContentType == type).Select(s => s.Content)];

    public static TheoryData<string> Spellings => new()
    {
        "public DbSet<Customer> Rich => Set<Customer>();",
        "public DbSet<Customer> Rich => this.Set<Customer>();",
        "public DbSet<Customer> Rich { get => Set<Customer>(); }",
        "public DbSet<Customer> Rich { get { return Set<Customer>(); } }",
        "private DbSet<Customer>? rich;\n\n    public DbSet<Customer> Rich => rich ??= Set<Customer>();",
        "public DbSet<Customer> Rich => (base.Set<Customer>())!;",
        "public ShopContext() { Rich = Set<Customer>(); }\n\n    public DbSet<Customer> Rich { get; }",
        "public ShopContext() { this.Rich = Set<Customer>(); }\n\n    public DbSet<Customer> Rich { get; }",
    };

    [Theory]
    [MemberData(nameof(Spellings))]
    public void TheValueOfASetIsNoQueryAndTheChainOverItKeepsItsName(string set)
    {
        var result = Convert(ORMEnum.Dapper, $$"""
            public class ShopContext : DbContext
            {
                {{set}}

                public List<Customer> Over() => Rich.Where(c => c.CreditLimit > 2000).ToList();
            }
            """);

        var query = Assert.Single(Artifacts(result, ConversionContentType.CSharpQuery));
        Assert.Contains("public static List<Customer> Query(", query, StringComparison.Ordinal);
        Assert.Contains("FROM Rich AS c", query, StringComparison.Ordinal);
        Assert.Contains("2000", query, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void TheTableIsNamedAfterTheSetInEverySpelling(string set)
    {
        var result = Convert(ORMEnum.EFCore, $$"""
            public class ShopContext : DbContext
            {
                {{set}}
            }
            """);

        var entity = Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains("[Table(\"Rich\")]", entity, StringComparison.Ordinal);
        Assert.Empty(Artifacts(result, ConversionContentType.CSharpQuery));
    }

    /// <summary>
    /// The rule is about the value of the set, not about a root alone: enumerating Set&lt;T&gt;()
    /// in a loop, returning it as a query, and keeping it in a field the code does not continue
    /// hand the whole table over, as decisions 109 and 114 read them, and so does a chain the
    /// get of a set runs besides returning it.
    /// </summary>
    [Theory]
    [InlineData("public decimal Sum() { decimal total = 0; foreach (var c in Set<Customer>()) { total += c.CreditLimit; } return total; }")]
    [InlineData("public IQueryable<Customer> All() => Set<Customer>();")]
    [InlineData("private readonly DbSet<Customer> all;\n\n    public ShopContext() { all = Set<Customer>(); }")]
    [InlineData("public DbSet<Customer> Rich { get { Log(Set<Customer>().Where(c => c.CreditLimit > 2000).ToList()); return Set<Customer>(); } }")]
    public void ARootAloneElsewhereStaysAQuery(string code)
    {
        var result = Convert(ORMEnum.Dapper, $$"""
            public class ShopContext : DbContext
            {
                public DbSet<Customer> Customers { get; set; } = null!;

                {{code}}
            }
            """);

        Assert.Single(Artifacts(result, ConversionContentType.CSharpQuery));
    }
}
