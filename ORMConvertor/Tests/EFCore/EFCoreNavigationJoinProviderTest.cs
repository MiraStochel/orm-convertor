using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// The join EF Core 10 makes of a navigation in an operand, measured by its own query pipeline
/// without a connection (the third level of decision 027) - the fact the shared LINQ reader's
/// join kinds rest on (decisions 148 and 151): a required reference from a row that cannot be
/// absent is an inner join, an optional reference a left one, and a reference crossed from a
/// row that may be absent a left one too, however required its own foreign key is.
/// </summary>
public class EFCoreNavigationJoinProviderTest
{
    private static string Entities(bool optionalCustomer) => $$"""
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Navigated;

        [Table("Regions")]
        public class Region
        {
            [Key]
            public int RegionId { get; set; }
            public string RegionName { get; set; } = "";
        }

        [Table("Customers")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public string CustomerName { get; set; } = "";
            public int RegionId { get; set; }
            public Region Region { get; set; } = null!;
        }

        [Table("Orders")]
        public class Order
        {
            [Key]
            public int OrderId { get; set; }
            public int{{(optionalCustomer ? "?" : string.Empty)}} CustomerId { get; set; }
            public Customer{{(optionalCustomer ? "?" : string.Empty)}} Customer { get; set; }{{(optionalCustomer ? string.Empty : " = null!;")}}
            public decimal Total { get; set; }
        }
        """;

    private static string Translate(string name, bool optionalCustomer, string chain)
    {
        var method = $$"""
            public static IQueryable Query(DbContext ctx)
            {
                return {{chain}};
            }
            """;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "EFCoreNavigationJoin_" + name,
            method,
            [Entities(optionalCustomer)],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Navigated;");

        return EFCoreQueryAcceptance.Translate(compiled).Replace("\r\n", " ");
    }

    [Fact]
    public void ARequiredChainJoinsInner()
    {
        var sql = Translate("Required", optionalCustomer: false, "ctx.Set<Order>().Select(o => o.Customer.Region.RegionName)");

        Assert.Contains("INNER JOIN [Customers]", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [Regions]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceBehindAnOptionalOneJoinsLeft()
    {
        var sql = Translate("Optional", optionalCustomer: true, "ctx.Set<Order>().Select(o => o.Customer!.Region.RegionName)");

        Assert.Contains("LEFT JOIN [Customers]", sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN [Regions]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INNER JOIN", sql, StringComparison.Ordinal);
    }
}
