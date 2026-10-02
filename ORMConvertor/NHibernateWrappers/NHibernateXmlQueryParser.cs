using System.Xml.Linq;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers.Convertors;
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
///
/// What the element states beside its text is the parser's own to answer, and nothing of it
/// passes in silence (decisions 048 and 109): a &lt;query-param&gt; is read as the stated
/// scalar of its parameter (decision 083), the attributes are execution options the
/// representation does not carry and are each a loss, and a child the schema does not
/// admit refuses the query.
/// </summary>
public class NHibernateXmlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    SourceSqlDialect? declaredSourceDialect = null) : IQueryParser
{
    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

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

        // A document that cannot be read leaves no record here (decision 093): the mapping
        // parser reads the same unit on the earlier pass and has already said so, and the
        // rule is that the reading which comes first speaks - one broken document is one
        // fact about the unit, not two.
        var mapping = XmlSource.Read(source).Root;
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

        var builder = Named(name);
        ReadOptions(builder, element, name!);

        if (ReadChildren(builder, element, name!) is not { } declared)
        {
            return [builder];
        }

        // Only the element's own text, so a <query-param> between two halves of the query
        // cannot end up inside the HQL. CDATA is text as much as a plain run is - the form
        // NHibernate's own documentation writes a query in.
        var hql = Text(element);

        new NHibernateHqlQueryParser(() => builder, declared) { Limits = Limits }
            .Parse(ConversionContentType.HqlQuery, hql, entityMaps);

        return [builder];
    }

    /// <summary>
    /// Reads the other form of a named query, whose language is native SQL (decision 082).
    /// The wrapper's own part is getting hold of plain T-SQL: the element's text without its
    /// children, refused outright where it carries a placeholder only NHibernate could
    /// resolve, with NHibernate's <c>:name</c> respelled for the grammar (decision 113,
    /// <see cref="NHibernateNativeSql"/>). The grammar itself is the shared reader's, because
    /// SQL here is the same language it is in a Dapper unit.
    /// </summary>
    private IReadOnlyCollection<AbstractQueryBuilder> ReadNative(XElement element)
    {
        var name = element.Attribute("name")?.Value;

        if (Nameless(element, name) is { } nameless)
        {
            return [nameless];
        }

        var builder = Named(name);
        ReadOptions(builder, element, name!);

        if (ReadChildren(builder, element, name!) is not { } declared)
        {
            return [builder];
        }

        // The text is NHibernate's native SQL, the same language CreateSQLQuery takes in C#,
        // so it is read the same way: its :name respelled for the grammar (decision 113). A
        // list is bound in code, never in the document, so none is stated here.
        NHibernateNativeSql.Read(
            builder,
            Text(element),
            $"The native query '{name}'",
            new HashSet<string>(),
            declaredSourceDialect,
            Limits,
            (kind, reason, feature) => Report(builder, kind, ConversionContentType.SqlQuery, reason, feature),
            declared);

        return [builder];
    }

    /// <summary>
    /// The child elements of a named query, which never belong to its text. The schema
    /// admits few, and each is answered by what it states:
    ///
    /// <list type="bullet">
    /// <item>&lt;query-param&gt;, in either form, declares the type of one parameter. The
    /// scalar the name states is the parameter's stated scalar (decision 083), returned for
    /// the reading to put on the parameter; a type that states none of the vocabulary is a
    /// loss, and the scalar is derived as if nothing had been declared.</item>
    /// <item>The result mapping of a native query - &lt;return&gt; and its siblings - is the
    /// one thing it states that the query IR has no slot for: every target derives the
    /// materialized type from the table itself (decision 067), so dropping it leaves the rows
    /// as they are and is a loss, not a refusal. So is &lt;synchronize&gt;, which names a
    /// table NHibernate flushes before the query runs.</item>
    /// <item>Any other element is no part of a named query, and its text - which the reading
    /// leaves out, as it leaves out every child's - could be a piece of the query. That would
    /// be a query read with other rows than the source wrote (decision 053), so the query is
    /// refused naming the element.</item>
    /// </list>
    ///
    /// Null where the query is refused, after every child has had its say.
    /// </summary>
    private Dictionary<string, ScalarType>? ReadChildren(AbstractQueryBuilder builder, XElement element, string name)
    {
        var declared = new Dictionary<string, ScalarType>(StringComparer.Ordinal);
        var native = element.Name.LocalName == "sql-query";
        var refused = false;

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "query-param":
                    refused |= !Declare(builder, child, name, declared);
                    break;

                case "return" or "return-scalar" or "return-join" or "load-collection" when native:
                    Report(
                        builder,
                        ConversionRecordKind.Loss,
                        ConversionContentType.XML,
                        $"<{child.Name.LocalName}> of the native query '{name}' states how its result is mapped, "
                            + "which the query representation does not carry; it was dropped and the result type "
                            + "is derived from the table.");
                    break;

                case "synchronize" when native:
                    Report(
                        builder,
                        ConversionRecordKind.Loss,
                        ConversionContentType.XML,
                        $"<synchronize> of the native query '{name}' names a table NHibernate flushes before running it, which "
                            + "says how NHibernate executes the query rather than which rows it reads; the query representation "
                            + "has no place for it and it was dropped.");
                    break;

                default:
                    Report(
                        builder,
                        ConversionRecordKind.Failure,
                        ConversionContentType.XML,
                        $"The query '{name}' carries <{child.Name.LocalName}>, which is no part of a named query in the hbm.xml "
                            + "schema; its text is not read as the query's, and it could be a piece of it, so no artifact was "
                            + "generated.");
                    refused = true;
                    break;
            }
        }

        return refused ? null : declared;
    }

    /// <summary>
    /// One &lt;query-param&gt;. The same name declared twice with two scalars is two answers
    /// to one question, refused the way the builder template refuses two stated scalars of
    /// one parameter; false then, true otherwise. A declaration naming a parameter the query
    /// does not write states nothing about the query, and goes unremarked, as the parameters
    /// of a MyBatis mapper method the statement does not use do.
    /// </summary>
    private static bool Declare(
        AbstractQueryBuilder builder,
        XElement declaration,
        string name,
        Dictionary<string, ScalarType> declared)
    {
        var parameter = declaration.Attribute("name")?.Value.Trim();
        var type = declaration.Attribute("type")?.Value.Trim();

        if (string.IsNullOrEmpty(parameter) || string.IsNullOrEmpty(type))
        {
            Report(
                builder,
                ConversionRecordKind.Loss,
                ConversionContentType.XML,
                $"A <query-param> of the query '{name}' states no {(string.IsNullOrEmpty(parameter) ? "name" : "type")}, so it "
                    + "declares nothing the query can use; it was dropped.",
                QueryFeature.QueryParameter);
            return true;
        }

        if (ScalarTypeConvertor.FromNHibernate(type) is not { } scalar)
        {
            Report(
                builder,
                ConversionRecordKind.Loss,
                ConversionContentType.XML,
                $"The <query-param> of the query '{name}' declares the type '{type}' for the parameter :{parameter}, which is no "
                    + "scalar of the type vocabulary; the scalar of the parameter is derived from what it is compared against "
                    + "instead.",
                QueryFeature.QueryParameter);
            return true;
        }

        if (declared.TryGetValue(parameter, out var earlier) && earlier != scalar)
        {
            Report(
                builder,
                ConversionRecordKind.Failure,
                ConversionContentType.XML,
                $"The query '{name}' declares the parameter :{parameter} twice, as {earlier} and as {scalar}; no artifact was "
                    + "generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        declared[parameter] = scalar;
        return true;
    }

    /// <summary>
    /// The attributes of a named query beside its name. None of them is read: cacheable,
    /// cache-region, cache-mode, fetch-size, timeout, flush-mode, read-only and comment say
    /// how NHibernate executes the query, not which rows it reads - the same answer the
    /// reading of a CreateQuery in C# gives the setters of its query object. Each is a loss
    /// all the same (decision 048), unless it spells out the default: cacheable and callable
    /// are false unless stated, so stating false states nothing.
    ///
    /// Two attributes of a native query get a reason of their own. resultset-ref names a
    /// result mapping, which is the &lt;return&gt; of <see cref="ReadChildren"/> by reference.
    /// callable marks the query as a call of a stored procedure: the text is read for what
    /// it states, and a call written in it - EXEC - is refused by the shared reading as a
    /// statement that is not a query, so what is lost is the mark alone.
    /// </summary>
    private static void ReadOptions(AbstractQueryBuilder builder, XElement element, string name)
    {
        foreach (var attribute in element.Attributes())
        {
            var option = attribute.Name.LocalName;
            var value = attribute.Value.Trim();

            if (attribute.IsNamespaceDeclaration || option == "name"
                || (option is "cacheable" or "callable" && value is "false" or "0"))
            {
                continue;
            }

            var reason = option switch
            {
                "callable" => $"The native query '{name}' states callable=\"{value}\", which marks it as a call of a stored "
                    + "procedure; the text is read as the query it states - a call itself, EXEC, is refused by the reading - "
                    + "and the mark was dropped.",

                "resultset-ref" => $"The native query '{name}' states resultset-ref=\"{value}\", which names how its result is "
                    + "mapped; the query representation does not carry it, so it was dropped and the result type is derived "
                    + "from the table.",

                _ => $"The query '{name}' states {option}=\"{value}\", which says how NHibernate executes it rather than which "
                    + "rows it reads; the query representation has no place for it and it was dropped.",
            };

            Report(builder, ConversionRecordKind.Loss, ConversionContentType.XML, reason);
        }
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
