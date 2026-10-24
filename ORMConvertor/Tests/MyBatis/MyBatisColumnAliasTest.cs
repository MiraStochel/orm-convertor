using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;
using Tests.Catalog;

namespace Tests.MyBatis;

/// <summary>
/// The alias in a MyBatis select whose result type is an entity of the conversion is the
/// source's statement of the column of a property (decision 181): the statement maps its rows
/// by auto-mapping, column of the result into the property of the same name, so
/// <c>CATID AS categoryId</c> is what <c>&lt;result column="CATID" property="categoryId"/&gt;</c>
/// is. The domain is JPetStore's (decision 176), whose categories the catalog of the case study
/// could not key for exactly this reason.
/// </summary>
public class MyBatisColumnAliasTest
{
    private const string Mapper = "mapper";

    private const string Domain = """
        package org.example.shop;

        public class Category {
            private String categoryId;
            private String name;
            private String description;
        }

        public class Product {
            private String productId;
            private String categoryId;
            private String name;
        }
        """;

    private const string Interface = """
        package org.example.shop;

        import java.util.List;

        public interface CategoryMapper {
            Category getCategory(String categoryId);
            List<Category> getCategoryList();
        }
        """;

    private const string Aliased = """
        <select id="getCategory" resultType="Category">
          SELECT CATID AS categoryId, NAME, DESCN AS description
          FROM CATEGORY
          WHERE CATID = #{categoryId}
        </select>
        """;

    private static TableImage CategoryImage() => new()
    {
        Schema = "dbo",
        Name = "Category",
        Columns =
        [
            new ColumnImage { Name = "catid", Type = DatabaseType.VarChar, Length = 10, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "name", Type = DatabaseType.VarChar, Length = 80, IsNullable = true, IsIdentity = false },
            new ColumnImage { Name = "descn", Type = DatabaseType.VarChar, Length = 255, IsNullable = true, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["catid"],
        ForeignKeys = [],
    };

    private static ConversionResult Convert(ORMEnum target, string statements, ICatalogReader? catalog = null, string mapperInterface = Interface) =>
        ConversionHandler.Convert(
            ORMEnum.MyBatis,
            target,
            [
                new() { ContentType = ConversionContentType.Java, Name = "domain", Content = Domain },
                new() { ContentType = ConversionContentType.Java, Name = "interface", Content = mapperInterface },
                new()
                {
                    ContentType = ConversionContentType.XML,
                    Name = Mapper,
                    Content = $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <!DOCTYPE mapper PUBLIC "-//mybatis.org//DTD Mapper 3.0//EN" "https://mybatis.org/dtd/mybatis-3-mapper.dtd">
                        <mapper namespace="org.example.shop.CategoryMapper">
                        {statements}
                        </mapper>
                        """,
                },
            ],
            catalog);

    private static string CategoryClass(ConversionResult result) =>
        result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpEntity && s.Content.Contains("class Category")).Content;

    /// <summary>
    /// JPetStore with the catalog of its schema: the alias pairs the property with the key
    /// column, so the key the catalog knows of catid lands on categoryId - where the property
    /// used to have no column, the key column no property, and the targets that require a key
    /// refused the entity.
    /// </summary>
    [Fact]
    public void TheAliasPairsThePropertyWithTheKeyColumnOfTheCatalog()
    {
        var result = Convert(ORMEnum.NHibernate, Aliased, new FakeCatalogReader(CategoryImage()));

        var mapping = result.Sources.Single(s => s.ContentType == ConversionContentType.XML && s.Content.Contains("class name=\"Category\"")).Content;

        Assert.True(mapping.Contains("<id name=\"categoryId\" type=\"String\">", StringComparison.Ordinal), mapping);
        Assert.Contains("<column name=\"CATID\" length=\"10\" />", mapping);
        Assert.Contains("<generator class=\"assigned\" />", mapping);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Entity == "Category");
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Entity == "Category" && r.Category == MappingFactCategory.PrimaryKey);
    }

    /// <summary>Without a catalog the target carries the columns the statement stated, and the bare column stays as it is.</summary>
    [Fact]
    public void WithoutACatalogTheTargetCarriesTheColumns()
    {
        var category = CategoryClass(Convert(ORMEnum.EFCore, Aliased));

        Assert.Contains("[Column(\"CATID\")]", category);
        Assert.Contains("[Column(\"DESCN\")]", category);
        Assert.DoesNotContain("[Column(\"NAME\")]", category);
    }

    /// <summary>The annotated spelling: a @Select on a method whose return type materializes the entity, with no @Results beside it.</summary>
    [Fact]
    public void AnAnnotatedSelectStatesTheColumnToo()
    {
        var result = Convert(ORMEnum.EFCore, statements: string.Empty, mapperInterface: """
            package org.example.shop;

            import java.util.List;
            import org.apache.ibatis.annotations.Select;

            public interface CategoryMapper {
                @Select("SELECT CATID AS categoryId, NAME, DESCN AS description FROM CATEGORY")
                List<Category> getCategoryList();
            }
            """);

        Assert.Contains("[Column(\"CATID\")]", CategoryClass(result));
    }

    /// <summary>The statement is the source's, so the ledger gives the fact to the unit that holds it, as stated and not supplied.</summary>
    [Fact]
    public void TheFactBelongsToTheUnitWithTheStatement()
    {
        var result = Convert(ORMEnum.EFCore, Aliased, new FakeCatalogReader(CategoryImage()));

        var fact = Assert.Single(result.Facts, f => f.Entity == "Category" && f.Property == "categoryId" && f.Category == MappingFactCategory.ColumnName);
        Assert.Equal(Mapper, fact.Unit);
        Assert.Equal(FactOrigin.Source, fact.Origin);
        Assert.Equal("CATID", fact.Value);
    }

    /// <summary>
    /// What states no column (decision 181): a statement with a resultMap, whose explicit map is
    /// the mapping and whose remaining columns MyBatis maps only under a setting of the
    /// configuration; an unqualified column over two tables, which only the schema can place;
    /// and a dotted alias, which names a nested property and no property of the class.
    /// </summary>
    [Theory]
    [InlineData("""
        <resultMap id="categoryMap" type="Category">
          <result column="CATID" property="categoryId"/>
        </resultMap>
        <select id="getCategory" resultMap="categoryMap">
          SELECT CATID, NAME, DESCN AS description FROM CATEGORY WHERE CATID = #{categoryId}
        </select>
        """)]
    [InlineData("""
        <select id="getCategory" resultType="Category">
          SELECT CATID AS categoryId, NAME, DESCN AS description
          FROM CATEGORY INNER JOIN PRODUCT ON PRODUCT.CATEGORY = CATEGORY.CATID
          WHERE CATID = #{categoryId}
        </select>
        """)]
    [InlineData("""
        <select id="getCategory" resultType="Category">
          SELECT CATID AS categoryId, NAME, DESCN AS "product.description" FROM CATEGORY WHERE CATID = #{categoryId}
        </select>
        """)]
    public void WhatMapsNoRowOntoThePropertyStatesNoColumn(string statements)
    {
        var category = CategoryClass(Convert(ORMEnum.EFCore, statements));

        Assert.DoesNotContain("[Column(\"DESCN\")]", category);
    }
}
