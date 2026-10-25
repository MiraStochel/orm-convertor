using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using OrmConvertor.Factories;
using ORMConvertorAPI.Data;
using ORMConvertorAPI.Dtos;

namespace Tests.Combined;

/// <summary>
/// The criteria of T1 in the assignment's words - at least 15 entities, relations, a composite
/// key, at least 20 read-only queries - counted from the excerpt of each application of the case
/// study (decision 184) rather than written into a text, where they drift from the files. Counted
/// as a conversion from the source framework into itself reads the excerpt without a catalog,
/// because that reading is what enters the experiment run: an entity is a map the conversion
/// holds when the catalog phase is over - key classes dissolved, excluded constructs out -, a
/// relation an owning one between two entities of the excerpt, a query one the source reads
/// without a Failure - a statement that writes is one (decision 053). The thresholds are
/// asserted, not the counts, so
/// the test does not move when an excerpt grows; the counts go to the output. That the excerpt
/// is open source under a permissive licence is <see cref="ThirdPartySourcesTest"/>'s, its schema
/// <see cref="CaseStudySchemaTest"/>'s, and what the tool achieves over it the experiment run's.
/// </summary>
public class CaseStudyCriteriaTest(ITestOutputHelper output)
{
    private const int MinimumEntities = 15;
    private const int MinimumQueries = 20;

    /// <summary>The examples that are the applications of the case study of T1 (decision 184); the other real projects are secondary case studies, measured by the run and not counted here.</summary>
    public static TheoryData<string> Applications() => new("keycloak-identity", "cofoundry-pages");

    [Theory]
    [MemberData(nameof(Applications))]
    public void TheApplicationMeetsTheCriteriaOfTheAssignment(string key)
    {
        Assert.Contains(key, Examples.RealProjectKeys);

        var (entities, queries) = Read(Examples.GetExamples.Single(example => example.Key == key));

        var names = entities.Select(map => map.Entity.Name).ToHashSet(StringComparer.Ordinal);
        var compositeKeys = entities.Where(map => map.PrimaryKey is { Parts.Count: > 1 }).Select(map => map.Entity.Name).ToList();
        var relations = entities
            .SelectMany(map => map.Relations)
            .Where(relation => relation.Role == RelationRole.Owning && names.Contains(relation.TargetEntity))
            .ToList();

        output.WriteLine($"{key}: {entities.Count} entities, {compositeKeys.Count} composite keys ({string.Join(", ", compositeKeys)}), {relations.Count} relations, {queries} read-only queries");

        Assert.True(entities.Count >= MinimumEntities, $"'{key}' has {entities.Count} entities; T1 asks for at least {MinimumEntities}.");
        Assert.True(compositeKeys.Count > 0, $"'{key}' has no composite key; T1 asks for one.");
        Assert.True(relations.Count > 0, $"'{key}' has no relation between two of its entities; T1 asks for relations.");
        Assert.True(queries >= MinimumQueries, $"'{key}' has {queries} read-only queries; T1 asks for at least {MinimumQueries}.");
    }

    /// <summary>
    /// The entity maps and the number of queries the source framework reads from the example's
    /// units, in the steps of <c>ConversionHandler.Convert</c>: the entity parsers, the catalog
    /// phase without a reader, the build - which completes what the queries need by convention,
    /// the columns of a relation's foreign key among them -, then the query parsers.
    /// </summary>
    private static (List<EntityMap> Entities, int Queries) Read(ExampleDefinition example)
    {
        var builder = EntityBuilderFactory.Create(example.SourceOrm)
            ?? throw new InvalidOperationException($"{example.SourceOrm} has no entity builder.");

        foreach (var parser in ParserFactory.Create(example.SourceOrm, builder, qb: null).OfType<IEntityParser>())
        {
            foreach (var unit in example.Units.Where(unit => parser.CanParse(unit.ContentType)))
            {
                parser.Parse(unit.Content);
            }
        }

        CatalogCompletion.Complete(builder, reader: null);
        List<EntityMap> entities = [.. builder.EntityMaps];
        builder.Build();

        AbstractQueryBuilder NewQueryBuilder()
        {
            var queryBuilder = QueryBuilderFactory.Create(example.SourceOrm)
                ?? throw new InvalidOperationException($"{example.SourceOrm} has no query builder.");
            queryBuilder.EntityMaps = builder.EntityMaps;
            return queryBuilder;
        }

        var queryParsers = ParserFactory.Create(example.SourceOrm, builder, NewQueryBuilder).OfType<IQueryParser>().ToList();
        var queries = 0;
        foreach (var unit in example.Units)
        {
            if (queryParsers.FirstOrDefault(parser => parser.CanParse(unit.ContentType)) is { } parser)
            {
                queries += parser.Parse(unit.ContentType, unit.Content, builder.EntityMaps)
                    .Count(query => query.Records.All(record => record.Kind != ConversionRecordKind.Failure));
            }
        }

        return (entities, queries);
    }
}
