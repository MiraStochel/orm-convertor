using AbstractWrappers.Descriptors;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace OrmConvertor.Factories;

/// <summary>
/// Descriptor of a framework without instantiating its builder. Decision 009 made the
/// descriptor readable on its own precisely for consumers like this one: the run record
/// (S6) states the source framework's version and there is no source-side builder to ask.
/// </summary>
internal static class DescriptorFactory
{
    public static TargetFrameworkDescriptor Create(ORMEnum orm) => orm switch
    {
        ORMEnum.Dapper => DapperDescriptor.Instance,
        ORMEnum.NHibernate => NHibernateDescriptor.Instance,
        ORMEnum.EFCore => EFCoreDescriptor.Instance,
        _ => throw new InvalidOperationException("Source ORM not supported"),
    };
}
