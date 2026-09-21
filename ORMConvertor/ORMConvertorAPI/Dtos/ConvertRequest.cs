using Model;

namespace ORMConvertorAPI.Dtos;

/// <param name="DeclaredSourceDialect">
/// The dialect the source declares for its literal SQL, optional (decision 088). Omitted -
/// the ordinary case - the source states nothing and its SQL is read as it always was;
/// <c>AnotherSystem</c> stops that reading, so a query is not emitted and a literal column
/// type is not read. It is stated once per conversion, beside the source framework: a
/// dialect is a property of the database the source project speaks to, not of one file, so
/// a mixed input is two requests rather than one with per-unit labels.
/// </param>
internal record ConvertRequest(
    ORMEnum SourceOrm,
    ORMEnum TargetOrm,
    List<ConversionSource> Sources,
    SourceSqlDialect? DeclaredSourceDialect = null
);
