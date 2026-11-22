using System.Diagnostics.CodeAnalysis;
using AbstractWrappers;
using TransactSql;

namespace JakartaPersistence;

/// <summary>
/// How the name of a table or a column stands in the annotations, so that the SQL an
/// implementation generates over it reads as a name. Neither Hibernate 7.4.5 nor EclipseLink
/// 5.0.0 quotes a keyword by itself: a table or a column spelled like a reserved word of T-SQL
/// - <c>User</c>, <c>Rule</c>, <c>Key</c>, <c>Order</c> - reaches the SQL bare and the server
/// refuses the statement. Such a name is written as the delimited identifier of Jakarta
/// Persistence 3.2 §2.13, the name in double quotes inside the string; Hibernate writes it in
/// the brackets of its dialect, EclipseLink in double quotes, in every statement. Hibernate's
/// backticks would not do: EclipseLink takes them for characters of the name. Measured on the
/// pinned releases (JavaTests <c>hibernate/ReservedSqlNameClaimsTest</c>,
/// <c>eclipselink/ReservedSqlNameClaimsTest</c>). Which words are reserved is the one list the
/// SQL targets and the NHibernate mapping use (<see cref="SqlNames"/>); every other name,
/// a name that is no identifier among them, stays as the source spelled it. The reading strips
/// either delimiter, so an artifact the tool wrote is one it reads.
/// </summary>
internal static class JpaDatabaseNames
{
    /// <summary>The name as the string literal of an annotation spells it, the escaped quotes included.</summary>
    public static string Written(string name)
        => Identifiers.IsRegular(name) && SqlNames.IsReserved(name) ? $"\\\"{name}\\\"" : name;

    /// <summary>A name the source delimited - in the specification's double quotes or in Hibernate's backticks - without the delimiters.</summary>
    [return: NotNullIfNotNull(nameof(name))]
    public static string? Bare(string? name)
        => name is { Length: > 2 } && ((name[0] == '"' && name[^1] == '"') || (name[0] == '`' && name[^1] == '`'))
            ? name[1..^1]
            : name;
}
