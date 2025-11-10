using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace OrmConvertor.Factories;
internal class ParserFactory
{
    public static List<IParser> Create(ORMEnum orm, AbstractEntityBuilder eb, AbstractQueryBuilder? qb)
    {
        return orm switch
        {
            ORMEnum.Dapper => new List<IParser>
            {
                new DapperEntityParser(eb)
            },
            ORMEnum.NHibernate => new List<IParser>
            {
                new NHibernateEntityParser(eb),
                new NHibernateXMLMappingParser(eb)
            },
            ORMEnum.EFCore =>
                qb is null
                    ? new List<IParser> { new EFCoreEntityParser(eb) }
                    : new List<IParser> { new EFCoreEntityParser(eb), new EFCoreLinqQueryParser(qb) },
            _ => []
        };
    }
}
