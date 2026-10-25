using System.Runtime.CompilerServices;
using AbstractWrappers;
using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers;

/// <summary>
/// What the parsers of an EF Core source share within one conversion (decision 132): the
/// fluent configuration is read first, and the class parser reading after it takes from
/// here what the configuration states but cannot write on its own. A relationship names
/// its navigations by lambda and not their type, so it waits for the class that declares
/// them; the table a DbSet gives and the default schema are a convention and a default,
/// which stand below the annotations (decision 068); a concurrency token is the version
/// only under conditions the class decides (decision 116); and a member the configuration
/// names is mapped even where the class gives it no setter (decision 134).
/// The query pass reads from it too (decision 185): the entity a DbSet of a given name
/// exposes, which is how a root written by the set's name becomes the query over that
/// entity when the name is not the entity's with an "s" - PageDirectories over
/// DbSet&lt;PageDirectory&gt;. One context per conversion, found through the entity builder
/// the orchestration creates once and hands to both calls of the parser factory, the same
/// shape as the MyBatis reading context (decision 084); the table is weak on the key, so a
/// finished conversion takes its context with it. The orchestration and the abstract
/// wrappers never see the context itself - the same shape as the JPA reading context
/// (decision 077).
/// </summary>
public sealed class EFCoreReadingContext
{
    private static readonly ConditionalWeakTable<AbstractEntityBuilder, EFCoreReadingContext> Contexts = [];

    /// <summary>
    /// The context of the conversion the entity builder belongs to. Called by the parser
    /// factory, once per parser list, and the same instance comes back both times.
    /// </summary>
    public static EFCoreReadingContext For(AbstractEntityBuilder entityBuilder)
    {
        ArgumentNullException.ThrowIfNull(entityBuilder);
        return Contexts.GetValue(entityBuilder, _ => new EFCoreReadingContext());
    }

    private readonly Dictionary<string, string> dbSets = new(StringComparer.Ordinal);

    /// <summary>
    /// The entity each DbSet name exposes; null where two contexts of the conversion expose
    /// different entities under one name, which no model of EF Core has and no name decides.
    /// </summary>
    private readonly Dictionary<string, string?> exposed = new(StringComparer.Ordinal);

    private readonly List<FluentRelationship> relationships = [];

    private readonly HashSet<(string Entity, string Property)> concurrencyTokens = [];

    private readonly HashSet<(string Entity, string Property)> rowVersions = [];

    private readonly Dictionary<(string Entity, string Property), string> generatedOptions = [];

    private readonly HashSet<(string Entity, string Property)> named = [];

    /// <summary>The schema HasDefaultSchema states for the whole model, or null.</summary>
    public string? DefaultSchema { get; private set; }

    /// <summary>
    /// Records the name of the DbSet property that exposes an entity. The first one read
    /// stands; the second, differing one is returned so that the caller can report it.
    /// </summary>
    public string? AddDbSet(string entity, string property)
    {
        if (!exposed.TryAdd(property, entity) && exposed[property] != entity)
        {
            exposed[property] = null;
        }

        if (dbSets.TryAdd(entity, property) || dbSets[entity] == property)
        {
            return null;
        }

        return dbSets[entity];
    }

    /// <summary>The name of the DbSet property that exposes the entity, or null.</summary>
    public string? DbSetOf(string entity) => dbSets.GetValueOrDefault(entity);

    /// <summary>
    /// The entity the DbSet property of this name exposes, in any context the conversion
    /// read (decision 185); null where no context declares the name, or where two declare it
    /// over different entities.
    /// </summary>
    public string? EntityExposedBy(string property) => exposed.GetValueOrDefault(property);

    /// <summary>
    /// Records HasDefaultSchema. The first one read stands; a differing later one is
    /// returned so that the caller can report it.
    /// </summary>
    public string? SetDefaultSchema(string schema)
    {
        if (DefaultSchema is null || DefaultSchema == schema)
        {
            DefaultSchema = schema;
            return null;
        }

        return DefaultSchema;
    }

    public void AddRelationship(FluentRelationship relationship) => relationships.Add(relationship);

    /// <summary>The relationships configured on the entity, as the configured side states them.</summary>
    public IEnumerable<FluentRelationship> RelationshipsOf(string entity)
        => relationships.Where(r => r.Entity == entity);

    /// <summary>
    /// The relationship a navigation belongs to: configured on its own entity, or configured
    /// on the entity it points at and naming it as the far end. The configured side first,
    /// because that is where the configuration spoke of the navigation by itself.
    /// </summary>
    public (FluentRelationship Relationship, bool Mirrored)? RelationshipFor(string entity, string navigation, string target)
    {
        if (relationships.FirstOrDefault(r => r.Entity == entity && r.Navigation == navigation) is { } own)
        {
            return (own, false);
        }

        if (relationships.FirstOrDefault(r => r.Entity == target && r.Inverse == navigation) is { } far)
        {
            return (far, true);
        }

        return null;
    }

    public void MarkConcurrencyToken(string entity, string property) => concurrencyTokens.Add((entity, property));

    public bool IsConcurrencyToken(string entity, string property) => concurrencyTokens.Contains((entity, property));

    public void MarkRowVersion(string entity, string property) => rowVersions.Add((entity, property));

    public bool HasRowVersion(string entity) => rowVersions.Any(v => v.Entity == entity);

    public bool IsRowVersion(string entity, string property) => rowVersions.Contains((entity, property));

    /// <summary>
    /// Records ValueGeneratedNever (None) or ValueGeneratedOnAdd (Identity), in the vocabulary
    /// of [DatabaseGenerated], whose reading the class parser shares.
    /// </summary>
    public void SetGeneratedOption(string entity, string property, string option) => generatedOptions[(entity, property)] = option;

    public string? GeneratedOptionOf(string entity, string property) => generatedOptions.GetValueOrDefault((entity, property));

    private readonly Dictionary<(string Entity, string Property), string> generationCalls = [];

    /// <summary>
    /// Records a call of the configuration on a property that names how its value is generated
    /// - UseHiLo, UseSequence, UseIdentityColumn, a default, a generator - which the subset of
    /// decision 132 does not read: a key under it claims no strategy (decision 187). The first
    /// call stands.
    /// </summary>
    public void MarkGenerationCall(string entity, string property, string call) => generationCalls.TryAdd((entity, property), call);

    public string? GenerationCallOf(string entity, string property) => generationCalls.GetValueOrDefault((entity, property));

    /// <summary>
    /// The model's UseHiLo() or UseKeySequences(), which the subset does not read either: under
    /// it a key the convention or the annotation would make an IDENTITY column is fed from a
    /// sequence instead, and claims no strategy (decision 187). The first call stands.
    /// </summary>
    public string? ModelGenerationCall { get; private set; }

    public void SetModelGenerationCall(string call) => ModelGenerationCall ??= call;

    /// <summary>
    /// Records a member the configuration names on the entity it configures - in Property,
    /// HasKey, HasIndex, HasAlternateKey, HasOne or HasMany. EF Core maps a member so named
    /// whether or not the class gives it a setter, through its backing field (decision 134);
    /// Ignore names nothing, it takes the member out.
    /// </summary>
    public void MarkNamed(string entity, string property) => named.Add((entity, property));

    /// <summary>
    /// Whether the configuration names a scalar member of the entity: on the entity itself, or
    /// as a member of HasForeignKey, which belongs to the dependent - the entity
    /// HasForeignKey&lt;T&gt; names, the configured one where its navigation is a reference,
    /// and the far side where it is a collection. The configuration does not spell the far
    /// side's name, so it is the class that declares the inverse navigation WithOne names,
    /// pointing back at the configured entity, and any class but the configured one where
    /// WithOne names none (decision 134). A navigation named as the far end is
    /// <see cref="RelationshipFor"/>'s answer.
    /// </summary>
    public bool Names(string entity, string property, Func<string, string, bool> declaresNavigation)
        => named.Contains((entity, property))
           || relationships.Any(r => r.ForeignKey?.Contains(property) == true && IsDependent(r, entity, declaresNavigation));

    private static bool IsDependent(FluentRelationship relationship, string entity, Func<string, string, bool> declaresNavigation)
    {
        if (relationship.Dependent is { } dependent)
        {
            return dependent == entity;
        }

        if (!relationship.NavigationIsCollection)
        {
            return relationship.Entity == entity;
        }

        return relationship.Entity != entity
               && (relationship.Inverse is null || declaresNavigation(relationship.Inverse, relationship.Entity));
    }
}

/// <summary>
/// One relationship as the fluent configuration states it, from the side it was configured
/// on (decision 132): the navigation of HasOne or HasMany, the far end WithOne or WithMany
/// names, the foreign key properties HasForeignKey lists in their order, and for a
/// one-to-one the entity HasForeignKey&lt;T&gt; names as the one holding the key.
/// </summary>
public sealed record FluentRelationship(
    string Entity,
    string Navigation,
    bool NavigationIsCollection,
    string? Inverse,
    bool InverseIsCollection,
    IReadOnlyList<string>? ForeignKey,
    string? Dependent)
{
    /// <summary>The cardinality seen from the configured navigation.</summary>
    public Cardinality Cardinality => (NavigationIsCollection, InverseIsCollection) switch
    {
        (false, true) => Cardinality.ManyToOne,
        (false, false) => Cardinality.OneToOne,
        (true, false) => Cardinality.OneToMany,
        (true, true) => Cardinality.ManyToMany,
    };
}
