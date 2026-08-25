using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;

namespace Tests.Combined;

public class EFCoreLinqToDapperSqlTest
{
    private static string Translate(string linq, params EntityMap[] maps)
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = maps };
        new EFCoreLinqQueryParser(builder).Parse(ConversionContentType.CSharpQuery, linq, maps);

        return builder.Build()
            .Single(s => s.ContentType == ConversionContentType.SqlQuery)
            .Content;
    }

    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

    [Fact]
    public void SimpleLinqToSql()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => c.Id != 25)
                .OrderByDescending(c => c.Name)
                .Select(c => new { Name = c.CustomerName })
                .ToList();
        }
        """;

        string expected = """
        SELECT c.CustomerName AS Name
        FROM Sales.Customers AS c
        WHERE c.Id <> 25
        ORDER BY c.Name DESC
        """;

        Assert.Equal(expected, Translate(linqSource, Customers()), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ConditionTreeLinqToSql()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => (c.CreditLimit > 2000 || c.CreditLimit == null) && c.Name != "Foo")
                .ToList();
        }
        """;

        string expected = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE (c.CreditLimit > 2000 OR c.CreditLimit IS NULL) AND c.Name <> 'Foo'
        """;

        Assert.Equal(expected, Translate(linqSource, Customers()), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void MultiKeyJoinLinqToSql()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.OrderLines
                .Join(ctx.Orders,
                    ol => new { ol.OrderId, ol.CompanyId },
                    o => new { o.OrderId, o.CompanyId },
                    (ol, o) => new { ol.Description })
                .ToList();
        }
        """;

        var map = new EntityMap { Entity = new() { Name = "OrderLine" }, Table = "OrderLines", Schema = "Sales" };

        string expected = """
        SELECT *
        FROM Sales.OrderLines AS o
        INNER JOIN Orders orders ON o.OrderId = orders.OrderId AND o.CompanyId = orders.CompanyId
        """;

        Assert.Equal(expected, Translate(linqSource, map), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// Select(c =&gt; c.Name) is the commonest projection there is and used to produce none at
    /// all, because only anonymous-object bodies were read.
    /// </summary>
    [Fact]
    public void SingleColumnProjectionIsRead()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.Customers.Select(c => c.CustomerName).ToList();
        }
        """;

        Assert.Contains("SELECT c.CustomerName", Translate(linqSource, Customers()));
    }

    /// <summary>Select(c =&gt; c) is rule Q3's default: the whole entity, so no projection.</summary>
    [Fact]
    public void IdentityProjectionMaterialisesTheWholeEntity()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.Customers.Select(c => c).ToList();
        }
        """;

        Assert.Contains("SELECT *", Translate(linqSource, Customers()));
    }

    /// <summary>
    /// The context variable used to be hard-coded as "ctx", so any other name silently
    /// produced nothing at all.
    /// </summary>
    [Fact]
    public void TheContextVariableNeedNotBeCalledCtx()
    {
        const string linqSource = """
        public void Query()
        {
            var q = database.Customers.Where(c => c.Id != 25).ToList();
        }
        """;

        Assert.Contains("FROM Sales.Customers AS c", Translate(linqSource, Customers()));
    }

    /// <summary>ctx.Set&lt;T&gt;() is the root the Advisor's samples already use.</summary>
    [Fact]
    public void TheSetFormOfTheRootIsRead()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.Set<Customer>().Where(c => c.Id != 25).ToList();
        }
        """;

        Assert.Contains("FROM Sales.Customers AS c", Translate(linqSource, Customers()));
    }

    /// <summary>
    /// A decimal literal used to reach SQL with its C# suffix intact, which AdvisorBenchmarking
    /// then stripped downstream (decision 024).
    /// </summary>
    [Fact]
    public void ANumericSuffixDoesNotReachTheSql()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.Customers.Where(c => c.CreditLimit > 2000m).ToList();
        }
        """;

        var sql = Translate(linqSource, Customers());

        Assert.Contains("c.CreditLimit > 2000", sql);
        Assert.DoesNotContain("2000m", sql);
    }

    /// <summary>
    /// An unreadable predicate used to drop the entire filter in silence, which is exactly
    /// what F11 forbids.
    /// </summary>
    [Fact]
    public void AnUnreadablePredicateIsReportedRatherThanDropped()
    {
        const string linqSource = """
        public void Query()
        {
            var q = ctx.Customers.Where(c => c.Name.StartsWith("A")).ToList();
        }
        """;

        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();
        new EFCoreLinqQueryParser(builder).Parse(ConversionContentType.CSharpQuery, linqSource, [Customers()]);
        builder.Build();

        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Filtering);
    }

}
