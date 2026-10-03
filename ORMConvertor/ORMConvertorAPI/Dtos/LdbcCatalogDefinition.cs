using Model;
using SampleData;

namespace ORMConvertorAPI.Dtos;

/// <summary>
/// The LDBC query catalog of decision 110 as the page reads it: the source framework the
/// queries are written for, the entity units over the LDBC tables, and the 41 read queries.
/// A query the page runs is sent to <c>/convert</c> as the entity units plus the query as one
/// more unit, so the page states no unit of its own.
/// </summary>
public record LdbcCatalogDefinition(
    ORMEnum SourceOrm,
    List<ConversionSource> Entities,
    List<LdbcQueryDefinition> Queries
);

/// <summary>
/// One read query of the specification: its workload and number, the title the specification
/// gives it, how much of it the tool translates and the note saying what that means, the T-SQL
/// text when there is one, the parameters with example values over scale factor 1, the
/// targets that refuse it although the others translate it, the targets that write it in
/// the native SQL of their dialect because their query language does not speak it
/// (decision 113), and for a query of the Interactive workload its binding to the validation
/// set that judges it (decision 117) - which the Java suite reads from here, as it takes
/// everything else from a running instance (decision 078).
/// </summary>
public record LdbcQueryDefinition(
    string Key,
    LdbcWorkload Workload,
    int Number,
    string Title,
    LdbcTranslation Translation,
    string Note,
    string? Sql,
    List<LdbcParameterDefinition> Parameters,
    List<LdbcRefusalDefinition> RefusedBy,
    List<LdbcRefusalDefinition> FallbackBy,
    LdbcValidationDefinition? Validation
);

/// <summary>A parameter: name without the @, SQL Server type, example value; a list's values are comma-separated.</summary>
public record LdbcParameterDefinition(string Name, string SqlType, string Example, bool IsList);

/// <summary>A target that refuses the query or falls back from it, and why, in the page's words.</summary>
public record LdbcRefusalDefinition(ORMEnum Target, string Reason);

/// <summary>
/// The binding of a query to the validation set of LDBC Interactive v1 (decision 117): the
/// driver's operation, the parameters of the text made from its fields, the columns of the text
/// in the order it projects them with the fields of the expected result, and whether the rows
/// are compared in their order.
/// </summary>
public record LdbcValidationDefinition(
    string Operation,
    List<LdbcArgumentDefinition> Arguments,
    List<LdbcResultFieldDefinition> Fields,
    bool Ordered
);

/// <summary>A parameter of the text and the field of the operation it is made from; the derivation as the number of its enum.</summary>
public record LdbcArgumentDefinition(string Parameter, string Field, LdbcDerivation Derivation, string? Operand);

/// <summary>A column of the text and the field of the result it answers to; the kind as the number of its enum.</summary>
public record LdbcResultFieldDefinition(
    string Column,
    string Field,
    LdbcValueKind Kind,
    string? Separator,
    List<string>? Elements,
    string? ElementSeparator);
