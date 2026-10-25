using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Tests.EFCore;

/// <summary>
/// What EF Core 10 with the SQL Server provider makes of a key's generation, measured on the
/// model it builds without a connection and on the create script it writes (decision 187).
/// The facts the EF Core parser reads a key's strategy by and the EF Core builder decides
/// its <c>[DatabaseGenerated]</c> by:
/// <list type="bullet">
/// <item><c>[DatabaseGenerated(Identity)]</c> states "generated on add" and no mechanism: on a
/// single integer key it changes nothing the convention does not already do, and the
/// mechanism behind it is the model's default - an IDENTITY column, a hi/lo sequence under
/// <c>UseHiLo()</c>, a sequence default under <c>UseKeySequences()</c>.</item>
/// <item>The convention generates a single key of an integer type other than <c>byte</c> as an
/// IDENTITY column and a single <c>Guid</c> key on the client; a composite key, a <c>byte</c>
/// key and a key that is also a foreign key it leaves to the application.</item>
/// <item>The attribute makes an IDENTITY column of any integer or decimal key it stands on -
/// a part of a composite key and a <c>byte</c> key too -, and on a <c>Guid</c> or a string
/// key it has the client generate a GUID, with nothing in the database.</item>
/// <item>A default value leaves the key without IDENTITY, and <c>Computed</c> on a key is a model EF Core refuses.</item>
/// </list>
/// </summary>
public class EFCoreValueGenerationProviderTest
{
    // ---- the convention over a single key ---------------------------------------------------

    public static TheoryData<string, Type, bool> ConventionByType => new()
    {
        { "int", typeof(IntKey), true },
        { "long", typeof(LongKey), true },
        { "short", typeof(ShortKey), true },
        { "byte", typeof(ByteKey), false },
        { "sbyte", typeof(SByteKey), true },
        { "ushort", typeof(UShortKey), true },
        { "uint", typeof(UIntKey), true },
        { "ulong", typeof(ULongKey), true },
    };

    /// <summary>
    /// The convention generates a single integer key as an IDENTITY column - every integer type
    /// but <c>byte</c>, which the core convention leaves out by name.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConventionByType))]
    public void TheConventionMakesASingleIntegerKeyAnIdentityColumnExceptAByte(string type, Type entity, bool identity)
    {
        var key = Read(entity, "Id");

        if (identity)
        {
            Assert.True(key.IsIdentity, $"{type}: {key}");
        }
        else
        {
            Assert.Equal(ValueGenerated.Never, key.Generated);
            Assert.False(key.IsIdentity, $"{type}: {key}");
        }
    }

    /// <summary>On such a key the attribute restates the convention: the same model and the same column.</summary>
    [Fact]
    public void TheAttributeChangesNothingOnAKeyTheConventionGenerates()
    {
        var bare = Read(typeof(IntKey), "Id");
        var attributed = Read(typeof(AttributedIntKey), "Id");

        Assert.Equal(bare.Generated, attributed.Generated);
        Assert.Equal(bare.Strategy, attributed.Strategy);
        Assert.Equal(bare.Column, attributed.Column);
        Assert.True(attributed.IsIdentity, attributed.ToString());
    }

    /// <summary>
    /// Where the convention generates nothing, the attribute is what makes the IDENTITY column:
    /// on a part of a composite key, on a byte key, on a decimal key.
    /// </summary>
    [Fact]
    public void TheAttributeMakesAnIdentityColumnWhereTheConventionGeneratesNothing()
    {
        Assert.True(Read(typeof(CompositeKey), nameof(CompositeKey.A)).IsIdentity);
        Assert.Equal(ValueGenerated.Never, Read(typeof(CompositeKey), nameof(CompositeKey.B)).Generated);
        Assert.True(Read(typeof(AttributedByteKey), "Id").IsIdentity);
        Assert.True(Read(typeof(AttributedDecimalKey), "Id").IsIdentity);
    }

    /// <summary>
    /// The attribute names no mechanism: the model's default decides it. Under UseHiLo() the
    /// key is fed from one shared hi/lo sequence stepping by 10, under UseKeySequences() from a
    /// sequence of the entity's own name that the column's default reads - neither of them an
    /// IDENTITY column, with the attribute or without it.
    /// </summary>
    [Fact]
    public void TheModelsDefaultDecidesTheMechanismBehindTheAttribute()
    {
        var hiLo = Read(typeof(HiLoKey), "Id", b => b.UseHiLo());
        Assert.Equal(SqlServerValueGenerationStrategy.SequenceHiLo, hiLo.Strategy);
        Assert.False(hiLo.IsIdentity, hiLo.ToString());
        Assert.Contains("CREATE SEQUENCE [EntityFrameworkHiLoSequence] START WITH 1 INCREMENT BY 10", hiLo.Script);

        var hiLoByConvention = Read(typeof(HiLoConventionKey), "Id", b => b.UseHiLo());
        Assert.Equal(SqlServerValueGenerationStrategy.SequenceHiLo, hiLoByConvention.Strategy);

        var sequence = Read(typeof(KeySequenceKey), "Id", b => b.UseKeySequences());
        Assert.Equal(SqlServerValueGenerationStrategy.Sequence, sequence.Strategy);
        Assert.False(sequence.IsIdentity, sequence.ToString());
        Assert.Contains("DEFAULT (NEXT VALUE FOR [KeySequenceKeySequence])", sequence.Column);
    }

    /// <summary>
    /// A call on the key property names the mechanism there: hi/lo and a sequence are no
    /// IDENTITY column, nor is a key whose value a default supplies; UseIdentityColumn() is.
    /// </summary>
    [Fact]
    public void ACallOnTheKeyPropertyNamesTheMechanism()
    {
        Assert.Equal(SqlServerValueGenerationStrategy.SequenceHiLo,
            Read(typeof(PropertyHiLoKey), "Id", b => b.Entity<PropertyHiLoKey>().Property(e => e.Id).UseHiLo()).Strategy);
        Assert.Equal(SqlServerValueGenerationStrategy.Sequence,
            Read(typeof(PropertySequenceKey), "Id", b => b.Entity<PropertySequenceKey>().Property(e => e.Id).UseSequence()).Strategy);

        var defaulted = Read(typeof(DefaultedKey), "Id", b =>
        {
            b.HasSequence<int>("Numbers");
            b.Entity<DefaultedKey>().Property(e => e.Id).HasDefaultValueSql("NEXT VALUE FOR [Numbers]");
        });
        Assert.Equal(SqlServerValueGenerationStrategy.None, defaulted.Strategy);
        Assert.False(defaulted.IsIdentity, defaulted.ToString());

        Assert.True(Read(typeof(PropertyIdentityKey), "Id", b => b.Entity<PropertyIdentityKey>().Property(e => e.Id).UseIdentityColumn()).IsIdentity);
    }

    /// <summary>
    /// A Guid key is generated on the client - by the convention and by the attribute alike -
    /// and so is a string key under the attribute: each gets a GUID when it is added, and the
    /// database has nothing for it.
    /// </summary>
    [Fact]
    public void AGuidOrAStringKeyIsAGuidTheClientGenerates()
    {
        foreach (var entity in new[] { typeof(GuidKey), typeof(AttributedGuidKey) })
        {
            var key = Read(entity, "Id", add: true);
            Assert.Equal(ValueGenerated.OnAdd, key.Generated);
            Assert.False(key.IsIdentity, key.ToString());
            Assert.False(key.Temporary, key.ToString());
            Assert.True(key.Added is Guid guid && guid != Guid.Empty, key.ToString());
        }

        var text = Read(typeof(AttributedStringKey), "Id", add: true);
        Assert.Equal(ValueGenerated.OnAdd, text.Generated);
        Assert.False(text.IsIdentity, text.ToString());
        Assert.False(text.Temporary, text.ToString());
        Assert.True(text.Added is string value && Guid.TryParse(value, out _), text.ToString());

        Assert.Equal(ValueGenerated.Never, Read(typeof(StringKey), "Id").Generated);
    }

    /// <summary>
    /// Computed on a key is generation on add and on update, and a key whose value may change
    /// once stored is a model EF Core refuses: no valid source carries it.
    /// </summary>
    [Fact]
    public void ComputedOnAKeyIsAModelEFCoreRefuses()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Read(typeof(ComputedKey), "Id"));

        Assert.Contains("'ValueGeneratedOnAddOrUpdate' because it's part of a key", refusal.Message);
    }

    /// <summary>
    /// The key of the dependent of a one-to-one that is also its foreign key takes the
    /// principal's value: the convention generates nothing for it, and even the attribute
    /// makes no IDENTITY column of it.
    /// </summary>
    [Fact]
    public void AKeyThatIsAForeignKeyIsNoIdentityColumn()
    {
        var bare = Read(typeof(Passport), nameof(Passport.PersonId), others: typeof(Person));
        Assert.Equal(ValueGenerated.Never, bare.Generated);
        Assert.False(bare.IsIdentity, bare.ToString());

        var attributed = Read(typeof(AttributedPassport), nameof(AttributedPassport.PersonId), others: typeof(Person));
        Assert.False(attributed.IsIdentity, attributed.ToString());
    }

    // ---- the probe --------------------------------------------------------------------------

    /// <summary>One key property as the model and the script have it, and what adding an instance does to it.</summary>
    private sealed record Reading(ValueGenerated Generated, SqlServerValueGenerationStrategy Strategy, string Column, string Script, object? Added, bool Temporary)
    {
        /// <summary>The provider's own judgement and the script's: both say IDENTITY, or neither.</summary>
        public bool IsIdentity
        {
            get
            {
                var scripted = Regex.IsMatch(Column, @"\bIDENTITY\b");
                Assert.Equal(Strategy == SqlServerValueGenerationStrategy.IdentityColumn, scripted);
                return scripted;
            }
        }

        public override string ToString() => $"{Generated}, {Strategy}, column '{Column}', added {Added ?? "null"}{(Temporary ? " (temporary)" : string.Empty)}";
    }

    private static Reading Read(Type entity, string property, Action<ModelBuilder>? configure = null, bool add = false, params Type[] others)
    {
        using var context = new ProbeContext(entity.Name, [entity, .. others], configure);

        var model = context.GetService<IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(entity)!;
        var key = entityType.FindProperty(property)!;
        var script = context.Database.GenerateCreateScript();

        // Adding an instance runs the client's value generator; only where that generator is the fact,
        // because a hi/lo generator would open a connection to take its next block.
        var entry = add ? context.Add(Activator.CreateInstance(entity)!).Property(property) : null;

        return new Reading(
            key.ValueGenerated,
            key.GetValueGenerationStrategy(),
            ColumnOf(script, entityType.GetTableName()!, key.GetColumnName()),
            script,
            entry?.CurrentValue,
            entry?.IsTemporary ?? false);
    }

    /// <summary>The line of the create script that defines the column, without its trailing comma.</summary>
    private static string ColumnOf(string script, string table, string column)
    {
        var start = script.IndexOf($"CREATE TABLE [{table}] (", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the script has no table {table}:{Environment.NewLine}{script}");

        var end = script.IndexOf(");", start, StringComparison.Ordinal);
        var line = script[start..end]
            .Split('\n')
            .Select(l => l.Trim().TrimEnd(','))
            .FirstOrDefault(l => l.StartsWith($"[{column}] ", StringComparison.Ordinal));

        return line ?? throw new InvalidOperationException($"the table {table} has no column {column}:{Environment.NewLine}{script}");
    }

    /// <summary>A context over the scenario's entities and configuration, never connected; the model is cached per scenario.</summary>
    private sealed class ProbeContext(string scenario, Type[] entities, Action<ModelBuilder>? configure) : DbContext
    {
        public string Scenario { get; } = scenario;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
            => options
                .UseSqlServer("Server=(local);Database=Probe;Trusted_Connection=True;TrustServerCertificate=True")
                .ReplaceService<IModelCacheKeyFactory, ScenarioModelCacheKeyFactory>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entity in entities)
            {
                modelBuilder.Entity(entity);
            }

            configure?.Invoke(modelBuilder);
        }
    }

    private sealed class ScenarioModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (((ProbeContext)context).Scenario, designTime);
    }

    // ---- the entities, one table each ------------------------------------------------------

    [Table(nameof(IntKey))] public class IntKey { public int Id { get; set; } }
    [Table(nameof(LongKey))] public class LongKey { public long Id { get; set; } }
    [Table(nameof(ShortKey))] public class ShortKey { public short Id { get; set; } }
    [Table(nameof(ByteKey))] public class ByteKey { public byte Id { get; set; } }
    [Table(nameof(SByteKey))] public class SByteKey { public sbyte Id { get; set; } }
    [Table(nameof(UShortKey))] public class UShortKey { public ushort Id { get; set; } }
    [Table(nameof(UIntKey))] public class UIntKey { public uint Id { get; set; } }
    [Table(nameof(ULongKey))] public class ULongKey { public ulong Id { get; set; } }

    [Table(nameof(AttributedIntKey))]
    public class AttributedIntKey { [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int Id { get; set; } }

    [Table(nameof(CompositeKey))]
    [PrimaryKey(nameof(A), nameof(B))]
    public class CompositeKey
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int A { get; set; }
        public int B { get; set; }
    }

    [Table(nameof(AttributedByteKey))]
    public class AttributedByteKey { [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public byte Id { get; set; } }

    [Table(nameof(AttributedDecimalKey))]
    public class AttributedDecimalKey { [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public decimal Id { get; set; } }

    [Table(nameof(HiLoKey))]
    public class HiLoKey { [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int Id { get; set; } }

    [Table(nameof(HiLoConventionKey))] public class HiLoConventionKey { public int Id { get; set; } }

    [Table(nameof(KeySequenceKey))]
    public class KeySequenceKey { [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int Id { get; set; } }

    [Table(nameof(PropertyHiLoKey))] public class PropertyHiLoKey { public int Id { get; set; } }
    [Table(nameof(PropertySequenceKey))] public class PropertySequenceKey { public int Id { get; set; } }
    [Table(nameof(DefaultedKey))] public class DefaultedKey { public int Id { get; set; } }
    [Table(nameof(PropertyIdentityKey))] public class PropertyIdentityKey { public int Id { get; set; } }

    [Table(nameof(GuidKey))] public class GuidKey { public Guid Id { get; set; } }

    [Table(nameof(AttributedGuidKey))]
    public class AttributedGuidKey { [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public Guid Id { get; set; } }

    [Table(nameof(StringKey))] public class StringKey { public string Id { get; set; } = null!; }

    [Table(nameof(AttributedStringKey))]
    public class AttributedStringKey { [DatabaseGenerated(DatabaseGeneratedOption.Identity)] public string Id { get; set; } = null!; }

    [Table(nameof(ComputedKey))]
    public class ComputedKey { [DatabaseGenerated(DatabaseGeneratedOption.Computed)] public int Id { get; set; } }

    [Table(nameof(Person))] public class Person { public int Id { get; set; } }

    [Table(nameof(Passport))]
    public class Passport
    {
        [Key, ForeignKey(nameof(Person))] public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
    }

    [Table(nameof(AttributedPassport))]
    public class AttributedPassport
    {
        [Key, ForeignKey(nameof(Person)), DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int PersonId { get; set; }
        public Person Person { get; set; } = null!;
    }
}
