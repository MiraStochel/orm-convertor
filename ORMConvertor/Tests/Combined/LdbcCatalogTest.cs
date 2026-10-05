using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Dapper;
using Model;
using OrmConvertor;
using SampleData;
using Tests.Database;
using Tests.Differential;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// The LDBC query catalog (decision 110). The page that shows it claims, per query, whether
/// the tool translates it as the specification defines it, translates a stated
/// simplification of it, or does not translate it - and this class is where each claim is
/// held. A query with a text is translated from Dapper into the other five frameworks with the
/// LDBC tables as the catalog, which is how the page means it: those tables are created by
/// the fixture from the very scripts the container's LdbcSnb database is loaded with.
///
/// The claims are about the first verification level - the target artifact is produced
/// without a Failure record, or it is refused with one - and, for EF Core and NHibernate, the
/// second: the artifact compiles with the entities of its conversion. Whether a query of the Interactive
/// workload returns what the specification defines is the fourth level, held by LDBC's own
/// validation set in <c>Ldbc/LdbcValidationTest</c> over a loaded LdbcSnb (decision 117); here
/// it is only claimed that every such query is bound to its judge. What this class does run is
/// every text, against the empty tables, so that a text the page shows is T-SQL over those
/// tables and not a sketch of it.
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
    /// up here instead of staying on the page. A target the query names as falling back is
    /// held to the third value of decision 113 - the artifact in native SQL, with the record
    /// that says so -, and every other target to the first, so that neither value passes for
    /// the other.
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

        var fellBack = result.Records.Any(record => record.Kind == ConversionRecordKind.Fallback);
        if (query.Fallbacks.Any(fallback => fallback.Target == target))
        {
            Assert.True(fellBack, $"{key} was expected to go out in native SQL from {target}, but no Fallback was recorded.");
            Assert.Contains(result.Sources, source => source.ContentType == ConversionContentType.SqlQuery);
        }
        else
        {
            Assert.False(
                fellBack,
                $"{key} went out in native SQL from {target}, which the catalog does not say:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, result.Records.Where(record => record.Kind == ConversionRecordKind.Fallback).Select(record => $"  {record.Feature}: {record.Reason}")));
        }
    }

    /// <summary>
    /// The second verification level for the .NET targets (decision 027): the query artifact
    /// EF Core or NHibernate receives - in its own language or in native SQL - compiles with the
    /// entities of the same conversion. The judge of decision 117 compiles the Interactive
    /// queries anyway; this holds the BI queries too, which no judge runs, so that a method the
    /// consumer's compiler would refuse does not pass for a translation on the first level alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(DotNetArtifacts))]
    public void EveryDotNetArtifactCompiles(string key, ORMEnum target)
    {
        fixture.SkipIfUnavailable();
        var query = Query(key);

        var result = ConversionHandler.Convert(ORMEnum.Dapper, target, Units(query), fixture.CatalogReader);
        var (references, usings) = target == ORMEnum.EFCore
            ? (GeneratedQueryCompiler.EFCoreConsumerReferences, "using Microsoft.EntityFrameworkCore;")
            : (GeneratedQueryCompiler.NHibernateConsumerReferences, "using NHibernate;");

        var compiled = GeneratedQueryCompiler.Compile(
            $"LdbcCatalog_{key}_{target}",
            DotNetQueryRunner.QueryMethod(result),
            PreparedQuery.EntitySources(result),
            references,
            PreparedQuery.Usings(result, usings));

        Assert.True(
            compiled.Success,
            $"{key} into {target} does not compile:{Environment.NewLine}"
                + string.Join(Environment.NewLine, compiled.Errors.Select(error => "  " + error)));
    }

    public static TheoryData<string, ORMEnum> DotNetArtifacts()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var query in LdbcSnbSample.Queries.Where(query => query.Translation != LdbcTranslation.NotTranslated))
        {
            foreach (var target in new[] { ORMEnum.EFCore, ORMEnum.NHibernate }.Where(target => query.Refusals.All(refusal => refusal.Target != target)))
            {
                data.Add(query.Key, target);
            }
        }

        return data;
    }

    /// <summary>
    /// Every query of the Interactive workload is bound to the operation of the driver that
    /// judges it (decision 117), and no query of BI is - LDBC publishes no expected results for
    /// that workload over the Interactive data. A query of Interactive without a binding would be
    /// a query out of the judge's reach, which is what this rules out.
    /// </summary>
    [Fact]
    public void EveryInteractiveQueryIsBoundToItsJudge()
    {
        foreach (var query in LdbcSnbSample.Queries)
        {
            var operation = query.Workload switch
            {
                LdbcWorkload.InteractiveShort => $"IS{query.Number}",
                LdbcWorkload.InteractiveComplex => $"IC{query.Number}",
                _ => null,
            };

            Assert.True(operation == query.Validation?.Operation, $"{query.Key} is bound to {query.Validation?.Operation ?? "nothing"}, not to {operation ?? "nothing"}.");
        }
    }

    /// <summary>
    /// A binding states every parameter of the text once, from a field of the operation, with
    /// an operand exactly where the derivation takes one; and every column it compares is a
    /// column the text projects, a list with its separator and an element of fields only in a list.
    /// </summary>
    [Fact]
    public void EveryBindingStatesTheTextItJudges()
    {
        foreach (var query in LdbcSnbSample.Queries.Where(query => query.Validation is not null))
        {
            var binding = query.Validation!;

            Assert.Equal(
                query.Parameters.Select(parameter => parameter.Name).Order(StringComparer.Ordinal),
                binding.Arguments.Select(argument => argument.Parameter).Order(StringComparer.Ordinal));
            Assert.All(binding.Arguments, argument =>
                Assert.True((argument.Derivation == LdbcDerivation.PlusDays) == (argument.Operand is not null), $"{query.Key}: {argument}"));

            Assert.Equal(binding.Fields.Count, binding.Fields.Select(field => field.Column).Distinct().Count());
            Assert.All(binding.Fields, field =>
            {
                // Under its alias, or as the bare column it is (SELECT p.FirstName).
                Assert.Matches($@"\bAS\s+{field.Column}\b|\w\.{field.Column}\b", query.Sql!);

                var list = field.Kind is LdbcValueKind.Set or LdbcValueKind.Sequence;
                Assert.True(list == (field.Separator is not null), $"{query.Key}: {field}");
                Assert.True((field.Elements is not null) == (field.ElementSeparator is not null), $"{query.Key}: {field}");
                Assert.True(field.Elements is null || list, $"{query.Key}: {field}");
            });
        }
    }

    /// <summary>A target refuses a query or falls back from it or translates it - never two of the three.</summary>
    [Fact]
    public void NoTargetBothRefusesAndFallsBack()
    {
        Assert.All(LdbcSnbSample.Queries, query => Assert.Empty(
            query.Refusals.Select(refusal => refusal.Target).Intersect(query.Fallbacks.Select(fallback => fallback.Target))));
    }

    /// <summary>
    /// The claim of the page for a query it shows but does not translate: the natural text is
    /// refused with a Failure record in every direction, and no query artifact comes out.
    /// If this starts failing, the tool learned something and the page is wrong in the
    /// modest direction - the catalog is to be updated, not the test loosened. A fact over
    /// every such query rather than a theory: since decision 113 the catalog has none, and a
    /// theory without data is no claim at all.
    /// </summary>
    [Fact]
    public void EveryRefusedTextIsRefusedInEveryDirection()
    {
        fixture.SkipIfUnavailable();

        foreach (var query in LdbcSnbSample.Queries.Where(query => query.Translation == LdbcTranslation.NotTranslated && query.Sql is not null))
        {
            foreach (var target in Targets)
            {
                var result = ConversionHandler.Convert(ORMEnum.Dapper, target, Units(query), fixture.CatalogReader);

                Assert.True(
                    result.Records.Any(record => record.Kind == ConversionRecordKind.Failure),
                    $"{query.Key} into {target} was expected to be refused, but no Failure was recorded.");
                Assert.DoesNotContain(result.Sources, source => source.ContentType.IsQuery());
            }
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
