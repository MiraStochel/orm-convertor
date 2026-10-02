using AbstractWrappers;
using AbstractWrappers.Descriptors;
using JakartaPersistence;

namespace EclipseLinkWrappers;

/// <summary>
/// Reads the query language of EclipseLink, which is JPQL and the fourth hook of decision
/// 076 left empty (decision 080). EclipseLink Query Language is a superset of JPQL like
/// HQL, but what it adds is either already read by the shared parser - the join of
/// unrelated entities with ON, which the builder emits for both implementations - or has
/// no place in the model at all: FUNC, OPERATOR, SQL and COLUMN reach past the mapping
/// into the database, and a subquery in FROM stands only as a comma-separated declaration,
/// a cross join the representation does not carry, although it carries the subquery itself
/// as an intermediate result (decision 112).
/// So nothing of the text is overridden, and a construct outside JPQL meets the shared
/// parser's rule instead: a Failure with its line and column, never a guess (decisions 062
/// and 070).
///
/// The empty dialect hook is a statement about EclipseLink, not an omission - the same kind
/// of empty step the builder template method has (decision 016), and the pagination HQL
/// spells inside the query text has no counterpart here: EQL has no limit clause and the
/// shared builder puts the window on the query object, where the specification puts it.
///
/// What EclipseLink adds to the query object, it adds as hints (QueryHints of 5.0), and five
/// of them change the rows: two slice the result on the JDBC statement, two read the rows as
/// they stood in the past, and one turns the query into another kind of EclipseLink query.
/// Those are refused; the result type, which says how rows are materialized, and the
/// pessimistic lock are losses with their reasons; every other hint is a loss.
/// </summary>
public sealed class EclipseLinkJpqlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    Model.SourceSqlDialect? declaredSourceDialect = null) : JpqlQueryParser(queryBuilders, declaredSourceDialect)
{
    /// <summary>Each hint under its key and under the name of the QueryHints constant that spells it.</summary>
    private static readonly Dictionary<string, QueryObjectCall> Hints = new(StringComparer.Ordinal)
    {
        ["eclipselink.jdbc.max-rows"] = new(QueryObjectCallKind.ChangesRows, "caps the rows the JDBC statement returns", QueryFeature.Pagination),
        ["JDBC_MAX_ROWS"] = new(QueryObjectCallKind.ChangesRows, "caps the rows the JDBC statement returns", QueryFeature.Pagination),
        ["eclipselink.jdbc.first-result"] = new(QueryObjectCallKind.ChangesRows, "skips the first rows of the JDBC result", QueryFeature.Pagination),
        ["JDBC_FIRST_RESULT"] = new(QueryObjectCallKind.ChangesRows, "skips the first rows of the JDBC result", QueryFeature.Pagination),
        ["eclipselink.history.as-of"] = new(QueryObjectCallKind.ChangesRows, "reads the rows as they stood at a point in the past"),
        ["AS_OF"] = new(QueryObjectCallKind.ChangesRows, "reads the rows as they stood at a point in the past"),
        ["eclipselink.history.as-of.scn"] = new(QueryObjectCallKind.ChangesRows, "reads the rows as they stood at a point in the past"),
        ["AS_OF_SCN"] = new(QueryObjectCallKind.ChangesRows, "reads the rows as they stood at a point in the past"),
        ["eclipselink.query-type"] = new(QueryObjectCallKind.ChangesRows, "turns the query into another kind of EclipseLink query, which may read one object or rows of values"),
        ["QUERY_TYPE"] = new(QueryObjectCallKind.ChangesRows, "turns the query into another kind of EclipseLink query, which may read one object or rows of values"),
        ["eclipselink.result-type"] = new(QueryObjectCallKind.MapsResult),
        ["RESULT_TYPE"] = new(QueryObjectCallKind.MapsResult),
        ["eclipselink.pessimistic-lock"] = new(QueryObjectCallKind.Locks),
        ["PESSIMISTIC_LOCK"] = new(QueryObjectCallKind.Locks),
    };

    protected override IReadOnlyDictionary<string, QueryObjectCall> ImplementationHints => Hints;
}
