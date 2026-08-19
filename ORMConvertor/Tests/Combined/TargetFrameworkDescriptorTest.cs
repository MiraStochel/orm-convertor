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
    /// The two full ORMs part company on exactly one category: NHibernate refuses a
    /// mapping without an identifier, EF Core falls back to a keyless type.
    /// </summary>
    [Fact]
    public void OnlyNHibernateRequiresAPrimaryKey()
    {
        Assert.Equal(FactSupport.Required, NHibernateDescriptor.Instance.SupportOf(MappingFactCategory.PrimaryKey));
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
    [InlineData(0, 3)] // no key: virtual, non-sealed, parameterless constructor
    [InlineData(1, 3)] // simple key: the same three
    [InlineData(2, 6)] // composite key: plus [Serializable], Equals, GetHashCode
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