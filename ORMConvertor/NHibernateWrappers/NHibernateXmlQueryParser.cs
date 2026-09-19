using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using TransactSql;

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
/// Both forms of a named query are read, each in its own language: &lt;query&gt; carries HQL and
/// goes to <see cref="NHibernateHqlQueryParser"/> (decision 062), &lt;sql-query&gt; carries
/// native SQL and goes to the shared <see cref="SqlQueryReader"/> (decision 082) - a fresh
/// reader per query either way, so nothing can leak from one query into the next. This
/// parser is therefore the reader of two languages at once, which is why it composes the
/// shared SQL reading rather than inheriting it.
/// </summary>
public class NHibernateXmlQueryParser(Func<AbstractQueryBuilder> queryBuilders) : IQueryParser
{
    /// <summary>
    /// The placeholders NHibernate substitutes into a native query before handing it to the
    /// database: {alias}, {alias.property} and {alias.*}. They are not T-SQL and the grammar
    /// must not be taught them (decision 082), so a query that carries one is refused by
    /// name instead of failing as a syntax error at some column.
    /// </summary>
    private static readonly Regex Placeholder = new(
        @"\{[A-Za-z_][A-Za-z0-9_]*(\.([A-Za-z_][A-Za-z0-9_]*|\*))?\}",
        RegexOptions.CultureInvariant);

    public bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.XML;

    /// <summary>
    /// Reads every &lt;query&gt; and &lt;sql-query&gt; of the document, in the order they are
    /// written. An hbm.xml that declares none yields no builder, and that is not a failure:
    /// a mapping document carrying no query is an ordinary input, and whether the unit was
    /// barren at all is a question about both passes together (decision 081).
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

        foreach (var element in mapping.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "query":
                    filled.AddRange(Read(element, entityMaps));
                    break;

                case "sql-query":
                    filled.AddRange(ReadNative(element));
                    break;
            }
        }

        return filled;
    }

    private IReadOnlyCollection<AbstractQueryBuilder> Read(XElement element, IReadOnlyList<EntityMap>? entityMaps)
    {
        var name = element.Attribute("name")?.Value;

        if (Nameless(element, name) is { } refused)
        {
            return [refused];
        }

        // Only the element's own text, so a <query-param> hint between two halves of the
        // query cannot end up inside the HQL. CDATA is text as much as a plain run is - the
        // form NHibernate's own documentation writes a query in.
        var hql = Text(element);

        return new NHibernateHqlQueryParser(() => Named(name)).Parse(ConversionContentType.HqlQuery, hql, entityMaps);
    }

    /// <summary>
    /// Reads the other form of a named query, whose language is native SQL (decision 082).
    /// The wrapper's own part is getting hold of plain T-SQL: the element's text without its
    /// children, refused outright where it carries a placeholder only NHibernate could
    /// resolve. The grammar itself is the shared reader's, because SQL here is the same
    /// language it is in a Dapper unit.
    /// </summary>
    private IReadOnlyCollection<AbstractQueryBuilder> ReadNative(XElement element)
    {
        var name = element.Attribute("name")?.Value;

        if (Nameless(element, name) is { } nameless)
        {
            return [nameless];
        }

        var builder = Named(name);

        // The result mapping is the one thing a native query states that the query IR has no
        // slot for: every target derives the materialized type from the table itself, with a
        // record of its own (decision 067). Dropping it leaves the rows as they are, so it is
        // a loss and not a refusal - and so is anything else stated as a child element here.
        foreach (var child in element.Elements())
        {
            Report(
                builder,
                ConversionRecordKind.Loss,
                ConversionContentType.XML,
                $"<{child.Name.LocalName}> of the native query '{name}' states how its result is mapped, "
                    + "which the query representation does not carry; it was dropped and the result type "
                    + "is derived from the table.");
        }

        var sql = Text(element);

        if (Placeholder.Match(sql) is { Success: true } placeholder)
        {
            Report(
                builder,
                ConversionRecordKind.Failure,
                ConversionContentType.SqlQuery,
                $"The native query '{name}' contains the NHibernate placeholder '{placeholder.Value}', which is not T-SQL "
                    + "and which only NHibernate itself could resolve; no artifact was generated.");

            return [builder];
        }

        new SqlQueryReader(
            builder,
            (kind, reason, feature) => Report(builder, kind, ConversionContentType.SqlQuery, reason, feature))
            .Read(sql);

        return [builder];
    }

    /// <summary>
    /// The builder carries the source's name of the query from the moment it exists: the
    /// generated method is named after it and so are the records of this query, which is
    /// the only thing telling two failed queries of one document apart (decision 081).
    /// </summary>
    private AbstractQueryBuilder Named(string? name)
    {
        var builder = queryBuilders();
        builder.QueryName = name;
        return builder;
    }

    /// <summary>
    /// A refused builder where the element states no name, null where it does. All three
    /// forms that let a unit carry several queries require the name, so a nameless one is
    /// malformed input rather than a query falling back to the fixed method name.
    /// </summary>
    private AbstractQueryBuilder? Nameless(XElement element, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var refused = Named(name);

        Report(
            refused,
            ConversionRecordKind.Failure,
            ConversionContentType.XML,
            $"An hbm.xml <{element.Name.LocalName}> states its name in a name attribute and this one carries none, "
                + "so the query could not be translated.");

        return refused;
    }

    /// <summary>
    /// The element's own text, children left out. CDATA is text as much as a plain run is -
    /// the form NHibernate's own documentation writes a query in.
    /// </summary>
    private static string Text(XElement element)
        => string.Concat(element.Nodes().OfType<XText>().Select(text => text.Value));

    private static void Report(
        AbstractQueryBuilder builder,
        ConversionRecordKind kind,
        ConversionContentType artifact,
        string reason,
        QueryFeature? feature = null)
        => builder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = builder.Descriptor.Framework,
            Artifact = artifact,
            Feature = feature,
            Reason = reason,
        });
}
