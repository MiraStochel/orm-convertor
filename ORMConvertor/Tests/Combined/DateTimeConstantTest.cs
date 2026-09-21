using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using EclipseLinkWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// A moment written in a LINQ predicate, read as a typed constant (decision 024) instead of
/// sinking the filter around it (decision 070). C# has no literal for a date, so
/// <c>new DateTime(2025, 1, 1)</c> is the only way a predicate says one - and until it was
/// read, every LINQ query filtering by date came out as a Failure with no artifact, which
/// is what took the date filter out of the EF Core sample.
///
/// What each target does with the constant was already in place: the DateTime scalar has a
/// branch in all four query visitors. What is tested here is therefore both halves at once -
/// that the parser produces the constant, and that the spelling it produces is the one each
/// target can read back.
/// </summary>
public class DateTimeConstantTest
{
    private static EntityMap Customers()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var opened = new Property { Name = "AccountOpenedDate", Type = LangType.Scalar(ScalarType.DateTime) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, opened] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = opened, ColumnName = "AccountOpenedDate" },
            ],
        };
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string predicate)
    {
        builder.EntityMaps = [Customers()];
        new EFCoreLinqQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            $$"""
            public void Query()
            {
                var q = ctx.Customers.Where(c => {{predicate}}).ToList();
            }
            """,
            [Customers()]);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return outputs.Single(s => s.ContentType == type).Content;
    }

    private const string ByDate = "c.AccountOpenedDate > new DateTime(2025, 1, 1)";

    // ---- The constant reaches every target, in that target's own spelling ----------

    [Fact]
    public void AMomentBecomesAQuotedTSqlLiteral()
    {
        var builder = ParseLinq(new DapperSqlQueryBuilder(), ByDate);

        Assert.Contains(
            "WHERE c.AccountOpenedDate > '2025-01-01 00:00:00'",
            Artifact(builder, ConversionContentType.SqlQuery));
    }

    [Fact]
    public void AMomentBecomesAQuotedHqlLiteral()
    {
        var builder = ParseLinq(new NHibernateHqlQueryBuilder(), ByDate);

        Assert.Contains(
            "where c.AccountOpenedDate > '2025-01-01 00:00:00'",
            Artifact(builder, ConversionContentType.HqlQuery));
    }

    /// <summary>
    /// The JDBC escape the specification names for a timestamp literal, and the reason the
    /// constant carries the time of day even when the source stopped at the date: the escape
    /// is defined for no shorter form.
    /// </summary>
    [Theory]
    [InlineData("Hibernate")]
    [InlineData("EclipseLink")]
    public void AMomentBecomesAJdbcTimestampEscape(string profile)
    {
        AbstractQueryBuilder builder = profile == "Hibernate"
            ? new HibernateJpqlQueryBuilder()
            : new EclipseLinkJpqlQueryBuilder();

        Assert.Contains(
            "where c.AccountOpenedDate > {ts '2025-01-01 00:00:00'}",
            Artifact(ParseLinq(builder, ByDate), ConversionContentType.JpqlQuery));
    }

    [Fact]
    public void AMomentComesBackAsAParsedDateTimeInLinq()
    {
        var builder = ParseLinq(new EFCoreLinqQueryBuilder(), ByDate);

        Assert.Contains(
            "c.AccountOpenedDate > DateTime.Parse(\"2025-01-01 00:00:00\")",
            Artifact(builder, ConversionContentType.CSharpQuery));
    }

    // ---- The spellings of the constructor the parser accepts -----------------------

    [Theory]
    [InlineData("new DateTime(2025, 1, 1)", "2025-01-01 00:00:00")]
    [InlineData("new System.DateTime(2025, 1, 1)", "2025-01-01 00:00:00")]
    [InlineData("new global::System.DateTime(2025, 1, 1)", "2025-01-01 00:00:00")]
    [InlineData("new DateTime(2025, 12, 31, 23, 59, 58)", "2025-12-31 23:59:58")]
    [InlineData("new DateTime(2025, 12, 31, 23, 59, 58, 123)", "2025-12-31 23:59:58.123")]
    public void EveryAcceptedSpellingYieldsTheSameIsoConstant(string construction, string expected)
    {
        var builder = ParseLinq(new DapperSqlQueryBuilder(), $"c.AccountOpenedDate > {construction}");

        Assert.Contains($"> '{expected}'", Artifact(builder, ConversionContentType.SqlQuery));
    }

    // ---- What stays unread, and therefore keeps refusing ---------------------------

    /// <summary>
    /// The refusal of decision 070 is unchanged for everything the parser still cannot
    /// evaluate: a computed component, a moment built from ticks, and components that do
    /// not make a real date. Reading the first would mean running the program, and writing
    /// the last would mean inventing a date no calendar has.
    /// </summary>
    [Theory]
    [InlineData("new DateTime(2025, month, 1)")]
    [InlineData("new DateTime(2025, 1 + 1, 1)")]
    [InlineData("new DateTime(638000000000000000L)")]
    [InlineData("new DateTime(2025, 13, 1)")]
    [InlineData("new DateTime(2025, 2, 30)")]
    [InlineData("new TimeSpan(1, 0, 0)")]
    public void AConstructionTheParserCannotEvaluateStillRefusesTheArtifact(string construction)
    {
        var builder = ParseLinq(new DapperSqlQueryBuilder(), $"c.AccountOpenedDate > {construction}");

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Filtering);
    }

    /// <summary>
    /// The sample the tool serves from /samples is the one input every user meets first, so
    /// the filter that decision 070 took out of it is back and has to convert cleanly. The
    /// direction is the one the sample is written for.
    /// </summary>
    [Fact]
    public void TheEFCoreSampleQueryConvertsWithItsDateFilter()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        new EFCoreLinqQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            SampleData.CustomerSampleEFCore.Query,
            [Customers()]);

        Assert.Contains("'2025-01-01 00:00:00'", Artifact(builder, ConversionContentType.SqlQuery));
    }
}
