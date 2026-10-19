using System.Text.RegularExpressions;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of a predicate over a value that may be NULL, measured by its own
/// query pipeline without a connection (the third level of decision 027) - the facts the LINQ
/// writer and reader of decision 156 rest on: the provider carries C#'s two-valued logic into
/// the SQL it writes, compensating ==, != and every negation, and Contains over a subquery
/// whose value may be NULL; the relational operators and the keys of a join it writes as SQL
/// does; and the shapes the writer emits instead hold exactly where SQL's condition is true.
/// </summary>
public class EFCoreNullSemanticsProviderTest
{
    private const string Entities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Nulls;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public string? Name { get; set; }
            public int? Score { get; set; }
            public int? Rank { get; set; }
            public int Level { get; set; }
        }

        [Table("Orders")]
        public class Order
        {
            [Key]
            public int OrderId { get; set; }
            public int? Rating { get; set; }
            public decimal Total { get; set; }
        }
        """;

    /// <summary>What the provider writes of C#'s predicate, with whitespace collapsed.</summary>
    public static TheoryData<string, string, string> Compensated => new()
    {
        { "inequality with a constant", "c => c.Score != 5", @"WHERE \[c\]\.\[Score\] <> 5 OR \[c\]\.\[Score\] IS NULL$" },
        { "equality of two nullable values", "c => c.Score == c.Rank", @"WHERE \[c\]\.\[Score\] = \[c\]\.\[Rank\] OR \(\[c\]\.\[Score\] IS NULL AND \[c\]\.\[Rank\] IS NULL\)$" },
        { "inequality of two nullable values", "c => c.Score != c.Rank", @"\(\[c\]\.\[Score\] IS NOT NULL OR \[c\]\.\[Rank\] IS NOT NULL\)$" },
        { "equality with a subquery over a value that cannot be null", "c => c.Score == ctx.Set<Order>().Max(o => o.Total)", @"OR \(\[c\]\.\[Score\] IS NULL AND \( SELECT MAX\(\[o\]\.\[Total\]\) FROM \[Orders\] AS \[o\]\) IS NULL\)$" },
        { "negated equality", "c => !(c.Score == 5)", @"WHERE \[c\]\.\[Score\] <> 5 OR \[c\]\.\[Score\] IS NULL$" },
        { "negated relational comparison", "c => !(c.Score > 5)", @"WHERE CASE WHEN \[c\]\.\[Score\] > 5 THEN CAST\(0 AS bit\) ELSE CAST\(1 AS bit\) END = CAST\(1 AS bit\)$" },
        { "negated IN", "c => !new int?[] { 1, 2 }.Contains(c.Score)", @"WHERE \[c\]\.\[Score\] NOT IN \(1, 2\) OR \[c\]\.\[Score\] IS NULL$" },
        { "negated LIKE", "c => !c.Name.StartsWith(\"A\")", @"WHERE \[c\]\.\[Name\] NOT LIKE N'A%' OR \[c\]\.\[Name\] IS NULL$" },
        { "Contains over a nullable subquery", "c => ctx.Set<Order>().Select(o => o.Rating).Contains(c.Score)", @"WHERE EXISTS \( SELECT 1 FROM \[Orders\] AS \[o\] WHERE \[o\]\.\[Rating\] = \[c\]\.\[Score\] OR \(\[o\]\.\[Rating\] IS NULL AND \[c\]\.\[Score\] IS NULL\)\)$" },
        { "relational comparison", "c => c.Score <= 5", @"WHERE \[c\]\.\[Score\] <= 5$" },
        { "equality with a constant", "c => c.Score == 5", @"WHERE \[c\]\.\[Score\] = 5$" },
    };

    [Theory]
    [MemberData(nameof(Compensated))]
    public void TheProviderWritesCSharpLogic(string shape, string predicate, string hallmark)
    {
        Assert.Matches(hallmark, Translate(shape, $"ctx.Set<Customer>().Where({predicate})"));
    }

    /// <summary>The shapes the writer of decision 156 emits, which hold exactly where SQL's condition is true.</summary>
    public static TheoryData<string, string, string> Written => new()
    {
        { "inequality tested for NULL", "c => c.Score != 5 && c.Score != null", @"WHERE \[c\]\.\[Score\] IS NOT NULL AND \[c\]\.\[Score\] <> 5$" },
        { "equality tested for NULL", "c => c.Score == c.Rank && c.Score != null", @"AND \[c\]\.\[Score\] IS NOT NULL$" },
        { "negated IN tested for NULL", "c => !new int?[] { 1, 2 }.Contains(c.Score) && c.Score != null", @"WHERE \[c\]\.\[Score\] NOT IN \(1, 2\) AND \[c\]\.\[Score\] IS NOT NULL$" },
        { "NOT IN over a nullable subquery", "c => c.Score != null && ctx.Set<Order>().Select(o => o.Rating).All(v => v != null && v != c.Score)", @"WHERE \[c\]\.\[Score\] IS NOT NULL AND NOT EXISTS \( SELECT 1 FROM \[Orders\] AS \[o\] WHERE \[o\]\.\[Rating\] IS NULL OR \[o\]\.\[Rating\] = \[c\]\.\[Score\]\)$" },
        { "a lifted subquery tested for NULL", "c => (decimal?)ctx.Set<Order>().Max(o => o.Total) != null", @"WHERE \( SELECT MAX\(\[o\]\.\[Total\]\) FROM \[Orders\] AS \[o\]\) IS NOT NULL$" },
    };

    [Theory]
    [MemberData(nameof(Written))]
    public void TheWrittenShapesHoldWhereSqlHolds(string shape, string predicate, string hallmark)
    {
        Assert.Matches(hallmark, Translate(shape, $"ctx.Set<Customer>().Where({predicate})"));
    }

    /// <summary>The keys of a join are compared as SQL compares them.</summary>
    [Fact]
    public void TheKeysOfAJoinAreNotCompensated()
    {
        var sql = Translate("join", "ctx.Set<Customer>().Join(ctx.Set<Order>(), c => c.Score, o => o.Rating, (c, o) => new { c.CustomerId, o.OrderId })");

        Assert.Contains("INNER JOIN [Orders] AS [o] ON [c].[Score] = [o].[Rating]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("IS NULL", sql, StringComparison.Ordinal);
    }

    private static string Translate(string shape, string chain)
    {
        var method = $$"""
            public static IQueryable Query(DbContext ctx)
            {
                return {{chain}};
            }
            """;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "EFCoreNullSemantics_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            method,
            [Entities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Nulls;");

        return Regex.Replace(EFCoreQueryAcceptance.Translate(compiled), @"\s+", " ").Trim();
    }
}
