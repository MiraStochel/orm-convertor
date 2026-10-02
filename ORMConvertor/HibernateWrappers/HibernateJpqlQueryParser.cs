using AbstractWrappers;
using AbstractWrappers.Descriptors;
using JakartaPersistence;
using Model.QueryInstructions;

namespace HibernateWrappers;

/// <summary>
/// Reads HQL as the superset of JPQL it is (decision 077): the shared JPQL reading plus
/// what HQL adds and the model has a place for - the fourth hook of decision 076. Here it
/// is the limit and offset clauses of HQL 6, which the representation carries as the
/// pagination of the scope (decision 060); what HQL adds and the model has no place for
/// falls to the shared parser's records.
///
/// Both clauses take a number or a parameter, which is the shape paged HQL is written in,
/// and the representation carries either (decision 085).
///
/// HQL also reads a query as a source of rows - the with clause and a subquery in from or
/// after join -, which the representation carries as a named intermediate result
/// (decision 112), so the profile turns the shared reading of it on; and the functions HQL
/// adds that the vocabulary carries since decision 113 - timestampadd and timestampdiff, the
/// ranking functions over a window and listagg -, which it turns on the same way.
///
/// What Hibernate adds to the query object (SelectionQuery, Query and NativeQuery of 7.4)
/// is the other half of its profile: three calls change the rows - a page, a keyed page and
/// a count in place of the rows -, and are refused; the transformers and the result mapping
/// of a native query say how rows are materialized; its own lock calls lock; and its own
/// ways of binding a parameter and of running the query say nothing the artifact would lose.
/// </summary>
public sealed class HibernateJpqlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    Model.SourceSqlDialect? declaredSourceDialect = null) : JpqlQueryParser(queryBuilders, declaredSourceDialect)
{
    private static readonly Dictionary<string, QueryObjectCall> Calls = new(StringComparer.Ordinal)
    {
        ["setParameterList"] = new(QueryObjectCallKind.Binds),
        ["setProperties"] = new(QueryObjectCallKind.Binds),
        ["list"] = new(QueryObjectCallKind.Runs, Leaves: true),
        ["stream"] = new(QueryObjectCallKind.Runs, Leaves: true),
        ["scroll"] = new(QueryObjectCallKind.Runs, Leaves: true),
        ["uniqueResult"] = new(QueryObjectCallKind.Runs, Leaves: true),
        ["uniqueResultOptional"] = new(QueryObjectCallKind.Runs, Leaves: true),
        ["setPage"] = new(QueryObjectCallKind.ChangesRows, "sets the page of the result the query returns", QueryFeature.Pagination),
        ["getKeyedResultList"] = new(QueryObjectCallKind.ChangesRows, "runs the query for a page keyed by the last row of the page before it", QueryFeature.Pagination, Leaves: true),
        ["getResultCount"] = new(QueryObjectCallKind.ChangesRows, "runs a count of the query's rows in place of the rows", QueryFeature.Projection, Leaves: true),
        ["setTupleTransformer"] = new(QueryObjectCallKind.MapsResult),
        ["setResultListTransformer"] = new(QueryObjectCallKind.MapsResult),
        ["setResultTransformer"] = new(QueryObjectCallKind.MapsResult),
        ["addScalar"] = new(QueryObjectCallKind.MapsResult),
        ["addEntity"] = new(QueryObjectCallKind.MapsResult),
        ["addJoin"] = new(QueryObjectCallKind.MapsResult),
        ["addAttributeResult"] = new(QueryObjectCallKind.MapsResult),
        ["addRoot"] = new(QueryObjectCallKind.MapsResult, Leaves: true),
        ["addFetch"] = new(QueryObjectCallKind.MapsResult, Leaves: true),
        ["addInstantiation"] = new(QueryObjectCallKind.MapsResult, Leaves: true),
        ["setHibernateLockMode"] = new(QueryObjectCallKind.Locks),
        ["setLockOptions"] = new(QueryObjectCallKind.Locks),
        ["setLockScope"] = new(QueryObjectCallKind.Locks),
        ["setFollowOnLocking"] = new(QueryObjectCallKind.Locks),
        ["setFollowOnStrategy"] = new(QueryObjectCallKind.Locks),
    };

    protected override IReadOnlyDictionary<string, QueryObjectCall> ImplementationCalls => Calls;

    protected override bool ReadsIntermediateResults => true;

    protected override bool ReadsHqlFunctions => true;

    protected override bool TryReadDialectClause()
    {
        RowCount? limit = null, offset = null;
        var read = false;

        while (AtKeyword("limit") || AtKeyword("offset"))
        {
            if (TryConsumeKeyword("limit"))
            {
                if (limit is not null)
                {
                    throw Error("a second limit clause");
                }

                limit = ConsumeRowCount();
            }
            else
            {
                ConsumeKeyword("offset");
                if (offset is not null)
                {
                    throw Error("a second offset clause");
                }

                offset = ConsumeRowCount();
                TryConsumeKeyword("rows");
            }

            read = true;
        }

        if (read)
        {
            queryBuilder.Paginate(offset, limit);
        }

        return read;
    }
}
