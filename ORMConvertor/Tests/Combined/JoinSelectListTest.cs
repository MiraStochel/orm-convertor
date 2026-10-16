using System.Text.RegularExpressions;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using MyBatisWrappers;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The select list of a query over a join, the three shapes real code showed the tool
/// reading as something else (the findings V4-V6 of the open-source excerpts, decision 138):
/// an alias the source delimited because it is no identifier, a column written without its
/// table, and the whole row of one range variable. One mechanism carries all three - the
/// projection says what it takes and the builder template resolves what the mapping says -
/// and every target answers it the same way: a SQL target writes the name delimited, the
/// column bare and the row as <c>t.*</c>; a target that names a column as a member of its row
/// (LINQ, HQL, JPQL) writes the row it knows and refuses what it cannot name.
/// </summary>
public class JoinSelectListTest
{
    private static EntityMap Map(string entity, string table, params (string Name, ScalarType Type)[] columns)
    {
        var properties = columns.Select(c => new Property { Name = c.Name, Type = LangType.Scalar(c.Type) }).ToList();

        return new EntityMap
        {
            Entity = new Entity { Name = entity, Properties = properties },
            Table = table,
            Schema = "Sales",
            PropertyMaps = properties.Select(p => new PropertyMap { Property = p, ColumnName = p.Name }).ToList(),
        };
    }

    private static EntityMap[] Maps() =>
    [
        Map("Customer", "Customers", ("CustomerId", ScalarType.Int), ("Name", ScalarType.String)),
        Map("Invoice", "Invoices", ("OrderId", ScalarType.Int), ("CustomerId", ScalarType.Int), ("Total", ScalarType.Decimal)),
    ];

    private const string Join = """
        FROM Sales.Customers c
        INNER JOIN Sales.Invoices o ON o.CustomerId = c.CustomerId
        """;

    private static AbstractQueryBuilder FromSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder FromJpql(AbstractQueryBuilder builder, string jpql)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, maps);
        return builder;
    }

    private static AbstractQueryBuilder FromLinq(AbstractQueryBuilder builder, string chain)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        new EFCoreLinqQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            $$"""
            public void Query()
            {
                var q = {{chain}}.ToList();
            }
            """,
            maps);
        return builder;
    }

    private static string? Artifact(AbstractQueryBuilder builder, ConversionContentType type)
        => builder.Build().SingleOrDefault(s => s.ContentType == type)?.Content;

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery)!;

    /// <summary>
    /// The target cannot write the shape in its own language and falls back to native SQL
    /// (decision 113): the record of kind Fallback names the shape, and the artifacts are the
    /// escape path's - the bare SQL among them, or none where the native API cannot take the
    /// result either, which the record of kind Failure then says.
    /// </summary>
    private static (ConversionRecord Fallback, string? Sql) FallsBack(AbstractQueryBuilder builder, QueryFeature feature)
    {
        var artifacts = builder.Build();
        var fallback = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.Equal(feature, fallback.Feature);
        return (fallback, artifacts.SingleOrDefault(s => s.ContentType == ConversionContentType.SqlQuery)?.Content);
    }

    private static void AssertSql(string expected, string actual)
        => Assert.Equal(expected, actual, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);

    /// <summary>The text with every run of whitespace as one space, so that a line break of the target's layout does not count.</summary>
    private static string Flat(string? text) => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

    private static string Records(AbstractQueryBuilder builder) => string.Join("\n", builder.Records.Select(r => $"{r.Kind} {r.Feature}: {r.Reason}"));

    /* ---- an alias the source delimited (V4) --------------------------------------------- */

    [Fact]
    public void ADelimitedAliasIsDelimitedAgainInSql()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), """SELECT c.CustomerId AS "customer.id" FROM Sales.Customers c""");

        Assert.Equal("SELECT c.CustomerId AS [customer.id] FROM Sales.Customers AS c", Flat(Sql(builder)));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
    }

    [Fact]
    public void AnOrderingByADelimitedAliasIsDelimitedToo()
    {
        var sql = Sql(FromSql(new DapperSqlQueryBuilder(), """SELECT c.CustomerId AS "customer.id" FROM Sales.Customers c ORDER BY "customer.id" """));

        Assert.Contains("ORDER BY [customer.id] ASC", sql);
    }

    /// <summary>
    /// LINQ has no member name for it, and neither has the row class of EF Core's native
    /// SqlQuery, so nothing comes out: the escape path refused, and its refusal is the record
    /// that stands (the gap it was taken for is not reported beside it).
    /// </summary>
    [Fact]
    public void ADelimitedAliasThatIsNoIdentifierHasNoMemberNameInLinq()
    {
        var builder = FromSql(new EFCoreLinqQueryBuilder(), """SELECT c.CustomerId AS "customer.id" FROM Sales.Customers c""");

        Assert.Empty(builder.Build());
        var failure = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Failure, failure.Kind);
        Assert.Contains("'customer.id'", failure.Reason);
        Assert.Contains("not a C# identifier", failure.Reason);
    }

    /// <summary>JPQL and HQL have no result variable for it; the native SQL of the framework names the column as T-SQL does.</summary>
    [Fact]
    public void ADelimitedAliasThatIsNoIdentifierGoesToNativeSqlFromJpqlAndHql()
    {
        foreach (var builder in new AbstractQueryBuilder[] { new HibernateJpqlQueryBuilder(), new NHibernateHqlQueryBuilder() })
        {
            var (fallback, sql) = FallsBack(FromSql(builder, """SELECT c.CustomerId AS "customer.id" FROM Sales.Customers c"""), QueryFeature.Projection);

            Assert.Contains("'customer.id'", fallback.Reason);
            Assert.NotNull(sql);
            Assert.Contains("AS [customer.id]", sql);
        }
    }

    /// <summary>Delimited for nothing the target needs: an identifier after all is written bare where names are members.</summary>
    [Fact]
    public void ADelimitedIdentifierIsBracketedInSqlAndBareInLinq()
    {
        Assert.Contains("AS [Label]", Sql(FromSql(new DapperSqlQueryBuilder(), "SELECT c.Name AS [Label] FROM Sales.Customers c")));

        var linq = Artifact(FromSql(new EFCoreLinqQueryBuilder(), "SELECT c.Name AS [Label] FROM Sales.Customers c"), ConversionContentType.CSharpQuery);
        Assert.NotNull(linq);
        Assert.Contains("Label = c.Name", linq);
    }

    /// <summary>The nesting MyBatis reads into a dotted alias is what the representation does not carry, and the MyBatis reader says so.</summary>
    [Fact]
    public void AMyBatisSourceSaysWhatItsDottedAliasMeant()
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };

        new MyBatisXmlQueryParser(() => builder, MyBatisReadingContext.For(new DummyEntityBuilder()))
            .Parse(ConversionContentType.XML, """
                <?xml version="1.0" encoding="UTF-8"?>
                <mapper namespace="Shop.CustomerMapper">
                  <select id="find" resultType="Customer">SELECT c.CustomerId AS "customer.id" FROM Sales.Customers c</select>
                </mapper>
                """);

        Assert.Contains("AS [customer.id]", Sql(builder));
        var loss = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("nested property 'customer.id'"));
        Assert.Equal(QueryFeature.Projection, loss.Feature);
    }

    /* ---- a column written without its table (V5) ---------------------------------------- */

    /// <summary>The mapping says which row holds the column, in every clause of the query.</summary>
    [Fact]
    public void ABareColumnOfAJoinBelongsToTheRowWhoseEntityMapsIt()
    {
        var sql = $"SELECT Name, OrderId {Join} WHERE Total > 10 ORDER BY Name";

        AssertSql(
            """
            SELECT c.Name, o.OrderId
            FROM Sales.Customers AS c
            INNER JOIN Sales.Invoices o ON o.CustomerId = c.CustomerId
            WHERE o.Total > 10
            ORDER BY c.Name ASC
            """,
            Sql(FromSql(new DapperSqlQueryBuilder(), sql)));

        var linq = Artifact(FromSql(new EFCoreLinqQueryBuilder(), sql), ConversionContentType.CSharpQuery);
        Assert.NotNull(linq);
        Assert.Contains(".o.Total > 10", linq);
        Assert.Contains("Name = ", linq);
        Assert.Contains(".c.Name", linq);

        var jpql = Artifact(FromSql(new HibernateJpqlQueryBuilder(), sql), ConversionContentType.JpqlQuery);
        Assert.NotNull(jpql);
        Assert.Contains("select c.Name, o.OrderId", jpql);
        Assert.Contains("where o.Total > 10", jpql);
    }

    /// <summary>
    /// A column of neither map: SQL leaves it as the source wrote it; a target over members
    /// cannot say whose it is and writes the query in native SQL, where it stands bare again
    /// (decision 113 - the mapping a catalog supplies decides the direction).
    /// </summary>
    [Fact]
    public void ABareColumnNoMappingHoldsStaysBareInSqlAndGoesToNativeSqlWhereNamesAreMembers()
    {
        var sql = $"SELECT c.CustomerId {Join} WHERE Region = 'EU'";

        Assert.Contains("WHERE Region = 'EU'", Sql(FromSql(new DapperSqlQueryBuilder(), sql)));

        foreach (var builder in new AbstractQueryBuilder[] { new EFCoreLinqQueryBuilder(), new HibernateJpqlQueryBuilder(), new NHibernateHqlQueryBuilder() })
        {
            var (fallback, native) = FallsBack(FromSql(builder, sql), QueryFeature.Filtering);

            Assert.Contains("'Region'", fallback.Reason);
            Assert.Contains("without its table", fallback.Reason);
            Assert.NotNull(native);
            Assert.Contains("WHERE Region = 'EU'", native);
        }
    }

    /// <summary>The same column projected: the row class of EF Core's native SqlQuery has nothing to type it by, so EF Core alone refuses, and the refusal is the record that stands.</summary>
    [Fact]
    public void ABareColumnNoMappingHoldsCannotBeTypedInTheRowOfEFCoresNativeSql()
    {
        var builder = FromSql(new EFCoreLinqQueryBuilder(), $"SELECT Region, c.CustomerId {Join}");

        Assert.Empty(builder.Build());
        var failure = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Failure, failure.Kind);
        Assert.Contains("'Region'", failure.Reason);
        Assert.Contains("typed by nothing", failure.Reason);
    }

    /// <summary>Over one table every bare column is that table's, whatever the mapping says of it.</summary>
    [Fact]
    public void ABareColumnOfOneTableIsThatTablesInEveryClause()
    {
        const string sql = "SELECT Name, Region FROM Sales.Customers c WHERE Region = 'EU' ORDER BY Name";

        AssertSql(
            """
            SELECT c.Name, c.Region
            FROM Sales.Customers AS c
            WHERE c.Region = 'EU'
            ORDER BY c.Name ASC
            """,
            Sql(FromSql(new DapperSqlQueryBuilder(), sql)));

        var linq = Artifact(FromSql(new EFCoreLinqQueryBuilder(), sql), ConversionContentType.CSharpQuery);
        Assert.NotNull(linq);
        Assert.Contains("c.Region == \"EU\"", linq);
    }

    /// <summary>An ordering by the alias of a projection names the projection, not a column of any row (decision 073).</summary>
    [Fact]
    public void AnOrderingByAProjectionAliasIsNotAColumnOfTheJoin()
    {
        var sql = Sql(FromSql(new DapperSqlQueryBuilder(), $"SELECT c.Name AS Label, o.Total {Join} ORDER BY Label"));

        Assert.Contains("ORDER BY Label ASC", sql);
    }

    /* ---- the whole row of one range variable (V6) --------------------------------------- */

    [Fact]
    public void TheWholeRowOfOneJoinSideIsThatRowInEveryTarget()
    {
        var sql = $"SELECT o.* {Join} WHERE c.Name = 'A'";

        var dapper = FromSql(new DapperSqlQueryBuilder(), sql);
        AssertSql(
            """
            SELECT o.*
            FROM Sales.Customers AS c
            INNER JOIN Sales.Invoices o ON o.CustomerId = c.CustomerId
            WHERE c.Name = 'A'
            """,
            Sql(dapper));
        Assert.Contains("connection.Query<Invoice>(", Artifact(dapper, ConversionContentType.CSharpQuery));
        Assert.DoesNotContain(dapper.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);

        var linq = Artifact(FromSql(new EFCoreLinqQueryBuilder(), sql), ConversionContentType.CSharpQuery);
        Assert.NotNull(linq);
        Assert.Matches(new Regex(@"\.Select\((\w+) => \1\.o\)"), linq);
        Assert.Contains("IQueryable<Invoice>", linq);

        var jpa = FromSql(new HibernateJpqlQueryBuilder(), sql);
        Assert.StartsWith("select o from Customer c join Invoice o on o.CustomerId = c.CustomerId", Flat(Artifact(jpa, ConversionContentType.JpqlQuery)));
        Assert.DoesNotContain(jpa.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure or ConversionRecordKind.Fallback);

        var nhibernate = FromSql(new NHibernateHqlQueryBuilder(), sql);
        Assert.StartsWith("select o from Customer c inner join Invoice o with o.CustomerId = c.CustomerId", Flat(Artifact(nhibernate, ConversionContentType.HqlQuery)));
        Assert.DoesNotContain(nhibernate.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
    }

    /// <summary>Under DISTINCT the ordering keys have to be projected (decision 073); the whole row of o keeps every column of o.</summary>
    [Fact]
    public void ADistinctWholeRowKeepsTheColumnsOfItsRowForTheOrdering()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), $"SELECT DISTINCT o.* {Join} ORDER BY o.Total DESC");

        Assert.StartsWith("SELECT DISTINCT o.*", Sql(builder));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AWholeRowBesideColumnsIsDroppedWithARecordAndTheColumnsStand()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), $"SELECT o.*, c.Name {Join}");

        Assert.StartsWith("SELECT c.Name", Sql(builder));
        var loss = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(QueryFeature.Projection, loss.Feature);
        Assert.Contains("whole row of 'o'", loss.Reason);
    }

    [Fact]
    public void AWholeRowIsNoOperandOfIn()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Customers c WHERE c.CustomerId IN (SELECT o.* FROM Sales.Invoices o)");

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("whole row"));
    }

    [Fact]
    public void AJpqlSourceProjectingOneSideOfAJoinReachesEveryTarget()
    {
        const string jpql = "select o from Customer c join Invoice o on o.CustomerId = c.CustomerId where c.Name = 'A'";

        var dapper = FromJpql(new DapperSqlQueryBuilder(), jpql);
        Assert.StartsWith("SELECT o.*", Sql(dapper));
        Assert.Contains("connection.Query<Invoice>(", Artifact(dapper, ConversionContentType.CSharpQuery));
        Assert.DoesNotContain(dapper.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);

        var nhibernate = FromJpql(new NHibernateHqlQueryBuilder(), jpql);
        var hql = Artifact(nhibernate, ConversionContentType.HqlQuery);
        Assert.True(hql is not null, Records(nhibernate));
        Assert.StartsWith("select o from Customer c inner join Invoice o with", Flat(hql));
    }

    /// <summary>The root's own row stays the absence of a projection: rule Q3, as before.</summary>
    [Fact]
    public void AJpqlSourceProjectingTheRootIsTheWholeEntity()
    {
        var dapper = FromJpql(new DapperSqlQueryBuilder(), "select c from Customer c where c.Name = 'A'");

        Assert.StartsWith("SELECT *", Sql(dapper));
        Assert.Contains("connection.Query<Customer>(", Artifact(dapper, ConversionContentType.CSharpQuery));
    }

    [Fact]
    public void ALinqSourceSelectingOneSideOfAJoinIsThatRowWithoutARecord()
    {
        var dapper = FromLinq(
            new DapperSqlQueryBuilder(),
            "ctx.Customers.Join(ctx.Invoices, c => c.CustomerId, o => o.CustomerId, (c, o) => new { c, o }).Where(x => x.c.Name == \"A\").Select(x => x.o)");

        AssertSql(
            """
            SELECT o.*
            FROM Sales.Customers AS c
            INNER JOIN Sales.Invoices o ON c.CustomerId = o.CustomerId
            WHERE c.Name = 'A'
            """,
            Sql(dapper));
        Assert.Contains("connection.Query<Invoice>(", Artifact(dapper, ConversionContentType.CSharpQuery));
        Assert.DoesNotContain(dapper.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
    }

    [Fact]
    public void ALinqResultSelectorNamingOneSideIsThatRow()
    {
        var sql = Sql(FromLinq(
            new DapperSqlQueryBuilder(),
            "ctx.Customers.Join(ctx.Invoices, c => c.CustomerId, o => o.CustomerId, (c, o) => o)"));

        Assert.StartsWith("SELECT o.*", sql);
    }
}
