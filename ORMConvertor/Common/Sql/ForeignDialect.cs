using Model;

namespace Common.Sql;

/// <summary>
/// The one statement of the rule decision 088 laid down: a source that declares the dialect
/// of a database system this version does not read has that literal SQL left unread. It sits
/// here, beside <see cref="SqlTypeSpelling"/>, because adding a seventh framework must not
/// mean a seventh copy of the rule (S1) - the wrappers that read literal SQL ask this type
/// what the declaration means, and only the record they write is their own.
///
/// Two reading sites act on it, and no more: <see cref="SqlTypeSpelling.Read"/>, the one
/// reader of a literal type name, and SqlQueryReader in TransactSql, the one T-SQL grammar
/// in the solution. Everything else a source states about its queries - LINQ, HQL, JPQL, the
/// jdbcType of a MyBatis parameter - is a language of a framework rather than of a database
/// system and is read whatever the declaration says; widening the guard to them would refuse
/// input that does not depend on the dialect at all.
///
/// The catalog completion phase is a third consumer and not a reading site (decision 091):
/// it asks a different question - <see cref="ContradictsTheCatalog"/> rather than
/// <see cref="StopsReading"/> - and the answer costs it nothing but a record, because a
/// fact read from a live schema is exact and only its provenance is in doubt.
/// </summary>
public static class ForeignDialect
{
    /// <summary>
    /// Whether a declaration stops the reading of literal SQL. An unstated dialect states
    /// nothing and reads as it always did, which is the same three-valued rule the NHibernate
    /// parser reads not-null by (decision 067); the guard therefore protects only the caller
    /// who speaks up, and that is the price of the choice rather than a gap in it.
    /// </summary>
    public static bool StopsReading(SourceSqlDialect? declared)
        => declared is SourceSqlDialect.AnotherSystem;

    /// <summary>
    /// Whether the declaration contradicts the system the database catalog speaks for
    /// (decision 091). The catalog reader is SQL Server and the tool has no other, so a
    /// source that declares another system and still gets mapping facts from a catalog gets
    /// them from a system it said it is not written for. It is a different question from
    /// <see cref="StopsReading"/> and only happens to have the same answer today: once
    /// <see cref="SourceSqlDialect.AnotherSystem"/> breaks up into named systems, a readable
    /// foreign dialect will stop no reading and still contradict a SQL Server catalog.
    /// </summary>
    public static bool ContradictsTheCatalog(SourceSqlDialect? declared)
        => declared is SourceSqlDialect.AnotherSystem;

    /// <summary>
    /// Why a literal column type was not read. A loss rather than a failure: the target
    /// derives the column type from the language type itself (decision 014) or the catalog
    /// supplies it (F6), so the mapping still holds and is merely poorer by one claim
    /// (decision 048). The record names the spelling it threw away, because that spelling is
    /// the one thing the caller would have to change.
    /// </summary>
    public static string Reason(string spelling)
        => $"The source declares that its literal SQL is written for another database system, so {spelling} was not "
            + "read: a type name legal in both dialects means different things in them - `timestamp` is eight bytes "
            + "of binary in T-SQL and an instant in time elsewhere - and reading it as T-SQL would state a column "
            + "type the source never wrote. The column type is left to the target's own derivation or to the "
            + "database catalog (decision 088).";

    /// <summary>
    /// Why a query was not emitted. A failure rather than a loss, and by the same rule that
    /// governs an unread clause (decisions 053 and 070): a query has no poorer form - an
    /// artifact without its filter or its join returns a different set of rows - so no
    /// artifact is better than one whose text was guessed. It travels without a category,
    /// because refusing the query is no property of the query (decision 048).
    /// </summary>
    public const string QueryReason =
        "The source declares that its literal SQL is written for another database system, which this version does "
        + "not read: the grammar filters syntax and not vocabulary, so `SUBSTR(name, 1, 3)` passes through it as an "
        + "ordinary function call and would be emitted under a name T-SQL does not have. No artifact was generated "
        + "(decision 088).";

    /// <summary>
    /// Why a run in which a declaredly foreign source met a catalog fact says so, once
    /// (decision 091). A conflict rather than an origin: the source's declaration and the
    /// system the catalog speaks for disagree, and the tool does not resolve that silently.
    /// Unlike every other conflict the source does not win here - it stated no value at all,
    /// which is why the phase ran - so the catalog's facts are used and the doubt is handed
    /// to the only one who can settle it. The record is one per run, without a category and
    /// without an entity, because the doubt is no property of one fact.
    /// </summary>
    public const string CatalogReason =
        "The source declares that its literal SQL is written for another database system, yet the mapping facts "
        + "below were completed from a SQL Server catalog - the only kind this version reads. The connection is "
        + "server configuration rather than part of the request (decisions 029 and 030), so the tool cannot tell a "
        + "database migrated to SQL Server - the ordinary reason to convert such a project - from a connection "
        + "pointing at a database the source project never talks to. The facts were used and the source still "
        + "outranks them wherever it stated a value of its own; check that the connected database is the one this "
        + "source belongs to (decision 091).";
}
