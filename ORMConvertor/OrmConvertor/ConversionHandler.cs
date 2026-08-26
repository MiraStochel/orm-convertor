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

        // 1) Build entity maps using entity parsers only
        var entityParsers = ParserFactory.Create(sourceOrm, entityBuilder, qb: null)
            .OfType<IEntityParser>()
            .ToList();

        // A non-blank unit written in a language the source framework cannot read would
        // otherwise fall through the loop below without a word - the loop only ever asks
        // parsers what they accept, never what nobody claimed (decision 045). The query
        // branch has reported the same situation since decision 025; this is its entity-side
        // half. A blank unit stays silent: an unfilled input box is not a claim.
        foreach (var src in sources.Where(x =>
            !x.ContentType.IsQuery() && !string.IsNullOrWhiteSpace(x.Content)))
        {
            if (!entityParsers.Any(p => p.CanParse(src.ContentType)))
            {
                runRecords.Add(NotTranslated(
                    targetOrm,
                    src.ContentType,
                    UnitReference(src, sources),
                    $"{sourceOrm} has no parser for a {src.ContentType} artifact, so the unit was not read."));
            }
        }

        // The parser-outer order is source precedence ordered in time (decision 017); the
        // blank-unit filter keeps an unfilled box from being read at all. What each unit
        // yielded is the parser's own statement, and a claimed unit nothing came of is a
        // record - beside a productive unit it used to be the last silent case of decision
        // 045. Records born during the reading are attributed to the unit, because its
        // reading is their origin (decision 066).
        foreach (var parser in entityParsers)
        {
            foreach (var src in sources.Where(x =>
                parser.CanParse(x.ContentType) && !string.IsNullOrWhiteSpace(x.Content)))
            {
                var unit = UnitReference(src, sources);
                var recordsBefore = entityBuilder.Records.Count;

                var read = parser.Parse(src.Content);

                entityBuilder.AttributeRecords(recordsBefore, unit);

                if (read.Count == 0)
                {
                    runRecords.Add(NotTranslated(
                        targetOrm,
                        src.ContentType,
                        unit,
                        $"The unit was read as {src.ContentType} and no entity or mapping fact came of it; check that its content is what the declared type names."));
                }
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

        // An unfilled input box is not a claim, so a blank query source is skipped without a
        // record; a non-blank one nobody can read is a Failure (decision 025).
        var querySources = sources
            .Where(s => s.ContentType.IsQuery() && !string.IsNullOrWhiteSpace(s.Content))
            .ToList();

        foreach (var qsrc in querySources)
        {
            var unit = UnitReference(qsrc, sources);

            var qb = QueryBuilderFactory.Create(targetOrm);
            if (qb is null)
            {
                queryRecords.Add(NotTranslated(
                    targetOrm,
                    qsrc.ContentType,
                    unit,
                    $"{targetOrm} has no query builder, so the query was not translated."));
                continue;
            }

            var queryParsers = ParserFactory.Create(sourceOrm, entityBuilder, qb)
                .OfType<IQueryParser>()
                .Where(p => p.CanParse(qsrc.ContentType))
                .ToList();

            if (queryParsers.Count == 0)
            {
                queryRecords.Add(NotTranslated(
                    targetOrm,
                    qsrc.ContentType,
                    unit,
                    $"{sourceOrm} has no parser for a {qsrc.ContentType} query, so it was not translated."));
                continue;
            }

            // Exactly one parser per source ORM claims a given query language, so the choice
            // no longer depends on the order of the list (decision 025).
            qb.EntityMaps = entityBuilder.EntityMaps;
            queryParsers[0].Parse(qsrc.ContentType, qsrc.Content, entityBuilder.EntityMaps);

            results.AddRange(qb.Build());

            // The builder was created for this one unit and dies with it, so every record it
            // holds - the parser's and the build's alike - came from this unit (decision 066).
            queryRecords.AddRange(qb.Records.Select(r => r with { Unit = unit }));
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
    /// One unit did not become an artifact: the target has no builder for its language, the
    /// source has no parser for it, nobody claimed it at all, or it was read and nothing
    /// came of it. One shape for all four, because for the caller it is one event - the
    /// unit went in and nothing came of it (decisions 025, 045 and 066).
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
}
