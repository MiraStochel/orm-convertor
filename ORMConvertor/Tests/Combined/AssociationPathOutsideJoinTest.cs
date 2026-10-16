using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using SampleData;

namespace Tests.Combined;

/// <summary>
/// A path through an association outside join, as real projects write it (decision 144):
/// the Keycloak and Ambari excerpts carry such queries in the text their projects wrote,
/// and the whole conversion reads them - the path to the referenced key as the foreign
/// key column, every other path as an implicit join derived from the relation.
/// </summary>
public class AssociationPathOutsideJoinTest
{
    private static ConversionSource Java(string path)
        => new() { Content = ThirdPartySources.Read(path), ContentType = ConversionContentType.Java };

    [Fact]
    public void KeycloakReadsThePathToTheUserKeyAsTheForeignKeyColumn()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.NHibernate,
        [
            Java("keycloak/entities/UserConsentEntity.java"),
            Java("keycloak/entities/UserEntity.java"),
        ]);

        // consent.user.id = :userId is the USER_ID column the consent carries; no scalar
        // property maps it, so HQL writes it back as the same path, and nothing is joined.
        var consents = result.Sources.Where(s => s.ContentType == ConversionContentType.HqlQuery && s.Content.Contains("from UserConsentEntity consent", StringComparison.Ordinal)).ToList();

        Assert.Equal(3, consents.Count);
        Assert.All(consents, hql => Assert.Contains("where consent.user.id = :userId", hql.Content, StringComparison.Ordinal));
        Assert.All(consents, hql => Assert.DoesNotContain("join", hql.Content, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Query?.StartsWith("userConsent", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void AmbariReadsThePathsThroughTheHistoryAsImplicitJoins()
    {
        var result = ConversionHandler.Convert(ORMEnum.EclipseLink, ORMEnum.Dapper,
        [
            Java("ambari/entities/AlertCurrentEntity.java"),
            Java("ambari/entities/AlertHistoryEntity.java"),
            Java("ambari/entities/AlertDefinitionEntity.java"),
        ]);

        // findByHostAndName crosses alertHistory three times and alertHistory.alertDefinition
        // once: two inner joins, each made once, under aliases that say where the row came from.
        var sql = result.Sources.Single(s => s.ContentType == ConversionContentType.SqlQuery
            && s.Content.Contains("definition_name = @definitionName AND alert_alertHistory.host_name = @hostName", StringComparison.Ordinal)).Content;

        Assert.Contains("INNER JOIN alert_history alert_alertHistory ON alert.history_id = alert_alertHistory.alert_id", sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN alert_definition alert_alertHistory_alertDefinition ON alert_alertHistory.alert_definition_id = alert_alertHistory_alertDefinition.definition_id", sql, StringComparison.Ordinal);
        Assert.Equal(2, sql.Split("INNER JOIN").Length - 1);
        Assert.Contains("WHERE alert_alertHistory.cluster_id = @clusterId AND", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Query?.StartsWith("AlertCurrentEntity.", StringComparison.Ordinal) == true);
    }
}
