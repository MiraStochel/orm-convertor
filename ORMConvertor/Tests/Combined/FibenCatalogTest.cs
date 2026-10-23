using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using SampleData;
using Tests.FibenJudge;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// What the FIBEN catalog (decision 149) states about itself, held at every build without a
/// database: the catalog is complete and every query states itself whole. Whether a query
/// translates, agrees and how fast it runs is the on-demand run (<c>FibenJudge/FibenRunTest</c>),
/// never a test of a build - the status of 237 queries is a measure of a version, not an
/// invariant of the tool.
/// </summary>
public class FibenCatalogTest
{
    private static readonly IReadOnlySet<string> Tables =
        FibenSample.Entities.Select(entity => Path.GetFileNameWithoutExtension(entity.FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>All 237 distinct queries of the benchmark are in the catalog, each once, numbered as the benchmark numbers them.</summary>
    [Fact]
    public void TheCatalogHoldsEveryQueryOfTheBenchmark()
    {
        Assert.Equal(Enumerable.Range(1, 237), FibenSample.Queries.Select(query => query.Number));
        Assert.All(FibenSample.Queries, query => Assert.Equal($"q{query.Number}", query.Key));
        Assert.Equal(152, FibenSample.Entities.Count);
    }

    /// <summary>
    /// Every query has the benchmark's question, Kim's type, a text, the tables it reads among
    /// the entities - and no other table in its text -, the columns it projects, and a note
    /// wherever its status is not the first value.
    /// </summary>
    [Fact]
    public void EveryQueryStatesItselfCompletely()
    {
        foreach (var query in FibenSample.Queries)
        {
            Assert.True(query.Questions.Count > 0 && query.Questions.All(question => !string.IsNullOrWhiteSpace(question)), $"{query.Key} has no question.");
            Assert.True(Enum.IsDefined(query.Nesting), $"{query.Key} has no type.");
            Assert.False(string.IsNullOrWhiteSpace(query.Sql), $"{query.Key} has no text.");
            Assert.False(string.IsNullOrWhiteSpace(query.BenchmarkSql), $"{query.Key} has no benchmark text.");
            Assert.True(query.Columns.Count > 0, $"{query.Key} projects no column.");
            Assert.Equal(query.Columns.Count, query.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain("@", query.Sql);

            Assert.True(query.Tables.Count > 0, $"{query.Key} reads no table.");
            Assert.All(query.Tables, table => Assert.Contains(table, Tables));

            var read = Regex.Matches(query.Sql, @"\b(?:FROM|JOIN)\s+(\w+)", RegexOptions.IgnoreCase)
                .Select(match => match.Groups[1].Value)
                .Where(Tables.Contains)
                .Select(name => name.ToUpperInvariant())
                .ToHashSet();
            Assert.True(read.SetEquals(query.Tables), $"{query.Key} reads {string.Join(", ", read)} but declares {string.Join(", ", query.Tables)}.");

            Assert.True(
                query.Translation == FibenTranslation.AsSpecified || !string.IsNullOrWhiteSpace(query.Note),
                $"{query.Key} is {query.Translation} without a note.");
        }
    }

    /// <summary>
    /// The ordering key of every query is the ordering of its text (decision 170): the columns
    /// the keys of the outermost ordering fall on, in their order, every column where a key falls
    /// on none, nothing where the text does not order. The import derives the key from the text
    /// and this holds it against the intermediate representation the text reads into, so that
    /// what the run keeps the order of is what the source orders by.
    /// </summary>
    [Fact]
    public void EveryQueryStatesTheKeyOfItsOrdering()
    {
        foreach (var query in FibenSample.Queries)
        {
            var text = OrderingKeys.Of(SourceQueries.ReadSingle(ORMEnum.Dapper, FibenRun.Units(query)), query.Columns);

            Assert.True(
                text.SequenceEqual(query.OrderingKey, StringComparer.OrdinalIgnoreCase),
                $"{query.Key}: the text orders by {Key(text)} and the catalog states {Key(query.OrderingKey)}.");
        }

        static string Key(IReadOnlyList<string> key) => key.Count == 0 ? "nothing" : string.Join(", ", key);
    }

    /// <summary>
    /// Every slice of the catalog - a TOP in the query or in a subquery of it - determines the
    /// rows it returns (decision 170): its scope is an aggregate without a grouping, or its
    /// ordering contains every key of its grouping, or every expression of its projection. Read
    /// from the representation, without data, so that the next query whose slice cuts through a
    /// tie fails here on any machine and not in a run over another server.
    /// </summary>
    [Fact]
    public void EverySliceDeterminesItsRows()
    {
        foreach (var query in FibenSample.Queries)
        {
            var scopes = OrderingKeys.Scopes(SourceQueries.ReadSingle(ORMEnum.Dapper, FibenRun.Units(query))).ToList();

            Assert.True(
                scopes.All(OrderingKeys.SliceDeterminesItsRows),
                $"{query.Key}: a slice of the text does not determine the rows it returns; the deciding key of decision 170 is missing.");
        }
    }

    /// <summary>The entity of every table is given to a query that reads it, and the files are the classes they are named after.</summary>
    [Fact]
    public void TheEntitiesAreTheTablesOfTheSchema()
    {
        foreach (var (fileName, content) in FibenSample.Entities)
        {
            var table = Path.GetFileNameWithoutExtension(fileName);
            Assert.Contains($"public class {table}", content);
            Assert.Contains("namespace Fiben;", content);
        }

        var first = FibenSample.Queries[0];
        Assert.Equal(first.Tables, FibenSample.EntitiesOf(first).Select(entity => Path.GetFileNameWithoutExtension(entity.FileName)));
    }

    /// <summary>
    /// Every generated entity is read by the Dapper parser without a Failure, so that the run
    /// never meets an entity file the tool cannot read. From Dapper into Dapper without a
    /// catalog, because a Dapper target asks for no fact the source does not state.
    /// </summary>
    [Fact]
    public void EveryEntityIsReadByTheTool()
    {
        var units = FibenSample.Entities.Select(entity => new ConversionSource
        {
            Name = entity.FileName,
            ContentType = ConversionContentType.CSharp,
            Content = entity.Content,
        }).ToList();

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.Dapper, units, catalogReader: null);

        Assert.DoesNotContain(result.Records, record => record.Kind == ConversionRecordKind.Failure);
        Assert.Equal(FibenSample.Entities.Count, result.Sources.Count(source => source.ContentType == ConversionContentType.CSharpEntity));
    }
}
