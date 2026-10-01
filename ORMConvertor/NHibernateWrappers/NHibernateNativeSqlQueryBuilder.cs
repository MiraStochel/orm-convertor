using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model;
using Model.AbstractRepresentation.Enums;
using TransactSql;

namespace NHibernateWrappers;

/// <summary>
/// NHibernate's escape path (decision 113): the query its HQL does not speak - a full outer
/// join, a set operation, an intermediate result, a slice inside a subquery -, written whole
/// by the shared T-SQL writer, the text the Dapper target writes, and handed to
/// <c>ISession.CreateSQLQuery</c>. The parameters are NHibernate's <c>:name</c>, bound by the
/// calls the HQL builder makes - a list by its own call, which NHibernate expands in a native
/// query as it does in HQL -, and the row is declared on the query: the whole entity by
/// AddEntity, any other row a scalar per column, named as the column comes back and typed
/// by the template's gate - or, where a column has no name or no type, no scalar at all, and
/// NHibernate reads the row as the driver types it. The method returns IQuery, which
/// ISQLQuery is, so the levels of verification and the Advisor see the shape an HQL
/// translation has.
///
/// Beside the method goes the bare SQL, as Dapper publishes it (decision 025): it is the
/// query, and what the third level of verification reads (decision 113).
/// </summary>
public sealed class NHibernateNativeSqlQueryBuilder : AbstractSqlQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => NHibernateDescriptor.Instance;

    protected override List<ConversionSource> Emit(string sql, string? resultEntity)
    {
        if (RefusesAnEntityOverDifferentRows(resultEntity, "AddEntity"))
        {
            return [];
        }

        var row = resultEntity is not null
            ? $"\n        .AddEntity(typeof({resultEntity}))"
            : Scalars();

        var text = SqlPlaceholders.Respell(sql, Parameters, p =>
            p.IsCollection ? $"(:{QueryParameterNaming.IdentifierFor(p)})" : $":{QueryParameterNaming.IdentifierFor(p)}");
        var indented = string.Join("\n", text.Split('\n').Select(line => "        " + line));

        var binding = string.Concat(Parameters.Select(p =>
        {
            var name = QueryParameterNaming.IdentifierFor(p);
            return p.IsCollection
                ? $"\n        .SetParameterList(\"{name}\", {name})"
                : $"\n        .SetParameter(\"{name}\", {name})";
        }));

        var method =
            $$""""
            public static IQuery {{MethodName}}(ISession session{{CSharpParameters()}})
            {
                return session.CreateSQLQuery(
                    """
            {{indented}}
                    """){{row}}{{binding}};
            }
            """";

        return
        [
            new() { Content = method, ContentType = ConversionContentType.CSharpQuery },
            new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ];
    }

    /// <summary>
    /// An AddScalar per column of the result, typed as the gate types it. Once one scalar is
    /// declared NHibernate returns only the declared ones, so it is every column or none:
    /// where a column has no name AddScalar could declare it by, or nothing the mapping states
    /// types it, none is declared, and NHibernate reads every column of the row in the type
    /// the driver reports - the same columns in the same order, typed by the database rather
    /// than by the model, which is what the record of kind Incompleteness says.
    /// </summary>
    private string Scalars()
    {
        var text = new System.Text.StringBuilder();

        foreach (var column in ResultColumns())
        {
            var type = column.Scalar is { } scalar ? TypeOf(scalar) : null;
            if (column.Name is null || type is null)
            {
                Report(
                    ConversionRecordKind.Incompleteness,
                    column.Name is null
                        ? $"The column '{column.Operand}' of the result carries no name, so no AddScalar declares the row; NHibernate reads its columns in the types the driver reports."
                        : $"The column '{column.Name}' of the result is typed by nothing the mapping states, so no AddScalar declares the row; NHibernate reads its columns in the types the driver reports.",
                    QueryFeature.Projection);
                return string.Empty;
            }

            text.Append($"\n        .AddScalar(\"{column.Name}\", NHibernateUtil.{type})");
        }

        return text.ToString();
    }

    /// <summary>
    /// The NHibernateUtil type a scalar is read with - the type NHibernate 5.7.0 maps the CLR
    /// type of the scalar to by default, so that a column of a native query comes back as the
    /// property of a mapped entity would. A time of day is the TimeAsTimeSpan type, because
    /// NHibernate's plain TimeSpan type reads a bigint of ticks.
    /// </summary>
    private static string? TypeOf(ScalarType scalar) => scalar switch
    {
        ScalarType.Bool => "Boolean",
        ScalarType.Byte => "Byte",
        ScalarType.Short => "Int16",
        ScalarType.Int => "Int32",
        ScalarType.Long => "Int64",
        ScalarType.Float => "Single",
        ScalarType.Double => "Double",
        ScalarType.Decimal => "Decimal",
        ScalarType.Char => "Character",
        ScalarType.String => "String",
        ScalarType.DateTime => "DateTime",
        ScalarType.Guid => "Guid",
        ScalarType.Date => "Date",
        ScalarType.TimeOfDay or ScalarType.Duration => "TimeAsTimeSpan",
        ScalarType.DateTimeOffset => "DateTimeOffset",
        ScalarType.ByteArray => "BinaryBlob",
        _ => null,
    };
}
