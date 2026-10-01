using Model;
using ORMConvertorAPI.Dtos;

namespace ORMConvertorAPI.Data;

/// <summary>
/// What the interface can collect for each source framework: the languages it reads, each
/// once (decision 111). A unit is one file in one language with whatever it holds - entity
/// classes and the code that queries them alike -, and which of its classes are entities and
/// where it hands a query over is what the source framework reads out of it; the languages
/// themselves are named by the content type (decision 025), so a Dapper query may stand in
/// its C# call or bare as SQL, and a Hibernate one in Java or bare as JPQL.
/// </summary>
public static class RequiredContent
{
    public static List<RequiredContentDefinition> GetRequiredContent => [
        new (ORMEnum.Dapper, [
            new(1, ConversionContentType.CSharp, "C# source"),
            new(8, ConversionContentType.SqlQuery, "Query (SQL)"),
        ]),
        new (ORMEnum.NHibernate, [
            new (2, ConversionContentType.CSharp, "C# source"),
            new (3, ConversionContentType.XML, "XML Mapping"),
            new (10, ConversionContentType.HqlQuery, "Query (HQL)"),
        ]),
        new (ORMEnum.EFCore, [
            new(4, ConversionContentType.CSharp, "C# source"),
        ]),
        new (ORMEnum.Hibernate, [
            new (11, ConversionContentType.Java, "Java source"),
            new (12, ConversionContentType.XML, "orm.xml Mapping"),
            new (14, ConversionContentType.JpqlQuery, "Query (JPQL)"),
        ]),
        // The same three languages as Hibernate's, because both implementations read the
        // same specification and the same documents; only the samples behind them differ
        // (decision 080).
        new (ORMEnum.EclipseLink, [
            new (15, ConversionContentType.Java, "Java source"),
            new (16, ConversionContentType.XML, "orm.xml Mapping"),
            new (18, ConversionContentType.JpqlQuery, "Query (JPQL)"),
        ]),
        // Java and XML and no new content type (decisions 025 and 084): the domain class and
        // the mapper interface are Java like any other, and the mapper document is XML like
        // any other mapping one. The interface and the document are a mapping and a query at
        // once, which is why neither is asked for twice - the orchestration offers every unit
        // to both passes (decision 081).
        new (ORMEnum.MyBatis, [
            new (19, ConversionContentType.Java, "Java source"),
            new (21, ConversionContentType.XML, "XML Mapper"),
        ]),
    ];

    /// <summary>
    /// What the Advisor screen collects: the shared units and the query templates apart,
    /// because the Advisor weighs every query and a C# unit no longer says by its content
    /// type which of the two it is (decision 111).
    /// </summary>
    public static List<AdvisorRequiredContentDefinition> GetRequiredContentAdvisor => [
        new (ORMEnum.Dapper, [
            new(1, ConversionContentType.CSharp, "Entity Class"),
        ], []),
        new (ORMEnum.NHibernate, [
            new (2, ConversionContentType.CSharp, "Entity Class"),
            new (3, ConversionContentType.XML, "XML Mapping"),
        ], []),
        new (ORMEnum.EFCore, [
            new(4, ConversionContentType.CSharp, "Entity Class"),
        ], [
            new (5, ConversionContentType.CSharp, "Query Method"),
            new(6, ConversionContentType.CSharp, "Query Method 2"),
        ]),
    ];
}
