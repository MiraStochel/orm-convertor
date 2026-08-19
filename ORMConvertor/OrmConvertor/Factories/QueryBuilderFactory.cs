using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace OrmConvertor.Factories;
internal static class QueryBuilderFactory
{
    static Dictionary<ORMEnum, Func<AbstractQueryBuilder?>> Map =>
        new()
        {
            [ORMEnum.Dapper] = () => new DapperSqlQueryBuilder(),
            [ORMEnum.NHibernate] = () => new NHibernateHqlQueryBuilder(),
            [ORMEnum.EFCore] = () => new EFCoreLinqQueryBuilder()
        };

    public static AbstractQueryBuilder? Create(ORMEnum orm) =>
        Map.TryGetValue(orm, out var ctor) ? ctor() : null;
}
