using AbstractWrappers;
using JakartaPersistence;

namespace EclipseLinkWrappers;

/// <summary>
/// Reads the query language of EclipseLink, which is JPQL and the fourth hook of decision
/// 076 left empty (decision 080). EclipseLink Query Language is a superset of JPQL like
/// HQL, but what it adds is either already read by the shared parser - the join of
/// unrelated entities with ON, which the builder emits for both implementations - or has
/// no place in the model at all: FUNC, OPERATOR, SQL and COLUMN reach past the mapping
/// into the database, and a subquery in FROM is a shape the representation does not carry.
/// So nothing is overridden, and a construct outside JPQL meets the shared parser's rule
/// instead: a Failure with its line and column, never a guess (decisions 062 and 070).
///
/// The empty body is a statement about EclipseLink, not an omission - the same kind of
/// empty step the builder template method has (decision 016), and the pagination HQL
/// spells inside the query text has no counterpart here: EQL has no limit clause and the
/// shared builder puts the window on the query object, where the specification puts it.
/// </summary>
public sealed class EclipseLinkJpqlQueryParser(AbstractQueryBuilder queryBuilder) : JpqlQueryParser(queryBuilder);
