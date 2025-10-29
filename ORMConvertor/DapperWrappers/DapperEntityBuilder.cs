using AbstractWrappers;
using Common.Convertors;
using Model;
using Model.AbstractRepresentation;
using System.Text;

namespace DapperWrappers;

public class DapperEntityBuilder : AbstractEntityBuilder
{
    /// <summary>
    /// Builds one C# class per accumulated entity.
    /// </summary>
    public override List<ConversionSource> Build()
    {
        var outputs = new List<ConversionSource>();
        foreach (var em in EntityMaps)
        {
            var codeResult = new StringBuilder();
            BuildImports(em, codeResult);
            BuildTableSchema(em, codeResult);
            BuildProperties(em, codeResult);
            FinalizeBuild(codeResult);

            outputs.Add(new ConversionSource
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = codeResult.ToString()
            });
        }
        return outputs;
    }

    /// <summary>
    /// Dapper does not support foreign keys.
    /// </summary>
    protected override void BuildForeignKey()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Dapper needs no imports for the entity. Only builds namespace if provided.
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
    }

    /// <summary>
    /// Dapper does not support primary keys.
    /// </summary>
    protected override void BuildPrimaryKey()
    {
        throw new NotImplementedException();
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
        foreach (var property in em.Entity.Properties)
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

            codeResult.AppendLine($"    {modifiers} {type} {property.Name}{getterSetter}{defaultValue}");
            codeResult.AppendLine();
        }
    }

    /// <summary>
    /// Dapped support no information about table schema.
    /// Only builds the class.
    /// </summary>
    protected override void BuildTableSchema()
    {
        // unused in multi-entity flow
    }

    private static void BuildTableSchema(EntityMap em, StringBuilder codeResult)
    {
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
}
