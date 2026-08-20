using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
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
public class NHibernateXMLMappingParser(AbstractEntityBuilder entityBuilder) : IParser
{
    public bool CanParse(ConversionContentType contentType)
    {
        return contentType == ConversionContentType.XML;
    }

    /// <summary>
    /// Parses an NHibernate mapping XML file from the provided source code string.
    /// </summary>
    /// <param name="source">String containing XML mapping file</param>
    public void Parse(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        var xmlDoc = XDocument.Parse(source.Trim());
        var mapping = xmlDoc.Root;
        if (mapping == null || mapping.Name.LocalName != "hibernate-mapping")
        {
            return;
        }

        ParseMapping(mapping);
    }

    /// <summary>
    /// Parses the mapping element of the NHibernate XML mapping file.
    /// </summary>
    private void ParseMapping(XElement mapping)
    {
        var mappingNamespace = mapping.Attribute("namespace")?.Value;

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
        }
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
        // Header (class name)
        //var nameAttr = classElement.Attribute("name")?.Value;
        //if (!string.IsNullOrEmpty(nameAttr))
        //{
        //    var fullType = nameAttr.Split(',')[0].Trim();
        //    var className = fullType.Contains('.')
        //        ? fullType[(fullType.LastIndexOf('.') + 1)..]
        //        : fullType;

        //    entityBuilder.AddClassHeader(string.Empty, className);
        //}

        // Table and schema
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

        ParsePrimaryKey(classElement);
        ParseProperties(classElement);
        ParseRelations(classElement);
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

                    var dbProps = ReadColumnFacts(part);

                    if (dbProps.Count > 0)
                    {
                        entityBuilder.SetPropertyDatabaseMapping(name, dbProps);
                    }

                    ApplyTypeFacts(part, name);

                    // <composite-id> admits no generator, so the values are the application's to
                    // supply - that is a statement of the framework, not silence of the source.
                    parts.Add((name, order++, PrimaryKeyStrategy.Assigned));
                    continue;
                }

                if (part.Name.LocalName == "key-many-to-one")
                {
                    order = ParseKeyManyToOne(part, parts, order);
                }
            }

            if (parts.Count > 0)
            {
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

        var idDbProps = ReadColumnFacts(idElem);

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
    {
        var value = element.Attribute(attributeName)?.Value;

        return value == "1" || (bool.TryParse(value, out var parsed) && parsed);
    }

    /// <summary>
    /// Parses the properties of the class element, extracting their names, types, and database attributes.
    /// </summary>
    private void ParseProperties(XElement classElement)
    {
        foreach (var prop in classElement.Elements().Where(e => e.Name.LocalName == "property"))
        {
            var propertyName = prop.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(propertyName))
            {
                continue;
            }

            // Called even with nothing to record: a property that appears only in the XML
            // descriptor has to exist in the model before anything can be attached to it.
            entityBuilder.SetPropertyDatabaseMapping(
                propertyName,
                ReadColumnFacts(prop)
            );

            ApplyTypeFacts(prop, propertyName);
        }
    }

    /// <summary>
    /// The type claim of an element, read through the typed channel (decision 019): the
    /// type attribute becomes the family with the facets its name itself carries, and the
    /// sql-type of a nested &lt;column&gt; travels verbatim on the escape path. A type name
    /// outside the vocabulary keeps only its literal spelling and is reported - the family
    /// fact is missing rather than lost, and the catalog may still supply it (decision 010).
    /// </summary>
    private void ApplyTypeFacts(XElement element, string propertyName)
    {
        var columnElement = element.Elements().FirstOrDefault(e => e.Name.LocalName == "column");
        var sqlType = columnElement?.Attribute("sql-type")?.Value;

        if (!string.IsNullOrWhiteSpace(sqlType))
        {
            entityBuilder.SetPropertyDatabaseType(propertyName, null, sourceSqlType: sqlType.Trim());
        }

        var typeAttr = element.Attribute("type")?.Value;

        if (string.IsNullOrWhiteSpace(typeAttr))
        {
            return;
        }

        var reading = DatabaseTypeConvertor.FromNHibernate(typeAttr);

        entityBuilder.SetPropertyDatabaseType(
            propertyName,
            reading.Type,
            reading.IsUnicode,
            reading.SourceType,
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
    /// column; a property spread over several columns is a separate concern. Attributes with
    /// no counterpart in the model - unique, index, check, default - are skipped. The type
    /// attribute and the sql-type of a nested &lt;column&gt; do not travel through this
    /// dictionary: they go through the typed channel of <see cref="ApplyTypeFacts"/>
    /// (decision 019).
    /// </summary>
    private static Dictionary<string, string> ReadColumnFacts(XElement element)
    {
        var dbProps = new Dictionary<string, string>();

        if (element.Attribute("column")?.Value is string column && !string.IsNullOrWhiteSpace(column))
        {
            dbProps["column"] = column;
        }

        ReadFacetsInto(dbProps, element);

        var columnElement = element.Elements().FirstOrDefault(e => e.Name.LocalName == "column");
        if (columnElement is null)
        {
            return dbProps;
        }

        if (columnElement.Attribute("name")?.Value is string nestedName && !string.IsNullOrWhiteSpace(nestedName))
        {
            dbProps["column"] = nestedName;
        }

        ReadFacetsInto(dbProps, columnElement);

        return dbProps;
    }

    /// <summary>
    /// Facets an element and a nested &lt;column&gt; spell the same way.
    /// </summary>
    private static void ReadFacetsInto(Dictionary<string, string> dbProps, XElement element)
    {
        if (bool.TryParse(element.Attribute("not-null")?.Value, out var notNull))
        {
            dbProps["nullable"] = (!notNull).ToString().ToLowerInvariant();
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
                    Reason = $"property-ref=\"{propertyRef}\" names a property of the referenced entity; the model has nowhere to keep the value, only the inverse role survives, and the generated mapping will not restate it (decision 012).",
                });
            }

            entityBuilder.AddForeignKey(
                isOneToOne ? Cardinality.OneToOne : Cardinality.ManyToOne,
                propName,
                target,
                role,
                // <one-to-one> admits no column by schema, so only <many-to-one> can state any.
                ReadRelationColumns(relation));
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

            var oneToMany = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "one-to-many");
            if (oneToMany != null)
            {
                var target = oneToMany.Attribute("class")?.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    entityBuilder.AddForeignKey(Cardinality.OneToMany, propName, target, foreignKeyColumns: keyColumns);
                    ApplyCollectionShape(collection, propName);
                }
                continue;
            }

            var manyToMany = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "many-to-many");
            if (manyToMany != null)
            {
                var target = manyToMany.Attribute("class")?.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    // The <key> and <many-to-many> columns belong to a junction table, not
                    // to the target entity; together with the collection's table they are
                    // what the junction entity is synthesized from (decision 005).
                    entityBuilder.AddForeignKey(
                        Cardinality.ManyToMany,
                        propName,
                        target,
                        foreignKeyColumns: keyColumns,
                        junction: new JunctionFacts(
                            collection.Attribute("table")?.Value,
                            collection.Attribute("schema")?.Value,
                            ReadRelationColumns(manyToMany)));
                    ApplyCollectionShape(collection, propName);
                }
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
