using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The quantified comparison, end to end (decision 119): <c>x &gt; ALL (subquery)</c> and
/// <c>x &gt; ANY (subquery)</c> travel as one comparison with a quantifier, the four readers
/// read them - SQL, HQL and JPQL by their keywords, LINQ as All() and Any() with the
/// comparison in the lambda -, the three SQL-shaped targets write the keyword back, and the
/// LINQ target writes All() and Any() over the projected values. <c>= ANY</c> and
/// <c>&lt;&gt; ALL</c> fold into IN and NOT IN, which the targets already write; SOME is ANY. A
/// negation over a quantified comparison is flipped by De Morgan before LINQ writes it, and
/// a provider that does not compensate for C#'s null semantics gets the null tests spelled
/// out, because a NULL on either side is the one place where All() and ALL part.
/// </summary>
public class QuantifiedComparisonTest
{
    private static EntityMap Customers(bool creditNullable = false)
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var credit = new Property { Name = "Credit", Type = LangType.Scalar(ScalarType.Decimal, creditNullable) };
        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, credit] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID", IsNullable = false },
                new PropertyMap { Property = credit, ColumnName = "Credit", IsNullable = creditNullable },
            ],
        };
    }

    private static EntityMap Orders(bool totalNullable = false)
    {
        var id = new Property { Name = "OrderID", Type = LangType.Scalar(ScalarType.Int) };
        var customer = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal, totalNullable) };
        return new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [id, customer, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "OrderID", IsNullable = false },
                new PropertyMap { Property = customer, ColumnName = "CustomerID", IsNullable = false },
                new PropertyMap { Property = total, ColumnName = "Total", IsNullable = totalNullable },
            ],
        };
    }

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql, params EntityMap[] maps)
    {
        builder.EntityMaps = maps.Length == 0 ? [Customers(), Orders()] : maps;
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseHql(AbstractQueryBuilder builder, string hql)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, [Customers(), Orders()]);
        return builder;
    }

    private static AbstractQueryBuilder ParseJpql(AbstractQueryBuilder builder, string jpql)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, [Customers(), Orders()]);
        return builder;
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string linq)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, linq, [Customers(), Orders()]);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return outputs.Single(s => s.ContentType == type).Content;
    }

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery);
    private static string Hql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.HqlQuery);
    private static string Jpql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.JpqlQuery);
    private static string CSharp(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.CSharpQuery);

    private static ConversionRecord AssertRefused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());
        var record = builder.Records.FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
        Assert.NotNull(record);
        return record;
    }

    private const string Correlated = "SELECT o.Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID";

    private static string Quantified(string op, string quantifier)
        => $"SELECT * FROM Sales.Customers AS c WHERE c.Credit {op} {quantifier} ({Correlated})";

    // ---- carried ----------------------------------------------------------------------

    [Fact]
    public void SqlAllReachesEveryTarget()
    {
        var sql = Quantified(">", "ALL");
        Assert.Contains("WHERE c.Credit > ALL (SELECT o.Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains("where c.Credit > all (select o.Total from Order o where o.CustomerID = c.CustomerID)", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));
        Assert.Contains("where c.Credit > all (select o.Total from Order o where o.CustomerID = c.CustomerID)", Jpql(ParseSql(new HibernateJpqlQueryBuilder(), sql)));
        Assert.Contains(
            ".Where(c => ctx.Set<Order>().Where(o => o.CustomerID == c.CustomerID).Select(o => o.Total).All(v => c.Credit > v))",
            CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
    }

    [Fact]
    public void SqlAnyReachesEveryTarget()
    {
        var sql = Quantified("<", "ANY");
        Assert.Contains("c.Credit < ANY (SELECT o.Total", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains("c.Credit < any (select o.Total", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));
        Assert.Contains("c.Credit < any (select o.Total", Jpql(ParseSql(new HibernateJpqlQueryBuilder(), sql)));
        Assert.Contains(".Select(o => o.Total).Any(v => c.Credit < v))", CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
    }

    /// <summary>SOME is a second spelling of ANY and comes out as ANY, with no record: the rows are the same and the representation carries one name.</summary>
    [Fact]
    public void SqlSomeIsAny()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), Quantified(">=", "SOME"));
        Assert.Contains("c.Credit >= ANY (SELECT o.Total", Sql(builder));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Convention);
    }

    /// <summary>
    /// = ANY is IN and &lt;&gt; ALL is NOT IN, exactly, so both fold into the shape the targets
    /// already write (decision 061), with a convention record naming the rewrite.
    /// </summary>
    [Theory]
    [InlineData("=", "ANY", "WHERE c.Credit IN (SELECT o.Total")]
    [InlineData("=", "SOME", "WHERE c.Credit IN (SELECT o.Total")]
    [InlineData("<>", "ALL", "WHERE NOT (c.Credit IN (SELECT o.Total")]
    public void EqualAnyAndNotEqualAllFoldIntoIn(string op, string quantifier, string expected)
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), Quantified(op, quantifier));
        Assert.Contains(expected, Sql(builder));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("selects the same rows"));
    }

    /// <summary>
    /// SQL keeps the negation as written; LINQ pushes it into the comparison by De Morgan -
    /// NOT (x &gt; ALL S) is x &lt;= ANY S -, because !All() would keep the rows SQL leaves
    /// unknown over a NULL.
    /// </summary>
    [Fact]
    public void NegationIsFlippedForLinqAndKeptForSql()
    {
        var sql = $"SELECT * FROM Sales.Customers AS c WHERE NOT (c.Credit > ALL ({Correlated}))";
        Assert.Contains("WHERE NOT (c.Credit > ALL (SELECT o.Total", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains("where not (c.Credit > all (select o.Total", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));

        var linq = CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql));
        Assert.Contains(".Select(o => o.Total).Any(v => c.Credit <= v))", linq);
        Assert.DoesNotContain("!(", linq);
    }

    [Fact]
    public void NegatedAnyBecomesLinqAll()
    {
        var sql = $"SELECT * FROM Sales.Customers AS c WHERE NOT (c.Credit >= ANY ({Correlated}))";
        Assert.Contains(".Select(o => o.Total).All(v => c.Credit < v))", CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
    }

    /// <summary>
    /// A quantified comparison inside a scalar subquery of a condition on the left, a
    /// constant on the right: 100 &lt; ANY (…) is a value of the enclosing query compared,
    /// whatever that value is.
    /// </summary>
    [Fact]
    public void AConstantMayBeTheComparedValue()
    {
        var sql = "SELECT * FROM Sales.Customers AS c WHERE 100 < ANY (SELECT o.Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)";
        Assert.Contains("WHERE 100 < ANY (SELECT o.Total", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains(".Select(o => o.Total).Any(v => 100 < v))", CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
    }

    // ---- the provider that does not compensate for C#'s null semantics ----------------

    /// <summary>
    /// NHibernate's LINQ form (decision 118) writes All() with the null tests the provider
    /// would not add itself: the projected value where the mapping lets it be NULL, the
    /// compared value likewise. Any() needs none - a comparison with a NULL holds in
    /// neither logic.
    /// </summary>
    [Fact]
    public void NHibernateLinqSpellsTheNullTestsOutUnderAll()
    {
        var sql = Quantified(">", "ALL");

        var nullable = ParseSql(new NHibernateHqlQueryBuilder(), sql, Customers(creditNullable: true), Orders(totalNullable: true));
        var outputs = nullable.Build();
        Assert.DoesNotContain(nullable.Records, r => r.Kind == ConversionRecordKind.Failure);
        var linq = outputs.Single(s => s.ContentType == ConversionContentType.CSharpLinqQuery).Content;
        Assert.Contains(".Select(o => o.Total).All(v => c.Credit > v && v != null && c.Credit != null))", linq);
        Assert.Contains("where c.Credit > all (select o.Total", outputs.Single(s => s.ContentType == ConversionContentType.HqlQuery).Content);

        var declared = ParseSql(new NHibernateHqlQueryBuilder(), sql, Customers(), Orders());
        var declaredLinq = declared.Build().Single(s => s.ContentType == ConversionContentType.CSharpLinqQuery).Content;
        Assert.Contains(".Select(o => o.Total).All(v => c.Credit > v))", declaredLinq);
    }

    [Fact]
    public void NHibernateLinqWritesAnyWithoutNullTests()
    {
        var builder = ParseSql(new NHibernateHqlQueryBuilder(), Quantified("<", "ANY"), Customers(creditNullable: true), Orders(totalNullable: true));
        var linq = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpLinqQuery).Content;
        Assert.Contains(".Select(o => o.Total).Any(v => c.Credit < v))", linq);
    }

    /// <summary>EF Core compensates for C#'s semantics itself, so its All() carries no null test even over nullable columns.</summary>
    [Fact]
    public void EFCoreWritesNoNullTests()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder(), Quantified(">", "ALL"), Customers(creditNullable: true), Orders(totalNullable: true));
        Assert.Contains(".Select(o => o.Total).All(v => c.Credit > v))", CSharp(builder));
    }

    // ---- the other readers ------------------------------------------------------------

    [Theory]
    [InlineData("all", "c.Credit > ALL (SELECT o.Total")]
    [InlineData("any", "c.Credit > ANY (SELECT o.Total")]
    [InlineData("some", "c.Credit > ANY (SELECT o.Total")]
    public void HqlReadsTheQuantifier(string quantifier, string expected)
    {
        var hql = $"from Customer c where c.Credit > {quantifier} (select o.Total from Order o where o.CustomerID = c.CustomerID)";
        Assert.Contains(expected, Sql(ParseHql(new DapperSqlQueryBuilder(), hql)));
    }

    [Theory]
    [InlineData("all", "c.Credit > ALL (SELECT o.Total")]
    [InlineData("any", "c.Credit > ANY (SELECT o.Total")]
    [InlineData("some", "c.Credit > ANY (SELECT o.Total")]
    public void JpqlReadsTheQuantifier(string quantifier, string expected)
    {
        var jpql = $"select c from Customer c where c.Credit > {quantifier} (select o.Total from Order o where o.CustomerID = c.CustomerID)";
        Assert.Contains(expected, Sql(ParseJpql(new DapperSqlQueryBuilder(), jpql)));
    }

    [Fact]
    public void HqlEqualAnyFoldsIntoIn()
    {
        var hql = "from Customer c where c.Credit = any (select o.Total from Order o)";
        var builder = ParseHql(new DapperSqlQueryBuilder(), hql);
        Assert.Contains("WHERE c.Credit IN (SELECT o.Total", Sql(builder));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Convention);
    }

    /// <summary>
    /// The LINQ reader takes both spellings: All() over the rows with the projected value
    /// named in the predicate, and All() over an already projected chain with the value as
    /// the lambda's parameter. A predicate with the element on the left is mirrored into the
    /// representation's one shape.
    /// </summary>
    [Theory]
    [InlineData("ctx.Orders.Where(o => o.CustomerID == c.CustomerID).All(o => c.Credit > o.Total)", "c.Credit > ALL (SELECT o.Total AS Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)")]
    [InlineData("ctx.Orders.Where(o => o.CustomerID == c.CustomerID).Select(o => o.Total).All(v => c.Credit > v)", "c.Credit > ALL (SELECT o.Total AS Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)")]
    [InlineData("ctx.Orders.Where(o => o.CustomerID == c.CustomerID).Select(o => o.Total).All(v => v < c.Credit)", "c.Credit > ALL (SELECT o.Total AS Total FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)")]
    [InlineData("ctx.Orders.Select(o => o.Total).Any(v => c.Credit <= v)", "c.Credit <= ANY (SELECT o.Total AS Total FROM Sales.Orders AS o)")]
    [InlineData("ctx.Orders.All(o => o.Total < c.Credit)", "c.Credit > ALL (SELECT o.Total AS Total FROM Sales.Orders AS o)")]
    public void LinqAllAndAnyAreReadAsTheQuantifier(string predicate, string expected)
    {
        var linq = $$"""
        public void Query()
        {
            var q = ctx.Customers.Where(c => {{predicate}}).ToList();
        }
        """;
        Assert.Contains(expected, Sql(ParseLinq(new DapperSqlQueryBuilder(), linq)));
    }

    /// <summary>Any(predicate) over an unprojected chain keeps the reading of decision 061: Where(predicate).Any(), the same rows.</summary>
    [Fact]
    public void LinqAnyOverRowsStaysExists()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Where(c => ctx.Orders.Any(o => c.Credit > o.Total)).ToList();
        }
        """;
        Assert.Contains("WHERE EXISTS (SELECT * FROM Sales.Orders AS o WHERE c.Credit > o.Total)", Sql(ParseLinq(new DapperSqlQueryBuilder(), linq)));
    }

    [Fact]
    public void LinqEqualAnyOverAProjectedChainFoldsIntoIn()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Where(c => ctx.Orders.Select(o => o.CustomerID).Any(v => v == c.CustomerID)).ToList();
        }
        """;
        var builder = ParseLinq(new DapperSqlQueryBuilder(), linq);
        Assert.Contains("WHERE c.CustomerID IN (SELECT o.CustomerID AS CustomerID FROM Sales.Orders AS o)", Sql(builder));
    }

    /// <summary>A LINQ reading written back to LINQ: the two providers read what the other wrote.</summary>
    [Fact]
    public void LinqAllRoundTripsToLinq()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Where(c => ctx.Orders.All(o => c.Credit > o.Total)).ToList();
        }
        """;
        Assert.Contains(".Where(c => ctx.Set<Order>().Select(o => o.Total).All(v => c.Credit > v))", CSharp(ParseLinq(new EFCoreLinqQueryBuilder(), linq)));
    }

    // ---- refused ----------------------------------------------------------------------

    /// <summary>
    /// All() over a predicate that is not one comparison against the element is refused by
    /// name: NOT EXISTS over the negated predicate, which is what the providers make of it,
    /// selects other rows than SQL's quantifier where a value is NULL, and the
    /// representation does not guess.
    /// </summary>
    [Theory]
    [InlineData("ctx.Orders.All(o => o.Total > 1 && o.CustomerID == c.CustomerID)", "not one relational comparison")]
    [InlineData("ctx.Orders.All(o => o.Total > o.OrderID)", "both sides")]
    [InlineData("ctx.Orders.All(o => c.Credit > 1)", "neither side")]
    [InlineData("ctx.Orders.Select(o => o.Total).All(v => c.Credit > v.Value)", "projected value itself")]
    public void LinqAllOverAnotherPredicateIsRefusedByName(string predicate, string reason)
    {
        var linq = $$"""
        public void Query()
        {
            var q = ctx.Customers.Where(c => {{predicate}}).ToList();
        }
        """;
        var builder = ParseLinq(new DapperSqlQueryBuilder(), linq);
        var record = AssertRefused(builder, QueryFeature.Subquery);
        Assert.Contains(reason, record.Reason);
    }

    /// <summary>The template refuses a quantifier on any shape but a relational comparison against a subquery, once for every target.</summary>
    [Fact]
    public void TheGateRefusesAQuantifierOffASubquery()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.From("Sales.Customers", "c");
        builder.Where(new ComparisonCondition(
            QueryOperand.Column("c", "Credit"),
            ComparisonOperator.GreaterThan,
            QueryOperand.Value(QueryConstant.Of("1", ScalarType.Int)),
            Quantifier: Quantifier.All));
        var record = AssertRefused(builder, QueryFeature.Subquery);
        Assert.Contains("ALL", record.Reason);
    }

    [Fact]
    public void TheFactoryRefusesAQuantifierOnAPredicate()
    {
        var sub = QueryOperand.Nested(new Model.QueryInstructions.SubQueryInstruction([]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ComparisonCondition.Quantified(QueryOperand.Column("c", "Credit"), ComparisonOperator.Like, Quantifier.All, sub));
        Assert.Throws<ArgumentException>(() =>
            ComparisonCondition.Quantified(QueryOperand.Column("c", "Credit"), ComparisonOperator.Equal, Quantifier.All, QueryOperand.Column("o", "Total")));
    }
}
