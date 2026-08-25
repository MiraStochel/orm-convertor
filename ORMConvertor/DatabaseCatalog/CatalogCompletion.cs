using System.Diagnostics;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace DatabaseCatalog;

/// <summary>
/// The completion phase of decision 015: sits between parsing and generation, formulates
/// the demand from the target framework's descriptor, reads the whole column image of the
/// affected tables in one batch, and writes into the intermediate representation only what
/// the demand covers. Writing is incremental and idempotent - a fact once present is never
/// overwritten; the source outranks the catalog, and a disagreement is reported as a
/// conflict record instead of being resolved silently. A translation without a database
/// must not fail: an absent or unreachable catalog becomes a record, not an exception.
/// </summary>
public static class CatalogCompletion
{
    /// <summary>
    /// Runs the phase over the builder's accumulated entities. Returns the state of the
    /// catalog connection - the first-class answer the /convert response carries beside
    /// the records (decision 030) - and the time the catalog read and write took, so it
    /// can be reported separately from translation time (S3); a null time means the
    /// connection was never tried - an empty demand means zero queries (decision 015).
    /// </summary>
    public static CatalogPhaseResult Complete(AbstractEntityBuilder builder, ICatalogReader? reader)
    {
        // A key class named by a composite key is not an entity of the conversion and
        // has no table, so it dissolves into the key before the catalog would look one
        // up (decision 031). Only then the source's conventional claims stand up before
        // the catalog is compared against them - a navigation the source states by
        // convention is a first-degree fact and outranks anything read below (decision 015).
        builder.DissolveKeyClasses();
        builder.ResolveConventionNavigations();

        var demand = Enum.GetValues<MappingFactCategory>()
            .Where(category => builder.Descriptor.SupportOf(category) != FactSupport.NotExpressible)
            .ToHashSet();

        TimeSpan? elapsed = null;
        var state = reader is null ? CatalogConnectionState.NotConfigured : CatalogConnectionState.Unused;

        if (demand.Count > 0 && builder.EntityMaps.Any(em => em.Entity.Name.Length > 0))
        {
            if (reader is null)
            {
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Reason = "No database connection is configured, so the catalog cannot supply the facts "
                        + "the target could express; missing facts fall back to conventions (decision 015).",
                });
            }
            else
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    Apply(builder, reader, demand);
                    state = CatalogConnectionState.Reached;
                }
                catch (Exception ex)
                {
                    // A configured but unreachable catalog is infrastructure, not input;
                    // the translation continues on conventions and says why (decision 015).
                    state = CatalogConnectionState.Unreachable;
                    builder.Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Incompleteness,
                        Framework = builder.Descriptor.Framework,
                        Reason = $"The catalog could not be read ({ex.Message}); missing facts fall back "
                            + "to conventions (decision 015).",
                    });
                }

                stopwatch.Stop();
                elapsed = stopwatch.Elapsed;
            }
        }

        InferLanguageTypes(builder);

        return new CatalogPhaseResult(state, elapsed);
    }

    private static void Apply(AbstractEntityBuilder builder, ICatalogReader reader, HashSet<MappingFactCategory> demand)
    {
        var entities = builder.EntityMaps.Where(em => em.Entity.Name.Length > 0).ToList();

        var requests = entities
            .Select(em => new TableRequest(
                em.Entity.Name,
                em.Schema,
                em.Table is not null ? [em.Table] : EntityTableNaming.TableCandidatesFor(em.Entity.Name)))
            .ToList();

        var lookups = reader.ReadTables(requests);

        // Which entity a referenced table belongs to, for foreign keys between entities
        // of the same conversion.
        var entityByTable = new Dictionary<(string Schema, string Table), EntityMap>();
        var imageByEntity = new Dictionary<EntityMap, TableImage>();

        foreach (var em in entities)
        {
            if (lookups.TryGetValue(em.Entity.Name, out var lookup) && lookup.Image is not null)
            {
                imageByEntity[em] = lookup.Image;
                entityByTable[(lookup.Image.Schema.ToLowerInvariant(), lookup.Image.Name.ToLowerInvariant())] = em;
            }
        }

        // First the facts of each entity itself - table, schema, columns, primary key -
        // so that every key stands before the foreign keys that reference it.
        foreach (var em in entities)
        {
            var lookup = lookups.GetValueOrDefault(em.Entity.Name);

            if (lookup?.Image is null)
            {
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Entity = em.Entity.Name,
                    Reason = lookup is { AmbiguousMatches.Count: > 0 }
                        ? $"More than one table matches the entity ({string.Join(", ", lookup.AmbiguousMatches)}); "
                            + "the catalog cannot complete its mapping facts without a stated table or schema."
                        : "No table matching the entity was found in the catalog; its mapping facts cannot be completed.",
                });
                continue;
            }

            CompleteEntity(builder, em, lookup.Image, demand);
        }

        if (demand.Contains(MappingFactCategory.ForeignKeyColumns))
        {
            foreach (var em in entities)
            {
                if (imageByEntity.TryGetValue(em, out var image))
                {
                    CompleteForeignKeys(builder, em, image, entityByTable);
                }
            }

            MarkJunctionEntities(builder, imageByEntity);
            var ambiguousPairs = CompleteManyToMany(builder, reader, imageByEntity, entityByTable);
            CompleteInverseCollections(builder, imageByEntity, entityByTable, ambiguousPairs);
        }
    }

    /// <summary>
    /// An entity of the conversion whose table is junction-shaped - the whole primary key
    /// consists of two foreign keys - gets the junction flag of decision 005. A fact of
    /// the schema, so it carries its origin as a record.
    /// </summary>
    private static void MarkJunctionEntities(AbstractEntityBuilder builder, Dictionary<EntityMap, TableImage> imageByEntity)
    {
        foreach (var (em, image) in imageByEntity)
        {
            if (!em.IsJunctionTable && JunctionShape.TryGet(image) is not null)
            {
                em.IsJunctionTable = true;
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Supplied,
                    Framework = builder.Descriptor.Framework,
                    Entity = em.Entity.Name,
                    Reason = $"The whole primary key of '{image.QualifiedName}' consists of two foreign keys, "
                        + "so the entity is marked as a junction table (decisions 005 and 015).",
                });
            }
        }
    }

    /// <summary>
    /// The many-to-many nobody's artifact expresses (decision 005): a junction-shaped
    /// table outside the conversion whose two foreign keys point at tables of two
    /// conversion entities. Where the source declares a collection navigation towards the
    /// far side, a many-to-many relation with the junction table's facts is registered and
    /// the standard synthesis builds the junction entity before generation; a many-to-many
    /// the source stated without naming its table gets the missing facts supplied instead.
    /// No member is invented - a side without a collection navigation stays untouched and
    /// the unused junction table is reported. Returns the entity pairs a junction table
    /// and a direct foreign key both link - a collection between them is ambiguous, so no
    /// relation of either reading may be derived from the catalog.
    /// </summary>
    private static HashSet<(EntityMap, EntityMap)> CompleteManyToMany(
        AbstractEntityBuilder builder, ICatalogReader reader,
        Dictionary<EntityMap, TableImage> imageByEntity,
        Dictionary<(string Schema, string Table), EntityMap> entityByTable)
    {
        var ambiguousPairs = new HashSet<(EntityMap, EntityMap)>();

        foreach (var junction in reader.FindJunctionTables(imageByEntity.Values.ToList()))
        {
            if (entityByTable.ContainsKey((junction.Schema.ToLowerInvariant(), junction.Name.ToLowerInvariant())))
            {
                continue; // the junction is an entity of the conversion; MarkJunctionEntities covered it
            }

            if (JunctionShape.TryGet(junction) is not { } shape)
            {
                continue;
            }

            var first = entityByTable.GetValueOrDefault((shape.First.ReferencedSchema.ToLowerInvariant(), shape.First.ReferencedTable.ToLowerInvariant()));
            var second = entityByTable.GetValueOrDefault((shape.Second.ReferencedSchema.ToLowerInvariant(), shape.Second.ReferencedTable.ToLowerInvariant()));

            if (first is null || second is null || ReferenceEquals(first, second))
            {
                continue; // a side outside the conversion, or a self-referencing junction
            }

            // A direct foreign key between the two tables would make a collection
            // ambiguous - the inverse of the direct key, or the far side of the junction -
            // so nothing is derived and the ambiguity is reported.
            if (HasDirectForeignKey(imageByEntity[first], imageByEntity[second])
                || HasDirectForeignKey(imageByEntity[second], imageByEntity[first]))
            {
                ambiguousPairs.Add((first, second));
                ambiguousPairs.Add((second, first));
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"Both the junction table '{junction.QualifiedName}' and a direct foreign key link "
                        + $"'{first.Entity.Name}' and '{second.Entity.Name}'; a collection between them is ambiguous, "
                        + "so no many-to-many is derived.",
                });
                continue;
            }

            var firstColumns = OrderByReferencedKey(shape.First, first);
            var secondColumns = OrderByReferencedKey(shape.Second, second);

            if (firstColumns is null || secondColumns is null)
            {
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"The junction table '{junction.QualifiedName}' links '{first.Entity.Name}' and "
                        + $"'{second.Entity.Name}', but its columns do not pair with their keys; nothing is derived.",
                });
                continue;
            }

            var used = CompleteManyToManySide(builder, junction, first, second, firstColumns, secondColumns)
                | CompleteManyToManySide(builder, junction, second, first, secondColumns, firstColumns);

            if (!used)
            {
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"The catalog states the junction table '{junction.QualifiedName}' between "
                        + $"'{first.Entity.Name}' and '{second.Entity.Name}', but neither entity has a collection "
                        + "navigation towards the other; the many-to-many is not generated.",
                });
                continue;
            }

            ReportJunctionPayload(builder, junction, firstColumns, secondColumns);
        }

        return ambiguousPairs;
    }

    /// <summary>
    /// One side of a catalog-detected many-to-many: an existing relation gets the facts it
    /// lacks, a collection navigation without a relation gets one registered. Returns
    /// whether the junction table found a consumer on this side.
    /// </summary>
    private static bool CompleteManyToManySide(
        AbstractEntityBuilder builder, TableImage junction,
        EntityMap em, EntityMap other, List<string> myColumns, List<string> otherColumns)
    {
        var existing = em.Relations
            .Where(r => r.Cardinality == Cardinality.ManyToMany
                && SimpleEntityName(r.TargetEntity) == other.Entity.Name)
            .ToList();

        if (existing.Count > 0)
        {
            foreach (var relation in existing)
            {
                var stated = builder.StatedJunctionFacts(relation);

                if (stated is { Table: not null, TargetColumns: not null })
                {
                    continue; // the source stated everything; the synthesis needs nothing more
                }

                // Merged so that every fact the source stated outranks the catalog (decision 015).
                builder.SupplyJunctionFacts(relation, new JunctionFacts(
                    stated?.Table ?? junction.Name,
                    stated?.Schema ?? junction.Schema,
                    stated?.TargetColumns ?? otherColumns));

                ReportSupplied(builder, em, relation.SourceNavigationProperty, MappingFactCategory.ForeignKeyColumns,
                    $"The junction table '{junction.QualifiedName}' behind the many-to-many towards "
                    + $"'{other.Entity.Name}' supplied by the database catalog.");
            }

            return true;
        }

        var navigation = FindCollectionNavigation(em, other.Entity.Name);

        if (navigation is null)
        {
            return false;
        }

        builder.EntityMap = em;
        builder.AddForeignKey(
            Cardinality.ManyToMany,
            navigation.Name,
            other.Entity.Name,
            foreignKeyColumns: myColumns,
            junction: new JunctionFacts(junction.Name, junction.Schema, otherColumns));

        ReportSupplied(builder, em, navigation.Name, MappingFactCategory.ForeignKeyColumns,
            $"Many-to-many towards '{other.Entity.Name}' over the junction table '{junction.QualifiedName}' "
            + "supplied by the database catalog; the source declares the collection, the schema the association.");

        return true;
    }

    /// <summary>
    /// A collection navigation towards the named entity that no relation claims yet. The
    /// element is a reference - or an unknown naming the entity, which the resolution
    /// phase upgrades before generation.
    /// </summary>
    private static Property? FindCollectionNavigation(EntityMap em, string targetName)
        => em.Entity.Properties.FirstOrDefault(p =>
            p.Type is { Category: LangTypeCategory.Collection } type
            && ((type.ElementType is { Category: LangTypeCategory.Reference } reference && reference.TargetEntity == targetName)
                || (type.ElementType is { Category: LangTypeCategory.Unknown } unknown && unknown.SourceName == targetName))
            && !em.Relations.Any(r => r.SourceNavigationProperty == p.Name));

    private static bool HasDirectForeignKey(TableImage from, TableImage to)
        => from.ForeignKeys.Any(fk =>
            string.Equals(fk.ReferencedSchema, to.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(fk.ReferencedTable, to.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Columns of the junction table beyond its two foreign keys. The synthesized entity
    /// is built from the association's facts alone, so payload columns do not reach it -
    /// said openly rather than silently.
    /// </summary>
    private static void ReportJunctionPayload(
        AbstractEntityBuilder builder, TableImage junction, List<string> firstColumns, List<string> secondColumns)
    {
        var keyColumns = firstColumns.Concat(secondColumns).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var payload = junction.Columns
            .Select(c => c.Name)
            .Where(name => !keyColumns.Contains(name))
            .ToList();

        if (payload.Count > 0)
        {
            builder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = builder.Descriptor.Framework,
                Reason = $"The junction table '{junction.QualifiedName}' carries columns beyond its foreign keys "
                    + $"({string.Join(", ", payload)}); nobody declares them as properties, so the synthesized "
                    + "junction entity does not generate them.",
            });
        }
    }

    private static void CompleteEntity(AbstractEntityBuilder builder, EntityMap em, TableImage image, HashSet<MappingFactCategory> demand)
    {
        if (demand.Contains(MappingFactCategory.TableName) && em.Table is null)
        {
            em.Table = image.Name;
            ReportSupplied(builder, em, null, MappingFactCategory.TableName,
                $"Table name '{image.Name}' supplied by the database catalog.");
        }

        if (demand.Contains(MappingFactCategory.SchemaName) && em.Schema is null)
        {
            em.Schema = image.Schema;
            ReportSupplied(builder, em, null, MappingFactCategory.SchemaName,
                $"Schema '{image.Schema}' supplied by the database catalog.");
        }

        foreach (var pm in em.PropertyMaps)
        {
            if (IsNavigation(builder, em, pm))
            {
                continue;
            }

            var column = image.FindColumn(pm.ColumnName ?? pm.Property.Name);

            if (column is null)
            {
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Entity = em.Entity.Name,
                    Property = pm.Property.Name,
                    Reason = $"No column of '{image.QualifiedName}' matches the property; the catalog cannot complete its mapping facts.",
                });
                continue;
            }

            CompleteColumn(builder, em, pm, column, image, demand);
        }

        if (demand.Contains(MappingFactCategory.PrimaryKey) && image.PrimaryKeyColumns.Count > 0)
        {
            CompletePrimaryKey(builder, em, image);
        }

        if (demand.Contains(MappingFactCategory.UniqueConstraint) && image.UniqueConstraints.Count > 0)
        {
            CompleteUniqueConstraints(builder, em, image);
        }
    }

    /// <summary>
    /// Unique constraints the catalog states (decision 055). Each one is translated from
    /// columns to properties, because that is what the model names; a constraint with a
    /// column no property maps is not supplied, the same rule the primary key follows -
    /// inventing the member would put into the class what the source never declared.
    ///
    /// Identity is the set of properties, not the name, so a constraint the source already
    /// carries is not supplied a second time; where the two name it differently, the source
    /// wins and the catalog's name is reported (rule E9, decision 015). The comparison is
    /// made here rather than left to the builder, because only here is it a disagreement
    /// with the catalog rather than between two input sources.
    /// </summary>
    private static void CompleteUniqueConstraints(AbstractEntityBuilder builder, EntityMap em, TableImage image)
    {
        foreach (var constraint in image.UniqueConstraints)
        {
            var propertyNames = new List<string>();
            string? unmapped = null;

            foreach (var column in constraint.Columns)
            {
                var pm = FindPropertyMapForColumn(em, column);

                if (pm is null)
                {
                    unmapped = column;
                    break;
                }

                propertyNames.Add(pm.Property.Name);
            }

            if (unmapped is not null)
            {
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Entity = em.Entity.Name,
                    Category = MappingFactCategory.UniqueConstraint,
                    Reason = $"The catalog states the unique constraint '{constraint.Name}' "
                        + $"({string.Join(", ", constraint.Columns)}) of '{image.QualifiedName}', "
                        + $"but no property matches the column '{unmapped}'; the constraint is not supplied.",
                });
                continue;
            }

            var subject = propertyNames.Count == 1 ? propertyNames[0] : null;

            var known = em.UniqueConstraints.FirstOrDefault(c =>
                c.PropertyNames.Count == propertyNames.Count
                && c.PropertyNames.ToHashSet(StringComparer.Ordinal).SetEquals(propertyNames));

            if (known is not null)
            {
                if (known.Name is null)
                {
                    // The source stated the set without naming it, so the catalog completes
                    // it rather than contradicting it.
                    builder.EntityMap = em;
                    builder.AddUniqueConstraint(constraint.Name, propertyNames);

                    ReportSupplied(builder, em, subject, MappingFactCategory.UniqueConstraint,
                        $"Name '{constraint.Name}' of the unique constraint over ({string.Join(", ", propertyNames)}) "
                        + $"supplied by the database catalog from '{image.QualifiedName}'.");
                }
                else if (!string.Equals(known.Name, constraint.Name, StringComparison.OrdinalIgnoreCase))
                {
                    ReportConflict(builder, em, subject, MappingFactCategory.UniqueConstraint,
                        $"The source names the unique constraint over ({string.Join(", ", propertyNames)}) "
                        + $"'{known.Name}', the catalog names it '{constraint.Name}' in '{image.QualifiedName}'.");
                }

                continue;
            }

            builder.EntityMap = em;
            builder.AddUniqueConstraint(constraint.Name, propertyNames);

            ReportSupplied(builder, em, subject, MappingFactCategory.UniqueConstraint,
                $"Unique constraint '{constraint.Name}' ({string.Join(", ", constraint.Columns)}) "
                + $"supplied by the database catalog from '{image.QualifiedName}'.");
        }
    }

    private static void CompleteColumn(
        AbstractEntityBuilder builder, EntityMap em, PropertyMap pm, ColumnImage column, TableImage image, HashSet<MappingFactCategory> demand)
    {
        if (demand.Contains(MappingFactCategory.ColumnName) && pm.ColumnName is null
            && !string.Equals(column.Name, pm.Property.Name, StringComparison.Ordinal))
        {
            // An exactly matching name is left implicit - every builder falls back to the
            // property name, so writing it down would state nothing (decision 015).
            pm.ColumnName = column.Name;
            ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.ColumnName,
                $"Column name '{column.Name}' supplied by the database catalog from '{image.QualifiedName}'.");
        }

        if (demand.Contains(MappingFactCategory.DatabaseType) && column.Type is not null)
        {
            if (pm.Type is null)
            {
                // The unicode facet and the literal spelling are part of the same type
                // claim (decision 019), so they arrive together with the family.
                pm.Type = column.Type;
                pm.IsUnicode ??= column.IsUnicode;
                pm.SourceSqlType ??= column.SourceSqlType;
                ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.DatabaseType,
                    $"Database type {column.Type} supplied by the database catalog from '{image.QualifiedName}'.");
            }
            else if (pm.Type != column.Type)
            {
                ReportConflict(builder, em, pm.Property.Name, MappingFactCategory.DatabaseType,
                    $"The source maps the property as {pm.Type}, the catalog column '{column.Name}' is {column.Type}.");
            }
            else if (pm.IsUnicode is null && column.IsUnicode is not null)
            {
                pm.IsUnicode = column.IsUnicode;
                ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.DatabaseType,
                    $"The unicode facet ({(column.IsUnicode.Value ? "unicode" : "non-unicode")}) of the {column.Type} column "
                    + $"'{column.Name}' supplied by the database catalog from '{image.QualifiedName}'.");
            }
            else if (column.IsUnicode is not null && pm.IsUnicode != column.IsUnicode)
            {
                ReportConflict(builder, em, pm.Property.Name, MappingFactCategory.DatabaseType,
                    $"The source maps the property as {(pm.IsUnicode!.Value ? "unicode" : "non-unicode")} {pm.Type}, "
                    + $"the catalog column '{column.Name}' is {(column.IsUnicode.Value ? "unicode" : "non-unicode")}.");
            }
        }

        if (demand.Contains(MappingFactCategory.Length) && column.Length is not null)
        {
            if (pm.Length is null)
            {
                pm.Length = column.Length;
                ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.Length,
                    $"Length {column.Length} supplied by the database catalog from '{image.QualifiedName}'.");
            }
            else if (pm.Length != column.Length)
            {
                ReportConflict(builder, em, pm.Property.Name, MappingFactCategory.Length,
                    $"The source states length {pm.Length}, the catalog column '{column.Name}' has {column.Length}.");
            }
        }

        if (demand.Contains(MappingFactCategory.PrecisionAndScale) && (column.Precision is not null || column.Scale is not null))
        {
            if (pm.Precision is null && pm.Scale is null)
            {
                pm.Precision = column.Precision;
                pm.Scale = column.Scale;
                ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.PrecisionAndScale,
                    $"Precision and scale ({column.Precision}, {column.Scale}) supplied by the database catalog from '{image.QualifiedName}'.");
            }
            else if (pm.Precision != column.Precision || (pm.Scale ?? 0) != (column.Scale ?? 0))
            {
                ReportConflict(builder, em, pm.Property.Name, MappingFactCategory.PrecisionAndScale,
                    $"The source states precision and scale ({pm.Precision}, {pm.Scale}), the catalog column '{column.Name}' has ({column.Precision}, {column.Scale}).");
            }
        }

        if (demand.Contains(MappingFactCategory.Nullability))
        {
            if (pm.IsNullable is null)
            {
                pm.IsNullable = column.IsNullable;
                ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.Nullability,
                    $"Nullability ({(column.IsNullable ? "NULL" : "NOT NULL")}) supplied by the database catalog from '{image.QualifiedName}'.");
            }
            else if (pm.IsNullable != column.IsNullable)
            {
                ReportConflict(builder, em, pm.Property.Name, MappingFactCategory.Nullability,
                    $"The source states the property is {(pm.IsNullable.Value ? "nullable" : "not nullable")}, the catalog column '{column.Name}' is {(column.IsNullable ? "NULL" : "NOT NULL")}.");
            }
        }

        // Only the supply direction exists for the version flag: a rowversion column
        // states the claim positively, but the schema cannot deny it - a version the
        // framework manages itself looks like any other numeric column - so a flag the
        // source stated over a non-rowversion column is no conflict (decision 030).
        if (demand.Contains(MappingFactCategory.VersionColumn) && column.IsRowVersion && !pm.IsVersion)
        {
            pm.IsVersion = true;
            ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.VersionColumn,
                $"The rowversion column '{column.Name}' of '{image.QualifiedName}' carries the row version; the version flag supplied by the database catalog.");
        }
    }

    private static void CompletePrimaryKey(AbstractEntityBuilder builder, EntityMap em, TableImage image)
    {
        // A stated "no key" is not an empty fact to fill (decision 063): the source
        // answered the key question in the negative, so the catalog's key is a
        // disagreement, not a supply. It is a conflict record rather than a refusal -
        // a keyless entity over a table that does have a key is a legitimate mapping.
        if (em.HasNoKey)
        {
            ReportConflict(builder, em, null, MappingFactCategory.PrimaryKey,
                $"The source states the entity has no key; the catalog states the primary key "
                + $"({string.Join(", ", image.PrimaryKeyColumns)}) of '{image.QualifiedName}'.");
            return;
        }

        if (em.PrimaryKey is null)
        {
            var parts = new List<(string PropertyName, int Order, PrimaryKeyStrategy Strategy)>();

            foreach (var (columnName, index) in image.PrimaryKeyColumns.Select((c, i) => (c, i)))
            {
                var pm = FindPropertyMapForColumn(em, columnName);

                if (pm is null)
                {
                    // A key part needs a property to hang on; inventing one would put a
                    // member into the class that the source never declared.
                    builder.Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Incompleteness,
                        Framework = builder.Descriptor.Framework,
                        Entity = em.Entity.Name,
                        Category = MappingFactCategory.PrimaryKey,
                        Reason = $"The catalog states the primary key ({string.Join(", ", image.PrimaryKeyColumns)}) of '{image.QualifiedName}', "
                            + $"but no property matches the key column '{columnName}'; the key is not supplied.",
                    });
                    return;
                }

                var column = image.FindColumn(columnName);
                parts.Add((pm.Property.Name, index + 1, StrategyFor(column)));

                if (column is { IsIdentity: false, HasDefault: true })
                {
                    ReportDefaultBackedKeyColumn(builder, em, pm.Property.Name, columnName, image);
                }
            }

            builder.EntityMap = em;
            builder.AddPrimaryKey(parts);
            ReportSupplied(builder, em, null, MappingFactCategory.PrimaryKey,
                $"Primary key ({string.Join(", ", image.PrimaryKeyColumns)}) supplied by the database catalog from '{image.QualifiedName}'.");
            return;
        }

        var sourceColumns = em.PrimaryKey.Parts
            .Select(p => p.PropertyMap.ColumnName ?? p.PropertyMap.Property.Name)
            .ToList();

        if (!sourceColumns.SequenceEqual(image.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase))
        {
            ReportConflict(builder, em, null, MappingFactCategory.PrimaryKey,
                $"The source states the primary key ({string.Join(", ", sourceColumns)}), "
                + $"the catalog states ({string.Join(", ", image.PrimaryKeyColumns)}) for '{image.QualifiedName}'.");
            return;
        }

        CompleteKeyStrategies(builder, em, image);
    }

    /// <summary>
    /// The catalog knows two strategy facts, one from each side (decision 064): an
    /// identity column is store-generated, and a column that is neither identity nor
    /// backed by a default must receive its value with the INSERT - which is what
    /// Assigned names. A part the source left unspecified gets whichever the column
    /// states; a non-identity column that carries a default stays unspecified, because
    /// a boolean flag cannot name the mechanism behind the default.
    /// </summary>
    private static void CompleteKeyStrategies(AbstractEntityBuilder builder, EntityMap em, TableImage image)
    {
        var key = em.PrimaryKey!;
        var rebuilt = new List<PrimaryKeyPart>();
        var changed = false;

        foreach (var part in key.Parts)
        {
            var columnName = part.PropertyMap.ColumnName ?? part.PropertyMap.Property.Name;
            var column = image.FindColumn(columnName);
            var isIdentity = column?.IsIdentity == true;

            if (part.Strategy == PrimaryKeyStrategy.Unspecified && isIdentity)
            {
                rebuilt.Add(RestrategizedPart(part, PrimaryKeyStrategy.Identity));
                changed = true;
                ReportSupplied(builder, em, part.PropertyMap.Property.Name, MappingFactCategory.PrimaryKeyStrategy,
                    $"Identity generation supplied by the database catalog: '{columnName}' is an IDENTITY column of '{image.QualifiedName}'.");
                continue;
            }

            if (part.Strategy == PrimaryKeyStrategy.Unspecified && column is { IsIdentity: false, HasDefault: false })
            {
                rebuilt.Add(RestrategizedPart(part, PrimaryKeyStrategy.Assigned));
                changed = true;
                ReportSupplied(builder, em, part.PropertyMap.Property.Name, MappingFactCategory.PrimaryKeyStrategy,
                    $"Assigned generation supplied by the database catalog: '{columnName}' of '{image.QualifiedName}' "
                    + "is neither an IDENTITY column nor backed by a default, so the value must arrive with the INSERT (decision 064).");
                continue;
            }

            if (part.Strategy == PrimaryKeyStrategy.Unspecified && column is { IsIdentity: false, HasDefault: true })
            {
                ReportDefaultBackedKeyColumn(builder, em, part.PropertyMap.Property.Name, columnName, image);
                rebuilt.Add(part);
                continue;
            }

            // Auto ("the store generates the value") is what an identity column looks
            // like from the source's side, so the two do not contradict each other.
            var claimsGenerated = part.Strategy is PrimaryKeyStrategy.Identity or PrimaryKeyStrategy.Auto;

            if ((claimsGenerated && !isIdentity) || (part.Strategy is PrimaryKeyStrategy.Assigned && isIdentity))
            {
                ReportConflict(builder, em, part.PropertyMap.Property.Name, MappingFactCategory.PrimaryKeyStrategy,
                    $"The source states the strategy {part.Strategy}, the catalog column '{columnName}' "
                    + $"{(isIdentity ? "is" : "is not")} an IDENTITY column of '{image.QualifiedName}'.");
            }

            rebuilt.Add(part);
        }

        if (changed)
        {
            em.PrimaryKey = new PrimaryKey { Parts = rebuilt, SourceKeyClass = key.SourceKeyClass };
        }
    }

    private static PrimaryKeyPart RestrategizedPart(PrimaryKeyPart part, PrimaryKeyStrategy strategy)
        => new()
        {
            PropertyMap = part.PropertyMap,
            Order = part.Order,
            Strategy = strategy,
            SourceStrategyName = part.SourceStrategyName,
            StrategyParameters = part.StrategyParameters,
            SourceStrategyParameters = part.SourceStrategyParameters,
        };

    private static PrimaryKeyStrategy StrategyFor(ColumnImage? column)
        => column switch
        {
            { IsIdentity: true } => PrimaryKeyStrategy.Identity,
            { HasDefault: false } => PrimaryKeyStrategy.Assigned,
            _ => PrimaryKeyStrategy.Unspecified,
        };

    /// <summary>
    /// A non-identity key column backed by a default constraint (decision 064): the store
    /// can fill it, but the boolean flag cannot name the mechanism, so the strategy stays
    /// unspecified and the state is reported instead of guessed.
    /// </summary>
    private static void ReportDefaultBackedKeyColumn(
        AbstractEntityBuilder builder, EntityMap em, string property, string columnName, TableImage image)
        => builder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Incompleteness,
            Framework = builder.Descriptor.Framework,
            Entity = em.Entity.Name,
            Property = property,
            Category = MappingFactCategory.PrimaryKeyStrategy,
            Reason = $"The key column '{columnName}' of '{image.QualifiedName}' is filled by a default constraint; "
                + "the catalog cannot name the mechanism, so the generation strategy stays unspecified (decision 064).",
        });

    private static void CompleteForeignKeys(
        AbstractEntityBuilder builder, EntityMap em, TableImage image, Dictionary<(string Schema, string Table), EntityMap> entityByTable)
    {
        foreach (var fk in image.ForeignKeys)
        {
            if (!entityByTable.TryGetValue((fk.ReferencedSchema.ToLowerInvariant(), fk.ReferencedTable.ToLowerInvariant()), out var target))
            {
                // A reference outside the conversion: no entity to point a relation at,
                // so nothing can be generated from it. The existing resolution phase
                // already reports the mirror case of a navigation without a target.
                continue;
            }

            // The pairing follows the referenced key's order (decision 012). The key
            // stands already - entity facts are completed before foreign keys.
            var orderedColumns = OrderByReferencedKey(fk, target);

            if (orderedColumns is null)
            {
                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = builder.Descriptor.Framework,
                    Entity = em.Entity.Name,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"The catalog states the foreign key {fk.Name} towards '{target.Entity.Name}', but its columns "
                        + "do not pair with that entity's primary key; the relation is not supplied.",
                });
                continue;
            }

            var existing = em.Relations
                .Where(r => r.Role == RelationRole.Owning
                    && r.Cardinality is Cardinality.OneToOne or Cardinality.ManyToOne
                    && SimpleEntityName(r.TargetEntity) == target.Entity.Name)
                .ToList();

            if (existing.Count > 0)
            {
                CompleteExistingRelation(builder, em, existing, orderedColumns, target, fk);
                continue;
            }

            SynthesizeRelation(builder, em, target, orderedColumns, fk);
        }
    }

    /// <summary>
    /// The foreign key's parent columns ordered by the referenced entity's key, or null
    /// when they do not pair with it - a key mismatch, or a constraint referencing a
    /// unique index rather than the primary key.
    /// </summary>
    private static List<string>? OrderByReferencedKey(ForeignKeyImage fk, EntityMap target)
    {
        var key = target.PrimaryKey;

        if (key is null || key.Parts.Count != fk.Columns.Count)
        {
            return null;
        }

        var ordered = new List<string>(fk.Columns.Count);

        foreach (var part in key.Parts)
        {
            var partColumn = part.PropertyMap.ColumnName ?? part.PropertyMap.Property.Name;
            var pair = fk.Columns.FirstOrDefault(c =>
                string.Equals(c.ReferencedColumn, partColumn, StringComparison.OrdinalIgnoreCase));

            if (pair is null)
            {
                return null;
            }

            ordered.Add(pair.Column);
        }

        return ordered;
    }

    private static void CompleteExistingRelation(
        AbstractEntityBuilder builder, EntityMap em, IReadOnlyList<Relation> candidates,
        List<string> orderedColumns, EntityMap target, ForeignKeyImage fk)
    {
        // A relation whose columns - resolved pairs or the source's stated columns -
        // already agree with the catalog needs nothing.
        foreach (var relation in candidates)
        {
            var stated = relation.ColumnPairs.Count > 0
                ? relation.ColumnPairs.Select(p => p.Source.ColumnName ?? p.Source.Property.Name).ToList()
                : builder.StatedForeignKeyColumns(relation)?.ToList();

            if (stated is not null && stated.SequenceEqual(orderedColumns, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var open = candidates.FirstOrDefault(r =>
            r.ColumnPairs.Count == 0 && builder.StatedForeignKeyColumns(r) is null);

        if (open is not null)
        {
            var pairs = new List<ColumnPair>(orderedColumns.Count);

            for (var i = 0; i < orderedColumns.Count; i++)
            {
                pairs.Add(new ColumnPair
                {
                    Source = FindPropertyMapForColumn(em, orderedColumns[i])
                        ?? new PropertyMap
                        {
                            Property = new Property { Name = orderedColumns[i] },
                            ColumnName = orderedColumns[i],
                        },
                    Target = target.PrimaryKey!.Parts[i].PropertyMap,
                });
            }

            open.ColumnPairs = pairs;
            ReportSupplied(builder, em, open.SourceNavigationProperty, MappingFactCategory.ForeignKeyColumns,
                $"Foreign key columns ({string.Join(", ", orderedColumns)}) towards '{target.Entity.Name}' supplied by the database catalog ({fk.Name}).");
            return;
        }

        // Every candidate states columns and none of them matches the catalog.
        ReportConflict(builder, em, candidates[0].SourceNavigationProperty, MappingFactCategory.ForeignKeyColumns,
            $"The source states foreign key columns towards '{target.Entity.Name}' that do not match "
            + $"the catalog's {fk.Name} ({string.Join(", ", orderedColumns)}).");
    }

    private static void SynthesizeRelation(
        AbstractEntityBuilder builder, EntityMap em, EntityMap target, List<string> orderedColumns, ForeignKeyImage fk)
    {
        // A relation needs a navigation property to be emitted; without one, the foreign
        // key is a fact of the table the entity has no shape for.
        var navigation = em.Entity.Properties.FirstOrDefault(p =>
            (p.Type is { Category: LangTypeCategory.Reference } r && r.TargetEntity == target.Entity.Name)
            || (p.Type is { Category: LangTypeCategory.Unknown } u && u.SourceName == target.Entity.Name));

        if (navigation is null)
        {
            builder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = builder.Descriptor.Framework,
                Entity = em.Entity.Name,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"The catalog states the foreign key {fk.Name} towards '{target.Entity.Name}', but the entity "
                    + "has no navigation property of that type; the relation is not generated.",
            });
            return;
        }

        // A foreign key covering the whole primary key is the shared-key one-to-one.
        var keyColumns = em.PrimaryKey?.Parts
            .Select(p => p.PropertyMap.ColumnName ?? p.PropertyMap.Property.Name)
            .ToList();
        var cardinality = keyColumns is not null
            && keyColumns.Count == orderedColumns.Count
            && keyColumns.All(k => orderedColumns.Contains(k, StringComparer.OrdinalIgnoreCase))
                ? Cardinality.OneToOne
                : Cardinality.ManyToOne;

        builder.EntityMap = em;
        builder.AddForeignKey(cardinality, navigation.Name, target.Entity.Name, RelationRole.Owning, orderedColumns);
        ReportSupplied(builder, em, navigation.Name, MappingFactCategory.ForeignKeyColumns,
            $"Foreign key ({string.Join(", ", orderedColumns)}) towards '{target.Entity.Name}' supplied by the database catalog ({fk.Name}).");
    }

    /// <summary>
    /// The inverse side of a collection: its key columns live in the child's table
    /// (decision 012), so they come from the foreign keys of every other entity of the
    /// conversion pointing back at this one. An existing inverse one-to-many gets its
    /// column pairs; a collection navigation no relation claims gets the relation
    /// synthesized. No member is invented - the collection navigation must exist in the
    /// source, only the relation and its pairs are supplied.
    /// </summary>
    private static void CompleteInverseCollections(
        AbstractEntityBuilder builder,
        Dictionary<EntityMap, TableImage> imageByEntity,
        Dictionary<(string Schema, string Table), EntityMap> entityByTable,
        HashSet<(EntityMap, EntityMap)> ambiguousPairs)
    {
        foreach (var (child, image) in imageByEntity)
        {
            foreach (var fk in image.ForeignKeys)
            {
                if (!entityByTable.TryGetValue((fk.ReferencedSchema.ToLowerInvariant(), fk.ReferencedTable.ToLowerInvariant()), out var parent)
                    || ReferenceEquals(parent, child))
                {
                    continue;
                }

                var orderedColumns = OrderByReferencedKey(fk, parent);

                if (orderedColumns is null)
                {
                    continue; // the owning pass over the child's image reported the pairing failure already
                }

                // A foreign key covering the child's whole primary key is the shared-key
                // one-to-one; its inverse side is a reference, not a collection (decision 012).
                if (CoversWholeKey(image, fk))
                {
                    continue;
                }

                var existing = parent.Relations
                    .Where(r => r.Role == RelationRole.Inverse
                        && r.Cardinality == Cardinality.OneToMany
                        && SimpleEntityName(r.TargetEntity) == child.Entity.Name)
                    .ToList();

                if (existing.Count > 0)
                {
                    CompleteExistingInverseRelation(builder, parent, child, existing, orderedColumns, fk);
                    continue;
                }

                // A relation the source stated resolves the ambiguity itself; deriving one
                // is a guess, so between an ambiguous pair nothing is synthesized. The
                // many-to-many detection reported the state.
                if (!ambiguousPairs.Contains((parent, child)))
                {
                    SynthesizeInverseRelation(builder, parent, child, orderedColumns, fk);
                }
            }
        }
    }

    private static bool CoversWholeKey(TableImage image, ForeignKeyImage fk)
        => image.PrimaryKeyColumns.Count == fk.Columns.Count
            && image.PrimaryKeyColumns.All(key =>
                fk.Columns.Any(c => string.Equals(c.Column, key, StringComparison.OrdinalIgnoreCase)));

    private static void CompleteExistingInverseRelation(
        AbstractEntityBuilder builder, EntityMap parent, EntityMap child,
        IReadOnlyList<Relation> candidates, List<string> orderedColumns, ForeignKeyImage fk)
    {
        // A relation whose columns - resolved pairs or the source's stated columns -
        // already agree with the catalog needs nothing.
        foreach (var relation in candidates)
        {
            var stated = relation.ColumnPairs.Count > 0
                ? relation.ColumnPairs.Select(p => p.Source.ColumnName ?? p.Source.Property.Name).ToList()
                : builder.StatedForeignKeyColumns(relation)?.ToList();

            if (stated is not null && stated.SequenceEqual(orderedColumns, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
        }

        var open = candidates.FirstOrDefault(r =>
            r.ColumnPairs.Count == 0 && builder.StatedForeignKeyColumns(r) is null);

        if (open is not null)
        {
            var pairs = new List<ColumnPair>(orderedColumns.Count);

            for (var i = 0; i < orderedColumns.Count; i++)
            {
                pairs.Add(new ColumnPair
                {
                    // The key columns belong to the child's table (decision 012), so the
                    // source side of each pair is the child's property map.
                    Source = FindPropertyMapForColumn(child, orderedColumns[i])
                        ?? new PropertyMap
                        {
                            Property = new Property { Name = orderedColumns[i] },
                            ColumnName = orderedColumns[i],
                        },
                    Target = parent.PrimaryKey!.Parts[i].PropertyMap,
                });
            }

            open.ColumnPairs = pairs;
            ReportSupplied(builder, parent, open.SourceNavigationProperty, MappingFactCategory.ForeignKeyColumns,
                $"Key columns ({string.Join(", ", orderedColumns)}) of the collection towards '{child.Entity.Name}' "
                + $"supplied by the database catalog ({fk.Name}).");
            return;
        }

        // Every candidate states columns and none of them matches the catalog.
        ReportConflict(builder, parent, candidates[0].SourceNavigationProperty, MappingFactCategory.ForeignKeyColumns,
            $"The source states key columns for the collection towards '{child.Entity.Name}' that do not match "
            + $"the catalog's {fk.Name} ({string.Join(", ", orderedColumns)}).");
    }

    private static void SynthesizeInverseRelation(
        AbstractEntityBuilder builder, EntityMap parent, EntityMap child, List<string> orderedColumns, ForeignKeyImage fk)
    {
        var navigation = FindCollectionNavigation(parent, child.Entity.Name);

        if (navigation is null)
        {
            // A unidirectional relation seen from its far side - the owning side carries
            // it, so a parent without a collection is a fact, not a gap.
            return;
        }

        // The stated columns pair with the parent's key in the resolution phase, like any
        // inverse relation a parser registers.
        builder.EntityMap = parent;
        builder.AddForeignKey(Cardinality.OneToMany, navigation.Name, child.Entity.Name, RelationRole.Inverse, orderedColumns);
        ReportSupplied(builder, parent, navigation.Name, MappingFactCategory.ForeignKeyColumns,
            $"Inverse one-to-many towards '{child.Entity.Name}' over its foreign key ({string.Join(", ", orderedColumns)}) "
            + "supplied by the database catalog "
            + $"({fk.Name}); the source declares the collection, the schema the relation.");
    }

    /// <summary>
    /// The third-level convention of decision 015: a property known only to the mapping
    /// gets its language type inferred from its database type, with the origin reported.
    /// The property is made declarable at the same time - a mapping never states access
    /// or accessors, and a C# property cannot be emitted without them.
    /// </summary>
    private static void InferLanguageTypes(AbstractEntityBuilder builder)
    {
        foreach (var em in builder.EntityMaps)
        {
            foreach (var pm in em.PropertyMaps.Where(pm => pm.Property.Type is null && pm.Type is not null))
            {
                var scalar = LanguageTypeInference.FromDatabaseType(pm.Type!.Value);

                if (scalar is null)
                {
                    continue;
                }

                pm.Property.Type = LangType.Scalar(scalar.Value, pm.IsNullable ?? false);
                pm.Property.AccessModifier ??= AccessModifier.Public;

                if (!pm.Property.HasGetter && !pm.Property.HasSetter)
                {
                    pm.Property.HasGetter = true;
                    pm.Property.HasSetter = true;
                }

                builder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Convention,
                    Framework = builder.Descriptor.Framework,
                    Entity = em.Entity.Name,
                    Property = pm.Property.Name,
                    Reason = $"The property has no language type; {scalar} was inferred from the database type "
                        + $"{pm.Type} - a third-level convention (decision 015).",
                });
            }
        }
    }

    private static bool IsNavigation(AbstractEntityBuilder builder, EntityMap em, PropertyMap pm)
    {
        if (pm.Property.Type is { Category: LangTypeCategory.Reference or LangTypeCategory.Collection })
        {
            return true;
        }

        // An unknown type naming an entity of this conversion is a navigation-to-be: the
        // resolution phase upgrades it to a reference before generation.
        if (pm.Property.Type is { Category: LangTypeCategory.Unknown } unknown
            && builder.EntityMaps.Any(other => other.Entity.Name == unknown.SourceName))
        {
            return true;
        }

        return em.Relations.Any(r => r.SourceNavigationProperty == pm.Property.Name);
    }

    private static PropertyMap? FindPropertyMapForColumn(EntityMap em, string column)
        => em.PropertyMaps.FirstOrDefault(pm =>
                string.Equals(pm.ColumnName, column, StringComparison.OrdinalIgnoreCase))
            ?? em.PropertyMaps.FirstOrDefault(pm =>
                pm.ColumnName is null && string.Equals(pm.Property.Name, column, StringComparison.OrdinalIgnoreCase));

    private static string SimpleEntityName(string name)
    {
        var typeName = name.Split(',')[0].Trim();
        var lastDot = typeName.LastIndexOf('.');

        return lastDot < 0 ? typeName : typeName[(lastDot + 1)..];
    }

    private static void ReportSupplied(AbstractEntityBuilder builder, EntityMap em, string? property, MappingFactCategory category, string reason)
        => builder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Supplied,
            Framework = builder.Descriptor.Framework,
            Entity = em.Entity.Name,
            Property = property,
            Category = category,
            Reason = reason,
        });

    private static void ReportConflict(AbstractEntityBuilder builder, EntityMap em, string? property, MappingFactCategory category, string reason)
        => builder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Conflict,
            Framework = builder.Descriptor.Framework,
            Entity = em.Entity.Name,
            Property = property,
            Category = category,
            Reason = reason + " The source outranks the catalog (rule E9, decision 015), so the source value is kept.",
        });
}
