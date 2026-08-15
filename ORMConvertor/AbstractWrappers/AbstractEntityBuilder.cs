using System.Text;
using Common.Convertors;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using AbstractWrappers.Descriptors;

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
    /// source entity of an owning relation, the target entity of an inverse one.</param>
    public void AddForeignKey(
        Cardinality cardinality,
        string propertyName,
        string target,
        RelationRole? role = null,
        IReadOnlyList<string>? foreignKeyColumns = null)
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
    /// The resolution phase promised by decision 001: runs once per <see cref="Build"/>, after
    /// all entities of the conversion have been parsed and before anything is generated.
    /// Resolves relation targets by name against <see cref="EntityMaps"/>, fills
    /// <see cref="Relation.ColumnPairs"/> where the source stated the foreign key columns, and
    /// completes the second half of the Reference rule of decision 014 - an unknown type name
    /// matching an entity of the same conversion becomes a reference. A name that resolves to
    /// nothing is left as it is: reporting it is a completeness error for the structured
    /// diagnostics of decision 010, which does not exist yet.
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
                ResolveColumnPairs(entityMap, relation);
            }
        }
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
    /// key, like an unresolved target, is left unpaired for diagnostics to report.
    /// </summary>
    private void ResolveColumnPairs(EntityMap entityMap, Relation relation)
    {
        if (relation.ColumnPairs.Count > 0
            || relation.Cardinality == Cardinality.ManyToMany
            || !pendingForeignKeyColumns.TryGetValue(relation, out var columns))
        {
            return;
        }

        var target = FindEntityMap(relation.TargetEntity);
        if (target is null)
        {
            return;
        }

        var (keyHolder, referencedKey) = relation.Role == RelationRole.Owning
            ? (entityMap, target.PrimaryKey)
            : (target, entityMap.PrimaryKey);

        if (referencedKey is null || referencedKey.Parts.Count != columns.Count)
        {
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

    /// <summary>
    /// Builds the artifacts for every accumulated entity. The order of the steps is fixed
    /// here so that it cannot drift between frameworks; a framework with nothing to emit in
    /// a step overrides it with an empty body, which is a statement rather than dead code.
    /// </summary>
    /// <returns>List of ConversionSource containing the generated content and type (C#, XML, ...)</returns>
    public List<ConversionSource> Build()
    {
        // Entity names resolve only against the finished set, so the phase sits between
        // parsing and generation rather than inside either.
        ResolveEntityNames();

        var outputs = new List<ConversionSource>();

        foreach (var entityMap in EntityMaps)
        {
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