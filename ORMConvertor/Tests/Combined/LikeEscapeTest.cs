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
/// The escape character of LIKE, end to end (decision 102): it travels on the comparison
/// as the character the query wrote, the SQL, HQL and JPQL parsers read their escape
/// clause, the three SQL-shaped targets write it back, and the LINQ target reads the
/// pattern past it - a wildcard behind the escape is a literal, so the split of decision
/// 051 stays exact and the core goes out without its escapes. What is not a character
/// the query states - a variable, a parameter - is refused, and the template holds the
/// escape to LIKE and to one character.
/// </summary>
public class LikeEscapeTest
{
    private static EntityMap Customers()
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

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = [Customers()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseHql(AbstractQueryBuilder builder, string hql)
    {
        builder.EntityMaps = [Customers()];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, [Customers()]);
        return builder;
    }

    private static AbstractQueryBuilder ParseJpql(AbstractQueryBuilder builder, string jpql)
    {
        builder.EntityMaps = [Customers()];
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, [Customers()]);
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

    private static string Like(string pattern, string escape)
        => $"SELECT * FROM Sales.Customers AS c WHERE c.CustomerName LIKE {pattern} ESCAPE '{escape}'";

    // ---- carried ----------------------------------------------------------------------

    [Fact]
    public void SqlEscapeReachesEveryTarget()
    {
        var sql = Like("'A!_%'", "!");

        Assert.Contains("c.CustomerName LIKE 'A!_%' ESCAPE '!'", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains("c.CustomerName like 'A!_%' escape '!'", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));
        Assert.Contains("c.CustomerName like 'A!_%' escape '!'", Jpql(ParseSql(new HibernateJpqlQueryBuilder(), sql)));
        Assert.Contains("c.CustomerName.StartsWith(\"A_\")", CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
    }

    /// <summary>
    /// The split of decision 051 read past the escape: an escaped wildcard is a literal and
    /// leaves without its escape, because EF Core escapes the argument of a string method
    /// itself; an unescaped wildcard in the core, or an escape with nothing behind it,
    /// keeps the text as written under EF.Functions.Like with the escape as its third
    /// argument.
    /// </summary>
    [Theory]
    [InlineData("'A!_%'", "c.CustomerName.StartsWith(\"A_\")")]
    [InlineData("'!%A%'", "c.CustomerName.StartsWith(\"%A\")")]
    [InlineData("'%A!%'", "c.CustomerName.EndsWith(\"A%\")")]
    [InlineData("'%A!_B%'", "c.CustomerName.Contains(\"A_B\")")]
    [InlineData("'A!_'", "c.CustomerName == \"A_\"")]
    [InlineData("'A_!%%'", "EF.Functions.Like(c.CustomerName, \"A_!%%\", \"!\")")]
    [InlineData("'A!'", "EF.Functions.Like(c.CustomerName, \"A!\", \"!\")")]
    public void LinqSplitsThePatternPastTheEscape(string pattern, string expected)
    {
        Assert.Contains(expected, CSharp(ParseSql(new EFCoreLinqQueryBuilder(), Like(pattern, "!"))));
    }

    [Fact]
    public void AParameterPatternKeepsTheEscape()
    {
        var linq = CSharp(ParseSql(new EFCoreLinqQueryBuilder(), Like("@p", "!")));

        Assert.Contains("EF.Functions.Like(c.CustomerName, p, \"!\")", linq);
        Assert.Contains("string p", linq);
    }

    [Fact]
    public void HqlAndJpqlReadTheEscapeAndRoundTrip()
    {
        const string hql = "from Customer c where c.CustomerName like 'A!_%' escape '!'";
        const string jpql = "select c from Customer c where c.CustomerName like 'A!_%' escape '!'";

        Assert.Contains("c.CustomerName LIKE 'A!_%' ESCAPE '!'", Sql(ParseHql(new DapperSqlQueryBuilder(), hql)));
        Assert.Contains("c.CustomerName like 'A!_%' escape '!'", Hql(ParseHql(new NHibernateHqlQueryBuilder(), hql)));
        Assert.Contains("c.CustomerName.StartsWith(\"A_\")", CSharp(ParseJpql(new EFCoreLinqQueryBuilder(), jpql)));
        Assert.Contains("c.CustomerName like 'A!_%' escape '!'", Jpql(ParseJpql(new HibernateJpqlQueryBuilder(), jpql)));
    }

    // ---- refused ----------------------------------------------------------------------

    /// <summary>An escape the caller would supply is no character the query states.</summary>
    [Fact]
    public void AnEscapeThatIsNotALiteralRefusesTheArtifact()
    {
        var sql = ParseSql(new DapperSqlQueryBuilder(), "SELECT * FROM Sales.Customers AS c WHERE c.CustomerName LIKE 'A!_%' ESCAPE @e");
        Assert.Contains("@e", AssertRefused(sql, QueryFeature.Filtering).Reason, StringComparison.Ordinal);

        var jpql = ParseJpql(new DapperSqlQueryBuilder(), "select c from Customer c where c.CustomerName like 'A!_%' escape :e");
        Assert.Contains(":e", AssertRefused(jpql, QueryFeature.Filtering).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGateHoldsTheEscapeToLikeAndToOneCharacter()
    {
        var underEquality = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        underEquality.From("Sales.Customers", "c");
        underEquality.Where(new ComparisonCondition(
            QueryOperand.Column("c", "CustomerName"),
            ComparisonOperator.Equal,
            QueryOperand.Value(QueryConstant.Of("A", ScalarType.String)),
            Escape: "!"));
        Assert.Contains("only on a LIKE", AssertRefused(underEquality, QueryFeature.Filtering).Reason, StringComparison.Ordinal);

        var twoCharacters = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        twoCharacters.From("Sales.Customers", "c");
        twoCharacters.Where(new ComparisonCondition(
            QueryOperand.Column("c", "CustomerName"),
            ComparisonOperator.Like,
            QueryOperand.Value(QueryConstant.Of("A%", ScalarType.String)),
            Escape: "!!"));
        Assert.Contains("single character", AssertRefused(twoCharacters, QueryFeature.Filtering).Reason, StringComparison.Ordinal);
    }
}
