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

    /// <summary>
    /// MyBatis, the third framework of the Java ecosystem and the only one of the six that
    /// stands on no framework layer at all (decision 084): its wrapper is built over two
    /// layers of <em>language</em> - Java for the entity, T-SQL for the query - because
    /// MyBatis implements no specification. It is also the first framework whose mapping is
    /// bound to a statement rather than to a class.
    /// </summary>
    MyBatis = 60,
}
