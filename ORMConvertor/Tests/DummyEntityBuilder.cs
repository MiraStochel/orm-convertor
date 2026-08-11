using AbstractWrappers;
using AbstractWrappers.Descriptors;
using DapperWrappers;
using Model;
using Model.AbstractRepresentation;

namespace Tests;

/// <summary>
/// Collects the intermediate representation for parser tests that never generate output.
/// </summary>
public class DummyEntityBuilder : AbstractEntityBuilder
{
    /// <summary>
    /// Borrowed from Dapper: a framework that expresses no mapping fact and imposes nothing
    /// says exactly what this builder is. Constructing a second descriptor with the same
    /// content would only mean one more place to keep in step.
    /// </summary>
    public override TargetFrameworkDescriptor Descriptor => DapperDescriptor.Instance;

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