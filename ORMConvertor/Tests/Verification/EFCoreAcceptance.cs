using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests.Verification;

/// <summary>
/// Third verification level of decision 016: EF Core itself accepts the generated
/// entities. Building the model of a DbContext runs EF Core's model validation - including
/// the relational and SQL Server rules - before any connection is attempted, so an entity
/// EF Core cannot map fails here without a database.
/// </summary>
internal static class EFCoreAcceptance
{
    /// <summary>
    /// Registers every public class of the compiled generated assembly with a DbContext and
    /// returns the finalized model. Throws whatever EF Core throws when it refuses one.
    /// </summary>
    public static IModel BuildModel(byte[] compiledEntities)
    {
        var entityTypes = Assembly.Load(compiledEntities)
            .GetTypes()
            .Where(type => type.IsClass && type.IsPublic && !type.IsAbstract)
            .ToList();

        var options = new DbContextOptionsBuilder<VerificationContext>()
            .UseSqlServer()
            .Options;

        using var context = new VerificationContext(options, entityTypes);
        return context.Model;
    }

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
