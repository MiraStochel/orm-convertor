using AbstractWrappers;
using DapperWrappers;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace Tests.Dapper;

public class DapperSQLQueryBuilderTest
{
    [Fact]
    public void SelectWithAllQueryInstructions()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.Project("c", "CustomerName", "Name");
        builder.Project("ord", "Id", function: "COUNT", alias: "OrderCount");
        builder.Project("ord", "TotalPrice", function: "SUM", alias: "TotalSpent");
        builder.From("Sales.Customer", alias: "c");
        builder.Join(JoinKind.Inner, "c", "Sales.Orders",
            new ComparisonCondition("c", "Id", null, null, ComparisonOperator.Equal, "ord", "CustomerId", null, null),
            rightTableAlias: "ord");
        builder.Where(new ComparisonCondition("c", "Id", null, null, ComparisonOperator.NotEqual, null, null, "25", null));
        builder.Where(new ComparisonCondition("ord", "TotalPrice", null, null, ComparisonOperator.GreaterThanOrEqual, "c", "MaxOrderLimit", null, null));
        builder.OrderBy(null, "Name", asc: false);
        builder.OrderBy(null, "TotalSpent", asc: true);
        builder.GroupBy("c", "CustomerName");
        builder.Having(new ComparisonCondition("ord", "TotalPrice", null, "SUM", ComparisonOperator.GreaterThan, null, null, "1000", null));
        builder.Pop();

        var sql = builder.Build().First().Content;

        string expected = """"
        public List<Customer> Query() 
        {
            return connection.Query<Customer>(
                """
                SELECT c.CustomerName AS Name, COUNT(ord.Id) AS OrderCount, SUM(ord.TotalPrice) AS TotalSpent
                FROM Sales.Customer AS c
                INNER JOIN Sales.Orders ord ON c.Id = ord.CustomerId
                WHERE c.Id <> 25 AND ord.TotalPrice >= c.MaxOrderLimit
                GROUP BY c.CustomerName
                HAVING SUM(ord.TotalPrice) > 1000
                ORDER BY Name DESC, TotalSpent ASC
                """,    
            ).ToList();
        }
        """";

        Assert.Equal(expected, sql, ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ConditionTreeWithOrIsNullAndNot()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Where(new LogicalCondition(LogicalOperator.And,
        [
            new LogicalCondition(LogicalOperator.Or,
            [
                new ComparisonCondition("c", "CreditLimit", null, null, ComparisonOperator.GreaterThan, null, null, "2000", null),
                new ComparisonCondition("c", "CreditLimit", null, null, ComparisonOperator.IsNull, null, null, null, null),
            ]),
            new ComparisonCondition("c", "AccountOpenedDate", null, null, ComparisonOperator.GreaterThanOrEqual, null, null, "'2025-01-01'", null),
            new NotCondition(
                new ComparisonCondition("c", "IsOnCreditHold", null, null, ComparisonOperator.Equal, null, null, "1", null)),
        ]));
        builder.Pop();

        var sql = builder.Build().First().Content;

        string expected = """"
        public List<Customer> Query() 
        {
            return connection.Query<Customer>(
                """
                SELECT *
                FROM Sales.Customers AS c
                WHERE (c.CreditLimit > 2000 OR c.CreditLimit IS NULL) AND c.AccountOpenedDate >= '2025-01-01' AND NOT (c.IsOnCreditHold = 1)
                """,    
            ).ToList();
        }
        """";

        Assert.Equal(expected, sql, ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void MultiColumnJoin()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.From("Sales.OrderLines", alias: "ol");
        builder.Join(JoinKind.Left, "ol", "Sales.Orders",
            new LogicalCondition(LogicalOperator.And,
            [
                new ComparisonCondition("ol", "OrderId", null, null, ComparisonOperator.Equal, "o", "OrderId", null, null),
                new ComparisonCondition("ol", "CompanyId", null, null, ComparisonOperator.Equal, "o", "CompanyId", null, null),
            ]),
            rightTableAlias: "o");
        builder.Pop();

        var sql = builder.Build().First().Content;

        string expected = """"
        public List<OrderLine> Query() 
        {
            return connection.Query<OrderLine>(
                """
                SELECT *
                FROM Sales.OrderLines AS ol
                LEFT JOIN Sales.Orders o ON ol.OrderId = o.OrderId AND ol.CompanyId = o.CompanyId
                """,    
            ).ToList();
        }
        """";

        Assert.Equal(expected, sql, ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SetOperationOnTwoSelects()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.Project("c", "CustomerName", "Name");
        builder.From("Sales.Customer", alias: "c");
        builder.Pop();
        builder.SetOperation(SetOperationType.Union);
        builder.Push();
        builder.Project("c", "CustomerName", "Name");
        builder.From("Sales.Customer", alias: "c");
        builder.Pop();

        var sql = builder.Build().First().Content;

        string expected = """"
        public List<Customer> Query() 
        {
            return connection.Query<Customer>(
                """
                SELECT c.CustomerName AS Name
                FROM Sales.Customer AS c
                
                UNION

                SELECT c.CustomerName AS Name
                FROM Sales.Customer AS c
                """,    
            ).ToList();
        }
        """";

        Assert.Equal(expected, sql, ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }
}