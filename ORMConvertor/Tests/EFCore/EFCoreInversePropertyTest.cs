using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// Two navigations of one class to the same entity - the ancestor and the descendant of a
/// closure row - are more candidates than EF Core's convention pairs: it pairs a navigation
/// with the only one pointing back, and with two refuses the model ("Unable to determine the
/// relationship represented by navigation …", the EF Core row of the Cofoundry example in the
/// host check of 2026-11-05). The builder names the inverse where the source stated it, by
/// the annotation the parser reads; with one navigation nothing is written and the convention
/// finds the same pair. EF Core 10 builds the model over the artifact, which is the measurement.
/// </summary>
public class EFCoreInversePropertyTest
{
    private const string Closure = """
        using Microsoft.EntityFrameworkCore;

        namespace Tree;

        public class Node
        {
            public int NodeId { get; set; }
            public string Name { get; set; } = "";
            public ICollection<Closure> Ancestors { get; set; } = new List<Closure>();
            public ICollection<Closure> Descendants { get; set; } = new List<Closure>();
        }

        public class Closure
        {
            public int AncestorId { get; set; }
            public int DescendantId { get; set; }
            public int Distance { get; set; }
            public Node Ancestor { get; set; } = null!;
            public Node Descendant { get; set; } = null!;
        }

        public class TreeContext : DbContext
        {
            public DbSet<Node> Nodes { get; set; }
            public DbSet<Closure> Closures { get; set; }

            protected override void OnModelCreating(ModelBuilder builder)
            {
                builder.Entity<Closure>().HasKey(c => new { c.AncestorId, c.DescendantId });
                builder.Entity<Closure>().HasOne(c => c.Ancestor).WithMany(n => n.Descendants).HasForeignKey(c => c.AncestorId);
                builder.Entity<Closure>().HasOne(c => c.Descendant).WithMany(n => n.Ancestors).HasForeignKey(c => c.DescendantId);
            }
        }

        public class Reader(TreeContext ctx)
        {
            public List<Closure> Above(int id) => ctx.Closures.Where(c => c.DescendantId == id).ToList();
        }
        """;

    private const string OneNavigation = """
        using Microsoft.EntityFrameworkCore;

        namespace Shop;

        public class Customer
        {
            public int CustomerId { get; set; }
            public ICollection<Order> Orders { get; set; } = new List<Order>();
        }

        public class Order
        {
            public int OrderId { get; set; }
            public int CustomerId { get; set; }
            public Customer Customer { get; set; } = null!;
        }

        public class ShopContext : DbContext
        {
            public DbSet<Customer> Customers { get; set; }
            public DbSet<Order> Orders { get; set; }

            protected override void OnModelCreating(ModelBuilder builder)
            {
                builder.Entity<Order>().HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
            }
        }
        """;

    private static bool Annotated(string entity, string inverse, string navigation)
        => Regex.IsMatch(entity, $@"\[InverseProperty\(""{inverse}""\)\](\s*\[[^\]]*\])*\s*public[^\n]* {navigation} \{{");

    [Fact]
    public void EachOfTwoNavigationsToOneEntityNamesItsInverseAndEFCoreBuildsTheModel()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.EFCore,
            [new() { ContentType = ConversionContentType.CSharp, Content = Closure }]);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        var entities = result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content).ToList();
        var node = Assert.Single(entities, e => e.Contains("class Node"));
        var closure = Assert.Single(entities, e => e.Contains("class Closure"));

        Assert.True(Annotated(node, "Descendant", "Ancestors"), node);
        Assert.True(Annotated(node, "Ancestor", "Descendants"), node);
        Assert.True(Annotated(closure, "Descendants", "Ancestor"), closure);
        Assert.True(Annotated(closure, "Ancestors", "Descendant"), closure);

        var query = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "EFCoreInverseProperty",
            query,
            entities,
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Tree;");

        Assert.Contains("Closure", EFCoreQueryAcceptance.Translate(compiled));
    }

    [Fact]
    public void AClassWithOneNavigationToAnEntityNamesNoInverse()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.EFCore,
            [new() { ContentType = ConversionContentType.CSharp, Content = OneNavigation }]);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        foreach (var entity in result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity))
        {
            Assert.DoesNotContain("[InverseProperty", entity.Content);
        }
    }
}
