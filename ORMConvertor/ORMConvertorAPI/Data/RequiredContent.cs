using Model;
using ORMConvertorAPI.Dtos;

namespace ORMConvertorAPI.Data;

/// <summary>
/// What the interface has to collect for each source framework. Every unit names the
/// language its content is written in (decision 025), so a Dapper query is asked for as SQL
/// and an NHibernate query as the LINQ chain its parser reads.
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
