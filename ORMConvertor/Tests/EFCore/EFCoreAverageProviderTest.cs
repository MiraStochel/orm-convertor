using System.Text.RegularExpressions;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of Average, measured by its own query pipeline without a connection
/// (the third level of decision 027) - the facts the LINQ writer's conversion of an average and
/// the LINQ reader's reading of Average rest on (decision 161): over a whole number Average is
/// AVG(CAST(x AS float)), the average in floating point, which does not cut the quotient off as
/// SQL's AVG does; cast into int it is CAST(… AS int), which cuts it off again, over a group
/// and in a subquery alike; over a decimal it is SQL's AVG itself.
/// </summary>
public class EFCoreAverageProviderTest
{
    private const string Entities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Averaged;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public int? Score { get; set; }
        }

        [Table("Orders")]
        public class Order
        {
            [Key]
            public int OrderId { get; set; }
            public int CustomerId { get; set; }
            public int? Rating { get; set; }
            public decimal Total { get; set; }
        }
        """;

    /// <summary>The chains and the SQL the provider makes of each, with whitespace collapsed.</summary>
    public static TheoryData<string, string, string> Translated => new()
    {
        { "Average over a whole number", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = g.Average(e => e.OrderId) })", @"AVG\(CAST\(\[o\]\.\[OrderId\] AS float\)\) AS \[A\]" },
        { "Average over a nullable whole number", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = g.Average(e => e.Rating) })", @"AVG\(CAST\(\[o\]\.\[Rating\] AS float\)\) AS \[A\]" },
        { "Average over a decimal", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = g.Average(e => e.Total) })", @"AVG\(\[o\]\.\[Total\]\) AS \[A\]" },
        { "Average cast into int", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = (int)g.Average(e => e.OrderId) })", @"CAST\(AVG\(CAST\(\[o\]\.\[OrderId\] AS float\)\) AS int\) AS \[A\]" },
        { "Average cast into a nullable int", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, A = (int?)g.Average(e => e.Rating) })", @"CAST\(AVG\(CAST\(\[o\]\.\[Rating\] AS float\)\) AS int\) AS \[A\]" },
        { "Average in a subquery cast into a nullable int", "ctx.Set<Customer>().Where(c => c.Score > (int?)ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId).Average(o => o.OrderId))", @"> CAST\(\( SELECT AVG\(CAST\(\[o\]\.\[OrderId\] AS float\)\) FROM \[Orders\] AS \[o\] WHERE \[o\]\.\[CustomerId\] = \[c\]\.\[CustomerId\]\) AS int\)" },
    };

    [Theory]
    [MemberData(nameof(Translated))]
    public void TheProviderTranslates(string shape, string chain, string hallmark)
    {
        var sql = Translate(shape, chain);

        Assert.Matches(hallmark, sql);
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
            "EFCoreAverage_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            method,
            [Entities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Averaged;");

        return Regex.Replace(EFCoreQueryAcceptance.Translate(compiled), @"\s+", " ").Trim();
    }
}
