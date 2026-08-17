namespace Tests.Verification;

/// <summary>
/// A verification level that never says no proves nothing, so each one is shown here
/// refusing a broken artifact. The refused artifacts are exactly the class of errors
/// decision 006 named - syntactically fine text that no framework would run - plus a plain
/// compile error for the compilation step.
/// </summary>
public class VerificationHarnessTest
{
    [Fact]
    public void CompilationRefusesAnUnknownType()
    {
        var result = GeneratedEntityCompiler.Compile(
            "VerificationHarness.UnknownType",
            ["""
            public class Order
            {
                public virtual NoSuchType Value { get; set; }
            }
            """],
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void SchemaValidationRefusesAnUnknownElement()
    {
        var errors = NHibernateMappingSchema.Validate("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <klass name="Order" table="Orders" />
            </hibernate-mapping>
            """);

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void NHibernateRefusesAMappingToAMissingProperty()
    {
        // The mapping is schema-valid and the class compiles; only binding one to the other
        // reveals that the property does not exist. This is the error no shape assertion or
        // schema validation can catch (decisions 006 and 016).
        var entities = GeneratedEntityCompiler.CompileOrFail(
            "VerificationHarness.MissingProperty",
            ["""
            public class Order
            {
                public virtual int OrderID { get; set; }
            }
            """],
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        Assert.ThrowsAny<global::NHibernate.MappingException>(() =>
            NHibernateAcceptance.BuildSessionFactory(entities, ["""
                <?xml version="1.0" encoding="utf-8" ?>
                <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                    <class name="Order" table="Orders">
                        <id name="OrderID" column="OrderID" type="Int32">
                            <generator class="native" />
                        </id>
                        <property name="Nonexistent" />
                    </class>
                </hibernate-mapping>
                """]));
    }

    [Fact]
    public void NHibernateRefusesACompositeIdClassWithoutIdentityMembers()
    {
        // The very failure the enforced members of decision 009 exist to prevent: a class
        // mapped with <composite-id> but overriding neither Equals nor GetHashCode.
        var entities = GeneratedEntityCompiler.CompileOrFail(
            "VerificationHarness.NoIdentityMembers",
            ["""
            public class OrderLine
            {
                public virtual int OrderID { get; set; }

                public virtual int OrderLineID { get; set; }
            }
            """],
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        Assert.ThrowsAny<global::NHibernate.MappingException>(() =>
            NHibernateAcceptance.BuildSessionFactory(entities, ["""
                <?xml version="1.0" encoding="utf-8" ?>
                <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                    <class name="OrderLine" table="OrderLines">
                        <composite-id>
                            <key-property name="OrderID" column="OrderID" type="Int32" />
                            <key-property name="OrderLineID" column="OrderLineID" type="Int32" />
                        </composite-id>
                    </class>
                </hibernate-mapping>
                """]));
    }

    [Fact]
    public void EFCoreRefusesAnEntityWithoutAKeyOrKeylessMarker()
    {
        var entities = GeneratedEntityCompiler.CompileOrFail(
            "VerificationHarness.NoKey",
            ["""
            public class Order
            {
                public string? Comments { get; set; }
            }
            """],
            GeneratedEntityCompiler.EFCoreConsumerReferences);

        Assert.Throws<InvalidOperationException>(() => EFCoreAcceptance.BuildModel(entities));
    }
}
