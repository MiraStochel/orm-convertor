using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Tests.Database;

namespace Tests.Differential;

/// <summary>
/// The fourth verification level over a query (decisions 016 and 089): the generated query
/// is executed against the fixture and what came back is compared, field by field, with the
/// canonical result of that query.
///
/// Both halves of a pair really run - the source variant here or in the Java suite, the
/// translated one in the suite that owns its framework - and they meet over the canonical
/// file rather than across a process boundary, because decision 089 refused to grow an
/// endpoint that runs foreign code for the sake of a test.
///
/// The canonical result belongs to the query and not to a direction, which is what keeps
/// it honest: a translation broken in one direction cannot be fixed by moving the target,
/// because moving it breaks every other direction of the same query at once. A category of
/// T2 has several sources, and every one of their runs meets the same file too.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DifferentialVerificationTest(TestSchemaFixture fixture)
{
    /// <summary>
    /// Where a recording run writes the canonical results. It is a path and not a flag so
    /// that recording cannot happen by accident, and only the source variant ever writes:
    /// a result recorded from a translation would make the translation its own judge.
    /// </summary>
    private const string RecordVariable = "ORMCONVERTOR_RECORD_DIFFERENTIAL";

    /// <summary>The source variants this suite can run: every (query, source) whose source it owns.</summary>
    public static TheoryData<string, ORMEnum> SourceVariants()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var query in DifferentialMatrix.Queries)
        {
            foreach (var source in query.Sources.Where(DotNetQueryRunner.Owns))
            {
                data.Add(query.Id, source);
            }
        }

        return data;
    }

    /// <summary>
    /// The pairs of the matrix whose target this suite can run. The rest are the Java
    /// suite's, and each suite states that it ran every pair assigned to it
    /// (<see cref="DifferentialMatrixTest"/>), so neither half can be met by skipping.
    /// </summary>
    public static TheoryData<string, ORMEnum, ORMEnum> Pairs()
    {
        var data = new TheoryData<string, ORMEnum, ORMEnum>();
        foreach (var pair in DifferentialMatrix.Pairs().Where(p => DotNetQueryRunner.Owns(p.Target)))
        {
            data.Add(pair.Query.Id, pair.Source, pair.Target);
        }

        return data;
    }

    /// <summary>The pairs of the matrix whose target is NHibernate, which writes a LINQ form beside its HQL (decision 118).</summary>
    public static TheoryData<string, ORMEnum> NHibernatePairs()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var pair in DifferentialMatrix.Pairs().Where(p => p.Target == ORMEnum.NHibernate))
        {
            data.Add(pair.Query.Id, pair.Source);
        }

        return data;
    }

    /// <summary>The pairs of the matrix whose target falls back to native SQL and which this suite owns (decision 113).</summary>
    public static TheoryData<string, ORMEnum, ORMEnum, QueryFeature> FallbackDirections()
    {
        var data = new TheoryData<string, ORMEnum, ORMEnum, QueryFeature>();
        foreach (var fallback in DifferentialMatrix.FallbackDirections().Where(f => DotNetQueryRunner.Owns(f.Target)))
        {
            data.Add(fallback.Query.Id, fallback.Source, fallback.Target, fallback.Feature);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SourceVariants))]
    public void TheSourceVariantFixesTheCanonicalResult(string id, ORMEnum source)
    {
        fixture.SkipIfUnavailable();

        var query = Query(id);
        var rows = DotNetQueryRunner.Run(query, source, source, fixture);

        if (Recording() is { } directory)
        {
            Record(directory, query, source, rows);
            return;
        }

        Assert.Equal(query.CanonicalResult(), rows);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheTranslatedVariantMatchesTheCanonicalResult(string id, ORMEnum source, ORMEnum target)
    {
        fixture.SkipIfUnavailable();

        if (Recording() is not null)
        {
            Assert.Skip("A recording run fixes the canonical results; comparing against them is the next run.");
        }

        var query = Query(id);
        var rows = DotNetQueryRunner.Run(query, source, target, fixture);

        Assert.Equal(query.CanonicalResult(), rows);
    }

    /// <summary>
    /// The fourth level over the second form of an NHibernate query (decision 118): the LINQ
    /// chain over <c>session.Query&lt;T&gt;()</c> runs beside the HQL the pair above judges, and
    /// its rows meet the same canonical result. A pair whose query the LINQ form does not
    /// speak carries no such artifact and an Omitted record instead - the HQL form stands
    /// alone there, and the theory says so rather than failing on a form that was never
    /// promised.
    /// </summary>
    [Theory]
    [MemberData(nameof(NHibernatePairs))]
    public void TheLinqFormOfNHibernateReturnsTheCanonicalRows(string id, ORMEnum source)
    {
        fixture.SkipIfUnavailable();

        if (Recording() is not null)
        {
            Assert.Skip("A recording run fixes the canonical results; comparing against them is the next run.");
        }

        var query = Query(id);
        var conversion = DotNetQueryRunner.Translate(query, source, ORMEnum.NHibernate, fixture);

        if (!DotNetQueryRunner.Carries(conversion, ConversionContentType.CSharpLinqQuery))
        {
            // No second form after the escape path either: the native SQL already is the query.
            Assert.Contains(conversion.Records, record => record.Kind is ConversionRecordKind.Omitted or ConversionRecordKind.Fallback);
            Assert.Skip($"{id}: the LINQ form of NHibernate does not speak the query, and the binding form stands alone.");
        }

        var rows = DotNetQueryRunner.Run(query, source, ORMEnum.NHibernate, fixture, form: ConversionContentType.CSharpLinqQuery);

        Assert.Equal(query.CanonicalResult(), rows);
    }

    /// <summary>
    /// A direction the matrix states as refused is refused: no query artifact comes out and
    /// a record names the feature (decision 053). It is the word decision 089 asks the matrix
    /// to say instead of a missing row - and a target that started accepting the query would
    /// show up here rather than nowhere. Since decision 113 a target whose language merely
    /// lacks a category falls back instead, and no category of the manifest is refused any
    /// more; the check stays for the refusals that are not about a language, so a fact over
    /// what the matrix states rather than a theory with no data.
    /// </summary>
    [Fact]
    public void EveryRefusedDirectionIsRefusedAsStated()
    {
        fixture.SkipIfUnavailable();

        Assert.All(DifferentialMatrix.RefusedDirections().Where(r => DotNetQueryRunner.Owns(r.Target)), refused =>
        {
            var conversion = OrmConvertor.ConversionHandler.Convert(
                refused.Source, refused.Target, refused.Query.Units(refused.Source), fixture.CatalogReader);

            Assert.DoesNotContain(conversion.Sources, artifact => artifact.ContentType.IsQuery());
            Assert.Contains(conversion.Records, record => record.Feature == refused.Feature);
        });
    }

    /// <summary>
    /// A pair whose target falls back writes the query in native SQL, as the matrix states
    /// (decision 113): a record of kind Fallback names the feature, and the bare SQL stands
    /// beside the method. Its rows are judged by the theory above with every other pair - the
    /// fourth level measures the escape path as it measures a translation -, and this one
    /// keeps the two values of a cell from passing as each other.
    /// </summary>
    [Theory]
    [MemberData(nameof(FallbackDirections))]
    public void AFallbackDirectionFallsBackAsStated(string id, ORMEnum source, ORMEnum target, QueryFeature feature)
    {
        fixture.SkipIfUnavailable();

        var conversion = DotNetQueryRunner.Translate(Query(id), source, target, fixture);

        Assert.Contains(conversion.Records, record => record.Kind == ConversionRecordKind.Fallback && record.Feature == feature);
        Assert.Contains(conversion.Sources, artifact => artifact.ContentType == ConversionContentType.SqlQuery);
    }

    internal static DifferentialQuery Query(string id)
        => DifferentialMatrix.Queries.Single(query => query.Id == id);

    private static string? Recording()
    {
        var directory = Environment.GetEnvironmentVariable(RecordVariable);
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    /// <summary>
    /// Writes the canonical result of the query. A category is stated by several sources
    /// and every one of them records into the same file, so a second recording of a file
    /// has to agree with the first: two sources that disagree about the rows are two
    /// queries, and a file written by whichever ran last would hide that.
    /// </summary>
    private static void Record(string directory, DifferentialQuery query, ORMEnum source, IEnumerable<string> rows)
    {
        var results = Path.Combine(directory, "results");
        Directory.CreateDirectory(results);

        // CRLF because the repository stores these as text files under its own line-ending
        // rule; the comparison is by line, so the choice affects the diff and nothing else.
        var text = string.Concat(rows.Select(row => row + "\r\n"));
        var file = Path.Combine(results, query.Id + ".txt");

        if (File.Exists(file))
        {
            var recorded = File.ReadAllText(file);
            Assert.True(
                recorded == text,
                $"{query.Id}: the source variant of {source} returned other rows than the source variant "
                + $"recorded before it into {file}, so the sources do not state one query.");
            return;
        }

        File.WriteAllText(file, text);
    }
}
