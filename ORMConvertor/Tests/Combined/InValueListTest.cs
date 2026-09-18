using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// IN over a list of values, end to end (decision 074): the operand carries the values the
/// query itself states as typed constants, each parser reads its own spelling of the list,
/// each visitor writes its own - IN (...), new[] { ... }.Contains(...), in (...) - and the
/// template holds what every target needs: the list stands only as IN's right side, and
/// its values share a scalar or one numeric family. What is not a value the query states -
/// a null, a column, a parameter, a collection from the enclosing scope - refuses the
/// artifact with a record that names it.
/// </summary>
public class InValueListTest
{
    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

    private static EntityMap Orders() =>
        new() { Entity = new() { Name = "Order" }, Table = "Orders", Schema = "Sales" };

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql)
    {
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string linq, params EntityMap[] maps)
    {
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, linq, maps);
        return builder;
    }

    private static AbstractQueryBuilder ParseHql(AbstractQueryBuilder builder, string hql, params EntityMap[] maps)
    {
        builder.EntityMaps = maps;
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, maps);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
        => builder.Build().Single(s => s.ContentType == type).Content;

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery);

    private static string Linq(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.CSharpQuery);

    private static string Hql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.HqlQuery);

    private static ConversionRecord AssertRefused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());
        return Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
    }

    private static string LinqQuery(string predicate) => $$"""
        public void Query()
        {
            var q = ctx.Customers.Where(c => {{predicate}}).ToList();
        }
        """;

    // ---- Carried shapes ------------------------------------------------------------

    [Fact]
    public void SqlInListRoundTripsToSql()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (1, 2, 3)
        """;

        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, sql);

        Assert.Equal(sql, Sql(builder), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void SqlInListBecomesLinqContainsOverAnInlineArray()
    {
        var builder = ParseSql(
            new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID IN (1, 2, 3)");

        Assert.Contains(".Where(c => new[] { 1, 2, 3 }.Contains(c.CustomerID))", Linq(builder));
    }

    [Fact]
    public void SqlInListBecomesHqlIn()
    {
        var builder = ParseSql(
            new NHibernateHqlQueryBuilder { EntityMaps = [Customers()] },
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID IN (1, 2, 3)");

        Assert.Contains("where c.CustomerID in (1, 2, 3)", Hql(builder));
    }

    [Fact]
    public void LinqInlineArrayContainsBecomesSqlIn()
    {
        var builder = ParseLinq(
            new DapperSqlQueryBuilder { EntityMaps = [Customers()] },
            LinqQuery("new[] { \"Alice\", \"Bob\" }.Contains(c.CustomerName)"),
            Customers());

        // The strings arrive undecorated and leave in the target's quotes (decision 024).
        Assert.Contains("WHERE c.CustomerName IN ('Alice', 'Bob')", Sql(builder));
    }

    [Fact]
    public void LinqInlineArrayContainsBecomesHqlIn()
    {
        var builder = ParseLinq(
            new NHibernateHqlQueryBuilder { EntityMaps = [Customers()] },
            LinqQuery("new[] { \"Alice\", \"Bob\" }.Contains(c.CustomerName)"),
            Customers());

        Assert.Contains("where c.CustomerName in ('Alice', 'Bob')", Hql(builder));
    }

    [Fact]
    public void HqlInListBecomesSqlAndLinq()
    {
        const string hql = "from Customer c where c.CustomerName in ('Alice', 'Bob')";

        Assert.Contains(
            "WHERE c.CustomerName IN ('Alice', 'Bob')",
            Sql(ParseHql(new DapperSqlQueryBuilder(), hql, Customers())));

        Assert.Contains(
            ".Where(c => new[] { \"Alice\", \"Bob\" }.Contains(c.CustomerName))",
            Linq(ParseHql(new EFCoreLinqQueryBuilder(), hql, Customers())));
    }

    [Fact]
    public void SqlNotInIsANegationInBothTargets()
    {
        const string sql = "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID NOT IN (1, 2)";

        Assert.Contains(
            ".Where(c => !(new[] { 1, 2 }.Contains(c.CustomerID)))",
            Linq(ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] }, sql)));

        Assert.Contains(
            "where not (c.CustomerID in (1, 2))",
            Hql(ParseSql(new NHibernateHqlQueryBuilder { EntityMaps = [Customers()] }, sql)));
    }

    [Fact]
    public void LinqNegatedContainsBecomesSqlNotIn()
    {
        var builder = ParseLinq(
            new DapperSqlQueryBuilder { EntityMaps = [Customers()] },
            LinqQuery("!new[] { 1, 2 }.Contains(c.CustomerID)"),
            Customers());

        Assert.Contains("WHERE NOT (c.CustomerID IN (1, 2))", Sql(builder));
    }

    [Theory]
    [InlineData("new int[] { 1, 2 }")]
    [InlineData("new List<int> { 1, 2 }")]
    public void ATypedArrayOrACollectionInitializerIsAListOfValues(string receiver)
    {
        var builder = ParseLinq(
            new DapperSqlQueryBuilder { EntityMaps = [Customers()] },
            LinqQuery($"{receiver}.Contains(c.CustomerID)"),
            Customers());

        Assert.Contains("WHERE c.CustomerID IN (1, 2)", Sql(builder));
    }

    [Fact]
    public void ASingleValueStaysAList()
    {
        // Rule Q15: the source wrote a list, so the target gets a list, not an equality.
        var builder = ParseSql(
            new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID IN (1)");

        Assert.Contains("new[] { 1 }.Contains(c.CustomerID)", Linq(builder));
    }

    [Fact]
    public void IntegersMixedWithADecimalAreOneNumericFamily()
    {
        const string sql = "SELECT * FROM Sales.Orders AS o WHERE o.Total IN (1000, 2500.50)";

        // Nothing is rewritten: each value keeps the scalar the parser read, so C# gets the
        // suffix on the decimal alone and infers decimal[] as the best common type.
        Assert.Contains(
            "new[] { 1000, 2500.50m }.Contains(o.Total)",
            Linq(ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Orders()] }, sql)));

        Assert.Contains(
            "WHERE o.Total IN (1000, 2500.50)",
            Sql(ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Orders()] }, sql)));
    }

    [Fact]
    public void DecimalValuesFromLinqLoseTheirSuffixInSql()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Orders.Where(o => new[] { 1000m, 2500.5m }.Contains(o.Total)).ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Orders()] }, linq, Orders());

        Assert.Contains("WHERE o.Total IN (1000, 2500.5)", Sql(builder));
    }

    // ---- Refused shapes ------------------------------------------------------------

    [Theory]
    [InlineData("c.CustomerID IN (1, 'a')", "Int and String")]
    [InlineData("c.CreditLimit IN (0.5, 1E0)", "Decimal and Double")]
    public void ValuesThatShareNoScalarRefuseTheArtifact(string predicate, string scalars)
    {
        // The list has no type the model can state, so no target types it: C# has no
        // best common type, T-SQL would convert and compare a different value.
        var builder = ParseSql(
            new DapperSqlQueryBuilder { EntityMaps = [Customers()] },
            $"SELECT * FROM Sales.Customers AS c WHERE {predicate}");

        var record = AssertRefused(builder, QueryFeature.Filtering);
        Assert.Contains(scalars, record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullAmongTheValuesRefusesTheArtifact()
    {
        // NULL is no value the model carries (decision 002), and NOT IN over it means
        // different things in SQL and in LINQ.
        var builder = ParseSql(
            new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID NOT IN (1, NULL)");

        var record = AssertRefused(builder, QueryFeature.Filtering);
        Assert.Contains("NULL among the values", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AColumnAmongTheValuesRefusesTheArtifactInHql()
    {
        var builder = ParseHql(
            new DapperSqlQueryBuilder(),
            "from Customer c where c.CustomerID in (1, c.ParentID)",
            Customers());

        var record = AssertRefused(builder, QueryFeature.Filtering);
        Assert.Contains("not a literal", record.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("new int[] { }.Contains(c.CustomerID)", "empty collection")]
    [InlineData("new[] { 1, null }.Contains(c.CustomerID)", "null among the values")]
    public void AnInlineCollectionNoTargetWritesRefusesTheArtifact(string predicate, string reason)
    {
        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, LinqQuery(predicate), Customers());

        var record = AssertRefused(builder, QueryFeature.Filtering);
        Assert.Contains(reason, record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AListAsTheLeftOperandRefusesTheArtifact()
    {
        // No parser produces this tree; the template refuses it all the same, so that a
        // fourth framework's parser could not slip it past the three visitors.
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.From("Sales.Customers", "c");
        builder.Where(new ComparisonCondition(
            QueryOperand.ValueList([QueryConstant.Of("1", ScalarType.Int)]),
            ComparisonOperator.Equal,
            QueryOperand.Column("c", "CustomerID")));

        var record = AssertRefused(builder, QueryFeature.Filtering);
        Assert.Contains("only as the right side of IN", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyListIsNotConstructible()
    {
        Assert.Throws<ArgumentException>(() => QueryOperand.ValueList([]));
    }

    // ---- Parameters, not values ----------------------------------------------------

    [Fact]
    public void AParameterAmongTheValuesIsRefusedUnderItsOwnCategory()
    {
        var byParser = new (string Name, Action<AbstractQueryBuilder> Parse)[]
        {
            ("@p", b => ParseSql(b, "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID IN (1, @p)")),
            (":p", b => ParseHql(b, "from Customer c where c.CustomerID in (1, :p)", Customers())),
            ("'id'", b => ParseLinq(b, LinqQuery("new[] { 1, id }.Contains(c.CustomerID)"), Customers())),
            ("'ids'", b => ParseLinq(b, LinqQuery("ids.Contains(c.CustomerID)"), Customers())),
        };

        foreach (var (name, parse) in byParser)
        {
            var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
            parse(builder);

            // A value the caller supplies is a parameter, whichever way it is spelled - even
            // a whole collection from the enclosing scope (decisions 070 and 074).
            var record = AssertRefused(builder, QueryFeature.QueryParameter);
            Assert.Contains(name, record.Reason, StringComparison.Ordinal);
        }
    }
}
