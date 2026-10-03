using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using Common.Naming;
using Microsoft.CodeAnalysis.CSharp;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using TransactSql;

namespace EFCoreWrappers;

/// <summary>
/// EF Core's escape path (decision 113): the query its LINQ does not speak, written whole by
/// the shared T-SQL writer - the text the Dapper target writes - and handed to EF Core's own
/// API for native SQL. <c>ctx.Set&lt;T&gt;().FromSql</c> for a query over the whole of an
/// entity, <c>ctx.Database.SqlQuery&lt;TRow&gt;</c> for any other, both with the parameters
/// as interpolation holes, which EF Core binds as parameters of the command.
///
/// The method keeps the shape a LINQ translation has - a DbContext first, an IQueryable
/// back -, so the levels of verification and the Advisor find it where they look. The
/// objection decision 022 raised, that the escape path must return the columns of a mapped
/// entity, fell with EF Core 8, which opened SqlQuery to a type no model maps; that type is
/// generated beside the method, a property per column, named the way the column comes back
/// and typed by the template's gate. Verified against EF Core 10.0.10 when the path was
/// written: SqlQuery takes a text that begins with WITH, so long as nothing is composed over
/// it, and ToQueryString gives the text back unchanged.
///
/// Beside the method goes the bare SQL, as Dapper publishes it (decision 025): it is the
/// query, and what the third level of verification reads (decision 113).
/// </summary>
public sealed class EFCoreNativeSqlQueryBuilder : AbstractSqlQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => EFCoreDescriptor.Instance;

    protected override List<ConversionSource> Emit(string sql, string? resultEntity)
    {
        // An interpolated collection is one value to EF Core - since EF Core 8 a JSON text -
        // and not the list IN ranges over (verified against 10.0.10: `IN @p0` with
        // N'[1,2]'), so the one shape the native API of this target cannot take is refused.
        if (Parameters.FirstOrDefault(p => p.IsCollection) is { } collection)
        {
            Report(
                ConversionRecordKind.Failure,
                $"EF Core binds an interpolated collection as one value, not as the list IN ranges over, so the collection parameter {QueryParameterNaming.IdentifierFor(collection)} has no form in its native SQL; no artifact was generated.",
                QueryFeature.QueryParameter);
            return [];
        }

        if (RefusesAnEntityOverDifferentRows(resultEntity, "FromSql"))
        {
            return [];
        }

        string returns;
        string call;
        var row = string.Empty;

        if (resultEntity is not null)
        {
            returns = $"IQueryable<{resultEntity}>";
            call = $"ctx.Set<{resultEntity}>().FromSql(";
        }
        else
        {
            var rowName = $"{MethodName}Row";
            if (RowClass(rowName) is not { } declaration)
            {
                return [];
            }

            returns = $"IQueryable<{rowName}>";
            call = $"ctx.Database.SqlQuery<{rowName}>(";
            row = "\n\n" + declaration;
        }

        // An interpolated raw string literal takes a brace of the text as a hole unless the
        // literal has more dollar signs than the longest run of braces in it, so the count
        // follows the text: one for any text T-SQL usually is.
        var braces = LongestBraceRun(sql);
        var dollars = new string('$', braces + 1);
        var (open, close) = (new string('{', braces + 1), new string('}', braces + 1));

        var text = SqlPlaceholders.Respell(sql, Parameters, p => $"{open}{QueryParameterNaming.IdentifierFor(p)}{close}");
        var indented = string.Join("\n", text.Split('\n').Select(line => "        " + line));

        var method =
            $$""""
            public static {{returns}} {{MethodName}}(DbContext ctx{{CSharpParameters()}})
            {
                return {{call}}
                    {{dollars}}"""
            {{indented}}
                    """);
            }{{row}}
            """";

        return
        [
            new() { Content = method, ContentType = ConversionContentType.CSharpQuery },
            new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ];
    }

    /// <summary>
    /// The class SqlQuery materializes a row into: a property per column of the result, named
    /// as the column comes back and nullable, because a column of a native query may come back
    /// NULL - an outer join, an aggregate over nothing - and a non-nullable property would
    /// throw there instead. Null when the row cannot be declared, the reason on the channel:
    /// a column without a name has nothing to be filled by, one the gate cannot type nothing
    /// to be declared as, and two of one name one property for two values.
    /// </summary>
    private string? RowClass(string rowName)
    {
        var columns = ResultColumns();
        var properties = new List<string>(columns.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            if (column.Name is not { } name)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The column '{column.Operand}' of the result carries no name, and SqlQuery fills the properties of the row by the names of the columns; no artifact was generated.",
                    QueryFeature.Projection);
                return null;
            }

            if (!names.Add(name))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"Two columns of the result are named '{name}', and SqlQuery fills one property of the row by one name; no artifact was generated.",
                    QueryFeature.Projection);
                return null;
            }

            if (!SyntaxFacts.IsValidIdentifier(name))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The column '{name}' of the result is not a C# identifier, so no property of the row can carry its name; no artifact was generated.",
                    QueryFeature.Projection);
                return null;
            }

            // SQL Server answers COUNT with an int whatever the language targets make of it -
            // through an intermediate result as well -, and EF Core reads a column with the
            // getter of the property's type, which does not widen.
            var scalar = column.CountsRows ? ScalarType.Int : column.Scalar;

            if (scalar is null or ScalarType.Object)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The column '{name}' of the result is typed by nothing the mapping states, so the row SqlQuery materializes into cannot declare it; no artifact was generated.",
                    QueryFeature.Projection);
                return null;
            }

            var identifier = SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
            properties.Add($"    public {CSharpTypeConvertor.ToString(LangType.Scalar(scalar.Value))}? {identifier} {{ get; set; }}");
        }

        return $"public sealed class {rowName}\n{{\n{string.Join("\n", properties)}\n}}";
    }

    private static int LongestBraceRun(string text)
    {
        var longest = 0;
        var run = 0;
        var previous = '\0';

        foreach (var character in text)
        {
            run = character is '{' or '}' && character == previous ? run + 1 : character is '{' or '}' ? 1 : 0;
            longest = Math.Max(longest, run);
            previous = character;
        }

        return longest;
    }
}
