using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A root whose entity is a type parameter - <c>ctx.Set&lt;T&gt;()</c> or <c>session.Query&lt;T&gt;()</c>
/// in a generic repository, a name declared <c>DbSet&lt;T&gt;</c> - reads the table of whatever type
/// the caller supplies, which the text does not state (<c>EFCore/EFCoreDbSetParameterProviderTest</c>):
/// refused naming the parameter and what declares it, at the root of the query or inside it. Every
/// one of them used to come out as a query over a table T. A call that only tracks an entity is no
/// query, so a generic repository refuses its queries alone; a root that names an entity in a
/// generic class is read.
/// </summary>
public class TypeParameterRootTest
{
    private const string File = "Repository.cs";

    private const string Model = """
        public class Order
        {
            public int OrderID { get; set; }

            public decimal Amount { get; set; }
        }

        public class ShopContext : DbContext
        {
            public DbSet<Order> Orders { get; set; } = null!;
        }


        """;

    private static ConversionResult Convert(string content, ORMEnum source = ORMEnum.EFCore)
        => ConversionHandler.Convert(source, ORMEnum.Dapper, [new ConversionSource { ContentType = ConversionContentType.CSharp, Content = Model + content, Name = File }]);

    private static string Generic(string code)
        => "public class Repository<T>(ShopContext ctx) where T : class\n{\n    " + code + "\n}\n";

    private static List<string> Queries(ConversionResult result)
        => [.. result.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery).Select(s => s.Content)];

    private static List<ConversionRecord> Failures(ConversionResult result)
        => [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Failure)];

    private const string SetOfT = "'ctx.Set<T>()', whose entity is 'T', a type parameter of 'Repository<T>'";

    [Theory]
    [InlineData("public List<T> All() => ctx.Set<T>().ToList();", SetOfT)]
    [InlineData("public IQueryable<T> Query() => ctx.Set<T>();", SetOfT)]
    [InlineData("public T? ById(int id) => ctx.Set<T>().Find(id);", SetOfT)]
    [InlineData("public List<T> Page(int n) => ctx.Set<T>().AsNoTracking().Skip(n).Take(10).ToList();", SetOfT)]
    [InlineData("public List<Order> WithAny() => ctx.Orders.Where(o => ctx.Set<T>().Any()).ToList();", SetOfT)]
    [InlineData("public List<TOther> All<TOther>() where TOther : class => ctx.Set<TOther>().ToList();", "'ctx.Set<TOther>()', whose entity is 'TOther', a type parameter of 'All<TOther>'")]
    public void ASetOverATypeParameterIsRefusedNamingIt(string code, string named)
    {
        var result = Convert(Generic(code));

        Assert.Empty(Queries(result));
        Assert.Contains(named, Assert.Single(Failures(result)).Reason, StringComparison.Ordinal);
        Assert.Contains(result.Records, r => r.Reason.Contains("'Repository'", StringComparison.Ordinal) && r.Reason.Contains("not as an entity", StringComparison.Ordinal));
    }

    /// <summary>A name declared DbSet&lt;T&gt; - a parameter, a field whatever its value, the own set of a class that declares one - names its entity by the parameter too.</summary>
    [Theory]
    [InlineData("public static class Repository\n{\n    public static List<T> All<T>(DbSet<T> set) where T : class => set.ToList();\n}\n", "'set', whose entity is 'T', a type parameter of 'All<T>'")]
    [InlineData("public class Repository<T> where T : class\n{\n    private readonly DbSet<T> _set;\n\n    public Repository(ShopContext ctx) { _set = ctx.Set<T>(); }\n\n    public List<T> All() => _set.ToList();\n}\n", SetOfT)]
    [InlineData("public class Repository<T>(DbSet<T> injected) where T : class\n{\n    private readonly DbSet<T> _set = injected;\n\n    public List<T> All() => this._set.ToList();\n}\n", "'this._set', whose entity is 'T', a type parameter of 'Repository<T>'")]
    [InlineData("public class Repository<T>(DbSet<T> injected) where T : class\n{\n    private readonly DbSet<T> _set = injected;\n\n    public List<T> All() => _set.ToList();\n}\n", "'injected', whose entity is 'T', a type parameter of 'Repository<T>'")]
    [InlineData("public class Repository<T> where T : class\n{\n    private readonly DbSet<T> _set = null!;\n\n    public List<T> All() => _set.Where(e => e != null).ToList();\n}\n", "'_set', whose entity is 'T', a type parameter of 'Repository<T>'")]
    [InlineData("public class Repository<T>(ShopContext ctx) where T : class\n{\n    public DbSet<T> Items => ctx.Set<T>();\n\n    public List<T> All() => Items.ToList();\n}\n", "'Items', whose entity is 'T', a type parameter of 'Repository<T>'")]
    public void ANameDeclaredAsASetOfATypeParameterIsRefusedNamingIt(string code, string named)
    {
        var result = Convert(code);

        // A class with a DbSet<T> property is a context, and its configuration names an entity T
        // with no class, refused on the entity side; the query's own refusal is the unit's.
        Assert.Empty(Queries(result));
        Assert.Contains(named, Assert.Single(Failures(result), f => f.Unit == File).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The usual generic repository: the calls that track an entity hand nothing over, the
    /// queries are refused one by one, and the class is the code around them.
    /// </summary>
    [Fact]
    public void AGenericRepositoryRefusesItsQueriesAndNotItsTracking()
    {
        var result = Convert("""
            public class Repository<TEntity> where TEntity : class
            {
                protected readonly DbSet<TEntity> _dbSet;

                public Repository(ShopContext ctx) { _dbSet = ctx.Set<TEntity>(); }

                public void Add(TEntity entity) => _dbSet.Add(entity);

                public Task AddMany(IEnumerable<TEntity> entities) => _dbSet.AddRangeAsync(entities);

                public void Remove(TEntity entity) => _dbSet.Remove(entity);

                public void Update(TEntity entity) => _dbSet.Update(entity);

                public void Detach(TEntity entity) => _dbSet.Entry(entity).State = EntityState.Detached;

                public TEntity? ById(int id) => _dbSet.Find(id);

                public List<TEntity> All() => _dbSet.ToList();
            }
            """);

        Assert.Empty(Queries(result));
        Assert.Equal(2, Failures(result).Count);
        Assert.All(Failures(result), f => Assert.Contains("'TEntity', a type parameter of 'Repository<TEntity>'", f.Reason, StringComparison.Ordinal));
        Assert.Equal(["Query01", "Query02"], Failures(result).Select(f => f.Query));
        Assert.Contains(result.Records, r => r.Reason.Contains("'Repository'", StringComparison.Ordinal) && r.Reason.Contains("not as an entity", StringComparison.Ordinal));
    }

    /// <summary>In a generic class a root that names an entity is read, and only the root over the parameter is refused.</summary>
    [Fact]
    public void ARootThatNamesAnEntityInAGenericClassIsRead()
    {
        var result = Convert(Generic("public List<Order> Big() => ctx.Set<Order>().Where(o => o.Amount > 100).ToList();\n\n    public List<T> All() => ctx.Set<T>().ToList();"));

        Assert.Contains("FROM Orders AS o", Assert.Single(Queries(result)), StringComparison.Ordinal);
        Assert.Equal("Query02", Assert.Single(Failures(result)).Query);
    }

    /// <summary>The reading is the shared one of LINQ, so NHibernate's root over a type parameter is refused the same way.</summary>
    [Fact]
    public void NHibernateRefusesQueryOverATypeParameter()
    {
        var result = Convert(
            "public class Repository<T>(ISession session) where T : class\n{\n    public IList<T> All() => session.Query<T>().ToList();\n\n    public IList<Order> Big() => session.Query<Order>().Where(o => o.Amount > 1).ToList();\n}\n",
            ORMEnum.NHibernate);

        Assert.Contains("WHERE o.Amount > 1", Assert.Single(Queries(result)), StringComparison.Ordinal);
        Assert.Contains("'session.Query<T>()', whose entity is 'T', a type parameter of 'Repository<T>'", Assert.Single(Failures(result)).Reason, StringComparison.Ordinal);
    }
}
