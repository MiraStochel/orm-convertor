using System.Diagnostics;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
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
    /// Runs the phase over the builder's accumulated entities. Returns the time the
    /// catalog read and write took, so it can be reported separately from translation
    /// time (S3); null when the phase had nothing to do - an empty demand means zero
    /// queries (decision 015).
    /// </summary>
    public static TimeSpan? Complete(AbstractEntityBuilder builder, ICatalogReader? reader)
    {
        var demand = Enum.GetValues<MappingFactCategory>()
            .Where(category => builder.Descriptor.SupportOf(category) != FactSupport.NotExpressible)
            .ToHashSet();

        TimeSpan? elapsed = null;

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
                }
                catch (Exception ex)
                {
                    // A configured but unreachable catalog is infrastructure, not input;
                    // the translation continues on conventions and says why (decision 015).
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

        return elapsed;
    }

    private static void Apply(AbstractEntityBuilder builder, ICatalogReader reader, HashSet<MappingFactCategory> demand)
    {
        var entities = builder.EntityMaps.Where(em => em.Entity.Name.Length > 0).ToList();

        var requests = entities
            .Select(em => new TableRequest(
                em.Entity.Name,
                em.Schema,
                em.Table is not null ? [em.Table] : TableNameCandidates.For(em.Entity.Name)))
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
                pm.Type = column.Type;
                ReportSupplied(builder, em, pm.Property.Name, MappingFactCategory.DatabaseType,
                    $"Database type {column.Type} supplied by the database catalog from '{image.QualifiedName}'.");
            }
            else if (pm.Type != column.Type)
            {
                ReportConflict(builder, em, pm.Property.Name, MappingFactCategory.DatabaseType,
                    $"The source maps the property as {pm.Type}, the catalog column '{column.Name}' is {column.Type}.");
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
    }

    private static void CompletePrimaryKey(AbstractEntityBuilder builder, EntityMap em, TableImage image)
    {
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

                parts.Add((pm.Property.Name, index + 1, StrategyFor(image, columnName)));
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
    /// The one positive strategy fact the catalog knows is an identity column. A part the
    /// source left unspecified gets it; a non-identity column stays unspecified, because
    /// "the database does not generate it" does not say who does.
    /// </summary>
    private static void CompleteKeyStrategies(AbstractEntityBuilder builder, EntityMap em, TableImage image)
    {
        var key = em.PrimaryKey!;
        var rebuilt = new List<PrimaryKeyPart>();
        var changed = false;

        foreach (var part in key.Parts)
        {
            var columnName = part.PropertyMap.ColumnName ?? part.PropertyMap.Property.Name;
            var isIdentity = image.FindColumn(columnName)?.IsIdentity == true;

            if (part.Strategy == PrimaryKeyStrategy.Unspecified && isIdentity)
            {
                rebuilt.Add(new PrimaryKeyPart
                {
                    PropertyMap = part.PropertyMap,
                    Order = part.Order,
                    Strategy = PrimaryKeyStrategy.Identity,
                    SourceStrategyName = part.SourceStrategyName,
                    StrategyParameters = part.StrategyParameters,
                });
                changed = true;
                ReportSupplied(builder, em, part.PropertyMap.Property.Name, MappingFactCategory.PrimaryKeyStrategy,
                    $"Identity generation supplied by the database catalog: '{columnName}' is an IDENTITY column of '{image.QualifiedName}'.");
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

    private static PrimaryKeyStrategy StrategyFor(TableImage image, string columnName)
        => image.FindColumn(columnName)?.IsIdentity == true
            ? PrimaryKeyStrategy.Identity
            : PrimaryKeyStrategy.Unspecified;

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
