using System.Reflection;
using System.Xml.Linq;
using AbstractWrappers.Descriptors;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

public class TargetFrameworkDescriptorTest
{
    private static readonly TargetFrameworkDescriptor[] AllDescriptors =
    [
        DapperDescriptor.Instance,
        EFCoreDescriptor.Instance,
        NHibernateDescriptor.Instance,
        HibernateWrappers.HibernateDescriptor.Instance,
        EclipseLinkWrappers.EclipseLinkDescriptor.Instance,
        MyBatisWrappers.MyBatisDescriptor.Instance,
    ];

    /// <summary>
    /// Adding a framework without adding its descriptor is exactly the kind of omission
    /// decision 009 exists to prevent, so the count is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void EveryFrameworkHasExactlyOneDescriptor()
    {
        var declared = AllDescriptors.Select(d => d.Framework).ToList();

        Assert.Equal(Enum.GetValues<ORMEnum>().Length, declared.Count);
        Assert.Equal(Enum.GetValues<ORMEnum>().OrderBy(f => f), declared.OrderBy(f => f));
    }

    /// <summary>
    /// The declared version must be the release the acceptance level of verification
    /// actually loads (decision 016) - the same pinned set as the table in
    /// architecture.md (decision 013). The informational version is compared because
    /// Dapper keeps its assembly version at 2.0.0.0 across releases; the "+commit"
    /// suffix is stripped before comparing.
    /// </summary>
    [Fact]
    public void DeclaredVersionsMatchTheVerificationPackages()
    {
        // global:: because the sibling test namespaces Tests.Dapper and Tests.NHibernate
        // shadow the package namespaces from inside Tests.Combined.
        Assert.Equal(PackageVersion(typeof(global::Dapper.SqlMapper).Assembly), DapperDescriptor.Instance.Version);
        Assert.Equal(PackageVersion(typeof(global::NHibernate.ISession).Assembly), NHibernateDescriptor.Instance.Version);
        Assert.Equal(PackageVersion(typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly), EFCoreDescriptor.Instance.Version);
    }

    /// <summary>
    /// The Java counterpart of the test above, without a JVM (decision 076): the version
    /// a Java descriptor declares must be the dependency the Java suite loads, and that
    /// dependency is pinned as a property of JavaTests/pom.xml. The pom is read as text -
    /// nothing here resolves or builds it - so a change of the pin on either side, or a
    /// renamed property, fails here rather than in a container run nobody was watching.
    /// </summary>
    [Fact]
    public void JavaSuiteDependenciesMatchTheJavaDescriptors()
    {
        var pom = XDocument.Load(Path.Combine(SolutionDirectory(), "JavaTests", "pom.xml"));
        var ns = pom.Root!.GetDefaultNamespace();
        var properties = pom.Root.Element(ns + "properties")!;

        Assert.Equal(HibernateWrappers.HibernateDescriptor.Instance.Version, properties.Element(ns + "hibernate.version")?.Value);
        Assert.Equal(EclipseLinkWrappers.EclipseLinkDescriptor.Instance.Version, properties.Element(ns + "eclipselink.version")?.Value);
        Assert.Equal(MyBatisWrappers.MyBatisDescriptor.Instance.Version, properties.Element(ns + "mybatis.version")?.Value);

        // The dependency must really be bound to that property; a literal version beside
        // an unused property would satisfy the assertion above and pin nothing.
        var hibernate = pom.Descendants(ns + "dependency")
            .Single(d => d.Element(ns + "artifactId")?.Value == "hibernate-core");
        Assert.Equal("${hibernate.version}", hibernate.Element(ns + "version")?.Value);

        var eclipseLink = pom.Descendants(ns + "dependency")
            .Single(d => d.Element(ns + "artifactId")?.Value == "eclipselink");
        Assert.Equal("${eclipselink.version}", eclipseLink.Element(ns + "version")?.Value);

        var myBatis = pom.Descendants(ns + "dependency")
            .Single(d => d.Element(ns + "artifactId")?.Value == "mybatis");
        Assert.Equal("${mybatis.version}", myBatis.Element(ns + "version")?.Value);
    }

    /// <summary>
    /// The two implementations of one specification declare the same members and the same
    /// support, and differ only in what stands beside the descriptor: the profile
    /// (decisions 076 and 080). If a difference ever appeared in the descriptor itself, the
    /// shared layer would have stopped being shared and this test says so.
    /// </summary>
    [Fact]
    public void TheJpaImplementationsShareEverythingButTheirProfileAndVersion()
    {
        var hibernate = HibernateWrappers.HibernateDescriptor.Instance;
        var eclipseLink = EclipseLinkWrappers.EclipseLinkDescriptor.Instance;

        Assert.Same(hibernate.EnforcedMembers, eclipseLink.EnforcedMembers);
        Assert.Same(hibernate.Support, eclipseLink.Support);
        Assert.Same(hibernate.QuerySupport, eclipseLink.QuerySupport);
        Assert.NotEqual(hibernate.Version, eclipseLink.Version);

        var hibernateProfile = HibernateWrappers.HibernateDescriptor.Profile;
        var eclipseLinkProfile = EclipseLinkWrappers.EclipseLinkDescriptor.Profile;

        Assert.Equal(hibernateProfile.SpecificationLevel, eclipseLinkProfile.SpecificationLevel);
        Assert.NotEqual(hibernateProfile.AutoStrategy, eclipseLinkProfile.AutoStrategy);
        Assert.False(hibernateProfile.UppercaseImplicitNames);
        Assert.True(eclipseLinkProfile.UppercaseImplicitNames);
        Assert.False(hibernateProfile.LazyReferenceNeedsWeaving);
        Assert.True(eclipseLinkProfile.LazyReferenceNeedsWeaving);
    }

    /// <summary>
    /// The solution directory, found by walking up from the test assembly: on the host
    /// that is the checkout, in the container the copy the tests stage was built from.
    /// </summary>
    private static string SolutionDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ORMConvertor.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("ORMConvertor.sln was not found above " + AppContext.BaseDirectory);
    }

    private static string PackageVersion(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var metadata = informational.IndexOf('+');
        return metadata < 0 ? informational : informational[..metadata];
    }

    [Fact]
    public void DapperExpressesNoMappingFactAndImposesNothing()
    {
        var descriptor = DapperDescriptor.Instance;

        Assert.Empty(descriptor.EnforcedMembers);
        Assert.All(
            Enum.GetValues<MappingFactCategory>(),
            category => Assert.Equal(FactSupport.NotExpressible, descriptor.SupportOf(category)));
    }

    /// <summary>
    /// The two full .NET ORMs part company on exactly one category: NHibernate refuses a
    /// mapping without an identifier, EF Core falls back to a keyless type. Both JPA
    /// implementations stand with NHibernate: every JPA entity has an @Id (decisions 077
    /// and 080), and it is the specification saying so, not either of them.
    /// </summary>
    [Fact]
    public void OnlyNHibernateAndTheJpaImplementationsRequireAPrimaryKey()
    {
        Assert.Equal(FactSupport.Required, NHibernateDescriptor.Instance.SupportOf(MappingFactCategory.PrimaryKey));
        Assert.Equal(FactSupport.Required, HibernateWrappers.HibernateDescriptor.Instance.SupportOf(MappingFactCategory.PrimaryKey));
        Assert.Equal(FactSupport.Required, EclipseLinkWrappers.EclipseLinkDescriptor.Instance.SupportOf(MappingFactCategory.PrimaryKey));
        Assert.Equal(FactSupport.Expressible, EFCoreDescriptor.Instance.SupportOf(MappingFactCategory.PrimaryKey));
        Assert.Equal(FactSupport.NotExpressible, DapperDescriptor.Instance.SupportOf(MappingFactCategory.PrimaryKey));
    }

    [Fact]
    public void DescriptorRejectsAnIncompleteSupportTable()
    {
        var incomplete = new Dictionary<MappingFactCategory, FactSupport>
        {
            [MappingFactCategory.TableName] = FactSupport.Expressible,
        };

        Assert.Throws<ArgumentException>(() => _ = new TargetFrameworkDescriptor
        {
            Framework = ORMEnum.Dapper,
            Version = DapperDescriptor.Instance.Version,
            Support = incomplete,
            QuerySupport = DapperDescriptor.Instance.QuerySupport,
        });
    }

    [Fact]
    public void DescriptorRejectsAnIncompleteQuerySupportTable()
    {
        // The same gate as for mapping facts (decision 022): a missing query feature has to
        // fail loudly, because a capability report that defaults to silence reports nothing.
        var incomplete = new Dictionary<QueryFeature, FactSupport>
        {
            [QueryFeature.Projection] = FactSupport.Expressible,
        };

        Assert.Throws<ArgumentException>(() => _ = new TargetFrameworkDescriptor
        {
            Framework = ORMEnum.Dapper,
            Version = DapperDescriptor.Instance.Version,
            Support = DapperDescriptor.Instance.Support,
            QuerySupport = incomplete,
        });
    }

    [Fact]
    public void DescriptorRejectsAMemberThatAssertsNothing()
    {
        Assert.Throws<InvalidOperationException>(() => _ = new TargetFrameworkDescriptor
        {
            Framework = ORMEnum.Dapper,
            Version = DapperDescriptor.Instance.Version,
            Support = DapperDescriptor.Instance.Support,
            QuerySupport = DapperDescriptor.Instance.QuerySupport,
            EnforcedMembers =
            [
                new EnforcedMember
                {
                    Name = "states nothing",
                    Condition = EnforcedMemberCondition.Always,
                    Reason = "-",
                },
            ],
        });
    }

    [Theory]
    [InlineData(0, 5)] // no key: virtual, non-sealed, parameterless constructor, both collection interfaces
    [InlineData(1, 5)] // simple key: the same five
    [InlineData(2, 8)] // composite key: plus [Serializable], Equals, GetHashCode
    public void NHibernateEnforcedMembersFollowTheShapeOfTheKey(int keyParts, int expected)
    {
        var entityMap = EntityMapWithKeyParts(keyParts);

        var applicable = NHibernateDescriptor.Instance.EnforcedMembersFor(entityMap).ToList();

        Assert.Equal(expected, applicable.Count);
    }

    [Theory]
    [InlineData(0, 1)] // no key: the keyless marker applies
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    public void EFCoreEnforcesTheKeylessMarkerOnlyWithoutAKey(int keyParts, int expected)
    {
        var entityMap = EntityMapWithKeyParts(keyParts);

        var applicable = EFCoreDescriptor.Instance.EnforcedMembersFor(entityMap).ToList();

        Assert.Equal(expected, applicable.Count);
    }

    private static EntityMap EntityMapWithKeyParts(int keyParts)
    {
        var builder = new DummyEntityBuilder();
        builder.AddClassHeader("public", "Sample");
        builder.AddProperty("int", "PartOne", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "PartTwo", "public", hasGetter: true, hasSetter: true);

        if (keyParts == 1)
        {
            builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "PartOne");
        }
        else if (keyParts == 2)
        {
            builder.AddPrimaryKey(
            [
                ("PartOne", 1, PrimaryKeyStrategy.Assigned),
                ("PartTwo", 2, PrimaryKeyStrategy.Assigned),
            ]);
        }

        return builder.EntityMap;
    }
}