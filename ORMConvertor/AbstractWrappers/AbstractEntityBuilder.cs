using System.Text;
using Common.Convertors;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;

namespace AbstractWrappers;

/// <summary>
/// Abstract base class for building entity representations and their mappings.
/// Provides methods to configure table, schema, namespace, class header, properties, primary keys, and foreign keys.
/// </summary>
public abstract class AbstractEntityBuilder
{
    /// <summary>
    /// Collection of built entity maps.
    /// </summary>
    public List<EntityMap> EntityMaps { get; } = new();

    /// <summary>
    /// The currently active entity map (for parser convenience).
    /// </summary>
    public EntityMap EntityMap
    {
        get
        {
            if (currentEntityMap is null)
            {
                BeginEntity();
            }
            return currentEntityMap!;
        }
        set
        {
            currentEntityMap = value;
            if (!EntityMaps.Contains(value))
            {
                EntityMaps.Add(value);
            }
        }
    }

    private EntityMap? currentEntityMap;

    /// <summary>
    /// Starts a new entity definition and sets it as current.
    /// </summary>
    public void BeginEntity()
    {
        currentEntityMap = new EntityMap { Entity = new() };
        EntityMaps.Add(currentEntityMap);
    }

    /// <summary>
    /// Add a table name.
    /// </summary>
    /// <param name="tableName">Table name</param>
    public void AddTable(string tableName)
    {
        if (!string.IsNullOrEmpty(tableName))
        {
            EntityMap.Table = tableName;
        }
    }

    /// <summary>
    /// Add a schema name.
    /// </summary>
    /// <param name="schemaName">Schema name</param>
    public void AddSchema(string schemaName)
    {
        if (!string.IsNullOrEmpty(schemaName))
        {
            EntityMap.Schema = schemaName;
        }
    }

    /// <summary>
    /// Add a namespace to the entity.
    /// </summary>
    /// <param name="namespaceName">Namespace name</param>
    public void AddNamespace(string namespaceName)
    {
        EntityMap.Entity.Namespace = namespaceName;
    }

    /// <summary>
    /// Add class header information such as access modifier and class name.
    /// </summary>
    /// <param name="accessModifier">Access modifier (public, private, …)</param>
    /// <param name="className">Class name</param>
    public void AddClassHeader(string accessModifier, string className)
    {
        EntityMap.Entity.Name = className;
        EntityMap.Entity.AccessModifier = AccessModifierConvertor.FromString(accessModifier.Trim());
    }

    /// <summary>
    /// Define the (possibly composite) primary key of the entity.
    /// The whole key is defined by a single call; a repeated call replaces the previous key.
    /// </summary>
    /// <param name="parts">Key parts: property name, explicit 1-based order, and per-part generation strategy.</param>
    /// <param name="sourceKeyClass">Optional record of a key class used by the source; null when the source declared the key parts directly on the entity.</param>
    public void AddPrimaryKey(
        IReadOnlyList<(string PropertyName, int Order, PrimaryKeyStrategy Strategy)> parts,
        SourceKeyClass? sourceKeyClass = null)
    {
        if (parts == null || parts.Count == 0)
        {
            throw new ArgumentException("Primary key must have at least one part.", nameof(parts));
        }

        var keyParts = new List<PrimaryKeyPart>();

        foreach (var (propertyName, order, strategy) in parts)
        {
            var propertyMap = GetOrCreatePropertyMap(propertyName);

            keyParts.Add(new PrimaryKeyPart
            {
                PropertyMap = propertyMap,
                Order = order,
                Strategy = strategy,
            });
        }

        EntityMap.PrimaryKey = new PrimaryKey
        {
            Parts = keyParts,
            SourceKeyClass = sourceKeyClass,
        };
    }

    /// <summary>
    /// Convenience overload for a simple (single-property) primary key.
    /// </summary>
    /// <param name="strategy">Primary key strategy</param>
    /// <param name="propertyName">Property name to be used as primary key</param>
    public void AddPrimaryKey(PrimaryKeyStrategy strategy, string propertyName)
        => AddPrimaryKey([(propertyName, 1, strategy)]);

    /// <summary>
    /// Record what the source said about a key part's strategy beyond the vocabulary value:
    /// its own name for it and the generator parameters. Call after <see cref="AddPrimaryKey"/>,
    /// which defines the key as a whole - a repeated AddPrimaryKey call discards these details
    /// together with the key it replaces.
    /// </summary>
    /// <param name="propertyName">Name of a property that is already part of the key.</param>
    /// <param name="sourceStrategyName">The source's own name for the strategy, when the vocabulary lost it.</param>
    /// <param name="parameters">Generator parameters, e.g. sequence name or block size.</param>
    public void SetKeyStrategyDetails(
        string propertyName,
        string? sourceStrategyName = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        var key = EntityMap.PrimaryKey
            ?? throw new InvalidOperationException("Key strategy details can only be set once the key is defined.");

        var target = key.Parts.FirstOrDefault(p => p.PropertyMap.Property.Name == propertyName)
            ?? throw new ArgumentException($"Property '{propertyName}' is not part of the primary key.", nameof(propertyName));

        // Key parts are init-only, so the detail lands on a replacement part and the key
        // is rebuilt around it. Rebuilding also re-checks the invariants of PrimaryKey.
        var replacement = new PrimaryKeyPart
        {
            PropertyMap = target.PropertyMap,
            Order = target.Order,
            Strategy = target.Strategy,
            SourceStrategyName = sourceStrategyName ?? target.SourceStrategyName,
            StrategyParameters = parameters is null
                ? target.StrategyParameters
                : new Dictionary<string, string>(parameters),
        };

        EntityMap.PrimaryKey = new PrimaryKey
        {
            Parts = [.. key.Parts.Select(p => ReferenceEquals(p, target) ? replacement : p)],
            SourceKeyClass = key.SourceKeyClass,
        };
    }

    private PropertyMap GetOrCreatePropertyMap(string propertyName)
    {
        // Find the property in the entity's properties
        var property = EntityMap.Entity.Properties.FirstOrDefault(p => p.Name == propertyName);
        if (property == null)
        {
            // If not found, create and add it; a null type records that nobody stated it.
            property = new Property
            {
                Name = propertyName,
            };
            EntityMap.Entity.Properties.Add(property);
        }

        // Find or create the property map
        var propertyMap = EntityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == propertyName);
        if (propertyMap == null)
        {
            propertyMap = new PropertyMap
            {
                Property = property
            };
            EntityMap.PropertyMaps.Add(propertyMap);
        }

        return propertyMap;
    }

    /// <summary>
    /// Convenience method: registers a relation known from a navigation property.
    /// A typical parser case - the target's columns cannot be resolved from a single translation
    /// unit, so ColumnPairs stay empty here. The foreign key columns the source stated are kept
    /// aside and paired with the target's key by <see cref="ResolveEntityNames"/> before
    /// generation, once every entity of the conversion has been parsed; a target outside the
    /// conversion is the database catalog's case (decision 015).
    /// </summary>
    /// <param name="cardinality">Relationship cardinality</param>
    /// <param name="propertyName">Navigation property name on the source entity</param>
    /// <param name="target">Target entity name</param>
    /// <param name="role">Which side holds the foreign key; derived from the cardinality when omitted.</param>
    /// <param name="foreignKeyColumns">Columns (or properties) of the foreign key as the source
    /// stated them, in the source's order. They belong to whichever side holds the key: the
    /// source entity of an owning relation, the target entity of an inverse one - and to the
    /// junction table for a many-to-many, where they reference this entity's key.</param>
    /// <param name="junction">Many-to-many only: what the source stated about the junction
    /// table, waiting for the synthesis of the junction entity (decision 005).</param>
    public void AddForeignKey(
        Cardinality cardinality,
        string propertyName,
        string target,
        RelationRole? role = null,
        IReadOnlyList<string>? foreignKeyColumns = null,
        JunctionFacts? junction = null)
    {
        var propertyMap = GetOrCreatePropertyMap(propertyName); // the navigation property must exist in the model

        // The mapping or annotation just claimed the property points at an entity, which is
        // exactly when a Reference arises (decision 014). The C# side has already read the
        // same property as Unknown - or as a collection of one - so the claim upgrades the
        // type in place, keeping the nullability and the collection kind the source declared.
        propertyMap.Property.Type = ReferenceTypeFor(propertyMap.Property.Type, cardinality, target);

        var relation = new Relation
        {
            Cardinality = cardinality,
            // A 1:1 sits on either side, so the caller may say which one this is - NHibernate marks
            // it with constrained or property-ref, EF Core by where the key properties live.
            Role = role ?? (cardinality is Cardinality.OneToOne or Cardinality.ManyToOne
                ? RelationRole.Owning
                : RelationRole.Inverse),
            SourceEntity = EntityMap.Entity.Name,
            TargetEntity = target,
            SourceNavigationProperty = propertyName,
        };

        AddRelation(relation);

        if (foreignKeyColumns is { Count: > 0 })
        {
            pendingForeignKeyColumns[relation] = foreignKeyColumns;
        }

        if (cardinality == Cardinality.ManyToMany && junction is not null)
        {
            pendingJunctionFacts[relation] = junction;
        }
    }

    /// <summary>
    /// Registers a fully specified relation (including ColumnPairs, junction scenarios, …).
    /// </summary>
    public void AddRelation(Relation relation)
    {
        EntityMap.Relations.Add(relation);
    }

    /// <summary>
    /// Language type of a navigation property once a relation names its target. The entity is
    /// referenced by its simple name (decision 001), so an assembly-qualified or namespaced
    /// name from an NHibernate class attribute is trimmed to the class itself.
    /// </summary>
    private static LangType ReferenceTypeFor(LangType? current, Cardinality cardinality, string target)
    {
        var simpleName = SimpleEntityName(target);

        if (cardinality is Cardinality.OneToOne or Cardinality.ManyToOne)
        {
            return LangType.Reference(simpleName, current?.IsNullable ?? false);
        }

        // The "many" side is a collection of references; what the C# declaration said about
        // the kind and the element's nullability survives the upgrade.
        var element = LangType.Reference(simpleName, current?.ElementType?.IsNullable ?? false);

        return LangType.Collection(
            element,
            current?.CollectionKind ?? CollectionKind.Unspecified,
            current?.IsNullable ?? false);
    }

    /// <summary>
    /// Foreign key columns stated by the source, keyed by the relation they belong to, waiting
    /// for the target entity's key to pair with. Builder state, not part of the model: once
    /// paired they live in <see cref="Relation.ColumnPairs"/>, and an entry whose target never
    /// arrives simply stays unconsumed.
    /// </summary>
    private readonly Dictionary<Relation, IReadOnlyList<string>> pendingForeignKeyColumns = [];

    /// <summary>
    /// Junction table facts of many-to-many relations, waiting for
    /// <see cref="SynthesizeJunctionEntities"/>. Same lifecycle as the pending columns above.
    /// </summary>
    private readonly Dictionary<Relation, JunctionFacts> pendingJunctionFacts = [];

    /// <summary>
    /// Foreign key columns the source stated for a relation whose pairs are not resolved
    /// yet, or null when it stated none. Read by the catalog completion phase (decision
    /// 015) to compare the source's claim with the catalog without consuming it.
    /// </summary>
    public IReadOnlyList<string>? StatedForeignKeyColumns(Relation relation)
        => pendingForeignKeyColumns.TryGetValue(relation, out var columns) ? columns : null;

    /// <summary>
    /// The resolution phase promised by decision 001: runs once per <see cref="Build"/>, after
    /// all entities of the conversion have been parsed and before anything is generated.
    /// Resolves relation targets by name against <see cref="EntityMaps"/>, fills
    /// <see cref="Relation.ColumnPairs"/> where the source stated the foreign key columns, and
    /// completes the second half of the Reference rule of decision 014 - an unknown type name
    /// matching an entity of the same conversion becomes a reference. A name that resolves to
    /// nothing is left as it is and recorded as incompleteness (decision 010): it may be a
    /// typo, or a reference outside the conversion that the database catalog would resolve
    /// (decision 015), and only the catalog can tell the two apart.
    /// </summary>
    private void ResolveEntityNames()
    {
        foreach (var entityMap in EntityMaps)
        {
            foreach (var property in entityMap.Entity.Properties)
            {
                property.Type = ResolveUnknownType(property.Type);
            }

            foreach (var relation in entityMap.Relations)
            {
                var target = FindEntityMap(relation.TargetEntity);

                if (target is null)
                {
                    Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Incompleteness,
                        Framework = Descriptor.Framework,
                        Entity = entityMap.Entity.Name,
                        Property = relation.SourceNavigationProperty,
                        Reason = $"Target entity '{relation.TargetEntity}' is not part of the conversion, so nothing can be resolved against it - a reference outside the conversion is the database catalog's case (decision 015).",
                    });
                }
                else if (relation.Cardinality == Cardinality.ManyToMany && !HasJunctionEntityFor(relation))
                {
                    Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Incompleteness,
                        Framework = Descriptor.Framework,
                        Entity = entityMap.Entity.Name,
                        Property = relation.SourceNavigationProperty,
                        Reason = $"The many-to-many relation to '{relation.TargetEntity}' has no junction entity in the conversion; decision 005 represents N:M as an explicit junction entity, so its mapping cannot be generated.",
                    });
                }

                ResolveColumnPairs(entityMap, relation, target);
            }
        }
    }

    /// <summary>
    /// The generating half of decision 005: a many-to-many between two entities of the
    /// conversion becomes an explicit junction entity with two owning many-to-one
    /// relations, and the collections of both sides retarget to it as inverse
    /// one-to-many. Everything the entity is made of is a stated fact of the source -
    /// the junction table, its key columns towards this side and the far side's columns -
    /// except the class name, which derives from the table name and is reported as a
    /// convention. Where a fact is missing the synthesis declines and the relation stays
    /// many-to-many; the resolution phase then reports the missing junction entity as it
    /// always has.
    /// </summary>
    private void SynthesizeJunctionEntities()
    {
        foreach (var entityMap in EntityMaps.ToList())
        {
            foreach (var relation in entityMap.Relations.Where(r => r.Cardinality == Cardinality.ManyToMany).ToList())
            {
                TrySynthesizeJunction(entityMap, relation);
            }
        }
    }

    private void TrySynthesizeJunction(EntityMap entityMap, Relation relation)
    {
        if (HasJunctionEntityFor(relation))
        {
            return;
        }

        var target = FindEntityMap(relation.TargetEntity);

        // A target outside the conversion or a self-referencing many-to-many is left as it
        // is; the resolution phase reports the state, so nothing is said twice here.
        if (target is null || ReferenceEquals(target, entityMap))
        {
            return;
        }

        var counterpart = target.Relations.FirstOrDefault(r =>
            r != relation
            && r.Cardinality == Cardinality.ManyToMany
            && FindEntityMap(r.TargetEntity) == entityMap);

        var facts = pendingJunctionFacts.GetValueOrDefault(relation);
        var counterpartFacts = counterpart is null ? null : pendingJunctionFacts.GetValueOrDefault(counterpart);

        // Both collections describe the same junction table from opposite ends, so either
        // supplies what the other left out: this side's <key> columns are the far side's
        // <many-to-many> columns and vice versa.
        var table = facts?.Table ?? counterpartFacts?.Table;
        var schema = facts?.Schema ?? counterpartFacts?.Schema;
        var sourceSideColumns = StatedForeignKeyColumns(relation) ?? counterpartFacts?.TargetColumns;
        var targetSideColumns = facts?.TargetColumns
            ?? (counterpart is null ? null : StatedForeignKeyColumns(counterpart));

        if (table is null || sourceSideColumns is null || targetSideColumns is null)
        {
            return;
        }

        if (entityMap.PrimaryKey is null || entityMap.PrimaryKey.Parts.Count != sourceSideColumns.Count
            || target.PrimaryKey is null || target.PrimaryKey.Parts.Count != targetSideColumns.Count)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = Descriptor.Framework,
                Entity = entityMap.Entity.Name,
                Property = relation.SourceNavigationProperty,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"The junction table '{table}' cannot become an entity: its stated columns do not pair "
                    + $"with the keys of '{entityMap.Entity.Name}' and '{target.Entity.Name}' (decision 005).",
            });
            return;
        }

        var junctionName = DeriveJunctionEntityName(table);
        var memberNames = sourceSideColumns.Concat(targetSideColumns)
            .Append(entityMap.Entity.Name)
            .Append(target.Entity.Name)
            .ToList();

        if (FindEntityMap(junctionName) is not null
            || memberNames.Distinct(StringComparer.Ordinal).Count() != memberNames.Count)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = Descriptor.Framework,
                Entity = entityMap.Entity.Name,
                Property = relation.SourceNavigationProperty,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"The junction entity '{junctionName}' for table '{table}' cannot be synthesized: "
                    + "its name or members would collide with what the conversion already holds (decision 005).",
            });
            return;
        }

        var junction = new EntityMap
        {
            Entity = new Entity
            {
                Name = junctionName,
                Namespace = entityMap.Entity.Namespace,
                AccessModifier = AccessModifier.Public,
            },
            Table = table,
            Schema = schema,
            IsJunctionTable = true,
        };
        EntityMap = junction;

        AddJunctionColumnProperties(junction, sourceSideColumns, entityMap.PrimaryKey);
        AddJunctionColumnProperties(junction, targetSideColumns, target.PrimaryKey);

        AddPrimaryKey([.. sourceSideColumns.Concat(targetSideColumns)
            .Select((column, index) => (column, index + 1, PrimaryKeyStrategy.Assigned))]);

        AddJunctionNavigation(junction, entityMap, sourceSideColumns);
        AddJunctionNavigation(junction, target, targetSideColumns);

        RetargetCollection(entityMap, relation, junctionName);
        if (counterpart is not null)
        {
            RetargetCollection(target, counterpart, junctionName);
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Convention,
            Framework = Descriptor.Framework,
            Entity = junctionName,
            Reason = $"The many-to-many between '{entityMap.Entity.Name}' and '{target.Entity.Name}' is generated "
                + $"as the explicit junction entity '{junctionName}' with two many-to-one relations, and both "
                + $"collections now hold it (decision 005). The class name derives from the table '{table}', "
                + "which is the tool's convention, not a fact of the source.",
        });
    }

    /// <summary>
    /// Class name of a synthesized junction entity: the table name, singularized by the
    /// same trailing-s heuristic the rest of the solution uses for the opposite direction.
    /// </summary>
    private static string DeriveJunctionEntityName(string table)
        => table.Length > 1 && (table.EndsWith('s') || table.EndsWith('S'))
            ? table[..^1]
            : table;

    /// <summary>
    /// One property per junction column, its facts copied from the key part it references:
    /// a foreign key column shares the referenced column's types, and being part of the
    /// junction's key it is not nullable.
    /// </summary>
    private void AddJunctionColumnProperties(EntityMap junction, IReadOnlyList<string> columns, PrimaryKey referencedKey)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var part = referencedKey.Parts[i];
            var property = new Property
            {
                Name = columns[i],
                Type = part.PropertyMap.Property.Type,
                AccessModifier = AccessModifier.Public,
                HasGetter = true,
                HasSetter = true,
            };

            junction.Entity.Properties.Add(property);
            junction.PropertyMaps.Add(new PropertyMap
            {
                Property = property,
                Type = part.PropertyMap.Type,
                Length = part.PropertyMap.Length,
                Precision = part.PropertyMap.Precision,
                Scale = part.PropertyMap.Scale,
                IsNullable = false,
            });
        }
    }

    /// <summary>
    /// The navigation the junction's many-to-one hangs on, named after the entity it points
    /// at. Declared here rather than left to <see cref="AddForeignKey"/>, because a property
    /// nobody wrote needs access and accessors to be declarable at all.
    /// </summary>
    private void AddJunctionNavigation(EntityMap junction, EntityMap target, IReadOnlyList<string> columns)
    {
        var property = new Property
        {
            Name = target.Entity.Name,
            Type = LangType.Reference(target.Entity.Name),
            AccessModifier = AccessModifier.Public,
            HasGetter = true,
            HasSetter = true,
        };

        junction.Entity.Properties.Add(property);
        junction.PropertyMaps.Add(new PropertyMap { Property = property });

        EntityMap = junction;
        AddForeignKey(Cardinality.ManyToOne, property.Name, target.Entity.Name, RelationRole.Owning, columns);
    }

    /// <summary>
    /// Turns a side's many-to-many collection into the inverse one-to-many towards the
    /// junction entity - the shape decision 005 gives the model. The element type of the
    /// collection follows; the stated key columns stay pending on the relation and pair
    /// with the junction's properties in the resolution phase.
    /// </summary>
    private void RetargetCollection(EntityMap entityMap, Relation relation, string junctionName)
    {
        relation.Cardinality = Cardinality.OneToMany;
        relation.Role = RelationRole.Inverse;
        relation.TargetEntity = junctionName;

        var propertyMap = entityMap.PropertyMaps.FirstOrDefault(pm =>
            pm.Property.Name == relation.SourceNavigationProperty);

        if (propertyMap?.Property.Type is { Category: LangTypeCategory.Collection } type)
        {
            propertyMap.Property.Type = LangType.Collection(
                LangType.Reference(junctionName, type.ElementType?.IsNullable ?? false),
                type.CollectionKind ?? CollectionKind.Unspecified,
                type.IsNullable);
        }

        pendingJunctionFacts.Remove(relation);
    }

    /// <summary>
    /// Whether a junction entity connecting both sides of an N:M relation takes part in
    /// this conversion (decision 005: many-to-many has no type of its own).
    /// </summary>
    private bool HasJunctionEntityFor(Relation relation)
    {
        var source = SimpleEntityName(relation.SourceEntity);
        var target = SimpleEntityName(relation.TargetEntity);

        return EntityMaps.Any(em => em.IsJunctionTable
            && em.Relations.Any(r => SimpleEntityName(r.TargetEntity) == source)
            && em.Relations.Any(r => SimpleEntityName(r.TargetEntity) == target));
    }

    /// <summary>
    /// An Unknown whose source name is exactly the name of an entity of this conversion becomes
    /// a Reference; the element of a collection likewise. Anything else stays untouched -
    /// a namespaced or otherwise decorated name is not the claim this rule reads.
    /// </summary>
    private LangType? ResolveUnknownType(LangType? type)
    {
        if (type is { Category: LangTypeCategory.Unknown }
            && FindEntityMap(type.SourceName!) is not null)
        {
            return LangType.Reference(type.SourceName!, type.IsNullable);
        }

        if (type is { Category: LangTypeCategory.Collection }
            && ResolveUnknownType(type.ElementType) is { Category: LangTypeCategory.Reference } element)
        {
            return LangType.Collection(element, type.CollectionKind!.Value, type.IsNullable);
        }

        return type;
    }

    /// <summary>
    /// Pairs the foreign key columns the source stated with the key they reference, once both
    /// entities are part of the same conversion. The key side follows the role: an owning
    /// relation holds the key and references the target's primary key, an inverse one is
    /// referenced through its own. N:M stays out - its key columns belong to a junction table,
    /// which is an entity of its own (decision 005). A column count that disagrees with the
    /// key is left unpaired and recorded (decision 010); pairs the source supplied already
    /// filled are checked against the order of the referenced key (decision 012).
    /// </summary>
    private void ResolveColumnPairs(EntityMap entityMap, Relation relation, EntityMap? target)
    {
        if (relation.Cardinality == Cardinality.ManyToMany || target is null)
        {
            return;
        }

        var (keyHolder, referencedKey) = relation.Role == RelationRole.Owning
            ? (entityMap, target.PrimaryKey)
            : (target, entityMap.PrimaryKey);

        if (relation.ColumnPairs.Count > 0)
        {
            CheckColumnPairOrder(entityMap, relation, referencedKey);
            return;
        }

        if (!pendingForeignKeyColumns.TryGetValue(relation, out var columns))
        {
            return;
        }

        if (referencedKey is null || referencedKey.Parts.Count != columns.Count)
        {
            // The builder must not pair by guesswork: a wrong count means either the source
            // or the key is incomplete, and quietly padding or trimming would hide which.
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = Descriptor.Framework,
                Entity = entityMap.Entity.Name,
                Property = relation.SourceNavigationProperty,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = referencedKey is null
                    ? $"The source states foreign key columns towards '{relation.TargetEntity}', but the referenced key does not exist; the columns stay unpaired."
                    : $"The source states {columns.Count} foreign key column(s) towards '{relation.TargetEntity}', but the referenced key has {referencedKey.Parts.Count} part(s); the columns stay unpaired.",
            });
            return;
        }

        var pairs = new List<ColumnPair>(columns.Count);

        for (var i = 0; i < columns.Count; i++)
        {
            pairs.Add(new ColumnPair
            {
                Source = ForeignKeyColumnMap(keyHolder, columns[i]),
                Target = referencedKey.Parts[i].PropertyMap,
            });
        }

        relation.ColumnPairs = pairs;
        pendingForeignKeyColumns.Remove(relation);
    }

    /// <summary>
    /// Pairs supplied ready-made are ordered and authoritative; the builder never reorders
    /// them, because that would cover up a defect of the producer. A sequence of targets
    /// that disagrees with the referenced key is a completeness error of the intermediate
    /// representation and is recorded as such (decision 012).
    /// </summary>
    private void CheckColumnPairOrder(EntityMap entityMap, Relation relation, PrimaryKey? referencedKey)
    {
        if (referencedKey is null || referencedKey.Parts.Count != relation.ColumnPairs.Count)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = Descriptor.Framework,
                Entity = entityMap.Entity.Name,
                Property = relation.SourceNavigationProperty,
                Category = MappingFactCategory.ForeignKeyColumns,
                Reason = $"The relation to '{relation.TargetEntity}' carries {relation.ColumnPairs.Count} column pair(s) but the referenced key has {referencedKey?.Parts.Count ?? 0} part(s); the pairs are emitted as stored.",
            });
            return;
        }

        for (var i = 0; i < referencedKey.Parts.Count; i++)
        {
            if (relation.ColumnPairs[i].Target.Property.Name != referencedKey.Parts[i].PropertyMap.Property.Name)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Incompleteness,
                    Framework = Descriptor.Framework,
                    Entity = entityMap.Entity.Name,
                    Property = relation.SourceNavigationProperty,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"The order of the column pairs of the relation to '{relation.TargetEntity}' does not match the order of the referenced key; the pairs are emitted as stored, not silently reordered (decision 012).",
                });
                return;
            }
        }
    }

    /// <summary>
    /// The property map behind one stated foreign key column: an existing one of the entity
    /// holding the key, found by column and then by property name (EF Core states properties,
    /// NHibernate columns). A column nobody declared a property for gets a detached map -
    /// a column is not a property until it has a language type, so it must not enter the
    /// entity's PropertyMaps, where builders would emit it as one.
    /// </summary>
    private static PropertyMap ForeignKeyColumnMap(EntityMap keyHolder, string column)
    {
        return keyHolder.PropertyMaps.FirstOrDefault(pm => pm.ColumnName == column)
            ?? keyHolder.PropertyMaps.FirstOrDefault(pm => pm.ColumnName is null && pm.Property.Name == column)
            ?? new PropertyMap
            {
                Property = new Property { Name = column },
                ColumnName = column,
            };
    }

    /// <summary>
    /// Finds an entity of this conversion by name; the match is on the simple class name,
    /// which is how relations reference entities (decision 001).
    /// </summary>
    private EntityMap? FindEntityMap(string entityName)
    {
        var simpleName = SimpleEntityName(entityName);

        return EntityMaps.FirstOrDefault(em => em.Entity.Name == simpleName);
    }

    /// <summary>
    /// Trims an assembly-qualified or namespaced name to the class itself.
    /// </summary>
    private static string SimpleEntityName(string name)
    {
        var typeName = name.Split(',')[0].Trim();
        var lastDot = typeName.LastIndexOf('.');

        return lastDot < 0 ? typeName : typeName[(lastDot + 1)..];
    }

    /// <summary>
    /// Add a property to the entity and its mapping.
    /// </summary>
    /// <param name="type">Property C# type</param>
    /// <param name="propertyName">Property name</param>
    /// <param name="accessModifier">Access modifier (public, private, …)</param>
    /// <param name="OtherModifiers">Other modifiers (required, virtual, …)</param>
    /// <param name="hasGetter">Indicates if property has a getter</param>
    /// <param name="hasSetter">Indicates if property has a setter</param>
    /// <param name="defaultValue">Default value</param>
    /// <param name="isNullable">Indicates if property is nullable (language side)</param>
    public void AddProperty(
    string type,
    string propertyName,
    string? accessModifier = null,
    List<string>? OtherModifiers = null,
    bool hasGetter = false,
    bool hasSetter = false,
    string? defaultValue = null,
    bool isNullable = false
)
    {
        var property = new Property
        {
            Name = propertyName,
            // An unrecognized name becomes an Unknown type rather than an exception,
            // so an entity referencing another one survives parsing (decision 014).
            Type = CSharpTypeConvertor.FromString(type, isNullable),
            AccessModifier = AccessModifierConvertor.FromString(accessModifier),
            OtherModifiers = OtherModifiers ?? [],
            HasGetter = hasGetter,
            HasSetter = hasSetter,
            DefaultValue = defaultValue,
        };

        EntityMap.Entity.Properties.Add(property);
        EntityMap.PropertyMaps.Add(new PropertyMap { Property = property });
    }

    /// <summary>
    /// Add or update database-specific property settings for a property.
    /// </summary>
    /// <param name="propertyName">Property name</param>
    /// <param name="databaseProperties">Database-specific property settings</param>
    public void SetPropertyDatabaseMapping(string propertyName, Dictionary<string, string> databaseProperties)
    {
        var propertyMap = EntityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == propertyName);
        Property? property = null;

        if (propertyMap == null)
        {
            property = EntityMap.Entity.Properties.FirstOrDefault(p => p.Name == propertyName);
            if (property == null)
            {
                property = new Property { Name = propertyName };
                EntityMap.Entity.Properties.Add(property);
            }
            propertyMap = new PropertyMap { Property = property };
            EntityMap.PropertyMaps.Add(propertyMap);
        }
        else
        {
            property = propertyMap.Property;
        }

        foreach (var kvp in databaseProperties)
        {
            switch (kvp.Key.ToLowerInvariant())
            {
                case "columnname" or "column":
                    propertyMap.ColumnName = kvp.Value;
                    break;
                case "type":
                    propertyMap.Type = (DatabaseType)int.Parse(kvp.Value);
                    break;
                case "precision":
                    if (int.TryParse(kvp.Value, out var precision))
                    {
                        propertyMap.Precision = precision;
                    }
                    break;
                case "scale":
                    if (int.TryParse(kvp.Value, out var scale))
                    {
                        propertyMap.Scale = scale;
                    }
                    break;
                case "length":
                    if (int.TryParse(kvp.Value, out var length))
                    {
                        propertyMap.Length = length;
                    }
                    break;
                case "isnullable" or "nullable":
                    if (bool.TryParse(kvp.Value, out var isNullable))
                    {
                        propertyMap.IsNullable = isNullable;
                    }

                    break;
                default:
                    propertyMap.OtherDatabaseProperties[kvp.Key] = kvp.Value;
                    break;
            }
        }
    }

    /// <summary>
    /// Buffers for the artifacts of one entity. NHibernate splits a mapping over an entity
    /// class and an XML descriptor; frameworks that emit code only leave Mapping empty.
    /// </summary>
    protected sealed class EntityArtifact
    {
        public StringBuilder Code { get; } = new();

        public StringBuilder Mapping { get; } = new();

        /// <summary>
        /// Set by BuildTableSchema when it opens an element that FinalizeBuild has to close.
        /// </summary>
        public bool ClassOpened { get; set; }
    }

    /// <summary>
    /// Declaration of what the target framework requires, can express, and adds to the
    /// generated artifact. See decision 009.
    /// </summary>
    public abstract TargetFrameworkDescriptor Descriptor { get; }

    private readonly List<ConversionRecord> records = [];

    /// <summary>
    /// Diagnostic records of this conversion (decision 010). Filled while parsing and while
    /// building; the orchestration returns them next to the artifacts.
    /// </summary>
    public IReadOnlyList<ConversionRecord> Records => records;

    /// <summary>
    /// Adds a record. Public because a loss can occur on the way into the model as well -
    /// a parser reading a fact the model has nowhere to keep reports it here.
    /// </summary>
    public void Report(ConversionRecord record) => records.Add(record);

    /// <summary>
    /// Builds the artifacts for every accumulated entity. The order of the steps is fixed
    /// here so that it cannot drift between frameworks; a framework with nothing to emit in
    /// a step overrides it with an empty body, which is a statement rather than dead code.
    /// </summary>
    /// <returns>List of ConversionSource containing the generated content and type (C#, XML, ...)</returns>
    public List<ConversionSource> Build()
    {
        // The junction entities have to stand before names resolve, so that their relations
        // and the retargeted collections pair like any others; both phases sit between
        // parsing and generation rather than inside either.
        SynthesizeJunctionEntities();
        ResolveEntityNames();

        var outputs = new List<ConversionSource>();

        foreach (var entityMap in EntityMaps)
        {
            // The completeness gate of decision 010, per §3.3 of the paper: a category the
            // descriptor requires and nobody supplied refuses the entity with a failure
            // record before half an artifact is written.
            if (!CheckCompleteness(entityMap))
            {
                continue;
            }

            ReportLosses(entityMap);

            var artifact = new EntityArtifact();

            BuildImports(entityMap, artifact);
            BuildTableSchema(entityMap, artifact);
            BuildPrimaryKey(entityMap, artifact);
            BuildProperties(entityMap, artifact);
            BuildForeignKey(entityMap, artifact);
            BuildEnforcedMembers(entityMap, artifact);

            outputs.AddRange(FinalizeBuild(entityMap, artifact));
        }

        return outputs;
    }

    /// <summary>
    /// The gate before generation (decision 010): every category the descriptor marks as
    /// required has to be present, and every property that would reach the output needs a
    /// language type. A failed check emits failure records and the entity's artifacts are
    /// not generated. Exceptions stay reserved for states the design does not expect at all.
    /// </summary>
    private bool CheckCompleteness(EntityMap entityMap)
    {
        var complete = true;

        foreach (var category in Enum.GetValues<MappingFactCategory>())
        {
            if (Descriptor.SupportOf(category) == FactSupport.Required && !Carries(entityMap, category))
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Failure,
                    Framework = Descriptor.Framework,
                    Entity = entityMap.Entity.Name,
                    Category = category,
                    Reason = $"The target requires {category} and neither the source nor a catalog supplied it; the entity's artifacts are not generated.",
                });
                complete = false;
            }
        }

        foreach (var propertyMap in entityMap.PropertyMaps.Where(pm => pm.Property.Type is null))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = Descriptor.Framework,
                Entity = entityMap.Entity.Name,
                Property = propertyMap.Property.Name,
                Reason = "The property has no language type - it is known only to the mapping, not to the entity class - and no property can be declared without one; the entity's artifacts are not generated.",
            });
            complete = false;
        }

        return complete;
    }

    /// <summary>
    /// Loss records at emission time: the intersection of the facts the model carries with
    /// the categories the descriptor marks as inexpressible (decisions 004, 009, 010). The
    /// builder does not write these by hand - they follow from the descriptor, which is
    /// what makes the list impossible to forget.
    /// </summary>
    private void ReportLosses(EntityMap entityMap)
    {
        foreach (var category in Enum.GetValues<MappingFactCategory>())
        {
            if (Descriptor.SupportOf(category) != FactSupport.NotExpressible || !Carries(entityMap, category))
            {
                continue;
            }

            foreach (var property in LossSubjects(entityMap, category))
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Entity = entityMap.Entity.Name,
                    Property = property,
                    Category = category,
                    Reason = $"The source states {category} and the target has no way to record it; the fact is dropped from the output.",
                });
            }
        }
    }

    /// <summary>
    /// Whether the intermediate representation holds a fact of the category for this entity.
    /// The intersection with the descriptor's states yields both mechanical halves of
    /// decision 010: required-and-absent is a failure, present-and-inexpressible a loss.
    /// </summary>
    private static bool Carries(EntityMap em, MappingFactCategory category) => category switch
    {
        MappingFactCategory.TableName => em.Table is not null,
        MappingFactCategory.SchemaName => em.Schema is not null,
        MappingFactCategory.ColumnName => em.PropertyMaps.Any(pm => pm.ColumnName is not null),
        MappingFactCategory.DatabaseType => em.PropertyMaps.Any(pm => pm.Type is not null),
        MappingFactCategory.Length => em.PropertyMaps.Any(pm => pm.Length is not null),
        MappingFactCategory.PrecisionAndScale => em.PropertyMaps.Any(pm => pm.Precision is not null || pm.Scale is not null),
        MappingFactCategory.Nullability => em.PropertyMaps.Any(pm => pm.IsNullable is not null),
        MappingFactCategory.PrimaryKey => em.PrimaryKey is not null,
        MappingFactCategory.PrimaryKeyStrategy => em.PrimaryKey?.Parts.Any(p => p.Strategy != PrimaryKeyStrategy.Unspecified || p.SourceStrategyName is not null) == true,
        MappingFactCategory.ForeignKeyColumns => em.Relations.Any(r => r.ColumnPairs.Count > 0),
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    /// <summary>
    /// The properties a lost category concerns: one record per property for facts living on
    /// a property map or key part, a single record without a property for facts of the
    /// entity itself.
    /// </summary>
    private static IEnumerable<string?> LossSubjects(EntityMap em, MappingFactCategory category) => category switch
    {
        MappingFactCategory.ColumnName => em.PropertyMaps.Where(pm => pm.ColumnName is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.DatabaseType => em.PropertyMaps.Where(pm => pm.Type is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.Length => em.PropertyMaps.Where(pm => pm.Length is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.PrecisionAndScale => em.PropertyMaps.Where(pm => pm.Precision is not null || pm.Scale is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.Nullability => em.PropertyMaps.Where(pm => pm.IsNullable is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.PrimaryKeyStrategy => em.PrimaryKey!.Parts
            .Where(p => p.Strategy != PrimaryKeyStrategy.Unspecified || p.SourceStrategyName is not null)
            .Select(p => (string?)p.PropertyMap.Property.Name),
        MappingFactCategory.ForeignKeyColumns => em.Relations.Where(r => r.ColumnPairs.Count > 0).Select(r => r.SourceNavigationProperty),
        _ => [null],
    };

    /// <summary>
    /// Namespace declaration and imports.
    /// </summary>
    protected abstract void BuildImports(EntityMap entityMap, EntityArtifact artifact);

    /// <summary>
    /// Class header, class-level attributes, and the opening of the mapping element.
    /// </summary>
    protected abstract void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact);

    /// <summary>
    /// Primary key. Runs before the properties because the key parts are emitted in the
    /// order the key declares, not the order the properties were declared in.
    /// </summary>
    protected abstract void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact);

    /// <summary>
    /// Properties that are neither key parts nor navigation properties.
    /// </summary>
    protected abstract void BuildProperties(EntityMap entityMap, EntityArtifact artifact);

    /// <summary>
    /// Relations and their navigation properties.
    /// </summary>
    protected abstract void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact);

    /// <summary>
    /// Members the framework forces onto the body of the class, as declared by
    /// <see cref="Descriptor"/>. Class-level attributes belong to BuildTableSchema, since
    /// they precede the class header.
    /// </summary>
    protected abstract void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact);

    /// <summary>
    /// Closes the artifacts and turns them into conversion outputs.
    /// </summary>
    protected abstract IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact);
}