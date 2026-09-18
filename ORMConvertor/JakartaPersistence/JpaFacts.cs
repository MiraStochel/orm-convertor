namespace JakartaPersistence;

/// <summary>
/// The mapping facts one reading of a JPA entity produced, whether from annotations or
/// from orm.xml (decision 077). Both readers fill this one shape and one writer carries
/// it into the builder, so the two spellings of a fact cannot end in two different calls.
/// The facts are what the source stated, nothing more: an absent default of the
/// implementation is not filled in here (decision 067).
/// </summary>
public sealed class JpaEntityFacts
{
    public required string ClassName { get; init; }

    public string? Table { get; set; }

    public string? Schema { get; set; }

    public List<JpaUniqueConstraintFacts> UniqueConstraints { get; } = [];

    /// <summary>The class @IdClass names, or null.</summary>
    public string? IdClass { get; set; }

    /// <summary>Generators declared at class level, by name.</summary>
    public Dictionary<string, JpaGeneratorFacts> Generators { get; } = new(StringComparer.Ordinal);

    public List<JpaAttributeFacts> Attributes { get; } = [];

    /// <summary>Class-level annotations or elements the model has no place for, for the loss records.</summary>
    public List<string> Unread { get; } = [];
}

public enum JpaAttributeKind
{
    Basic,
    Id,
    EmbeddedId,
    Version,
    Transient,
    ManyToOne,
    OneToOne,
    OneToMany,
    ManyToMany,
}

public sealed class JpaAttributeFacts
{
    public required string Name { get; init; }

    public JpaAttributeKind Kind { get; set; } = JpaAttributeKind.Basic;

    /// <summary>The written type of the attribute where the reader knows it; the fallback target of a relation.</summary>
    public string? TypeText { get; set; }

    public string? ColumnName { get; set; }

    public int? Length { get; set; }

    public int? Precision { get; set; }

    /// <summary>
    /// @Column(secondPrecision), the attribute Jakarta Persistence 3.2 added for the
    /// fractional seconds of a time or timestamp column - the same fact the model carries
    /// as the Precision facet of a temporal family (decisions 019 and 079). Kept apart
    /// from <see cref="Precision"/> here so that the writer can see which of the two the
    /// source actually spelled.
    /// </summary>
    public int? SecondPrecision { get; set; }

    public int? Scale { get; set; }

    public bool? Nullable { get; set; }

    public bool Unique { get; set; }

    public string? ColumnDefinition { get; set; }

    /// <summary>Set by a vendor hook: the column holds national character data.</summary>
    public bool? Nationalized { get; set; }

    /// <summary>GenerationType as written - AUTO, IDENTITY, SEQUENCE, TABLE, UUID - or null.</summary>
    public string? GenerationStrategy { get; set; }

    /// <summary>True when @GeneratedValue stands without a strategy, which means AUTO.</summary>
    public bool Generated { get; set; }

    public string? GeneratorName { get; set; }

    /// <summary>A generator declared on the attribute itself.</summary>
    public JpaGeneratorFacts? Generator { get; set; }

    public string? TargetEntity { get; set; }

    public string? MappedBy { get; set; }

    public bool? Optional { get; set; }

    /// <summary>@MapsId or @PrimaryKeyJoinColumn: the relation shares the primary key.</summary>
    public bool MapsId { get; set; }

    public List<JpaJoinColumnFacts> JoinColumns { get; } = [];

    public JpaJoinTableFacts? JoinTable { get; set; }

    /// <summary>Annotations or elements on the attribute the model has no place for, for the loss records.</summary>
    public List<string> Unread { get; } = [];

    /// <summary>
    /// Losses an implementation's own reader states in its own words (decision 080): the
    /// generic sentence about an annotation without a counterpart would not say what the
    /// reader knows, as at a lazy reference under EclipseLink. Each entry is a whole reason.
    /// </summary>
    public List<string> Notes { get; } = [];
}

public sealed record JpaUniqueConstraintFacts(string? Name, IReadOnlyList<string> ColumnNames);

/// <summary>A @SequenceGenerator or @TableGenerator, whichever elements the source stated.</summary>
public sealed record JpaGeneratorFacts(
    bool IsTable,
    string Name,
    string? SequenceName = null,
    string? Schema = null,
    int? AllocationSize = null,
    int? InitialValue = null,
    string? Table = null,
    string? PkColumnName = null,
    string? ValueColumnName = null,
    string? PkColumnValue = null);

public sealed record JpaJoinColumnFacts(string? Name, string? ReferencedColumnName, bool? Nullable);

public sealed record JpaJoinTableFacts(
    string? Name,
    string? Schema,
    IReadOnlyList<JpaJoinColumnFacts> JoinColumns,
    IReadOnlyList<JpaJoinColumnFacts> InverseJoinColumns);
