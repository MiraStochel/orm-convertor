using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The string methods of a LINQ predicate read back as LIKE - the table of decision 051
/// inverted, so that EF Core is a source of a pattern and its own StartsWith survives the
/// identity direction. StartsWith, EndsWith and Contains over a column are the anchored
/// patterns; a wildcard in their argument is a literal character under EF Core's provider
/// and goes out escaped with the canonical escape (decision 102 gave it a place), verbatim
/// under NHibernate's, which does not escape; <c>EF.Functions.Like</c> is LIKE with the
/// pattern as written, a literal or a parameter, with or without an escape. What no target
/// could write faithfully - a value from the enclosing scope joined with a wildcard, an
/// overload the provider does not translate, an escape that is not a literal - is refused
/// by name (decision 070).
/// </summary>
public class LinqStringMethodTest
{
    private static EntityMap Customers()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var code = new Property { Name = "Code", Type = LangType.Scalar(ScalarType.String) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, name, code] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = code, ColumnName = "Code" },
            ],
        };
    }

    private static string Method(string predicate, bool nhibernate = false)
        => $$"""
        public void Query()
        {
            var q = {{(nhibernate ? "session.Query<Customer>()" : "ctx.Customers")}}.Where(c => {{predicate}}).ToList();
        }
        """;

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string predicate, bool nhibernate = false)
    {
        builder.EntityMaps = [Customers()];
        LinqParsing.LinqQueryParser parser = nhibernate
            ? new NHibernateLinqQueryParser(() => builder)
            : new EFCoreLinqQueryParser(() => builder);
        parser.Parse(ConversionContentType.CSharpQuery, Method(predicate, nhibernate), [Customers()]);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return outputs.Single(s => s.ContentType == type).Content;
    }

    private static string Sql(string predicate, bool nhibernate = false)
        => Artifact(Parse(new DapperSqlQueryBuilder(), predicate, nhibernate), ConversionContentType.SqlQuery);

    private static string Hql(string predicate) => Artifact(Parse(new NHibernateHqlQueryBuilder(), predicate), ConversionContentType.HqlQuery);

    private static string Jpql(string predicate) => Artifact(Parse(new HibernateJpqlQueryBuilder(), predicate), ConversionContentType.JpqlQuery);

    private static string CSharp(string predicate) => Artifact(Parse(new EFCoreLinqQueryBuilder(), predicate), ConversionContentType.CSharpQuery);

    private static ConversionRecord AssertRefused(string predicate, QueryFeature feature)
    {
        var builder = Parse(new DapperSqlQueryBuilder(), predicate);
        Assert.Empty(builder.Build());
        var record = builder.Records.FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
        Assert.NotNull(record);
        return record;
    }

    // ---- carried ----------------------------------------------------------------------

    /// <summary>
    /// The anchors follow the method; a wildcard in the argument is a literal character to
    /// EF Core and leaves escaped, the escape escaped with itself where the argument carries
    /// it, and an argument without a wildcard needs no clause even when it carries the
    /// escape character.
    /// </summary>
    [Theory]
    [InlineData("c.CustomerName.StartsWith(\"A\")", "c.CustomerName LIKE 'A%'")]
    [InlineData("c.CustomerName.EndsWith(\"A\")", "c.CustomerName LIKE '%A'")]
    [InlineData("c.CustomerName.Contains(\"A\")", "c.CustomerName LIKE '%A%'")]
    [InlineData("c.CustomerName.StartsWith('A')", "c.CustomerName LIKE 'A%'")]
    [InlineData("c.CustomerName.StartsWith(\"A_\")", "c.CustomerName LIKE 'A!_%' ESCAPE '!'")]
    [InlineData("c.CustomerName.Contains(\"50%\")", "c.CustomerName LIKE '%50!%%' ESCAPE '!'")]
    [InlineData("c.CustomerName.EndsWith(\"[x]\")", "c.CustomerName LIKE '%![x]' ESCAPE '!'")]
    [InlineData("c.CustomerName.StartsWith(\"A!_\")", "c.CustomerName LIKE 'A!!!_%' ESCAPE '!'")]
    [InlineData("c.CustomerName.StartsWith(\"A!\")", "c.CustomerName LIKE 'A!%'")]
    [InlineData("!c.CustomerName.StartsWith(\"A\")", "NOT (c.CustomerName LIKE 'A%')")]
    [InlineData("EF.Functions.Like(c.CustomerName, \"A%B\")", "c.CustomerName LIKE 'A%B'")]
    [InlineData("EF.Functions.Like(c.CustomerName, \"A!_%\", \"!\")", "c.CustomerName LIKE 'A!_%' ESCAPE '!'")]
    [InlineData("Microsoft.EntityFrameworkCore.EF.Functions.Like(c.CustomerName, \"A%B\")", "c.CustomerName LIKE 'A%B'")]
    public void AStringMethodReadsAsLike(string predicate, string expected)
    {
        Assert.Contains(expected, Sql(predicate), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEscapedPatternReachesEveryTarget()
    {
        const string predicate = "c.CustomerName.StartsWith(\"A_\")";

        Assert.Contains("c.CustomerName LIKE 'A!_%' ESCAPE '!'", Sql(predicate), StringComparison.Ordinal);
        Assert.Contains("c.CustomerName like 'A!_%' escape '!'", Hql(predicate), StringComparison.Ordinal);
        Assert.Contains("c.CustomerName like 'A!_%' escape '!'", Jpql(predicate), StringComparison.Ordinal);
        Assert.Contains("c.CustomerName.StartsWith(\"A_\")", CSharp(predicate), StringComparison.Ordinal);
    }

    /// <summary>
    /// The identity direction: what the EF Core builder writes for a pattern (decision 051)
    /// is what this reader reads, so EF Core → EF Core over LIKE is the same text - and a
    /// core that carries the escape character itself comes back without it, because the
    /// split of decision 051 reads past the escape.
    /// </summary>
    [Theory]
    [InlineData("c.CustomerName.StartsWith(\"A\")")]
    [InlineData("c.CustomerName.EndsWith(\"A\")")]
    [InlineData("c.CustomerName.Contains(\"A\")")]
    [InlineData("c.CustomerName.StartsWith(\"A!_\")")]
    [InlineData("EF.Functions.Like(c.CustomerName, \"A%B\")")]
    [InlineData("EF.Functions.Like(c.CustomerName, \"A!%_B%\", \"!\")")]
    public void EFCoreRoundTripsItsOwnPattern(string predicate)
    {
        Assert.Contains(predicate, CSharp(predicate), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pattern function whose pattern the split of decision 051 reads exactly comes back
    /// as the string method - the same query, in the form the builder prefers.
    /// </summary>
    [Fact]
    public void AnExactPatternOfThePatternFunctionComesBackAsTheStringMethod()
    {
        Assert.Contains("c.CustomerName.StartsWith(\"A%B\")", CSharp("EF.Functions.Like(c.CustomerName, \"A!%B%\", \"!\")"), StringComparison.Ordinal);
    }

    /// <summary>The pattern function takes the pattern from the caller; the escape stays a fact the query states.</summary>
    [Fact]
    public void AParameterIsThePatternOfThePatternFunction()
    {
        Assert.Contains("c.CustomerName LIKE @p", Sql("EF.Functions.Like(c.CustomerName, p)"), StringComparison.Ordinal);

        var linq = CSharp("EF.Functions.Like(c.CustomerName, p)");
        Assert.Contains("EF.Functions.Like(c.CustomerName, p)", linq, StringComparison.Ordinal);
        Assert.Contains("string p", linq, StringComparison.Ordinal);

        Assert.Contains("c.CustomerName LIKE @p ESCAPE '!'", Sql("EF.Functions.Like(c.CustomerName, p, \"!\")"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Contains is three things by its receiver: a pattern over a column, IN over a list of
    /// values (decision 074), IN over a nested chain (decision 061). The column used to be
    /// taken for the head of a chain, because a lambda parameter and a context look alike.
    /// </summary>
    [Fact]
    public void ContainsIsToldApartByItsReceiver()
    {
        var pattern = Sql("c.CustomerName.Contains(\"A\")");
        Assert.Contains("LIKE '%A%'", pattern, StringComparison.Ordinal);
        Assert.DoesNotContain(" IN ", pattern, StringComparison.Ordinal);

        Assert.Contains("c.CustomerID IN (1, 2)", Sql("new[] { 1, 2 }.Contains(c.CustomerID)"), StringComparison.Ordinal);
        Assert.Contains("c.CustomerID IN (SELECT", Sql("ctx.Customers.Select(o => o.CustomerID).Contains(c.CustomerID)"), StringComparison.Ordinal);
    }

    /// <summary>
    /// NHibernate's provider concatenates the argument with the wildcard as written, so the
    /// same call means another query there - the underscore matches any character - and is
    /// read as it means, without an escape.
    /// </summary>
    [Fact]
    public void NHibernatesProviderDoesNotEscapeTheArgument()
    {
        var sql = Sql("c.CustomerName.StartsWith(\"A_\")", nhibernate: true);

        Assert.Contains("c.CustomerName LIKE 'A_%'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ESCAPE", sql, StringComparison.Ordinal);
    }

    // ---- a bound argument (decision 107) -----------------------------------------------

    /// <summary>
    /// A value from the enclosing scope as the argument of a string method is the pattern
    /// that value joined with the wildcard (decision 107): LIKE over a concatenation, with
    /// the value escaped as EF Core's provider escapes it at run time, so that a wildcard in
    /// it stays literal in every target. Refused until 2026-09-30, because no operand carried
    /// an expression.
    /// </summary>
    [Fact]
    public void AParameterArgumentOfAStringMethodIsLikeOverAConcatenation()
    {
        var sql = Sql("c.CustomerName.StartsWith(prefix)");

        Assert.Contains("c.CustomerName LIKE REPLACE(REPLACE(REPLACE(REPLACE(@prefix, '!', '!!'), '%', '!%'), '_', '!_'), '[', '![') + '%' ESCAPE '!'", sql, StringComparison.Ordinal);
        Assert.Contains("string prefix", Artifact(Parse(new DapperSqlQueryBuilder(), "c.CustomerName.StartsWith(prefix)"), ConversionContentType.CSharpQuery), StringComparison.Ordinal);
    }

    /// <summary>A column as the argument is an operand like any other; the pattern is the column escaped and anchored.</summary>
    [Fact]
    public void AColumnArgumentOfAStringMethodIsLikeOverAConcatenation()
    {
        Assert.Contains("c.CustomerName LIKE REPLACE(REPLACE(REPLACE(REPLACE(c.Code, '!', '!!'), '%', '!%'), '_', '!_'), '[', '![') + '%' ESCAPE '!'", Sql("c.CustomerName.StartsWith(c.Code)"), StringComparison.Ordinal);
    }

    // ---- refused ----------------------------------------------------------------------

    [Fact]
    public void AnOverloadTheProviderDoesNotTranslateRefusesTheArtifact()
    {
        var record = AssertRefused("c.CustomerName.StartsWith(\"A\", StringComparison.OrdinalIgnoreCase)", QueryFeature.Filtering);
        Assert.Contains("overload", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>The same line the SQL and JPQL readers hold for <c>ESCAPE @e</c> (decision 102).</summary>
    [Fact]
    public void AnEscapeThatIsNotALiteralRefusesTheArtifact()
    {
        var record = AssertRefused("EF.Functions.Like(c.CustomerName, \"A%\", e)", QueryFeature.Filtering);
        Assert.Contains("'e'", record.Reason, StringComparison.Ordinal);
    }
}
