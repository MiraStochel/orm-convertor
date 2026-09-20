using AbstractWrappers;
using DapperWrappers;
using EclipseLinkWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using MyBatisWrappers;
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
            [ORMEnum.EclipseLink] = () => new EclipseLinkEntityBuilder(),
            [ORMEnum.MyBatis] = () => new MyBatisEntityBuilder(),
        };

    public static AbstractEntityBuilder? Create(ORMEnum orm) =>
        Map.TryGetValue(orm, out var ctor) ? ctor() : null;
}
