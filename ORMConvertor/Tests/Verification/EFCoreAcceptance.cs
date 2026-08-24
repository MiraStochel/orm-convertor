using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests.Verification;

/// <summary>
/// Third verification level of decision 016: EF Core itself accepts the generated
/// entities. Building the model of a DbContext runs EF Core's model validation - including
/// the relational and SQL Server rules - before any connection is attempted, so an entity
/// EF Core cannot map fails here without a database. The fourth level uses the same
/// context type over <see cref="OpenContext"/>, with a real connection supplied by the
/// test.
/// </summary>
internal static class EFCoreAcceptance
{
    /// <summary>
    /// Registers every public class of the compiled generated assembly with a DbContext and
    /// returns the finalized model. Throws whatever EF Core throws when it refuses one.
    /// </summary>
    public static IModel BuildModel(byte[] compiledEntities)
    {
        using var context = new VerificationContext(
            Options(builder => builder.UseSqlServer()),
            EntityTypes(Assembly.Load(compiledEntities)));
        return context.Model;
    }

    /// <summary>
    /// The same context over a live connection, for the fourth verification level. The
    /// assembly comes in already loaded rather than as bytes, because the caller needs the
    /// very <see cref="Type"/>s the context registers - a second load of the same image
    /// would be a different assembly, and its types would not be part of this model.
    /// The caller owns both the connection and the returned context.
    /// </summary>
    public static DbContext OpenContext(Assembly compiledEntities, DbConnection connection)
        => new VerificationContext(
            Options(builder => builder.UseSqlServer(connection)),
            EntityTypes(compiledEntities));

    private static DbContextOptions<VerificationContext> Options(
        Action<DbContextOptionsBuilder<VerificationContext>> useSqlServer)
    {
        // EF Core caches the model per context type, and every verification shares this one
        // context type. With the shared cache, whichever test built a model first would hand
        // it to every later call - OnModelCreating and validation would never run again, and
        // a broken artifact would be "accepted". A fresh internal service provider per call
        // keeps every build, and therefore every verdict, real.
        var builder = new DbContextOptionsBuilder<VerificationContext>()
            .EnableServiceProviderCaching(false);
        useSqlServer(builder);
        return builder.Options;
    }

    private static List<Type> EntityTypes(Assembly compiledEntities)
        => compiledEntities
            .GetTypes()
            .Where(type => type.IsClass && type.IsPublic && !type.IsAbstract)
            .ToList();

    private sealed class VerificationContext(
        DbContextOptions<VerificationContext> options,
        IReadOnlyList<Type> entityTypes) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entityType in entityTypes)
            {
                modelBuilder.Entity(entityType);
            }
        }
    }
}
