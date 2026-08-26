using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace OrmConvertor.Factories;
internal class ParserFactory
{
    /// <summary>
    /// Parsers for one source framework, in the reading order that is a stated fact of
    /// the framework, not an accident of the list (decision 017). The order follows the
    /// precedence the framework documents for its own artifacts, strongest first; where
    /// it documents none, the default holds: the input text - the entity class, level
    /// 1a - parses before its auxiliary mapping artifacts - level 1b, for NHibernate
    /// the hbm.xml (decision 068). Every list below is the default order; the first
    /// fluent configuration parser will enter the EF Core list first, because EF Core
    /// puts the fluent API above annotations. The orchestration runs each parser over
    /// all its units before the next one starts, so a fact of a higher level is already
    /// in place when a lower level arrives, and the builder can keep the first value
    /// and report a conflict without the model tracking the origin of a fact.
    ///
    /// Query parsers appear only when a query builder is there to receive them; within
    /// one framework each query parser claims a different query language, so the caller
    /// can pick by content type instead of by list order (decision 025).
    /// </summary>
    public static List<IParser> Create(ORMEnum orm, AbstractEntityBuilder eb, AbstractQueryBuilder? qb)
    {
        return orm switch
        {
            ORMEnum.Dapper => qb is null
                ? [new DapperEntityParser(eb)]
                : [new DapperEntityParser(eb), new DapperSqlQueryParser(qb)],

            // The entity parser stands before the XML mapping parser as a rule, not as a
            // coincidence: swapping the two would invert the source precedence (decision 017).
            ORMEnum.NHibernate => qb is null
                ? [new NHibernateEntityParser(eb), new NHibernateXMLMappingParser(eb)]
                : [new NHibernateEntityParser(eb), new NHibernateXMLMappingParser(eb), new NHibernateLinqQueryParser(qb), new NHibernateHqlQueryParser(qb)],

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
