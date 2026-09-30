using Model.AbstractRepresentation.Enums;

namespace TransactSql;

/// <summary>
/// What the source stated about one parameter of a SQL query, keyed by the parameter's
/// undecorated name and handed to <see cref="SqlQueryReader"/> at construction
/// (decision 084). Not a plug-in point of the grammar, which decision 082 refused as
/// needless: it is a fact about the text that the wrapper peeled off the source before the
/// grammar ever saw it, and it therefore stands in the same position as the report channel.
///
/// T-SQL itself states neither of the two, so the map is filled by the wrappers whose
/// source says more than T-SQL does. The MyBatis wrapper supplies both: the scalar from
/// <c>#{id,javaType=Integer}</c>, from a <c>parameterType</c> naming an entity of the
/// conversion, or from the signature of the mapper method (decision 083), and the
/// collection flag from a <c>&lt;foreach&gt;</c> in the canonical form, whose whole tag the
/// wrapper replaced by this one parameter. The Dapper wrapper supplies the collection flag
/// alone, from Dapper's own spelling of a list parameter - a bare parameter after IN, which
/// the wrapper puts in parentheses for the grammar (decision 106); the scalar of its
/// elements is the builder template's to derive, from the column with the catalog's table
/// binding (decision 105).
/// </summary>
/// <param name="Scalar">
/// The scalar the source stated, or null when it stated none; the builder template then
/// derives it from the other side of the comparison (decision 083).
/// </param>
/// <param name="IsCollection">
/// Whether the value bound is a list rather than a single value. True only where the source
/// said so, because the grammar cannot tell: <c>IN (@ids)</c> is a one-element list of
/// values under every other reader, and reading it as a collection there would bind
/// something the query did not write.
/// </param>
public readonly record struct SqlParameterFacts(ScalarType? Scalar = null, bool IsCollection = false);
