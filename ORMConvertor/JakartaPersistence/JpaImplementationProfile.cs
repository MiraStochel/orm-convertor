using Model;
using Model.AbstractRepresentation.Enums;

namespace JakartaPersistence;

/// <summary>
/// The profile of a Jakarta Persistence implementation (decisions 076 and 077): what the
/// version number of the descriptor cannot say, because the same annotation means
/// different things under Hibernate and under EclipseLink. Beside the descriptor rather
/// than inside it - the shared descriptor type is the .NET frameworks' too and stays
/// untouched by the Java side.
/// </summary>
/// <param name="Implementation">The framework the profile belongs to; the key to its defaults.</param>
/// <param name="SpecificationLevel">The Jakarta Persistence level the shared layer is written against.</param>
/// <param name="AutoStrategy">The mechanism the implementation resolves GenerationType.AUTO to on the pinned dialect.</param>
/// <param name="DefaultAllocationSize">The allocation size the implementation assumes when none is stated.</param>
/// <param name="NamingStrategy">The naming the artifact is correct under, in words - a fact of the consumer's configuration (decision 040).</param>
/// <param name="NationalizedByDefault">Whether a plain String maps to national character data without an annotation.</param>
/// <param name="UppercaseImplicitNames">Whether the implementation writes implicit names in upper case (EclipseLink).</param>
/// <param name="VendorAnnotationPackage">The package of the implementation's own annotations.</param>
/// <param name="LazyReferenceNeedsWeaving">Whether fetch = LAZY on a reference is inert without bytecode weaving (EclipseLink).</param>
/// <param name="DefaultCounterTable">The counter table a TABLE generator uses when the source names none, where a run measured it.</param>
/// <param name="NativeQueryExpandsCollection">Whether a collection bound to a positional parameter of a native query is expanded into the list IN ranges over, where a run measured that it is (decision 113); otherwise the escape path refuses a collection parameter rather than hand the driver a list. Hibernate 7.4.5 expands it, EclipseLink 5.0.0 does not.</param>
/// <param name="BindsLiterals">Whether the implementation binds every literal of a JPQL query as a parameter of the SQL it writes, where a run measured that it does (decision 113): a grouping key with a literal in it then reaches SQL Server as another expression than the same value in the select list - each literal a parameter of its own -, which the database refuses, so the builder writes such a grouping in native SQL. EclipseLink 5.0.0 binds them, Hibernate 7.4.5 writes them inline.</param>
/// <param name="KeyThroughReferenceJoins">Whether the implementation reaches the key of a referenced entity through the reference - <c>p.customer.id</c>, the way a foreign key column no attribute maps is written - with an inner join of the referenced table, where a run measured that it does: the path then drops the rows whose foreign key is NULL, and inside the on of an outer join the implementation writes SQL the database refuses, so the builder writes such a query in native SQL (decision 113). EclipseLink 5.0.0 joins, Hibernate 7.4.5 reads the foreign key column.</param>
/// <param name="DropsJoinsInSubqueries">Whether the implementation leaves a join inside a subquery out of the SQL it writes, where a run measured that it does: the joined entity and its condition disappear and a column of the joined entity is read from the subquery's own entity, so the subquery answers other rows without an error, and the builder writes such a query in native SQL (decision 113). EclipseLink 5.0.0 drops them, Hibernate 7.4.5 writes them.</param>
/// <param name="InnerJoinsFollowOuterJoins">Whether the implementation writes every inner entity join into the list of tables after the outer joins, where a run measured that it does: the condition of an outer join that names the alias of an inner join then names a table SQL Server has not met yet, which it refuses, so the builder writes such a query in native SQL (decision 113). EclipseLink 5.0.0 moves them, Hibernate 7.4.5 keeps the order of the query.</param>
/// <param name="EntityNamesRefused">The entity names the implementation's JPQL parser refuses wherever an entity is named - the words of its grammar it does not take for a name there, compared without regard to case -, where a run measured them. JPQL has no qualified or quoted spelling of an entity name, so the builder writes such a query in native SQL (decision 113).</param>
/// <param name="EntityNamesRefusedAsJoinTarget">The entity names the parser refuses as the target of an entity join only, measured the same way.</param>
public sealed record JpaImplementationProfile(
    ORMEnum Implementation,
    string SpecificationLevel,
    PrimaryKeyStrategy AutoStrategy,
    int DefaultAllocationSize,
    string NamingStrategy,
    bool NationalizedByDefault,
    bool UppercaseImplicitNames,
    string VendorAnnotationPackage,
    bool LazyReferenceNeedsWeaving = false,
    JpaCounterTable? DefaultCounterTable = null,
    bool NativeQueryExpandsCollection = false,
    bool BindsLiterals = false,
    bool KeyThroughReferenceJoins = false,
    bool DropsJoinsInSubqueries = false,
    bool InnerJoinsFollowOuterJoins = false,
    IReadOnlySet<string>? EntityNamesRefused = null,
    IReadOnlySet<string>? EntityNamesRefusedAsJoinTarget = null);

/// <summary>
/// The table behind a TABLE generator, as the implementation would create it from its own
/// defaults (decision 080). The builder writes these values out instead of leaving them to
/// the target, because silence here does not name one database object: each implementation
/// would reach for a counter table of its own, and the row of a shared one is what the
/// generated key actually counts from. Stated only where a run measured it - EclipseLink's
/// values are the DDL its 5.0.0 release emitted for @GeneratedValue without a strategy.
/// </summary>
/// <param name="Table">The counter table (EclipseLink: SEQUENCE).</param>
/// <param name="KeyColumn">The column naming the counter (pkColumnName; EclipseLink: SEQ_NAME).</param>
/// <param name="ValueColumn">The column holding the value (valueColumnName; EclipseLink: SEQ_COUNT).</param>
/// <param name="KeyValue">The row the counter lives in (pkColumnValue; EclipseLink: SEQ_GEN).</param>
public sealed record JpaCounterTable(string Table, string KeyColumn, string ValueColumn, string KeyValue);
