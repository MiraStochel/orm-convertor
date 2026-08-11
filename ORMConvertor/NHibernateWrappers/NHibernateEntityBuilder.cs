using AbstractWrappers;
using AbstractWrappers.Descriptors;
using Common.Convertors;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers.Convertors;
using System.Text;

namespace NHibernateWrappers;

public class NHibernateEntityBuilder : AbstractEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor => NHibernateDescriptor.Instance;

    /// <summary>
    /// True when the entity is mapped with a composite identifier.
    /// NHibernate then imposes extra requirements on the persistent class,
    /// see decision 006.
    /// </summary>
    private static bool HasCompositeKey(EntityMap em)
        => em.PrimaryKey is not null && em.PrimaryKey.Parts.Count > 1;

    /// <summary>
    /// Adds C# namespace.
    /// Adds XML prolog and root <hibernate-mapping> tag.
    /// </summary>
    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
        // System is required for [Serializable] and HashCode in the identity
        // members emitted for composite keys. A plain entity needs no imports.
        if (HasCompositeKey(entityMap))
        {
            artifact.Code.AppendLine("using System;");
            artifact.Code.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(entityMap.Entity.Namespace))
        {
            artifact.Code.AppendLine($"namespace {entityMap.Entity.Namespace};");
            artifact.Code.AppendLine();
        }

        // XML: prolog + root <hibernate-mapping>
        AppendXml(artifact.Mapping, 0, "<?xml version=\"1.0\" encoding=\"utf-8\" ?>");
        var xmlNs = "urn:nhibernate-mapping-2.2";
        var nsAttr = string.IsNullOrWhiteSpace(entityMap.Entity.Namespace)
            ? string.Empty
            : $" namespace=\"{entityMap.Entity.Namespace}\"";
        AppendXml(artifact.Mapping, 0, $"<hibernate-mapping xmlns=\"{xmlNs}\"{nsAttr}>");
    }

    /// <summary>
    /// Builds C# class header and XML <class> tag.
    /// </summary>
    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
        var modifier = AccessModifierConvertor.ToModifierString(entityMap.Entity.AccessModifier);
        var name = entityMap.Entity.Name;

        // C#
        if (HasCompositeKey(entityMap))
        {
            // Required by NHibernate for classes mapped with <composite-id>. Declared as an
            // enforced member, but emitted here because it precedes the class header.
            artifact.Code.AppendLine("[Serializable]");
        }

        artifact.Code.AppendLine($"{modifier} class {name}");
        artifact.Code.AppendLine("{");

        // XML <class>
        var nameWithNamespace = string.IsNullOrWhiteSpace(entityMap.Entity.Namespace)
            ? name
            : $"{entityMap.Entity.Namespace}.{name}, {entityMap.Entity.Namespace}";

        var table = entityMap.Table ?? name; // default = class name
        var schema = entityMap.Schema ?? string.Empty; // TODO schema
        var schemaAttr = string.IsNullOrWhiteSpace(schema) ? string.Empty : $" schema=\"{schema}\"";

        AppendXml(artifact.Mapping, 1, $"<class name=\"{nameWithNamespace}\" table=\"{table}\"{schemaAttr}>");
        artifact.ClassOpened = true;
    }

    /// <summary>
    /// Builds C# primary key property and XML <id> tag.
    /// </summary>
    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.PrimaryKey is null)
        {
            return; // no PK
        }

        if (entityMap.PrimaryKey.Parts.Count == 1)
        {
            var part = entityMap.PrimaryKey.Parts[0];
            var propertyMap = part.PropertyMap;
            var prop = propertyMap.Property;
            var columnName = propertyMap.ColumnName ?? prop.Name;

            var generatorClass = PrimaryKeyStrategyConvertor.ToNHibernate(part.Strategy);

            AppendPropertyToCode(artifact.Code, prop, isPrimaryKey: true);

            AppendXml(artifact.Mapping, 2, $"<id name=\"{prop.Name}\" column=\"{columnName}\" type=\"{ResolveNhType(propertyMap)}\">");
            AppendXml(artifact.Mapping, 3, $"<generator class=\"{generatorClass}\" />");
            AppendXml(artifact.Mapping, 2, "</id>");
            return;
        }

        // Composite key: <composite-id> without a generator (assigned semantics),
        // the order of <key-property> elements matches PrimaryKeyPart.Order.
        AppendXml(artifact.Mapping, 2, "<composite-id>");
        foreach (var part in entityMap.PrimaryKey.Parts)
        {
            var propertyMap = part.PropertyMap;
            var prop = propertyMap.Property;
            var columnName = propertyMap.ColumnName ?? prop.Name;

            AppendPropertyToCode(artifact.Code, prop, isPrimaryKey: true);
            AppendXml(artifact.Mapping, 3, $"<key-property name=\"{prop.Name}\" column=\"{columnName}\" type=\"{ResolveNhType(propertyMap)}\" />");
        }
        AppendXml(artifact.Mapping, 2, "</composite-id>");
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
    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var pm in entityMap.PropertyMaps)
        {
            if (entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap.Property.Name == pm.Property.Name) == true)
            {
                continue; // handled in BuildPrimaryKey
            }

            if (entityMap.Relations.Any(r => r.SourceNavigationProperty == pm.Property.Name))
            {
                continue; // navigation property – handled in BuildForeignKey
            }

            AppendPropertyToCode(artifact.Code, pm.Property);
            AppendPropertyToXml(artifact.Mapping, pm);
        }
    }

    /// <summary>
    /// Builds C# foreign key properties and XML <one-to-one>, <many-to-one>, <bag> or <many-to-many> tags.
    /// </summary>
    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
        // 1:1 and N:1 foreign keys
        foreach (var relation in entityMap.Relations.Where(r => r.Cardinality is Cardinality.OneToOne or Cardinality.ManyToOne))
        {
            var propertyMap = FindNavigationPropertyMap(entityMap, relation);
            if (propertyMap is null)
            {
                continue;
            }

            var xmlTag = relation.Cardinality == Cardinality.OneToOne ? "one-to-one" : "many-to-one";

            AppendPropertyToCode(artifact.Code, propertyMap.Property); // navigation property in C#

            var columnName = propertyMap.ColumnName ?? propertyMap.Property.Name;
            AppendXml(artifact.Mapping, 2, $"<{xmlTag} name=\"{propertyMap.Property.Name}\" class=\"{relation.TargetEntity}\" column=\"{columnName}\" />");
        }

        // 1:N and N:N collections
        foreach (var relation in entityMap.Relations.Where(r => r.Cardinality is Cardinality.OneToMany or Cardinality.ManyToMany))
        {
            var propertyMap = FindNavigationPropertyMap(entityMap, relation);
            if (propertyMap is null)
            {
                continue;
            }

            artifact.Code.AppendLine($"    {BuildPropertySignature(propertyMap.Property)}");
            artifact.Code.AppendLine();

            // XML <bag> (TODO: allow set/list/map etc.)
            // TODO other collection properties
            AppendXml(artifact.Mapping, 2, $"<bag name=\"{propertyMap.Property.Name}\" inverse=\"true\" cascade=\"all-delete-orphan\">");
            var primaryKeyCol = GetPrimaryKeyColumn(entityMap);
            AppendXml(artifact.Mapping, 3, $"<key column=\"{primaryKeyCol}\" />");

            if (relation.Cardinality == Cardinality.OneToMany)
            {
                AppendXml(artifact.Mapping, 3, $"<one-to-many class=\"{relation.TargetEntity}\" />");
            }
            else // ManyToMany
            {
                AppendXml(artifact.Mapping, 3, $"<many-to-many class=\"{relation.TargetEntity}\" />");
            }

            AppendXml(artifact.Mapping, 2, "</bag>");
        }
    }

    private static PropertyMap? FindNavigationPropertyMap(EntityMap em, Relation relation)
        => relation.SourceNavigationProperty is null
            ? null
            : em.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

    /// <summary>
    /// Emits the Equals/GetHashCode overrides that NHibernate requires from any
    /// class mapped with <composite-id>, as declared by <see cref="Descriptor"/>.
    /// Without them the mapping fails to compile with "composite-id class must
    /// override Equals()" while the session factory is being built. The matching
    /// [Serializable] attribute precedes the class header and is therefore emitted
    /// in BuildTableSchema. See decision 006.
    /// </summary>
    protected override void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact)
    {
        if (!HasCompositeKey(entityMap))
        {
            return;
        }

        var className = entityMap.Entity.Name;
        var keyNames = entityMap.PrimaryKey!.Parts
            .Select(p => p.PropertyMap.Property.Name)
            .ToList();

        artifact.Code.AppendLine("    public override bool Equals(object? obj)");
        artifact.Code.AppendLine("    {");
        artifact.Code.AppendLine("        if (ReferenceEquals(this, obj))");
        artifact.Code.AppendLine("        {");
        artifact.Code.AppendLine("            return true;");
        artifact.Code.AppendLine("        }");
        artifact.Code.AppendLine();
        // Pattern matching rather than GetType() equality: an NHibernate proxy is a
        // subclass of the entity, so comparing the runtime types would reject it.
        artifact.Code.AppendLine($"        if (obj is not {className} other)");
        artifact.Code.AppendLine("        {");
        artifact.Code.AppendLine("            return false;");
        artifact.Code.AppendLine("        }");
        artifact.Code.AppendLine();

        for (var i = 0; i < keyNames.Count; i++)
        {
            var name = keyNames[i];
            var prefix = i == 0 ? "return " : "    && ";
            var suffix = i == keyNames.Count - 1 ? ";" : string.Empty;
            artifact.Code.AppendLine($"        {prefix}Equals({name}, other.{name}){suffix}");
        }

        artifact.Code.AppendLine("    }");
        artifact.Code.AppendLine();

        artifact.Code.AppendLine("    public override int GetHashCode()");
        artifact.Code.AppendLine("    {");

        if (keyNames.Count <= 8)
        {
            artifact.Code.AppendLine($"        return HashCode.Combine({string.Join(", ", keyNames)});");
        }
        else
        {
            // HashCode.Combine is only defined up to eight arguments.
            artifact.Code.AppendLine("        var hash = new HashCode();");
            foreach (var name in keyNames)
            {
                artifact.Code.AppendLine($"        hash.Add({name});");
            }
            artifact.Code.AppendLine("        return hash.ToHashCode();");
        }

        artifact.Code.AppendLine("    }");
        artifact.Code.AppendLine();
    }

    /// <summary>
    /// Finalizes the build process by closing the class and XML tags.
    /// </summary>
    protected override IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact)
    {
        // Close C# class
        artifact.Code.AppendLine("}");

        if (artifact.ClassOpened)
        {
            AppendXml(artifact.Mapping, 1, "</class>");
        }

        AppendXml(artifact.Mapping, 0, "</hibernate-mapping>", appendLine: false);

        yield return new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = artifact.Code.ToString() };
        yield return new ConversionSource { ContentType = ConversionContentType.XML, Content = artifact.Mapping.ToString() };
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