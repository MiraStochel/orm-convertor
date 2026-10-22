using System.Text.RegularExpressions;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of a subquery inside the predicate of a correlated SelectMany - the
/// shape the LINQ writer gives a join whose condition goes beyond equalities of columns
/// (decision 113) -, measured by its own query pipeline without a connection (the third level
/// of decision 027): <c>Contains</c> over a chain and <c>Any</c> become <c>IN (SELECT …)</c>
/// and <c>EXISTS</c> inside the join's ON, in an inner join and in a left one over
/// <c>DefaultIfEmpty()</c>. These are the facts decision 162 rests on when it carries a
/// subquery in a join condition.
/// </summary>
public class EFCoreJoinSubqueryProviderTest
{
    private const string Entities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace JoinedSub;

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
        }

        [Table("Orders")]
        public class Order
        {
            [Key]
            public int OrderId { get; set; }
            public int CustomerId { get; set; }
            public int? Rating { get; set; }
        }
        """;

    private const string Rated = "ctx.Set<Order>().Where(i => i.Rating > 3).Select(i => i.CustomerId)";

    /// <summary>The applied set every chain below makes, with the join's key equality first: CROSS APPLY for an inner join, OUTER APPLY over DefaultIfEmpty().</summary>
    private const string Applied = @" APPLY \( SELECT \[o\]\.\[OrderId\] FROM \[Orders\] AS \[o\] WHERE \[o\]\.\[CustomerId\] = \[c\]\.\[CustomerId\] AND ";

    /// <summary>
    /// The chains and the SQL the provider makes of each, with whitespace collapsed. EF Core
    /// 10.0.10 keeps a correlated SelectMany whose predicate holds a subquery as CROSS APPLY,
    /// or OUTER APPLY over DefaultIfEmpty(), with the subquery in the WHERE of the applied
    /// set - the rows of an inner, or a left, join with that ON condition.
    /// </summary>
    public static TheoryData<string, string, string> Translated => new()
    {
        {
            "IN over a subquery in an inner join",
            $"ctx.Set<Customer>().SelectMany(c => ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId && {Rated}.Contains(c.CustomerId)), (c, o) => new {{ c, o }}).Select(t => new {{ t.c.CustomerName, t.o.OrderId }})",
            "CROSS" + Applied + @"\[c\]\.\[CustomerId\] IN \( SELECT \[o0\]\.\[CustomerId\] FROM \[Orders\] AS \[o0\] WHERE \[o0\]\.\[Rating\] > 3 \) \) AS \[o1\]"
        },
        {
            "NOT IN over a subquery in an inner join",
            $"ctx.Set<Customer>().SelectMany(c => ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId && !{Rated}.Contains(c.CustomerId)), (c, o) => new {{ c, o }}).Select(t => new {{ t.c.CustomerName, t.o.OrderId }})",
            "CROSS" + Applied + @"\[c\]\.\[CustomerId\] NOT IN \( SELECT \[o0\]\.\[CustomerId\] FROM \[Orders\] AS \[o0\] WHERE \[o0\]\.\[Rating\] > 3 \) \) AS \[o1\]"
        },
        {
            "a correlated subquery in a left join",
            "ctx.Set<Customer>().SelectMany(c => ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId && ctx.Set<Order>().Where(i => i.CustomerId == c.CustomerId && i.Rating > 3).Select(i => i.CustomerId).Contains(c.CustomerId)).DefaultIfEmpty(), (c, o) => new { c, o }).Select(t => new { t.c.CustomerName, OrderId = (int?)t.o.OrderId })",
            "OUTER" + Applied + @"\[c\]\.\[CustomerId\] IN \( SELECT \[o0\]\.\[CustomerId\] FROM \[Orders\] AS \[o0\] WHERE \[o0\]\.\[CustomerId\] = \[c\]\.\[CustomerId\] AND \[o0\]\.\[Rating\] > 3 \) \) AS \[o1\]"
        },
        {
            "EXISTS in an inner join",
            "ctx.Set<Customer>().SelectMany(c => ctx.Set<Order>().Where(o => o.CustomerId == c.CustomerId && ctx.Set<Order>().Where(i => i.CustomerId == c.CustomerId && i.Rating > 3).Any()), (c, o) => new { c, o }).Select(t => new { t.c.CustomerName, t.o.OrderId })",
            "CROSS" + Applied + @"EXISTS \( SELECT 1 FROM \[Orders\] AS \[o0\] WHERE \[o0\]\.\[CustomerId\] = \[c\]\.\[CustomerId\] AND \[o0\]\.\[Rating\] > 3\) \) AS \[o1\]"
        },
    };

    [Theory]
    [MemberData(nameof(Translated))]
    public void TheProviderTranslates(string shape, string chain, string hallmark)
    {
        var sql = Translate(shape, chain);

        Assert.True(Regex.IsMatch(sql, hallmark), sql);
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
            "EFCoreJoinSubquery_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            method,
            [Entities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using JoinedSub;");

        return Regex.Replace(EFCoreQueryAcceptance.Translate(compiled), @"\s+", " ").Trim();
    }
}
