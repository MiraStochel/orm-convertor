using AbstractWrappers;
using DapperWrappers;
using EclipseLinkWrappers;
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
            [ORMEnum.EclipseLink] = () => new EclipseLinkJpqlQueryBuilder(),
        };

    public static AbstractQueryBuilder? Create(ORMEnum orm) =>
        Map.TryGetValue(orm, out var ctor) ? ctor() : null;

    /// <summary>
    /// Whether the target can receive a query at all, asked without making a builder: the
    /// orchestration has to know it before it offers a unit to the query pass, and the
    /// builders themselves are made one per query (decision 081).
    /// </summary>
    public static bool Supports(ORMEnum orm) => Map.ContainsKey(orm);
}
