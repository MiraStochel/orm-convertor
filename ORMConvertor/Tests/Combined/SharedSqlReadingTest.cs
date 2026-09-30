using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using MyBatisWrappers;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// T-SQL is one language and since decision 082 it has one reader, shared by the wrappers
/// that write in it. The move itself is proved by the Dapper query suite passing unchanged;
/// what this file adds is the proof decision 026 asked for when LINQ moved and 082 repeats
/// for SQL: that the sharing is a statement about <em>behavior</em> and not merely about
/// where the code sits - the same text read out of a Dapper unit and out of the
/// &lt;sql-query&gt; of an hbm.xml gives the same intermediate representation.
///
/// Beside it, what the NHibernate wrapper has to do before the shared reading sees the text
/// at all: a native query may carry a result mapping and NHibernate's own placeholders,
/// neither of which is T-SQL.
/// </summary>
public class SharedSqlReadingTest
{
    private const string Sql = """
        SELECT c.CustomerName, c.CreditLimit
        FROM Sales.Customers AS c
        WHERE c.CreditLimit > 1000
        ORDER BY c.CustomerName ASC
        """;

    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

    private static string Hbm(string queryElement) =>
        $"""
        <hibernate-mapping>
          <class name="Customer" table="Customers" schema="Sales" />
        {queryElement}
        </hibernate-mapping>
        """;

    private static AbstractQueryBuilder FromDapperUnit(string sql)
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder FromNativeQuery(string queryElement)
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        new NHibernateXmlQueryParser(() => builder).Parse(ConversionContentType.XML, Hbm(queryElement));
        return builder;
    }

    /// <summary>
    /// The third reader of the same language (decision 084). Its own work is the same kind
    /// as the NHibernate parser's - getting hold of plain T-SQL out of an XML document - and
    /// the grammar it hands the text to is the same one.
    /// </summary>
    private static AbstractQueryBuilder FromMyBatisMapper(string sql)
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };

        new MyBatisXmlQueryParser(() => builder, MyBatisReadingContext.For(new DummyEntityBuilder()))
            .Parse(ConversionContentType.XML, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <mapper namespace="Shop.CustomerMapper">
                  <select id="findRich" resultType="Customer">{sql}</select>
                </mapper>
                """);

        return builder;
    }

    private static string? BareSql(AbstractQueryBuilder builder)
        => builder.Build().SingleOrDefault(s => s.ContentType == ConversionContentType.SqlQuery)?.Content;

    /// <summary>
    /// The proof the decision asked for. Both routes end in the same artifact because both
    /// went through the same reader; the only difference between them is where the text came
    /// from, which is the wrapper's business and not the grammar's.
    /// </summary>
    [Fact]
    public void TheSameSqlReadFromBothSourcesGivesTheSameQuery()
    {
        var fromDapper = BareSql(FromDapperUnit(Sql));
        var fromNative = BareSql(FromNativeQuery($"  <sql-query name=\"findRich\">{Sql}</sql-query>"));

        Assert.NotNull(fromDapper);
        Assert.Equal(fromDapper, fromNative);
    }

    /// <summary>
    /// The same proof extended to the third reader of the language (decision 084). Three
    /// wrappers, two ecosystems, one grammar: what differs between them is where the text
    /// comes from, which is the wrapper's business and not the grammar's.
    /// </summary>
    [Fact]
    public void TheSameSqlReadFromAMyBatisMapperGivesTheSameQuery()
    {
        var fromDapper = BareSql(FromDapperUnit(Sql));
        var fromMyBatis = BareSql(FromMyBatisMapper(Sql));

        Assert.NotNull(fromDapper);
        Assert.Equal(fromDapper, fromMyBatis);
    }

    /// <summary>
    /// The same proof on the one construct each SQL source spells in its own word rather
    /// than in T-SQL: the collection parameter, which Dapper writes as a bare parameter after
    /// IN (decision 106) and MyBatis as a canonical &lt;foreach&gt; (decision 084). Each
    /// wrapper peels its word off before the grammar, and what the grammar reads is the same
    /// query - the sequence in the signature included. The &lt;sql-query&gt; of NHibernate
    /// has no row here, because its collectionness is stated by a SetParameterList call the
    /// unit does not carry.
    /// </summary>
    [Fact]
    public void ACollectionParameterReadFromDapperAndFromMyBatisGivesTheSameQuery()
    {
        const string select = "SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN ";

        var fromDapper = Typed(FromDapperUnit(select + "@ids"));
        var fromMyBatis = Typed(FromMyBatisMapper(
            select + "<foreach item=\"item\" collection=\"ids\" open=\"(\" separator=\",\" close=\")\">#{item}</foreach>"));

        Assert.NotNull(BareSql(fromDapper));
        Assert.Contains("IN @ids", BareSql(fromDapper));
        Assert.Equal(BareSql(fromDapper), BareSql(fromMyBatis));
        Assert.Equal(Method(fromDapper), Method(fromMyBatis));
        Assert.Contains("IEnumerable<int> ids", Method(fromDapper));
    }

    /// <summary>
    /// The scalar of a parameter is derived from the column it is compared with, which takes
    /// a mapped property (decision 083); the mapping the other facts use states none.
    /// </summary>
    private static AbstractQueryBuilder Typed(AbstractQueryBuilder builder)
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };

        builder.EntityMaps =
        [
            new EntityMap
            {
                Entity = new Entity { Name = "Customer", Properties = [id, name] },
                Table = "Customers",
                Schema = "Sales",
                PropertyMaps =
                [
                    new PropertyMap { Property = id, ColumnName = "CustomerID" },
                    new PropertyMap { Property = name, ColumnName = "CustomerName" },
                ],
            },
        ];

        return builder;
    }

    /// <summary>The generated method, which is named after the query for MyBatis and after the fallback for Dapper - so compared past its name.</summary>
    private static string? Method(AbstractQueryBuilder builder)
    {
        var content = builder.Build().SingleOrDefault(s => s.ContentType == ConversionContentType.CSharpQuery)?.Content;
        return content?[content.IndexOf('(')..];
    }

    /// <summary>
    /// The query's name travels with it, as it does for the HQL form beside it
    /// (decision 081): the generated method is named after the query and not after the
    /// fixed fallback.
    /// </summary>
    [Fact]
    public void ANativeQueryIsNamedByItsElement()
    {
        var builder = FromNativeQuery($"  <sql-query name=\"findRich\">{Sql}</sql-query>");

        var method = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains("FindRich(IDbConnection connection)", method);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// The result mapping is the one fact a native query states that the query IR has no
    /// slot for: every target derives the materialized type from the table itself. Dropping
    /// it leaves the rows as they are, so it is a loss and not a refusal - the query comes
    /// out.
    /// </summary>
    [Fact]
    public void AResultMappingIsALossAndTheQueryStillComesOut()
    {
        var builder = FromNativeQuery(
            $"  <sql-query name=\"findRich\"><return alias=\"c\" class=\"Customer\" />{Sql}</sql-query>");

        var loss = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);

        Assert.Contains("<return>", loss.Reason);
        Assert.Contains("findRich", loss.Reason);
        Assert.NotNull(BareSql(builder));
    }

    /// <summary>
    /// NHibernate's own placeholders are not T-SQL and the grammar must not be taught them
    /// (decision 082), so the wrapper refuses before the reader sees the text. The refusal
    /// names the placeholder rather than leaving a syntax error at some column, which is
    /// what handing it over would produce.
    /// </summary>
    [Theory]
    [InlineData("SELECT {c.*} FROM Sales.Customers c", "{c.*}")]
    [InlineData("SELECT c.CustomerName FROM Sales.Customers c WHERE {c.CreditLimit} > 1000", "{c.CreditLimit}")]
    public void APlaceholderRefusesTheNativeQuery(string sql, string placeholder)
    {
        var builder = FromNativeQuery($"  <sql-query name=\"findRich\">{sql}</sql-query>");

        var refused = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);

        Assert.Contains(placeholder, refused.Reason);
        Assert.Empty(builder.Build());
    }

    /// <summary>
    /// A document may carry both forms at once, and neither reading disturbs the other: a
    /// fresh builder and a fresh reader per query is what keeps two queries of one file
    /// apart.
    /// </summary>
    [Fact]
    public void BothFormsOfANamedQueryAreReadOutOfOneDocument()
    {
        var builders = new List<AbstractQueryBuilder>();

        new NHibernateXmlQueryParser(() =>
        {
            AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
            builders.Add(builder);
            return builder;
        }).Parse(
            ConversionContentType.XML,
            Hbm($"""
                  <query name="findAll">select c.CustomerName from Customer c</query>
                  <sql-query name="findRich">{Sql}</sql-query>
                """));

        Assert.Equal(2, builders.Count);
        Assert.Equal(["findAll", "findRich"], builders.Select(b => b.QueryName));
        Assert.All(builders, b => Assert.NotNull(BareSql(b)));
    }
}
