using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Driver;
using NHibernate.Engine;
using OrmConvertor;

namespace Tests.NHibernate;

/// <summary>
/// What NHibernate 5.7.0 reads as its own in the text of a native query, measured by the
/// parameters its own reading of the text finds - the metadata CreateSQLQuery works from, no
/// connection opened -, and what the builder does about it (decision 217). The reading does
/// not know the square brackets of T-SQL: inside a bracketed name a colon opens a named
/// parameter, whatever follows it, a ? is a positional one, and an apostrophe opens a
/// literal, so that a parameter after it is not found. Neither a backslash nor a doubled
/// mark keeps any of them text, so <c>NHibernateNativeSqlQueryBuilder</c> refuses a query
/// whose bracketed name holds one. A fact the reading stops holding on an upgrade shows up
/// here, beside the step that assumes it.
/// </summary>
public class NHibernateNativeSqlReadingTest
{
    private static readonly Lazy<ISessionFactoryImplementor> Factory = new(() =>
    {
        var configuration = new Configuration();
        configuration.SetProperty(global::NHibernate.Cfg.Environment.Dialect, typeof(MsSql2012Dialect).AssemblyQualifiedName);
        configuration.SetProperty(global::NHibernate.Cfg.Environment.ConnectionDriver, typeof(MicrosoftDataSqlClientDriver).AssemblyQualifiedName);
        configuration.SetProperty(global::NHibernate.Cfg.Environment.Hbm2ddlKeyWords, "none");
        return (ISessionFactoryImplementor)configuration.BuildSessionFactory();
    });

    /// <summary>The text, the named parameters NHibernate reads in it and the number of positional ones.</summary>
    public static TheoryData<string, string[], int> Readings => new()
    {
        // Inside a bracketed name: a colon is a named parameter whatever follows it.
        { "SELECT 1 AS [a:b]", ["b"], 0 },
        { "SELECT 1 AS [a: b]", [""], 0 },
        { "SELECT 1 AS [a::b]", [":b"], 0 },
        { "SELECT 1 AS [a:b] WHERE 1 = :id", ["b", "id"], 0 },

        // A ? is a positional parameter.
        { "SELECT 1 AS [a?b]", [], 1 },
        { "SELECT 1 AS [a?b] WHERE 1 = :id", ["id"], 1 },

        // An apostrophe opens a literal: the parameter after it is not found - unless a second
        // name happens to close it again.
        { "SELECT 1 AS [it's] WHERE 1 = :id", [], 0 },
        { "SELECT 1 AS [it's], 2 AS [o'k] WHERE 1 = :id", ["id"], 0 },

        // Nothing escapes them.
        { @"SELECT 1 AS [a\:b]", ["b"], 0 },
        { @"SELECT 1 AS [a\?b]", [], 1 },
        { "SELECT 1 AS [a??b]", [], 2 },

        // Quotes are read the same way as brackets.
        { "SELECT 1 AS \"a:b\"", ["b\""], 0 },
        { "SELECT 1 AS \"a?b\"", [], 1 },

        // What is text: a quote, a backslash and a brace inside a bracketed name, the content
        // of a literal and of a comment.
        { "SELECT 1 AS [a\"b] WHERE 1 = :id", ["id"], 0 },
        { @"SELECT 1 AS [c\d], 2 AS [a{b}c] WHERE 1 = :id", ["id"], 0 },
        { "SELECT 'a:b' AS x, 'c?d' AS y WHERE 1 = :id", ["id"], 0 },
        { "SELECT 1 AS x /* a:b ? */ WHERE 1 = :id", ["id"], 0 },
        { "SELECT 1 AS x -- a:b ?\n WHERE 1 = :id", ["id"], 0 },
    };

    [Theory]
    [MemberData(nameof(Readings))]
    public void NHibernateReadsABracketedNameLikeTheRestOfTheText(string sql, string[] named, int positional)
    {
        var metadata = Factory.Value.QueryPlanCache.GetSQLParameterMetadata(sql);

        Assert.Equal(named.Order(StringComparer.Ordinal), metadata.NamedParameterNames.Order(StringComparer.Ordinal));
        Assert.Equal(positional, metadata.OrdinalParameterCount);
    }

    private const string Badge = """
        namespace Shop;

        public class Badge
        {
            public int Id { get; set; }

            public string? Name { get; set; }
        }
        """;

    private static ConversionResult Convert(string sql)
        => ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.NHibernate,
            [
                new ConversionSource { Name = "Badge.cs", ContentType = ConversionContentType.CSharp, Content = Badge },
                new ConversionSource { Name = "Query.sql", ContentType = ConversionContentType.SqlQuery, Content = sql },
            ]);

    /// <summary>
    /// The alias is no member name, so the query goes out in native SQL; a name NHibernate
    /// would read a parameter or a literal into has no spelling it runs, and the query is
    /// refused with a record that names the name.
    /// </summary>
    [Theory]
    [InlineData("[a:b]", ':')]
    [InlineData("[a?b]", '?')]
    [InlineData("[it's]", '\'')]
    public void ANameHoldingWhatNHibernateReadsRefusesTheQuery(string name, char read)
    {
        var result = Convert($"SELECT b.Name AS {name} FROM Badge b WHERE b.Id = @id");

        Assert.DoesNotContain(result.Sources, s => s.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.SqlQuery);
        var failure = Assert.Single(result.Records, r => r is { Kind: ConversionRecordKind.Failure, Feature: QueryFeature.Projection });
        Assert.Contains($"names {name}", failure.Reason);
        Assert.Contains($"reads the {read} inside a bracketed name", failure.Reason);
        Assert.Contains("no artifact was generated", failure.Reason);
    }

    /// <summary>What NHibernate reads as text in a bracketed name goes out as it stands, the parameter beside it bound.</summary>
    [Fact]
    public void TheOtherCharactersOfABracketedNameGoOutAsTheyStand()
    {
        var result = Convert(@"SELECT b.Name AS [c\d], b.Id AS [a{b}c] FROM Badge b WHERE b.Id = @id AND b.Name <> 'x:y?z'");
        var method = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.DoesNotContain(result.Records, r => r is { Kind: ConversionRecordKind.Failure, Feature: not null });
        Assert.Contains("session.CreateSQLQuery(", method);
        Assert.Contains(@"AS [c\d]", method);
        Assert.Contains("AS [a{b}c]", method);
        Assert.Contains("b.Id = :id", method);
        Assert.Contains("'x:y?z'", method);
    }
}
