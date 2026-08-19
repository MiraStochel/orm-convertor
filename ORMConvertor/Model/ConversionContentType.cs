namespace Model;

/// <summary>
/// What language a conversion source or artifact is written in (decision 025). The values
/// name a <em>language</em>, not a framework, so the vocabulary grows with ecosystems
/// rather than with wrappers: a Java ecosystem adds JavaEntity and Jpql, and Jpql then
/// serves Hibernate and EclipseLink alike.
/// </summary>
public enum ConversionContentType
{
    CSharpEntity = 10,

    /// <summary>A query written in C#: a LINQ chain, or a method wrapping a query string.</summary>
    CSharpQuery = 20,

    /// <summary>NHibernate hbm mapping.</summary>
    XML = 30,

    /// <summary>A query written in SQL.</summary>
    SqlQuery = 40,

    /// <summary>A query written in HQL.</summary>
    HqlQuery = 50,
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
        ConversionContentType.HqlQuery;
}
