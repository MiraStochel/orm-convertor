using AbstractWrappers;
using AbstractWrappers.Descriptors;
using Common.Convertors;
using Model;
using Model.AbstractRepresentation;

namespace DapperWrappers;

public class DapperEntityBuilder : AbstractEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor => DapperDescriptor.Instance;

    /// <summary>
    /// Dapper needs no imports for the entity; only the namespace is emitted.
    /// </summary>
    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.Entity.Namespace != null)
        {
            artifact.Code.AppendLine($"namespace {entityMap.Entity.Namespace};");
            artifact.Code.AppendLine();
        }
    }

    /// <summary>
    /// Dapper records no table or schema; only the class header is emitted.
    /// </summary>
    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
        var modifier = AccessModifierConvertor.ToModifierString(entityMap.Entity.AccessModifier);

        artifact.Code.AppendLine($"{modifier} class {entityMap.Entity.Name}");
        artifact.Code.AppendLine("{");
    }

    /// <summary>
    /// Dapper has no mechanism for keys. Per decision 004 the fact is reported rather than
    /// approximated, so nothing is emitted.
    /// </summary>
    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    /// <summary>
    /// Dapper has no mechanism for relations; joins are written by hand in SQL.
    /// </summary>
    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var property in entityMap.Entity.Properties)
        {
            var modifiers = $"{AccessModifierConvertor.ToModifierString(property.AccessModifier)} {string.Join(' ', property.OtherModifiers)}".Trim();
            var clrType = CLRTypeConvertor.ToString(property.Type);
            var type = property.IsNullable ? $"{clrType}?" : clrType;

            var getterSetter = (property.HasGetter || property.HasSetter)
                ? $" {{ {(property.HasGetter ? "get; " : string.Empty)}{(property.HasSetter ? "set; " : string.Empty)}}}"
                : string.Empty;

            var defaultValue = string.IsNullOrWhiteSpace(property.DefaultValue)
                ? string.Empty
                : $" = {property.DefaultValue};";

            artifact.Code.AppendLine($"    {modifiers} {type} {property.Name}{getterSetter}{defaultValue}");
            artifact.Code.AppendLine();
        }
    }

    /// <summary>
    /// Dapper imposes nothing on the generated class - the descriptor declares no members.
    /// </summary>
    protected override void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact)
    {
        artifact.Code.AppendLine("}");

        yield return new ConversionSource
        {
            ContentType = ConversionContentType.CSharpEntity,
            Content = artifact.Code.ToString()
        };
    }
}