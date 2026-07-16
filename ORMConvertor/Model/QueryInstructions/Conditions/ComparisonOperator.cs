namespace Model.QueryInstructions.Conditions;

public enum ComparisonOperator
{
    Equal = 1,              // =
    NotEqual = 2,           // <>
    GreaterThan = 3,        // >
    GreaterThanOrEqual = 4, // >=
    LessThan = 5,           // 
    LessThanOrEqual = 6,    // <=
    Like = 7,               // LIKE
    In = 8,                 // IN (...)
    IsNull = 9,             // IS NULL – pravý operand se ignoruje (rozhodnutí 7.2 v design docu 001)
    IsNotNull = 10,         // IS NOT NULL – pravý operand se ignoruje
}