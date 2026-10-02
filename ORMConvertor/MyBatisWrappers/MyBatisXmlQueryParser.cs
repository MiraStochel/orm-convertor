using System.Xml.Linq;
using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using TransactSql;

namespace MyBatisWrappers;

/// <summary>
/// Reads the statements of a MyBatis XML mapper (decision 084). The document is the
/// framework's primary form and carries &lt;select&gt; beside &lt;resultMap&gt;, so the same
/// unit is a mapping and a query at once - the cleanest case decision 081 has: the
/// orchestration offers the unit to both passes,
/// <see cref="MyBatisXmlMappingParser"/> takes the result maps out of it and this parser the
/// statements, and neither knows about the other.
///
/// Only &lt;select&gt; becomes a query. The other three statements change rows rather than
/// return them, which is outside what the tool translates for any framework - the same
/// refusal a Dapper unit carrying an INSERT meets in the shared reader - so each is named
/// and refused rather than dropped in silence (decision 048). Of the attributes of a
/// &lt;select&gt;, four are read - id, parameterType, resultType and resultMap -, and the
/// rest are options of the statement, answered by the rule the annotated form shares
/// (<see cref="MyBatisQueryParser.ReadOptions"/>).
/// </summary>
public sealed class MyBatisXmlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    MyBatisReadingContext context,
    SourceSqlDialect? declaredSourceDialect = null)
    : MyBatisQueryParser(queryBuilders, context)
{
    protected override ConversionContentType Artifact => ConversionContentType.XML;

    public override bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.XML;

    public override IReadOnlyCollection<AbstractQueryBuilder> Parse(
        ConversionContentType contentType,
        string source,
        IReadOnlyList<EntityMap>? entityMaps = null)
    {
        // A document that cannot be read leaves no record here (decision 093): the mapping
        // parser read the same unit on the earlier pass and has already said so.
        if (MyBatisMapperDocument.Read(source).Root is not { } root)
        {
            return [];
        }

        var namespaceName = MyBatisMapperDocument.NamespaceOf(root);
        var filled = new List<AbstractQueryBuilder>();

        foreach (var element in root.Elements().Where(e => MyBatisMapperDocument.IsStatement(e.Name.LocalName)))
        {
            filled.Add(element.Name.LocalName == "select"
                ? ReadSelect(element, namespaceName, entityMaps)
                : RefuseWritingStatement(element.Attribute("id")?.Value, $"<{element.Name.LocalName}>"));
        }

        return filled;
    }

    private AbstractQueryBuilder ReadSelect(XElement select, string namespaceName, IReadOnlyList<EntityMap>? entityMaps)
    {
        var id = select.Attribute("id")?.Value;
        var builder = Named(id);

        if (string.IsNullOrWhiteSpace(id))
        {
            Report(builder, ConversionRecordKind.Failure,
                "A MyBatis <select> states its name in an id attribute and this one carries none, so the statement could not be "
                + "translated.");
            return builder;
        }

        if (RefuseIfDeclaredTwice(builder, namespaceName, id))
        {
            return builder;
        }

        // The result mapping is the one thing a statement states that the query IR has no
        // slot for: every target derives the materialized type from the table itself, with a
        // record of its own. It is the same record and the same reason as the <return> of an
        // NHibernate <sql-query> (decision 082) - dropping it leaves the rows as they are.
        foreach (var attribute in new[] { "resultType", "resultMap" })
        {
            if (select.Attribute(attribute)?.Value is { } value)
            {
                Report(builder, ConversionRecordKind.Loss,
                    $"The statement states {attribute}=\"{value}\", which says how its result is mapped; the query representation does "
                    + "not carry it, so it was dropped and the result type is derived from the table.");
            }
        }

        // Every other attribute is an option of the statement, answered by the rule both
        // forms share; a language driver of its own refuses the statement.
        var options = select.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && a.Name.LocalName is not ("id" or "parameterType" or "resultType" or "resultMap"))
            .Select(a => new StatementOption(a.Name.LocalName, a.Value.Trim(), $"{a.Name.LocalName}=\"{a.Value}\""));

        if (!ReadOptions(builder, options))
        {
            return builder;
        }

        var text = new MyBatisStatementText(context, namespaceName, (kind, reason, feature) => Report(builder, kind, reason, feature));

        foreach (var (name, facts) in StatedParameters(namespaceName, id, select.Attribute("parameterType")?.Value, entityMaps))
        {
            text.Parameters[name] = facts;
        }

        if (text.FromElement(select) is not { } sql)
        {
            return builder;
        }

        new SqlQueryReader(
            builder,
            (kind, reason, feature) => ReportSql(builder, kind, reason, feature),
            declaredSourceDialect,
            text.Parameters,
            Limits)
            .Read(sql);

        return builder;
    }
}
