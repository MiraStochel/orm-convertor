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
}
