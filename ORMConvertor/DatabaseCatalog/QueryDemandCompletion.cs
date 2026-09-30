using System.Diagnostics;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Common.Sql;
using Model;
using Model.AbstractRepresentation;

namespace DatabaseCatalog;

/// <summary>
/// The second consumer of the catalog (decision 105): the demand a query formulates, served
/// between the reading of the queries and their generation by the rules the completion phase
/// of decision 015 serves the target's demand with. A query demands nothing but a binding -
/// which entity of the conversion the table it names is - for the tables the gate of
/// decision 083 has to type a parameter from and no stated mapping resolves. The catalog
/// answers the way the entity phase already pairs a Dapper entity with its table whenever
/// the target asks for one: the candidate is the entity the naming rule of decision 050
/// would put in that table, and the catalog confirms that the table exists, in the schema
/// the query named if it named one. The binding is a fact of the schema, a fact of the
/// second degree, so the gate takes it as a stated mapping; the convention alone still
/// types nothing.
///
/// Three things hold it to decision 015. It is one bounded step with a time of its own
/// (S3), not a call from inside Build. It writes incrementally - a table or schema the
/// source or the entity phase already stated is never overwritten, only the missing half
/// is filled - and it writes only after the entity artifacts have been built, so that
/// they come out exactly as the target's demand determined them and the mechanical loss
/// rule has nothing to report about a fact the source never stated. And a run whose queries
/// demand nothing reads nothing: a conversion without queries, or with queries whose
/// parameters are typed from a constant, a pattern, an unqualified column or a mapped
/// table, leaves the connection where the entity phase left it.
/// </summary>
public static class QueryDemandCompletion
{
    /// <summary>
    /// Serves the demand of every query of the conversion in one batch - one read of the
    /// catalog however many queries there are, however many of them name the same table.
    /// Records go to the query that demanded the table first, in the order the input orders
    /// the units and the queries within them, so the same input yields the same records
    /// (S2): the <see cref="ConversionRecordKind.Supplied"/> record that carries the origin
    /// of the binding, or the <see cref="ConversionRecordKind.Incompleteness"/> record that
    /// says why there is none - no connection, no such table, more than one, no entity that
    /// could be it - which is what tells a missing catalog apart from a catalog that does not
    /// have the table (decision 105). The gate then refuses the parameter as it always did.
    /// </summary>
    /// <param name="entityBuilder">The conversion's entity builder: its maps are what a binding is written into, and its records carry the one run-level record of decision 091.</param>
    /// <param name="queryBuilders">Every query of the conversion, read and not yet built, in input order.</param>
    /// <param name="reader">The catalog, or null where no connection is configured.</param>
    /// <param name="declaredSourceDialect">What the source declared about its dialect (decision 088), for the caveat of decision 091.</param>
    public static CatalogPhaseResult Complete(
        AbstractEntityBuilder entityBuilder,
        IReadOnlyList<AbstractQueryBuilder> queryBuilders,
        ICatalogReader? reader,
        SourceSqlDialect? declaredSourceDialect = null)
    {
        ArgumentNullException.ThrowIfNull(entityBuilder);
        ArgumentNullException.ThrowIfNull(queryBuilders);

        // One demand per table across all queries, remembered with the query that demanded
        // it first. A second query naming the same table adds nothing: the binding, once
        // written, is a stated mapping for every query built after it.
        var demands = new List<Demand>();

        foreach (var builder in queryBuilders)
        {
            foreach (var table in builder.CatalogDemand())
            {
                if (!demands.Any(known => known.Table.Names(table)))
                {
                    demands.Add(new Demand(table, builder));
                }
            }
        }

        var state = reader is null ? CatalogConnectionState.NotConfigured : CatalogConnectionState.Unused;

        if (demands.Count == 0)
        {
            return new CatalogPhaseResult(state, null);
        }

        if (reader is null)
        {
            foreach (var demand in demands)
            {
                Report(demand, ConversionRecordKind.Incompleteness,
                    $"No database connection is configured, so the catalog cannot say which entity of the conversion "
                    + $"the table '{demand.Table.QualifiedName}' is; the source states no mapping for it, and a parameter "
                    + "compared with one of its columns cannot be typed (decision 105).");
            }

            return new CatalogPhaseResult(state, null);
        }

        // The candidate for a table is the entity the naming rule of decision 050 would put
        // in it - exactly the pairing the entity phase makes for a source that states no
        // table. An entity already bound to a table is not a candidate for another one; an
        // entity bound to this table would have resolved the qualifier and left no demand.
        var entities = entityBuilder.EntityMaps.Where(em => em.Entity.Name.Length > 0).ToList();
        var candidates = new Dictionary<Demand, EntityMap>();
        var requests = new List<TableRequest>();

        foreach (var demand in demands)
        {
            var matching = entities
                .Where(em => em.Table is null
                    && EntityTableNaming.TableCandidatesFor(em.Entity.Name)
                        .Contains(demand.Table.Table, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (matching.Count == 0)
            {
                Report(demand, ConversionRecordKind.Incompleteness,
                    $"No entity of the conversion is named so that the naming rule would put it in the table "
                    + $"'{demand.Table.QualifiedName}', so the catalog was not asked about it; the source states no mapping "
                    + "for the table, and a parameter compared with one of its columns cannot be typed (decisions 050 and 105).");
                continue;
            }

            if (matching.Count > 1)
            {
                Report(demand, ConversionRecordKind.Incompleteness,
                    $"More than one entity of the conversion ({string.Join(", ", matching.Select(em => em.Entity.Name))}) "
                    + $"could stand for the table '{demand.Table.QualifiedName}' by the naming rule, so no binding was made; "
                    + "a parameter compared with one of its columns cannot be typed (decisions 050 and 105).");
                continue;
            }

            candidates[demand] = matching[0];
            requests.Add(new TableRequest(demand.Key, demand.Table.Schema ?? matching[0].Schema, [demand.Table.Table]));
        }

        if (requests.Count == 0)
        {
            return new CatalogPhaseResult(state, null);
        }

        var stopwatch = Stopwatch.StartNew();
        IReadOnlyDictionary<string, TableLookup>? lookups = null;

        try
        {
            lookups = reader.ReadTables(requests);
            state = CatalogConnectionState.Reached;
        }
        catch (Exception ex)
        {
            // A configured but unreachable catalog is infrastructure, not input; the query
            // goes on to the gate, which refuses the parameter, and the record says why
            // (decision 015).
            state = CatalogConnectionState.Unreachable;

            foreach (var demand in candidates.Keys)
            {
                Report(demand, ConversionRecordKind.Incompleteness,
                    $"The catalog could not be read ({ex.Message}), so it cannot say whether the table "
                    + $"'{demand.Table.QualifiedName}' is the entity's; a parameter compared with one of its columns "
                    + "cannot be typed (decision 105).");
            }
        }

        stopwatch.Stop();

        var supplied = false;

        if (lookups is not null)
        {
            foreach (var (demand, em) in candidates)
            {
                var lookup = lookups.GetValueOrDefault(demand.Key);

                if (lookup?.Image is null)
                {
                    Report(demand, ConversionRecordKind.Incompleteness,
                        lookup is { AmbiguousMatches.Count: > 0 }
                            ? $"More than one table in the catalog matches '{demand.Table.QualifiedName}' "
                                + $"({string.Join(", ", lookup.AmbiguousMatches)}), so the catalog cannot bind it to the entity "
                                + $"'{em.Entity.Name}' without a schema in the query; a parameter compared with one of its columns "
                                + "cannot be typed (decision 105)."
                            : $"No table matching '{demand.Table.QualifiedName}' was found in the catalog, so it cannot be bound "
                                + $"to the entity '{em.Entity.Name}'; a parameter compared with one of its columns cannot be typed "
                                + "(decision 105).");
                    continue;
                }

                supplied |= Bind(demand, em, lookup.Image);
            }
        }

        // The one run-level caveat of decision 091, written here only where the entity phase
        // had no fact of the catalog to write it about - a target with an empty demand - so
        // that a run carries it once whichever phase met the catalog first.
        if (supplied
            && ForeignDialect.ContradictsTheCatalog(declaredSourceDialect)
            && !entityBuilder.Records.Any(r => r.Kind == ConversionRecordKind.Conflict && r.Reason == ForeignDialect.CatalogReason))
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Conflict,
                Framework = entityBuilder.Descriptor.Framework,
                Reason = ForeignDialect.CatalogReason,
            });
        }

        return new CatalogPhaseResult(state, stopwatch.Elapsed);
    }

    /// <summary>
    /// Writes the binding into the entity map - the name and the schema of the table, and
    /// nothing else, because the query demanded nothing else - incrementally, as decision
    /// 015 writes: a half the source or the entity phase already stated stays. Returns
    /// whether anything was written.
    /// </summary>
    private static bool Bind(Demand demand, EntityMap em, TableImage image)
    {
        var written = false;

        if (em.Table is null)
        {
            em.Table = image.Name;
            written = true;
            Report(demand, ConversionRecordKind.Supplied,
                $"Table name '{image.Name}' supplied by the database catalog: the query names the table and the source "
                + $"states no mapping for it, and the binding is what types the parameter compared with its column (decision 105).",
                em.Entity.Name, MappingFactCategory.TableName);
        }

        if (em.Schema is null)
        {
            em.Schema = image.Schema;
            written = true;
            Report(demand, ConversionRecordKind.Supplied,
                $"Schema '{image.Schema}' supplied by the database catalog with the table the query names (decision 105).",
                em.Entity.Name, MappingFactCategory.SchemaName);
        }

        return written;
    }

    /// <summary>
    /// A record of the query that demanded the table. It goes to that query's builder, so
    /// the orchestration attributes it to the query's unit and name like every other record
    /// of the builder (decisions 066 and 081); the feature is the parameter the demand
    /// exists for, and the entity and category are the fact where one was written.
    /// </summary>
    private static void Report(
        Demand demand,
        ConversionRecordKind kind,
        string reason,
        string? entity = null,
        MappingFactCategory? category = null)
        => demand.Builder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = demand.Builder.Descriptor.Framework,
            Entity = entity,
            Category = category,
            Feature = QueryFeature.QueryParameter,
            Reason = reason,
        });

    /// <summary>One demanded table and the query that demanded it first.</summary>
    private sealed record Demand(QueryTableDemand Table, AbstractQueryBuilder Builder)
    {
        /// <summary>The key the catalog's answer comes back under: the name as written, which is unique across the batch.</summary>
        public string Key => Table.QualifiedName;
    }
}
