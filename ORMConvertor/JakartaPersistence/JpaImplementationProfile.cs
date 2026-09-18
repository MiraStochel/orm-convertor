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
    JpaCounterTable? DefaultCounterTable = null);

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
