using AbstractWrappers;
using AbstractWrappers.Descriptors;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// Checks every generated artifact against the descriptor of its framework. The test that
/// was missing when the composite-key identity members were forgotten: the output was
/// syntactically valid and would not have run.
/// </summary>
public class EnforcedMembersTest
{
    private const string ClassName = "Sample";

    public static TheoryData<string, int> Cases()
    {
        var data = new TheoryData<string, int>();

        foreach (var framework in new[] { "Dapper", "EFCore", "NHibernate" })
        {
            foreach (var keyParts in new[] { 0, 1, 2 })
            {
                data.Add(framework, keyParts);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedArtifactSatisfiesTheDescriptor(string framework, int keyParts)
    {
        var builder = Create(framework);
        Populate(builder, keyParts);

        var code = builder.Build()
            .Single(output => output.ContentType == ConversionContentType.CSharpEntity)
            .Content;

        foreach (var member in builder.Descriptor.EnforcedMembersFor(builder.EntityMap))
        {
            var marker = EnforcedMember.Resolve(member.Marker, ClassName);
            if (marker is not null)
            {
                Assert.True(
                    code.Contains(marker, StringComparison.Ordinal),
                    $"{framework}: '{member.Name}' is missing. {member.Reason}");
            }

            var forbidden = EnforcedMember.Resolve(member.ForbiddenMarker, ClassName);
            if (forbidden is not null)
            {
                Assert.False(
                    code.Contains(forbidden, StringComparison.Ordinal),
                    $"{framework}: '{member.Name}' is violated. {member.Reason}");
            }
        }
    }

    /// <summary>
    /// The negative half: a member whose condition does not hold must not appear. Without
    /// it the test would pass on a builder that emits everything unconditionally.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedArtifactOmitsMembersWhoseConditionDoesNotHold(string framework, int keyParts)
    {
        var builder = Create(framework);
        Populate(builder, keyParts);

        var code = builder.Build()
            .Single(output => output.ContentType == ConversionContentType.CSharpEntity)
            .Content;

        var applicable = builder.Descriptor.EnforcedMembersFor(builder.EntityMap).ToHashSet();

        foreach (var member in builder.Descriptor.EnforcedMembers.Where(m => !applicable.Contains(m)))
        {
            var marker = EnforcedMember.Resolve(member.Marker, ClassName);
            if (marker is not null)
            {
                Assert.False(
                    code.Contains(marker, StringComparison.Ordinal),
                    $"{framework}: '{member.Name}' appears although its condition does not hold.");
            }
        }
    }

    /// <summary>
    /// Only the builder is created; its descriptor is read from it. Naming the descriptor
    /// here as well would put the framework-to-descriptor mapping in a second place, and a
    /// test that carries its own copy of what it verifies cannot catch the two drifting.
    /// </summary>
    private static AbstractEntityBuilder Create(string framework)
        => framework switch
        {
            "Dapper" => new DapperEntityBuilder(),
            "EFCore" => new EFCoreEntityBuilder(),
            "NHibernate" => new NHibernateEntityBuilder(),
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null),
        };

    private static void Populate(AbstractEntityBuilder builder, int keyParts)
    {
        builder.AddClassHeader("public", ClassName);
        builder.AddTable("Samples");
        builder.AddProperty("int", "PartOne", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "PartTwo", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("string", "Description", "public", hasGetter: true, hasSetter: true);

        if (keyParts == 1)
        {
            builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "PartOne");
        }
        else if (keyParts == 2)
        {
            builder.AddPrimaryKey(
            [
                ("PartOne", 1, PrimaryKeyStrategy.None),
                ("PartTwo", 2, PrimaryKeyStrategy.None),
            ]);
        }
    }
}