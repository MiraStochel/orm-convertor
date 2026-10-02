using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using LinqParsing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.QueryInstructions.Conditions;
using TransactSql;

namespace EFCoreWrappers;

/// <summary>
/// Reads an EF Core LINQ query. Everything about walking the chain lives in
/// <see cref="LinqQueryParser"/>, because it is System.Linq rather than EF Core
/// (decision 026); what is genuinely EF Core is how a query starts, the steps of its own
/// API that decide which rows come back, and, for a pattern, its own <c>EF.Functions.Like</c>
/// - the string methods are System.String and the shared reader reads them, under the
/// provider's default of escaping their argument. Since decision 113 also the native SQL
/// EF Core is handed - <c>Database.SqlQuery</c>, <c>SqlQueryRaw</c> and a <c>FromSql…</c>
/// that is the whole query -, read by the shared T-SQL reader in the dialect the source
/// declared (decision 088), and since decision 115 the explicit loading of a navigation -
/// <c>Entry(…).Collection(…).Query()</c> or <c>.Load()</c> -, recognized here as a root and
/// read by the shared parser as the query the provider composes out of it.
/// </summary>
public class EFCoreLinqQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    SourceSqlDialect? declaredSourceDialect = null) : LinqQueryParser(queryBuilders)
{
    /// <summary>
    /// The calls on the result of a native query that say nothing about its rows: they
    /// materialize it, or they say how it is tracked. Anything else after it composes LINQ
    /// over the native SQL, which EF Core runs as a subquery of its own.
    /// </summary>
    private static readonly HashSet<string> Materializers = new(StringComparer.Ordinal)
    {
        "ToList", "ToListAsync", "ToArray", "ToArrayAsync", "AsEnumerable", "AsAsyncEnumerable",
        "AsNoTracking", "AsNoTrackingWithIdentityResolution", "AsTracking", "TagWith",
    };

    /// <summary>
    /// The native SQL EF Core is handed (decision 113), recognized in EF Core's own API (S1):
    /// <c>Database.SqlQuery&lt;T&gt;</c> and <c>SqlQueryRaw&lt;T&gt;</c>, which is what the escape
    /// path writes for a row; <c>FromSql</c>, <c>FromSqlRaw</c> and <c>FromSqlInterpolated</c>
    /// on a root with nothing composed over them, which is what it writes for an entity; and
    /// <c>Database.ExecuteSql…</c>, which runs a command and is no query to read. A FromSql with
    /// LINQ composed over it is not taken here: the chain reading refuses the step (decision
    /// 070), and over SqlQuery the same composition is refused here by name - both are the
    /// one shape of native SQL the escape path never writes.
    /// </summary>
    protected override ForeignQuery? TryReadForeignQuery(InvocationExpressionSyntax outermost)
    {
        var chain = CallChain(outermost);

        for (var i = 0; i < chain.Count; i++)
        {
            var call = chain[i];
            var name = MethodNameOf(call);
            var after = chain.Skip(i + 1).ToList();

            if (OnDatabase(call) && name is "SqlQuery" or "SqlQueryRaw")
            {
                return Native(outermost, call, after, raw: name == "SqlQueryRaw", api: $"Database.{name}");
            }

            if (OnDatabase(call) && name is not null && name.StartsWith("ExecuteSql", StringComparison.Ordinal))
            {
                return new ForeignQuery(builder => ChannelOf(builder, ConversionContentType.SqlQuery)(
                    ConversionRecordKind.Failure,
                    $"Database.{name} runs a command and returns no rows, so there is no query to translate; no artifact was generated.",
                    QueryFeature.Projection));
            }

            if (name is "FromSql" or "FromSqlRaw" or "FromSqlInterpolated"
                && call.Expression is MemberAccessExpressionSyntax member
                && TryReadQueryRoot(member.Expression, out _)
                && after.All(next => MethodNameOf(next) is { } step && Materializers.Contains(step)))
            {
                return Native(outermost, call, after, raw: name == "FromSqlRaw", api: name);
            }
        }

        return null;
    }

    /// <summary>Whether the call is made on <c>Database</c> - of a context, or the context's own inside it.</summary>
    private static bool OnDatabase(InvocationExpressionSyntax call)
        => call.Expression is MemberAccessExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Database" } or IdentifierNameSyntax { Identifier.Text: "Database" },
        };

    private ForeignQuery Native(
        InvocationExpressionSyntax outermost,
        InvocationExpressionSyntax call,
        IReadOnlyList<InvocationExpressionSyntax> after,
        bool raw,
        string api)
        => new(builder =>
        {
            var report = ChannelOf(builder, ConversionContentType.SqlQuery);

            if (after.FirstOrDefault(next => MethodNameOf(next) is not { } step || !Materializers.Contains(step)) is { } composed)
            {
                report(
                    ConversionRecordKind.Failure,
                    $"The code composes {MethodNameOf(composed)}() over the native SQL handed to {api}, which EF Core runs as a subquery of its own and the query representation does not carry; no artifact was generated.",
                    QueryFeature.Filtering);
                return;
            }

            if (ContinuedElsewhere(outermost, Materializers) is { } variable)
            {
                report(
                    ConversionRecordKind.Failure,
                    $"The native query handed to {api} is kept in the variable '{variable}', which the code continues in another statement out of the reading's sight; no artifact was generated.",
                    QueryFeature.Filtering);
                return;
            }

            var arguments = call.ArgumentList.Arguments;
            string? unread = "the call passes no text";
            var sql = arguments.Count == 0
                ? null
                : raw
                    ? EFCoreNativeSqlReading.Raw(arguments, out unread)
                    : EFCoreNativeSqlReading.Interpolated(arguments[0].Expression, out unread);

            if (sql is null)
            {
                report(
                    ConversionRecordKind.Incompleteness,
                    $"The query handed to {api} could not be read: {unread}.",
                    null);
                return;
            }

            new SqlQueryReader(builder, report, declaredSourceDialect, statedParameters: null, Limits).Read(sql);
        });
    /// <summary>
    /// EF Core composes a query over the rows of a projection or a slice and over a query
    /// held in a variable, translating it into a derived table (decision 112, verified
    /// against EF Core 10), so the shared reading of it is on here.
    /// </summary>
    protected override bool ReadsIntermediateResults => true;

    /// <summary>
    /// <c>EF.Functions.Like(column, pattern)</c> and the overload with the escape - the
    /// shape the EF Core builder writes where the split of decision 051 is not exact, so
    /// the one the identity direction has to read back. Recognized by the last two names of
    /// the receiver, so that the qualified spelling is the same call.
    /// </summary>
    protected override bool TryReadProviderPatternFunction(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax? column,
        out ExpressionSyntax? pattern,
        out ExpressionSyntax? escape)
    {
        column = null;
        pattern = null;
        escape = null;

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Like",
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Functions" } functions,
            }
            || LastIdentifier(functions.Expression) != "EF")
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count is not (2 or 3))
        {
            return false;
        }

        column = arguments[0].Expression;
        pattern = arguments[1].Expression;
        escape = arguments.Count == 3 ? arguments[2].Expression : null;
        return true;
    }

    /// <summary>
    /// <c>EF.Functions.DateDiffYear</c> through <c>DateDiffSecond</c> (decision 113): the
    /// provider's own date difference, which SQL Server's provider maps onto DATEDIFF with the
    /// unit the method names - verified against EF Core 10.0.10 -, so it counts boundaries as
    /// the vocabulary's DateDiff does.
    /// </summary>
    protected override QueryOperand? TryReadProviderFunction(InvocationExpressionSyntax invocation, Func<ExpressionSyntax, QueryOperand?> readLeaf)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Functions" } functions,
            } member
            || LastIdentifier(functions.Expression) != "EF")
        {
            return null;
        }

        DateUnit? unit = member.Name.Identifier.Text switch
        {
            "DateDiffYear" => DateUnit.Year,
            "DateDiffMonth" => DateUnit.Month,
            "DateDiffDay" => DateUnit.Day,
            "DateDiffHour" => DateUnit.Hour,
            "DateDiffMinute" => DateUnit.Minute,
            "DateDiffSecond" => DateUnit.Second,
            _ => null,
        };

        var arguments = invocation.ArgumentList.Arguments;
        if (unit is null || arguments.Count != 2)
        {
            return null;
        }

        var start = readLeaf(arguments[0].Expression);
        var end = start is null ? null : readLeaf(arguments[1].Expression);
        return end is null ? null : QueryOperand.Computed(QueryExpression.Call(QueryFunction.DateDiff, [start!, end], unit));
    }

    /// <summary>
    /// The steps that replace the source of the chain, so they decide which rows come back as a
    /// filter does, and the representation does not carry them (decision 070).
    /// <c>FromSql()</c>, <c>FromSqlRaw()</c> and <c>FromSqlInterpolated()</c> replace it with
    /// SQL; the whole query a FromSql is, with nothing composed over it, is read as native SQL
    /// (decision 113, <see cref="TryReadForeignQuery"/>), and one with LINQ composed over it
    /// comes here and is refused - composition over native SQL is the one shape the escape path
    /// never writes. The temporal steps of the SQL Server
    /// provider replace it with the table's history - every version of a row, or the versions
    /// valid at a point or within a period -, where the artifact would read the current rows.
    /// </summary>
    protected override QueryFeature? ProviderStepChangesTheRowSet(string method) => method switch
    {
        "FromSql" or "FromSqlRaw" or "FromSqlInterpolated" => QueryFeature.Filtering,
        "TemporalAll" or "TemporalAsOf" or "TemporalFromTo" or "TemporalBetween" or "TemporalContainedIn" => QueryFeature.Filtering,
        _ => null,
    };

    /// <summary>
    /// A member the unit declares as a DbSet is EF Core's query root (decision 114): where the
    /// unit declares the type of the name a root is written as a member of, a DbSet member keeps
    /// the root the position reads, and a member of any other type is not one.
    /// </summary>
    protected override bool IsProviderQuerySource(TypeSyntax declaredType) => EFCoreContext.IsDbSet(declaredType);

    /// <summary>
    /// The navigation entry of EF Core's explicit loading (decision 115): <c>X.Entry(e)</c>, or
    /// a bare <c>Entry(e)</c> inside the context, with one argument, and on it <c>Collection</c>,
    /// <c>Reference</c> or <c>Navigation</c> with one argument that names the navigation - a
    /// lambda over the entity, <c>o =&gt; o.Lines</c>, or a string literal. The type argument of
    /// <c>Entry&lt;T&gt;</c> states the entity outright where it is written. An argument of any
    /// other shape leaves the navigation unread, and the shared reading refuses the place by
    /// name.
    /// </summary>
    private static bool TryReadNavigationEntry(InvocationExpressionSyntax navigation, out ExplicitLoad? load)
    {
        load = null;

        if (navigation.Expression is not MemberAccessExpressionSyntax { Name: var api, Expression: InvocationExpressionSyntax entry }
            || api.Identifier.Text is not ("Collection" or "Reference" or "Navigation")
            || navigation.ArgumentList.Arguments.Count != 1
            || entry.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        SimpleNameSyntax? entryName = entry.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name,
            SimpleNameSyntax bare => bare,
            _ => null,
        };

        if (entryName?.Identifier.Text != "Entry")
        {
            return false;
        }

        var argument = navigation.ArgumentList.Arguments[0].Expression;
        var named = argument switch
        {
            SimpleLambdaExpressionSyntax { Body: ExpressionSyntax body } lambda => NavigationOf(body, lambda.Parameter.Identifier.Text),
            _ => LiteralText(argument),
        };

        load = new ExplicitLoad(entry.ArgumentList.Arguments[0].Expression, TypeArgumentOf(entryName), named, api.Identifier.Text);
        return true;
    }

    /// <summary>The member a lambda body names on its parameter, a null-forgiving mark left behind; null for any other body.</summary>
    private static string? NavigationOf(ExpressionSyntax body, string parameter)
    {
        while (body is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } suppressed)
        {
            body = suppressed.Operand;
        }

        return body is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax head } member && head.Identifier.Text == parameter
            ? member.Name.Identifier.Text
            : null;
    }

    private static string? LastIdentifier(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        AliasQualifiedNameSyntax qualified => qualified.Name.Identifier.Text,
        _ => null,
    };

    protected override bool TryReadQueryRoot(ExpressionSyntax expression, out LinqQueryRoot? root)
    {
        root = null;

        switch (expression)
        {
            // ctx.Set<Customer>() - the type argument names the entity outright.
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax setAccess
                     && setAccess.Name.Identifier.Text == "Set"
                     && TypeArgumentOf(setAccess.Name) is { } entity:
                root = new LinqQueryRoot(entity);
                return true;

            // ctx.Entry(order).Collection(o => o.Lines).Query(), or .Load() - the explicit load
            // of a navigation (decision 115), out of which the provider composes a query over
            // the navigation's rows. A root that is a call, whatever Entry is called on, like
            // Set<T>(); what the query is comes from the mapping, in the shared reading.
            case InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Query" or "Load" or "LoadAsync", Expression: InvocationExpressionSyntax entry },
            } loading
                when (MethodNameOf(loading) != "Query" || loading.ArgumentList.Arguments.Count == 0)
                     && TryReadNavigationEntry(entry, out var load):
                root = new LinqQueryRoot(load!.Navigation ?? load.Api, load);
                return true;

            // ctx.Customers - a DbSet property on the context. The context is recognized by
            // its position at the head of the chain, not by being called "ctx": a hard-coded
            // identifier made every other name silently unreadable.
            case MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax:
                root = new LinqQueryRoot(member.Name.Identifier.Text);
                return true;

            // this._ctx.Customers - the same name as _ctx.Customers, written through this
            // (decision 114). Read by the bare name alone, it was no root, and the query
            // vanished without a word.
            case MemberAccessExpressionSyntax { Expression: MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax } } member:
                root = new LinqQueryRoot(member.Name.Identifier.Text);
                return true;

            // Inside the context, its own DbSet is the root the same way ctx.Customers is from
            // outside - by bare name, through this, or as Set<T>() (decision 111). Anywhere
            // else a member of the class's own instance is a navigation over what the instance
            // loaded, which no provider translates: Lines.Sum(…) in an entity is LINQ over
            // objects in memory, not a query.
            case IdentifierNameSyntax own when EFCoreContext.IsOwnDbSet(own, own.Identifier.Text):
                root = new LinqQueryRoot(own.Identifier.Text);
                return true;

            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } own
                when EFCoreContext.IsOwnDbSet(own, own.Name.Identifier.Text):
                root = new LinqQueryRoot(own.Name.Identifier.Text);
                return true;

            case InvocationExpressionSyntax { Expression: GenericNameSyntax { Identifier.Text: "Set" } set } bare
                when EFCoreContext.Around(bare) is not null && TypeArgumentOf(set) is { } ownEntity:
                root = new LinqQueryRoot(ownEntity);
                return true;

            default:
                return false;
        }
    }
}
