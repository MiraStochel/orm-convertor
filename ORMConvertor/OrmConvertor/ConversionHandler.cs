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

        // 1) Build entity maps using entity parsers only
        var entityParsers = ParserFactory.Create(sourceOrm, entityBuilder, qb: null)
            .Where(p => p is not IQueryParser)
            .ToList();
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
        var catalogReadTime = CatalogCompletion.Complete(entityBuilder, catalogReader);

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
                queryRecords.Add(NoQuerySupport(
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
                queryRecords.Add(NoQuerySupport(
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

        // The records accumulate on the entity builder - parsers and the build phases both
        // report there - and leave as returned data next to the artifacts (decision 010).
        // The framework versions come from the descriptors (decision 013), so the run
        // record and the generator cannot disagree about them (S6).
        return new ConversionResult
        {
            RunId = Guid.NewGuid(),
            SourceFramework = sourceOrm,
            SourceFrameworkVersion = DescriptorFactory.Create(sourceOrm).Version,
            TargetFramework = targetOrm,
            TargetFrameworkVersion = entityBuilder.Descriptor.Version,
            Sources = results,
            Records = [.. entityBuilder.Records, .. queryRecords],
            CatalogReadTime = catalogReadTime,
        };
    }

    private static ConversionRecord NoQuerySupport(
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
