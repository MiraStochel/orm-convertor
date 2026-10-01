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
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var opened = new Property { Name = "AccountOpenedDate", Type = LangType.Scalar(ScalarType.DateTime) };
        var since = new Property { Name = "Since", Type = LangType.Scalar(ScalarType.Date) };
        var opensAt = new Property { Name = "OpensAt", Type = LangType.Scalar(ScalarType.TimeOfDay) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, name, opened, since, opensAt] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = opened, ColumnName = "AccountOpenedDate" },
                new PropertyMap { Property = since, ColumnName = "Since" },
                new PropertyMap { Property = opensAt, ColumnName = "OpensAt" },
            ],
        };
    }

    private static EntityMap Orders()
    {
        var id = new Property { Name = "OrderID", Type = LangType.Scalar(ScalarType.Int) };
        var customer = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var placed = new Property { Name = "PlacedAt", Type = LangType.Scalar(ScalarType.DateTime) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [id, customer, placed] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "OrderID" },
                new PropertyMap { Property = customer, ColumnName = "CustomerID" },
                new PropertyMap { Property = placed, ColumnName = "PlacedAt" },
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

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string predicate)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new DapperSqlQueryParser(() => builder).Parse(
            ConversionContentType.SqlQuery,
            $"SELECT * FROM Sales.Customers AS c WHERE {predicate}");
        return builder;
    }

    private static AbstractQueryBuilder ParseHql(AbstractQueryBuilder builder, string predicate)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new NHibernateHqlQueryParser(() => builder).Parse(
            ConversionContentType.HqlQuery,
            $"from Customer c where {predicate}",
            [Customers(), Orders()]);
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

    // ---- The other direction: the string T-SQL and HQL write a moment as ----------------

    /// <summary>
    /// T-SQL and HQL have no literal for a moment either - they write it as a string, and
    /// their grammars cannot tell it from one, so the readers carry it as a string. The
    /// builder template types it from the column it is compared with, the way the gate of
    /// decision 083 types a parameter, so that LINQ does not come out comparing a date with
    /// a string; every target then writes the moment with its time of day, as it does for a
    /// LINQ source.
    /// </summary>
    [Theory]
    [InlineData("Dapper")]
    [InlineData("NHibernate")]
    public void AStringComparedWithAMomentColumnIsReadAsAMoment(string source)
    {
        var linq = source == "Dapper"
            ? ParseSql(new EFCoreLinqQueryBuilder(), "c.AccountOpenedDate > '2025-01-01'")
            : ParseHql(new EFCoreLinqQueryBuilder(), "c.AccountOpenedDate > '2025-01-01'");

        Assert.Contains(
            "c.AccountOpenedDate > DateTime.Parse(\"2025-01-01 00:00:00\")",
            Artifact(linq, ConversionContentType.CSharpQuery));

        var jpql = source == "Dapper"
            ? ParseSql(new HibernateJpqlQueryBuilder(), "c.AccountOpenedDate > '2025-01-01'")
            : ParseHql(new HibernateJpqlQueryBuilder(), "c.AccountOpenedDate > '2025-01-01'");

        Assert.Contains(
            "c.AccountOpenedDate > {ts '2025-01-01 00:00:00'}",
            Artifact(jpql, ConversionContentType.JpqlQuery));

        var sql = source == "Dapper"
            ? ParseSql(new DapperSqlQueryBuilder(), "c.AccountOpenedDate > '2025-01-01'")
            : ParseHql(new DapperSqlQueryBuilder(), "c.AccountOpenedDate > '2025-01-01'");

        Assert.Contains(
            "c.AccountOpenedDate > '2025-01-01 00:00:00'",
            Artifact(sql, ConversionContentType.SqlQuery));
    }

    /// <summary>
    /// The ISO 8601 forms accepted, and the one spelling they all become: the time of day
    /// always (the JDBC escape knows no shorter form), the fraction of a second only when
    /// the source wrote one, and the T of the source read as the space of the model.
    /// </summary>
    [Theory]
    [InlineData("2025-01-01", "2025-01-01 00:00:00")]
    [InlineData("2025-12-31 23:59", "2025-12-31 23:59:00")]
    [InlineData("2025-12-31 23:59:58", "2025-12-31 23:59:58")]
    [InlineData("2025-12-31T23:59:58", "2025-12-31 23:59:58")]
    [InlineData("2025-12-31 23:59:58.123", "2025-12-31 23:59:58.123")]
    [InlineData("2025-12-31 23:59:58.1234567", "2025-12-31 23:59:58.1234567")]
    public void EveryAcceptedStringYieldsTheSameIsoMoment(string written, string expected)
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder(), $"c.AccountOpenedDate >= '{written}'");

        Assert.Contains($">= DateTime.Parse(\"{expected}\")", Artifact(builder, ConversionContentType.CSharpQuery));
    }

    /// <summary>
    /// The two temporal scalars of decision 071 with a literal of their own take the same
    /// route: a string against a Date column is a date, against a TimeOfDay column a time
    /// of day, each in the spelling the visitors of decision 071 write it in.
    /// </summary>
    [Fact]
    public void AStringComparedWithADateOrTimeColumnTakesThatScalar()
    {
        var date = ParseSql(new EFCoreLinqQueryBuilder(), "c.Since >= '2024-01-31'");
        Assert.Contains("c.Since >= DateOnly.Parse(\"2024-01-31\")", Artifact(date, ConversionContentType.CSharpQuery));

        var dateJpql = ParseSql(new HibernateJpqlQueryBuilder(), "c.Since >= '2024-01-31'");
        Assert.Contains("c.Since >= {d '2024-01-31'}", Artifact(dateJpql, ConversionContentType.JpqlQuery));

        var time = ParseSql(new EFCoreLinqQueryBuilder(), "c.OpensAt < '08:30'");
        Assert.Contains("c.OpensAt < TimeOnly.Parse(\"08:30:00\")", Artifact(time, ConversionContentType.CSharpQuery));

        var timeJpql = ParseSql(new HibernateJpqlQueryBuilder(), "c.OpensAt < '08:30'");
        Assert.Contains("c.OpensAt < {t '08:30:00'}", Artifact(timeJpql, ConversionContentType.JpqlQuery));
    }

    /// <summary>
    /// A list of values is the right side of IN (decision 074) and the column on the left
    /// types its elements, so a list of nothing but strings against a moment column is a
    /// list of moments; and a column inside a subquery finds its entity the way a parameter
    /// in one does, so the string in a nested scope is typed as well.
    /// </summary>
    [Fact]
    public void AListOfStringsAndAStringInsideASubqueryAreTypedToo()
    {
        var list = ParseSql(new EFCoreLinqQueryBuilder(), "c.AccountOpenedDate IN ('2025-01-01', '2025-01-02')");

        Assert.Contains(
            "DateTime.Parse(\"2025-01-01 00:00:00\"), DateTime.Parse(\"2025-01-02 00:00:00\")",
            Artifact(list, ConversionContentType.CSharpQuery));

        var nested = ParseSql(
            new EFCoreLinqQueryBuilder(),
            "c.CustomerID IN (SELECT o.CustomerID FROM Sales.Orders AS o WHERE o.PlacedAt > '2025-01-01')");

        Assert.Contains(
            "o.PlacedAt > DateTime.Parse(\"2025-01-01 00:00:00\")",
            Artifact(nested, ConversionContentType.CSharpQuery));
    }

    /// <summary>
    /// A subquery compares the one value it projects (decision 061), so a string against a
    /// subquery over a moment column is a moment as much as one against the column itself -
    /// until 2026-10-01 it stayed a string, and the LINQ target compared a date with it.
    /// </summary>
    [Theory]
    [InlineData("Dapper")]
    [InlineData("NHibernate")]
    public void AStringComparedWithASubqueryOverAMomentColumnIsReadAsAMoment(string source)
    {
        AbstractQueryBuilder Parse(AbstractQueryBuilder builder) => source == "Dapper"
            ? ParseSql(builder, "(SELECT MAX(o.PlacedAt) FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID) > '2025-01-01'")
            : ParseHql(builder, "(select max(o.PlacedAt) from Order o where o.CustomerID = c.CustomerID) > '2025-01-01'");

        Assert.Contains(
            ".Max(o => o.PlacedAt) > DateTime.Parse(\"2025-01-01 00:00:00\")",
            Artifact(Parse(new EFCoreLinqQueryBuilder()), ConversionContentType.CSharpQuery));

        Assert.Contains(
            ") > {ts '2025-01-01 00:00:00'}",
            Artifact(Parse(new HibernateJpqlQueryBuilder()), ConversionContentType.JpqlQuery));
    }

    /// <summary>
    /// Only the column decides: the same string against a string column is a string, and
    /// so is a LIKE pattern whichever column it matches (decision 051).
    /// </summary>
    [Fact]
    public void AStringComparedWithAStringColumnStaysAString()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder(), "c.CustomerName = '2025-01-01'");

        Assert.Contains("c.CustomerName == \"2025-01-01\"", Artifact(builder, ConversionContentType.CSharpQuery));
    }

    /// <summary>
    /// A string the column's scalar cannot read is refused with a record, not carried: the
    /// database would refuse it at run time, and a LINQ target could not even compile it.
    /// A time of day against a Date column is the same refusal - the database would
    /// truncate it in silence, which is the row set changing (decision 053).
    /// </summary>
    [Theory]
    [InlineData("c.AccountOpenedDate > 'yesterday'")]
    [InlineData("c.AccountOpenedDate > '2025-13-01'")]
    [InlineData("c.AccountOpenedDate > '20250101'")]
    [InlineData("c.Since > '2025-01-01 10:00'")]
    [InlineData("c.OpensAt > '2025-01-01'")]
    [InlineData("(SELECT MAX(o.PlacedAt) FROM Sales.Orders AS o) > 'yesterday'")]
    public void AStringThatIsNoMomentRefusesTheArtifact(string predicate)
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder(), predicate);

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Filtering);
    }
}
