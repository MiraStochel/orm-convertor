using AbstractWrappers;
using Common.Convertors;
using EFCoreWrappers.Convertors;
using Model;
using Model.AbstractRepresentation;
using System.Data;
using System.Text;

namespace EFCoreWrappers;

public class EFCoreEntityBuilder : AbstractEntityBuilder
{
    /// <summary>
    /// Builds one EF Core entity class per accumulated entity.
    /// </summary>
    public override List<ConversionSource> Build()
    {
        var outputs = new List<ConversionSource>();
        foreach (var em in EntityMaps)
        {
            var codeResult = new StringBuilder();
            BuildImports(em, codeResult);
            BuildTableSchema(em, codeResult);
            BuildPrimaryKey(em, codeResult);
            BuildProperties(em, codeResult);
            BuildForeignKey(em, codeResult);
            FinalizeBuild(codeResult);

            outputs.Add(new ConversionSource
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = codeResult.ToString()
            });
        }
        return outputs;
    }

    protected override void BuildForeignKey()
    {
        // unused in multi-entity flow
    }

    private static void BuildForeignKey(EntityMap em, StringBuilder codeResult)
    {
        var foreignKeyPropertyMaps = em.PropertyMaps
            .Where(pm => pm.OtherDatabaseProperties.TryGetValue("IsForeignKey", out var v) &&
                     string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));

        foreach (var propertyMap in foreignKeyPropertyMaps)
        {
            var rel = propertyMap.Relation;
            bool nullable = propertyMap.IsNullable ?? true;

            codeResult.Append(BuildPropertyAttributes(propertyMap));
            codeResult.AppendLine($"    {BuildPropertySignature(propertyMap.Property, nullable: nullable)}");
            codeResult.AppendLine();
        }
    }

    /// <summary>
    /// Adds namespace if present and imports required for EF Core entity mapping.
    /// </summary>
    protected override void BuildImports()
    {
        // unused in multi-entity flow
    }

    private static void BuildImports(EntityMap em, StringBuilder codeResult)
    {
        if (em.Entity.Namespace != null)
        {
            codeResult.AppendLine($"namespace {em.Entity.Namespace};");
            codeResult.AppendLine();
        }

        if (em.PrimaryKey?.Parts.Count > 1)
        {
            codeResult.AppendLine("using Microsoft.EntityFrameworkCore;"); // [PrimaryKey] attribute
        }

        codeResult.AppendLine("using System.ComponentModel.DataAnnotations;");
        codeResult.AppendLine("using System.ComponentModel.DataAnnotations.Schema;");

        codeResult.AppendLine();
    }

    protected override void BuildPrimaryKey()
    {
        // unused in multi-entity flow
    }

    private static void BuildPrimaryKey(EntityMap em, StringBuilder codeResult)
    {
        if (em.PrimaryKey is null)
        {
            return; // no PK
        }

        bool composite = em.PrimaryKey.Parts.Count > 1;

        // TODO primary key strategy

        foreach (var part in em.PrimaryKey.Parts)
        {
            var propertyMap = part.PropertyMap;
            bool nullable = propertyMap.IsNullable ?? false;

            // U jednoduchého klíče [Key] na vlastnosti; u kompozitního definuje klíč
            // třídní [PrimaryKey(...)] atribut (viz BuildTableSchema), [Key] se negeneruje.
            codeResult.Append(BuildPropertyAttributes(propertyMap, isPrimaryKey: !composite));
            codeResult.AppendLine($"    {BuildPropertySignature(propertyMap.Property, isPrimaryKey: true, nullable: nullable)}");
            codeResult.AppendLine();
        }
    }

    /// <summary>
    /// Builds the properties of the entity.
    /// </summary>
    protected override void BuildProperties()
    {
        // unused in multi-entity flow
    }

    private static void BuildProperties(EntityMap em, StringBuilder codeResult)
    {
        foreach (var propertyMap in em.PropertyMaps)
        {
            if (em.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == propertyMap.Property.Name) == true)
            {
                continue; // handled in BuildPrimaryKey
            }

            if (propertyMap.OtherDatabaseProperties.TryGetValue("IsForeignKey", out var fk) && fk.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue; // navigation property – handled in BuildForeignKey
            }

            bool nullable = propertyMap.IsNullable ?? false;

            codeResult.Append(BuildPropertyAttributes(propertyMap));
            codeResult.AppendLine($"    {BuildPropertySignature(propertyMap.Property, nullable: nullable)}");
            codeResult.AppendLine();
        }
    }

    /// <summary>
    /// Builds table schema - attributes and class definition.
    /// </summary>
    protected override void BuildTableSchema()
    {
        // unused in multi-entity flow
    }

    private static void BuildTableSchema(EntityMap em, StringBuilder codeResult)
    {
        if (em.Table != null)
        {
            string schemaIfPresent = "";
            if (em.Schema != null)
            {
                schemaIfPresent = $", Schema = \"{em.Schema}\"";
            }

            codeResult.AppendLine($"[Table(\"{em.Table}\"{schemaIfPresent})]");
        }

        if (em.PrimaryKey?.Parts.Count > 1)
        {
            var keyNames = string.Join(", ", em.PrimaryKey.Parts.Select(p => $"nameof({p.PropertyMap.Property.Name})"));
            codeResult.AppendLine($"[PrimaryKey({keyNames})]");
        }

        var modifier = AccessModifierConvertor.ToModifierString(em.Entity.AccessModifier);
        var name = em.Entity.Name;

        codeResult.AppendLine($"{modifier} class {name}");
        codeResult.AppendLine("{");
    }

    /// <summary>
    /// Finalizes the build process by closing the class definition.
    /// </summary>
    protected override void FinalizeBuild()
    {
        // unused in multi-entity flow
    }

    private static void FinalizeBuild(StringBuilder codeResult)
    {
        codeResult.AppendLine("}");
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

        // This would be a place to query database type in the future, if needed
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
}
