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
            BuildIdentityMembers(em, codeResult);
            FinalizeBuild(codeResult, mappingResult, classOpened);

            outputs.Add(new() { ContentType = ConversionContentType.CSharpEntity, Content = codeResult.ToString() });
            outputs.Add(new() { ContentType = ConversionContentType.XML, Content = mappingResult.ToString() });
        }

        return outputs;
    }

    /// <summary>
    /// True when the entity is mapped with a composite identifier.
    /// NHibernate then imposes extra requirements on the persistent class,
    /// see design doc 001 section 3.5.
    /// </summary>
    private static bool HasCompositeKey(EntityMap em)
        => em.PrimaryKey is not null && em.PrimaryKey.Parts.Count > 1;

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
        // System is required for [Serializable] and HashCode in the identity
        // members emitted for composite keys. A plain entity needs no imports.
        if (HasCompositeKey(em))
        {
            codeResult.AppendLine("using System;");
            codeResult.AppendLine();
        }

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
        if (HasCompositeKey(em))
        {
            // Required by NHibernate for classes mapped with <composite-id>.
            codeResult.AppendLine("[Serializable]");
        }

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

        // Composite key: <composite-id> without a generator (assigned semantics),
        // the order of <key-property> elements matches PrimaryKeyPart.Order.
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

    /// <summary>
    /// Emits the Equals/GetHashCode overrides that NHibernate requires from any
    /// class mapped with <composite-id>. Without them the mapping fails to compile
    /// with "composite-id class must override Equals()" while the session factory
    /// is being built. See design doc 001, section 3.5 and decision 7.5.
    /// </summary>
    private static void BuildIdentityMembers(EntityMap em, StringBuilder codeResult)
    {
        if (!HasCompositeKey(em))
        {
            return;
        }

        var className = em.Entity.Name;
        var keyNames = em.PrimaryKey!.Parts
            .Select(p => p.PropertyMap.Property.Name)
            .ToList();

        codeResult.AppendLine("    public override bool Equals(object? obj)");
        codeResult.AppendLine("    {");
        codeResult.AppendLine("        if (ReferenceEquals(this, obj))");
        codeResult.AppendLine("        {");
        codeResult.AppendLine("            return true;");
        codeResult.AppendLine("        }");
        codeResult.AppendLine();
        // Pattern matching rather than GetType() equality: an NHibernate proxy is a
        // subclass of the entity, so comparing the runtime types would reject it.
        codeResult.AppendLine($"        if (obj is not {className} other)");
        codeResult.AppendLine("        {");
        codeResult.AppendLine("            return false;");
        codeResult.AppendLine("        }");
        codeResult.AppendLine();

        for (var i = 0; i < keyNames.Count; i++)
        {
            var name = keyNames[i];
            var prefix = i == 0 ? "return " : "    && ";
            var suffix = i == keyNames.Count - 1 ? ";" : string.Empty;
            codeResult.AppendLine($"        {prefix}Equals({name}, other.{name}){suffix}");
        }

        codeResult.AppendLine("    }");
        codeResult.AppendLine();

        codeResult.AppendLine("    public override int GetHashCode()");
        codeResult.AppendLine("    {");

        if (keyNames.Count <= 8)
        {
            codeResult.AppendLine($"        return HashCode.Combine({string.Join(", ", keyNames)});");
        }
        else
        {
            // HashCode.Combine is only defined up to eight arguments.
            codeResult.AppendLine("        var hash = new HashCode();");
            foreach (var name in keyNames)
            {
                codeResult.AppendLine($"        hash.Add({name});");
            }
            codeResult.AppendLine("        return hash.ToHashCode();");
        }

        codeResult.AppendLine("    }");
        codeResult.AppendLine();
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