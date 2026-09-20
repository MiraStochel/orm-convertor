using Model;
using ORMConvertorAPI.Dtos;

namespace ORMConvertorAPI.Data;

/// <summary>
/// What the interface has to collect for each source framework. Every unit names the
/// language its content is written in (decision 025), so a Dapper query is asked for as SQL,
/// an NHibernate query as the LINQ chain its parser reads, and a Hibernate query as a Java
/// method or as bare JPQL (decision 077).
/// </summary>
public static class RequiredContent
{
    public static List<RequiredContentDefinition> GetRequiredContent => [
        new (ORMEnum.Dapper, [
            new(1, ConversionContentType.CSharpEntity, "Entity Class"),
            new(8, ConversionContentType.SqlQuery, "Query (SQL)"),
        ]),
        new (ORMEnum.NHibernate, [
            new (2, ConversionContentType.CSharpEntity, "Entity Class"),
            new (3, ConversionContentType.XML, "XML Mapping"),
            new (9, ConversionContentType.CSharpQuery, "Query (LINQ)"),
            new (10, ConversionContentType.HqlQuery, "Query (HQL)"),
        ]),
        new (ORMEnum.EFCore, [
            new(4, ConversionContentType.CSharpEntity, "Entity Class"),
            new (5, ConversionContentType.CSharpQuery, "Query (LINQ)"),
        ]),
        new (ORMEnum.Hibernate, [
            new (11, ConversionContentType.JavaEntity, "Entity Class (Java)"),
            new (12, ConversionContentType.XML, "orm.xml Mapping"),
            new (13, ConversionContentType.JavaQuery, "Query (Java method)"),
            new (14, ConversionContentType.JpqlQuery, "Query (JPQL)"),
        ]),
        // The same four units as Hibernate's, because both implementations read the same
        // specification and the same documents; only the samples behind them differ
        // (decision 080).
        new (ORMEnum.EclipseLink, [
            new (15, ConversionContentType.JavaEntity, "Entity Class (Java)"),
            new (16, ConversionContentType.XML, "orm.xml Mapping"),
            new (17, ConversionContentType.JavaQuery, "Query (Java method)"),
            new (18, ConversionContentType.JpqlQuery, "Query (JPQL)"),
        ]),
        // Three units and no new content type (decisions 025 and 084): the mapper interface
        // is Java like any other query unit and the mapper document is XML like any other
        // mapping one. Two of the three are a mapping and a query at once, which is why
        // neither is asked for twice - the orchestration offers every unit to both passes.
        new (ORMEnum.MyBatis, [
            new (19, ConversionContentType.JavaEntity, "Domain Class (Java)"),
            new (20, ConversionContentType.JavaQuery, "Mapper Interface (Java)"),
            new (21, ConversionContentType.XML, "XML Mapper"),
        ]),
    ];

    public static List<RequiredContentDefinition> GetRequiredContentAdvisor => [
        new (ORMEnum.Dapper, [
            new(1, ConversionContentType.CSharpEntity, "Entity Class")
        ]),
        new (ORMEnum.NHibernate, [
            new (2, ConversionContentType.CSharpEntity, "Entity Class"),
            new (3, ConversionContentType.XML, "XML Mapping"),
        ]),
        new (ORMEnum.EFCore, [
            new(4, ConversionContentType.CSharpEntity, "Entity Class"),
            new (5, ConversionContentType.CSharpQuery, "Query Method"),
            new(6, ConversionContentType.CSharpQuery, "Query Method 2"),
        ]),
    ];
}
