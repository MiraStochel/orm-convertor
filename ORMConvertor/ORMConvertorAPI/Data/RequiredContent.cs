using Model;
using ORMConvertorAPI.Dtos;

namespace ORMConvertorAPI.Data;
public static class RequiredContent
{
    public static List<RequiredContentDefinition> GetRequiredContent => [
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
        ]),
    ];
}
