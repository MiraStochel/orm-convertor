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
            .Where(p => p is not IQueryParser)
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
                    $"{sourceOrm} has no parser for a {src.ContentType} artifact, so the unit was not read."));
            }
        }

        foreach (var parser in entityParsers)
        {
            foreach (var src in sources.Where(x => parser.CanParse(x.ContentType)))
            {
                parser.Parse(src.Content);
            }
        }

        // The completion phase of decision 015 sits between parsing and generation: the
        // target's descriptor formulates the demand, one component reads the catalog, and
        // the phase is timed on its own (S3). The connection is an optional input - a
        // translation without one proceeds on conventions and says so in the records.
        var catalogReader = string.IsNullOrWhiteSpace(catalogConnectionString)
            ? null
            : new SqlServerCatalogReader(catalogConnectionString);
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
            var qb = QueryBuilderFactory.Create(targetOrm);
            if (qb is null)
            {
                queryRecords.Add(NotTranslated(
                    targetOrm,
                    qsrc.ContentType,
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
                    $"{sourceOrm} has no parser for a {qsrc.ContentType} query, so it was not translated."));
                continue;
            }

            // Exactly one parser per source ORM claims a given query language, so the choice
            // no longer depends on the order of the list (decision 025).
            qb.EntityMaps = entityBuilder.EntityMaps;
            queryParsers[0].Parse(qsrc.Content, entityBuilder.EntityMaps);

            results.AddRange(qb.Build());
            queryRecords.AddRange(qb.Records);
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
    /// source has no parser for it, or nobody claimed it at all. One shape for all three,
    /// because for the caller it is one event - the unit went in and nothing came of it
    /// (decisions 025 and 045).
    /// </summary>
    private static ConversionRecord NotTranslated(
        ORMEnum targetOrm,
        ConversionContentType artifact,
        string reason)
        => new()
        {
            Kind = ConversionRecordKind.Failure,
            Framework = targetOrm,
            Artifact = artifact,
            Reason = reason,
        };
}
