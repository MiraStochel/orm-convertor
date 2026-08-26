using System.Text;
using Common.Convertors;
using Common.Naming;
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
    /// Add a table name. Only an empty fact is filled: a table an earlier-read source
    /// already stated stays, and a different later claim is a conflict record (decision 017).
    /// </summary>
    /// <param name="tableName">Table name</param>
    public void AddTable(string tableName)
    {
        if (string.IsNullOrEmpty(tableName))
        {
            return;
        }

        if (EntityMap.Table is null)
        {
            EntityMap.Table = tableName;
        }
        else if (!string.Equals(EntityMap.Table, tableName, StringComparison.Ordinal))
        {
            ReportInputConflict(null, MappingFactCategory.TableName,
                $"An earlier source maps the entity to the table '{EntityMap.Table}', a later one to '{tableName}'.");
        }
    }

    /// <summary>
    /// Add a schema name. Fills only an empty fact, like <see cref="AddTable"/> (decision 017).
    /// </summary>
    /// <param name="schemaName">Schema name</param>
    public void AddSchema(string schemaName)
    {
        if (string.IsNullOrEmpty(schemaName))
        {
            return;
        }

        if (EntityMap.Schema is null)
        {
            EntityMap.Schema = schemaName;
        }
        else if (!string.Equals(EntityMap.Schema, schemaName, StringComparison.Ordinal))
        {
            ReportInputConflict(null, MappingFactCategory.SchemaName,
                $"An earlier source maps the entity to the schema '{EntityMap.Schema}', a later one to '{schemaName}'.");
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
    /// Define the (possibly composite) primary key of the entity. The whole key is one
    /// compound fact under the source precedence rule (decision 036): the first call
    /// defines it, and a repeated call is compared instead of replacing - an identical
    /// restatement is no event, a claim over the same parts fills what the first left
    /// empty (a part's Unspecified strategy, a missing key class record), and a different
    /// part list is a conflict record with the first key kept whole, details included.
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

        if (EntityMap.PrimaryKey is { } existing)
        {
            ReconcileKeyClaim(existing, parts, sourceKeyClass);
            return;
        }

        lastKeyClaimDiscards.Remove(EntityMap);

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
    /// Records that the source explicitly states the entity has no key (decision 063) -
    /// EF Core's [Keyless]. A statement, not an absence: it keeps the catalog from
    /// supplying a key and the target's convention from deriving one. It is not
    /// reconciled with a key claim here - the model does not validate, so both may
    /// stand, and the completeness gate refuses the contradiction before generation.
    /// </summary>
    public void MarkNoKey() => EntityMap.HasNoKey = true;

    /// <summary>
    /// A later key claim against an already defined key (decision 036). The identity of
    /// the key is the ordered list of its parts: a claim over the same parts fills what
    /// the first left empty and a differing part list is discarded whole with a conflict
    /// record - parts interlock, so a key merged from two claims would be one nobody
    /// stated. What the claim discarded is remembered per part, so that the strategy
    /// details trailing the discarded claim can fall with it in SetKeyStrategyDetails.
    /// </summary>
    private void ReconcileKeyClaim(
        PrimaryKey existing,
        IReadOnlyList<(string PropertyName, int Order, PrimaryKeyStrategy Strategy)> parts,
        SourceKeyClass? sourceKeyClass)
    {
        // Both sides in key order: PrimaryKey.Parts is sorted by Order on construction.
        var claimed = parts.OrderBy(p => p.Order).ToList();
        var discarded = new HashSet<string>(StringComparer.Ordinal);
        lastKeyClaimDiscards[EntityMap] = discarded;

        if (!existing.Parts.Select(p => p.PropertyMap.Property.Name)
                .SequenceEqual(claimed.Select(p => p.PropertyName), StringComparer.Ordinal))
        {
            foreach (var (propertyName, _, _) in claimed)
            {
                discarded.Add(propertyName);
            }

            ReportInputConflict(null, MappingFactCategory.PrimaryKey,
                $"An earlier source states the primary key ({string.Join(", ", existing.Parts.Select(p => p.PropertyMap.Property.Name))}), "
                + $"a later one ({string.Join(", ", claimed.Select(p => p.PropertyName))}).");
            return;
        }

        // Same parts: only an empty fact is filled. An Unspecified strategy states
        // nothing - like the collection kind - so a stated one fills it without an event.
        var rebuilt = new List<PrimaryKeyPart>();
        var changed = false;

        for (var i = 0; i < existing.Parts.Count; i++)
        {
            var part = existing.Parts[i];
            var strategy = claimed[i].Strategy;

            if (part.Strategy == PrimaryKeyStrategy.Unspecified && strategy != PrimaryKeyStrategy.Unspecified)
            {
                rebuilt.Add(new PrimaryKeyPart
                {
                    PropertyMap = part.PropertyMap,
                    Order = part.Order,
                    Strategy = strategy,
                    SourceStrategyName = part.SourceStrategyName,
                    StrategyParameters = part.StrategyParameters,
                    SourceStrategyParameters = part.SourceStrategyParameters,
                });
                changed = true;
                continue;
            }

            if (strategy != PrimaryKeyStrategy.Unspecified && strategy != part.Strategy)
            {
                discarded.Add(part.PropertyMap.Property.Name);
                ReportInputConflict(part.PropertyMap.Property.Name, MappingFactCategory.PrimaryKeyStrategy,
                    $"An earlier source states the strategy {part.Strategy}, a later one {strategy}.");
            }

            rebuilt.Add(part);
        }

        var keyClass = existing.SourceKeyClass;

        if (sourceKeyClass is not null)
        {
            if (keyClass is null)
            {
                keyClass = sourceKeyClass;
                changed = true;
            }
            else if (!string.Equals(keyClass.ClassName, sourceKeyClass.ClassName, StringComparison.Ordinal))
            {
                ReportInputConflict(null, MappingFactCategory.PrimaryKey,
                    $"An earlier source names the key class '{keyClass.ClassName}', a later one '{sourceKeyClass.ClassName}'.");
            }
        }

        if (changed)
        {
            EntityMap.PrimaryKey = new PrimaryKey { Parts = rebuilt, SourceKeyClass = keyClass };
        }
    }

    /// <summary>
    /// Part names whose claim the last AddPrimaryKey call on the entity discarded - a
    /// differing part list, or a differing stated strategy. SetKeyStrategyDetails follows
    /// its AddPrimaryKey in every parser, so the details trailing a discarded claim are
    /// recognized here and dropped with it instead of landing on the kept key; the
    /// conflict record already stands at the key (decision 036).
    /// </summary>
    private readonly Dictionary<EntityMap, HashSet<string>> lastKeyClaimDiscards = [];

    /// <summary>
    /// Record what the source said about a key part's strategy beyond the vocabulary value:
    /// its own name for it and the generator parameters. Call after <see cref="AddPrimaryKey"/>.
    /// Only an empty fact is filled - the name where none is recorded, parameters entry by
    /// entry - and a differing later claim is a conflict record with the first value kept
    /// (decisions 017 and 036). Details following a key claim that AddPrimaryKey discarded
    /// are dropped with it: the conflict is already recorded at the key.
    /// </summary>
    /// <param name="propertyName">Name of a property that is already part of the key.</param>
    /// <param name="sourceStrategyName">The source's own name for the strategy, when the vocabulary lost it.</param>
    /// <param name="parameters">Canonical generator parameters (decision 020), e.g. sequence name or block size.</param>
    /// <param name="sourceParameters">Parameters as the source wrote them, where the vocabulary did not capture them.</param>
    public void SetKeyStrategyDetails(
        string propertyName,
        string? sourceStrategyName = null,
        IReadOnlyDictionary<GeneratorParameter, string>? parameters = null,
        IReadOnlyDictionary<string, string>? sourceParameters = null)
    {
        var key = EntityMap.PrimaryKey
            ?? throw new InvalidOperationException("Key strategy details can only be set once the key is defined.");

        // A detail whose key claim was discarded falls with its claim (decision 036).
        if (lastKeyClaimDiscards.TryGetValue(EntityMap, out var discarded) && discarded.Contains(propertyName))
        {
            return;
        }

        var target = key.Parts.FirstOrDefault(p => p.PropertyMap.Property.Name == propertyName)
            ?? throw new ArgumentException($"Property '{propertyName}' is not part of the primary key.", nameof(propertyName));

        var mergedName = target.SourceStrategyName;

        if (sourceStrategyName is not null)
        {
            if (mergedName is null)
            {
                mergedName = sourceStrategyName;
            }
            else if (!string.Equals(mergedName, sourceStrategyName, StringComparison.Ordinal))
            {
                ReportInputConflict(propertyName, MappingFactCategory.PrimaryKeyStrategy,
                    $"An earlier source calls the strategy '{mergedName}', a later one '{sourceStrategyName}'.");
            }
        }

        // Key parts are init-only, so the detail lands on a replacement part and the key
        // is rebuilt around it. Rebuilding also re-checks the invariants of PrimaryKey.
        var replacement = new PrimaryKeyPart
        {
            PropertyMap = target.PropertyMap,
            Order = target.Order,
            Strategy = target.Strategy,
            SourceStrategyName = mergedName,
            StrategyParameters = parameters is null
                ? target.StrategyParameters
                : MergedParameters(target.StrategyParameters, parameters, propertyName),
            SourceStrategyParameters = sourceParameters is null
                ? target.SourceStrategyParameters
                : MergedParameters(target.SourceStrategyParameters, sourceParameters, propertyName),
        };

        EntityMap.PrimaryKey = new PrimaryKey
        {
            Parts = [.. key.Parts.Select(p => ReferenceEquals(p, target) ? replacement : p)],
            SourceKeyClass = key.SourceKeyClass,
        };
    }

    /// <summary>
    /// Generator parameters of two claims, entry by entry like the other key-value facts:
    /// an entry nobody recorded is filled, an occupied one is kept and a differing later
    /// value is a conflict record (decisions 017 and 036).
    /// </summary>
    private Dictionary<TKey, string> MergedParameters<TKey>(
        IReadOnlyDictionary<TKey, string> existing,
        IReadOnlyDictionary<TKey, string> claimed,
        string propertyName)
        where TKey : notnull
    {
        var merged = existing.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        foreach (var (parameter, value) in claimed)
        {
            if (!merged.TryGetValue(parameter, out var current))
            {
                merged[parameter] = value;
            }
            else if (!string.Equals(current, value, StringComparison.Ordinal))
            {
                ReportInputConflict(propertyName, MappingFactCategory.PrimaryKeyStrategy,
                    $"An earlier source states the generator parameter {parameter} '{current}', a later one '{value}'.");
            }
        }

        return merged;
    }

    /// <summary>
    /// States the kind of a collection property - what tells &lt;set&gt; from &lt;bag&gt; in an
    /// NHibernate mapping - where the language side has not committed to one. Only an empty
    /// fact is filled: the entity text outranks an auxiliary mapping artifact (decision 017),
    /// so a kind the declared type already carries stays, and Unspecified states nothing and
    /// never overwrites. A property that is no collection is left alone - the claim has
    /// nothing to attach to until a relation upgrades the type, so call this after the
    /// relation is registered.
    /// </summary>
    public void SetCollectionKind(string propertyName, CollectionKind kind)
    {
        var propertyMap = EntityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == propertyName);

        if (kind == CollectionKind.Unspecified
            || propertyMap?.Property.Type is not
                { Category: LangTypeCategory.Collection, CollectionKind: CollectionKind.Unspecified } type)
        {
            return;
        }

        propertyMap.Property.Type = LangType.Collection(type.ElementType!, kind, type.IsNullable);
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
    /// Adds a unique constraint over the named properties (decision 055). Constraints are
    /// identified by the set of properties they cover, not by their name: one and the same
    /// constraint reaches the model from the source and from the catalog under two
    /// spellings, and the set is what decides. A repeated set is therefore not added twice;
    /// where the two spellings of the name differ, the first is kept and the difference is
    /// a conflict record, exactly as decision 017 rules for every other fact.
    ///
    /// An empty list is a no-op rather than an error: a source stating a constraint over
    /// nothing said nothing, and the model does not validate (see the invariants).
    /// </summary>
    public void AddUniqueConstraint(string? name, IReadOnlyList<string> propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);

        var parts = propertyNames
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (parts.Count == 0)
        {
            return;
        }

        var constraint = new UniqueConstraint
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            PropertyNames = parts,
        };

        var existing = EntityMap.UniqueConstraints.FirstOrDefault(c => c.CoversSameAs(constraint));

        if (existing is null)
        {
            EntityMap.UniqueConstraints.Add(constraint);
            return;
        }

        if (existing.Name is null && constraint.Name is not null)
        {
            // The set was already stated without a name; a later source naming the same
            // set adds information rather than contradicting it.
            EntityMap.UniqueConstraints[EntityMap.UniqueConstraints.IndexOf(existing)] = new UniqueConstraint
            {
                Name = constraint.Name,
                PropertyNames = existing.PropertyNames,
            };
            return;
        }

        if (constraint.Name is not null && !string.Equals(existing.Name, constraint.Name, StringComparison.Ordinal))
        {
            ReportInputConflict(
                parts.Count == 1 ? parts[0] : null,
                MappingFactCategory.UniqueConstraint,
                $"An earlier source names the unique constraint over ({string.Join(", ", existing.PropertyNames)}) "
                + $"'{existing.Name}', a later one '{constraint.Name}'.");
        }
    }

    /// <summary>
    /// A navigation the source framework states by convention rather than annotation - what a
    /// bare "public Customer Customer" means in EF Core. Whether the written type names an
    /// entity or a scalar the language vocabulary does not capture (uint, a key class) is
    /// decidable no sooner than after the last source of the conversion is parsed, so the
    /// claim waits here and <see cref="ResolveConventionNavigations"/> materializes it. The
    /// optional callback derives the foreign key columns by the source framework's own
    /// convention once the target entity and its key are known - the framework knowledge
    /// stays in the wrapper, this mechanism is neutral.
    /// </summary>
    public void AddConventionNavigation(
        Cardinality cardinality,
        string propertyName,
        string targetTypeName,
        RelationRole? role = null,
        Func<EntityMap, EntityMap, IReadOnlyList<string>?>? foreignKeyColumns = null)
    {
        conventionNavigations.Add(new ConventionNavigation(
            EntityMap, cardinality, propertyName, targetTypeName, role, foreignKeyColumns));
    }

    /// <summary>
    /// Materializes the pending convention navigations against the entities of the
    /// conversion. The catalog completion phase calls it before reading, so that the
    /// source's conventional claims outrank the catalog (decision 015), and
    /// <see cref="Build"/> calls it for conversions that never meet a catalog. A name that
    /// resolves to no entity was not a navigation and the candidate is dropped without a
    /// record, exactly like any other unknown type name; a property that meanwhile carries
    /// a relation (an annotation, an earlier call) or sits in the primary key is skipped.
    /// </summary>
    public void ResolveConventionNavigations()
    {
        var pending = conventionNavigations.ToList();
        conventionNavigations.Clear();

        foreach (var candidate in pending)
        {
            var target = FindEntityMap(candidate.TargetTypeName);

            if (target is null
                || candidate.Entity.Relations.Any(r => r.SourceNavigationProperty == candidate.PropertyName)
                || candidate.Entity.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == candidate.PropertyName) == true)
            {
                continue;
            }

            EntityMap = candidate.Entity;
            AddForeignKey(
                candidate.Cardinality,
                candidate.PropertyName,
                target.Entity.Name,
                candidate.Role,
                candidate.ForeignKeyColumns?.Invoke(candidate.Entity, target));
        }
    }

    /// <summary>
    /// The dissolution phase of decision 031: the name a mapping recorded in
    /// <see cref="SourceKeyClass"/> is a reference to a class of the same conversion, and
    /// what that class declares are the parts of the key, not the properties of another
    /// entity. The phase transfers the language declarations of the class's members onto
    /// the key parts (only an empty fact is filled - the column side stays as the mapping
    /// read it, decision 017), removes the holding property of the Embedded form, and
    /// takes the class out of <see cref="EntityMaps"/> - but only a class that carries no
    /// mapping of its own; one that does is a conflict of two first-degree sources and
    /// stays an entity. Runs before every other phase: the class must be gone before
    /// convention navigations or name resolution could take it for an entity, and before
    /// the catalog would look for its table. The catalog completion phase calls it as its
    /// first step and <see cref="Build"/> calls it for conversions that never meet a
    /// catalog; a key class once handled is never handled again, so the double run and
    /// the order of the input sources change nothing (S2).
    /// </summary>
    public void DissolveKeyClasses()
    {
        foreach (var entityMap in EntityMaps.ToList())
        {
            var key = entityMap.PrimaryKey;

            if (key?.SourceKeyClass is not { } keyClass || !handledKeyClasses.Add(keyClass))
            {
                continue;
            }

            var keyClassMap = FindEntityMap(keyClass.ClassName);

            if (keyClassMap is null)
            {
                // The types of the parts are not guessed: the catalog completion phase may
                // still supply them, and otherwise the completeness gate refuses the
                // entity - this record says why that refusal happens (decision 031).
                if (keyClass.Form == KeyClassForm.Embedded
                    || key.Parts.Any(p => p.PropertyMap.Property.Type is null))
                {
                    Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Incompleteness,
                        Framework = Descriptor.Framework,
                        Entity = entityMap.Entity.Name,
                        Property = keyClass.PropertyName,
                        Category = MappingFactCategory.PrimaryKey,
                        Reason = $"The mapping names '{keyClass.ClassName}' as the key class of the entity, but no "
                            + "source of the conversion declares it; the language declarations of the key parts "
                            + "cannot be taken from it (decision 031).",
                    });
                }

                DissolveHoldingProperty(entityMap, keyClass);
                continue;
            }

            if (keyClassMap.Table is not null || keyClassMap.Schema is not null
                || keyClassMap.PrimaryKey is not null || keyClassMap.Relations.Count > 0)
            {
                // Silently un-entitying a mapped class would be worse than an unread key
                // class, so it stays and the disagreement is said out loud (decision 031).
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Conflict,
                    Framework = Descriptor.Framework,
                    Entity = SimpleEntityName(keyClass.ClassName),
                    Category = MappingFactCategory.PrimaryKey,
                    Reason = $"The mapping of '{entityMap.Entity.Name}' names '{keyClass.ClassName}' as its key "
                        + "class, but the class carries a mapping of its own; two first-degree sources claim "
                        + "different things about it, so it remains an entity of the conversion (decision 031).",
                });
                continue;
            }

            foreach (var part in key.Parts)
            {
                var member = keyClassMap.Entity.Properties
                    .FirstOrDefault(p => p.Name == part.PropertyMap.Property.Name);

                if (member is null)
                {
                    continue; // known only to the mapping; the completeness gate reports it
                }

                // Only an empty fact is filled: the class is level 1a of decision 017 and
                // carries the language side, the mapping artifact is 1b and keeps the
                // column side - an entity declaring the same property loses nothing.
                var property = part.PropertyMap.Property;
                property.Type ??= member.Type;
                property.AccessModifier ??= member.AccessModifier;
                property.DefaultValue ??= member.DefaultValue;

                if (!property.HasGetter && !property.HasSetter)
                {
                    property.HasGetter = member.HasGetter;
                    property.HasSetter = member.HasSetter;
                }
            }

            var partNames = key.Parts
                .Select(p => p.PropertyMap.Property.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var member in keyClassMap.Entity.Properties)
            {
                if (partNames.Contains(member.Name)
                    || entityMap.Relations.Any(r => r.SourceNavigationProperty == member.Name))
                {
                    continue;
                }

                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Entity = entityMap.Entity.Name,
                    Property = member.Name,
                    Category = MappingFactCategory.PrimaryKey,
                    Reason = $"The key class '{keyClass.ClassName}' declares the member and the mapping does not "
                        + "name it as a key part, so nothing persists it; it becomes neither a key part nor a "
                        + "property of the entity (decision 031).",
                });
            }

            if (keyClass.Form == KeyClassForm.Embedded)
            {
                DissolveHoldingProperty(entityMap, keyClass);

                // The change of form itself is a loss for every .NET target: the flat key
                // (decision 006) drops the class name and shortens the access path to the
                // parts - o.Id.OrderID becomes o.OrderID (decision 031).
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Entity = entityMap.Entity.Name,
                    Property = keyClass.PropertyName,
                    Category = MappingFactCategory.PrimaryKey,
                    Reason = $"The source reaches the key parts through the key class '{keyClass.ClassName}' and "
                        + "every target renders the key flat (decision 006); the class name and the access path "
                        + $"through '{keyClass.PropertyName}' disappear from the output.",
                });
            }

            EntityMaps.Remove(keyClassMap);

            if (ReferenceEquals(currentEntityMap, keyClassMap))
            {
                currentEntityMap = entityMap;
            }
        }
    }

    /// <summary>
    /// Removes the property holding the key class of the Embedded form: the flat rendering
    /// replaces it with the key parts themselves, so emitting both would emit the same
    /// column twice. The Mirrored form has no holding property and nothing happens.
    /// </summary>
    private static void DissolveHoldingProperty(EntityMap entityMap, SourceKeyClass keyClass)
    {
        if (keyClass.PropertyName is not { } holder)
        {
            return;
        }

        entityMap.Entity.Properties.RemoveAll(p => p.Name == holder);
        entityMap.PropertyMaps.RemoveAll(pm => pm.Property.Name == holder);
    }

    /// <summary>
    /// Key classes the dissolution phase has handled. The phase runs twice on the catalog
    /// path - as the completion phase's first step and again in <see cref="Build"/> - so
    /// the guard keeps the records single as well as the edits (decision 031).
    /// </summary>
    private readonly HashSet<SourceKeyClass> handledKeyClasses = [];

    /// <summary>
    /// One pending conventional navigation claim; see <see cref="AddConventionNavigation"/>.
    /// </summary>
    private sealed record ConventionNavigation(
        EntityMap Entity,
        Cardinality Cardinality,
        string PropertyName,
        string TargetTypeName,
        RelationRole? Role,
        Func<EntityMap, EntityMap, IReadOnlyList<string>?>? ForeignKeyColumns);

    /// <summary>
    /// Convention navigations waiting for the entities of the conversion to be known.
    /// Builder state like the pending columns below; an entry whose name resolves to no
    /// entity is dropped at materialization, the way any unknown type name is.
    /// </summary>
    private readonly List<ConventionNavigation> conventionNavigations = [];

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
    /// Junction facts waiting on a many-to-many relation, or null when there are none.
    /// The counterpart of <see cref="SupplyJunctionFacts"/> for the catalog completion phase.
    /// </summary>
    public JunctionFacts? StatedJunctionFacts(Relation relation)
        => pendingJunctionFacts.TryGetValue(relation, out var facts) ? facts : null;

    /// <summary>
    /// Replaces the junction facts of a many-to-many relation. Called by the catalog
    /// completion phase (decision 015) when the schema knows the junction table the source
    /// left unnamed; the caller merges, so that every fact the source stated wins over the
    /// catalog. Ignored for other cardinalities, where junction facts mean nothing.
    /// </summary>
    public void SupplyJunctionFacts(Relation relation, JunctionFacts facts)
    {
        if (relation.Cardinality == Cardinality.ManyToMany)
        {
            pendingJunctionFacts[relation] = facts;
        }
    }

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
    /// Class name of a synthesized junction entity: the table name, singularized by the one
    /// convention the whole solution shares (decision 050) rather than by a copy of it.
    /// </summary>
    private static string DeriveJunctionEntityName(string table)
        => EntityTableNaming.EntityNameFor(table);

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
                IsUnicode = part.PropertyMap.IsUnicode,
                SourceSqlType = part.PropertyMap.SourceSqlType,
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

        // The referenced side follows the role the same way the key did above.
        var referencedEntity = relation.Role == RelationRole.Owning
            ? relation.TargetEntity
            : entityMap.Entity.Name;
        CompleteForeignKeyColumnTypes(keyHolder, referencedEntity, pairs);
    }

    /// <summary>
    /// A foreign key column property the source never typed - a &lt;key-many-to-one&gt;
    /// part, where the class holds the navigation and the columns live only in the
    /// mapping - takes its language type and column facts from the key part it
    /// references. Only properties of the entity are completed; a detached map inside a
    /// pair stays as it is, because nothing declares it. The value is derived within the
    /// same conversion, but it is still a claim the source never made, so it carries a
    /// record (decision 010).
    /// </summary>
    private void CompleteForeignKeyColumnTypes(EntityMap keyHolder, string referencedEntity, List<ColumnPair> pairs)
    {
        foreach (var pair in pairs)
        {
            if (pair.Source.Property.Type is not null
                || pair.Target.Property.Type is null
                || !keyHolder.PropertyMaps.Contains(pair.Source))
            {
                continue;
            }

            pair.Source.Property.Type = pair.Target.Property.Type;
            pair.Source.Property.AccessModifier ??= AccessModifier.Public;

            if (!pair.Source.Property.HasGetter && !pair.Source.Property.HasSetter)
            {
                pair.Source.Property.HasGetter = true;
                pair.Source.Property.HasSetter = true;
            }

            pair.Source.Type ??= pair.Target.Type;
            pair.Source.IsUnicode ??= pair.Target.IsUnicode;
            pair.Source.SourceSqlType ??= pair.Target.SourceSqlType;
            pair.Source.Length ??= pair.Target.Length;
            pair.Source.Precision ??= pair.Target.Precision;
            pair.Source.Scale ??= pair.Target.Scale;

            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Convention,
                Framework = Descriptor.Framework,
                Entity = keyHolder.Entity.Name,
                Property = pair.Source.Property.Name,
                Reason = $"The property has no language type; it was taken over from the key part "
                    + $"'{pair.Target.Property.Name}' of '{referencedEntity}' that its column references - "
                    + "derived within the conversion, but still a claim the source never made (decision 010).",
            });
        }
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
    /// The entity of this conversion a possibly qualified name refers to, or null when the
    /// name points outside the conversion. Protected so that a builder can ask about the
    /// far side of a relation - the NHibernate builder derives the inverse attribute of a
    /// collection from whether the owning counterpart is at hand. The match is on the simple
    /// class name, which is how relations reference entities (decision 001).
    /// </summary>
    protected EntityMap? FindEntityMap(string entityName)
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
    /// Add a property to the entity and its mapping. Find-or-create, like
    /// <see cref="GetOrCreatePropertyMap"/>: a second artifact declaring the same property -
    /// a Java class beside orm.xml, a class beside a MyBatis resultMap - fills in only the
    /// language facts nobody stated yet and leaves a conflict record where it claims
    /// something else, which is the rule of decision 017 applied to the language side
    /// (decision 049).
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
        // An unrecognized name becomes an Unknown type rather than an exception,
        // so an entity referencing another one survives parsing (decision 014).
        var langType = CSharpTypeConvertor.FromString(type, isNullable);
        var access = AccessModifierConvertor.FromString(accessModifier);

        // Copied, not aliased: a later declaration merges into this list, and mutating a
        // list the caller still holds would be a surprise it never asked for.
        List<string> modifiers = [.. OtherModifiers ?? []];

        var existing = EntityMap.Entity.Properties.FirstOrDefault(p => p.Name == propertyName);

        if (existing is null)
        {
            var property = new Property
            {
                Name = propertyName,
                Type = langType,
                AccessModifier = access,
                OtherModifiers = modifiers,
                HasGetter = hasGetter,
                HasSetter = hasSetter,
                DefaultValue = defaultValue,
            };

            EntityMap.Entity.Properties.Add(property);
            EntityMap.PropertyMaps.Add(new PropertyMap { Property = property });
            return;
        }

        MergeLanguageFacts(existing, langType, access, modifiers, hasGetter, hasSetter, defaultValue);

        // A property the mapping named first has an entry among the entity's properties
        // already; its map is created with it, but pairing them here costs nothing and
        // keeps the two lists from drifting.
        if (!EntityMap.PropertyMaps.Any(pm => pm.Property.Name == propertyName))
        {
            EntityMap.PropertyMaps.Add(new PropertyMap { Property = existing });
        }
    }

    /// <summary>
    /// Fills the language facts a second declaration of the same property brings, fact by
    /// fact (decision 049). An empty fact is filled, a stated one is never overwritten and
    /// a differing claim leaves a conflict record. Getter and setter are positive-only -
    /// false means nobody said so, not that the property has none - and the other modifiers
    /// are a set rather than a fact, so they merge instead of clashing.
    /// </summary>
    private void MergeLanguageFacts(
        Property property,
        LangType? type,
        AccessModifier? access,
        List<string> modifiers,
        bool hasGetter,
        bool hasSetter,
        string? defaultValue)
    {
        if (type is not null)
        {
            if (property.Type is null)
            {
                property.Type = type;
            }
            else if (!SameLanguageType(property.Type, type))
            {
                ReportInputConflict(property.Name, null,
                    $"An earlier source declares the property as '{Describe(property.Type)}', a later one as '{Describe(type)}'.");
            }
        }

        if (access is not null)
        {
            if (property.AccessModifier is null)
            {
                property.AccessModifier = access;
            }
            else if (property.AccessModifier != access)
            {
                ReportInputConflict(property.Name, null,
                    $"An earlier source declares the property {property.AccessModifier}, a later one {access}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            if (string.IsNullOrWhiteSpace(property.DefaultValue))
            {
                property.DefaultValue = defaultValue;
            }
            else if (!string.Equals(property.DefaultValue, defaultValue, StringComparison.Ordinal))
            {
                ReportInputConflict(property.Name, null,
                    $"An earlier source initializes the property with '{property.DefaultValue}', a later one with '{defaultValue}'.");
            }
        }

        property.HasGetter |= hasGetter;
        property.HasSetter |= hasSetter;

        foreach (var modifier in modifiers.Where(m => !property.OtherModifiers.Contains(m, StringComparer.Ordinal)))
        {
            property.OtherModifiers.Add(modifier);
        }
    }

    /// <summary>
    /// Value comparison of two language types: instances are created per parse, so reference
    /// equality would call two identical declarations a conflict.
    /// </summary>
    private static bool SameLanguageType(LangType left, LangType right)
        => left.Category == right.Category
           && left.IsNullable == right.IsNullable
           && left.ScalarType == right.ScalarType
           && left.CollectionKind == right.CollectionKind
           && string.Equals(left.TargetEntity, right.TargetEntity, StringComparison.Ordinal)
           && string.Equals(left.SourceName, right.SourceName, StringComparison.Ordinal)
           && (left.ElementType is null
               ? right.ElementType is null
               : right.ElementType is not null && SameLanguageType(left.ElementType, right.ElementType));

    /// <summary>
    /// A language type in words, for a conflict record. Deliberately not the C# spelling:
    /// the record talks about the model's claim, and the model is ecosystem-neutral
    /// (decision 014).
    /// </summary>
    private static string Describe(LangType type)
    {
        var core = type.Category switch
        {
            LangTypeCategory.Scalar => type.ScalarType!.Value.ToString(),
            LangTypeCategory.Reference => $"reference to {type.TargetEntity}",
            LangTypeCategory.Collection => $"{type.CollectionKind} of {Describe(type.ElementType!)}",
            _ => type.SourceName!,
        };

        return type.IsNullable ? core + ", nullable" : core;
    }

    /// <summary>
    /// Add database-specific property settings for a property. Only an empty fact is
    /// filled: a fact an earlier-read source already stated is never overwritten, and a
    /// different later claim leaves a conflict record - the same incremental write the
    /// catalog completion phase uses, applied to the levels within the input (decision 017).
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
                    if (propertyMap.ColumnName is null)
                    {
                        propertyMap.ColumnName = kvp.Value;
                    }
                    else if (!string.Equals(propertyMap.ColumnName, kvp.Value, StringComparison.Ordinal))
                    {
                        ReportInputConflict(propertyName, MappingFactCategory.ColumnName,
                            $"An earlier source maps the property to the column '{propertyMap.ColumnName}', a later one to '{kvp.Value}'.");
                    }
                    break;
                case "precision":
                    if (int.TryParse(kvp.Value, out var precision))
                    {
                        if (propertyMap.Precision is null)
                        {
                            propertyMap.Precision = precision;
                        }
                        else if (propertyMap.Precision != precision)
                        {
                            ReportInputConflict(propertyName, MappingFactCategory.PrecisionAndScale,
                                $"An earlier source states precision {propertyMap.Precision}, a later one {precision}.");
                        }
                    }
                    else
                    {
                        ReportUnreadableFact(propertyName, MappingFactCategory.PrecisionAndScale, "precision", kvp.Value);
                    }

                    break;
                case "scale":
                    if (int.TryParse(kvp.Value, out var scale))
                    {
                        if (propertyMap.Scale is null)
                        {
                            propertyMap.Scale = scale;
                        }
                        else if (propertyMap.Scale != scale)
                        {
                            ReportInputConflict(propertyName, MappingFactCategory.PrecisionAndScale,
                                $"An earlier source states scale {propertyMap.Scale}, a later one {scale}.");
                        }
                    }
                    else
                    {
                        ReportUnreadableFact(propertyName, MappingFactCategory.PrecisionAndScale, "scale", kvp.Value);
                    }

                    break;
                case "length":
                    if (int.TryParse(kvp.Value, out var length))
                    {
                        if (propertyMap.Length is null)
                        {
                            propertyMap.Length = length;
                        }
                        else if (propertyMap.Length != length)
                        {
                            ReportInputConflict(propertyName, MappingFactCategory.Length,
                                $"An earlier source states length {propertyMap.Length}, a later one {length}.");
                        }
                    }
                    else
                    {
                        ReportUnreadableFact(propertyName, MappingFactCategory.Length, "length", kvp.Value);
                    }

                    break;
                case "isnullable" or "nullable":
                    if (bool.TryParse(kvp.Value, out var isNullable))
                    {
                        if (propertyMap.IsNullable is null)
                        {
                            propertyMap.IsNullable = isNullable;
                        }
                        else if (propertyMap.IsNullable != isNullable)
                        {
                            ReportInputConflict(propertyName, MappingFactCategory.Nullability,
                                $"An earlier source states the property is {(propertyMap.IsNullable.Value ? "nullable" : "not nullable")}, a later one states the opposite.");
                        }
                    }
                    else
                    {
                        ReportUnreadableFact(propertyName, MappingFactCategory.Nullability, "nullable", kvp.Value);
                    }

                    break;
                case "isversion" or "version":
                    // Positive-only, like the catalog's supply direction (decision 030):
                    // "not a version" is the absence of the claim, not a claim to conflict with.
                    if (bool.TryParse(kvp.Value, out var isVersion) && isVersion)
                    {
                        propertyMap.IsVersion = true;
                    }
                    else if (!bool.TryParse(kvp.Value, out _))
                    {
                        ReportUnreadableFact(propertyName, MappingFactCategory.VersionColumn, "version", kvp.Value);
                    }

                    break;
                default:
                    ReportUnknownFact(propertyName, kvp.Key, kvp.Value);
                    break;
            }
        }
    }

    /// <summary>
    /// The typed half of the database mapping: the type family and its companions travel
    /// as values of the model, never through the string dictionary - the untyped channel
    /// that carried the enum as a stringified ordinal is gone (decision 019). Type,
    /// unicode and the literal source spelling fill only an empty fact and a differing
    /// later claim is a conflict record (decision 017); the facets are claims the type
    /// name itself makes, so they never override a facet the source stated explicitly
    /// and their difference is no conflict.
    /// </summary>
    /// <param name="propertyName">Property name</param>
    /// <param name="type">Type family, or null when the vocabulary does not capture the source's type.</param>
    /// <param name="isUnicode">The unicode facet where the source's type name states it.</param>
    /// <param name="sourceSqlType">The literal type as the source spelled it, where the family is missing or coarser (decision 019).</param>
    /// <param name="length">Length the type name itself implies (e.g. a single character).</param>
    /// <param name="precision">Precision the type name itself implies (e.g. money, datetime).</param>
    /// <param name="scale">Scale the type name itself implies.</param>
    public void SetPropertyDatabaseType(
        string propertyName,
        DatabaseType? type,
        bool? isUnicode = null,
        string? sourceSqlType = null,
        int? length = null,
        int? precision = null,
        int? scale = null)
    {
        var propertyMap = GetOrCreatePropertyMap(propertyName);

        if (type is not null)
        {
            if (propertyMap.Type is null)
            {
                propertyMap.Type = type;
            }
            else if (propertyMap.Type != type)
            {
                ReportInputConflict(propertyName, MappingFactCategory.DatabaseType,
                    $"An earlier source maps the property as {propertyMap.Type}, a later one as {type}.");
            }
        }

        if (isUnicode is not null)
        {
            if (propertyMap.IsUnicode is null)
            {
                propertyMap.IsUnicode = isUnicode;
            }
            else if (propertyMap.IsUnicode != isUnicode)
            {
                ReportInputConflict(propertyName, MappingFactCategory.DatabaseType,
                    $"An earlier source maps the property as {(propertyMap.IsUnicode.Value ? "unicode" : "non-unicode")}, "
                    + $"a later one as {(isUnicode.Value ? "unicode" : "non-unicode")}.");
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceSqlType))
        {
            if (propertyMap.SourceSqlType is null)
            {
                propertyMap.SourceSqlType = sourceSqlType;
            }
            else if (!string.Equals(propertyMap.SourceSqlType, sourceSqlType, StringComparison.Ordinal))
            {
                ReportInputConflict(propertyName, MappingFactCategory.DatabaseType,
                    $"An earlier source spells the type '{propertyMap.SourceSqlType}', a later one '{sourceSqlType}'.");
            }
        }

        propertyMap.Length ??= length;
        propertyMap.Precision ??= precision;
        propertyMap.Scale ??= scale;
    }

    /// <summary>
    /// The conflict record of decision 017: a fact an earlier-read source of the input
    /// already claimed is never overwritten by a later one. The levels are ordered in
    /// time - the framework's input text parses before its auxiliary mapping artifacts,
    /// see ParserFactory - so "claimed at a higher or equal level" equals "occupied on
    /// arrival", and within one level the first value read wins so that the result stays
    /// deterministic (S2). The same event as a disagreement with the catalog, hence the
    /// same record kind (decision 015).
    /// </summary>
    private void ReportInputConflict(string? property, MappingFactCategory? category, string reason)
        => Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Conflict,
            Framework = Descriptor.Framework,
            Entity = EntityMap.Entity.Name,
            Property = property,
            Category = category,
            Reason = reason + " A fact read earlier is never overwritten by a later input source (decision 017), so the first value is kept.",
        });

    /// <summary>
    /// A fact the source stated in a spelling the model cannot read. Reported rather than
    /// dropped: silence would leave the target filling its own default in the place of
    /// something the input did say (decision 010).
    /// </summary>
    private void ReportUnreadableFact(string? property, MappingFactCategory? category, string key, string value)
        => Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Entity = EntityMap.Entity.Name,
            Property = property,
            Category = category,
            Reason = $"The source states {key} '{value}', which is not a value the representation can hold; the fact is dropped and the target's own default applies.",
        });

    /// <summary>
    /// The language nullability of a key part, which a flat key cannot carry: no target
    /// admits a nullable identifier, so a source's <c>int?</c> is emitted as <c>int</c>
    /// (decision 054). A loss and not a convention - the source did state something and the
    /// artifact states the opposite - and the wording lives here so that the two C# builders
    /// and the JPA builder after them cannot phrase the same fact differently. Called by the
    /// builder that flattens: Dapper knows no keys and keeps the question mark, so there the
    /// record would be untrue.
    /// </summary>
    protected void ReportNullableKeyPartLoss(EntityMap entityMap, Property property, ConversionContentType artifact)
    {
        if (property.Type is not { IsNullable: true })
        {
            return;
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = artifact,
            Entity = entityMap.Entity.Name,
            Property = property.Name,
            Category = MappingFactCategory.Nullability,
            Reason = "The source declares the key part as nullable; an identifier cannot be nullable in "
                + "the target, so the property is emitted without the language nullability.",
        });
    }

    /// <summary>
    /// A mapping fact the representation has no place for at all - as opposed to
    /// <see cref="ReportUnreadableFact"/>, where the place exists and only the spelling
    /// cannot be read. It used to land in a free dictionary on PropertyMap that nobody
    /// ever read, so it reached the model and died at emission without a word; now it is
    /// a loss like any other fact the target will not carry (decision 048). No category:
    /// a key outside the vocabulary belongs to none of them, and picking the nearest one
    /// would claim more about it than we know.
    /// </summary>
    private void ReportUnknownFact(string? property, string key, string value)
        => Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Entity = EntityMap.Entity.Name,
            Property = property,
            Reason = $"The source states {key} '{value}', a mapping fact the representation has no place "
                + "for; it is dropped and the target's own default applies.",
        });

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
    /// Attributes every record from <paramref name="fromIndex"/> on to one input unit
    /// (decision 066). Called by the orchestration around each entity parser's Parse call -
    /// the only place that knows which unit a parser was fed; a record born during that
    /// reading came from that unit. Parsers and builders never call this.
    /// </summary>
    public void AttributeRecords(int fromIndex, string unit)
    {
        for (var i = fromIndex; i < records.Count; i++)
        {
            records[i] = records[i] with { Unit = unit };
        }
    }

    /// <summary>
    /// Builds the artifacts for every accumulated entity. The order of the steps is fixed
    /// here so that it cannot drift between frameworks; a framework with nothing to emit in
    /// a step overrides it with an empty body, which is a statement rather than dead code.
    /// </summary>
    /// <returns>List of ConversionSource containing the generated content and type (C#, XML, ...)</returns>
    public List<ConversionSource> Build()
    {
        // Key classes dissolve before anything else reads the entity list, so that no
        // later phase takes one for an entity; convention navigations follow for the
        // same reason. A conversion that never met the catalog completion phase runs
        // both here; after one that did, both find everything handled already.
        DissolveKeyClasses();
        ResolveConventionNavigations();

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

        // An entity stating both a key and that it has none is a contradiction no target
        // can render, and the source framework builds no model from it either; the input
        // has no meaning to reproduce, so no artifact is generated (decision 063).
        if (entityMap.HasNoKey && entityMap.PrimaryKey is not null)
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = Descriptor.Framework,
                Entity = entityMap.Entity.Name,
                Category = MappingFactCategory.PrimaryKey,
                Reason = $"The source states both the primary key ({string.Join(", ", entityMap.PrimaryKey.Parts.Select(p => p.PropertyMap.Property.Name))}) "
                    + "and that the entity has no key; the two claims cannot both be reproduced, so the entity's artifacts are not generated (decision 063).",
            });
            complete = false;
        }

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
                    // The denied key gets its own wording: "nobody supplied it" would send
                    // the user looking for a missing fact instead of at a stated one (F11).
                    Reason = category == MappingFactCategory.PrimaryKey && entityMap.HasNoKey
                        ? $"The target requires {category} and the source states the entity has no key; the entity's artifacts are not generated."
                        : $"The target requires {category} and neither the source nor a catalog supplied it; the entity's artifacts are not generated.",
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
        // The unicode facet and the literal source spelling are part of the type claim
        // (decision 019), so any of the three carries the category.
        MappingFactCategory.DatabaseType => em.PropertyMaps.Any(pm =>
            pm.Type is not null || pm.IsUnicode is not null || pm.SourceSqlType is not null),
        MappingFactCategory.Length => em.PropertyMaps.Any(pm => pm.Length is not null),
        MappingFactCategory.PrecisionAndScale => em.PropertyMaps.Any(pm => pm.Precision is not null || pm.Scale is not null),
        MappingFactCategory.Nullability => em.PropertyMaps.Any(pm => pm.IsNullable is not null),
        MappingFactCategory.PrimaryKey => em.PrimaryKey is not null,
        MappingFactCategory.PrimaryKeyStrategy => em.PrimaryKey?.Parts.Any(p => p.Strategy != PrimaryKeyStrategy.Unspecified || p.SourceStrategyName is not null) == true,
        MappingFactCategory.ForeignKeyColumns => em.Relations.Any(r => r.ColumnPairs.Count > 0),
        MappingFactCategory.VersionColumn => em.PropertyMaps.Any(pm => pm.IsVersion),
        MappingFactCategory.UniqueConstraint => em.UniqueConstraints.Count > 0,
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
        MappingFactCategory.DatabaseType => em.PropertyMaps
            .Where(pm => pm.Type is not null || pm.IsUnicode is not null || pm.SourceSqlType is not null)
            .Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.Length => em.PropertyMaps.Where(pm => pm.Length is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.PrecisionAndScale => em.PropertyMaps.Where(pm => pm.Precision is not null || pm.Scale is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.Nullability => em.PropertyMaps.Where(pm => pm.IsNullable is not null).Select(pm => (string?)pm.Property.Name),
        MappingFactCategory.PrimaryKeyStrategy => em.PrimaryKey!.Parts
            .Where(p => p.Strategy != PrimaryKeyStrategy.Unspecified || p.SourceStrategyName is not null)
            .Select(p => (string?)p.PropertyMap.Property.Name),
        MappingFactCategory.ForeignKeyColumns => em.Relations.Where(r => r.ColumnPairs.Count > 0).Select(r => r.SourceNavigationProperty),
        MappingFactCategory.VersionColumn => em.PropertyMaps.Where(pm => pm.IsVersion).Select(pm => (string?)pm.Property.Name),
        // A constraint over one column concerns that property; over several it concerns
        // the entity, and naming one of them would be arbitrary.
        MappingFactCategory.UniqueConstraint => em.UniqueConstraints
            .Select(c => c.PropertyNames.Count == 1 ? (string?)c.PropertyNames[0] : null),
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