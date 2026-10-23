using AbstractWrappers;
using AbstractWrappers.Descriptors;
using DapperWrappers;
using Model;
using Model.QueryInstructions;
using OrmConvertor.Factories;

namespace Tests.Verification;

/// <summary>
/// The intermediate representation of the queries a source states, read the way the
/// orchestration reads them: the entity pass over every unit first, then the query pass with
/// the maps the entity pass left, through the parser factory of the orchestration itself. What
/// comes back is the body of each query as its parser filled it, before any builder normalizes
/// it - the ground a claim about "the ordering of the source" stands on (decision 170).
/// </summary>
internal static class SourceQueries
{
    /// <summary>The queries the units state, in the order the parsers read them.</summary>
    public static List<IReadOnlyList<QueryInstruction>> Read(ORMEnum source, List<ConversionSource> units)
    {
        var entityBuilder = EntityBuilderFactory.Create(source)
            ?? throw new InvalidOperationException($"{source} has no entity builder.");

        var captured = new List<CapturingQueryBuilder>();
        var parsers = ParserFactory.Create(source, entityBuilder, () =>
        {
            var builder = new CapturingQueryBuilder { EntityMaps = entityBuilder.EntityMaps };
            captured.Add(builder);
            return builder;
        });

        var read = units.Where(unit => !string.IsNullOrWhiteSpace(unit.Content)).ToList();

        foreach (var parser in parsers.OfType<IEntityParser>())
        {
            foreach (var unit in read.Where(unit => parser.CanParse(unit.ContentType)))
            {
                parser.Parse(unit.Content);
            }
        }

        var queryParsers = parsers.OfType<IQueryParser>().ToList();
        foreach (var unit in read)
        {
            queryParsers.FirstOrDefault(parser => parser.CanParse(unit.ContentType))
                ?.Parse(unit.ContentType, unit.Content, entityBuilder.EntityMaps);
        }

        return [.. captured.Select(builder => builder.Instructions)];
    }

    /// <summary>The one query the units state; more or none is a failure of the caller's claim.</summary>
    public static IReadOnlyList<QueryInstruction> ReadSingle(ORMEnum source, List<ConversionSource> units)
    {
        var queries = Read(source, units);

        Assert.True(queries.Count == 1, $"{source}: the units state {queries.Count} queries, and one was expected.");
        return queries[0];
    }

    /// <summary>A builder that keeps what a parser fills in and builds nothing.</summary>
    private sealed class CapturingQueryBuilder : AbstractQueryBuilder
    {
        public override TargetFrameworkDescriptor Descriptor => DapperDescriptor.Instance;

        public IReadOnlyList<QueryInstruction> Instructions => instructions;

        protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact) { }
        protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact) => [];
    }
}
