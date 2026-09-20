using System.Text;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Xml;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace MyBatisWrappers;

/// <summary>
/// Emits the MyBatis pair of an entity (decision 084): the domain class, and the mapper
/// document that maps it. The class is a plain POJO - no import from the framework, because
/// MyBatis asks for none, which makes it the clearest case of the rule that the demands of a
/// target are constraints of the framework and not facts of the domain. The mapper carries a
/// single &lt;resultMap&gt; and no statement: a statement is what the query branch writes,
/// and writing one here would name the table, which MyBatis mapping cannot do anyway.
///
/// Every emitted &lt;resultMap&gt; carries autoMapping="false", which is more than a detail.
/// With automatic mapping on, a column nobody named would still fill a property of the same
/// name, so the meaning of the artifact would depend on a coincidence of names and on a
/// mapUnderscoreToCamelCase in a configuration the tool does not see. Switched off, the
/// mapping is closed - exactly what is written holds - which is what makes a property with
/// no column expressible at all (decisions 072 and 076).
/// </summary>
public class MyBatisEntityBuilder : AbstractEntityBuilder
{
    private readonly SortedSet<string> imports = new(StringComparer.Ordinal);

    private readonly List<(string Type, string Name)> accessors = [];

    public override TargetFrameworkDescriptor Descriptor => MyBatisDescriptor.Instance;

    protected override void BuildImports(EntityMap entityMap, EntityArtifact artifact)
    {
        // The imports follow from what the steps below emit, so they are collected while
        // emitting and rendered ahead of the class in FinalizeBuild.
        imports.Clear();
        accessors.Clear();
    }

    protected override void BuildTableSchema(EntityMap entityMap, EntityArtifact artifact)
    {
        XmlEmitter.Prolog(artifact.Mapping);

        // The DOCTYPE of the MyBatis DTD, which every mapper carries. No model value takes
        // part in it, so there is nothing to escape and nothing for the element writer to do.
        artifact.Mapping.AppendLine("<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\"");
        artifact.Mapping.AppendLine("        \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">");

        var package = entityMap.Entity.Namespace;
        var mapper = $"{entityMap.Entity.Name}Mapper";

        XmlEmitter.Open(artifact.Mapping, 0, "mapper",
        [
            new XmlAttribute("namespace", string.IsNullOrEmpty(package) ? mapper : $"{package}.{mapper}"),
        ]);

        XmlEmitter.Open(artifact.Mapping, 1, "resultMap",
        [
            new XmlAttribute("id", entityMap.Entity.Name),
            new XmlAttribute("type", string.IsNullOrEmpty(package)
                ? entityMap.Entity.Name
                : $"{package}.{entityMap.Entity.Name}"),
            new XmlAttribute("autoMapping", "false"),
        ]);

        artifact.ClassOpened = true;

        var modifier = entityMap.Entity.AccessModifier == AccessModifier.Public ? "public " : string.Empty;
        artifact.Code.AppendLine($"{modifier}class {entityMap.Entity.Name} {{");
    }

    /// <summary>
    /// The key as &lt;id&gt;, in the order of the key's parts. Writing a key this way is
    /// exact; reading one back is not, because MyBatis marks the identity of a result row
    /// with the same element - the asymmetry decision 084 states out loud, and the reason a
    /// MyBatis round trip does not keep the key without a catalog.
    /// </summary>
    protected override void BuildPrimaryKey(EntityMap entityMap, EntityArtifact artifact)
    {
        if (entityMap.PrimaryKey is null)
        {
            return;
        }

        foreach (var part in entityMap.PrimaryKey.Parts)
        {
            AppendMapping(entityMap, part.PropertyMap, "id", artifact);

            // An identifier is always a wrapper type: the unassigned state has to be
            // representable, and MyBatis fills the property through the setter.
            AppendField(entityMap, part.PropertyMap.Property, forceWrapper: true, artifact);
        }
    }

    protected override void BuildProperties(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var propertyMap in entityMap.PropertyMaps)
        {
            if (entityMap.PrimaryKey?.Parts.Any(p => p.PropertyMap == propertyMap) == true
                || entityMap.Relations.Any(r => r.SourceNavigationProperty == propertyMap.Property.Name))
            {
                continue;
            }

            // A property the source states is not persisted is written by being left out of
            // a closed mapping - which is a statement only because autoMapping is off.
            if (!propertyMap.IsTransient)
            {
                AppendMapping(entityMap, propertyMap, "result", artifact);
            }

            AppendField(entityMap, propertyMap.Property, forceWrapper: false, artifact);
        }
    }

    /// <summary>
    /// A navigation keeps its member on the class and gets no mapping. MyBatis states a
    /// relation only inside the result shape of a statement - &lt;collection&gt; and
    /// &lt;association&gt; live in a &lt;resultMap&gt; that a select uses - and the entity
    /// mapper this builder writes carries no statement, so there is nothing for it to hang
    /// on (decision 084).
    /// </summary>
    protected override void BuildForeignKey(EntityMap entityMap, EntityArtifact artifact)
    {
        foreach (var relation in entityMap.Relations)
        {
            var propertyMap = relation.SourceNavigationProperty is null
                ? null
                : entityMap.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == relation.SourceNavigationProperty);

            if (propertyMap is null)
            {
                continue;
            }

            // A relation carrying columns is already reported by the mechanical loss of the
            // ForeignKeyColumns category; one without them states no fact of any category, so
            // its own loss is written here - otherwise the navigation would vanish in silence
            // (decision 048).
            if (relation.ColumnPairs.Count == 0)
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.XML,
                    Entity = entityMap.Entity.Name,
                    Property = relation.SourceNavigationProperty,
                    Category = MappingFactCategory.ForeignKeyColumns,
                    Reason = $"The relation to '{relation.TargetEntity}' has no place in a MyBatis mapper: the framework states a "
                        + "relation only inside the result shape of a statement, and the entity mapper carries none. The member stays "
                        + "on the class and the navigation is not mapped.",
                });
            }

            AppendField(entityMap, propertyMap.Property, forceWrapper: false, artifact);
        }
    }

    /// <summary>
    /// MyBatis forces nothing onto the body of a class. Its one demand is negative - the
    /// no-arg constructor its ObjectFactory reaches for - and it is met by declaring no
    /// constructor at all, which is why this step is empty rather than absent (decision 009).
    /// </summary>
    protected override void BuildEnforcedMembers(EntityMap entityMap, EntityArtifact artifact)
    {
    }

    protected override IEnumerable<ConversionSource> FinalizeBuild(EntityMap entityMap, EntityArtifact artifact)
    {
        var code = artifact.Code;

        foreach (var (type, name) in accessors)
        {
            var property = entityMap.Entity.Properties.First(p => p.Name == name);
            var getter = property.HasGetter || !property.HasSetter;
            var setter = property.HasSetter || !property.HasGetter;
            var capitalized = JavaClass.Capitalize(name);
            var prefix = type == "boolean" ? "is" : "get";

            if (getter)
            {
                code.AppendLine();
                code.AppendLine($"    public {type} {prefix}{capitalized}() {{");
                code.AppendLine($"        return {name};");
                code.AppendLine("    }");
            }

            if (setter)
            {
                code.AppendLine();
                code.AppendLine($"    public void set{capitalized}({type} value) {{");
                code.AppendLine($"        this.{name} = value;");
                code.AppendLine("    }");
            }
        }

        code.AppendLine("}");

        if (artifact.ClassOpened)
        {
            XmlEmitter.Close(artifact.Mapping, 1, "resultMap");
        }

        XmlEmitter.Close(artifact.Mapping, 0, "mapper", appendLine: false);

        var header = new StringBuilder();
        if (!string.IsNullOrEmpty(entityMap.Entity.Namespace))
        {
            header.AppendLine($"package {entityMap.Entity.Namespace};");
            header.AppendLine();
        }

        foreach (var import in imports)
        {
            header.AppendLine($"import {import};");
        }

        if (imports.Count > 0)
        {
            header.AppendLine();
        }

        yield return new ConversionSource
        {
            ContentType = ConversionContentType.JavaEntity,
            Content = header + code.ToString(),
        };

        yield return new ConversionSource
        {
            ContentType = ConversionContentType.XML,
            Content = artifact.Mapping.ToString(),
        };
    }

    /// <summary>
    /// One pair of column and property. The column name is written always, so that no
    /// property depends on a name coincidence the closed mapping was switched off to avoid;
    /// it restates the universal default where the source stated none and therefore carries
    /// no record (decision 067). The type reaches the mapping as far as jdbcType goes, which
    /// is a JDBC family and its unicode facet - the length and the precision beside it are
    /// reported inexpressible by the descriptor, and a literal type the source spelled has no
    /// counterpart at all and is reported here.
    /// </summary>
    private void AppendMapping(EntityMap entityMap, PropertyMap propertyMap, string element, EntityArtifact artifact)
    {
        var attributes = new List<XmlAttribute>
        {
            new("column", propertyMap.ColumnName ?? propertyMap.Property.Name),
            new("property", propertyMap.Property.Name),
        };

        if (propertyMap.Type is { } type)
        {
            if (MyBatisJdbcType.Write(type, propertyMap.IsUnicode) is { } jdbcType)
            {
                attributes.Add(new XmlAttribute("jdbcType", jdbcType));
            }
            else
            {
                ReportTypeLoss(entityMap, propertyMap,
                    $"The database type family {type} has no member in the JdbcType vocabulary MyBatis writes, and naming another "
                    + "family would bind the value through another type handler; the type was dropped.");
            }
        }
        else if (propertyMap.IsUnicode is not null)
        {
            ReportTypeLoss(entityMap, propertyMap,
                "The source states that the column holds national character data and names no type family, and jdbcType states the "
                + "two together or not at all; the facet was dropped.");
        }

        if (propertyMap.SourceSqlType is not null)
        {
            ReportTypeLoss(entityMap, propertyMap,
                $"The source spells the column type '{propertyMap.SourceSqlType}' literally, and a MyBatis mapper has no counterpart "
                + "of columnDefinition - jdbcType names a JDBC family, not a type of one database system; the spelling was dropped.");
        }

        XmlEmitter.Empty(artifact.Mapping, 2, element, attributes);
    }

    private void ReportTypeLoss(EntityMap entityMap, PropertyMap propertyMap, string reason)
        => Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.XML,
            Entity = entityMap.Entity.Name,
            Property = propertyMap.Property.Name,
            Category = MappingFactCategory.DatabaseType,
            Reason = reason,
        });

    /// <summary>
    /// The field, private, with the initializer the model carries where Java spells it: an
    /// empty collection of the target's own kind (decision 035), a literal both languages
    /// share; anything else is a loss. The accessors are collected and written after the last
    /// member, because MyBatis fills a property through its setter.
    /// </summary>
    private void AppendField(EntityMap entityMap, Property property, bool forceWrapper, EntityArtifact artifact)
    {
        var langType = property.Type
            ?? throw new NotSupportedException($"Property '{property.Name}' has no language type.");

        var type = JavaTypeConvertor.ToString(langType, forceWrapper);

        if (JavaTypeConvertor.ImportFor(langType) is { } typeImport)
        {
            imports.Add(typeImport);
        }

        if (langType.Category == LangTypeCategory.Collection && JavaTypeConvertor.ImportFor(langType.ElementType!) is { } elementImport)
        {
            imports.Add(elementImport);
        }

        artifact.Code.AppendLine();
        artifact.Code.AppendLine($"    private {type} {property.Name}{RenderInitializer(entityMap, property, langType)};");

        // Modifiers translate rather than travel (decision 076): what Java means implicitly
        // or what is a compile-time device of C# drops without a word; anything else has no
        // counterpart and is a loss.
        foreach (var modifier in property.OtherModifiers.Where(m => m is not ("virtual" or "override" or "sealed" or "new" or "required")))
        {
            Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = Descriptor.Framework,
                Artifact = ConversionContentType.JavaEntity,
                Entity = entityMap.Entity.Name,
                Property = property.Name,
                Reason = $"The modifier '{modifier}' has no counterpart in Java; it was dropped.",
            });
        }

        accessors.Add((type, property.Name));
    }

    private string RenderInitializer(EntityMap entityMap, Property property, LangType langType)
    {
        if (langType.Category == LangTypeCategory.Collection)
        {
            var (expression, import) = JavaTypeConvertor.EmptyCollection(langType.CollectionKind ?? CollectionKind.Unspecified);
            imports.Add(import);

            if (!string.IsNullOrWhiteSpace(property.DefaultValue) && !IsEmptyCollectionInitializer(property.DefaultValue))
            {
                Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = Descriptor.Framework,
                    Artifact = ConversionContentType.JavaEntity,
                    Entity = entityMap.Entity.Name,
                    Property = property.Name,
                    Reason = $"The initializer '{property.DefaultValue}' of the collection is replaced by an empty collection of the "
                        + "target; its content is dropped.",
                });
            }

            return $" = {expression}";
        }

        if (string.IsNullOrWhiteSpace(property.DefaultValue))
        {
            return string.Empty;
        }

        var text = property.DefaultValue.Trim();
        var scalar = langType.Category == LangTypeCategory.Scalar ? langType.ScalarType : null;

        if (text is "null" or "true" or "false" || (text.Length >= 2 && text[0] == '"' && text[^1] == '"'))
        {
            return $" = {text}";
        }

        var number = text.TrimEnd('m', 'M', 'd', 'D', 'f', 'F', 'L', 'l');
        if (number.Length > 0
            && decimal.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
            && !number.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return scalar switch
            {
                ScalarType.Decimal => $" = new BigDecimal(\"{number}\")",
                ScalarType.Float => $" = {number}f",
                ScalarType.Long => $" = {number}L",
                _ => $" = {number}",
            };
        }

        Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.JavaEntity,
            Entity = entityMap.Entity.Name,
            Property = property.Name,
            Reason = $"The initializer '{text}' is not a literal both languages spell alike; it was dropped.",
        });

        return string.Empty;
    }

    private static bool IsEmptyCollectionInitializer(string text)
    {
        var trimmed = text.Trim();
        return trimmed is "[]" or "new()"
            || (trimmed.StartsWith("new ", StringComparison.Ordinal) && trimmed.EndsWith("()", StringComparison.Ordinal));
    }
}
