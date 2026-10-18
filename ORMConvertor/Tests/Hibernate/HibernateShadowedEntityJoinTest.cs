using DapperWrappers;
using EclipseLinkWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.Hibernate;

/// <summary>
/// Hibernate 7.4.5 resolves an unqualified name in a join against the attributes of the
/// entities already in the from clause before the entity names (measured over the FIBEN
/// catalog, decision 149: <c>join POSTALADDRESS p</c> beside the entity ADDRESS, whose column
/// POSTALADDRESS holds the key, joined the column and every path through <c>p</c> failed). The
/// JPQL for Hibernate names such an entity by its fully qualified class name, which Hibernate
/// resolves as the entity; EclipseLink reads the bare name as the entity and keeps it.
/// </summary>
public class HibernateShadowedEntityJoinTest
{
    private const string Sql = "SELECT p.HASSTATE AS State FROM ADDRESS a INNER JOIN POSTALADDRESS p ON a.POSTALADDRESS = p.POSTALADDRESSID";

    private static List<EntityMap> Entities()
    {
        var addressId = new Property { Name = "ADDRESSID", Type = LangType.Scalar(ScalarType.Long) };
        var postal = new Property { Name = "POSTALADDRESS", Type = LangType.Scalar(ScalarType.Long, isNullable: true) };
        var postalId = new Property { Name = "POSTALADDRESSID", Type = LangType.Scalar(ScalarType.Long) };
        var state = new Property { Name = "HASSTATE", Type = LangType.Scalar(ScalarType.String, isNullable: true) };

        return
        [
            new EntityMap
            {
                Entity = new Entity { Name = "ADDRESS", Namespace = "Fiben", Properties = [addressId, postal] },
                Table = "ADDRESS",
                PropertyMaps = [new PropertyMap { Property = addressId, ColumnName = "ADDRESSID" }, new PropertyMap { Property = postal, ColumnName = "POSTALADDRESS" }],
            },
            new EntityMap
            {
                Entity = new Entity { Name = "POSTALADDRESS", Namespace = "Fiben", Properties = [postalId, state] },
                Table = "POSTALADDRESS",
                PropertyMaps = [new PropertyMap { Property = postalId, ColumnName = "POSTALADDRESSID" }, new PropertyMap { Property = state, ColumnName = "HASSTATE" }],
            },
        ];
    }

    private static string Jpql(ORMEnum target)
    {
        AbstractWrappers.AbstractQueryBuilder builder = target == ORMEnum.Hibernate ? new HibernateJpqlQueryBuilder() : new EclipseLinkJpqlQueryBuilder();
        var entities = Entities();
        builder.EntityMaps = entities;
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, Sql, entities);
        return builder.Build().Single(source => source.ContentType == ConversionContentType.JpqlQuery).Content;
    }

    [Fact]
    public void HibernateNamesTheShadowedEntityByItsPackage()
    {
        var jpql = Jpql(ORMEnum.Hibernate);

        Assert.Contains("join Fiben.POSTALADDRESS p on", jpql, StringComparison.Ordinal);
        Assert.Contains("from ADDRESS a", jpql, StringComparison.Ordinal);
    }

    [Fact]
    public void EclipseLinkKeepsTheBareName()
    {
        var jpql = Jpql(ORMEnum.EclipseLink);

        Assert.Contains("join POSTALADDRESS p on", jpql, StringComparison.Ordinal);
        Assert.DoesNotContain("Fiben.", jpql, StringComparison.Ordinal);
    }
}
