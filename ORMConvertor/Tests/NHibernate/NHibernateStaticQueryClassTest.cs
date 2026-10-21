using NHibernate.Linq;

namespace Tests.NHibernate;

/// <summary>
/// The static classes NHibernate 5.7.0 keeps its query steps and its bulk commands in, which the
/// reading refuses a query handed to in their static form (<c>NHibernateLinqQueryParser.IsProviderStaticQueryClass</c>):
/// <c>LinqExtensionMethods.CountAsync(q)</c> is the count of q, as <c>q.CountAsync()</c> is, and
/// a query read without it would answer rows; <c>DmlExtensionMethods.Delete(q)</c> deletes them.
/// Should a new version move them, this fails here.
/// </summary>
public class NHibernateStaticQueryClassTest
{
    [Theory]
    [InlineData(typeof(LinqExtensionMethods), "ToListAsync")]
    [InlineData(typeof(LinqExtensionMethods), "CountAsync")]
    [InlineData(typeof(LinqExtensionMethods), "WithLock")]
    [InlineData(typeof(EagerFetchingExtensionMethods), "Fetch")]
    [InlineData(typeof(DmlExtensionMethods), "Delete")]
    public void TheStepIsAStaticMethodOfTheClass(Type owner, string step)
        => Assert.Contains(owner.GetMethods(), method => method.IsStatic && method.Name == step);
}
