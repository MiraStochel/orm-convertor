namespace Model;

public enum ORMEnum
{
    Dapper = 10,
    NHibernate = 20,
    EFCore = 30,

    /// <summary>
    /// Hibernate ORM, the first framework of the Java ecosystem (decision 077): a thin
    /// profile over the shared Jakarta Persistence layer, read and written by wrappers in
    /// C# with no JVM in the translation path (decision 076).
    /// </summary>
    Hibernate = 40,

    /// <summary>
    /// EclipseLink, the second implementation of Jakarta Persistence (decision 080): the
    /// same shared layer as Hibernate with a profile of its own - AUTO is a counter table,
    /// an implicit name is upper case and national character data has no annotation.
    /// </summary>
    EclipseLink = 50,
}
