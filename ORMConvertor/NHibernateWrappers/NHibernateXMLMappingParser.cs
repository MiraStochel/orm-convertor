using AbstractWrappers;
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

            foreach (var keyProp in compositeElem.Elements().Where(e => e.Name.LocalName == "key-property"))
            {
                var name = keyProp.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var dbProps = ReadColumnFacts(keyProp);

                if (dbProps.Count > 0)
                {
                    entityBuilder.SetPropertyDatabaseMapping(name, dbProps);
                }

                // <composite-id> admits no generator, so the values are the application's to
                // supply - that is a statement of the framework, not silence of the source.
                parts.Add((name, order++, PrimaryKeyStrategy.Assigned));
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

        entityBuilder.AddPrimaryKey(strategy, propName);

        // What the generator says beyond the vocabulary: its own name where we narrowed it,
        // and its parameters. Without the parameters a sequence-backed key would translate
        // into a mapping naming no sequence, which compiles and does not run.
        var sourceStrategyName = PrimaryKeyStrategyConvertor.SourceNameFor(genClass, strategy);
        var strategyParameters = ReadGeneratorParameters(generatorElem);

        if (sourceStrategyName is not null || strategyParameters.Count > 0)
        {
            entityBuilder.SetKeyStrategyDetails(propName, sourceStrategyName, strategyParameters);
        }
    }

    /// <summary>
    /// Reads the &lt;param name="..."&gt; children of a generator. Which names appear is up to
    /// the generator itself - sequence, max_lo, table - so they are kept as the source wrote
    /// them rather than translated into a vocabulary the model does not have.
    /// </summary>
    private static Dictionary<string, string> ReadGeneratorParameters(XElement? generatorElem)
    {
        var parameters = new Dictionary<string, string>();

        if (generatorElem is null)
        {
            return parameters;
        }

        foreach (var param in generatorElem.Elements().Where(e => e.Name.LocalName == "param"))
        {
            var name = param.Attribute("name")?.Value;

            if (!string.IsNullOrEmpty(name))
            {
                parameters[name] = param.Value.Trim();
            }
        }

        return parameters;
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
    /// no counterpart in the model - unique, index, check, default - are skipped, and so is
    /// sql-type, whose place in the model belongs to the type neutralization work.
    /// </summary>
    private static Dictionary<string, string> ReadColumnFacts(XElement element)
    {
        var dbProps = new Dictionary<string, string>();

        if (element.Attribute("column")?.Value is string column && !string.IsNullOrWhiteSpace(column))
        {
            dbProps["column"] = column;
        }

        // The NHibernate type sits on the element itself; a <column> carries sql-type instead.
        if (element.Attribute("type")?.Value is string type && !string.IsNullOrWhiteSpace(type))
        {
            dbProps["type"] = ((int)DatabaseTypeConvertor.FromNHibernate(type)).ToString();
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
                }
                continue;
            }

            var manyToMany = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "many-to-many");
            if (manyToMany != null)
            {
                var target = manyToMany.Attribute("class")?.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    // The columns belong to a junction table here, not to the target entity;
                    // they are recorded all the same for the junction work (decision 005).
                    entityBuilder.AddForeignKey(Cardinality.ManyToMany, propName, target, foreignKeyColumns: keyColumns);
                }
            }
        }
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
