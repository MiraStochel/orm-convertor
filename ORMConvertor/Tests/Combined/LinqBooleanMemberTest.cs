using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A boolean member that stands as a predicate by itself - <c>Where(p =&gt; p.Featured)</c>,
/// <c>!p.Discontinued</c>, an operand of <c>&amp;&amp;</c> inside a subquery or a condition
/// of the conditional operator. C# takes only a <c>bool</c> in that place and refuses a
/// <c>bool?</c>, so the member is a truth value that is never null, and the LINQ reader
/// takes it as the comparison with <c>true</c> - its negation with <c>false</c> - which is
/// what the provider translates it to and what <c>== true</c> and <c>== false</c> already
/// read as. Until it was read, the predicate around it was refused (decision 070), so the
/// most common LINQ filter over a flag had no translation at all.
/// </summary>
public class LinqBooleanMemberTest
{
    private static List<EntityMap> Maps()
    {
        var productId = new Property { Name = "ProductId", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "Name", Type = LangType.Scalar(ScalarType.String) };
        var discontinued = new Property { Name = "Discontinued", Type = LangType.Scalar(ScalarType.Bool) };
        var featured = new Property { Name = "Featured", Type = LangType.Scalar(ScalarType.Bool) };

        var products = new EntityMap
        {
            Entity = new Entity { Name = "Product", Properties = [productId, name, discontinued, featured] },
            Table = "Products",
            PropertyMaps =
            [
                new PropertyMap { Property = productId, ColumnName = "ProductId" },
                new PropertyMap { Property = name, ColumnName = "Name" },
                new PropertyMap { Property = discontinued, ColumnName = "IsDiscontinued" },
                new PropertyMap { Property = featured, ColumnName = "Featured" },
            ],
        };

        var lineId = new Property { Name = "LineId", Type = LangType.Scalar(ScalarType.Int) };
        var lineProduct = new Property { Name = "ProductId", Type = LangType.Scalar(ScalarType.Int) };
        var paid = new Property { Name = "IsPaid", Type = LangType.Scalar(ScalarType.Bool) };

        var lines = new EntityMap
        {
            Entity = new Entity { Name = "OrderLine", Properties = [lineId, lineProduct, paid] },
            Table = "OrderLines",
            PropertyMaps =
            [
                new PropertyMap { Property = lineId, ColumnName = "LineId" },
                new PropertyMap { Property = lineProduct, ColumnName = "ProductId" },
                new PropertyMap { Property = paid, ColumnName = "IsPaid" },
            ],
        };

        return [products, lines];
    }

    private const string EFCoreEntities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace BooleanMemberDomain;

        [Table("Products")]
        public class Product
        {
            [Key]
            public int ProductId { get; set; }

            public string Name { get; set; } = "";

            [Column("IsDiscontinued")]
            public bool Discontinued { get; set; }

            public bool Featured { get; set; }
        }

        [Table("OrderLines")]
        public class OrderLine
        {
            [Key]
            public int LineId { get; set; }

            public int ProductId { get; set; }

            public bool IsPaid { get; set; }
        }
        """;

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string chain)
    {
        builder.EntityMaps = Maps();
        var source = $$"""
            public void Query()
            {
                var q = {{chain}}.ToList();
            }
            """;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, Maps());
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, string chain, ConversionContentType type)
    {
        var outputs = Parse(builder, chain).Build();
        Assert.True(
            !builder.Records.Any(r => r.Kind == ConversionRecordKind.Failure),
            string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        return outputs.First(s => s.ContentType == type).Content;
    }

    private const string FlagsAlone = "ctx.Products.Where(p => !p.Discontinued && p.Featured)";

    /// <summary>A flag and a negated flag: the comparisons with true and false, under the column the mapping names.</summary>
    [Fact]
    public void AFlagAndANegatedFlagAreTheComparisonsWithTrueAndFalse()
    {
        Assert.Contains("p.IsDiscontinued = 0 AND p.Featured = 1", Artifact(new DapperSqlQueryBuilder(), FlagsAlone, ConversionContentType.SqlQuery), StringComparison.Ordinal);
        Assert.Contains("p.Discontinued = false and p.Featured = true", Artifact(new NHibernateHqlQueryBuilder(), FlagsAlone, ConversionContentType.HqlQuery), StringComparison.Ordinal);
        Assert.Contains("p.Discontinued = false and p.Featured = true", Artifact(new HibernateJpqlQueryBuilder(), FlagsAlone, ConversionContentType.JpqlQuery), StringComparison.Ordinal);
    }

    /// <summary>
    /// The flag inside a correlated subquery and as the condition of the conditional operator,
    /// back into EF Core: the method compiles and EF Core 10 translates it (the third level,
    /// decision 027).
    /// </summary>
    [Fact]
    public void AFlagInASubqueryAndInAConditionalTranslatesBackIntoEFCore()
    {
        const string chain = """
            ctx.Products
                .Where(p => p.Featured && ctx.OrderLines.Any(l => l.ProductId == p.ProductId && !l.IsPaid))
                .Select(p => new { p.Name, Label = p.Discontinued ? "gone" : "on sale" })
            """;

        var method = Artifact(new EFCoreLinqQueryBuilder(), chain, ConversionContentType.CSharpQuery);
        Assert.Contains("l.IsPaid == false", method, StringComparison.Ordinal);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            nameof(AFlagInASubqueryAndInAConditionalTranslatesBackIntoEFCore),
            method,
            [EFCoreEntities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore;\nusing BooleanMemberDomain;");

        var sql = EFCoreQueryAcceptance.Translate(compiled);
        Assert.Contains("EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[IsDiscontinued]", sql, StringComparison.Ordinal);
    }

    /// <summary>A bare value from the scope in the same place is no column, and stays unread: the reading carries columns only.</summary>
    [Fact]
    public void AValueFromTheScopeAsAPredicateStaysUnread()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), "ctx.Products.Where(p => settings.OnlyFeatured)");

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }
}
