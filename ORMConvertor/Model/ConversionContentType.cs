namespace Model;

/// <summary>
/// What language a conversion source or artifact is written in (decision 025). The values
/// name a <em>language</em>, not a framework, so the vocabulary grows with ecosystems
/// rather than with wrappers: the Java ecosystem added JavaEntity, JavaQuery and JpqlQuery
/// (decision 077), and JpqlQuery serves Hibernate and EclipseLink alike.
/// </summary>
public enum ConversionContentType
{
    CSharpEntity = 10,

    /// <summary>A query written in C#: a LINQ chain, or a method wrapping a query string.</summary>
    CSharpQuery = 20,

    /// <summary>
    /// A document written in XML: the hbm.xml of NHibernate, the orm.xml of a Jakarta
    /// Persistence implementation, the mapper of MyBatis. One value for all of them,
    /// because it names the <em>language</em> and the source framework says which dialect
    /// that is and what role it plays - the hbm.xml carries mapping and named queries
    /// alike, and the MyBatis mapper is mapping and query in one document (decisions 025
    /// and 081). The value therefore promises no role of its own; which document is asked
    /// for is said per framework by RequiredContent.
    /// </summary>
    XML = 30,

    /// <summary>A query written in SQL.</summary>
    SqlQuery = 40,

    /// <summary>A query written in HQL - the query language of NHibernate.</summary>
    HqlQuery = 50,

    /// <summary>A Java class: an entity with jakarta.persistence annotations (decision 077).</summary>
    JavaEntity = 60,

    /// <summary>A query written in Java: a method wrapping a JPQL string in createQuery.</summary>
    JavaQuery = 70,

    /// <summary>A query written in JPQL - or in HQL, which is its superset (decision 077).</summary>
    JpqlQuery = 80,
}

public static class ConversionContentTypes
{
    /// <summary>
    /// Whether the content is a query rather than an entity or a mapping. One place, because
    /// the orchestration asks the question at three points and a new query language that
    /// slipped past any one of them would fall silently into the entity branch.
    /// </summary>
    public static bool IsQuery(this ConversionContentType contentType) => contentType is
        ConversionContentType.CSharpQuery or
        ConversionContentType.SqlQuery or
        ConversionContentType.HqlQuery or
        ConversionContentType.JavaQuery or
        ConversionContentType.JpqlQuery;
}
