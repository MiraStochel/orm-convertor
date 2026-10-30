namespace Model.QueryInstructions.Enums;

/// <summary>
/// The kind of a join (rule Q6). <see cref="Cross"/> is the cartesian product of the rows of
/// the two sides, which has no join condition (decision 196): comma-separated sources in
/// T-SQL, HQL and JPQL, <c>CROSS JOIN</c>, and a LINQ <c>SelectMany</c> over a second query
/// root; whatever condition the source wrote stays in its filter.
/// </summary>
public enum JoinKind { Inner, Left, Right, Full, Cross }
