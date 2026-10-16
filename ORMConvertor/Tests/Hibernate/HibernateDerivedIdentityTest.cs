using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using HibernateWrappers;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;
using SampleData;

namespace Tests.Hibernate;

/// <summary>
/// The derived identity of Jakarta Persistence - @Id on a @ManyToOne inside an @IdClass - is
/// read as the NHibernate parser reads &lt;key-many-to-one&gt; (decision 006): the stated join
/// column becomes a scalar key part named after the column, the relation stands beside it
/// over the same column, and the reading is reported. Before this reading the part through the
/// relation was dropped and the key came out narrower than the source described (finding V3 of
/// the open-source excerpts, Keycloak).
/// </summary>
public class HibernateDerivedIdentityTest
{
    private const string User = """
        import jakarta.persistence.*;

        @Entity
        @Table(name = "USER_ENTITY")
        public class UserEntity {
            @Id
            @Column(name = "ID")
            private String id;

            @Column(name = "USERNAME")
            private String username;
        }
        """;

    private static string Mapping(string userAnnotations) => $$"""
        import jakarta.persistence.*;
        import java.io.Serializable;

        @Entity
        @Table(name = "USER_ROLE_MAPPING")
        @IdClass(UserRoleMappingEntity.Key.class)
        public class UserRoleMappingEntity {
            {{userAnnotations}}
            protected UserEntity user;

            @Id
            @Column(name = "ROLE_ID")
            protected String roleId;

            public static class Key implements Serializable {
                protected UserEntity user;
                protected String roleId;
            }
        }
        """;

    private static HibernateEntityBuilder Read(params string[] units)
    {
        var builder = new HibernateEntityBuilder();
        var parser = new HibernateEntityParser(builder, new JpaReadingContext());

        foreach (var unit in units)
        {
            parser.Parse(unit);
        }

        return builder;
    }

    private static EntityMap Mapping(HibernateEntityBuilder builder)
        => builder.EntityMaps.Single(em => em.Entity.Name == "UserRoleMappingEntity");

    /// <summary>Whichever of the two annotations the source writes first.</summary>
    [Theory]
    [InlineData("@Id @ManyToOne(fetch = FetchType.LAZY) @JoinColumn(name = \"USER_ID\")")]
    [InlineData("@ManyToOne(fetch = FetchType.LAZY) @JoinColumn(name = \"USER_ID\") @Id")]
    public void IdOnAManyToOneIsReadAsItsJoinColumnPlusTheRelation(string userAnnotations)
    {
        var builder = Read(Mapping(userAnnotations), User);
        var artifacts = builder.Build();
        var map = Mapping(builder);

        Assert.Equal(["USER_ID", "roleId"], map.PrimaryKey!.Parts.OrderBy(p => p.Order).Select(p => p.PropertyMap.Property.Name));
        Assert.Equal(KeyClassForm.Mirrored, map.PrimaryKey.SourceKeyClass!.Form);

        var relation = Assert.Single(map.Relations);
        Assert.Equal("user", relation.SourceNavigationProperty);
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Equal("UserEntity", relation.TargetEntity);

        // The column part is the one the relation pairs with the referenced key, and it is
        // typed from that key (decision 010), so the completeness gate lets the entity through.
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("USER_ID", pair.Source.Property.Name);
        Assert.Equal(ScalarType.String, pair.Source.Property.Type?.ScalarType);
        Assert.Equal(PrimaryKeyStrategy.Assigned, map.PrimaryKey.Parts.Single(p => p.PropertyMap.Property.Name == "USER_ID").Strategy);
        Assert.False(map.PropertyMaps.Single(pm => pm.Property.Name == "user").IsNullable);

        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Conflict);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "user" && r.Category == MappingFactCategory.PrimaryKey
            && r.Reason.Contains("derived identity", StringComparison.Ordinal));

        // The nested key class dissolved into the key: its member 'user' is the relation, not a
        // dropped member, and the class is not an entity of the conversion (decision 031).
        Assert.Equal(2, builder.EntityMaps.Count);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "user" && r.Reason.Contains("key class", StringComparison.Ordinal));

        var java = artifacts.Single(a => a.ContentType == ConversionContentType.JavaEntity
            && a.Content.Contains("class UserRoleMappingEntity", StringComparison.Ordinal)).Content;
        Assert.Contains("@Column(name = \"USER_ID\")", java, StringComparison.Ordinal);
        Assert.Contains("@JoinColumn(name = \"USER_ID\", referencedColumnName = \"ID\", nullable = false, insertable = false, updatable = false)", java, StringComparison.Ordinal);
        Assert.Contains("@IdClass(UserRoleMappingEntity.Key.class)", java, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDescriptorSpellsTheSameWithIdTrue()
    {
        var builder = new HibernateEntityBuilder();
        var context = new JpaReadingContext();

        new JpaOrmXmlParser(builder, context).Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <entity class="UserRoleMappingEntity">
                    <table name="USER_ROLE_MAPPING"/>
                    <id-class class="UserRoleMappingEntity.Key"/>
                    <attributes>
                        <id name="roleId">
                            <column name="ROLE_ID"/>
                        </id>
                        <many-to-one name="user" target-entity="UserEntity" id="true">
                            <join-column name="USER_ID"/>
                        </many-to-one>
                    </attributes>
                </entity>
            </entity-mappings>
            """);

        var parser = new HibernateEntityParser(builder, context);
        parser.Parse("""
            import java.io.Serializable;

            public class UserRoleMappingEntity {
                protected String roleId;
                protected UserEntity user;

                public static class Key implements Serializable {
                    protected UserEntity user;
                    protected String roleId;
                }
            }
            """);
        parser.Parse(User);
        builder.Build();

        var map = Mapping(builder);
        Assert.Equal(["roleId", "USER_ID"], map.PrimaryKey!.Parts.OrderBy(p => p.Order).Select(p => p.PropertyMap.Property.Name));
        Assert.Equal("USER_ID", Assert.Single(Assert.Single(map.Relations).ColumnPairs).Source.Property.Name);
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Conflict);
    }

    /// <summary>
    /// Without a stated join column there is no column to name the part by - the default of
    /// @JoinColumn is derived only once the referenced key is known - so the key comes out
    /// shorter and the reading says so instead of keeping quiet (decision 010).
    /// </summary>
    [Fact]
    public void WithoutAJoinColumnTheShorterKeyIsReported()
    {
        var builder = Read(Mapping("@Id @ManyToOne"), User);

        var map = Mapping(builder);
        Assert.Equal(["roleId"], map.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.Single(map.Relations);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Property == "user" && r.Category == MappingFactCategory.PrimaryKey);
    }

    /// <summary>
    /// The shape as the project writes it (SampleData/ThirdParty/keycloak; the entity's mapping
    /// is the project's text, its named queries carry the excerpt's notes), through the
    /// orchestration into NHibernate: both parts of the key arrive. The named queries of the two
    /// classes are read as well since decision 143 and some of them reach entities this input
    /// does not carry, so the assertion about records is about the mapping records alone.
    /// </summary>
    [Fact]
    public void TheKeycloakMappingKeepsBothKeyParts()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.NHibernate,
        [
            new() { Content = ThirdPartySources.Read("keycloak/entities/UserRoleMappingEntity.java"), ContentType = ConversionContentType.Java },
            new() { Content = ThirdPartySources.Read("keycloak/entities/UserEntity.java"), ContentType = ConversionContentType.Java },
        ]);

        var xml = result.Sources.Where(s => s.ContentType == ConversionContentType.XML)
            .Single(s => s.Content.Contains("table=\"USER_ROLE_MAPPING\"", StringComparison.Ordinal)).Content;

        Assert.Contains("<key-property name=\"USER_ID\"", xml, StringComparison.Ordinal);
        Assert.Contains("<key-property name=\"roleId\"", xml, StringComparison.Ordinal);
        Assert.Contains("<many-to-one name=\"user\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Query is null && r.Kind is (ConversionRecordKind.Failure or ConversionRecordKind.Conflict));
    }
}
