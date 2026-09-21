using AbstractWrappers.Descriptors;
using DapperWrappers;
using EclipseLinkWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using MyBatisWrappers;
using NHibernateWrappers;

namespace Tests;

/// <summary>
/// The six descriptors in one place, because more than one test area asks them the same
/// question. Orchestration reaches them through its own internal <c>DescriptorFactory</c>
/// (decision 009) and the suite does not borrow it: a test that went through the factory
/// would stop seeing a descriptor the factory forgot, which is the omission
/// <see cref="Tests.Combined.TargetFrameworkDescriptorTest"/> exists to catch.
/// </summary>
internal static class FrameworkDescriptors
{
    public static IReadOnlyList<TargetFrameworkDescriptor> All { get; } =
    [
        DapperDescriptor.Instance,
        EFCoreDescriptor.Instance,
        NHibernateDescriptor.Instance,
        HibernateDescriptor.Instance,
        EclipseLinkDescriptor.Instance,
        MyBatisDescriptor.Instance,
    ];

    /// <summary>
    /// The ecosystem a framework's artifacts belong to, as its own descriptor declares it
    /// (decision 090). Never a list written here: a seventh framework would not be on it
    /// and the boundary F10 counts across would quietly keep its old shape.
    /// </summary>
    public static Ecosystem EcosystemOf(ORMEnum framework)
        => All.Single(descriptor => descriptor.Framework == framework).Ecosystem;

    /// <summary>Whether a direction crosses the boundary between the two ecosystems.</summary>
    public static bool CrossEcosystem(ORMEnum source, ORMEnum target)
        => EcosystemOf(source) != EcosystemOf(target);
}
