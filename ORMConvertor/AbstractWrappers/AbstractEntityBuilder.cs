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
            // If not found, create and add it
            property = new Property
            {
                Name = propertyName,
                Type = new() { CLRType = CLRType.None } // Should be replaced with actual type later
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
    /// A typical parser case - target columns cannot be resolved from a single translation unit,
    /// so ColumnPairs stay empty (to be filled from DB metadata / multi-entity context).
    /// </summary>
    /// <param name="cardinality">Relationship cardinality</param>
    /// <param name="propertyName">Navigation property name on the source entity</param>
    /// <param name="target">Target entity name</param>
    /// <param name="role">Which side holds the foreign key; derived from the cardinality when omitted.</param>
    public void AddForeignKey(Cardinality cardinality, string propertyName, string target, RelationRole? role = null)
    {
        GetOrCreatePropertyMap(propertyName); // the navigation property must exist in the model

        AddRelation(new Relation
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
        });
    }

    /// <summary>
    /// Registers a fully specified relation (including ColumnPairs, junction scenarios, …).
    /// </summary>
    public void AddRelation(Relation relation)
    {
        EntityMap.Relations.Add(relation);
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
    /// <param name="isNullable">Indicates if property is nullable</param>
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
        // Parse type and generic parameter (if any)
        int genericStart = type.IndexOf('<');
        int genericEnd = type.LastIndexOf('>');
        string? genericParameter = null;
        string baseTypeString;

        if (genericStart >= 0 && genericEnd > genericStart)
        {
            // Type has a generic parameter, e.g., List<string>
            genericParameter = type.Substring(genericStart + 1, genericEnd - genericStart - 1).Trim();
            baseTypeString = type[..genericStart].Trim();
        }
        else
        {
            // Type is not generic
            baseTypeString = type.Trim();
        }

        var clrType = CLRTypeConvertor.FromString(baseTypeString);

        var property = new Property
        {
            Name = propertyName,
            Type = new CLRTypeModel { CLRType = clrType, GenericParam = genericParameter },
            AccessModifier = AccessModifierConvertor.FromString(accessModifier),
            OtherModifiers = OtherModifiers ?? [],
            HasGetter = hasGetter,
            HasSetter = hasSetter,
            DefaultValue = defaultValue,
            IsNullable = isNullable,
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
                property = new Property { Name = propertyName, Type = new() { CLRType = CLRType.None } };
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