using AbstractWrappers;
using AbstractWrappers.Descriptors;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
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

        // Written by hand on purpose, unlike the cross tests that take their directions from
        // ORMEnum through CrossFrameworkInputs: this matrix is the contract of decision 037
        // and a framework's row belongs to its wrapper, so the row is added, not derived.
        foreach (var framework in new[] { "Dapper", "EFCore", "NHibernate", "Hibernate" })
        {
            foreach (var keyParts in new[] { 0, 1, 2 })
            {
                if (framework is "NHibernate" or "Hibernate" && keyParts == 0)
                {
                    // NHibernate and Hibernate require an identifier: the completeness gate
                    // refuses the entity instead of generating anything (decision 010), so
                    // there is no artifact to check here. The refusal itself is asserted in
                    // DiagnosticsTest and in the Hibernate tests.
                    continue;
                }

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

        var code = EntityArtifact(builder);

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

        var code = EntityArtifact(builder);

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
            "Hibernate" => new HibernateEntityBuilder(),
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null),
        };

    /// <summary>The entity class artifact, in whichever language the framework writes it.</summary>
    private static string EntityArtifact(AbstractEntityBuilder builder)
        => builder.Build()
            .Single(output => output.ContentType is ConversionContentType.CSharpEntity or ConversionContentType.JavaEntity)
            .Content;

    private static void Populate(AbstractEntityBuilder builder, int keyParts)
    {
        builder.AddClassHeader("public", ClassName);
        builder.AddTable("Samples");
        builder.AddProperty("int", "PartOne", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "PartTwo", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("string", "Description", "public", hasGetter: true, hasSetter: true);

        // Exercises the forbidden concrete-collection markers of decision 035: without a
        // collection in the artifact they would be satisfied by absence, not by the builder.
        builder.AddProperty("List<Item>", "Items", "public", hasGetter: true, hasSetter: true, defaultValue: "[]");
        builder.AddProperty("HashSet<Item>", "Tags", "public", hasGetter: true, hasSetter: true, defaultValue: "[]");

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
    }
}