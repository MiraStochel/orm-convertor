using System.Xml.Linq;
using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;

namespace NHibernateWrappers;

/// <summary>
/// Reads the named queries of an hbm.xml. The document is the framework's mapping artifact
/// and carries &lt;query&gt; elements as siblings of &lt;class&gt;, so the same unit is a mapping and a
/// query at once - which is precisely what decision 081 lets a unit be: the orchestration
/// offers the unit to both passes, <see cref="NHibernateXMLMappingParser"/> takes the classes
/// out of it and this parser the queries, and neither has to know about the other.
///
/// The document is parsed a second time on purpose. Sharing the reading with the mapping
/// parser would mean reading the queries before the catalog completion phase has spoken, so
/// they would be translated against maps that are not yet complete; the price of parsing the
/// XML twice is the price of that phase standing between the two passes.
///
/// The query language itself is HQL, read by <see cref="NHibernateHqlQueryParser"/>
/// (decision 062) - a fresh one per query, so nothing can leak from one query into the next.
/// The other form, &lt;sql-query&gt;, carries native SQL rather than HQL and is not read; the
/// mapping parser reports it, because reading SQL needs the T-SQL grammar that still lives
/// inside the Dapper wrapper, out of this one's reach (S1).
/// </summary>
public class NHibernateXmlQueryParser(Func<AbstractQueryBuilder> queryBuilders) : IQueryParser
{
    public bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.XML;

    /// <summary>
    /// Reads every &lt;query&gt; of the document. An hbm.xml that declares none yields no builder,
    /// and that is not a failure: a mapping document carrying no query is an ordinary input,
    /// and whether the unit was barren at all is a question about both passes together
    /// (decision 081).
    /// </summary>
    public IReadOnlyCollection<AbstractQueryBuilder> Parse(
        ConversionContentType contentType,
        string source,
        IReadOnlyList<EntityMap>? entityMaps = null)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        // Malformed XML throws here as it does in the mapping parser, which reads the same
        // unit on the earlier pass and therefore throws first.
        var mapping = XDocument.Parse(source.Trim()).Root;
        if (mapping is null || mapping.Name.LocalName != "hibernate-mapping")
        {
            return [];
        }

        var filled = new List<AbstractQueryBuilder>();

        foreach (var element in mapping.Elements().Where(e => e.Name.LocalName == "query"))
        {
            filled.AddRange(Read(element, entityMaps));
        }

        return filled;
    }

    private IReadOnlyCollection<AbstractQueryBuilder> Read(XElement element, IReadOnlyList<EntityMap>? entityMaps)
    {
        var name = element.Attribute("name")?.Value;

        // The builder carries the source's name of the query from the moment it exists: the
        // generated method is named after it and so are the records of this query, which is
        // the only thing telling two failed queries of one document apart (decision 081).
        AbstractQueryBuilder Named()
        {
            var builder = queryBuilders();
            builder.QueryName = name;
            return builder;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            var refused = Named();
            refused.Report(new ConversionRecord
            {
                Kind = ConversionRecordKind.Failure,
                Framework = refused.Descriptor.Framework,
                Artifact = ConversionContentType.XML,
                Reason = "An hbm.xml <query> states its name in a name attribute and this one carries none, "
                    + "so the query could not be translated.",
            });

            return [refused];
        }

        // Only the element's own text, so a <query-param> hint between two halves of the
        // query cannot end up inside the HQL. CDATA is text as much as a plain run is - the
        // form NHibernate's own documentation writes a query in.
        var hql = string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value));

        return new NHibernateHqlQueryParser(Named).Parse(ConversionContentType.HqlQuery, hql, entityMaps);
    }
}
