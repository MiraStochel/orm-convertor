namespace Model;

/// <summary>
/// Closed vocabulary of database systems a conversion can target (decision 086). It stands
/// beside <see cref="ORMEnum"/> rather than in Model.AbstractRepresentation on purpose:
/// both are vocabularies the target framework descriptor speaks about the target of a
/// conversion with, and neither is a fact of the intermediate representation - decision 019
/// said of the dialect exactly that, and sent it to the descriptor beside the framework
/// release of decision 013.
///
/// Closed rather than a free string because the descriptor binds to closed sets (decision
/// 009): the value picks a table of type spellings, and a name nobody can resolve would
/// have to be parsed at every emission site instead.
///
/// The values are numbered by tens, as ORMEnum is, so a second dialect can be placed
/// between two existing ones rather than only after them.
/// </summary>
public enum DatabaseDialect
{
    /// <summary>
    /// SQL Server 2022, the release pinned by decision 013 and the only database system
    /// this version targets. That it is the only one is a declared limit rather than an
    /// assumption to be found in the code (architecture.md, §9).
    /// </summary>
    SqlServer2022 = 10,
}
