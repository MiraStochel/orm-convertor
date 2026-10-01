using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Dapper;
using Model;
using OrmConvertor;
using SampleData;
using Tests.Database;

namespace Tests.Combined;

/// <summary>
/// The LDBC query catalog (decision 110). The page that shows it claims, per query, whether
/// the tool translates it as the specification defines it, translates a stated
/// simplification of it, or does not translate it - and this class is where each claim is
/// held. A query with a text is translated from Dapper into the other five frameworks with the
/// LDBC tables as the catalog, which is how the page means it: those tables are created by
/// the fixture from the very scripts the container's LdbcSnb database is loaded with.
///
/// The claims are about the first verification level only - the target artifact is produced
/// without a Failure record, or it is refused with one. Whether a translated query returns
/// what the source returns is the fourth level, and it is not claimed here: the catalog runs
/// over the scale factor 1 data set, which no suite loads. What the suite does run is every
/// text, against the empty tables, so that a text the page shows is T-SQL over those tables
/// and not a sketch of it.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class LdbcCatalogTest(TestSchemaFixture fixture)
{
    private static readonly ORMEnum[] Targets = [.. Enum.GetValues<ORMEnum>().Where(orm => orm != ORMEnum.Dapper)];

    private static readonly IReadOnlySet<string> LdbcTables =
        LdbcSnbSample.Entities.Select(entity => Path.GetFileNameWithoutExtension(entity.FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static LdbcQuery Query(string key) => LdbcSnbSample.Queries.Single(query => query.Key == key);

    public static TheoryData<string, ORMEnum> TranslatedQueries()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var query in LdbcSnbSample.Queries.Where(query => query.Translation != LdbcTranslation.NotTranslated))
        {
            foreach (var target in Targets)
            {
                data.Add(query.Key, target);
            }
        }

        return data;
    }

    public static TheoryData<string> RefusedTexts()
        => [.. LdbcSnbSample.Queries.Where(query => query.Translation == LdbcTranslation.NotTranslated && query.Sql is not null).Select(query => query.Key)];

    public static TheoryData<string> Texts()
        => [.. LdbcSnbSample.Queries.Where(query => query.Sql is not null).Select(query => query.Key)];

    /// <summary>
    /// All 41 read queries of the specification are in the catalog, each once, including the
    /// ones the tool does not translate: a catalog that left those out would claim coverage by
    /// omission, which is the one thing the page exists not to do.
    /// </summary>
    [Fact]
    public void TheCatalogHoldsEveryReadQueryOfTheSpecification()
    {
        static IReadOnlyList<int> Numbers(LdbcWorkload workload)
            => [.. LdbcSnbSample.Queries.Where(query => query.Workload == workload).Select(query => query.Number)];

        Assert.Equal(Enumerable.Range(1, 7), Numbers(LdbcWorkload.InteractiveShort));
        Assert.Equal(Enumerable.Range(1, 14), Numbers(LdbcWorkload.InteractiveComplex));
        Assert.Equal(Enumerable.Range(1, 20), Numbers(LdbcWorkload.BusinessIntelligence));

        var keys = LdbcSnbSample.Queries.Select(query => query.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Matches("^(is|ic|bi)[0-9]+$", key));
    }

    /// <summary>
    /// A translated query has a text, and every query says in its note what it exercises,
    /// what it leaves out or why it is not translated; every parameter the text names has an
    /// example value, and no example value names a parameter the text does not use.
    /// </summary>
    [Fact]
    public void EveryQueryStatesItselfCompletely()
    {
        foreach (var query in LdbcSnbSample.Queries)
        {
            Assert.False(string.IsNullOrWhiteSpace(query.Title), $"{query.Key} has no title.");
            Assert.False(string.IsNullOrWhiteSpace(query.Note), $"{query.Key} has no note.");

            if (query.Translation != LdbcTranslation.NotTranslated)
            {
                Assert.False(string.IsNullOrWhiteSpace(query.Sql), $"{query.Key} is translated but has no text.");
            }

            if (query.Sql is null)
            {
                continue;
            }

            var named = Regex.Matches(query.Sql, @"@(\w+)").Select(match => match.Groups[1].Value).ToHashSet();
            var declared = query.Parameters.Select(parameter => parameter.Name).ToHashSet();
            Assert.True(named.SetEquals(declared), $"{query.Key} names @{string.Join(", @", named)} but declares {string.Join(", ", declared)}.");
        }
    }

    /// <summary>
    /// The claim of the page for a translated query: from Dapper into this framework, with
    /// the LDBC tables as the catalog, the query artifact is produced and nothing is refused.
    /// Losses, conventions and completions from the catalog are what the records are for; a
    /// Failure would contradict the claim. A target the query names as refusing it is held to
    /// the opposite: a Failure and no query artifact, so that a refusal the tool outgrows turns
    /// up here instead of staying on the page.
    /// </summary>
    [Theory]
    [MemberData(nameof(TranslatedQueries))]
    public void EveryTranslatedQueryReachesEveryTarget(string key, ORMEnum target)
    {
        fixture.SkipIfUnavailable();
        var query = Query(key);

        var result = ConversionHandler.Convert(ORMEnum.Dapper, target, Units(query), fixture.CatalogReader);
        var failures = result.Records.Where(record => record.Kind == ConversionRecordKind.Failure).ToList();

        if (query.Refusals.Any(refusal => refusal.Target == target))
        {
            Assert.True(failures.Count > 0, $"{key} was expected to be refused by {target}, but no Failure was recorded.");
            Assert.DoesNotContain(result.Sources, source => source.ContentType.IsQuery());
            return;
        }

        Assert.True(
            failures.Count == 0,
            $"{key} into {target} fails:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures.Select(failure => $"  {failure.Entity} {failure.Feature}: {failure.Reason}")));
        Assert.Contains(result.Sources, source => source.ContentType.IsQuery());
    }

    /// <summary>
    /// The claim of the page for a query it shows but does not translate: the natural text is
    /// refused with a Failure record in every direction, and no query artifact comes out.
    /// If this starts failing, the tool learned something and the page is wrong in the
    /// modest direction - the catalog is to be updated, not the test loosened.
    /// </summary>
    [Theory]
    [MemberData(nameof(RefusedTexts))]
    public void EveryRefusedTextIsRefusedInEveryDirection(string key)
    {
        fixture.SkipIfUnavailable();
        var query = Query(key);

        foreach (var target in Targets)
        {
            var result = ConversionHandler.Convert(ORMEnum.Dapper, target, Units(query), fixture.CatalogReader);

            Assert.True(
                result.Records.Any(record => record.Kind == ConversionRecordKind.Failure),
                $"{key} into {target} was expected to be refused, but no Failure was recorded.");
            Assert.DoesNotContain(result.Sources, source => source.ContentType.IsQuery());
        }
    }

    /// <summary>
    /// Every text the page shows runs against the LDBC tables with its example parameters -
    /// the empty tables of the fixture, so what is checked is that the text is T-SQL over this
    /// schema, with names, types and groupings that SQL Server accepts, not what it returns.
    /// The texts name their tables without a schema, as they do over LdbcSnb, and are pointed
    /// at the fixture's schema here.
    /// </summary>
    [Theory]
    [MemberData(nameof(Texts))]
    public void EveryTextRunsOverTheLdbcTables(string key)
    {
        fixture.SkipIfUnavailable();
        var query = Query(key);

        using var connection = fixture.OpenConnection();

        // The statement is the assertion: SQL Server compiles and runs it, or the call throws.
        // A count over empty tables still returns its one row, so the rows prove nothing.
        _ = connection.Query(InSchema(query.Sql!, fixture.LdbcSchemaName), Arguments(query), commandTimeout: 60).ToList();
    }

    private static List<ConversionSource> Units(LdbcQuery query) =>
    [
        .. LdbcSnbSample.Entities.Select(entity => new ConversionSource
        {
            Name = entity.FileName,
            ContentType = ConversionContentType.CSharp,
            Content = entity.Content,
        }),
        new ConversionSource { Name = $"{query.Key}.sql", ContentType = ConversionContentType.SqlQuery, Content = query.Sql! },
    ];

    /// <summary>
    /// Qualifies every LDBC table the text reads with the schema. The texts are written with
    /// an alias after every table (FROM Person AS p), so a table reference is a name after FROM
    /// or JOIN; names that are not LDBC tables - the common table expressions of a query the
    /// tool refuses - stay as they are.
    /// </summary>
    private static string InSchema(string sql, string schema)
        => Regex.Replace(
            sql,
            @"\b(FROM|JOIN)\s+(\w+)\b",
            match => LdbcTables.Contains(match.Groups[2].Value)
                ? $"{match.Groups[1].Value} [{schema}].[{match.Groups[2].Value}]"
                : match.Value,
            RegexOptions.IgnoreCase);

    private static DynamicParameters Arguments(LdbcQuery query)
    {
        var arguments = new DynamicParameters();
        foreach (var parameter in query.Parameters)
        {
            if (parameter.IsList)
            {
                arguments.Add(parameter.Name, parameter.Example.Split(','));
                continue;
            }

            var type = parameter.SqlType.Split('(')[0].ToUpperInvariant();
            var (value, dbType) = type switch
            {
                "BIGINT" => ((object)long.Parse(parameter.Example, CultureInfo.InvariantCulture), DbType.Int64),
                "INT" => (int.Parse(parameter.Example, CultureInfo.InvariantCulture), DbType.Int32),
                "DATE" => (DateTime.Parse(parameter.Example, CultureInfo.InvariantCulture), DbType.Date),
                "DATETIME2" => (DateTime.Parse(parameter.Example, CultureInfo.InvariantCulture), DbType.DateTime2),
                "NVARCHAR" => (parameter.Example, DbType.String),
                "VARCHAR" => (parameter.Example, DbType.AnsiString),
                _ => throw new InvalidOperationException($"{query.Key}: no binding for the type {parameter.SqlType}."),
            };
            arguments.Add(parameter.Name, value, dbType);
        }

        return arguments;
    }
}
