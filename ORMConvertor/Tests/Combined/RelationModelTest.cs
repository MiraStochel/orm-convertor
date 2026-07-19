using EFCoreWrappers;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

public class RelationModelTest
{
    [Fact]
    public void EFCoreCollectionIsParsedAsInverseRelationOnEntity()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(SampleData.CustomerSampleEFCore.Entity);

        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Equal(RelationRole.Inverse, relation.Role);
        Assert.Equal("Customer", relation.SourceEntity);
        Assert.Equal("CustomerTransaction", relation.TargetEntity);
        Assert.Equal("Transactions", relation.SourceNavigationProperty);
        Assert.Empty(relation.ColumnPairs);
    }

    [Fact]
    public void NHibernateManyToOneIsParsedAsOwningRelation()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="CustomerTransaction" table="CustomerTransactions" schema="Sales">
                    <id name="TransactionID" column="TransactionID" type="int">
                        <generator class="identity" />
                    </id>
                    <many-to-one name="Customer" class="Customer" column="CustomerID" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Equal("Customer", relation.TargetEntity);
        Assert.Equal("Customer", relation.SourceNavigationProperty);
        Assert.False(relation.IsUnique);
    }

    [Fact]
    public void NHibernateOneToOneIsParsedAsOwningUniqueRelation()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Customer" table="Customers" schema="Sales">
                    <id name="CustomerID" column="CustomerID" type="int">
                        <generator class="identity" />
                    </id>
                    <one-to-one name="Profile" class="CustomerProfile" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.OneToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.True(relation.IsUnique);
    }
}