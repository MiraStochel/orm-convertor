using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace OrmConvertor.Factories;
internal class ParserFactory
{
    /// <summary>
    /// Parsers for one source framework. Query parsers appear only when a query builder is
    /// there to receive them; within one framework each query parser claims a different
    /// query language, so the caller can pick by content type instead of by list order
    /// (decision 025).
    /// </summary>
    public static List<IParser> Create(ORMEnum orm, AbstractEntityBuilder eb, AbstractQueryBuilder? qb)
    {
        return orm switch
        {
            ORMEnum.Dapper => qb is null
                ? [new DapperEntityParser(eb)]
                : [new DapperEntityParser(eb), new DapperSqlQueryParser(qb)],

            ORMEnum.NHibernate => qb is null
                ? [new NHibernateEntityParser(eb), new NHibernateXMLMappingParser(eb)]
                : [new NHibernateEntityParser(eb), new NHibernateXMLMappingParser(eb), new NHibernateLinqQueryParser(qb)],

            ORMEnum.EFCore => qb is null
                ? [new EFCoreEntityParser(eb)]
                : [new EFCoreEntityParser(eb), new EFCoreLinqQueryParser(qb)],

            // Symmetric with the target side, which refuses an unsupported framework rather
            // than returning nothing: an empty parser list produced an empty result and no
            // error at all, so a bad source framework looked like a source with no entities.
            _ => throw new InvalidOperationException("Source ORM not supported"),
        };
    }
}
