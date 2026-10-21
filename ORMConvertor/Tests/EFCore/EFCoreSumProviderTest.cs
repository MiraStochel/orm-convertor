using System.Text.RegularExpressions;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of Sum, measured by its own query pipeline without a connection (the
/// third level of decision 027) - the facts the hook <c>CoalescesSum</c> of the LINQ writer
/// rests on (decision 155): Sum is COALESCE(SUM(x), 0) over a group and in a subquery alike,
/// whatever the type of its element, while SQL's SUM over no values is NULL; the projection of
/// one group is absent over no rows, and NULL there when it is nullable; a count of the values
/// guards the sum within the same aggregation. Max is not coalesced. The projection of one
/// group taken by FirstOrDefault() is TOP(1), taken by Max() a MAX over the derived table of
/// the group, without TOP and without COALESCE in every position - the facts decision 159
/// rests on: SQL Server decorrelates the second form and not the first (measured over FIBEN on
/// 2026-10-21, the record in the decision).
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
        { "the maximum over one group in a predicate", $"ctx.Set<Customer>().Where(c => c.CreditLimit > {Orders}.GroupBy(o => 1).Select(g => (decimal?)g.Sum(e => e.Total)).Max())", @"WHERE \[c\]\.\[CreditLimit\] > \( SELECT MAX\(\[o1\]\.\[c\]\) FROM \( SELECT COALESCE\(SUM\(\[o0\]\.\[Total\]\), 0\.0\) AS \[c\] FROM \( SELECT .* \) AS \[o0\] GROUP BY \[o0\]\.\[Key\] \) AS \[o1\]\)$" },
        { "the maximum over one guarded group", $"ctx.Set<Customer>().Where(c => c.Score > {Orders}.GroupBy(o => 1).Select(g => g.Count(e => e.Rating != null) > 0 ? (int?)g.Sum(e => e.Rating) : null).Max())", @"> \( SELECT MAX\(\[o1\]\.\[c\]\) FROM \( SELECT CASE WHEN COUNT\(CASE WHEN \[o0\]\.\[Rating\] IS NOT NULL THEN 1 END\) > 0 THEN COALESCE\(SUM\(\[o0\]\.\[Rating\]\), 0\) END AS \[c\] FROM" },
        { "the maximum over one group in a projection", $"ctx.Set<Customer>().Select(c => new {{ c.CustomerId, S = {Orders}.GroupBy(o => 1).Select(g => (decimal?)g.Sum(e => e.Total)).Max() }})", @"^SELECT \[c\]\.\[CustomerId\], \( SELECT MAX\(\[o1\]\.\[c\]\) FROM .* GROUP BY \[o0\]\.\[Key\] \) AS \[o1\]\) AS \[S\] FROM \[Customers\] AS \[c\]$" },
        { "the maximum over one group in having", "ctx.Set<Order>().GroupBy(o => o.CustomerId).Where(g => g.Sum(e => e.Total) > ctx.Set<Order>().Where(i => i.CustomerId == g.Key && i.Rating > 3).GroupBy(i => 1).Select(h => (decimal?)h.Sum(e => e.Total)).Max()).Select(g => g.Key)", @"HAVING COALESCE\(SUM\(\[o\]\.\[Total\]\), 0\.0\) > \( SELECT MAX\(\[o2\]\.\[c\]\) FROM \( SELECT COALESCE\(SUM\(\[o1\]\.\[Total\]\), 0\.0\) AS \[c\] FROM .* GROUP BY \[o1\]\.\[Key\] \) AS \[o2\]\)$" },
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
