using AbstractWrappers;
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
        var idElem = classElement.Elements().FirstOrDefault(e => e.Name.LocalName == "id");
        if (idElem == null)
        {
            return;
        }

        var propName = idElem.Attribute("name")?.Value;
        var generatorElem = idElem.Elements().FirstOrDefault(e => e.Name.LocalName == "generator");
        var genClass = generatorElem?.Attribute("class")?.Value;
        var strategy = PrimaryKeyStrategyConvertor.FromNHibernate(genClass);

        if (!string.IsNullOrEmpty(propName))
        {
            entityBuilder.AddPrimaryKey(strategy, propName);
        }
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

            var dbProps = new Dictionary<string, string>();
            if (prop.Attribute("column")?.Value is string col && !string.IsNullOrEmpty(col))
            {
                dbProps["column"] = col;
            }

            if (prop.Attribute("type")?.Value is string t && !string.IsNullOrEmpty(t))
            {
                dbProps["type"] = ((int)DatabaseTypeConvertor.FromNHibernate(t)).ToString();
            }

            if (bool.TryParse(prop.Attribute("not-null")?.Value, out var notNull))
            {
                dbProps["nullable"] = (!notNull).ToString().ToLowerInvariant();
            }

            if (prop.Attribute("precision")?.Value is string prec && !string.IsNullOrWhiteSpace(prec))
            {
                dbProps["precision"] = prec;
            }

            if (prop.Attribute("scale")?.Value is string sc && !string.IsNullOrWhiteSpace(sc))
            {
                dbProps["scale"] = sc;
            }

            if (prop.Attribute("length")?.Value is string len && !string.IsNullOrWhiteSpace(len))
            {
                dbProps["length"] = len;
            }

            entityBuilder.SetPropertyDatabaseMapping(
                propertyName,
                dbProps
            );
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

            var cardinality = relation.Name.LocalName switch
            {
                "one-to-one" => Cardinality.OneToOne,
                "many-to-one" => Cardinality.ManyToOne,
                _ => throw new InvalidOperationException()
            };

            entityBuilder.AddForeignKey(cardinality, propName, target);
        }

        string[] collectionTypes = ["bag", "set", "list", "map"];
        foreach (var collection in classElement.Elements().Where(e => collectionTypes.Contains(e.Name.LocalName)))
        {
            var propName = collection.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(propName))
            {
                continue;
            }

            var oneToMany = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "one-to-many");
            if (oneToMany != null)
            {
                var target = oneToMany.Attribute("class")?.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    entityBuilder.AddForeignKey(Cardinality.OneToMany, propName, target);
                }
                continue;
            }

            var manyToMany = collection.Elements().FirstOrDefault(e => e.Name.LocalName == "many-to-many");
            if (manyToMany != null)
            {
                var target = manyToMany.Attribute("class")?.Value;
                if (!string.IsNullOrEmpty(target))
                {
                    entityBuilder.AddForeignKey(Cardinality.ManyToMany, propName, target);
                }
            }
        }
    }
}
