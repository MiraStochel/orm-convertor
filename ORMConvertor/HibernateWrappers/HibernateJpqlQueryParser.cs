using AbstractWrappers;
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
/// </summary>
public sealed class HibernateJpqlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    Model.SourceSqlDialect? declaredSourceDialect = null) : JpqlQueryParser(queryBuilders, declaredSourceDialect)
{
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
