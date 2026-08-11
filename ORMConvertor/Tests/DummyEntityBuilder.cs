using AbstractWrappers;
using AbstractWrappers.Descriptors;
using Model;
using Model.AbstractRepresentation;

namespace Tests;

/// <summary>
/// Collects the intermediate representation for parser tests that never generate output.
/// </summary>
public class DummyEntityBuilder : AbstractEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor { get; } = new()
    {
        Framework = ORMEnum.Dapper,
        EnforcedMembers = [],
        Support = Enum.GetValues<MappingFactCategory>()
            .ToDictionary(category => category, _ => FactSupport.NotExpressible),
    };

    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact)
        => throw new NotSupportedException("DummyEntityBuilder collects the model; it does not generate output.");
}