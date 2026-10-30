using System.Text.RegularExpressions;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 makes of a SelectMany over a second query root - the shape the LINQ writer
/// gives a cross join (decision 196) -, measured by its own query pipeline without a
/// connection (the third level of decision 027): SQL's CROSS JOIN, the condition of the Where
/// after it left in WHERE, at the top of a query and inside a subquery. A fact the release
/// stops holding on an upgrade shows up here, beside the hook that assumes it.
/// </summary>
public class EFCoreCrossJoinProviderTest
{
    private const string Entities = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Crossed;

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
        }
        """;

    /// <summary>The chains and the SQL EF Core 10.0.10 makes of each, with whitespace collapsed.</summary>
    public static TheoryData<string, string, string> Translated => new()
    {
        {
            "SelectMany over a second root with the condition in Where",
            "ctx.Set<Customer>().SelectMany(c => ctx.Set<Order>(), (c, o) => new { c, o }).Where(t => t.o.CustomerId == t.c.CustomerId).Select(t => new { t.c.CustomerName, t.o.OrderId })",
            @"FROM \[Customers\] AS \[c\] CROSS JOIN \[Orders\] AS \[o\] WHERE \[o\]\.\[CustomerId\] = \[c\]\.\[CustomerId\]"
        },
        {
            "SelectMany over a second root without a condition",
            "ctx.Set<Customer>().SelectMany(c => ctx.Set<Order>(), (c, o) => new { c, o }).Select(t => new { t.c.CustomerName, t.o.OrderId })",
            @"FROM \[Customers\] AS \[c\] CROSS JOIN \[Orders\] AS \[o\]$"
        },
        {
            "SelectMany over a second root inside a subquery",
            "ctx.Set<Customer>().Where(c => ctx.Set<Order>().SelectMany(o => ctx.Set<Customer>(), (o, c2) => new { o, c2 }).Where(t => t.c2.CustomerId == t.o.CustomerId && t.c2.CustomerName == \"x\").Select(t => t.o.CustomerId).Contains(c.CustomerId))",
            @"IN \( SELECT \[o\]\.\[CustomerId\] FROM \[Orders\] AS \[o\] CROSS JOIN \[Customers\] AS \[c0\] WHERE \[c0\]\.\[CustomerId\] = \[o\]\.\[CustomerId\]"
        },
        {
            "a SelectMany over a second root after a Join",
            "ctx.Set<Customer>().Join(ctx.Set<Order>(), c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o }).SelectMany(t => ctx.Set<Customer>(), (t, c2) => new { t.c, t.o, c2 }).Where(t => t.c2.CustomerId == t.o.CustomerId).Select(t => new { t.c.CustomerName })",
            @"INNER JOIN \[Orders\] AS \[o\] ON \[c\]\.\[CustomerId\] = \[o\]\.\[CustomerId\] CROSS JOIN \[Customers\] AS \[c0\] WHERE"
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
            "EFCoreCrossJoin_" + string.Concat(shape.Where(char.IsLetterOrDigit)),
            method,
            [Entities],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Crossed;");

        return Regex.Replace(EFCoreQueryAcceptance.Translate(compiled), @"\s+", " ").Trim();
    }
}
