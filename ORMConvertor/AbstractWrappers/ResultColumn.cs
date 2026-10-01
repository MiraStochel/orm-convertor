using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// One column of a query's result as the template describes it (decision 113): the name it
/// comes back under, null where SQL gives it none - an aggregate or an expression without an
/// alias -, the scalar the gate derives for it, null where nothing types it, and the operand
/// it projects, null for a column of an intermediate result read whole.
/// </summary>
public sealed record ResultColumn(string? Name, ScalarType? Scalar, QueryOperand? Operand);
