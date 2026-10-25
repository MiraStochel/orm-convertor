using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NHibernate.Dialect;
using Tests.Verification;

namespace Tests.Experiment;

/// <summary>
/// The fact the measure of mapping completeness counts the NOT NULL of a key column by
/// (decision 188): a column the mapping declares the key, or a part of it, and states no
/// nullability of, is a NOT NULL column to the framework - to EF Core 10 with the SQL Server
/// provider and to NHibernate 5.7.0 over <c>MsSql2012Dialect</c>, read off the model each
/// builds without a connection and off the create script it writes. Every probe states the
/// key over a type that is nullable by itself and keeps a column of the same type outside the
/// key as a control, so that it is the key and not the language type that makes the column
/// NOT NULL. The Java half - Hibernate and EclipseLink - is the Java suite's
/// <c>KeyColumnNullabilityClaimsTest</c> of each implementation.
/// </summary>
public class KeyColumnNullabilityProviderTest
{
    // ---- EF Core ----------------------------------------------------------------------------

    public static TheoryData<Type, string> EFCoreKeyColumns => new()
    {
        { typeof(NullableIntKeyed), nameof(NullableIntKeyed.Id) },
        { typeof(TextKeyed), nameof(TextKeyed.Code) },
        { typeof(PairKeyed), nameof(PairKeyed.A) },
        { typeof(PairKeyed), nameof(PairKeyed.B) },
    };

    [Theory]
    [MemberData(nameof(EFCoreKeyColumns))]
    public void EFCoreMakesAKeyColumnNotNull(Type entity, string property)
    {
        var (nullable, column) = ReadEFCore(entity, property);

        Assert.False(nullable, $"{entity.Name}.{property} is optional in the model: {column}");
        Assert.Contains(" NOT NULL", column, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(NullableIntKeyed))]
    [InlineData(typeof(TextKeyed))]
    [InlineData(typeof(PairKeyed))]
    public void EFCoreLeavesTheSameTypeOutsideTheKeyNullable(Type entity)
    {
        var (nullable, column) = ReadEFCore(entity, "Note");

        Assert.True(nullable, $"{entity.Name}.Note is required in the model: {column}");
        Assert.EndsWith(" NULL", column, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT NULL", column, StringComparison.Ordinal);
    }

    private static (bool Nullable, string Column) ReadEFCore(Type entity, string property)
    {
        using var context = new ProbeContext(entity);

        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(entity)!;
        var mapped = entityType.FindProperty(property)!;
        var script = context.Database.GenerateCreateScript();

        return (mapped.IsNullable, ColumnLine(script, $"CREATE TABLE [{entityType.GetTableName()}] (", $"[{mapped.GetColumnName()}] "));
    }

    /// <summary>A context over one probe entity, never connected; the model is cached per entity.</summary>
    private sealed class ProbeContext(Type entity) : DbContext
    {
        public Type Entity { get; } = entity;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options
                .UseSqlServer("Server=(local);Database=Probe;Trusted_Connection=True;TrustServerCertificate=True")
                .ReplaceService<IModelCacheKeyFactory, EntityModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity(Entity);
    }

    private sealed class EntityModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((ProbeContext)context).Entity, designTime);
    }

#nullable disable
    // Oblivious to nullable reference types, as a project without them is: a string is
    // optional by the convention of EF Core unless something else makes it required.

    [Table(nameof(NullableIntKeyed))]
    public class NullableIntKeyed
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int? Id { get; set; }

        public int? Note { get; set; }
    }

    [Table(nameof(TextKeyed))]
    public class TextKeyed
    {
        [Key]
        public string Code { get; set; }

        public string Note { get; set; }
    }

    [Table(nameof(PairKeyed))]
    [PrimaryKey(nameof(A), nameof(B))]
    public class PairKeyed
    {
        public string A { get; set; }

        public int? B { get; set; }

        public string Note { get; set; }
    }
#nullable restore

    // ---- NHibernate -------------------------------------------------------------------------

    private const string NHibernateEntities = """
        using System;

        namespace Probe;

        public class Widget
        {
            public virtual int WidgetId { get; set; }
            public virtual int? Note { get; set; }
        }

        public class Label
        {
            public virtual string Code { get; set; }
            public virtual string Note { get; set; }
        }

        [Serializable]
        public class Pair
        {
            public virtual string A { get; set; }
            public virtual int B { get; set; }
            public virtual string Note { get; set; }

            public override bool Equals(object obj) => obj is Pair other && other.A == A && other.B == B;
            public override int GetHashCode() => HashCode.Combine(A, B);
        }
        """;

    // The shapes the NHibernate builder writes: <id> with an assigned generator, <composite-id>
    // of <key-property> elements without a generator, and not one not-null attribute.
    private const string NHibernateMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Probe">
          <class name="Widget" table="Widgets">
            <id name="WidgetId" column="WidgetId" type="Int32">
              <generator class="assigned" />
            </id>
            <property name="Note" column="Note" type="Int32" />
          </class>
          <class name="Label" table="Labels">
            <id name="Code" column="Code" type="String">
              <generator class="assigned" />
            </id>
            <property name="Note" column="Note" type="String" />
          </class>
          <class name="Pair" table="Pairs">
            <composite-id>
              <key-property name="A" column="A" type="String" />
              <key-property name="B" column="B" type="Int32" />
            </composite-id>
            <property name="Note" column="Note" type="String" />
          </class>
        </hibernate-mapping>
        """;

    public static TheoryData<string, string, string> NHibernateKeyColumns => new()
    {
        { "Widget", "Widgets", "WidgetId" },
        { "Label", "Labels", "Code" },
        { "Pair", "Pairs", "A" },
        { "Pair", "Pairs", "B" },
    };

    [Theory]
    [MemberData(nameof(NHibernateKeyColumns))]
    public void NHibernateMakesAKeyColumnNotNull(string entity, string table, string column)
    {
        var (nullable, line) = ReadNHibernate(entity, table, column);

        Assert.False(nullable, $"Probe.{entity}.{column} is nullable in the model: {line}");
        Assert.Contains(" not null", line, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Widget", "Widgets")]
    [InlineData("Label", "Labels")]
    [InlineData("Pair", "Pairs")]
    public void NHibernateLeavesTheSameTypeOutsideTheKeyNullable(string entity, string table)
    {
        var (nullable, line) = ReadNHibernate(entity, table, "Note");

        Assert.True(nullable, $"Probe.{entity}.Note is not nullable in the model: {line}");
        Assert.DoesNotContain("not null", line, StringComparison.OrdinalIgnoreCase);
    }

    private static (bool Nullable, string Column) ReadNHibernate(string entity, string table, string column)
    {
        var compiled = GeneratedEntityCompiler.CompileOrFail(
            "KeyColumnNullability",
            [NHibernateEntities],
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        var nullable = true;
        var script = string.Empty;

        NHibernateAcceptance.UseConfiguration(compiled, [NHibernateMapping], configuration =>
        {
            var mapped = configuration.GetClassMapping($"Probe.{entity}").Table.ColumnIterator.Single(c => c.Name == column);
            nullable = mapped.IsNullable;
            script = string.Join(Environment.NewLine, configuration.GenerateSchemaCreationScript(new MsSql2012Dialect()));
        });

        return (nullable, ColumnLine(script, $"create table {table} (", column + " "));
    }

    // ---- the script -------------------------------------------------------------------------

    /// <summary>The definition of one column in the create statement of one table, without the separator after it.</summary>
    private static string ColumnLine(string script, string tableStart, string columnStart)
    {
        var start = script.IndexOf(tableStart, StringComparison.OrdinalIgnoreCase);
        Assert.True(start >= 0, $"the script has no '{tableStart}':{Environment.NewLine}{script}");

        var body = script[(start + tableStart.Length)..];
        var definition = body
            .Split([',', '\n'], StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith(columnStart, StringComparison.Ordinal));

        return definition ?? throw new InvalidOperationException($"the table has no column '{columnStart.Trim()}':{Environment.NewLine}{script}");
    }
}
