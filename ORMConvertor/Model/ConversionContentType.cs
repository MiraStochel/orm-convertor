namespace Model;

/// <summary>
/// What language a conversion source or artifact is written in (decision 025). The values
/// name a <em>language</em>, not a framework, so the vocabulary grows with ecosystems
/// rather than with wrappers: the Java ecosystem added Java and JpqlQuery (decisions 077
/// and 111), and JpqlQuery serves Hibernate and EclipseLink alike.
///
/// An input unit declares its language and nothing else (decision 111): <see cref="CSharp"/>
/// and <see cref="Java"/> are whole files with whatever they hold, and which of their classes
/// are entities and where they hand a query over is said by the source framework, class by
/// class. A value that names a role besides the language - <see cref="CSharpEntity"/>,
/// <see cref="CSharpQuery"/>, <see cref="CSharpLinqQuery"/>, <see cref="JavaEntity"/>,
/// <see cref="JavaQuery"/> - is carried only by an artifact, because the target builder wrote
/// each artifact in one role and says so; no parser claims one of them on input.
/// </summary>
public enum ConversionContentType
{
    /// <summary>Artifact only: an entity class written in C#.</summary>
    CSharpEntity = 10,

    /// <summary>Artifact only: a query written in C# - a LINQ chain, or a method wrapping a query string.</summary>
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

    /// <summary>
    /// Artifact only: a Java class - an entity with jakarta.persistence annotations
    /// (decision 077), or the plain domain class MyBatis writes (decision 084).
    /// </summary>
    JavaEntity = 60,

    /// <summary>
    /// Artifact only: Java carrying a query - a method wrapping JPQL in createQuery, or the
    /// method of a MyBatis mapper interface with its @Param parameters (decision 084).
    /// </summary>
    JavaQuery = 70,

    /// <summary>A query written in JPQL - or in HQL, which is its superset (decision 077).</summary>
    JpqlQuery = 80,

    /// <summary>
    /// A C# file, or a fragment of one - a bare method, statements -, with whatever it holds:
    /// entity classes, a repository, a context, top-level code (decision 111). Which classes
    /// are entities and where a query is handed over is what the source framework reads out
    /// of it.
    /// </summary>
    CSharp = 90,

    /// <summary>
    /// A Java file, or a fragment of one - a bare method -, with whatever it holds: classes,
    /// interfaces, the code that hands a query over (decision 111).
    /// </summary>
    Java = 100,

    /// <summary>
    /// Artifact only: the second form of a query whose binding form is another C# method - a
    /// LINQ chain over <c>session.Query&lt;T&gt;()</c> beside NHibernate's HQL (decision 118).
    /// A value of its own so that a consumer taking the one <see cref="CSharpQuery"/> method
    /// of a query still finds exactly one; on input it is C# like any other.
    /// </summary>
    CSharpLinqQuery = 110,
}

public static class ConversionContentTypes
{
    /// <summary>
    /// Whether an artifact is a query rather than an entity or a mapping. A statement about
    /// the values the builders write: an input unit declares its language only, and a C# or
    /// Java unit is neither one nor the other until the source framework has read it
    /// (decision 111).
    /// </summary>
    public static bool IsQuery(this ConversionContentType contentType) => contentType is
        ConversionContentType.CSharpQuery or
        ConversionContentType.CSharpLinqQuery or
        ConversionContentType.SqlQuery or
        ConversionContentType.HqlQuery or
        ConversionContentType.JavaQuery or
        ConversionContentType.JpqlQuery;

    /// <summary>
    /// The language a value names, which is the value an input unit in that language declares
    /// (decision 111): the role values of the artifacts fold into the language they are
    /// written in, and every other value names its language already. It is what feeding an
    /// artifact of the tool back to it as input takes.
    /// </summary>
    public static ConversionContentType LanguageOf(this ConversionContentType contentType) => contentType switch
    {
        ConversionContentType.CSharpEntity or ConversionContentType.CSharpQuery or ConversionContentType.CSharpLinqQuery => ConversionContentType.CSharp,
        ConversionContentType.JavaEntity or ConversionContentType.JavaQuery => ConversionContentType.Java,
        _ => contentType,
    };
}
