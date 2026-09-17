using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
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
            [ORMEnum.EFCore] = () => new EFCoreLinqQueryBuilder(),
            [ORMEnum.Hibernate] = () => new HibernateJpqlQueryBuilder(),
        };

    public static AbstractQueryBuilder? Create(ORMEnum orm) =>
        Map.TryGetValue(orm, out var ctor) ? ctor() : null;
}
