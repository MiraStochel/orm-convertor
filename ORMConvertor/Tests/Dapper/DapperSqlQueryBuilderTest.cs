using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace Tests.Dapper;

public class DapperSqlQueryBuilderTest
{
    private static QueryOperand Col(string table, string property, string? function = null)
        => QueryOperand.Column(table, property, function);

    private static QueryOperand Int(int value)
        => QueryOperand.Value(QueryConstant.Of(value.ToString(), ScalarType.Int));

    private static string Sql(AbstractQueryBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

    private static string Method(AbstractQueryBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

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
            new ComparisonCondition(Col("c", "Id"), ComparisonOperator.Equal, Col("ord", "CustomerId")),
            rightTableAlias: "ord");
        builder.Where(new ComparisonCondition(Col("c", "Id"), ComparisonOperator.NotEqual, Int(25)));
        builder.Where(new ComparisonCondition(Col("ord", "TotalPrice"), ComparisonOperator.GreaterThanOrEqual, Col("c", "MaxOrderLimit")));
        builder.OrderBy(null, "Name", asc: false);
        builder.OrderBy(null, "TotalSpent", asc: true);
        builder.GroupBy("c", "CustomerName");
        builder.Having(new ComparisonCondition(Col("ord", "TotalPrice", "SUM"), ComparisonOperator.GreaterThan, Int(1000)));
        builder.Pop();

        string expected = """
        SELECT c.CustomerName AS Name, COUNT(ord.Id) AS OrderCount, SUM(ord.TotalPrice) AS TotalSpent
        FROM Sales.Customer AS c
        INNER JOIN Sales.Orders ord ON c.Id = ord.CustomerId
        WHERE c.Id <> 25 AND ord.TotalPrice >= c.MaxOrderLimit
        GROUP BY c.CustomerName
        HAVING SUM(ord.TotalPrice) > 1000
        ORDER BY Name DESC, TotalSpent ASC
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
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
                new ComparisonCondition(Col("c", "CreditLimit"), ComparisonOperator.GreaterThan, Int(2000)),
                new ComparisonCondition(Col("c", "CreditLimit"), ComparisonOperator.IsNull),
            ]),
            new ComparisonCondition(
                Col("c", "AccountOpenedDate"),
                ComparisonOperator.GreaterThanOrEqual,
                QueryOperand.Value(QueryConstant.Of("2025-01-01", ScalarType.DateTime))),
            new NotCondition(
                new ComparisonCondition(Col("c", "IsOnCreditHold"), ComparisonOperator.Equal, Int(1))),
        ]));
        builder.Pop();

        string expected = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE (c.CreditLimit > 2000 OR c.CreditLimit IS NULL) AND c.AccountOpenedDate >= '2025-01-01' AND NOT (c.IsOnCreditHold = 1)
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
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
                new ComparisonCondition(Col("ol", "OrderId"), ComparisonOperator.Equal, Col("o", "OrderId")),
                new ComparisonCondition(Col("ol", "CompanyId"), ComparisonOperator.Equal, Col("o", "CompanyId")),
            ]),
            rightTableAlias: "o");
        builder.Pop();

        string expected = """
        SELECT *
        FROM Sales.OrderLines AS ol
        LEFT JOIN Sales.Orders o ON ol.OrderId = o.OrderId AND ol.CompanyId = o.CompanyId
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
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

        string expected = """
        SELECT c.CustomerName AS Name
        FROM Sales.Customer AS c

        UNION

        SELECT c.CustomerName AS Name
        FROM Sales.Customer AS c
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// A set operation whose right operand is itself a set operation: the inner scopes open
    /// and close while the outer operation is still armed, so completing on the wrong Pop -
    /// what the old single-flag bookkeeping did - would tear the grouping apart.
    /// </summary>
    [Fact]
    public void ASetOperationNestedOnTheRightKeepsItsGrouping()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Pop();
        builder.SetOperation(SetOperationType.Union);
        builder.Push();
        builder.Push();
        builder.From("Sales.Prospects", alias: "p");
        builder.Pop();
        builder.SetOperation(SetOperationType.Intersect);
        builder.Push();
        builder.From("Sales.Banned", alias: "b");
        builder.Pop();
        builder.Pop();

        string expected = """
        SELECT *
        FROM Sales.Customers AS c

        UNION

        (
        SELECT *
        FROM Sales.Prospects AS p

        INTERSECT

        SELECT *
        FROM Sales.Banned AS b
        )
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// The generated method used to carry a trailing comma in the argument list, so it never
    /// compiled and AdvisorBenchmarking had to dig the SQL back out with a regex. Level 2 of
    /// decision 016 now judges it, so the shape assertion here guards the call site itself.
    /// </summary>
    [Fact]
    public void TheGeneratedMethodIsWellFormedCSharp()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Pop();

        var method = Method(builder);

        Assert.Contains("public static List<Customer> Query(IDbConnection connection)", method);
        Assert.Contains("connection.Query<Customer>(", method);
        Assert.Contains("\"\"\").ToList();", method);
        Assert.DoesNotContain(",\n)", method.Replace("\r\n", "\n"));
    }

    /// <summary>
    /// Decision 025: a target whose query form is a string emits the bare query beside the
    /// method, so nothing downstream has to extract it back out of the code.
    /// </summary>
    [Fact]
    public void BothTheMethodAndTheBareSqlAreEmitted()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.From("Sales.Customers", alias: "c");
        builder.Pop();

        var artifacts = builder.Build();

        Assert.Equal(2, artifacts.Count);
        Assert.Contains(artifacts, a => a.ContentType == ConversionContentType.CSharpQuery);
        Assert.Contains(artifacts, a => a.ContentType == ConversionContentType.SqlQuery);
        Assert.StartsWith("SELECT", artifacts.Single(a => a.ContentType == ConversionContentType.SqlQuery).Content);
    }

    /// <summary>
    /// Rule Q2 used to end in an exception; decision 010 says a state the design foresees is
    /// a record, not a crash.
    /// </summary>
    [Fact]
    public void AQueryWithoutASourceIsARecordNotAnException()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.Project("c", "Name");
        builder.Pop();

        var artifacts = builder.Build();

        Assert.Empty(artifacts);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("Q2"));
    }

    /// <summary>
    /// Rule Q8: aggregates next to plain columns need a grouping. Nothing checked this before.
    /// </summary>
    [Fact]
    public void AggregatesWithoutAGroupingAreReported()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        builder.Push();
        builder.From("Sales.Orders", alias: "o");
        builder.Project("o", "CustomerId");
        builder.Project("o", "TotalPrice", function: "SUM", alias: "Total");
        builder.Pop();

        builder.Build();

        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Incompleteness && r.Feature == QueryFeature.Grouping);
    }
}
