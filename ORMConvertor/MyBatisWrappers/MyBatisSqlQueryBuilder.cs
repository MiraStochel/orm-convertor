using System.Text;
using AbstractWrappers.Descriptors;
using Common.Naming;
using Common.Xml;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;

namespace MyBatisWrappers;

/// <summary>
/// MyBatis's query builder, which is the whole of what MyBatis adds to T-SQL: the
/// descriptor and the wrapping of a finished SELECT into artifacts. Clause order,
/// pagination, set operations and subqueries are properties of the language and live in
/// <see cref="TransactSql.AbstractSqlQueryBuilder"/> (decision 082).
///
/// Two artifacts leave here, and they do not overlap: the declaration of the mapper
/// method, which carries the parameters and no @Select, and the mapper document, which
/// carries the SQL and nothing else. Emitting both forms of the same statement is exactly
/// the input the reading side refuses (decision 068) - MyBatis would not build a factory
/// from it - so the tool must not write it either.
///
/// The method is a fragment without its interface, the way Dapper's method and both JPA
/// methods are emitted without their class (decision 022).
/// </summary>
public class MyBatisSqlQueryBuilder : TransactSql.AbstractSqlQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => MyBatisDescriptor.Instance;

    /// <summary>The runnable half is Java, so the records of the template name that artifact.</summary>
    protected override ConversionContentType MethodArtifact => ConversionContentType.JavaQuery;

    /// <summary>Java methods are camelCase, so the name of a statement is spelled that way (decision 081).</summary>
    protected override string MethodName => QueryMethodNaming.CamelCase(QueryName, "query");

    protected override List<ConversionSource> Emit(string sql, string? resultEntity)
    {
        var entity = resultEntity ?? "Object";
        var package = EntityMaps.FirstOrDefault(m => m.Entity.Name == entity)?.Entity.Namespace;
        var qualified = string.IsNullOrEmpty(package) ? entity : $"{package}.{entity}";

        // The document's own name, which the tool invents the way it invents the name of a
        // generated method: our artifact's identity, made the same way at every conversion,
        // so it is deterministic (S2) and carries no record (decisions 010 and 028). Being
        // unique by construction, nothing here relies on how MyBatis merges two documents of
        // one namespace, which we have not measured.
        var mapper = $"{JavaClass.Capitalize(MethodName)}Mapper";
        var mapperNamespace = string.IsNullOrEmpty(package) ? mapper : $"{package}.{mapper}";

        var method = $"List<{entity}> {MethodName}({JavaParameters()});";

        var document = new StringBuilder();
        XmlEmitter.Prolog(document);
        document.AppendLine("<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\"");
        document.AppendLine("        \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">");
        XmlEmitter.Open(document, 0, "mapper", [new XmlAttribute("namespace", mapperNamespace)]);
        XmlEmitter.Open(document, 1, "select",
        [
            new XmlAttribute("id", MethodName),

            // resultType and not resultMap (decision 082): neither MyBatis nor Dapper states
            // the materialized type in the query, so the shared builder derives it from the
            // table - and a resultMap would point into a second document, which a conversion
            // of a query alone does not have.
            new XmlAttribute("resultType", qualified),
        ]);

        foreach (var line in Placeholders(XmlEmitter.EscapeText(sql)).Split('\n'))
        {
            document.AppendLine($"        {line.TrimEnd()}");
        }

        XmlEmitter.Close(document, 1, "select");
        XmlEmitter.Close(document, 0, "mapper", appendLine: false);

        return
        [
            new() { Content = method, ContentType = ConversionContentType.JavaQuery },
            new() { Content = document.ToString(), ContentType = ConversionContentType.XML },
        ];
    }

    /// <summary>
    /// The parameters as the declaration of a mapper method: @Param on every one of them,
    /// because MyBatis derives a name from the declaration only where there is a single
    /// parameter and would otherwise know them as arg0, arg1 - names the statement does not
    /// write. A collection parameter is declared as the Collection it binds, which is what
    /// the &lt;foreach&gt; below iterates.
    /// </summary>
    private string JavaParameters()
        => string.Join(", ", Parameters.Select(p =>
        {
            var name = QueryParameterNaming.IdentifierFor(p);
            var element = LangType.Scalar(p.Type!.Value);
            var type = p.IsCollection
                ? $"Collection<{JavaTypeConvertor.ToString(element, forceWrapper: true)}>"
                : JavaTypeConvertor.ToString(element);

            return $"@Param(\"{name}\") {type} {name}";
        }));

    /// <summary>
    /// The placeholder of the target (decision 083). The shared T-SQL visitor writes @name,
    /// which is what Dapper and the server want; MyBatis writes #{name}, and a collection
    /// parameter is a &lt;foreach&gt; in the canonical form - exactly the one the parser
    /// reads back - because MyBatis does not expand a list behind #{}. It is also the only
    /// dynamic tag this builder ever writes: the representation carries no dynamic statement,
    /// so &lt;if&gt;, &lt;choose&gt;, &lt;trim&gt; and &lt;set&gt; have nothing to come from,
    /// and even &lt;where&gt; is not written - the condition tree is complete and a plain
    /// WHERE is exact, whereas &lt;where&gt; would leave the shape of the result to how the
    /// target trims its connectives.
    ///
    /// Substituted with the string literals of the SQL skipped, so that a value holding an @
    /// is not read as a name; run after the escaping, so that the markup written here is the
    /// only markup in the element.
    /// </summary>
    private string Placeholders(string sql)
    {
        var substituted = new StringBuilder(sql.Length);
        var insideLiteral = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var character = sql[i];

            if (character == '\'')
            {
                insideLiteral = !insideLiteral;
                substituted.Append(character);
                continue;
            }

            if (insideLiteral || character != '@' || i + 1 >= sql.Length || !IsNameStart(sql[i + 1]))
            {
                substituted.Append(character);
                continue;
            }

            var end = i + 1;
            while (end < sql.Length && IsNameCharacter(sql[end]))
            {
                end++;
            }

            var name = sql[(i + 1)..end];
            var parameter = Parameters.FirstOrDefault(p => QueryParameterNaming.IdentifierFor(p) == name);

            if (parameter is null)
            {
                substituted.Append(character);
                continue;
            }

            substituted.Append(parameter.IsCollection
                ? $"<foreach item=\"item\" collection=\"{name}\" open=\"(\" separator=\",\" close=\")\">#{{item}}</foreach>"
                : $"#{{{name}}}");

            i = end - 1;
        }

        return substituted.ToString();
    }

    private static bool IsNameStart(char character) => char.IsLetter(character) || character == '_';

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
}
