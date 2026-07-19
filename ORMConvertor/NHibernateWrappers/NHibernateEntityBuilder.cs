using AbstractWrappers;
using Common.Convertors;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers.Convertors;
using System.Text;

namespace NHibernateWrappers;

public class NHibernateEntityBuilder : AbstractEntityBuilder
{
    /// <summary>
    /// Builds one C# class and one XML mapping per entity.
    /// </summary>
    public override List<ConversionSource> Build()
    {
        var outputs = new List<ConversionSource>();
        foreach (var em in EntityMaps)
        {
            var codeResult = new StringBuilder();
            var mappingResult = new StringBuilder();
            bool classOpened = false;

            BuildImports(em, codeResult, mappingResult);
            BuildTableSchema(em, codeResult, mappingResult, ref classOpened);
            BuildPrimaryKey(em, codeResult, mappingResult);
            BuildProperties(em, codeResult, mappingResult);
            BuildForeignKey(em, codeResult, mappingResult);
            FinalizeBuild(codeResult, mappingResult, classOpened);

            outputs.Add(new() { ContentType = ConversionContentType.CSharpEntity, Content = codeResult.ToString() });
            outputs.Add(new() { ContentType = ConversionContentType.XML, Content = mappingResult.ToString() });
        }

        return outputs;
    }

    /// <summary>
    /// Adds C# namespace.
    /// Adds XML prolog and root <hibernate-mapping> tag.
    /// </summary>
    protected override void BuildImports()
    {
        // unused in multi-entity flow
    }

    private static void BuildImports(EntityMap em, StringBuilder codeResult, StringBuilder mappingResult)
    {
        // No imports needed for NHibernate entity
        if (!string.IsNullOrWhiteSpace(em.Entity.Namespace))
        {
            codeResult.AppendLine($"namespace {em.Entity.Namespace};");
            codeResult.AppendLine();
        }

        // XML: prolog + root <hibernate-mapping>
        AppendXml(mappingResult, 0, "<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
        var xmlNs = "urn:nhibernate-mapping-2.2";
        var nsAttr = string.IsNullOrWhiteSpace(em.Entity.Namespace)
            ? string.Empty
            : $" namespace=\"{em.Entity.Namespace}\"";
        AppendXml(mappingResult, 0, $"<hibernate-mapping xmlns=\"{xmlNs}\"{nsAttr}>");
    }

    /// <summary>
    /// Builds C# class header and XML <class> tag.
    /// </summary>
    protected override void BuildTableSchema()
    {
        // unused in multi-entity flow
    }

    private static void BuildTableSchema(EntityMap em, StringBuilder codeResult, StringBuilder mappingResult, ref bool classOpened)
    {
        var modifier = AccessModifierConvertor.ToModifierString(em.Entity.AccessModifier);
        var name = em.Entity.Name;

        // C#
        codeResult.AppendLine($"{modifier} class {name}");
        codeResult.AppendLine("{");

        // XML <class>
        var nameWithNamespace = string.IsNullOrWhiteSpace(em.Entity.Namespace)
            ? name
            : $"{em.Entity.Namespace}.{name}, {em.Entity.Namespace}";

        var table = em.Table ?? name; // default = class name
        var schema = em.Schema ?? string.Empty; // TODO schema
        var schemaAttr = string.IsNullOrWhiteSpace(schema) ? string.Empty : $" schema=\"{schema}\"";

        AppendXml(mappingResult, 1, $"<class name=\"{nameWithNamespace}\" table=\"{table}\"{schemaAttr}>");
        classOpened = true;
    }

    /// <summary>
    /// Builds C# primary key property and XML <id> tag.
    /// </summary>
    protected override void BuildPrimaryKey()
    {
        // unused in multi-entity flow
    }

    private static void BuildPrimaryKey(EntityMap em, StringBuilder codeResult, StringBuilder mappingResult)
    {
        if (em.PrimaryKey is null)
        {
            return; // no PK
        }

        if (em.PrimaryKey.Parts.Count == 1)
        {
            var part = em.PrimaryKey.Parts[0];
            var propertyMap = part.PropertyMap;
            var prop = propertyMap.Property;
            var columnName = propertyMap.ColumnName ?? prop.Name;

            var generatorClass = PrimaryKeyStrategyConvertor.ToNHibernate(part.Strategy);

            AppendPropertyToCode(codeResult, prop, isPrimaryKey: true);

            AppendXml(mappingResult, 2, $"<id name=\"{prop.Name}\" column=\"{columnName}\" type=\"{ResolveNhType(propertyMap)}\">");
            AppendXml(mappingResult, 3, $"<generator class=\"{generatorClass}\" />");
            AppendXml(mappingResult, 2, "</id>");
            return;
        }

        // Kompozitní klíč: <composite-id> bez generátoru (assigned sémantika),
        // pořadí <key-property> elementů odpovídá PrimaryKeyPart.Order.
        AppendXml(mappingResult, 2, "<composite-id>");
        foreach (var part in em.PrimaryKey.Parts)
        {
            var propertyMap = part.PropertyMap;
            var prop = propertyMap.Property;
            var columnName = propertyMap.ColumnName ?? prop.Name;

            AppendPropertyToCode(codeResult, prop, isPrimaryKey: true);
            AppendXml(mappingResult, 3, $"<key-property name=\"{prop.Name}\" column=\"{columnName}\" type=\"{ResolveNhType(propertyMap)}\" />");
        }
        AppendXml(mappingResult, 2, "</composite-id>");
    }

    private static string ResolveNhType(PropertyMap propertyMap)
    {
        if (propertyMap.Type != null)
        {
            return DatabaseTypeConvertor.ToNHibernate(propertyMap.Type.Value);
        }

        // TODO this would be a place to query database for the missing type
        // for now we guess it from CLR type
        return DatabaseTypeConvertor.GuessFromPropertyType(propertyMap.Property.Type.CLRType);
    }

    /// <summary>
    /// Builds C# properties and XML <property> tags.
    /// Primary and foreign keys are handled separately.
    /// </summary>
    protected override void BuildProperties()
    {
        // unused in multi-entity flow
    }

    private static void BuildProperties(EntityMap em, StringBuilder codeResult, StringBuilder mappingResult)
    {
        foreach (var pm in em.PropertyMaps)
        {
            if (em.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == pm.Property.Name) == true)
            {
                continue; // handled in BuildPrimaryKey
            }

            if (em.Relations.Any(r => r.SourceNavigationProperty == pm.Property.Name))
            {
                continue; // navigation property – handled in BuildForeignKey
            }

            AppendPropertyToCode(codeResult, pm.Property);
            AppendPropertyToXml(mappingResult, pm);
        }
    }

    /// <summary>
    /// Builds C# foreign key properties and XML <one-to-one>, <many-to-one>, <bag> or <many-to-many> tags.
    /// </summary>
    protected override void BuildForeignKey()
    {
        // unused in multi-entity flow
    }

    private static void BuildForeignKey(EntityMap em, StringBuilder codeResult, StringBuilder mappingResult)
    {
        // 1:1 and N:1 foreign keys
        foreach (var relation in em.Relations.Where(r => r.Cardinality is Cardinality.OneToOne or Cardinality.ManyToOne))
        {
            var propertyMap = FindNavigationPropertyMap(em, relation);
            if (propertyMap is null)
            {
                continue;
            }

            var xmlTag = relation.Cardinality == Cardinality.OneToOne ? "one-to-one" : "many-to-one";

            AppendPropertyToCode(codeResult, propertyMap.Property); // navigation property in C#

            var columnName = propertyMap.ColumnName ?? propertyMap.Property.Name;
            AppendXml(mappingResult, 2, $"<{xmlTag} name=\"{propertyMap.Property.Name}\" class=\"{relation.TargetEntity}\" column=\"{columnName}\" />");
        }

        // 1:N and N:N collections
        foreach (var relation in em.Relations.Where(r => r.Cardinality is Cardinality.OneToMany or Cardinality.ManyToMany))
        {
            var propertyMap = FindNavigationPropertyMap(em, relation);
            if (propertyMap is null)
            {
                continue;
            }

            codeResult.AppendLine($"    {BuildPropertySignature(propertyMap.Property)}");
            codeResult.AppendLine();

            // XML <bag> (TODO: allow set/list/map etc.)
            // TODO other collection properties
            AppendXml(mappingResult, 2, $"<bag name=\"{propertyMap.Property.Name}\" inverse=\"true\" cascade=\"all-delete-orphan\">");
            var primaryKeyCol = GetPrimaryKeyColumn(em);
            AppendXml(mappingResult, 3, $"<key column=\"{primaryKeyCol}\" />");

            if (relation.Cardinality == Cardinality.OneToMany)
            {
                AppendXml(mappingResult, 3, $"<one-to-many class=\"{relation.TargetEntity}\" />");
            }
            else // ManyToMany
            {
                AppendXml(mappingResult, 3, $"<many-to-many class=\"{relation.TargetEntity}\" />");
            }

            AppendXml(mappingResult, 2, "</bag>");
        }
    }

    private static PropertyMap? FindNavigationPropertyMap(EntityMap em, Relation relation)
        => relation.SourceNavigationProperty is null
            ? null
            : em.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

    /// <summary>
    /// Finalizes the build process by closing the class and XML tags.
    /// </summary>
    protected override void FinalizeBuild()
    {
        // unused in multi-entity flow
    }

    private static void FinalizeBuild(StringBuilder codeResult, StringBuilder mappingResult, bool classOpened)
    {
        // Close C# class
        codeResult.AppendLine("}");

        if (classOpened)
        {
            AppendXml(mappingResult, 1, "</class>");
        }

        AppendXml(mappingResult, 0, "</hibernate-mapping>", appendLine: false);
    }

    /// <summary>
    /// Appends a property to the C# code.
    /// </summary>
    private static void AppendPropertyToCode(StringBuilder codeResult, Property prop, bool isPrimaryKey = false)
    {
        var declaration = BuildPropertySignature(prop, isPrimaryKey);
        codeResult.AppendLine($"    {declaration}");
        codeResult.AppendLine();
    }

    /// <summary>
    /// Appends a property to the XML mapping.
    /// </summary>
    private static void AppendPropertyToXml(StringBuilder mappingResult, PropertyMap propertyMap)
    {
        var prop = propertyMap.Property;

        var attrs = new List<string> { $"name=\"{prop.Name}\"" };

        if (!string.IsNullOrWhiteSpace(propertyMap.ColumnName))
        {
            attrs.Add($"column=\"{propertyMap.ColumnName}\"");
        }

        if (propertyMap.IsNullable.HasValue)
        {
            attrs.Add($"not-null=\"{(!propertyMap.IsNullable.Value).ToString().ToLowerInvariant()}\"");
        }
        else if (!prop.IsNullable)
        {
            attrs.Add("not-null=\"true\"");
        }

        if (propertyMap.Type.HasValue)
        {
            attrs.Add($"type=\"{DatabaseTypeConvertor.ToNHibernate(propertyMap.Type.Value)}\"");
        }

        if (propertyMap.Precision.HasValue)
        {
            attrs.Add($"precision=\"{propertyMap.Precision.Value}\"");
        }

        if (propertyMap.Scale.HasValue)
        {
            attrs.Add($"scale=\"{propertyMap.Scale.Value}\"");
        }

        if (propertyMap.Length.HasValue)
        {
            attrs.Add($"length=\"{propertyMap.Length.Value}\"");
        }

        AppendXml(mappingResult, 2, $"<property {string.Join(' ', attrs)} />");
    }

    /// <summary>
    /// Gets the primary key column name.
    /// </summary>
    private static string GetPrimaryKeyColumn(EntityMap em)
    {
        var pkMap = em.PrimaryKey?.Parts.FirstOrDefault()?.PropertyMap;
        return pkMap?.ColumnName ?? pkMap?.Property.Name ?? "Id";
    }

    /// <summary>
    /// Appends a line to the XML mapping with indentation.
    /// </summary>
    private static void AppendXml(StringBuilder mappingResult, int indentLevels, string content, bool appendLine = true)
    {
        var indent = new string(' ', indentLevels * 4);
        if (appendLine)
        {
            mappingResult.AppendLine($"{indent}{content}");
        }
        else
        {
            mappingResult.Append($"{indent}{content}");
        }

    }

    /// <summary>
    /// Builds the property signature for C# code.
    /// Adds modifiers, type, name, getter/setter, and default value.
    /// </summary>
    private static string BuildPropertySignature(Property prop, bool isPrimaryKey = false)
    {
        var otherMods = new List<string>(prop.OtherModifiers ?? []);
        if (!otherMods.Any(m => m.Equals("virtual", StringComparison.OrdinalIgnoreCase)))
        {
            otherMods.Add("virtual");
        }

        var access = AccessModifierConvertor.ToModifierString(prop.AccessModifier);
        var modifiers = $"{access} {string.Join(' ', otherMods)}".Trim();
        var clrType = CLRTypeConvertor.ToString(prop.Type);
        var type = (!isPrimaryKey && prop.IsNullable) ? $"{clrType}?" : clrType;

        var getterSetter = (prop.HasGetter || prop.HasSetter)
            ? $" {{ {(prop.HasGetter ? "get;" : "")}{(prop.HasSetter ? " set;" : "")} }}"
            : "";
        var defaultVal = string.IsNullOrWhiteSpace(prop.DefaultValue)
            ? ""
            : $" = {prop.DefaultValue};";

        return $"{modifiers} {type} {prop.Name}{getterSetter}{defaultVal}";
    }
}
