using System.Text.RegularExpressions;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of Sum, measured by its own query pipeline without a connection (the
/// third level of decision 027) - the facts the hook <c>CoalescesSum</c> of the LINQ writer
/// rests on (decision 155): Sum is COALESCE(SUM(x), 0) over a group and in a subquery alike,
/// whatever the type of its element, while SQL's SUM over no values is NULL; the projection of
/// one group is absent over no rows, and NULL there when it is nullable; a count of the values
/// guards the sum within the same aggregation. Max is not coalesced.
/// </summary>
public class EFCoreSumProviderTest
{
    private const string Entities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Summed;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public decimal? CreditLimit { get; set; }
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

    private const string Orders = "ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId)";

    /// <summary>The chains and the SQL the provider makes of each, with whitespace collapsed.</summary>
    public static TheoryData<string, string, string> Translated => new()
    {
        { "Sum in a subquery", $"ctx.Set<Customer>().Where(c => c.CreditLimit > {Orders}.Sum(o => o.Total))", @"> \( SELECT COALESCE\(SUM\(\[o\]\.\[Total\]\), 0\.0\)" },
        { "Sum of a lifted element", $"ctx.Set<Customer>().Where(c => c.CreditLimit > {Orders}.Sum(o => (decimal?)o.Total))", @"> \( SELECT COALESCE\(SUM\(\[o\]\.\[Total\]\), 0\.0\)" },
        { "Sum over a group", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, S = g.Sum(e => e.Rating) })", @"COALESCE\(SUM\(\[o\]\.\[Rating\]\), 0\) AS \[S\]" },
        { "Sum of a lifted element over a group", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, S = g.Sum(e => (decimal?)e.Total) })", @"COALESCE\(SUM\(\[o\]\.\[Total\]\), 0\.0\) AS \[S\]" },
        { "Max in a subquery", $"ctx.Set<Customer>().Where(c => c.CreditLimit > {Orders}.Max(o => o.Total))", @"> \( SELECT MAX\(\[o\]\.\[Total\]\) FROM" },
        { "a value that cannot be null over one group", $"ctx.Set<Customer>().Where(c => c.CreditLimit > {Orders}.GroupBy(o => 1).Select(g => g.Sum(e => e.Total)).FirstOrDefault())", @"> COALESCE\(\( SELECT TOP\(1\)" },
        { "a nullable value over one group", $"ctx.Set<Customer>().Where(c => c.CreditLimit > {Orders}.GroupBy(o => 1).Select(g => (decimal?)g.Sum(e => e.Total)).FirstOrDefault())", @"> \( SELECT TOP\(1\) COALESCE\(SUM\(\[o0\]\.\[Total\]\), 0\.0\) FROM \( SELECT .* \) AS \[o0\] GROUP BY \[o0\]\.\[Key\]\)$" },
        { "a sum guarded by the count of its values", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Select(g => new { g.Key, S = g.Count(e => e.Rating != null) > 0 ? (int?)g.Sum(e => e.Rating) : null })", @"CASE WHEN COUNT\(CASE WHEN \[o\]\.\[Rating\] IS NOT NULL THEN 1 END\) > 0 THEN COALESCE\(SUM\(\[o\]\.\[Rating\]\), 0\) END AS \[S\]" },
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
            "EFCoreSum_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            method,
            [Entities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Summed;");

        return Regex.Replace(EFCoreQueryAcceptance.Translate(compiled), @"\s+", " ").Trim();
    }
}
