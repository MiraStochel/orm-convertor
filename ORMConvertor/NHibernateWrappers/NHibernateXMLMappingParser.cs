using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Sql;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers.Convertors;
using System;
using System.Xml.Linq;

namespace NHibernateWrappers;

/// <summary>
/// Parses NHibernate mapping from XML file.
/// Uses LINQ to XML to parse the mapping and extract relevant information.
/// </summary>
/// <param name="declaredSourceDialect">
/// What the source declared about the dialect of its literal SQL (decision 088). This parser
/// is the third place the declaration reaches and the only one that does not read a type name
/// at all: the sql-type of a nested &lt;column&gt; travels verbatim on the escape path, so the
/// guard here is a single condition before the write rather than a reading that stops.
/// </param>
public class NHibernateXMLMappingParser(
    AbstractEntityBuilder entityBuilder,
    SourceSqlDialect? declaredSourceDialect = null) : IEntityParser
{
    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType)
    {
        return contentType == ConversionContentType.XML;
    }

    /// <summary>
    /// Parses an NHibernate mapping XML file from the provided source code string.
    /// </summary>
    /// <param name="source">String containing XML mapping file</param>
    /// <returns>
    /// The entity maps the unit created or enriched; empty when the document holds no
    /// hibernate-mapping root or no class element (decision 066). Enrichment counts the
    /// same as creation - this parser usually adds no new map, which is exactly why the
    /// orchestration cannot count maps instead.
    /// </returns>
    public IReadOnlyCollection<EntityMap> Parse(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return [];
        }

        var (mapping, unreadable) = XmlSource.Read(source);

        // A document that cannot be read is a fact about this unit and nothing else
        // (decision 093): it used to leave as an XmlException and end as a 400, taking the
        // artifacts of every healthy unit of the same conversion with it. This parser is the
        // one that says it, because it reads the hbm.xml on the earlier pass; the query
        // parser over the same document then stays silent.
        if (unreadable is not null)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = entityBuilder.Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Reason = unreadable,
            });

            return [];
        }

        if (mapping == null || mapping.Name.LocalName != "hibernate-mapping")
        {
            return [];
        }

        return ParseMapping(mapping);
    }

    /// <summary>
    /// Parses the mapping element of the NHibernate XML mapping file.
    /// </summary>
    private IReadOnlyCollection<EntityMap> ParseMapping(XElement mapping)
    {
        var read = new List<EntityMap>();
        var mappingNamespace = mapping.Attribute("namespace")?.Value;

        foreach (var element in mapping.Elements().Where(e => e.Name.LocalName != "class"))
        {
            // Both forms of a named query are read - by NHibernateXmlQueryParser, on the query
            // pass over this same unit (decision 081): <query> as HQL and, since the T-SQL
            // grammar moved into a shared project of its own (decision 082), <sql-query> as
            // native SQL. Neither is this parser's loss to report any more.
            if (element.Name.LocalName is "query" or "sql-query")
            {
                continue;
            }

            // <subclass> and its joined and union forms can sit here with an extends
            // attribute, next to imports; none of them is read (decision 030).
            ReportUnreadElement(element, entity: null);
        }

        foreach (var classElement in mapping.Elements().Where(e => e.Name.LocalName == "class"))
        {
            var (classNamespace, className) = ParseClassIdentity(classElement);
            var effectiveNamespace = classNamespace ?? mappingNamespace;

            EntityMap? existing = null;
            if (!string.IsNullOrEmpty(className))
            {
                if (!string.IsNullOrEmpty(effectiveNamespace))
                {
                    existing = entityBuilder.EntityMaps.FirstOrDefault(em =>
                        string.Equals(em.Entity.Name, className, StringComparison.Ordinal) &&
                        string.Equals(em.Entity.Namespace, effectiveNamespace, StringComparison.Ordinal));
                }

                existing ??= entityBuilder.EntityMaps.FirstOrDefault(em =>
                    string.Equals(em.Entity.Name, className, StringComparison.Ordinal) &&
                    string.IsNullOrEmpty(em.Entity.Namespace));

                existing ??= entityBuilder.EntityMaps.FirstOrDefault(em =>
                    string.Equals(em.Entity.Name, className, StringComparison.Ordinal));
            }

            if (existing is null)
            {
                entityBuilder.BeginEntity();
                if (!string.IsNullOrEmpty(className))
                {
                    entityBuilder.AddClassHeader(string.Empty, className);
                }
            }
            else
            {
                entityBuilder.EntityMap = existing;
            }

            if (!string.IsNullOrEmpty(effectiveNamespace) &&
                string.IsNullOrEmpty(entityBuilder.EntityMap.Entity.Namespace))
            {
                entityBuilder.AddNamespace(effectiveNamespace);
            }

            ParseClass(classElement);

            if (!read.Contains(entityBuilder.EntityMap))
            {
                read.Add(entityBuilder.EntityMap);
            }
        }

        return read;
    }

    private static (string? Namespace, string? Name) ParseClassIdentity(XElement classElement)
    {
        var nameAttr = classElement.Attribute("name")?.Value;
        if (string.IsNullOrWhiteSpace(nameAttr))
        {
            return (null, null);
        }

        var fullType = nameAttr.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(fullType))
        {
            return (null, null);
        }

        var lastDot = fullType.LastIndexOf('.');
        if (lastDot < 0)
        {
            return (null, fullType);
        }

        var ns = fullType[..lastDot];
        var name = fullType[(lastDot + 1)..];
        return (ns, name);
    }

    /// <summary>
    /// Parses the class element of the NHibernate XML mapping file.
    /// </summary>
    private void ParseClass(XElement classElement)
    {
        // Table and schema, placed at their attributes (decision 125). The start tag of
        // <class> is the scope and nothing nested: the properties below open scopes of their own.
        using (entityBuilder.At(XmlSource.RangeOf(classElement), Refinements(classElement, relation: false, nested: false)))
        {
            var table = classElement.Attribute("table")?.Value;
            if (!string.IsNullOrEmpty(table))
            {
                entityBuilder.AddTable(table);
            }

            var schema = classElement.Attribute("schema")?.Value;
            if (!string.IsNullOrEmpty(schema))
            {
                entityBuilder.AddSchema(schema);
            }
        }

        ReportUnmodelledAttributes(classElement, UnmodelledClassAttributes, propertyName: null);

        ParsePrimaryKey(classElement);
        ParseVersion(classElement);
        ParseProperties(classElement);
        ParseRelations(classElement);
        ParseUniqueKeys(classElement);
        ReportUnreadClassChildren(classElement);
        MarkTransientProperties(classElement);
    }

    /// <summary>
    /// The class properties the mapping does not name are transient (decision 072). The
    /// document is the list of persisted members - NHibernate neither reads nor writes a
    /// member outside it - so a class property the document leaves out is stated not to be
    /// persisted: a claim about the boundary of the list, not a default standing in for a
    /// missing attribute (decision 067). Only a property the class declared can be meant -
    /// one known solely from the mapping has no language type and is named by the mapping
    /// by definition - and without a mapping document nothing is marked at all.
    ///
    /// "Named" is read widely, so that transience is claimed only where the mapping states
    /// it. The name attribute of any element under the class counts, nested ones included:
    /// the key parts of a composite-id, but also the components and the other elements this
    /// parser does not read and reports (decision 030), which do map the property, in a
    /// shape the model does not carry. So does a foreign key column of many-to-one and
    /// key-many-to-one: a scalar property of that name is the flat form of the column, which
    /// the resolution phase pairs with the relation and the EF Core builder writes as the
    /// foreign key property - the column is in the table, even though NHibernate fills the
    /// member through the association.
    /// </summary>
    private void MarkTransientProperties(XElement classElement)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in classElement.Descendants())
        {
            // A column, a generator parameter or a meta element names no property.
            if (element.Name.LocalName is not ("column" or "param" or "meta")
                && element.Attribute("name")?.Value is { Length: > 0 } name)
            {
                named.Add(name);
            }

            if (element.Name.LocalName is "many-to-one" or "key-many-to-one")
            {
                named.UnionWith(ReadRelationColumns(element) ?? []);
            }
        }

        foreach (var property in entityBuilder.EntityMap.Entity.Properties)
        {
            if (property.Type is not null && !named.Contains(property.Name))
            {
                entityBuilder.MarkTransient(property.Name);
            }
        }
    }

    /// <summary>
    /// The elements of a &lt;class&gt; this parser reads; everything else is a flat-class
    /// boundary crossing and leaves a record (decision 030). &lt;map&gt; is on the list
    /// because <see cref="ParseRelations"/> walks it, even though its shape itself is
    /// dropped with a record of its own.
    /// </summary>
    private static readonly HashSet<string> ReadClassChildren =
        ["id", "composite-id", "property", "version", "timestamp", "one-to-one", "many-to-one", "bag", "set", "list", "map"];

    /// <summary>
    /// Elements whose name attribute names a property of the entity - the record can
    /// point at it. On the inheritance and join elements the attribute names a class or
    /// a table instead, so it stays in the reason text only.
    /// </summary>
    private static readonly HashSet<string> PropertyNamedElements =
        ["component", "dynamic-component", "idbag", "array", "primitive-array", "any"];

    /// <summary>
    /// The flat-class boundary, stated per element (decision 030): inheritance
    /// (&lt;subclass&gt;, &lt;joined-subclass&gt;, &lt;union-subclass&gt;), components, joined tables,
    /// the natural key and the special collections are not read, and stay outside the
    /// guarantees of version 1.0 as a whole - the record is how that boundary reaches the
    /// user of this one input instead of the output being silently poorer than it.
    ///
    /// An element that maps a property takes the property out of the model with it
    /// (decision 177), and so does &lt;join&gt; with every property it maps into its table:
    /// the member left in the model would come out as a column of the entity's own table.
    /// A component holds a value type, whose class leaves the conversion too where nothing
    /// maps it as an entity.
    /// </summary>
    private void ReportUnreadClassChildren(XElement classElement)
    {
        foreach (var element in classElement.Elements().Where(e => !ReadClassChildren.Contains(e.Name.LocalName)))
        {
            if (PropertyNamedElements.Contains(element.Name.LocalName) && element.Attribute("name")?.Value is { Length: > 0 } name)
            {
                entityBuilder.ExcludeProperty(
                    name,
                    Opening(element),
                    holdsValue: element.Name.LocalName == "component",
                    valueClass: element.Attribute("class")?.Value);
                continue;
            }

            ReportUnreadElement(element, entityBuilder.EntityMap.Entity.Name);

            if (element.Name.LocalName == "join")
            {
                foreach (var mapped in element.Elements().Where(e => e.Attribute("name")?.Value is { Length: > 0 }))
                {
                    entityBuilder.ExcludeProperty(
                        mapped.Attribute("name")!.Value,
                        $"{Opening(mapped)} under <join table=\"{element.Attribute("table")?.Value}\">, in the joined table",
                        holdsValue: mapped.Name.LocalName == "component",
                        valueClass: mapped.Attribute("class")?.Value);
                }
            }
        }
    }

    /// <summary>The element as the document opens it, which is how a record names it.</summary>
    private static string Opening(XElement element)
    {
        var statedName = element.Attribute("name")?.Value;

        return statedName is null
            ? $"<{element.Name.LocalName}>"
            : $"<{element.Name.LocalName} name=\"{statedName}\">";
    }

    private void ReportUnreadElement(XElement element, string? entity)
        => ReportUnreadElement(
            element,
            entity,
            $"The parser reads only the flat class, so {Opening(element)} and everything it states are dropped (decision 030).");

    private void ReportUnreadElement(XElement element, string? entity, string reason)
    {
        var statedName = element.Attribute("name")?.Value;

        entityBuilder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = entityBuilder.Descriptor.Framework,
            Entity = entity,
            Property = PropertyNamedElements.Contains(element.Name.LocalName) ? statedName : null,
            Reason = reason,
        });
    }

    /// <summary>
    /// Reads the &lt;version&gt; element and its &lt;timestamp&gt; shortcut (decision 030): the
    /// flag, the column facts and the type claim of the property NHibernate versions the
    /// row with. &lt;timestamp&gt; is the framework's spelling of &lt;version type="timestamp"&gt;,
    /// so it carries the same flag and its element name itself is the type claim; the
    /// generated mapping states the same thing as a &lt;version&gt; element. The other
    /// attributes have no counterpart in the model and are reported (decision 048) - except
    /// generated="always" on a binary version, which the generated mapping derives from the
    /// type family itself (decision 067). The family is judged by what the mapping states;
    /// one the catalog supplies later is not known here, so the record errs towards saying.
    /// </summary>
    private void ParseVersion(XElement classElement)
    {
        var versionElem = classElement.Elements()
            .FirstOrDefault(e => e.Name.LocalName is "version" or "timestamp");
        var name = versionElem?.Attribute("name")?.Value;

        if (versionElem is null || string.IsNullOrEmpty(name))
        {
            return;
        }

        var dbProps = ReadColumnFacts(versionElem, name);
        dbProps["IsVersion"] = "true";

        using (Scope(versionElem))
        {
            entityBuilder.SetPropertyDatabaseMapping(name, dbProps);

            if (versionElem.Name.LocalName == "timestamp")
            {
                // The element name claims the family the way the version element's type
                // attribute would spell it.
                entityBuilder.SetPropertyDatabaseType(name, DatabaseType.Timestamp);
            }
            else
            {
                ApplyTypeFacts(versionElem, name);
            }
        }

        var binary = entityBuilder.EntityMap.PropertyMaps
            .FirstOrDefault(pm => pm.Property.Name == name)?.Type is DatabaseType.Binary or DatabaseType.VarBinary or DatabaseType.Blob;

        ReportUnmodelledAttributes(versionElem, UnmodelledVersionAttributes, name,
            (attribute, value) => attribute == "generated" && value == "always" && binary);
        ReportUnreadChildren(versionElem, UnreadMetaChildren, name);
    }

    /// <summary>
    /// Parses the primary key element.
    /// </summary>
    private void ParsePrimaryKey(XElement classElement)
    {
        var compositeElem = classElement.Elements().FirstOrDefault(e => e.Name.LocalName == "composite-id");
        if (compositeElem != null)
        {
            var parts = new List<(string PropertyName, int Order, PrimaryKeyStrategy Strategy)>();
            int order = 1;

            // Both part kinds are read in document order, because the order of the parts
            // is the order of the key.
            foreach (var part in compositeElem.Elements())
            {
                if (part.Name.LocalName == "key-property")
                {
                    var name = part.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var dbProps = ReadColumnFacts(part, name);

                    using (Scope(part))
                    {
                        if (dbProps.Count > 0)
                        {
                            entityBuilder.SetPropertyDatabaseMapping(name, dbProps);
                        }

                        ApplyTypeFacts(part, name);
                    }

                    // <composite-id> admits no generator, so the values are the application's to
                    // supply - that is a statement of the framework, not silence of the source.
                    parts.Add((name, order++, PrimaryKeyStrategy.Assigned));
                    ReportUnmodelledAttributes(part, UnmodelledKeyPartAttributes, name);
                    ReportUnreadChildren(part, UnreadTypedChildren, name);
                    continue;
                }

                if (part.Name.LocalName == "key-many-to-one")
                {
                    using (Scope(part, relation: true))
                    {
                        order = ParseKeyManyToOne(part, parts, order);
                    }

                    if (part.Attribute("name")?.Value is { Length: > 0 } reference)
                    {
                        ReportUnmodelledAttributes(part, UnmodelledKeyPartAttributes, reference);
                        ReportRelationColumnAttributes(part, reference);
                        ReportUnreadChildren(part, UnreadMetaChildren, reference);
                    }
                }
            }

            // mapped="true" beside a class and without a name is the mirrored key class the
            // model keeps (SourceKeyClass), so that one is read, not lost.
            var mirrored = compositeElem.Attribute("class") is not null && compositeElem.Attribute("name") is null;

            ReportUnmodelledAttributes(compositeElem, UnmodelledCompositeIdAttributes, compositeElem.Attribute("name")?.Value,
                (attribute, value) => attribute == "mapped" && IsTrueValue(value) && mirrored);
            ReportUnreadChildren(compositeElem, UnreadMetaChildren, compositeElem.Attribute("name")?.Value);

            if (parts.Count > 0)
            {
                // The key stands at <composite-id>, each part at its own element (decision 125).
                using var keyScope = entityBuilder.At(
                    XmlSource.RangeOf(compositeElem),
                    [.. compositeElem.Elements()
                        .Where(e => e.Name.LocalName is "key-property" or "key-many-to-one")
                        .Where(e => XmlSource.RangeOf(e) is not null)
                        .Select(e => new FactRefinement(MappingFactCategory.PrimaryKey, e.Attribute("name")?.Value, XmlSource.RangeOf(e)!.Value))]);

                entityBuilder.AddPrimaryKey(parts, ReadSourceKeyClass(compositeElem));
            }

            return;
        }

        var idElem = classElement.Elements().FirstOrDefault(e => e.Name.LocalName == "id");
        if (idElem == null)
        {
            return;
        }

        var propName = idElem.Attribute("name")?.Value;
        var generatorElem = idElem.Elements().FirstOrDefault(e => e.Name.LocalName == "generator");
        var genClass = generatorElem?.Attribute("class")?.Value;
        var strategy = PrimaryKeyStrategyConvertor.FromNHibernate(genClass);

        if (string.IsNullOrEmpty(propName))
        {
            return;
        }

        var idDbProps = ReadColumnFacts(idElem, propName);

        // Everything <id> states stands at its own attributes, the strategy at <generator>
        // (decision 125).
        using var scope = Scope(idElem);

        if (idDbProps.Count > 0)
        {
            entityBuilder.SetPropertyDatabaseMapping(propName, idDbProps);
        }

        ApplyTypeFacts(idElem, propName);

        entityBuilder.AddPrimaryKey(strategy, propName);

        // What the generator says beyond the vocabulary: its own name where we narrowed it,
        // and its parameters. Without the parameters a sequence-backed key would translate
        // into a mapping naming no sequence, which compiles and does not run.
        var sourceStrategyName = PrimaryKeyStrategyConvertor.SourceNameFor(genClass, strategy);
        var (parameters, sourceParameters) = ReadGeneratorParameters(generatorElem, genClass, strategy);

        if (sourceStrategyName is not null || parameters.Count > 0 || sourceParameters.Count > 0)
        {
            entityBuilder.SetKeyStrategyDetails(propName, sourceStrategyName, parameters, sourceParameters);
        }

        ReportUnmodelledAttributes(idElem, UnmodelledIdAttributes, propName);
        ReportUnreadChildren(idElem, UnreadTypedChildren, propName);
    }

    /// <summary>
    /// Reads a &lt;key-many-to-one&gt; - a key part that is at the same time a reference to
    /// another entity. The flat key of decision 006 has no reference-typed parts, so the
    /// element is read as its columns, which become scalar key parts, plus an owning
    /// many-to-one relation; the language types of the column parts arrive from the
    /// referenced key when the pairs resolve. Both readings are reported (decision 010):
    /// the reference form is not restated in the output, and a part without stated
    /// columns has nothing to stand on and shortens the key.
    /// </summary>
    /// <returns>The next free key part order.</returns>
    private int ParseKeyManyToOne(
        XElement element, List<(string PropertyName, int Order, PrimaryKeyStrategy Strategy)> parts, int order)
    {
        var name = element.Attribute("name")?.Value;
        var target = element.Attribute("class")?.Value;

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(target))
        {
            return order;
        }

        var columns = ReadRelationColumns(element);

        // The reference is a stated fact either way, so the relation is registered even
        // when its columns are not.
        entityBuilder.AddForeignKey(Cardinality.ManyToOne, name, target, RelationRole.Owning, columns);

        if (columns is null)
        {
            // Without stated columns the default column is the navigation's name, which is
            // no scalar part to build a flat key from; the drop is reported instead of the
            // part vanishing without a trace.
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = name,
                Category = MappingFactCategory.PrimaryKey,
                Reason = $"<key-many-to-one name=\"{name}\"> states no columns, so the flat key (decision 006) "
                    + "has no scalar part to stand in for it; the key comes out with fewer parts than the source described.",
            });
            return order;
        }

        foreach (var column in columns)
        {
            // Assigned for the same reason as a <key-property>: <composite-id> admits no generator.
            parts.Add((column, order++, PrimaryKeyStrategy.Assigned));
        }

        entityBuilder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = entityBuilder.Descriptor.Framework,
            Entity = entityBuilder.EntityMap.Entity.Name,
            Property = name,
            Category = MappingFactCategory.PrimaryKey,
            Reason = $"<key-many-to-one name=\"{name}\"> is read as its column(s) plus a many-to-one relation: "
                + "the key renders flat (decision 006), so the reference form of the key part is not restated in the output.",
        });

        return order;
    }

    /// <summary>
    /// Reads the &lt;param name="..."&gt; children of a generator and canonicalizes the ones whose
    /// meaning the generator class fixes (decision 020): sequence names the sequence of both
    /// sequence and seqhilo, table and column locate the counter of hilo, and max_lo is the
    /// highest low value, so the block holds one value more than it says. A strategy that
    /// stayed on the escape path keeps all of its parameters verbatim - they are not ours to
    /// interpret - and so does any parameter the vocabulary does not name, such as where.
    /// </summary>
    private static (Dictionary<GeneratorParameter, string> Canonical, Dictionary<string, string> Literal)
        ReadGeneratorParameters(XElement? generatorElem, string? generatorClass, PrimaryKeyStrategy strategy)
    {
        var canonical = new Dictionary<GeneratorParameter, string>();
        var literal = new Dictionary<string, string>();

        if (generatorElem is null)
        {
            return (canonical, literal);
        }

        foreach (var param in generatorElem.Elements().Where(e => e.Name.LocalName == "param"))
        {
            var name = param.Attribute("name")?.Value;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var value = param.Value.Trim();

            if (strategy == PrimaryKeyStrategy.Unspecified || !TryCanonicalize(generatorClass, name, value, canonical))
            {
                literal[name] = value;
            }
        }

        return (canonical, literal);
    }

    private static bool TryCanonicalize(
        string? generatorClass, string name, string value, Dictionary<GeneratorParameter, string> canonical)
    {
        switch (generatorClass, name)
        {
            case ("sequence" or "seqhilo", "sequence"):
            {
                var (schema, sequence) = SplitQualifiedName(value);
                canonical[GeneratorParameter.SequenceName] = sequence;
                if (schema is not null)
                {
                    canonical[GeneratorParameter.Schema] = schema;
                }
                return true;
            }
            case ("seqhilo" or "hilo", "max_lo") when int.TryParse(value, out var maxLo):
                // max_lo is the highest low value, so the block holds max_lo + 1 values.
                // Renaming without the shift is the off-by-one trap decision 020 names.
                canonical[GeneratorParameter.BlockSize] = (maxLo + 1).ToString();
                return true;
            case ("hilo", "table"):
            {
                var (schema, table) = SplitQualifiedName(value);
                canonical[GeneratorParameter.CounterTable] = table;
                if (schema is not null)
                {
                    canonical[GeneratorParameter.Schema] = schema;
                }
                return true;
            }
            case ("hilo", "column"):
                canonical[GeneratorParameter.CounterValueColumn] = value;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Splits a possibly schema-qualified name; the schema goes to its own canonical value
    /// (decision 020). A quoted identifier may legitimately contain a dot, so it is left whole
    /// rather than a schema being invented out of it.
    /// </summary>
    private static (string? Schema, string Name) SplitQualifiedName(string value)
    {
        var dot = value.LastIndexOf('.');

        if (dot <= 0 || dot == value.Length - 1 || value.IndexOfAny(['`', '"', '[', ']']) >= 0)
        {
            return (null, value);
        }

        return (value[..dot], value[(dot + 1)..]);
    }

    /// <summary>
    /// Reads the key class of a &lt;composite-id&gt;. NHibernate writes it in two shapes and they
    /// differ in where the key properties live: with the class attribute alone they are properties
    /// of the entity and the class only mirrors them, while with name as well they live inside the
    /// class and the entity holds it in one property. The distinction reaches the query side later
    /// - o.OrderID against o.Id.OrderID - so it is kept rather than flattened away (decision 011).
    /// </summary>
    private static SourceKeyClass? ReadSourceKeyClass(XElement compositeElem)
    {
        var className = compositeElem.Attribute("class")?.Value;

        if (string.IsNullOrEmpty(className))
        {
            return null;
        }

        var propertyName = compositeElem.Attribute("name")?.Value;

        return string.IsNullOrEmpty(propertyName)
            ? new SourceKeyClass(className, KeyClassForm.Mirrored)
            : new SourceKeyClass(className, KeyClassForm.Embedded, propertyName);
    }

    /// <summary>
    /// Reads a boolean attribute. The schema types these as xs:boolean, whose lexical space admits
    /// both true/false and 1/0, and mappings in the wild use either.
    /// </summary>
    private static bool IsTrue(XElement element, string attributeName)
        => element.Attribute(attributeName)?.Value is string value && IsTrueValue(value);

    private static bool IsTrueValue(string value)
        => value == "1" || (bool.TryParse(value, out var parsed) && parsed);

    /// <summary>The false half of the same lexical space; a value that is neither is not false.</summary>
    private static bool IsFalse(string value)
        => value == "0" || (bool.TryParse(value, out var parsed) && !parsed);

    /// <summary>
    /// The columns the references of the class name - &lt;many-to-one&gt; and the
    /// &lt;key-many-to-one&gt; parts of a composite key, the relations whose pairs the
    /// generated mapping checks a property's column against. Case-insensitive, as that
    /// check is.
    /// </summary>
    private static HashSet<string> RelationColumns(XElement classElement)
    {
        var keyParts = classElement.Elements()
            .Where(e => e.Name.LocalName == "composite-id")
            .Elements()
            .Where(e => e.Name.LocalName == "key-many-to-one");

        return new HashSet<string>(
            classElement.Elements()
                .Where(e => e.Name.LocalName == "many-to-one")
                .Concat(keyParts)
                .SelectMany(e => ReadRelationColumns(e) ?? []),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The columns of the identifier as the generated mapping names them - the column of
    /// each key part, or its property's name where it states none - against which it decides
    /// whether a reference over key columns is written read-only.
    /// </summary>
    private HashSet<string> KeyColumns()
        => new(
            entityBuilder.EntityMap.PrimaryKey?.Parts.Select(p => p.PropertyMap.ColumnName ?? p.PropertyMap.Property.Name) ?? [],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the properties of the class element, extracting their names, types, and database attributes.
    /// </summary>
    private void ParseProperties(XElement classElement)
    {
        // unique-key groups are read by ParseUniqueKeys once the relations stand, since a
        // <many-to-one> may be a member as well (decision 130).
        var singles = new List<string>();
        var elements = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var relationColumns = RelationColumns(classElement);

        foreach (var prop in classElement.Elements().Where(e => e.Name.LocalName == "property"))
        {
            var propertyName = prop.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(propertyName))
            {
                continue;
            }

            elements[propertyName] = prop;
            string column;

            // Called even with nothing to record: a property that appears only in the XML
            // descriptor has to exist in the model before anything can be attached to it.
            using (Scope(prop))
            {
                var dbProps = ReadColumnFacts(prop, propertyName);
                column = dbProps.GetValueOrDefault("column") ?? propertyName;

                entityBuilder.SetPropertyDatabaseMapping(propertyName, dbProps);

                ApplyTypeFacts(prop, propertyName);
            }

            var columnElement = prop.Elements().FirstOrDefault(e => e.Name.LocalName == "column");

            // unique="true" is a constraint over that column alone (decision 055), read from
            // the element and from a nested <column>, the two forms NHibernate admits for
            // every column fact - the same pair ReadColumnFacts reads the other facets from.
            if (IsTrue(prop, "unique") || (columnElement is not null && IsTrue(columnElement, "unique")))
            {
                singles.Add(propertyName);
            }

            // The generated mapping writes a column a reference of the entity also names
            // read-only itself, so on such a column insert="false" and update="false" are
            // derived from the model rather than lost (decision 067).
            ReportUnmodelledAttributes(prop, UnmodelledPropertyAttributes, propertyName,
                (attribute, value) => attribute is "insert" or "update" && IsFalse(value) && relationColumns.Contains(column));
            ReportUnreadChildren(prop, UnreadPropertyChildren, propertyName);
        }

        // A constraint stands at the element that states it (decision 125).
        foreach (var propertyName in singles)
        {
            using var scope = Scope(elements[propertyName]);
            entityBuilder.AddUniqueConstraint(null, [propertyName]);
        }
    }

    /// <summary>
    /// The unique-key groups of the class (decision 055): the columns sharing a value form
    /// one constraint, spread over as many elements as it covers, so they are grouped rather
    /// than added one by one. A &lt;property&gt; is a member through the attribute on itself
    /// or on its nested &lt;column&gt;; a &lt;many-to-one&gt; through the attribute on itself,
    /// which covers all its columns, or on every nested &lt;column&gt; - and then its
    /// navigation is the member, standing for the whole foreign key (decision 130). A group
    /// on only some columns of a composite foreign key cannot be named that way and is
    /// reported. Members keep the order of the document (S2); a constraint stands at its
    /// first member (decision 125).
    ///
    /// unique-key on a &lt;many-to-one&gt; says nothing about the multiplicity - that is
    /// unique="true", read by <see cref="ParseRelations"/> as the owning side of a 1:1
    /// (decision 012) - so both on one element are two statements, and both are read.
    /// </summary>
    private void ParseUniqueKeys(XElement classElement)
    {
        var groups = new Dictionary<string, List<(string Member, XElement Element)>>(StringComparer.Ordinal);

        void Join(IEnumerable<string> keys, string member, XElement element)
        {
            foreach (var group in keys)
            {
                if (!groups.TryGetValue(group, out var members))
                {
                    groups[group] = members = [];
                }

                members.Add((member, element));
            }
        }

        foreach (var element in classElement.Elements())
        {
            var member = element.Attribute("name")?.Value;

            if (string.IsNullOrEmpty(member))
            {
                continue;
            }

            var columns = element.Elements().Where(e => e.Name.LocalName == "column").ToList();

            switch (element.Name.LocalName)
            {
                case "property":
                    Join(UniqueKeys(element.Attribute("unique-key") ?? columns.FirstOrDefault()?.Attribute("unique-key")), member, element);
                    break;

                case "many-to-one":
                    var own = UniqueKeys(element.Attribute("unique-key"));
                    var perColumn = columns.Select(c => UniqueKeys(c.Attribute("unique-key"))).ToList();
                    var stated = perColumn.SelectMany(keys => keys).Distinct(StringComparer.Ordinal).ToList();
                    var whole = stated.Where(group => perColumn.All(keys => keys.Contains(group, StringComparer.Ordinal))).ToList();

                    Join(own.Concat(whole).Distinct(StringComparer.Ordinal), member, element);

                    foreach (var group in stated.Except(whole, StringComparer.Ordinal))
                    {
                        using var scope = Scope(element, relation: true);
                        entityBuilder.Report(new ConversionRecord
                        {
                            Kind = ConversionRecordKind.Incompleteness,
                            Framework = entityBuilder.Descriptor.Framework,
                            Entity = entityBuilder.EntityMap.Entity.Name,
                            Property = member,
                            Category = MappingFactCategory.UniqueConstraint,
                            Reason = $"unique-key=\"{group}\" stands on some columns of the foreign key of '{member}' only; the "
                                + "intermediate representation names a foreign key column by its navigation, which stands for all "
                                + "of them, so the constraint was not read (decision 130).",
                        });
                    }

                    break;
            }
        }

        foreach (var (name, members) in groups)
        {
            var first = members[0];
            using var scope = Scope(first.Element, relation: first.Element.Name.LocalName == "many-to-one");
            entityBuilder.AddUniqueConstraint(name, [.. members.Select(m => m.Member)]);
        }
    }

    /// <summary>The groups a unique-key attribute names; NHibernate admits several, comma-separated.</summary>
    private static List<string> UniqueKeys(XAttribute? attribute)
        => attribute?.Value is { } value
            ? [.. value.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0)]
            : [];

    /// <summary>
    /// The type claim of an element, read through the typed channel (decision 019): the
    /// type attribute becomes the family with the facets its name itself carries, and the
    /// sql-type of a nested &lt;column&gt; travels verbatim on the escape path. A type name
    /// outside the vocabulary keeps only its literal spelling and is reported - the family
    /// fact is missing rather than lost, and the catalog may still supply it (decision 010).
    ///
    /// Where the source declared the dialect of another database system, sql-type is dropped
    /// with a loss record and only the type attribute is read (decision 088). The two are not
    /// the same kind of claim: sql-type names a type of one database system, whereas type
    /// names a registered NHibernate IType - "AnsiString" is the framework's own vocabulary
    /// and means the same over every system, so the declaration has nothing to say about it.
    /// </summary>
    private void ApplyTypeFacts(XElement element, string propertyName)
    {
        var columnElement = element.Elements().FirstOrDefault(e => e.Name.LocalName == "column");
        var sqlType = columnElement?.Attribute("sql-type")?.Value;
        sqlType = string.IsNullOrWhiteSpace(sqlType) ? null : sqlType.Trim();

        if (sqlType is not null && ForeignDialect.StopsReading(declaredSourceDialect))
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Category = MappingFactCategory.DatabaseType,
                Reason = ForeignDialect.Reason($"sql-type=\"{sqlType}\""),
            });

            sqlType = null;
        }

        var typeAttr = element.Attribute("type")?.Value;

        if (string.IsNullOrWhiteSpace(typeAttr))
        {
            if (sqlType is not null)
            {
                entityBuilder.SetPropertyDatabaseType(propertyName, null, sourceSqlType: sqlType);
            }

            return;
        }

        var reading = DatabaseTypeConvertor.FromNHibernate(typeAttr);

        // One call per element, so that its two literal spellings never look like two
        // sources of the conversion (decision 017). Where both are stated, sql-type
        // takes the escape path over the type attribute's name, being the database-side
        // spelling of the two - the same precedence ReadColumnFacts gives the nested
        // <column> form.
        entityBuilder.SetPropertyDatabaseType(
            propertyName,
            reading.Type,
            reading.IsUnicode,
            sqlType ?? reading.SourceType,
            reading.Length,
            reading.Precision,
            reading.Scale);

        if (reading.Type is null)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Incompleteness,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Category = MappingFactCategory.DatabaseType,
                Reason = $"The type '{typeAttr.Trim()}' has no family in the neutral vocabulary; its literal "
                    + "spelling is kept on the escape path and no family is claimed (decision 019).",
            });
        }
        else if (reading.Narrowing is not null)
        {
            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Category = MappingFactCategory.DatabaseType,
                Reason = reading.Narrowing,
            });
        }
    }

    /// <summary>
    /// Reads the mapping facts of one column from an &lt;id&gt;, &lt;key-property&gt; or
    /// &lt;property&gt; element. NHibernate accepts them either as attributes of the element or
    /// as a nested &lt;column&gt; element, and for an identifier the nested form is the only one
    /// that can carry precision and scale at all. Where both forms are present the nested one
    /// wins, being the more specific of the two.
    ///
    /// Only the first &lt;column&gt; is read, because the model maps a property to a single
    /// column; a property spread over several columns would take a composite user type
    /// the model does not carry, so each further column is dropped with a record instead
    /// of vanishing (decision 030). Uniqueness travels its own way, as a fact of the entity
    /// rather than of the column (<see cref="ParseUniqueKeys"/>, decision 055); index, check
    /// and default have no counterpart in the model at all and each is reported as a loss
    /// rather than skipped, which is what decision 048 rules for a fact with no place. The
    /// type attribute and the sql-type of a nested &lt;column&gt; do not travel through this
    /// dictionary either: they go through the typed channel of
    /// <see cref="ApplyTypeFacts"/> (decision 019).
    /// </summary>
    /// <summary>
    /// The reading scope of one mapping element (decision 125): the element's start tag, with
    /// each attribute that states a fact named as where that fact stands - its own attribute,
    /// or one of a nested &lt;column&gt; or &lt;generator&gt;. The category an attribute states
    /// depends on the element: column names on a relation element are the foreign key's.
    /// </summary>
    private IDisposable Scope(XElement element, bool relation = false)
        => entityBuilder.At(XmlSource.RangeOf(element), Refinements(element, relation, nested: true));

    private static FactRefinement[] Refinements(XElement element, bool relation, bool nested)
    {
        var refinements = new List<FactRefinement>();

        void Add(XAttribute attribute, MappingFactCategory? category)
        {
            if (category is { } c && XmlSource.RangeOf(attribute) is { } range)
            {
                refinements.Add(new FactRefinement(c, range));
            }
        }

        foreach (var attribute in element.Attributes())
        {
            Add(attribute, attribute.Name.LocalName switch
            {
                "name" when element.Name.LocalName is "id" or "key-property" => MappingFactCategory.PrimaryKey,
                "name" when element.Name.LocalName is "version" or "timestamp" => MappingFactCategory.VersionColumn,
                _ => CategoryOf(attribute.Name.LocalName, relation),
            });
        }

        if (!nested)
        {
            return [.. refinements];
        }

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "column":
                    foreach (var attribute in child.Attributes())
                    {
                        Add(attribute, attribute.Name.LocalName == "name"
                            ? (relation ? MappingFactCategory.ForeignKeyColumns : MappingFactCategory.ColumnName)
                            : CategoryOf(attribute.Name.LocalName, relation));
                    }

                    break;

                case "generator":
                    if (XmlSource.RangeOf(child) is { } generator)
                    {
                        refinements.Add(new FactRefinement(MappingFactCategory.PrimaryKeyStrategy, generator));
                    }

                    break;

                case "key":
                    foreach (var attribute in child.Attributes().Where(a => a.Name.LocalName == "column"))
                    {
                        Add(attribute, MappingFactCategory.ForeignKeyColumns);
                    }

                    foreach (var attribute in child.Elements().Where(e => e.Name.LocalName == "column").SelectMany(e => e.Attributes()).Where(a => a.Name.LocalName == "name"))
                    {
                        Add(attribute, MappingFactCategory.ForeignKeyColumns);
                    }

                    break;
            }
        }

        return [.. refinements];
    }

    private static MappingFactCategory? CategoryOf(string attribute, bool relation) => attribute switch
    {
        "column" => relation ? MappingFactCategory.ForeignKeyColumns : MappingFactCategory.ColumnName,
        "length" => MappingFactCategory.Length,
        "precision" or "scale" => MappingFactCategory.PrecisionAndScale,
        "not-null" => MappingFactCategory.Nullability,
        "type" or "sql-type" => MappingFactCategory.DatabaseType,
        "unique" or "unique-key" => MappingFactCategory.UniqueConstraint,
        "table" => MappingFactCategory.TableName,
        "schema" => MappingFactCategory.SchemaName,
        _ => null,
    };

    private Dictionary<string, string> ReadColumnFacts(XElement element, string propertyName)
    {
        var dbProps = new Dictionary<string, string>();

        if (element.Attribute("column")?.Value is string column && !string.IsNullOrWhiteSpace(column))
        {
            dbProps["column"] = column;
        }

        ReadFacetsInto(dbProps, element);
        ReportUnmodelledFacets(element, propertyName);

        var columnElements = element.Elements().Where(e => e.Name.LocalName == "column").ToList();

        foreach (var surplus in columnElements.Skip(1))
        {
            var surplusName = surplus.Attribute("name")?.Value;

            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Category = MappingFactCategory.ColumnName,
                Reason = $"The model maps a property to a single column, so of the several <column> elements "
                    + $"of '{propertyName}' only the first is read; <column name=\"{surplusName}\"> "
                    + "and everything it states are dropped (decision 030).",
            });
        }

        var columnElement = columnElements.FirstOrDefault();
        if (columnElement is null)
        {
            return dbProps;
        }

        if (columnElement.Attribute("name")?.Value is string nestedName && !string.IsNullOrWhiteSpace(nestedName))
        {
            dbProps["column"] = nestedName;
        }

        ReadFacetsInto(dbProps, columnElement);
        ReportUnmodelledFacets(columnElement, propertyName);
        ReportUnreadChildren(columnElement, UnreadColumnChildren, propertyName);

        return dbProps;
    }

    /// <summary>
    /// Column attributes the intermediate representation has no place for (decision 055).
    /// A plain index is a performance artifact rather than a mapping fact; check and default
    /// carry a literal SQL expression, which is a claim about a database dialect the tool
    /// neither verifies nor translates, and which EF Core could state only through fluent
    /// configuration this solution does not emit. None of the three enters the model - but
    /// none of them vanishes without a record either, which is what decision 048 rules for
    /// a fact the model cannot keep.
    /// </summary>
    private void ReportUnmodelledFacets(XElement element, string propertyName)
    {
        foreach (var name in new[] { "index", "check", "default" })
        {
            if (element.Attribute(name)?.Value is not string value || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Reason = $"The attribute {name}=\"{value}\" has no counterpart in the intermediate "
                    + "representation and was dropped (decision 055).",
            });
        }
    }

    /// <summary>
    /// The attributes of a &lt;class&gt; the model has no place for, each with what its loss
    /// means for the generated artifact. The parser reads name, table and schema and used
    /// to skip everything else without a word, which is the silence decisions 048 and 004
    /// rule out: a mapping fact the model cannot keep is a loss and says so.
    ///
    /// Some of them change what the artifact means rather than only what it states. The
    /// where condition is a permanent filter on every load of the entity, so an output
    /// without it reads rows the source never saw; subselect and catalog move the entity
    /// onto rows other than those of the table the output names; discriminator-value and
    /// abstract belong to a hierarchy the parser does not read at all, an area outside the
    /// guarantees of this version as a whole (decision 030, see architecture.md §9). The
    /// rest are strategy settings NHibernate applies its own default for once the attribute
    /// is gone.
    ///
    /// node is left off this list and the others: it belongs to Hibernate's XML entity
    /// mode, which NHibernate does not have, so it carries nothing the framework acts on.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledClassAttributes =
    [
        ("discriminator-value", "names the class in a table-per-hierarchy mapping, which the parser does not read at all (decision 030)"),
        ("abstract", "marks the class as the abstract root of a hierarchy, which the parser does not read at all (decision 030)"),
        ("where", "restricts every load of the entity to the rows matching it, so without it the target reads the whole table"),
        ("subselect", "maps the entity onto the rows of a SQL query instead of a table, so the output maps it onto a table of that name, which need not exist"),
        ("catalog", "places the table in another database catalog, and without it the target looks for the table in the database of the connection"),
        ("entity-name", "names the entity apart from its class, and without it the target knows the entity by the class name only, so references by that name do not resolve"),
        ("mutable", "says whether the entity may be updated at all, and without it the target treats it as writable"),
        ("optimistic-lock", "chooses the concurrency strategy of the entity, and without it the target applies its own"),
        ("dynamic-insert", "limits the generated INSERT to the columns actually set, and without it the target writes all of them"),
        ("dynamic-update", "limits the generated UPDATE to the columns actually changed, and without it the target writes all of them"),
        ("select-before-update", "makes the framework read the row before updating a detached instance and skip the update when nothing changed, and without it the target updates unconditionally"),
        ("check", "declares a table check constraint in literal SQL, a claim about a database dialect the tool neither verifies nor translates (decision 055)"),
        ("polymorphism", "decides whether a query for a supertype returns instances of this class, and without it the target applies its own default"),
        ("persister", "names a custom class that loads and stores the entity, and without it the target persists it the standard way"),
        ("proxy", "names the type lazy-loading proxies of the class implement, and without it the target proxies the class itself"),
        ("batch-size", "sizes the batch the framework fetches proxies of this class in, which is a fetching strategy and not a mapping fact"),
        ("lazy", "says whether the class is proxied for lazy loading, and without it the target applies its own default"),
        ("rowid", "lets the framework address rows by a database row identifier, and without it the target addresses them by the key"),
        ("schema-action", "limits what NHibernate's schema tooling does with the table, and without it the tooling applies every action"),
    ];

    /// <summary>
    /// The same for an &lt;id&gt;, whose read attributes are name, column, type and length,
    /// and its &lt;generator&gt;.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledIdAttributes =
    [
        ("access", "chooses how the framework reaches the member, and without it the target goes through the property"),
        ("unsaved-value", "names the identifier value that marks an instance as not yet saved, and without it the target infers it from the identifier's type"),
    ];

    /// <summary>
    /// The same for a &lt;version&gt; or &lt;timestamp&gt;, whose read attributes are name,
    /// column, type and the facets. The two elements share the list; each states only the
    /// attributes its schema admits.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledVersionAttributes =
    [
        ("generated", "says whether the database produces the version and the framework reads it back, and the generated mapping states that only for a binary version"),
        ("source", "says whether the timestamp comes from the application's clock or from the database, and without it the target takes it from the application"),
        ("insert", "says whether the version column takes part in the generated INSERT, and without it the target writes it"),
        ("unsaved-value", "names the version value that marks an instance as not yet saved, and without it the target decides by the identifier"),
        ("access", "chooses how the framework reaches the member, and without it the target goes through the property"),
    ];

    /// <summary>
    /// The same for a &lt;many-to-one&gt; or &lt;one-to-one&gt;, whose read attributes are name,
    /// class, column, unique, unique-key and constrained; property-ref has a record of its
    /// own in <see cref="ParseRelations"/>. formula is the one that changes what the
    /// artifact means: the reference is joined over an expression, and the output joins it
    /// over a column the target derives from the navigation's name.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledReferenceAttributes =
    [
        ("formula", "joins the reference over a SQL expression instead of a column, so the output joins it over the column the target derives from the navigation's name"),
        ("not-null", "says the reference is required, which a relation in the model cannot say, and without it the target treats the reference as optional unless a property over the same column says otherwise"),
        ("not-found", "says what loading does when the referenced row is missing, and without it the target applies its own default"),
        ("cascade", "propagates operations on the entity to the referenced one, and without it the target cascades nothing"),
        ("insert", "says whether the foreign key takes part in the generated INSERT, and without it the target writes it"),
        ("update", "says whether the foreign key takes part in the generated UPDATE, and without it the target writes it"),
        ("entity-name", "names the referenced entity apart from its class, and without it the reference is resolved by the class alone"),
        ("foreign-key", "names the foreign key constraint the schema tooling creates, and without it the tooling names it itself"),
        ("index", "names a plain index over the foreign key, a performance artifact rather than a mapping fact (decision 055)"),
        ("lazy", "says whether the referenced entity is loaded on access through a proxy, and without it the target applies its own default"),
        ("fetch", "chooses how the referenced entity is fetched, which is a fetching strategy and not a mapping fact"),
        ("outer-join", "chooses how the referenced entity is fetched, which is a fetching strategy and not a mapping fact"),
        ("optimistic-lock", "says whether a change to this reference takes part in the entity's optimistic lock, and without it the target counts it in"),
        ("access", "chooses how the framework reaches the member, and without it the target goes through the property"),
    ];

    /// <summary>
    /// The same for a &lt;composite-id&gt;, whose read attributes are class and name (the
    /// key class of decision 011).
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledCompositeIdAttributes =
    [
        ("mapped", "says whether the key class mirrors properties of the entity, while the model reads the form of the key class from class and name"),
        ("unsaved-value", "says how the framework tells an instance not yet saved by its key, and without it the target applies its own default"),
        ("access", "chooses how the framework reaches the member, and without it the target goes through the property"),
    ];

    /// <summary>
    /// The same for a &lt;key-property&gt; or &lt;key-many-to-one&gt;, whose read attributes are
    /// name, column, type and length, or name, class and column. The two share the list;
    /// each states only the attributes its schema admits.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledKeyPartAttributes =
    [
        ("not-found", "says what loading does when the referenced row is missing, and without it the target applies its own default"),
        ("entity-name", "names the referenced entity apart from its class, and without it the reference is resolved by the class alone"),
        ("foreign-key", "names the foreign key constraint the schema tooling creates, and without it the tooling names it itself"),
        ("lazy", "says whether the referenced entity is loaded on access through a proxy, and without it the target applies its own default"),
        ("access", "chooses how the framework reaches the member, and without it the target goes through the property"),
    ];

    /// <summary>
    /// The same for a &lt;bag&gt;, &lt;set&gt;, &lt;list&gt; or &lt;map&gt; that holds an association.
    /// Read are name, table and schema (the junction facts of decision 005); inverse and
    /// cascade have their own records. where, subselect and catalog change which rows the
    /// collection holds, order-by and sort the order it holds them in.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledCollectionAttributes =
    [
        ("where", "restricts the collection to the rows matching it, so without it the target loads every associated row"),
        ("subselect", "loads the collection from the rows of a SQL query instead of a table, so without it the target reads the table"),
        ("catalog", "places the collection table in another database catalog, and without it the target looks for the table in the database of the connection"),
        ("order-by", "orders the loaded collection by a SQL expression, and without it the target loads it in no stated order"),
        ("sort", "keeps the collection sorted in memory, and without it the target leaves it unsorted"),
        ("mutable", "says whether the collection may change at all, and without it the target treats it as writable"),
        ("check", "declares a check constraint on the collection table in literal SQL, a claim about a database dialect the tool neither verifies nor translates (decision 055)"),
        ("collection-type", "names a custom collection type, and without it the target uses its own"),
        ("persister", "names a custom class that loads and stores the collection, and without it the target persists it the standard way"),
        ("generic", "says whether the collection is generic, and without it the target infers it from the member's type"),
        ("lazy", "says whether the collection is loaded on access, and without it the target applies its own default"),
        ("fetch", "chooses how the collection is fetched, which is a fetching strategy and not a mapping fact"),
        ("outer-join", "chooses how the collection is fetched, which is a fetching strategy and not a mapping fact"),
        ("batch-size", "sizes the batch the framework fetches collections of this role in, which is a fetching strategy and not a mapping fact"),
        ("optimistic-lock", "says whether a change to the collection takes part in the owner's optimistic lock, and without it the target counts it in"),
        ("access", "chooses how the framework reaches the member, and without it the target goes through the property"),
    ];

    /// <summary>
    /// The same for the &lt;key&gt; of a collection, whose read attribute is column.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledCollectionKeyAttributes =
    [
        ("property-ref", "makes the foreign key reference a property of the owner instead of its key, and without it the target references the owner's key"),
        ("on-delete", "lets the database delete the rows of the collection with their owner, and without it the target deletes them itself"),
        ("not-null", "says the foreign key column is NOT NULL, and without it the target treats it as nullable"),
        ("unique", "says the foreign key is unique, so that each owner has at most one row, and without it the target lets it repeat"),
        ("update", "says whether the foreign key is ever updated, and without it the target updates it"),
        ("foreign-key", "names the foreign key constraint the schema tooling creates, and without it the tooling names it itself"),
    ];

    /// <summary>
    /// The same for the &lt;one-to-many&gt; of a collection, whose read attribute is class.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledOneToManyAttributes =
    [
        ("not-found", "says what loading does when an associated row is missing, and without it the target applies its own default"),
        ("entity-name", "names the associated entity apart from its class, and without it the association is resolved by the class alone"),
    ];

    /// <summary>
    /// The same for the &lt;many-to-many&gt; of a collection, whose read attributes are class
    /// and column. where and formula change which rows the collection holds.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledManyToManyAttributes =
    [
        ("where", "restricts the associated entities to the rows matching it, so without it the target loads every associated row"),
        ("formula", "joins the associated entity over a SQL expression instead of a junction column, which the model has no place for"),
        ("property-ref", "makes the junction reference a property of the associated entity instead of its key, and without it the target references the key"),
        ("order-by", "orders the associated entities by a SQL expression, and without it the target loads them in no stated order"),
        ("unique", "says each associated entity belongs to one owner at most, and without it the target lets it repeat"),
        ("not-found", "says what loading does when an associated row is missing, and without it the target applies its own default"),
        ("entity-name", "names the associated entity apart from its class, and without it the association is resolved by the class alone"),
        ("foreign-key", "names the foreign key constraint the schema tooling creates, and without it the tooling names it itself"),
        ("lazy", "says whether the associated entities are loaded on access through proxies, and without it the target applies its own default"),
        ("fetch", "chooses how the associated entities are fetched, which is a fetching strategy and not a mapping fact"),
        ("outer-join", "chooses how the associated entities are fetched, which is a fetching strategy and not a mapping fact"),
    ];

    /// <summary>
    /// The same for a &lt;column&gt; nested in a relation element - &lt;many-to-one&gt;,
    /// &lt;key-many-to-one&gt;, &lt;key&gt; or &lt;many-to-many&gt; - of which only name is read
    /// (and unique-key under a &lt;many-to-one&gt;). The model keeps the facets of a column on
    /// the property mapped to it; a foreign key column is named by a relation, which has none.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledRelationColumnAttributes =
    [
        ("sql-type", "states the SQL type of a foreign key column, which a relation in the model cannot carry; the output states it only where a property over the same column does"),
        ("length", "sizes a foreign key column, which a relation in the model cannot carry; the output states it only where a property over the same column does"),
        ("precision", "sizes a foreign key column, which a relation in the model cannot carry; the output states it only where a property over the same column does"),
        ("scale", "sizes a foreign key column, which a relation in the model cannot carry; the output states it only where a property over the same column does"),
        ("not-null", "says a foreign key column is NOT NULL, which a relation in the model cannot carry; the output states it only where a property over the same column does"),
        ("unique", "makes a foreign key column unique on its own, which a relation in the model cannot carry"),
        ("unique-key", "makes a foreign key column a member of a unique constraint the model cannot place on this column"),
        ("index", "names a plain index over a foreign key column, a performance artifact rather than a mapping fact (decision 055)"),
        ("check", "declares a check constraint in literal SQL, a claim about a database dialect the tool neither verifies nor translates (decision 055)"),
        ("default", "declares a default value in literal SQL, a claim about a database dialect the tool neither verifies nor translates (decision 055)"),
    ];

    /// <summary>
    /// The same for a &lt;property&gt;, whose read attributes are name, column, type and the
    /// facets <see cref="ReadFacetsInto"/> and <see cref="ParseProperties"/> take.
    ///
    /// formula is the one that changes what the artifact means: a property read from a SQL
    /// expression has no column at all, while the model requires one, so the output does
    /// not merely lose the expression - it invents a column in its place. The framework
    /// comparison (docs/analysis/orm-frameworks-comparison.md) names it as an expressive
    /// capability of NHibernate, which is exactly the kind of fact F11 asks to be told
    /// about rather than left to be discovered in the output.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnmodelledPropertyAttributes =
    [
        ("formula", "reads the property from a SQL expression instead of a column, so it has no column of its own; the model requires one and the output names it after the property"),
        ("access", "chooses how the framework reaches the member, and without it the target goes through the property"),
        ("insert", "says whether the column takes part in the generated INSERT, and without it the target writes it"),
        ("update", "says whether the column takes part in the generated UPDATE, and without it the target writes it"),
        ("lazy", "defers loading of this one property until it is read, and without it the target loads it with the row"),
        ("generated", "says the database produces the value and the framework reads it back, and without it the target treats the value as the application's"),
        ("optimistic-lock", "says whether a change to this property takes part in the entity's optimistic lock, and without it the target counts it in"),
    ];

    /// <summary>
    /// Reports every attribute of the list the element actually states (decision 048). The
    /// list is named rather than derived as the complement of what is read: an attribute
    /// gets a record because it carries a mapping fact the model cannot keep, and what each
    /// one costs is said in the record instead of being left to the reader - which a
    /// complement could not do. Nothing here parses the value; the record repeats it as the
    /// source wrote it, which is what makes it possible to put the fact back by hand.
    ///
    /// The record carries no <see cref="MappingFactCategory"/>, for the reason decision 048
    /// gives: the categories are a closed vocabulary of facts the model holds, and none of
    /// these is one of them, so naming the nearest would claim more than is known.
    ///
    /// <paramref name="survives"/> names the stated values whose fact the model does keep.
    /// Mostly that separates a stated attribute from a derived one (the criterion of
    /// decision 067): the generated mapping writes a few of these attributes itself, from
    /// facts the model holds, and where it would write the same value nothing is lost -
    /// which is also what keeps a round trip of the tool's own output free of records about
    /// what it wrote. Elsewhere the value is read in one context and not in another.
    /// </summary>
    private void ReportUnmodelledAttributes(
        XElement element,
        (string Name, string Consequence)[] attributes,
        string? propertyName,
        Func<string, string, bool>? survives = null)
    {
        foreach (var (name, consequence) in attributes)
        {
            if (element.Attribute(name)?.Value is not string value || string.IsNullOrWhiteSpace(value)
                || survives?.Invoke(name, value) == true)
            {
                continue;
            }

            entityBuilder.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Loss,
                Framework = entityBuilder.Descriptor.Framework,
                Entity = entityBuilder.EntityMap.Entity.Name,
                Property = propertyName,
                Reason = $"The attribute {name}=\"{value}\" of <{element.Name.LocalName}> has no counterpart "
                    + $"in the intermediate representation and was dropped (decision 048): it {consequence}.",
            });
        }
    }

    /// <summary>
    /// The child elements the model has no place for, under the elements this parser reads
    /// below &lt;class&gt; - the counterpart of the attribute lists above. An unread child of
    /// &lt;class&gt; itself is the flat-class boundary and has its record from
    /// <see cref="ReportUnreadClassChildren"/>; a child of a read element would otherwise
    /// vanish with nothing said (decision 048). Each list names what the element costs, for
    /// the reason <see cref="ReportUnmodelledAttributes"/> gives; elements the parser reads
    /// (&lt;column&gt;, &lt;generator&gt;, &lt;key&gt;, the association of a collection) or reports
    /// elsewhere (the index of &lt;list&gt;, the keys of &lt;map&gt;) are not on them.
    /// </summary>
    private static readonly (string Name, string Consequence) UnreadMeta =
        ("meta", "attaches metadata for code generation and tooling, which the model does not carry");

    /// <summary>The children of &lt;composite-id&gt;, &lt;key-many-to-one&gt;, &lt;version&gt; and &lt;timestamp&gt;.</summary>
    private static readonly (string Name, string Consequence)[] UnreadMetaChildren = [UnreadMeta];

    /// <summary>The children of &lt;id&gt; and &lt;key-property&gt;.</summary>
    private static readonly (string Name, string Consequence)[] UnreadTypedChildren =
    [
        ("type", "states the NHibernate type of the member - a custom one, with its parameters - which the parser reads only from the type attribute, so the output types the column by the member's type"),
        UnreadMeta,
    ];

    /// <summary>The children of &lt;property&gt;; formula is the attribute's fact in element form.</summary>
    private static readonly (string Name, string Consequence)[] UnreadPropertyChildren =
    [
        ("formula", "reads the property from a SQL expression instead of a column, so it has no column of its own; the model requires one and the output names it after the property"),
        ("type", "states the NHibernate type of the member - a custom one, with its parameters - which the parser reads only from the type attribute, so the output types the column by the member's type"),
        UnreadMeta,
    ];

    /// <summary>The children of &lt;many-to-one&gt; and &lt;one-to-one&gt;.</summary>
    private static readonly (string Name, string Consequence)[] UnreadReferenceChildren =
    [
        ("formula", "joins the reference over a SQL expression instead of a column, so the output joins it over the column the target derives from the navigation's name"),
        UnreadMeta,
    ];

    /// <summary>
    /// The children of a &lt;bag&gt;, &lt;set&gt;, &lt;list&gt; or &lt;map&gt; that holds an
    /// association. filter and subselect change which rows the collection holds; loader and
    /// the sql- elements replace the SQL the framework would generate for it.
    /// </summary>
    private static readonly (string Name, string Consequence)[] UnreadCollectionChildren =
    [
        ("filter", "restricts the collection's rows by a named filter whenever the session enables it, and without it the target never restricts them"),
        ("subselect", "loads the collection from the rows of a SQL query instead of a table, so without it the target reads the table"),
        ("loader", "loads the collection with a named query instead of generated SQL, and without it the target generates its own"),
        ("sql-insert", "replaces the generated INSERT of the collection with custom SQL, and without it the target generates its own"),
        ("sql-update", "replaces the generated UPDATE of the collection with custom SQL, and without it the target generates its own"),
        ("sql-delete", "replaces the generated DELETE of a collection row with custom SQL, and without it the target generates its own"),
        ("sql-delete-all", "replaces the generated DELETE of the whole collection with custom SQL, and without it the target generates its own"),
        ("synchronize", "names tables whose pending changes are flushed before the collection is loaded from a subselect, and without it the target flushes none"),
        ("cache", "caches the collection in the second-level cache, which is a caching strategy and not a mapping fact"),
        ("comment", "is a comment the schema tooling writes for the collection table, which the model does not carry"),
        UnreadMeta,
    ];

    /// <summary>The children of the &lt;many-to-many&gt; of a collection besides its columns.</summary>
    private static readonly (string Name, string Consequence)[] UnreadManyToManyChildren =
    [
        ("formula", "joins the associated entity over a SQL expression instead of a junction column, which the model has no place for"),
        ("filter", "restricts the associated entities by a named filter whenever the session enables it, and without it the target never restricts them"),
        UnreadMeta,
    ];

    /// <summary>The children of a &lt;column&gt;.</summary>
    private static readonly (string Name, string Consequence)[] UnreadColumnChildren =
    [
        ("comment", "is a comment the schema tooling writes for the column, which the model does not carry"),
    ];

    /// <summary>
    /// Reports every child of the list the element actually contains, in document order and
    /// once per occurrence - two filters are two facts. The record carries no category, as
    /// the attribute records do not.
    /// </summary>
    private void ReportUnreadChildren(
        XElement element,
        (string Name, string Consequence)[] children,
        string? propertyName)
    {
        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;

            if (Array.FindIndex(children, c => c.Name == name) is var index and >= 0)
            {
                entityBuilder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = entityBuilder.Descriptor.Framework,
                    Entity = entityBuilder.EntityMap.Entity.Name,
                    Property = propertyName,
                    Reason = $"The element {Opening(child)} of <{element.Name.LocalName}> has no counterpart "
                        + $"in the intermediate representation and was dropped (decision 048): it {children[index].Consequence}.",
                });
            }
        }
    }

    /// <summary>
    /// Facets an element and a nested &lt;column&gt; spell the same way.
    /// </summary>
    private static void ReadFacetsInto(Dictionary<string, string> dbProps, XElement element)
    {
        // Read three-state through the same lexical space as the other boolean attributes
        // (see IsTrue): an absent attribute states nothing, so it must not become a claim,
        // while both `true`/`false` and `1`/`0` are legal spellings of one.
        if (element.Attribute("not-null")?.Value is string notNullText)
        {
            if (notNullText == "1" || (bool.TryParse(notNullText, out var parsed) && parsed))
            {
                dbProps["nullable"] = "false";
            }
            else if (notNullText == "0" || bool.TryParse(notNullText, out _))
            {
                dbProps["nullable"] = "true";
            }
        }

        if (element.Attribute("precision")?.Value is string precision && !string.IsNullOrWhiteSpace(precision))
        {
            dbProps["precision"] = precision;
        }

        if (element.Attribute("scale")?.Value is string scale && !string.IsNullOrWhiteSpace(scale))
        {
            dbProps["scale"] = scale;
        }

        if (element.Attribute("length")?.Value is string length && !string.IsNullOrWhiteSpace(length))
        {
            dbProps["length"] = length;
        }
    }

    /// <summary>
    /// Parses the relations defined in the class element, such as one-to-one, many-to-one, one-to-many, and many-to-many.
    /// </summary>
    private void ParseRelations(XElement classElement)
    {
        var identifierColumns = KeyColumns();

        foreach (var relation in classElement.Elements().Where(e =>
             e.Name.LocalName == "one-to-one" ||
             e.Name.LocalName == "many-to-one"))
        {
            var propName = relation.Attribute("name")?.Value;
            var target = relation.Attribute("class")?.Value;
            if (string.IsNullOrEmpty(propName) || string.IsNullOrEmpty(target))
            {
                continue;
            }

            // The element name describes the shape of the columns, not the multiplicity: a
            // <many-to-one unique="true"> is the owning side of a 1:1, while <one-to-one> is the
            // side that holds no foreign key at all (decision 012).
            var isOneToOne = relation.Name.LocalName == "one-to-one" || IsTrue(relation, "unique");

            // <one-to-one> owns the key only where it says its own identity is constrained by the
            // other entity, which is the shared primary key case. Otherwise the key sits on the far
            // side and property-ref names the property holding it - a value the model has nowhere
            // to keep, so of the whole attribute only the role survives.
            var role = relation.Name.LocalName == "one-to-one" && !IsTrue(relation, "constrained")
                ? RelationRole.Inverse
                : RelationRole.Owning;

            if (relation.Attribute("property-ref")?.Value is string propertyRef
                && !string.IsNullOrWhiteSpace(propertyRef))
            {
                // The value names a property of the other entity; the model keeps only the
                // role, so the drop is reported instead of happening silently (decision 010).
                entityBuilder.Report(new ConversionRecord
                {
                    Kind = ConversionRecordKind.Loss,
                    Framework = entityBuilder.Descriptor.Framework,
                    Entity = entityBuilder.EntityMap.Entity.Name,
                    Property = propName,
                    Reason = $"property-ref=\"{propertyRef}\" names a property of the referenced entity; the model has nowhere to keep the value, only the inverse role survives, and the generated mapping derives the attribute anew from the counterpart navigation - only where the counterpart takes part in the same conversion (decision 012).",
                });
            }

            // <one-to-one> admits no column by schema, so only <many-to-one> can state any.
            var columns = ReadRelationColumns(relation);

            using (Scope(relation, relation: true))
            {
                entityBuilder.AddForeignKey(
                    isOneToOne ? Cardinality.OneToOne : Cardinality.ManyToOne,
                    propName,
                    target,
                    role,
                    columns);
            }

            // The generated mapping writes a reference over key columns read-only itself, so
            // there insert="false" and update="false" are derived from the model rather than
            // lost (decision 067).
            ReportUnmodelledAttributes(relation, UnmodelledReferenceAttributes, propName,
                (attribute, value) => attribute is "insert" or "update" && IsFalse(value)
                    && columns?.Any(identifierColumns.Contains) == true);
            ReportRelationColumnAttributes(relation, propName);
            ReportUnreadChildren(relation, UnreadReferenceChildren, propName);
        }

        string[] collectionTypes = ["bag", "set", "list", "map"];
        foreach (var collection in classElement.Elements().Where(e => collectionTypes.Contains(e.Name.LocalName)))
        {
            var propName = collection.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(propName))
            {
                continue;
            }

            // The <key> of a collection names the columns of the child table; the parser of the
            // parent cannot pair them with anything, so they travel with the relation and the
            // pairing happens before generation, once the child is part of the same conversion.
            var keyElement = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "key");
            var keyColumns = keyElement is null ? null : ReadRelationColumns(keyElement);

            // The relation stands at the collection element, its columns at <key> (decision 125).
            using var scope = Scope(collection, relation: true);

            var oneToMany = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "one-to-many");
            if (oneToMany != null)
            {
                var target = oneToMany.Attribute("class")?.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    entityBuilder.AddForeignKey(Cardinality.OneToMany, propName, target, foreignKeyColumns: keyColumns);
                    ApplyCollectionShape(collection, propName);
                    ReportCollectionAttributes(collection, keyElement, oneToMany, UnmodelledOneToManyAttributes, propName);
                }
                continue;
            }

            var manyToMany = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "many-to-many");
            if (manyToMany is null)
            {
                // Neither association element is there, so the collection holds something
                // the model has no place for - values (<element>), a component, a nested
                // collection. The four collection names are on the read list, so the
                // boundary reporter never sees them and this is the only place that can
                // say so (decision 030); the property leaves the model with it (decision 177),
                // and the class of a composite element with the property.
                var compositeElement = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "composite-element");
                entityBuilder.ExcludeProperty(
                    propName,
                    $"<{collection.Name.LocalName} name=\"{propName}\"> without <one-to-many> or <many-to-many>, "
                    + "a collection of values rather than an association between entities",
                    holdsValue: compositeElement is not null,
                    valueClass: compositeElement?.Attribute("class")?.Value);
                continue;
            }

            var manyToManyTarget = manyToMany.Attribute("class")?.Value;
            if (!string.IsNullOrEmpty(manyToManyTarget))
            {
                // The <key> and <many-to-many> columns belong to a junction table, not
                // to the target entity; together with the collection's table they are
                // what the junction entity is synthesized from (decision 005).
                entityBuilder.AddForeignKey(
                    Cardinality.ManyToMany,
                    propName,
                    manyToManyTarget,
                    foreignKeyColumns: keyColumns,
                    junction: new JunctionFacts(
                        collection.Attribute("table")?.Value,
                        collection.Attribute("schema")?.Value,
                        ReadRelationColumns(manyToMany)));
                ApplyCollectionShape(collection, propName);
                ReportCollectionAttributes(collection, keyElement, manyToMany, UnmodelledManyToManyAttributes, propName);
            }
        }
    }

    /// <summary>
    /// Carries the shape of a collection element into the model and reports what has no
    /// home there. The element name is the kind of decision 014: &lt;set&gt; is Set, &lt;list&gt;
    /// is List, and &lt;bag&gt; states nothing beyond the default, so it stays Unspecified.
    /// The kind fills only an empty fact - the entity text outranks the mapping artifact
    /// (decision 017). What does not survive is reported by the parser, the only place
    /// that still sees it (decision 010): the index column of a &lt;list&gt;, whose order
    /// therefore holds in memory only; the &lt;map&gt; shape, which would need a key type
    /// (decision 014); and the inverse and cascade attributes, which the model does not
    /// keep - the generated mapping derives inverse from the shape of both sides and
    /// states no cascade at all.
    /// </summary>
    private void ApplyCollectionShape(XElement collection, string propertyName)
    {
        switch (collection.Name.LocalName)
        {
            case "set":
                entityBuilder.SetCollectionKind(propertyName, CollectionKind.Set);
                break;
            case "list":
                entityBuilder.SetCollectionKind(propertyName, CollectionKind.List);
                ReportCollectionShapeLoss(propertyName,
                    "The index column of <list> has no home in the model, so the order it carries is dropped; "
                    + "the generated mapping renders the collection as <bag>.");
                break;
            case "map":
                ReportCollectionShapeLoss(propertyName,
                    "The <map> shape needs a key type the model does not carry (decision 014); "
                    + "the collection is read as a plain one and the generated mapping renders it as <bag>.");
                break;
        }

        if (collection.Attribute("inverse")?.Value is string inverse)
        {
            ReportCollectionShapeLoss(propertyName,
                $"inverse=\"{inverse}\" is not kept by the model; the generated mapping derives the attribute "
                + "from whether the owning side of the relation is part of the conversion instead of restating it.");
        }

        if (collection.Attribute("cascade")?.Value is string cascade)
        {
            ReportCollectionShapeLoss(propertyName,
                $"cascade=\"{cascade}\" has no home in the model; the generated mapping states no cascade "
                + "and the target's default (none) applies.");
        }
    }

    /// <summary>
    /// Reports what a read collection states beyond what the model keeps (decision 048): its
    /// own attributes, those of its &lt;key&gt; and of the association element, and the facets
    /// of the columns under both. inverse and cascade have their records in
    /// <see cref="ApplyCollectionShape"/>; table and schema are read as the junction facts of
    /// a many-to-many, and a one-to-many has no table of its own for them to name.
    /// </summary>
    private void ReportCollectionAttributes(
        XElement collection,
        XElement? key,
        XElement association,
        (string Name, string Consequence)[] associationAttributes,
        string propertyName)
    {
        ReportUnmodelledAttributes(collection, UnmodelledCollectionAttributes, propertyName);
        ReportUnreadChildren(collection, UnreadCollectionChildren, propertyName);

        if (key is not null)
        {
            ReportUnmodelledAttributes(key, UnmodelledCollectionKeyAttributes, propertyName);
            ReportRelationColumnAttributes(key, propertyName);
        }

        ReportUnmodelledAttributes(association, associationAttributes, propertyName);
        ReportRelationColumnAttributes(association, propertyName);

        // <one-to-many> admits no children by schema.
        ReportUnreadChildren(association, UnreadManyToManyChildren, propertyName);
    }

    /// <summary>
    /// The facets of the nested &lt;column&gt; elements of a relation element, of which only
    /// the name is read - and, under a &lt;many-to-one&gt;, unique-key (decision 130).
    /// </summary>
    private void ReportRelationColumnAttributes(XElement relation, string propertyName)
    {
        var readsUniqueKey = relation.Name.LocalName == "many-to-one";

        foreach (var column in relation.Elements().Where(e => e.Name.LocalName == "column"))
        {
            ReportUnmodelledAttributes(column, UnmodelledRelationColumnAttributes, propertyName,
                (attribute, _) => readsUniqueKey && attribute == "unique-key");
            ReportUnreadChildren(column, UnreadColumnChildren, propertyName);
        }
    }

    private void ReportCollectionShapeLoss(string propertyName, string reason)
    {
        entityBuilder.Report(new ConversionRecord
        {
            Kind = ConversionRecordKind.Loss,
            Framework = entityBuilder.Descriptor.Framework,
            Entity = entityBuilder.EntityMap.Entity.Name,
            Property = propertyName,
            Reason = reason,
        });
    }

    /// <summary>
    /// Reads the foreign key columns of a <many-to-one> or a collection's <key>, in the
    /// source's order. One column is an attribute, several are nested <column> elements;
    /// where both appear the nested form wins, being the more specific of the two - the
    /// same precedence <see cref="ReadColumnFacts"/> applies.
    /// </summary>
    private static List<string>? ReadRelationColumns(XElement element)
    {
        var nested = element.Elements()
            .Where(e => e.Name.LocalName == "column")
            .Select(e => e.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        if (nested.Count > 0)
        {
            return nested;
        }

        var single = element.Attribute("column")?.Value;

        return string.IsNullOrWhiteSpace(single) ? null : [single];
    }
}
