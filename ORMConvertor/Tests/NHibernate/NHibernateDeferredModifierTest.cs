using AbstractWrappers.Descriptors;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.NHibernate;

/// <summary>
/// The language axis of decision 076 on the reading side: a modifier the source
/// framework enforces is its requirement, not a fact of the domain, and does not enter
/// the model. The binding to the declaration follows decision 037: what the descriptor
/// of the source declares as an enforced member is absent from the representation after
/// reading, and only the framework's own builder puts it back.
/// </summary>
public class NHibernateDeferredModifierTest
{
    private const string Entity = """
        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
        }
        """;

    private const string Mapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Customer" table="Customers">
                <id name="CustomerId" column="CustomerId" type="Int32">
                    <generator class="identity" />
                </id>
                <property name="CustomerName" />
            </class>
        </hibernate-mapping>
        """;

    /// <summary>
    /// The descriptor declares the member; the representation after reading lacks it. Both
    /// halves are asserted, so that the deferral cannot outlive the declaration in silence.
    /// </summary>
    [Fact]
    public void WhatTheSourceDescriptorEnforcesIsNotInTheModelAfterReading()
    {
        var enforced = NHibernateDescriptor.Instance.EnforcedMembers.Single(m => m.Marker == "virtual ");
        Assert.Equal(EnforcedMemberCondition.Always, enforced.Condition);

        var builder = new DummyEntityBuilder();
        new NHibernateEntityParser(builder).Parse(Entity);

        Assert.All(
            builder.EntityMaps.Single().Entity.Properties,
            p => Assert.DoesNotContain("virtual", p.OtherModifiers));
    }

    [Fact]
    public void AForeignTargetDoesNotCarryTheModifier()
    {
        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.EFCore,
        [
            new() { Content = Entity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = Mapping, ContentType = ConversionContentType.XML },
        ]);

        var code = result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;

        Assert.DoesNotContain("virtual", code);
        Assert.Contains("public required int CustomerId { get; set; }", code);
    }

    /// <summary>The framework's own builder adds its enforced member back, so the round trip is unchanged.</summary>
    [Fact]
    public void TheOwnTargetAddsTheModifierBack()
    {
        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.NHibernate,
        [
            new() { Content = Entity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = Mapping, ContentType = ConversionContentType.XML },
        ]);

        var code = result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;

        Assert.Contains("public virtual int CustomerId { get; set; }", code);
    }

    /// <summary>
    /// A language fact of the domain travels (decision 076): the same word from a source
    /// that does not enforce it - EF Core - stays a property of the class.
    /// </summary>
    [Fact]
    public void TheSameModifierFromAFrameworkThatDoesNotEnforceItTravels()
    {
        var builder = new DummyEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Customer
            {
                [Key]
                public virtual int CustomerId { get; set; }
            }
            """);

        Assert.Contains("virtual", builder.EntityMaps.Single().Entity.Properties.Single().OtherModifiers);
    }
}
