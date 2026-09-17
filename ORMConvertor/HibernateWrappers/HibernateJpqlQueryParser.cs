using AbstractWrappers;
using JakartaPersistence;

namespace HibernateWrappers;

/// <summary>
/// Reads HQL as the superset of JPQL it is (decision 077): the shared JPQL reading plus
/// what HQL adds and the model has a place for - the fourth hook of decision 076. Here it
/// is the limit and offset clauses of HQL 6, which the representation carries as the
/// pagination of the scope (decision 060); what HQL adds and the model has no place for
/// falls to the shared parser's records.
/// </summary>
public sealed class HibernateJpqlQueryParser(AbstractQueryBuilder queryBuilder) : JpqlQueryParser(queryBuilder)
{
    protected override bool TryReadDialectClause()
    {
        long? limit = null, offset = null;
        var read = false;

        while (AtKeyword("limit") || AtKeyword("offset"))
        {
            if (TryConsumeKeyword("limit"))
            {
                if (limit is not null)
                {
                    throw Error("a second limit clause");
                }

                limit = ConsumeInteger();
            }
            else
            {
                ConsumeKeyword("offset");
                if (offset is not null)
                {
                    throw Error("a second offset clause");
                }

                offset = ConsumeInteger();
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
