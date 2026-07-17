using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Model.AbstractRepresentation;

namespace Tests.Combined;

public class EFCoreLinqToDapperSqlTest
{
    [Fact]
    public void SimpleLinqToSql()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        var mockEntityMap = new EntityMap() { Entity = new(), Table = "Customers", Schema = "Sales" };

        var parser = new EFCoreLinqQueryParser(builder);

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

        parser.Parse(linqSource, new List<EntityMap> { mockEntityMap });
        string sql = builder.Build().First().Content;

        string expected = """"
        public List<Customer> Query() 
        {
            return connection.Query<Customer>(
                """
                SELECT c.CustomerName AS Name
                FROM Sales.Customers AS c
                WHERE c.Id <> 25
                ORDER BY c.Name DESC
                """,    
            ).ToList();
        }
        """";

        Assert.Equal(expected, sql, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ConditionTreeLinqToSql()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        var mockEntityMap = new EntityMap() { Entity = new(), Table = "Customers", Schema = "Sales" };

        var parser = new EFCoreLinqQueryParser(builder);

        const string linqSource = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => (c.CreditLimit > 2000 || c.CreditLimit == null) && c.Name != "Foo")
                .ToList();
        }
        """;

        parser.Parse(linqSource, new List<EntityMap> { mockEntityMap });
        string sql = builder.Build().First().Content;

        string expected = """"
        public List<Customer> Query() 
        {
            return connection.Query<Customer>(
                """
                SELECT *
                FROM Sales.Customers AS c
                WHERE (c.CreditLimit > 2000 OR c.CreditLimit IS NULL) AND c.Name <> 'Foo'
                """,    
            ).ToList();
        }
        """";

        Assert.Equal(expected, sql, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void MultiKeyJoinLinqToSql()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        var mockEntityMap = new EntityMap() { Entity = new(), Table = "OrderLines", Schema = "Sales" };

        var parser = new EFCoreLinqQueryParser(builder);

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

        parser.Parse(linqSource, new List<EntityMap> { mockEntityMap });
        string sql = builder.Build().First().Content;

        string expected = """"
        public List<OrderLine> Query() 
        {
            return connection.Query<OrderLine>(
                """
                SELECT *
                FROM Sales.OrderLines AS o
                INNER JOIN Orders orders ON o.OrderId = orders.OrderId AND o.CompanyId = orders.CompanyId
                """,    
            ).ToList();
        }
        """";

        Assert.Equal(expected, sql, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }
}