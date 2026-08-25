namespace Model.AbstractRepresentation;

public class EntityMap
{
    public required Entity Entity { get; set; }

    public string? Table { get; set; }

    public string? Schema { get; set; }

    public List<PropertyMap> PropertyMaps { get; set; } = [];
    
    public PrimaryKey? PrimaryKey { get; set; }

    /// <summary>
    /// True when the source explicitly states the entity has no key (decision 063) - a
    /// different claim from <see cref="PrimaryKey"/> being null, which only records that
    /// nobody stated one. The model does not validate: it holds this flag next to a filled
    /// key as well, and the completeness gate is what refuses the contradiction.
    /// </summary>
    public bool HasNoKey { get; set; }

    public List<Relation> Relations { get; set; } = [];

    /// <summary>
    /// Unique constraints of the entity (decision 055). A constraint may cover several
    /// columns, so it belongs here rather than on a property map - like <see cref="Relation"/>.
    /// Empty for most entities, which is why it is not required.
    /// </summary>
    public List<UniqueConstraint> UniqueConstraints { get; set; } = [];

    public bool IsJunctionTable { get; set; } = false;
}
