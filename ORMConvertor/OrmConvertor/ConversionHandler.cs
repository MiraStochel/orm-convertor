using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using OrmConvertor.Factories;

namespace OrmConvertor;

public static class ConversionHandler
{
    public static ConversionResult Convert(
        ORMEnum sourceOrm,
        ORMEnum targetOrm,
        List<ConversionSource> sources,
        string? catalogConnectionString = null
    )
    {
        // The reader lives and dies with the request; a caller holding a longer-lived
        // one - a cache over an Advisor run or a test collection - uses the overload
        // below and keeps ownership.
        using SqlServerCatalogReader? reader = string.IsNullOrWhiteSpace(catalogConnectionString)
            ? null
            : new SqlServerCatalogReader(catalogConnectionString);

        return Convert(sourceOrm, targetOrm, sources, reader);
    }

    public static ConversionResult Convert(
        ORMEnum sourceOrm,
        ORMEnum targetOrm,
        List<ConversionSource> sources,
        ICatalogReader? catalogReader
    )
    {
        var entityBuilder = EntityBuilderFactory.Create(targetOrm);

        if (entityBuilder == null)
        {
            throw new InvalidOperationException("Target ORM not supported");
        }

        var results = new List<ConversionSource>();

        // Records the orchestration writes about the run itself, as opposed to those the
        // builders write about one entity or one instruction (decision 010).
        var runRecords = new List<ConversionRecord>();

        // One row per non-blank unit, filled by both passes. Since decision 081 the
        // orchestration no longer splits the units by language: every unit is offered to
        // both passes and each takes what its parsers claim, so a document that is a mapping
        // and a query at once - an hbm.xml with a class beside a named query - is read by
        // both without the client cutting it up. Both questions the orchestration asks about
        // a unit are therefore answered across the passes together: a unit both of them read
        // must not collect two records about one event. A blank unit is not in this list at
        // all - an unfilled input box is not a claim - so it is never handed to a parser and
        // never spoken about.
        var units = sources
            .Where(s => !string.IsNullOrWhiteSpace(s.Content))
            .Select(s => new UnitOutcome(s, UnitReference(s, sources)))
            .ToList();

        // 1) Build entity maps using entity parsers only
        var entityParsers = ParserFactory.Create(sourceOrm, entityBuilder, qb: null)
            .OfType<IEntityParser>()
            .ToList();

        // The parser-outer order is source precedence ordered in time (decision 017). What
        // each unit yielded is the parser's own statement, because an enriching parser adds
        // no new map and counting maps around the call would call an honest unit barren
        // (decision 066). Records born during the reading are attributed to the unit,
        // because its reading is their origin (decision 066).
        foreach (var parser in entityParsers)
        {
            foreach (var unit in units.Where(u => parser.CanParse(u.Source.ContentType)))
            {
                unit.Claimed = true;

                var recordsBefore = entityBuilder.Records.Count;

                var read = parser.Parse(unit.Source.Content);

                entityBuilder.AttributeRecords(recordsBefore, unit.Reference);

                unit.Yielded |= read.Count > 0;
            }
        }

        // The completion phase of decision 015 sits between parsing and generation: the
        // target's descriptor formulates the demand, one component reads the catalog, and
        // the phase is timed on its own (S3). The reader is an optional input - a
        // translation without one proceeds on conventions and says so in the records.
        var catalogPhase = CatalogCompletion.Complete(entityBuilder, catalogReader);

        // Emit entities for target ORM
        results.AddRange(entityBuilder.Build());

        // 2) Translate each query independently so we can return multiple query outputs.
        //    Records of the query builders join the entity builder's, because the caller
        //    asked for one conversion (decision 022).
        var queryRecords = new List<ConversionRecord>();

        // The factory the query parsers are constructed with: one fresh builder per query,
        // with this conversion's maps already on it (decision 081). The parser may not make
        // one - a builder belongs to the target framework and a parser to the source (S1) -
        // and the maps are set here rather than there for the same reason. A target without
        // a query builder never reaches this delegate, because its units are recorded and
        // skipped before any parse; the throw therefore marks a program error, not an input.
        AbstractQueryBuilder NewQueryBuilder()
        {
            var builder = QueryBuilderFactory.Create(targetOrm)
                ?? throw new InvalidOperationException($"{targetOrm} has no query builder.");

            builder.EntityMaps = entityBuilder.EntityMaps;

            return builder;
        }

        var queryParsers = ParserFactory.Create(sourceOrm, entityBuilder, NewQueryBuilder)
            .OfType<IQueryParser>()
            .ToList();

        var targetTakesQueries = QueryBuilderFactory.Supports(targetOrm);

        foreach (var unit in units)
        {
            // Exactly one parser per source ORM claims a given query language, so the choice
            // does not depend on the order of the list (decision 025).
            var parser = queryParsers.FirstOrDefault(p => p.CanParse(unit.Source.ContentType));
            if (parser is null)
            {
                continue;
            }

            unit.Claimed = true;

            // Only a unit some query parser claimed can be missing a query builder. Written
            // for every unit, the record would land on each entity unit of a conversion into
            // a target that generates no queries at all (decision 081).
            if (!targetTakesQueries)
            {
                queryRecords.Add(NotTranslated(
                    targetOrm,
                    unit.Source.ContentType,
                    unit.Reference,
                    $"{targetOrm} has no query builder, so the query was not translated."));
                continue;
            }

            var filled = parser.Parse(unit.Source.ContentType, unit.Source.Content, entityBuilder.EntityMaps);

            // An empty collection is no error in itself: a mapping document that carries no
            // query is an ordinary input, and whether the unit was barren is asked of both
            // passes together below (decision 081).
            unit.Yielded |= filled.Count > 0;

            foreach (var queryBuilder in filled)
            {
                results.AddRange(queryBuilder.Build());

                // Each builder was made for one query of this one unit and dies with it, so
                // every record it holds - the parser's and the build's alike - came from that
                // query (decisions 066 and 081). The query's own name is what tells the
                // records of two queries of one document apart.
                queryRecords.AddRange(queryBuilder.Records.Select(r => r with
                {
                    Unit = unit.Reference,
                    Query = queryBuilder.QueryName,
                }));
            }
        }

        // Two statements about one unit, both made across both passes (decision 081). A
        // non-blank unit written in a language nobody claimed would otherwise fall through
        // without a word, because the loops only ever ask parsers what they accept, never
        // what nobody claimed (decisions 025 and 045); and a unit that was read and yielded
        // neither an entity map nor a query is a Failure of its own, written beside a
        // productive unit as much as alone - the story of a unit does not change with what
        // its neighbour produced (decision 066).
        foreach (var unit in units)
        {
            if (!unit.Claimed)
            {
                runRecords.Add(NotTranslated(
                    targetOrm,
                    unit.Source.ContentType,
                    unit.Reference,
                    $"{sourceOrm} has no parser for a {unit.Source.ContentType} unit, so it was not read."));
            }
            else if (!unit.Yielded)
            {
                runRecords.Add(NotTranslated(
                    targetOrm,
                    unit.Source.ContentType,
                    unit.Reference,
                    $"The unit was read as {unit.Source.ContentType} and neither a mapping fact nor a query came of it; check that its content is what the declared type names."));
            }
        }

        // A run that generated nothing at all has to say so. Answering with empty artifacts
        // and empty records would be a silent "done" about something that did not happen -
        // the same defect the source-framework check in ParserFactory already closed one
        // level up (decision 045). The status code stays 200: a partial conversion must
        // still hand over what it produced, so the reason belongs in the records.
        if (results.Count == 0)
        {
            runRecords.Add(NothingGenerated(targetOrm, sources));
        }

        // The records accumulate on the entity builder - parsers and the build phases both
        // report there - and leave as returned data next to the artifacts (decision 010).
        // The framework versions come from the descriptors (decision 013), so the run
        // record and the generator cannot disagree about them (S6).
        return new ConversionResult
        {
            RunId = Guid.NewGuid(),
            ToolVersion = ToolRelease.Version,
            SourceFramework = sourceOrm,
            SourceFrameworkVersion = DescriptorFactory.Create(sourceOrm).Version,
            TargetFramework = targetOrm,
            TargetFrameworkVersion = entityBuilder.Descriptor.Version,
            TargetDatabaseDialect = entityBuilder.Descriptor.Dialect,
            Sources = results,
            Records = [.. entityBuilder.Records, .. queryRecords, .. runRecords],
            CatalogState = catalogPhase.ConnectionState,
            CatalogReadTime = catalogPhase.ReadTime,
        };
    }

    /// <summary>
    /// The run produced no artifact. The two cases are told apart on purpose: no unit came in
    /// at all, or units came in and none of them yielded anything - for the caller those are
    /// different messages (decision 045).
    /// </summary>
    private static ConversionRecord NothingGenerated(ORMEnum targetOrm, List<ConversionSource> sources)
        => new()
        {
            Kind = ConversionRecordKind.Failure,
            Framework = targetOrm,
            Reason = sources.All(s => string.IsNullOrWhiteSpace(s.Content))
                ? "The request carried no source unit with content, so nothing was generated."
                : "No source unit yielded an entity or a query to generate from; check that each unit's content is written in the language the unit declares.",
        };

    /// <summary>
    /// One unit did not become an artifact: the target has no query builder for it, nobody
    /// claimed it at all, or it was read and nothing came of it. One shape for all three,
    /// because for the caller it is one event - the unit went in and nothing came of it
    /// (decisions 025, 045, 066 and 081).
    /// </summary>
    private static ConversionRecord NotTranslated(
        ORMEnum targetOrm,
        ConversionContentType artifact,
        string unit,
        string reason)
        => new()
        {
            Kind = ConversionRecordKind.Failure,
            Framework = targetOrm,
            Artifact = artifact,
            Unit = unit,
            Reason = reason,
        };

    /// <summary>
    /// How a record points back at an input unit (decision 066): the name the client sent,
    /// or the unit's 1-based position in the request - the one coordinate the caller can
    /// always compute from its own list.
    /// </summary>
    private static string UnitReference(ConversionSource src, List<ConversionSource> sources)
        => string.IsNullOrWhiteSpace(src.Name)
            ? $"unit {sources.IndexOf(src) + 1}"
            : src.Name.Trim();

    /// <summary>
    /// What became of one non-blank input unit, counted across both passes (decision 081).
    /// Two flags rather than a loop of their own per pass, because a unit both passes read -
    /// an hbm.xml with a class beside a named query - would otherwise be called unclaimed or
    /// barren by whichever pass did not take it.
    /// </summary>
    private sealed class UnitOutcome(ConversionSource source, string reference)
    {
        public ConversionSource Source { get; } = source;

        /// <summary>How a record points back at this unit (decision 066).</summary>
        public string Reference { get; } = reference;

        /// <summary>Some parser of either pass accepted the unit's language.</summary>
        public bool Claimed { get; set; }

        /// <summary>An entity map or a query came of it.</summary>
        public bool Yielded { get; set; }
    }
}
