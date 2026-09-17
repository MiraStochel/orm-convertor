using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using NHibernateWrappers;

namespace OrmConvertor.Factories;
internal static class EntityBuilderFactory
{
    static Dictionary<ORMEnum, Func<AbstractEntityBuilder>> Map =>
        new()
        {
            [ORMEnum.Dapper] = () => new DapperEntityBuilder(),
            [ORMEnum.NHibernate] = () => new NHibernateEntityBuilder(),
            [ORMEnum.EFCore] = () => new EFCoreEntityBuilder(),
            [ORMEnum.Hibernate] = () => new HibernateEntityBuilder(),
        };

    public static AbstractEntityBuilder? Create(ORMEnum orm) =>
        Map.TryGetValue(orm, out var ctor) ? ctor() : null;
}
