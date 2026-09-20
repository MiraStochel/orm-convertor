using System.Xml.Linq;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;

namespace MyBatisWrappers;

/// <summary>
/// Reads the mapping half of a MyBatis XML mapper: its &lt;resultMap&gt; elements
/// (decision 084). The other half of the same document - its statements - is read by
/// <see cref="MyBatisXmlQueryParser"/> on the query pass, which is what decision 081 lets a
/// unit be: a mapping and a query at once, claimed by two parsers of one framework with
/// neither knowing about the other.
///
/// Three things are put into the shared context on the way, because the query pass needs
/// them and the entity pass runs first: the ids of the document's statements, so that the
/// same id in an annotation and in XML is recognized; the bodies of its &lt;sql&gt;
/// fragments, so that an &lt;include&gt; can be expanded; and nothing else - the signatures
/// come from the interface.
/// </summary>
public sealed class MyBatisXmlMappingParser(AbstractEntityBuilder entityBuilder, MyBatisReadingContext context)
    : MyBatisMappingParser(entityBuilder)
{
    protected override ConversionContentType Artifact => ConversionContentType.XML;

    public override bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.XML;

    public override IReadOnlyCollection<EntityMap> Parse(string source)
    {
        if (MyBatisMapperDocument.Root(source) is not { } root)
        {
            return [];
        }

        var namespaceName = MyBatisMapperDocument.NamespaceOf(root);

        foreach (var element in root.Elements())
        {
            var id = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (element.Name.LocalName == "sql")
            {
                context.DeclareFragment(namespaceName, id, element);
            }
            else if (MyBatisMapperDocument.IsStatement(element.Name.LocalName))
            {
                context.DeclareStatement(namespaceName, id, MyBatisStatementForm.Xml);
            }
        }

        var byId = root.Elements()
            .Where(e => e.Name.LocalName == "resultMap" && e.Attribute("id") is not null)
            .GroupBy(e => e.Attribute("id")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var read = new List<EntityMap>();
        var closed = new ClosedMappings();

        foreach (var resultMap in root.Elements().Where(e => e.Name.LocalName == "resultMap"))
        {
            if (ReadResultMap(resultMap, byId, closed, []) is { } entityMap && !read.Contains(entityMap))
            {
                read.Add(entityMap);
            }
        }

        closed.Apply(entityBuilder);

        return read;
    }

    /// <summary>
    /// One &lt;resultMap&gt; with its parents. extends is a purely textual merge of the
    /// parent's pairs, so it is expanded here the way &lt;include&gt; is expanded in a
    /// statement; a cycle is refused rather than followed.
    /// </summary>
    private EntityMap? ReadResultMap(
        XElement resultMap,
        IReadOnlyDictionary<string, XElement> byId,
        ClosedMappings closed,
        HashSet<string> visited)
    {
        var type = resultMap.Attribute("type")?.Value;

        if (string.IsNullOrWhiteSpace(type))
        {
            Report(ConversionRecordKind.Loss, null, null, null,
                $"A <resultMap id=\"{resultMap.Attribute("id")?.Value}\"> names no type, so there is no entity its pairs belong to; it was dropped.");
            return null;
        }

        var entityMap = Entity(type);
        var identity = new List<string>();

        ReadMembers(resultMap, byId, entityMap, identity, closed, visited);
        ReportIdentity(entityMap, identity);

        return entityMap;
    }

    /// <summary>
    /// The members of a result mapping - of a &lt;resultMap&gt;, or of the
    /// &lt;association&gt; or &lt;collection&gt; nested in one, which map an entity of their
    /// own. One &lt;resultMap&gt; may therefore carry the mapping of several entities, which
    /// is a shape no other of the six frameworks has and which the model takes without a
    /// change, because entities reference each other by name (decision 001).
    /// </summary>
    private void ReadMembers(
        XElement owner,
        IReadOnlyDictionary<string, XElement> byId,
        EntityMap entityMap,
        List<string> identity,
        ClosedMappings closed,
        HashSet<string> visited)
    {
        if (owner.Attribute("extends")?.Value is { } parentId)
        {
            if (!visited.Add(parentId))
            {
                Report(ConversionRecordKind.Loss, entityMap, null, null,
                    $"The <resultMap> extends '{parentId}' in a cycle; the inherited pairs were not read.");
            }
            else if (byId.TryGetValue(parentId, out var parent))
            {
                ReadMembers(parent, byId, entityMap, identity, closed, visited);
            }
            else
            {
                Report(ConversionRecordKind.Incompleteness, entityMap, null, null,
                    $"The <resultMap> extends '{parentId}', which this document does not declare; the inherited pairs were not read.");
            }
        }

        if (string.Equals(owner.Attribute("autoMapping")?.Value, "false", StringComparison.OrdinalIgnoreCase))
        {
            closed.Close(entityMap);
        }

        foreach (var member in owner.Elements())
        {
            var property = member.Attribute("property")?.Value;

            switch (member.Name.LocalName)
            {
                case "id":
                    if (member.Attribute("column")?.Value is { } idColumn)
                    {
                        identity.Add(idColumn);
                    }

                    WriteColumn(entityMap, property!, member.Attribute("column")?.Value,
                        member.Attribute("javaType")?.Value, member.Attribute("jdbcType")?.Value);
                    closed.Cover(entityMap, property);
                    break;

                case "result":
                    WriteColumn(entityMap, property!, member.Attribute("column")?.Value,
                        member.Attribute("javaType")?.Value, member.Attribute("jdbcType")?.Value);
                    closed.Cover(entityMap, property);
                    break;

                case "association":
                    ReadNested(member, byId, entityMap, closed, visited, isCollection: false);
                    closed.Cover(entityMap, property);
                    break;

                case "collection":
                    ReadNested(member, byId, entityMap, closed, visited, isCollection: true);
                    closed.Cover(entityMap, property);
                    break;

                // Outside the table of decision 084 is not silence (decision 048): each of
                // these states something the model has no place for, and says which.
                case "discriminator":
                    Report(ConversionRecordKind.Loss, entityMap, null, null,
                        "<discriminator> chooses the type of the result row by a column's value, which the intermediate "
                        + "representation does not carry; it was dropped.");
                    break;

                case "constructor":
                    Report(ConversionRecordKind.Loss, entityMap, null, null,
                        "<constructor> maps the columns onto the arguments of a constructor, which the intermediate representation "
                        + "does not carry - it maps columns onto properties; it was dropped.");
                    break;
            }
        }
    }

    /// <summary>
    /// An &lt;association&gt; or a &lt;collection&gt;: the navigation itself, and the pairs
    /// nested inside it, which belong to the entity on the far side. The join condition of
    /// the statement is not read here at all - a hand-written join is a fact of the query and
    /// is translated as one, and deriving a foreign key from it would claim of the schema
    /// something that need not be in it (decision 084).
    /// </summary>
    private void ReadNested(
        XElement navigation,
        IReadOnlyDictionary<string, XElement> byId,
        EntityMap entityMap,
        ClosedMappings closed,
        HashSet<string> visited,
        bool isCollection)
    {
        var property = navigation.Attribute("property")?.Value;

        if (string.IsNullOrWhiteSpace(property))
        {
            Report(ConversionRecordKind.Loss, entityMap, null, null,
                $"A <{navigation.Name.LocalName}> of '{entityMap.Entity.Name}' names no property; it was dropped.");
            return;
        }

        if (navigation.Attribute("fetchType")?.Value is { } fetchType)
        {
            Report(ConversionRecordKind.Loss, entityMap, property, null,
                $"The navigation states fetchType '{fetchType}', which is how the value is loaded rather than what it is; the "
                + "intermediate representation does not carry a loading strategy and it was dropped.");
        }

        if (navigation.Attribute("select")?.Value is { } select)
        {
            var argument = navigation.Attribute("column")?.Value is { } column ? $" with '{column}' as its argument" : string.Empty;

            Report(ConversionRecordKind.Loss, entityMap, property, null,
                $"The navigation is filled by the statement '{select}'{argument} rather than by the columns of this result, which says "
                + "how the value is loaded; the intermediate representation does not carry it and it was dropped. The relation itself "
                + "is recorded, and its columns are left to a database catalog (F6).");
        }

        var stated = isCollection ? navigation.Attribute("ofType")?.Value : navigation.Attribute("javaType")?.Value;
        var target = stated is null ? TargetOfNavigation(entityMap, property) : MyBatisTypeNames.SimpleName(stated);

        if (target is null)
        {
            Report(ConversionRecordKind.Incompleteness, entityMap, property, MappingFactCategory.ForeignKeyColumns,
                $"The <{navigation.Name.LocalName}> names no type and no unit of the conversion declares the property, so the entity "
                + "it navigates to is not known; the relation was not recorded.");
            return;
        }

        WriteNavigation(entityMap, property, target, isCollection);

        if (!navigation.Elements().Any())
        {
            return;
        }

        var nested = stated is null ? Entity(target) : Entity(stated);
        var nestedIdentity = new List<string>();

        ReadMembers(navigation, byId, nested, nestedIdentity, closed, visited);
        ReportIdentity(nested, nestedIdentity);

        // The entity being read goes back to the one whose result map this is, so that the
        // members after the navigation land where they belong.
        entityBuilder.EntityMap = entityMap;
    }

    /// <summary>
    /// The properties a closed mapping covered, per entity. A mapping with
    /// autoMapping="false" states exactly what it lists, so a property of the class it does
    /// not list has no column behind it (decision 072) - which is the one form in which
    /// MyBatis can state a transient property at all. Gathered over the whole document and
    /// applied at the end, because a second &lt;resultMap&gt; over the same class is another
    /// view of it, not a contradiction of the first.
    /// </summary>
    private sealed class ClosedMappings
    {
        private readonly HashSet<EntityMap> closed = [];

        private readonly Dictionary<EntityMap, HashSet<string>> covered = [];

        public void Close(EntityMap entityMap) => closed.Add(entityMap);

        public void Cover(EntityMap entityMap, string? property)
        {
            if (string.IsNullOrWhiteSpace(property))
            {
                return;
            }

            if (!covered.TryGetValue(entityMap, out var properties))
            {
                covered[entityMap] = properties = new HashSet<string>(StringComparer.Ordinal);
            }

            properties.Add(property);
        }

        public void Apply(AbstractEntityBuilder entityBuilder)
        {
            foreach (var entityMap in closed)
            {
                var named = covered.GetValueOrDefault(entityMap) ?? [];
                entityBuilder.EntityMap = entityMap;

                foreach (var property in entityMap.Entity.Properties.ToList())
                {
                    if (named.Contains(property.Name)
                        || entityMap.Relations.Any(r => r.SourceNavigationProperty == property.Name))
                    {
                        continue;
                    }

                    entityBuilder.MarkTransient(property.Name);
                }
            }
        }
    }
}
