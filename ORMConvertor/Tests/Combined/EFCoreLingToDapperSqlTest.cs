using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Model.AbstractRepresentation;

namespace Tests.Combined;

public class EFCoreLingToDapperSqlTest
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

        parser.Parse(linqSource, new List<EntityMap>{ mockEntityMap });
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
}
