using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using LinqParsing;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.QueryInstructions;

namespace NHibernateWrappers;

/// <summary>
/// Reads an NHibernate LINQ query. LINQ is one of two query languages the framework is read
/// from: a C# unit comes here - a whole file or a fragment of one (decision 111) -, a bare HQL unit goes to
/// <see cref="NHibernateHqlQueryParser"/> (decisions 025 and 062) - one parser per language,
/// told apart by the content type the unit declares, never by what its text looks like.
///
/// A C# unit hands NHibernate a query in two more languages, and since decision 113 this
/// parser reads them too: native SQL through <c>CreateSQLQuery</c>, by the shared T-SQL
/// reader, and HQL through <c>CreateQuery</c>, by the HQL parser of this wrapper - which is
/// what the escape path and the HQL builder write, so that their own output reads back.
/// </summary>
public class NHibernateLinqQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    SourceSqlDialect? declaredSourceDialect = null) : LinqQueryParser(queryBuilders)
{
    /// <summary>
    /// The calls on a query object that state how its rows are materialized rather than which
    /// rows they are - the result mapping the representation has no slot for, as the
    /// children of an hbm.xml &lt;sql-query&gt; are (decision 082).
    /// </summary>
    private static readonly HashSet<string> ResultMapping = new(StringComparer.Ordinal)
    {
        "AddScalar", "AddEntity", "AddJoin", "SetResultTransformer",
    };

    /// <summary>The calls that run the query as it stands and return all of its rows.</summary>
    private static readonly HashSet<string> Materializers = new(StringComparer.Ordinal)
    {
        "List", "ListAsync", "Enumerable", "Future",
    };

    /// <summary>The calls a query object kept in a variable may get later without the reading losing anything.</summary>
    private static readonly HashSet<string> HarmlessLater = new(StringComparer.Ordinal)
    {
        "SetParameter", "List", "ListAsync", "Enumerable", "Future",
    };

    /// <summary>
    /// NHibernate's provider joins the argument of StartsWith, EndsWith and Contains with
    /// the wildcard as written (its HQL generators concatenate, they do not escape), so
    /// <c>StartsWith("A_")</c> matches any character after A there, and the argument is the
    /// pattern's core verbatim. The opposite of EF Core, and a fact about the provider.
    /// </summary>
    protected override bool ProviderEscapesStringMethodArguments => false;

    protected override bool TryReadQueryRoot(ExpressionSyntax expression, out LinqQueryRoot? root)
    {
        root = null;

        // session.Query<Customer>() - the type argument names the entity, so unlike EF Core
        // there is no DbSet name to un-pluralize.
        if (expression is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax member
            && member.Name.Identifier.Text == "Query"
            && TypeArgumentOf(member.Name) is { } entity)
        {
            root = new LinqQueryRoot(entity);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The other ways a C# unit hands NHibernate a query (decision 113), recognized in
    /// NHibernate's own API (S1): <c>CreateSQLQuery</c> with native SQL, <c>CreateQuery</c>
    /// with HQL, <c>GetNamedQuery</c> with the name of a query defined in a mapping - read
    /// where it is defined, so the reference itself yields nothing and says so -, and
    /// <c>QueryOver</c> and <c>CreateCriteria</c>, whose query the API composes at run time
    /// out of calls rather than out of a text, and which no reading can follow.
    /// </summary>
    protected override ForeignQuery? TryReadForeignQuery(InvocationExpressionSyntax outermost)
    {
        var chain = CallChain(outermost);
        var head = chain[0];

        if (head.Expression is not MemberAccessExpressionSyntax)
        {
            return null;
        }

        return MethodNameOf(head) switch
        {
            "CreateSQLQuery" => Text(outermost, chain, ConversionContentType.SqlQuery, "CreateSQLQuery"),
            "CreateQuery" => Text(outermost, chain, ConversionContentType.HqlQuery, "CreateQuery"),
            "GetNamedQuery" => new ForeignQuery(builder => ChannelOf(builder, ConversionContentType.CSharpQuery)(
                ConversionRecordKind.Incompleteness,
                $"The code hands NHibernate the named query {head.ArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "without a name"} through GetNamedQuery; a named query is read where its mapping defines it, so the reference yields no query of its own.",
                null)),
            "QueryOver" or "CreateCriteria" => new ForeignQuery(builder => ChannelOf(builder, ConversionContentType.CSharpQuery)(
                ConversionRecordKind.Failure,
                $"The code composes the query through {MethodNameOf(head)}, whose query NHibernate builds at run time out of calls rather than out of a text, which the reading does not follow; no artifact was generated.",
                QueryFeature.Projection)),
            _ => null,
        };
    }

    /// <summary>
    /// A query NHibernate is handed as a text - native SQL or HQL -, with what the code
    /// chains onto the query object: the parameters it binds as lists, the slice it sets, the
    /// result mapping it declares, which the representation does not carry (a loss, as for an
    /// hbm.xml &lt;sql-query&gt;), and anything else the reading does not know, which it names
    /// as a loss rather than refusing, as the chain reading does with an unknown step
    /// (decision 070).
    /// </summary>
    private ForeignQuery Text(
        InvocationExpressionSyntax outermost,
        IReadOnlyList<InvocationExpressionSyntax> chain,
        ConversionContentType language,
        string api)
        => new(builder =>
        {
            var report = ChannelOf(builder, language);

            if (LiteralText(chain[0].ArgumentList.Arguments.FirstOrDefault()?.Expression) is not { } text)
            {
                report(
                    ConversionRecordKind.Incompleteness,
                    $"The query handed to {api} is not a string literal - it is composed at run time - so the parser has nothing to read.",
                    null);
                return;
            }

            if (ContinuedElsewhere(outermost, HarmlessLater) is { } variable)
            {
                report(
                    ConversionRecordKind.Failure,
                    $"The query object of {api} is kept in the variable '{variable}', which the code goes on to slice or bind in another statement out of the reading's sight; no artifact was generated.",
                    QueryFeature.Pagination);
                return;
            }

            var lists = new HashSet<string>(StringComparer.Ordinal);
            RowCount? offset = null, limit = null;
            var mapped = false;

            foreach (var call in chain.Skip(1))
            {
                var name = MethodNameOf(call);
                var first = call.ArgumentList.Arguments.FirstOrDefault()?.Expression;

                switch (name)
                {
                    case "SetParameter":
                        break;

                    case "SetParameterList":
                        if (LiteralText(first) is { } list)
                        {
                            lists.Add(list);
                        }

                        break;

                    case "SetFirstResult" or "SetMaxResults":
                        if (RowCountOf(first) is not { } count)
                        {
                            report(
                                ConversionRecordKind.Failure,
                                $"The argument of {name}() on the query object is neither a non-negative integer literal nor a value from the enclosing scope, and a pagination the artifact does not carry would change which rows the query returns; no artifact was generated.",
                                QueryFeature.Pagination);
                            return;
                        }

                        if (name == "SetFirstResult")
                        {
                            offset = count;
                        }
                        else
                        {
                            limit = count;
                        }

                        break;

                    case not null when ResultMapping.Contains(name):
                        mapped = true;
                        break;

                    case not null when Materializers.Contains(name):
                        break;

                    default:
                        report(
                            ConversionRecordKind.Loss,
                            $"{name}() on the query object of {api} is a call the reading does not know; it was dropped, which changes nothing the query text says about its rows.",
                            null);
                        break;
                }
            }

            if (mapped)
            {
                report(
                    ConversionRecordKind.Loss,
                    $"The code declares how the rows of the {api} query are materialized (AddScalar, AddEntity), which the query representation does not carry; it was dropped and every target derives the row again.",
                    QueryFeature.Projection);
            }

            if (language == ConversionContentType.SqlQuery)
            {
                NHibernateNativeSql.Read(builder, text, $"The native query of {api}", lists, declaredSourceDialect, Limits, report);
            }
            else
            {
                new NHibernateHqlQueryParser(() => builder) { Limits = Limits }.Parse(ConversionContentType.HqlQuery, text, EntityMaps);
            }

            builder.PaginateReadQuery(offset, limit);
        });
}
