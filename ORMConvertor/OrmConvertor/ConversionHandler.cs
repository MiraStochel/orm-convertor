using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation;
using OrmConvertor.Factories;

namespace OrmConvertor;

public static class ConversionHandler
{
    public static ConversionResult Convert(
        ORMEnum sourceOrm,
        ORMEnum targetOrm,
        List<ConversionSource> sources,
        string? catalogConnectionString = null,
        SourceSqlDialect? declaredSourceDialect = null,
        ParseLimits? limits = null
    )
    {
        // The reader lives and dies with the request; a caller holding a longer-lived
        // one - a cache over an Advisor run or a test collection - uses the overload
        // below and keeps ownership.
        using SqlServerCatalogReader? reader = string.IsNullOrWhiteSpace(catalogConnectionString)
            ? null
            : new SqlServerCatalogReader(catalogConnectionString);

        return Convert(sourceOrm, targetOrm, sources, reader, declaredSourceDialect, limits);
    }

    /// <param name="declaredSourceDialect">
    /// The dialect the source declares for its literal SQL (decision 088). It is the one
    /// optional input the orchestration gained with that decision and it decides nothing
    /// else: whether the literal SQL of the source is read at all. Null - nobody said
    /// anything - reads exactly as before, and the run record tells the two apart (S6).
    /// The guard itself sits in the readers rather than here, because at this level a unit
    /// that is a mapping and a query at once shows only its content type, which does not
    /// tell the two halves apart (decision 081). It reaches the completion phase for a
    /// different reason (decision 091): the phase reads the connected database rather than
    /// the source, so nothing stops there, but the catalog is always SQL Server and a source
    /// that declared another system says so about the facts it took from one.
    /// </param>
    /// <param name="limits">
    /// The limits the parsers read under (decision 092). It comes from the application's own
    /// configuration, never from the request: the input a cap defends against must not be able
    /// to carry permission to exceed it. Null is the default cap, which is what every caller
    /// but the API - a test, an Advisor run - reads under.
    /// </param>
    public static ConversionResult Convert(
        ORMEnum sourceOrm,
        ORMEnum targetOrm,
        List<ConversionSource> sources,
        ICatalogReader? catalogReader,
        SourceSqlDialect? declaredSourceDialect = null,
        ParseLimits? limits = null
    )
    {
        var parseLimits = limits ?? ParseLimits.Default;

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

        // One refusal per unit, whichever pass ran out of stack on it first (decision 191);
        // the other pass leaves the unit alone, since the same text would end the same way.
        void RefuseTooDeep(UnitOutcome unit)
        {
            if (!unit.Refused)
            {
                unit.Refused = true;
                runRecords.Add(NotTranslated(targetOrm, unit.Source.ContentType, unit.Reference, ReaderStack.Reason));
            }
        }

        // The defects of the tool met during this run (decision 197): each one is a Failure
        // record for the caller and an exception for the log. A reading that threw anything
        // but a stack exhaustion refuses its unit for both passes - the same text would be
        // read by the other pass over a model the failed reading may have left half filled -
        // and what the reading had put into the model before it failed is taken out again.
        var defects = new List<TranslationDefect>();

        void RefuseDefective(UnitOutcome unit, string activity, Exception exception)
        {
            unit.Refused = true;
            runRecords.Add(NotTranslated(targetOrm, unit.Source.ContentType, unit.Reference, TranslationDefect.Reason(activity, exception)));
            defects.Add(new TranslationDefect(exception, Unit: unit.Reference));
        }

        // 1) Build entity maps using entity parsers only
        var entityParsers = ParserFactory.Create(sourceOrm, entityBuilder, qb: null, declaredSourceDialect, parseLimits)
            .OfType<IEntityParser>()
            .ToList();

        // The parser-outer order is source precedence ordered in time (decision 017). What
        // each unit yielded is the parser's own statement, because an enriching parser adds
        // no new map and counting maps around the call would call an honest unit barren
        // (decision 066). Records born during the reading are attributed to the unit,
        // because its reading is their origin (decision 066).
        foreach (var parser in entityParsers)
        {
            foreach (var unit in units.Where(u => parser.CanParse(u.Source.ContentType) && !u.Refused))
            {
                unit.Claimed = true;

                var recordsBefore = entityBuilder.Records.Count;
                var mapsBefore = entityBuilder.EntityMaps.Count;

                IReadOnlyCollection<EntityMap> read;
                try
                {
                    read = parser.Parse(unit.Source.Content);
                }
                catch (InsufficientExecutionStackException)
                {
                    // A reader that ran out of stack refuses its unit instead of ending the
                    // process (decision 191).
                    RefuseTooDeep(unit);
                    continue;
                }
                catch (Exception exception)
                {
                    // A defect of the tool refuses the unit the same way (decision 197). The
                    // maps the failed reading declared leave with it; what it stated about
                    // maps other units declared cannot be told apart and stays.
                    entityBuilder.AttributeRecords(recordsBefore, unit.Reference);
                    entityBuilder.EntityMaps.RemoveRange(mapsBefore, entityBuilder.EntityMaps.Count - mapsBefore);
                    RefuseDefective(unit, $"reading the unit {unit.Reference}", exception);
                    continue;
                }

                entityBuilder.AttributeRecords(recordsBefore, unit.Reference);

                // And what the reading left in the representation: the maps the unit declared
                // or enriched, and every fact that was not there before it (decision 124). The
                // artifacts of those maps will name the unit, and so will the ledger.
                entityBuilder.AttributeFacts(read, unit.Reference);

                unit.Yielded |= read.Count > 0;
            }
        }

        // The completion phase of decision 015 sits between parsing and generation: the
        // target's descriptor formulates the demand, one component reads the catalog, and
        // the phase is timed on its own (S3). The reader is an optional input - a
        // translation without one proceeds on conventions and says so in the records. The
        // queries formulate a demand of their own, served further down (decision 105).
        var catalogPhase = CatalogCompletion.Complete(entityBuilder, catalogReader, declaredSourceDialect);

        // Emit entities for target ORM. One entity that cannot be generated is the builder's
        // own record (decision 197); a defect in the phases over the whole model refuses the
        // entity artifacts as a whole and says so about the run.
        try
        {
            results.AddRange(entityBuilder.Build());
        }
        catch (Exception exception)
        {
            runRecords.Add(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = targetOrm,
                Reason = TranslationDefect.Reason("generating the entities", exception),
            });
            defects.Add(new TranslationDefect(exception));
        }

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
            builder.Limits = parseLimits;

            return builder;
        }

        var queryParsers = ParserFactory.Create(sourceOrm, entityBuilder, NewQueryBuilder, declaredSourceDialect, parseLimits)
            .OfType<IQueryParser>()
            .ToList();

        var targetTakesQueries = QueryBuilderFactory.Supports(targetOrm);

        // Reading and building are two passes rather than one loop, because the demand of
        // decision 105 is served between them - and served once: every query is read first,
        // the tables all of them together need bound to an entity go to the catalog in one
        // batch, and only then is any of them built. One read however many queries the
        // request carries, and none where none of them demands anything.
        var readQueries = new List<(UnitOutcome Unit, IReadOnlyCollection<AbstractQueryBuilder> Builders)>();

        foreach (var unit in units)
        {
            // Exactly one parser per source ORM claims a given query language, so the choice
            // does not depend on the order of the list (decision 025).
            var parser = queryParsers.FirstOrDefault(p => p.CanParse(unit.Source.ContentType));
            if (parser is null || unit.Refused)
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

            IReadOnlyCollection<AbstractQueryBuilder> filled;
            try
            {
                filled = parser.Parse(unit.Source.ContentType, unit.Source.Content, entityBuilder.EntityMaps);

                // The size of the model as written (decision 194): a reader that compensates
                // shares the operand it tests, so a model small in memory is written as many
                // times over as it nests, and every walk after the reading follows the writing.
                // Measured first, because the depth below walks the model as written too.
                if (parseLimits.CapsQueryText)
                {
                    foreach (var builder in filled)
                    {
                        if (builder.WrittenNodes is var nodes && nodes > parseLimits.MaxQueryTextLength)
                        {
                            builder.Discard(QueryTextCap.ModelReason(nodes, parseLimits));
                        }
                    }
                }

                // The depth of the model (decision 191): a chain the text writes without a
                // bracket is as deep in the model as it is long, and the cap of decision 092
                // counts only brackets. Measured once here, between the reading and every walk
                // after it, for all five query languages at once and against the same number.
                if (parseLimits.CapsNesting)
                {
                    foreach (var builder in filled)
                    {
                        if (builder.ModelDepth is var depth && depth > parseLimits.MaxNestingDepth)
                        {
                            builder.Discard(ModelDepth.Reason(depth, parseLimits));
                        }
                    }
                }
            }
            catch (InsufficientExecutionStackException)
            {
                RefuseTooDeep(unit);
                continue;
            }
            catch (Exception exception)
            {
                // A defect of the tool in the reading or in the measuring of what was read
                // refuses the unit (decision 197).
                RefuseDefective(unit, $"reading the unit {unit.Reference}", exception);
                continue;
            }

            // An empty collection is no error in itself: a mapping document that carries no
            // query is an ordinary input, and whether the unit was barren is asked of both
            // passes together below (decision 081).
            unit.Yielded |= filled.Count > 0;

            readQueries.Add((unit, filled));
        }

        // The queries' own demand on the catalog (decision 105): the tables a parameter has
        // to be typed from and no stated mapping binds. Served after the entity artifacts
        // are out, so that they stay exactly what the target's demand determined, and before
        // any query is built, so that the gate of decision 083 finds the binding as a stated
        // mapping. Its time adds to the entity phase's (S3).
        var queryPhase = QueryDemandCompletion.Complete(
            entityBuilder,
            [.. readQueries.SelectMany(read => read.Builders)],
            catalogReader,
            declaredSourceDialect);

        foreach (var (unit, filled) in readQueries)
        {
            foreach (var queryBuilder in filled)
            {
                // A defect of the tool met while generating this one query refuses the query
                // and leaves the rest of the unit alone (decision 197); the builder's own
                // records still leave with it below.
                var built = new List<ConversionSource>();
                try
                {
                    built.AddRange(queryBuilder.Build());
                }
                catch (Exception exception)
                {
                    queryRecords.Add(NotTranslated(
                        targetOrm,
                        unit.Source.ContentType,
                        unit.Reference,
                        TranslationDefect.Reason($"generating the query {queryBuilder.QueryName}", exception)) with { Query = queryBuilder.QueryName });
                    defects.Add(new TranslationDefect(exception, Unit: unit.Reference, Query: queryBuilder.QueryName));
                }

                // A query artifact belongs to no entity and came from exactly this unit, so
                // it leaves with the unit on it (decision 124) - stated here, where the loop
                // knows the unit, rather than guessed from names on the client.
                results.AddRange(built.Select(artifact => new ConversionSource
                {
                    ContentType = artifact.ContentType,
                    Content = artifact.Content,
                    Name = artifact.Name,
                    Units = [unit.Reference],
                }));

                // Each builder was made for one query of this one unit and dies with it, so
                // every record it holds - the parser's, the catalog's and the build's alike -
                // came from that query (decisions 066 and 081). The query's own name is what
                // tells the records of two queries of one document apart.
                queryRecords.AddRange(queryBuilder.Records.Select(r => r with
                {
                    Unit = unit.Reference,
                    Query = queryBuilder.QueryName,
                }));
            }
        }

        // One state and one time for the run, whichever of the two phases met the catalog.
        var catalog = catalogPhase.Then(queryPhase);

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
                // A value that names a role is an artifact's, which no parser claims on input
                // (decision 111); the record says which value the unit should have declared,
                // because the client that sent it most likely took it from an artifact.
                var language = unit.Source.ContentType.LanguageOf();
                var hint = language == unit.Source.ContentType
                    ? string.Empty
                    : $" {unit.Source.ContentType} names an artifact the tool writes; an input unit declares its language, {language}.";

                runRecords.Add(NotTranslated(
                    targetOrm,
                    unit.Source.ContentType,
                    unit.Reference,
                    $"{sourceOrm} has no parser for a {unit.Source.ContentType} unit, so it was not read.{hint}"));
            }
            else if (!unit.Yielded && !unit.Refused)
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
            DeclaredSourceDialect = declaredSourceDialect,
            MaxNestingDepth = parseLimits.MaxNestingDepth,
            MaxQueryTextLength = parseLimits.MaxQueryTextLength,
            Sources = results,
            Records = [.. entityBuilder.Records, .. queryRecords, .. runRecords],
            Facts = [.. entityBuilder.Facts],
            Defects = [.. defects, .. entityBuilder.Defects],
            CatalogState = catalog.ConnectionState,
            CatalogReadTime = catalog.ReadTime,
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

        /// <summary>
        /// Its reading ran out of stack (decision 191) or met a defect of the tool (decision
        /// 197); the refusal says all there is to say about it, and the other pass leaves it alone.
        /// </summary>
        public bool Refused { get; set; }
    }
}
