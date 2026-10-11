using AbstractWrappers;

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

    /// <summary>
    /// The source declared the class a mapped superclass, which says it is not an entity
    /// at all. Kept apart from <see cref="Unread"/> because the loss is a different one -
    /// see the record the writer emits for it.
    /// </summary>
    public bool IsMappedSuperclass { get; set; }

    /// <summary>
    /// Where the class-level statements stand in the source (decision 125), keyed by the
    /// annotation's simple name - "Table", "IdClass" - whether the reader took them from an
    /// annotation or from the orm.xml element that spells the same statement. The writer
    /// opens the builder's reading scopes from these; a reader that records none states no
    /// positions and the facts stand all the same.
    /// </summary>
    public Dictionary<string, TextRange> Positions { get; } = new(StringComparer.Ordinal);
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
    /// insertable = false and updatable = false on the column, as the source spelled them.
    /// Kept apart from <see cref="Unread"/> because they are a loss only where the column is
    /// the attribute's own: over a column a relation of the same class also maps they are
    /// the read-only idiom the model derives from the relation and writes again (decision
    /// 012), and the writer tells the two apart with the whole class in hand.
    /// </summary>
    public List<string> ReadOnlyFlags { get; } = [];

    /// <summary>
    /// Losses an implementation's own reader states in its own words (decision 080): the
    /// generic sentence about an annotation without a counterpart would not say what the
    /// reader knows, as at a lazy reference under EclipseLink. Each entry is a whole reason.
    /// </summary>
    public List<string> Notes { get; } = [];

    /// <summary>
    /// Where the attribute's statements stand in the source (decision 125), keyed by the
    /// annotation's simple name - "Column", "Id", "GeneratedValue", "ManyToOne", "JoinColumn",
    /// a vendor's "Nationalized" - and "member" for the declaration of the attribute itself,
    /// which is where a fact the language states (a primitive's not-null) stands. The orm.xml
    /// reader uses the same keys for the elements spelling the same statements, so the writer
    /// opens one scope whichever reader filled the facts.
    /// </summary>
    public Dictionary<string, TextRange> Positions { get; } = new(StringComparer.Ordinal);
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

/// <summary>
/// One @JoinColumn or &lt;join-column&gt;: the three elements the model reads, and the rest
/// as the source spelled them - <paramref name="Unread"/> for the elements the model has no
/// place for (table, columnDefinition), <paramref name="ReadOnlyFlags"/> for insertable =
/// false and updatable = false, which are a loss only where no basic attribute of the
/// class maps the same column (see <see cref="JpaAttributeFacts.ReadOnlyFlags"/>).
/// </summary>
/// <summary>
/// One @JoinColumn or join-column element. <paramref name="Unique"/> is unique = true as
/// the source spelled it: the specification's shortcut for @UniqueConstraint over the
/// column, which the writer reads as that annotation (decision 127).
/// </summary>
public sealed record JpaJoinColumnFacts(
    string? Name,
    string? ReferencedColumnName,
    bool? Nullable,
    bool Unique,
    IReadOnlyList<string> Unread,
    IReadOnlyList<string> ReadOnlyFlags);

public sealed record JpaJoinTableFacts(
    string? Name,
    string? Schema,
    IReadOnlyList<JpaJoinColumnFacts> JoinColumns,
    IReadOnlyList<JpaJoinColumnFacts> InverseJoinColumns);
