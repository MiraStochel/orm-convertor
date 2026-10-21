using System.Reflection;
using NHibernate;
using NHibernate.Linq;
using NHibernate.SqlCommand;
using Tests.Verification;

namespace Tests.NHibernate;

/// <summary>
/// What the refusal of a bulk command over a chain rests on (<c>NHibernateLinqQueryParser.ProviderCommandOf</c>),
/// measured against NHibernate 5.7.0 without a connection - the statement taken where it is
/// prepared: the methods of <c>DmlExtensionMethods</c> are the provider's bulk commands, each
/// answering the number of rows it wrote or the builder of one, and each sends a statement that
/// writes - a DELETE, an UPDATE, an INSERT … SELECT of the rows the chain selects. Should a new
/// version add one, this fails here.
/// </summary>
public class NHibernateBulkCommandProviderTest
{
    private const string Entities = """
        namespace Probe;

        public class Order
        {
            public virtual int OrderId { get; set; }
            public virtual int Version { get; set; }
            public virtual decimal Total { get; set; }
        }

        public class Archived
        {
            public virtual int ArchivedId { get; set; }
            public virtual decimal Total { get; set; }
        }
        """;

    private const string Mapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Probe">
          <class name="Order" table="Orders" schema="Sales">
            <id name="OrderId" column="OrderId" type="Int32"><generator class="identity" /></id>
            <version name="Version" column="Version" type="Int32" />
            <property name="Total" type="Decimal" />
          </class>
          <class name="Archived" table="Archive" schema="Sales">
            <id name="ArchivedId" column="ArchivedId" type="Int32"><generator class="assigned" /></id>
            <property name="Total" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

    /// <summary>The methods of NHibernate's class of bulk commands, by name.</summary>
    public static IReadOnlyList<string> Commands()
        => [.. typeof(DmlExtensionMethods).GetMethods(BindingFlags.Public | BindingFlags.Static).Select(method => method.Name).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void TheCommandsAreTheMethodsOfTheDmlClass()
    {
        Assert.Equal(
            [
                "Delete", "DeleteAsync", "InsertBuilder", "InsertInto", "InsertIntoAsync",
                "Update", "UpdateAsync", "UpdateBuilder", "UpdateVersioned", "UpdateVersionedAsync",
            ],
            Commands());

        Assert.All(
            typeof(DmlExtensionMethods).GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => Assert.True(
                method.ReturnType == typeof(int)
                || method.ReturnType == typeof(Task<int>)
                || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() is var builder
                    && (builder == typeof(UpdateBuilder<>) || builder == typeof(InsertBuilder<>))),
                $"{method.Name} answers {method.ReturnType}"));
    }

    [Theory]
    [InlineData("session.Query<Order>().Where(o => o.Total < 1m).Delete()", "delete from Sales.Orders where Total<?")]
    [InlineData("session.Query<Order>().Where(o => o.Total < 1m).Update(o => new Order { Total = 0m })", "update Sales.Orders set Total=? where Total<?")]
    [InlineData("session.Query<Order>().Where(o => o.Total < 1m).UpdateVersioned(o => new Order { Total = 0m })", "update Sales.Orders set Version=Version+1, Total=?")]
    [InlineData("session.Query<Order>().Where(o => o.Total < 1m).UpdateBuilder().Set(o => o.Total, 0m).Update()", "update Sales.Orders set Total=? where Total<?")]
    [InlineData("session.Query<Order>().Where(o => o.Total < 1m).InsertInto(o => new Archived { ArchivedId = o.OrderId, Total = o.Total })", "insert into Sales.Archive ( ArchivedId, Total ) select")]
    [InlineData("session.Query<Order>().Where(o => o.Total < 1m).InsertBuilder().Into<Archived>().Value(a => a.ArchivedId, o => o.OrderId).Insert()", "insert into Sales.Archive ( ArchivedId ) select")]
    public void TheCommandSendsAStatementThatWrites(string chain, string statement)
    {
        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "NHibernateBulkCommand_" + string.Concat(chain.Where(char.IsLetterOrDigit)),
            $$"""
            public static int Run(ISession session) => {{chain}};
            """,
            [Entities],
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            "using NHibernate;" + Environment.NewLine + "using NHibernate.Linq;" + Environment.NewLine + "using Probe;");

        var (factory, assembly) = NHibernateAcceptance.OpenSessionFactory(compiled, [Mapping]);
        NHibernateTypeRegistry.Forget(assembly);

        using (factory)
        using (var session = factory.WithOptions().Interceptor(new Taken()).OpenSession())
        {
            var run = assembly.GetType("GeneratedQueries")!.GetMethod("Run")!;
            var thrown = Assert.Throws<TargetInvocationException>(() => run.Invoke(null, [session]));

            Assert.StartsWith(statement, Assert.IsType<Sent>(Innermost(thrown)).Message, StringComparison.Ordinal);
        }
    }

    private static Exception Innermost(Exception exception)
    {
        while (exception is not Sent && exception.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception;
    }

    /// <summary>The text of the statement NHibernate was about to send.</summary>
    private sealed class Sent(string sql) : Exception(sql);

    private sealed class Taken : EmptyInterceptor
    {
        public override SqlString OnPrepareStatement(SqlString sql) => throw new Sent(sql.ToString());
    }
}
