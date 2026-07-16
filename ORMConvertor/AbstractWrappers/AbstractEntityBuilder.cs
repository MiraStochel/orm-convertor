using Common.Convertors;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

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
    /// Add a primary key to the entity.
    /// </summary>
    /// <param name="strategy">Primary key strategy</param>
    /// <param name="propertyName">Property name to be used as primary key</param>
    public void AddPrimaryKey(PrimaryKeyStrategy strategy, string propertyName)
    {
        // Find the property in the entity's properties
        var property = EntityMap.Entity.Properties.FirstOrDefault(p => p.Name == propertyName);
        if (property == null)
        {
            // If not found, create and add it
            property = new Property
            {
                Name = propertyName,
                Type = new (){ CLRType = CLRType.None  } // Should be replaced with actual type later
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

        propertyMap.OtherDatabaseProperties["IsPrimaryKey"] = "true";
        propertyMap.OtherDatabaseProperties["PrimaryKeyStrategy"] = ((int)strategy).ToString();
    }

    /// <summary>
    /// Add a foreign key to the entity.
    /// </summary>
    /// <param name="cardinality">Relationship cardinality</param>
    /// <param name="propertyName">Property name to be used as foreign key</param>
    public void AddForeignKey(Cardinality cardinality, string propertyName, string target)
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

        propertyMap.OtherDatabaseProperties["IsForeignKey"] = "true";
        propertyMap.OtherDatabaseProperties["ForeignKeyCardinality"] = ((int)cardinality).ToString();

        propertyMap.Relation = new Relation
        {
            Source = EntityMap?.Entity?.Name,
            Target = target,
            Cardinality = cardinality
        };
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
    /// Build the conversion result for the entity.
    /// </summary>
    /// <returns>List of ConversionResult containing the generated content and type (C#, XML, ...)</returns>
    public abstract List<ConversionSource> Build();

    /// <summary>
    /// Build import statements for the entity.
    /// </summary>
    protected abstract void BuildImports();

    /// <summary>
    /// Build table and schema information for the entity.
    /// </summary>
    protected abstract void BuildTableSchema();

    /// <summary>
    /// Build primary key information for the entity.
    /// </summary>
    protected abstract void BuildPrimaryKey();

    /// <summary>
    /// Build foreign key information for the entity.
    /// </summary>
    protected abstract void BuildForeignKey();

    /// <summary>
    /// Build property definitions for the entity.
    /// </summary>
    protected abstract void BuildProperties();

    /// <summary>
    /// Finalize the build process for the entity.
    /// </summary>
    protected abstract void FinalizeBuild();
}
