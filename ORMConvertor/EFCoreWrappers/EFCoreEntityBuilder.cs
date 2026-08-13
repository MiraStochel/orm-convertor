using AbstractWrappers;
using AbstractWrappers.Descriptors;
using Common.Convertors;
using EFCoreWrappers.Convertors;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using System.Text;

namespace EFCoreWrappers;

public class EFCoreEntityBuilder : AbstractEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor => EFCoreDescriptor.Instance;

    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.Entity.Namespace != null)
        {
            artifact.Code.AppendLine($"namespace {entityMap.Entity.Namespace};");
            artifact.Code.AppendLine();
        }

        // Both [PrimaryKey] and [Keyless] live in this namespace.
        if (entityMap.PrimaryKey is null || entityMap.PrimaryKey.Parts.Count > 1)
        {
            artifact.Code.AppendLine("using Microsoft.EntityFrameworkCore;");
        }

        artifact.Code.AppendLine("using System.ComponentModel.DataAnnotations;");
        artifact.Code.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");

        artifact.Code.AppendLine();
    }

    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.Table != null)
        {
            var schemaIfPresent = entityMap.Schema != null
                ? $", Schema = \"{entityMap.Schema}\""
                : string.Empty;

            artifact.Code.AppendLine($"[Table(\"{entityMap.Table}\"{schemaIfPresent})]");
        }

        if (entityMap.PrimaryKey is null)
        {
            // EF Core would otherwise derive a key by convention from a property named
            // Id or {TypeName}Id. The model holds no key, so none may appear in the output.
            artifact.Code.AppendLine("[Keyless]");
        }
        else if (entityMap.PrimaryKey.Parts.Count > 1)
        {
            var keyNames = string.Join(", ", entityMap.PrimaryKey.Parts.Select(p => $"nameof({p.PropertyMap.Property.Name})"));
            artifact.Code.AppendLine($"[PrimaryKey({keyNames})]");
        }

        var modifier = AccessModifierConvertor.ToModifierString(entityMap.Entity.AccessModifier);

        artifact.Code.AppendLine($"{modifier} class {entityMap.Entity.Name}");
        artifact.Code.AppendLine("{");
    }

    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.PrimaryKey is null)
        {
            return;
        }

        bool composite = entityMap.PrimaryKey.Parts.Count > 1;

        // TODO primary key strategy

        foreach (var part in entityMap.PrimaryKey.Parts)
        {
            var propertyMap = part.PropertyMap;
            bool nullable = propertyMap.IsNullable ?? false;

            // For a simple key [Key] goes on the property; for a composite key the class-level
            // [PrimaryKey(...)] attribute (see BuildTableSchema) defines it and [Key] is not emitted.
            artifact.Code.Append(BuildPropertyAttributes(propertyMap, isPrimaryKey: !composite));
            AppendKeyStrategyAttribute(artifact.Code, part, composite);
            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property, isPrimaryKey: true, nullable: nullable)}");
            artifact.Code.AppendLine();
        }
    }

    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var propertyMap in entityMap.PropertyMaps)
        {
            if (entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == propertyMap.Property.Name) == true)
            {
                continue; // handled in BuildPrimaryKey
            }

            if (entityMap.Relations.Any(r => r.SourceNavigationProperty == propertyMap.Property.Name))
            {
                continue; // navigation property - handled in BuildForeignKey
            }

            bool nullable = propertyMap.IsNullable ?? false;

            artifact.Code.Append(BuildPropertyAttributes(propertyMap));
            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property, nullable: nullable)}");
            artifact.Code.AppendLine();
        }
    }

    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var relation in entityMap.Relations)
        {
            var propertyMap = relation.SourceNavigationProperty is null
                ? null
                : entityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

            if (propertyMap is null)
            {
                continue; // a relation without a navigation property is not emitted into C# code
            }

            bool nullable = propertyMap.IsNullable ?? true;

            artifact.Code.Append(BuildPropertyAttributes(propertyMap));
            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property, nullable: nullable)}");
            artifact.Code.AppendLine();
        }
    }

    /// <summary>
    /// EF Core forces nothing onto the body of the class; its only enforced element is the
    /// keyless marker, which precedes the class header and is emitted in BuildTableSchema.
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

    /// <summary>
    /// Builds the property signature for C# code.
    /// Adds modifiers, type, name, getter/setter, and default value.
    /// </summary>
    private static string BuildPropertySignature(Property prop, bool isPrimaryKey = false, bool nullable = false)
    {
        var otherMods = new List<string>(prop.OtherModifiers ?? []);

        if (!nullable && !otherMods.Contains("required") && string.IsNullOrEmpty(prop.DefaultValue))
        {
            otherMods.Add("required");
        }

        var access = AccessModifierConvertor.ToModifierString(prop.AccessModifier);
        var modifiers = $"{access} {string.Join(' ', otherMods)}".Trim();

        var dbType = CLRTypeConvertor.ToString(prop.Type);
        var type = (!isPrimaryKey && prop.IsNullable) ? $"{dbType}?" : dbType;

        var getterSetter = (prop.HasGetter || prop.HasSetter)
            ? $" {{ {(prop.HasGetter ? "get;" : "")}{(prop.HasSetter ? " set;" : "")} }}"
            : "";
        var defaultVal = string.IsNullOrWhiteSpace(prop.DefaultValue)
            ? ""
            : $" = {prop.DefaultValue};";

        return $"{modifiers} {type} {prop.Name}{getterSetter}{defaultVal}";
    }

    /// <summary>
    /// Builds the property attributes for EF Core.
    /// </summary>
    private static string BuildPropertyAttributes(PropertyMap propMap, bool isPrimaryKey = false)
    {
        StringBuilder attributes = new();

        if (isPrimaryKey)
        {
            attributes.AppendLine($"    [Key]");
        }

        if (propMap.ColumnName != null || propMap.Type != null)
        {
            var parts = new List<string>();
            if (propMap.ColumnName != null)
            {
                parts.Add($"\"{propMap.ColumnName}\"");
            }

            if (propMap.Type.HasValue)
            {
                var typeText = DatabaseTypeConvertor.ToEFCore(propMap.Type.Value);
                parts.Add($"TypeName=\"{typeText}\"");
            }

            attributes.AppendLine($"    [Column({string.Join(", ", parts)})]");
        }


        if (propMap.Length != null)
        {
            attributes.AppendLine($"    [MaxLength({propMap.Length})]");
        }

        if (propMap.Precision != null)
        {
            var args = propMap.Scale != null
                ? $"{propMap.Precision}, {propMap.Scale}"
                : $"{propMap.Precision}";

            attributes.AppendLine($"    [Precision({args})]");
        }

        return attributes.ToString();
    }

    /// <summary>
    /// The strategy as an annotation. [DatabaseGenerated] can say two things: that the store
    /// produces the value on insert, and that nothing generates it. The named mechanisms -
    /// identity column, sequence, hi/lo - are fluent-only, so the model holds them and the
    /// annotation cannot; that narrowing is for diagnostics to report (decision 011).
    ///
    /// It is emitted only where it changes what EF Core would do anyway. Restating the target's
    /// own convention adds noise, while leaving it out where the convention disagrees flips the
    /// claim - a string key marked Auto would silently stop being generated.
    /// </summary>
    private static void AppendKeyStrategyAttribute(StringBuilder code, PrimaryKeyPart part, bool composite)
    {
        bool generatedByConvention = IsGeneratedByConvention(part, composite);

        var option = part.Strategy switch
        {
            PrimaryKeyStrategy.Assigned when generatedByConvention => "None",
            PrimaryKeyStrategy.Auto when !generatedByConvention => "Identity",
            _ => null,
        };

        if (option is not null)
        {
            code.AppendLine($"    [DatabaseGenerated(DatabaseGeneratedOption.{option})]");
        }
    }

    /// <summary>
    /// EF Core generates a value on its own for a single-property key of an integer type, and
    /// for a Guid key - which cannot reach this method at all, because the type model has no
    /// value for Guid and the parser throws on the property before the key is ever built.
    /// </summary>
    private static bool IsGeneratedByConvention(PrimaryKeyPart part, bool composite)
        => !composite
        && part.PropertyMap.Property.Type.CLRType is CLRType.Byte or CLRType.Int or CLRType.Long;
}