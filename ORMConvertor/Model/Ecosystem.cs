namespace Model;

/// <summary>
/// Closed vocabulary of the two ecosystems a framework belongs to (decision 090). It
/// stands beside <see cref="ORMEnum"/> and <see cref="DatabaseDialect"/> for the same
/// reason those two do: it is a vocabulary the descriptor speaks about a framework with,
/// not a fact of the intermediate representation - the pivot knows nothing about which
/// language a framework is written for, and that is the whole point of it.
///
/// It exists because F10 counts translations across this boundary, and a boundary nobody
/// declares can only be drawn by a list written by hand in a test - which a seventh
/// framework would walk past in silence, leaving the count exactly where it was.
///
/// The values are numbered by tens, as <see cref="ORMEnum"/> is, so a third ecosystem can
/// be placed between two existing ones rather than only after them.
/// </summary>
public enum Ecosystem
{
    /// <summary>Dapper, EF Core and NHibernate: artifacts in C#, consumed by a .NET project.</summary>
    DotNet = 10,

    /// <summary>
    /// Hibernate, EclipseLink and MyBatis: artifacts in Java, consumed by a JVM project
    /// (decision 076). That the wrapper generating them is itself written in C# says
    /// nothing here - the ecosystem is the artifact's, not the wrapper's.
    /// </summary>
    Java = 20,
}
