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
/// <param name="AutoStrategy">The mechanism the implementation resolves GenerationType.AUTO over an integer key to on the pinned dialect, as a run measured it - what the JPA parser reads a source's AUTO as (decision 187): Hibernate 7.4.5 a sequence of <paramref name="DefaultSequenceName"/>, EclipseLink 5.0.0 the counter table of <paramref name="DefaultCounterTable"/>.</param>
/// <param name="DefaultAllocationSize">The allocation size the implementation assumes when none is stated.</param>
/// <param name="DefaultSequenceName">The sequence the implementation takes for SEQUENCE - and Hibernate for AUTO - where no generator names one, <c>{0}</c> standing for the table, measured over a connection (decision 187): Hibernate 7.4.5 <c>{0}_SEQ</c>, after the table and not the entity; EclipseLink 5.0.0 <c>SEQ_GEN_SEQUENCE</c>. The parser reads a source's unnamed sequence by it, and the builder writes it for a sequence the model names none for.</param>
/// <param name="NamingStrategy">The naming the artifact is correct under, in words - a fact of the consumer's configuration (decision 040).</param>
/// <param name="NationalizedByDefault">Whether a plain String maps to national character data without an annotation.</param>
/// <param name="UppercaseImplicitNames">Whether the implementation writes implicit names in upper case (EclipseLink).</param>
/// <param name="VendorAnnotationPackage">The package of the implementation's own annotations.</param>
/// <param name="LazyReferenceNeedsWeaving">Whether fetch = LAZY on a reference is inert without bytecode weaving (EclipseLink).</param>
/// <param name="DefaultCounterTable">The counter table a TABLE generator uses when the source names none, where a run measured it.</param>
/// <param name="NativeQueryExpandsCollection">Whether a collection bound to a positional parameter of a native query is expanded into the list IN ranges over, where a run measured that it is (decision 113); otherwise the escape path refuses a collection parameter rather than hand the driver a list. Hibernate 7.4.5 expands it, EclipseLink 5.0.0 does not.</param>
/// <param name="BindsLiterals">Whether the implementation binds every literal of a JPQL query as a parameter of the SQL it writes, where a run measured that it does (decision 113): a grouping key with a literal in it then reaches SQL Server as another expression than the same value in the select list - each literal a parameter of its own -, which the database refuses. The builder answers with <paramref name="InlineLiteralsCall"/> where the implementation has one, and writes such a grouping in native SQL where it has none. EclipseLink 5.0.0 binds them, Hibernate 7.4.5 writes them inline.</param>
/// <param name="InlineLiteralsCall">The call on the query object under which the implementation writes the literals of that one query inline instead of binding them, where <paramref name="BindsLiterals"/> and a run measured that the call changes it: binding is a setting of the query, not of the language. The builder appends it to a query whose grouping key carries a literal and to no other, so every other query keeps binding. Spelled as the Java a consumer project writes, the constants unqualified (EclipseLink 5.0.0: <c>.setHint(QueryHints.BIND_PARAMETERS, HintValues.FALSE)</c>, both from <c>org.eclipse.persistence.config</c>); null where the implementation has no such switch.</param>
/// <param name="RecursionLimitCall">The call on the query object under which the implementation closes the SQL it writes with the limit of recursion, <c>{0}</c> standing for the number, where a run measured that the database obeys it (decision 139): JPQL and HQL have no limit in their text, so the limit is a setting of the query object, like the slice. The builder appends it to a query whose recursive definition the source limits and to no other. Hibernate 7.4.5: <c>.unwrap(org.hibernate.query.Query.class).addQueryHint("MAXRECURSION {0}")</c>, which its SQL Server dialect writes as <c>OPTION (MAXRECURSION n)</c> at the end of the statement; null where the implementation has no such call, and a query with a limit then goes to native SQL.</param>
/// <param name="KeyThroughReferenceJoins">Whether the implementation reaches the key of a referenced entity through the reference - <c>p.customer.id</c>, the way a foreign key column no attribute maps is written - with an inner join of the referenced table, where a run measured that it does: the path then drops the rows whose foreign key is NULL, and inside the on of an outer join the implementation writes SQL the database refuses, so the builder writes such a query in native SQL (decision 113). EclipseLink 5.0.0 joins, Hibernate 7.4.5 reads the foreign key column.</param>
/// <param name="DropsJoinsInSubqueries">Whether the implementation leaves an entity join inside a subquery out of the SQL it writes, where a run measured that it does: the joined entity and its condition disappear and a column of the joined entity is read from the subquery's own entity, so the subquery answers other rows without an error. The builder then writes an inner join of a subquery in JPQL's original spelling - a further range variable of the from clause with the condition in the where clause, which the implementation writes as a cross join with the condition in WHERE, the same rows - and a subquery with an outer join, which has no such spelling, in native SQL (decision 113). EclipseLink 5.0.0 drops them, Hibernate 7.4.5 writes them.</param>
/// <param name="RefusesSetOperationsInSubqueries">Whether the implementation's JPQL parser refuses a set operation inside a subquery with a syntax error, where a run measured that it does (decision 120): the set operation it takes at the top of a query it does not take in the parentheses of IN or EXISTS, so the builder writes such a query in native SQL (decision 113). EclipseLink 5.0.0 refuses it, Hibernate 7.4.5 takes it.</param>
/// <param name="InnerJoinsFollowOuterJoins">Whether the implementation writes every inner entity join into the list of tables after the outer joins, where a run measured that it does: the condition of an outer join that names the alias of an inner join then names a table SQL Server has not met yet, which it refuses, so the builder writes such a query in native SQL (decision 113). EclipseLink 5.0.0 moves them, Hibernate 7.4.5 keeps the order of the query.</param>
/// <param name="EntityNamesRefused">The entity names the implementation's JPQL parser refuses wherever an entity is named - the words of its grammar it does not take for a name there, compared without regard to case -, where a run measured them. JPQL has no qualified or quoted spelling of an entity name, so the builder writes such a query in native SQL (decision 113).</param>
/// <param name="EntityNamesRefusedAsJoinTarget">The entity names the parser refuses as the target of an entity join only, measured the same way.</param>
/// <param name="DropsJoinsInSetOperations">Whether the implementation leaves the entity joins of a member of a set operation out of the SQL it writes, as <paramref name="DropsJoinsInSubqueries"/> says of a subquery, where a run measured that it does: the member then answers the rows of its own entity alone, without an error. A member has no range-variable spelling the builder trusts there, so a set operation with a joined member goes out in native SQL (decision 113). EclipseLink 5.0.0 drops them (measured over the FIBEN catalog, decision 149: a union of two joined members returned 49 870 rows for 189), Hibernate 7.4.5 writes them.</param>
/// <param name="ResolvesNamesAsAttributesBeforeEntities">Whether the implementation's query language resolves an unqualified name in a join against the attributes of the entities already in the from clause before it tries the entity names, where a run measured that it does: an entity join onto an entity whose name is also an attribute of a joined entity (<c>join POSTALADDRESS p</c> beside an entity with a column <c>POSTALADDRESS</c>) then joins the attribute, and a path through the alias fails. The builder names such an entity by its fully qualified class name, which the implementation resolves as the entity (measured over the FIBEN catalog, decision 149). Hibernate 7.4.5 resolves attributes first; EclipseLink 5.0.0 reads the entity name.</param>
/// <param name="AverageCasts">The conversions under which the implementation's average answers what SQL's AVG answers, where a run measured them (decision 161): the language hands every average over as a Double, and Hibernate averages a whole number in floating point besides, so the builder converts the average into the integer or the decimal SQL answers in. Null where none is measured, and a query with such an average then goes to native SQL.</param>
/// <param name="RebuildsHavingOnTheRoot">Whether the implementation rebuilds the having clause on the root of the block - the first variable of its from clause - when the first operand of the clause that carries a variable, read from the left, is a path over another one, where a run measured that it does: a path, bare or under an aggregate, an operation or a call, over an entity join, a further range variable or the enclosing block; constants and parameters carry none, and a CASE or a subquery met first leaves the clause as written. Rebuilt, every path of the clause names the root's attribute of that name, so the query is refused when it runs where the root has none (an invalid query key) and answers other rows without an error where it has one. The builder writes such a query in native SQL (decision 113). EclipseLink 5.0.0 rebuilds it (measured over the FIBEN catalog, decision 149, and pinned by the Java suite), Hibernate 7.4.5 reads every path as it stands.</param>
/// <param name="RefusesBinaryVersion">Whether the implementation refuses a version attribute over a binary type at deployment, where a run measured that it does (decision 179): the specification admits a version of an integral or a timestamp type only (Jakarta Persistence 3.2 §11.1.54), and a binary version - the row version of SQL Server, EF Core's <c>[Timestamp] byte[]</c> - is a value the database produces. The builder then writes the column read-only without <c>@Version</c> and reports the loss. EclipseLink 5.0.0 refuses it (<c>EclipseLink-7168</c>, measured by the experiment run of 2026-10-24 and pinned by the Java suite's BinaryVersionClaimsTest), Hibernate 7.4.5 accepts it.</param>
/// <param name="RefusesFullJoin">Whether the implementation's query language has no full outer join, where a run measured that its parser refuses one (decision 180): the specification knows the inner and the left join only (§4.4.5), the right and the full join are extensions. The visitor then reports the join as unspoken and the query goes out in native SQL (decision 113), which has the join. EclipseLink 5.0.0 refuses it with a syntax error (measured by the experiment run of 2026-10-24 and pinned by the Java suite's FullJoinClaimsTest), HQL of Hibernate 7.4.5 reads it.</param>
public sealed record JpaImplementationProfile(
    ORMEnum Implementation,
    string SpecificationLevel,
    PrimaryKeyStrategy AutoStrategy,
    int DefaultAllocationSize,
    string DefaultSequenceName,
    string NamingStrategy,
    bool NationalizedByDefault,
    bool UppercaseImplicitNames,
    string VendorAnnotationPackage,
    bool LazyReferenceNeedsWeaving = false,
    JpaCounterTable? DefaultCounterTable = null,
    bool NativeQueryExpandsCollection = false,
    bool BindsLiterals = false,
    string? InlineLiteralsCall = null,
    string? RecursionLimitCall = null,
    bool KeyThroughReferenceJoins = false,
    bool DropsJoinsInSubqueries = false,
    bool RefusesSetOperationsInSubqueries = false,
    bool InnerJoinsFollowOuterJoins = false,
    IReadOnlySet<string>? EntityNamesRefused = null,
    IReadOnlySet<string>? EntityNamesRefusedAsJoinTarget = null,
    bool DropsJoinsInSetOperations = false,
    bool ResolvesNamesAsAttributesBeforeEntities = false,
    bool RebuildsHavingOnTheRoot = false,
    JpaAverageCasts? AverageCasts = null,
    bool RefusesBinaryVersion = false,
    bool RefusesFullJoin = false);

/// <summary>
/// The type names a Jakarta Persistence implementation converts an average into so that it
/// answers what SQL's AVG answers (decision 161), each measured against SQL Server: an
/// integer, a long, and a decimal of SQL's precision and scale, the last a format with the
/// precision as <c>{0}</c> and the scale as <c>{1}</c>.
/// </summary>
/// <param name="Int">The integer: Hibernate 7.4.5 <c>Integer</c>, EclipseLink 5.0.0 <c>INTEGER</c>.</param>
/// <param name="Long">The long: Hibernate <c>Long</c>, EclipseLink <c>BIGINT</c>.</param>
/// <param name="Decimal">The decimal: Hibernate <c>BigDecimal({0},{1})</c>, EclipseLink <c>DECIMAL({0},{1})</c>.</param>
public sealed record JpaAverageCasts(string Int, string Long, string Decimal);

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
