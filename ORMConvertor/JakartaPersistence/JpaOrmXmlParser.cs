using System.Xml.Linq;
using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;

namespace JakartaPersistence;

/// <summary>
/// Reads the orm.xml mapping descriptor of Jakarta Persistence 3.2 (decision 077) into the
/// same facts the annotation reader produces, and writes them through the same writer.
/// It stands before the entity parser in the wrapper's list, because the specification
/// puts the descriptor above the annotations (decision 068); a class it declares
/// metadata-complete has its annotations switched off through the shared context.
///
/// The subset is the annotation subset of decision 077 in its XML spelling; an element
/// outside it is a loss record, never silence (decision 048).
/// </summary>
public sealed class JpaOrmXmlParser(AbstractEntityBuilder entityBuilder, JpaReadingContext context) : IEntityParser
{
    private readonly JpaMappingWriter writer = new(entityBuilder, ConversionContentType.XML);

    private static readonly HashSet<string> ReadEntityChildren =
    [
        "description", "table", "id-class", "sequence-generator", "table-generator", "attributes",
    ];

    private static readonly HashSet<string> ReadAttributeChildren =
    [
        "description", "id", "embedded-id", "basic", "version", "many-to-one", "one-to-one", "one-to-many", "many-to-many", "transient",
    ];

    public bool CanParse(ConversionContentType contentType) => contentType == ConversionContentType.XML;

    /// <summary>
    /// The entity maps the document created or enriched; empty when the root is not
    /// entity-mappings or the document declares no entity (decision 066).
    /// </summary>
    public IReadOnlyCollection<EntityMap> Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        var root = XDocument.Parse(source.Trim()).Root;
        if (root is null || root.Name.LocalName != "entity-mappings")
        {
            return [];
        }

        var read = new List<EntityMap>();
        var package = root.Elements().FirstOrDefault(e => e.Name.LocalName == "package")?.Value.Trim();
        var rootGenerators = new Dictionary<string, JpaGeneratorFacts>(StringComparer.Ordinal);

        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "package":
                case "description":
                    break;

                case "persistence-unit-metadata":
                    if (element.Descendants().Any(d => d.Name.LocalName == "xml-mapping-metadata-complete"))
                    {
                        context.AllMetadataComplete = true;
                    }

                    break;

                case "sequence-generator":
                case "table-generator":
                    if (ReadGenerator(element) is { } generator)
                    {
                        rootGenerators[generator.Name] = generator;
                    }

                    break;

                case "entity":
                case "embeddable":
                    var map = ReadEntity(element, package, rootGenerators);
                    if (map is not null && !read.Contains(map))
                    {
                        read.Add(map);
                    }

                    break;

                default:
                    entityBuilder.Report(new ConversionRecord
                    {
                        Kind = ConversionRecordKind.Loss,
                        Framework = entityBuilder.Descriptor.Framework,
                        Artifact = ConversionContentType.XML,
                        Reason = $"The element <{element.Name.LocalName}> has no counterpart in the intermediate representation and was dropped.",
                    });
                    break;
            }
        }

        return read;
    }

    private EntityMap? ReadEntity(XElement entity, string? package, Dictionary<string, JpaGeneratorFacts> rootGenerators)
    {
        var qualified = entity.Attribute("class")?.Value?.Trim();
        if (string.IsNullOrEmpty(qualified))
        {
            return null;
        }

        var lastDot = qualified.LastIndexOf('.');
        var className = lastDot < 0 ? qualified : qualified[(lastDot + 1)..];
        var ns = lastDot < 0 ? package : qualified[..lastDot];

        var existing = entityBuilder.EntityMaps.FirstOrDefault(em =>
                string.Equals(em.Entity.Name, className, StringComparison.Ordinal)
                && string.Equals(em.Entity.Namespace, ns, StringComparison.Ordinal))
            ?? entityBuilder.EntityMaps.FirstOrDefault(em =>
                string.Equals(em.Entity.Name, className, StringComparison.Ordinal));

        if (existing is null)
        {
            entityBuilder.BeginEntity();
            entityBuilder.AddClassHeader(string.Empty, className);
        }
        else
        {
            entityBuilder.EntityMap = existing;
        }

        if (!string.IsNullOrEmpty(ns) && string.IsNullOrEmpty(entityBuilder.EntityMap.Entity.Namespace))
        {
            entityBuilder.AddNamespace(ns);
        }

        if (IsTrue(entity.Attribute("metadata-complete")?.Value))
        {
            context.MarkMetadataComplete(className);
        }

        var facts = new JpaEntityFacts { ClassName = className };
        foreach (var generator in rootGenerators)
        {
            facts.Generators[generator.Key] = generator.Value;
        }

        foreach (var child in entity.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "description":
                    break;

                case "table":
                    facts.Table = child.Attribute("name")?.Value;
                    facts.Schema = child.Attribute("schema")?.Value;
                    foreach (var constraint in child.Elements().Where(e => e.Name.LocalName == "unique-constraint"))
                    {
                        var columns = constraint.Elements()
                            .Where(e => e.Name.LocalName == "column-name")
                            .Select(e => e.Value.Trim())
                            .Where(c => c.Length > 0)
                            .ToList();

                        if (columns.Count > 0)
                        {
                            facts.UniqueConstraints.Add(new JpaUniqueConstraintFacts(constraint.Attribute("name")?.Value, columns));
                        }
                    }

                    break;

                case "id-class":
                    facts.IdClass = SimpleName(child.Attribute("class")?.Value);
                    break;

                case "sequence-generator":
                case "table-generator":
                    if (ReadGenerator(child) is { } generator)
                    {
                        facts.Generators[generator.Name] = generator;
                    }

                    break;

                case "attributes":
                    ReadAttributes(child, facts);
                    break;

                default:
                    facts.Unread.Add($"<{child.Name.LocalName}>");
                    break;
            }
        }

        writer.Write(facts);
        return entityBuilder.EntityMap;
    }

    private void ReadAttributes(XElement attributes, JpaEntityFacts facts)
    {
        foreach (var element in attributes.Elements())
        {
            var name = element.Attribute("name")?.Value;
            if (element.Name.LocalName == "description")
            {
                continue;
            }

            if (!ReadAttributeChildren.Contains(element.Name.LocalName))
            {
                facts.Unread.Add(name is null ? $"<{element.Name.LocalName}>" : $"<{element.Name.LocalName} name=\"{name}\">");
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var attribute = new JpaAttributeFacts { Name = name };

            switch (element.Name.LocalName)
            {
                case "id":
                    attribute.Kind = JpaAttributeKind.Id;
                    ReadColumn(element, attribute);
                    if (element.Elements().FirstOrDefault(e => e.Name.LocalName == "generated-value") is { } generated)
                    {
                        attribute.Generated = true;
                        attribute.GenerationStrategy = generated.Attribute("strategy")?.Value;
                        attribute.GeneratorName = generated.Attribute("generator")?.Value;
                    }

                    attribute.Generator = element.Elements()
                        .Where(e => e.Name.LocalName is "sequence-generator" or "table-generator")
                        .Select(ReadGenerator)
                        .FirstOrDefault(g => g is not null);
                    break;

                case "embedded-id":
                    attribute.Kind = JpaAttributeKind.EmbeddedId;
                    // The key class is the attribute's type, which only the Java declaration
                    // states; the entity parser reads it from there (decision 068).
                    context.MarkEmbeddedId(facts.ClassName, name);
                    break;

                case "basic":
                    attribute.Nullable = ReadBoolean(element.Attribute("optional")?.Value);
                    ReadColumn(element, attribute);
                    break;

                case "version":
                    attribute.Kind = JpaAttributeKind.Version;
                    ReadColumn(element, attribute);
                    break;

                case "transient":
                    attribute.Kind = JpaAttributeKind.Transient;
                    break;

                case "many-to-one":
                case "one-to-one":
                case "one-to-many":
                case "many-to-many":
                    attribute.Kind = element.Name.LocalName switch
                    {
                        "many-to-one" => JpaAttributeKind.ManyToOne,
                        "one-to-one" => JpaAttributeKind.OneToOne,
                        "one-to-many" => JpaAttributeKind.OneToMany,
                        _ => JpaAttributeKind.ManyToMany,
                    };
                    attribute.TargetEntity = SimpleName(element.Attribute("target-entity")?.Value);
                    attribute.MappedBy = element.Attribute("mapped-by")?.Value;
                    attribute.Optional = ReadBoolean(element.Attribute("optional")?.Value);
                    attribute.MapsId = element.Attribute("maps-id") is not null
                        || element.Elements().Any(e => e.Name.LocalName == "primary-key-join-column");

                    foreach (var joinColumn in element.Elements().Where(e => e.Name.LocalName == "join-column"))
                    {
                        attribute.JoinColumns.Add(ReadJoinColumn(joinColumn));
                    }

                    if (element.Elements().FirstOrDefault(e => e.Name.LocalName == "join-table") is { } joinTable)
                    {
                        attribute.JoinTable = new JpaJoinTableFacts(
                            joinTable.Attribute("name")?.Value,
                            joinTable.Attribute("schema")?.Value,
                            [.. joinTable.Elements().Where(e => e.Name.LocalName == "join-column").Select(ReadJoinColumn)],
                            [.. joinTable.Elements().Where(e => e.Name.LocalName == "inverse-join-column").Select(ReadJoinColumn)]);
                    }

                    if (element.Attribute("orphan-removal") is not null)
                    {
                        attribute.Unread.Add($"<{element.Name.LocalName} orphan-removal>");
                    }

                    foreach (var unread in element.Elements().Where(e =>
                        e.Name.LocalName is not ("join-column" or "join-table" or "primary-key-join-column" or "description")))
                    {
                        attribute.Unread.Add($"<{unread.Name.LocalName}>");
                    }

                    break;
            }

            if (element.Name.LocalName is "id" or "basic" or "version")
            {
                foreach (var unread in element.Elements().Where(e =>
                    e.Name.LocalName is not ("column" or "generated-value" or "sequence-generator" or "table-generator" or "description")))
                {
                    attribute.Unread.Add($"<{unread.Name.LocalName}>");
                }
            }

            facts.Attributes.Add(attribute);
        }
    }

    private static void ReadColumn(XElement element, JpaAttributeFacts attribute)
    {
        var column = element.Elements().FirstOrDefault(e => e.Name.LocalName == "column");
        if (column is null)
        {
            return;
        }

        attribute.ColumnName = column.Attribute("name")?.Value;
        attribute.Length = ReadInt(column.Attribute("length")?.Value);
        attribute.Precision = ReadInt(column.Attribute("precision")?.Value);
        // The XSD spelling of @Column(secondPrecision) (decision 079).
        attribute.SecondPrecision = ReadInt(column.Attribute("second-precision")?.Value);
        attribute.Scale = ReadInt(column.Attribute("scale")?.Value);
        attribute.Nullable = ReadBoolean(column.Attribute("nullable")?.Value) ?? attribute.Nullable;
        attribute.Unique |= IsTrue(column.Attribute("unique")?.Value);
        attribute.ColumnDefinition = column.Attribute("column-definition")?.Value;
    }

    private static JpaJoinColumnFacts ReadJoinColumn(XElement element)
        => new(element.Attribute("name")?.Value, element.Attribute("referenced-column-name")?.Value, ReadBoolean(element.Attribute("nullable")?.Value));

    private static JpaGeneratorFacts? ReadGenerator(XElement element)
    {
        var name = element.Attribute("name")?.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new JpaGeneratorFacts(
            IsTable: element.Name.LocalName == "table-generator",
            name,
            SequenceName: element.Attribute("sequence-name")?.Value,
            Schema: element.Attribute("schema")?.Value,
            AllocationSize: ReadInt(element.Attribute("allocation-size")?.Value),
            InitialValue: ReadInt(element.Attribute("initial-value")?.Value),
            Table: element.Attribute("table")?.Value,
            PkColumnName: element.Attribute("pk-column-name")?.Value,
            ValueColumnName: element.Attribute("value-column-name")?.Value,
            PkColumnValue: element.Attribute("pk-column-value")?.Value);
    }

    private static string? SimpleName(string? qualified)
    {
        if (string.IsNullOrWhiteSpace(qualified))
        {
            return null;
        }

        var lastDot = qualified.LastIndexOf('.');
        return lastDot < 0 ? qualified.Trim() : qualified[(lastDot + 1)..].Trim();
    }

    private static int? ReadInt(string? value) => int.TryParse(value, out var n) ? n : null;

    private static bool? ReadBoolean(string? value) => bool.TryParse(value, out var b) ? b : null;

    private static bool IsTrue(string? value) => bool.TryParse(value, out var b) && b;
}
