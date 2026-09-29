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
/// a null, a column, a collection from the enclosing scope - refuses the artifact with a
/// record that names it; a scalar parameter stands among the values since decision 102 and
/// takes its scalar from the column on IN's left side, never from its neighbours.
/// </summary>
public class InValueListTest
{
    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

    /// <summary>The same entity with typed properties, which is what a parameter's scalar comes from (decision 083).</summary>
    private static EntityMap TypedCustomers()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, name] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
            ],
        };
    }

    private static EntityMap TypedOrders()
    {
        var placed = new Property { Name = "PlacedAt", Type = LangType.Scalar(ScalarType.DateTime) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [placed] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps = [new PropertyMap { Property = placed, ColumnName = "PlacedAt" }],
        };
    }

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
            QueryOperand.ValueList([QueryOperand.Value(QueryConstant.Of("1", ScalarType.Int))]),
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

    // ---- A parameter among the values (decision 102) ---------------------------------

    [Fact]
    public void AParameterAmongTheValuesIsCarriedAndTypedFromTheColumn()
    {
        var byParser = new (string Name, Action<AbstractQueryBuilder> Parse)[]
        {
            ("p", b => ParseSql(b, "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID IN (1, @p)")),
            ("p", b => ParseHql(b, "from Customer c where c.CustomerID in (1, :p)", TypedCustomers())),
            ("id", b => ParseLinq(b, LinqQuery("new[] { 1, id }.Contains(c.CustomerID)"), TypedCustomers())),
        };

        foreach (var (name, parse) in byParser)
        {
            var dapper = new DapperSqlQueryBuilder { EntityMaps = [TypedCustomers()] };
            parse(dapper);
            Assert.Contains($"c.CustomerID IN (1, @{name})", Sql(dapper));
            Assert.Contains($"int {name}", Artifact(dapper, ConversionContentType.CSharpQuery));

            var efCore = new EFCoreLinqQueryBuilder { EntityMaps = [TypedCustomers()] };
            parse(efCore);
            var linq = Linq(efCore);
            Assert.Contains($"new[] {{ 1, {name} }}.Contains(c.CustomerID)", linq);
            Assert.Contains($"int {name}", linq);

            var nhibernate = new NHibernateHqlQueryBuilder { EntityMaps = [TypedCustomers()] };
            parse(nhibernate);
            var hql = Hql(nhibernate);
            Assert.Contains($"c.CustomerID in (1, :{name})", hql);
            Assert.Contains($".SetParameter(\"{name}\", {name})", Artifact(nhibernate, ConversionContentType.CSharpQuery));
        }
    }

    /// <summary>The scalar comes from the column, so without a mapping the parameter cannot be typed - the refusal of decision 083.</summary>
    [Fact]
    public void AParameterAmongTheValuesWithoutAMappingIsRefused()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] },
            "SELECT * FROM Sales.Customers AS c WHERE c.CustomerID IN (1, @p)");

        var record = AssertRefused(builder, QueryFeature.QueryParameter);
        Assert.Contains("p", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>The constants beside the parameter are typed from the temporal column; the parameter takes the column's scalar through the gate.</summary>
    [Fact]
    public void AParameterBesideTemporalStringsIsTypedFromTheColumn()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [TypedOrders()] };
        new DapperSqlQueryParser(() => builder).Parse(
            ConversionContentType.SqlQuery,
            "SELECT * FROM Sales.Orders AS o WHERE o.PlacedAt IN ('2025-01-01', @d)");

        Assert.Contains("o.PlacedAt IN ('2025-01-01 00:00:00', @d)", Sql(builder));
        Assert.Contains("DateTime d", Artifact(builder, ConversionContentType.CSharpQuery));
    }

    [Fact]
    public void ACollectionParameterAmongTheValuesIsNotConstructible()
    {
        Assert.Throws<ArgumentException>(() => QueryOperand.ValueList(
        [
            QueryOperand.Value(QueryConstant.Of("1", ScalarType.Int)),
            QueryOperand.Bound(QueryParameter.Named("ids", isCollection: true)),
        ]));
    }
}
