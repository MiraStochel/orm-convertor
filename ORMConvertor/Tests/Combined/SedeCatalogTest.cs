using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using SampleData;
using Tests.SedeJudge;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// What the SEDE catalog (decision 208) states about itself, held at every build without a
/// database: the catalog is complete and every query states itself whole. Whether a query
/// translates, agrees and how fast it runs is the on-demand run (<c>SedeJudge/SedeRunTest</c>),
/// never a test of a build - the status of the queries is a measure of a version, not an
/// invariant of the tool.
/// </summary>
public class SedeCatalogTest
{
    private static readonly IReadOnlySet<string> Tables =
        SedeSample.Entities.Select(entity => Path.GetFileNameWithoutExtension(entity.FileName)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every query of the validation and the test part is in the catalog, each once, keyed by its number in SEDE, the validation part first.</summary>
    [Fact]
    public void TheCatalogHoldsEveryQueryOfTheSet()
    {
        Assert.NotEmpty(SedeSample.Queries);
        Assert.All(SedeSample.Queries, query => Assert.Equal($"s{query.QuerySetId}", query.Key));
        Assert.Equal(SedeSample.Queries.Count, SedeSample.Queries.Select(query => query.QuerySetId).Distinct().Count());

        var sets = SedeSample.Queries.Select(query => query.Set).Distinct().ToList();
        Assert.Equal([SedeSet.Validation, SedeSet.Test], sets);
        foreach (var set in sets)
        {
            var numbers = SedeSample.Queries.Where(query => query.Set == set).Select(query => query.QuerySetId).ToList();
            Assert.Equal(numbers.Order(), numbers);
        }
    }

    /// <summary>
    /// Every query has a title, a text, the author's text with the placeholders, the tables it
    /// reads among the entities, a value of every parameter and a note wherever its status is
    /// not the first value; a translated query has the columns of its result, each once, and
    /// its text names every parameter it states and nothing but the schema's tables.
    /// </summary>
    [Fact]
    public void EveryQueryStatesItselfCompletely()
    {
        foreach (var query in SedeSample.Queries)
        {
            Assert.False(string.IsNullOrWhiteSpace(query.Sql), $"{query.Key} has no text.");
            Assert.False(string.IsNullOrWhiteSpace(query.SedeSql), $"{query.Key} has no SEDE text.");
            Assert.True(Enum.IsDefined(query.Set), $"{query.Key} has no part of the set.");
            Assert.DoesNotMatch(@"##[A-Za-z_][A-Za-z0-9_]*(?::[A-Za-z]+)?(?:\?[^#]*)?##", query.Sql);
            Assert.All(query.Tables, table => Assert.Contains(table, Tables));
            Assert.All(query.Parameters, parameter =>
            {
                Assert.Matches(@"^[A-Za-z_][A-Za-z0-9_]*$", parameter.Name);
                Assert.False(string.IsNullOrWhiteSpace(parameter.SqlType), $"{query.Key}: @{parameter.Name} has no type.");
                Assert.False(string.IsNullOrWhiteSpace(parameter.Value), $"{query.Key}: @{parameter.Name} has no value.");
                Assert.Contains("@" + parameter.Name, query.Sql);
            });
            Assert.Equal(query.Parameters.Count, query.Parameters.Select(parameter => parameter.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            Assert.True(
                query.Translation == SedeTranslation.AsSpecified || !string.IsNullOrWhiteSpace(query.Note),
                $"{query.Key} is {query.Translation} without a note.");

            if (query.Translation == SedeTranslation.NotTranslated)
            {
                continue;
            }

            Assert.False(string.IsNullOrWhiteSpace(query.Title), $"{query.Key} has no title.");
            Assert.True(query.Columns.Count > 0, $"{query.Key} projects no column.");
            Assert.All(query.Columns, column => Assert.False(string.IsNullOrEmpty(column), $"{query.Key} has a column without a name."));
            Assert.Equal(query.Columns.Count, query.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.True(query.Tables.Count > 0, $"{query.Key} reads no table.");
            Assert.All(query.OrderingKey, key => Assert.Contains(key, query.Columns, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// The ordering key of every query the tool reads is the ordering of its text (decision 170):
    /// the columns the keys of the outermost ordering fall on, in their order, every column where
    /// a key falls on none, nothing where the text does not order. The import derives the key
    /// from the text with ScriptDom and this holds it against the intermediate representation the
    /// text reads into, so that what the run keeps the order of is what the source orders by. A
    /// query the tool refuses states a key the run never uses, so it is not held.
    /// </summary>
    [Fact]
    public void EveryQueryTheToolReadsStatesTheKeyOfItsOrdering()
    {
        var read = 0;
        var differing = new List<string>();

        foreach (var query in SedeSample.Queries.Where(query => query.Translation != SedeTranslation.NotTranslated))
        {
            IReadOnlyList<string> text;
            try
            {
                text = OrderingKeys.Of(SourceQueries.ReadSingle(ORMEnum.Dapper, SedeRun.Units(query)), query.Columns);
            }
            catch (Exception)
            {
                continue;
            }

            read++;
            if (!text.SequenceEqual(query.OrderingKey, StringComparer.OrdinalIgnoreCase))
            {
                differing.Add($"{query.Key}: the text orders by {Key(text)} and the catalog states {Key(query.OrderingKey)}");
            }
        }

        Assert.True(read > 0, "The tool reads no query of the catalog.");
        Assert.True(differing.Count == 0, string.Join(Environment.NewLine, differing));

        static string Key(IReadOnlyList<string> key) => key.Count == 0 ? "nothing" : string.Join(", ", key);
    }

    /// <summary>The entity of every table is given to a query that reads it, and the files are the classes they are named after.</summary>
    [Fact]
    public void TheEntitiesAreTheTablesOfTheSchema()
    {
        foreach (var (fileName, content) in SedeSample.Entities)
        {
            var table = Path.GetFileNameWithoutExtension(fileName);
            Assert.Contains($"public class {table}", content);
            Assert.Contains("namespace Sede;", content);
        }

        var first = SedeSample.Queries.First(query => query.Tables.Count > 0);
        Assert.Equal(first.Tables, SedeSample.EntitiesOf(first).Select(entity => Path.GetFileNameWithoutExtension(entity.FileName)));
    }

    /// <summary>
    /// Every generated entity is read by the Dapper parser without a Failure, so that the run
    /// never meets an entity file the tool cannot read. From Dapper into Dapper without a
    /// catalog, because a Dapper target asks for no fact the source does not state.
    /// </summary>
    [Fact]
    public void EveryEntityIsReadByTheTool()
    {
        var units = SedeSample.Entities.Select(entity => new ConversionSource
        {
            Name = entity.FileName,
            ContentType = ConversionContentType.CSharp,
            Content = entity.Content,
        }).ToList();

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.Dapper, units, catalogReader: null);

        Assert.DoesNotContain(result.Records, record => record.Kind == ConversionRecordKind.Failure);
        Assert.Equal(SedeSample.Entities.Count, result.Sources.Count(source => source.ContentType == ConversionContentType.CSharpEntity));
    }

    /// <summary>The texts are the authors': the one change is the placeholders, so a text without one equals the SEDE text.</summary>
    [Fact]
    public void ATextWithoutAPlaceholderIsTheAuthorsText()
    {
        var unchanged = SedeSample.Queries.Where(query => !Regex.IsMatch(query.SedeSql, @"##[A-Za-z_]")).ToList();

        Assert.NotEmpty(unchanged);
        Assert.All(unchanged, query => Assert.Equal(query.SedeSql, query.Sql));
    }
}
